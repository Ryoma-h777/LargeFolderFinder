# Research & Design Decisions

## Summary
- **Feature**: `scan-correctness`
- **Discovery Scope**: Extension（既存の走査処理と描画処理への統合を伴う改修）
- **Key Findings**:
  - 本スキャンの長いパスの欠落は .NET 10 移行で解消済み（dotnet10-migration 2.6）。残るのは**事前カウントの P/Invoke** で、実行ファイルが long path aware でないため 260 文字を超える配下を数え損ね、進捗率の分母がずれる
  - 走査中の暫定ツリーの読み取りは、既に `ResultFormatter` が子の一覧を `lock (node.Children)` の下で写し取っており、並べ替えもキーを一度だけ評価する LINQ の `OrderBy` で安全。**本当の穴は描画の側**にある。描画は `CancellationToken.None` で呼ばれて取り消しが効かず、走査中は5秒ごとに投げっぱなし（`_ = RenderResult()`）で重なって走る。古い描画が最終結果の描画より後に終わると画面を古い状態で上書きしうる。また投げっぱなしのため描画中の例外は誰にも拾われず消える
  - 本体の `catch` は63件。A（意図して無視してよい）10、B（ログに残すべき）4、C（走査のスキップとして記録）2、D（既に適切）46、E（利用者に知らせる）1
  - `Config.Instance` の最初の参照は、タブの見出しを描く XAML のバインディング（`SessionData.IsOldData`）経由という偶発的な経路で起きる。走査のたびに `SessionViewModel.ScanAsync` が `Config.Load()` を別途呼び直している

## Research Log

### 事前カウントと本スキャンの数える対象の集合
- **Context**: 要件1.4（完了時に事前カウントの数と走査で数えた数が一致する）を満たすには、両者が数える集合を厳密にそろえる必要がある
- **Findings**:
  - 事前カウント（`CountFoldersRecursive`）: 各フォルダを1と数え、`currentDepth >= maxDepth` でそれ以上潜らない。`FindFirstFileEx` が失敗すると（アクセス拒否・長いパス）そのフォルダは数えるが配下は数えない。`.`・`..` とリパースポイントを除く。隠し・システム属性は含む
  - 本スキャン（`ScanRecursiveInternal`）: `dir.EnumerateDirectories()`（.NET の互換の既定＝隠し・システムを含む）からリパースポイントを除いて潜る。列挙の例外は `catch { }` で捨て、そのフォルダ自身は数える。`currentDepth <= maxDepth` のときに進捗の数を1増やす
  - 両者の対象は「深さ0〜maxDepth のフォルダ、リパースポイントを除く、隠し・システムを含む、アクセス拒否のフォルダ自身は数え配下は数えない」で一致する。**違いは長いパスの配下だけ**である
- **Implications**: 事前カウントを BCL の列挙に置き換える際は、`EnumerationOptions` を `AttributesToSkip = ReparsePoint`、`IgnoreInaccessible = true`、`ReturnSpecialDirectories = false`、`RecurseSubdirectories = false` にし、深さは自前の再帰で数える。既定の `AttributesToSkip`（隠し・システムを除く）を使うと集合がずれる

### 進捗の最後の報告
- **Findings**: `ReportProgress` は最初の1件と、以降5秒ごとにしか呼ばれない。走査の完了時に最後の数が報告される保証がない
- **Implications**: 要件1.4 を検証するには完了時の数が要る。完了時に1回だけ最後の報告を送る。これは通知の間隔を短くするものではない（要件3.4）

### 長いパスの配下で事前カウントが失敗することの再現
- **Findings**: 検証用のフィクスチャは `%TEMP%\gb_fix_<8桁>` 配下に1階層50文字×5階層の連鎖（ASCII と日本語）を作る。基準が約60文字のため、4階層目の配下（約264文字）で `FindFirstFileEx` が失敗する。既定の `MaxDepthForCount`（3）では境界に届かないため、検証では深さの上限を6にして数える
- **Implications**: 置き換え前は数え損ね（RED）、置き換え後はフィクスチャの定義から導いた数と一致する（GREEN）ことを確かめられる

### 残す P/Invoke と除く P/Invoke
| 宣言 | 用途 | 扱い | 理由 |
|---|---|---|---|
| `FindFirstFileEx` / `FindNextFile` / `FindClose`（と `WIN32_FIND_DATA`・関連の定数と列挙） | 事前カウント | 除く | パスを列挙するため長いパスの制約を受ける（要件2.1） |
| `ShowWindow` | なし | 除く | 宣言のみで未使用（要件2.2） |
| `GetCompressedFileSize` | なし | 除く | 宣言のみで未使用（要件2.2） |
| `GetDiskFreeSpace` | 物理サイズ換算のクラスタサイズ | 残す | 渡すのはドライブや共有のルート（`C:\`、`\server\share\`）だけで長いパスにならない。BCL にクラスタサイズを得る API が無い。検証ツールの `ScanRunner.MeasureClusterSize` が同じ式を持つので、変えると乖離する（要件2.4） |
| `SetProcessWorkingSetSize` | メモリの切り詰め | 残す | パスを扱わない。速度・メモリの判断は `scan-performance` |

### 失敗の扱いの監査（63件）
- 分類の内訳は Summary のとおり。ファイル別は `MainWindow.xaml.cs` 27、`SessionFileManager.cs` 5、`MainViewModel.cs` 5、`TextViewer.xaml.cs` 5、`LocalizationManager.cs` 4、`Logger.cs` 4、`SessionViewModel.cs` 4、`Scanner.cs` 3、`Config.cs` 2、`AppSettings.cs` 2、`TreeFilter.cs` 1、`LayoutViewBase.cs` 1
- A（10件）: `Logger` 自身の4件（ログの失敗をログに書くと循環する）、`Scanner.GetClusterSize`（0を返して換算しない＝既定の動作）、`TreeFilter` の正規表現の検証（入力途中の不正な式）、`MainWindow.OptimizeMemory`、所有者の表示の内部、`LocalizationManager` のカルチャの表示名、`TextViewer` の監視のタイマー
- B（4件）: `Config.Save`、ログフォルダを開くメニュー、`TextViewer.ReloadFile`、`TextViewer` の初回の読み込み
- C（2件）: `Scanner.cs` のファイル列挙とフォルダ列挙。`UnauthorizedAccessException` のほか `DirectoryNotFoundException`（走査中に消えた）、`IOException`（ネットワークの切断など）が来うる
- E（1件）: `Config.Load`
- 監査で分類されなかったが、**投げっぱなしの描画（`_ = RenderResult()`）の中の例外**も黙って捨てられている（タスクの戻り値を誰も待たない）

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations |
|---|---|---|---|
| 暫定ツリーを報告の時点で丸ごと複製する | 走査側が5秒ごとに木全体の写しを作って渡す | 読み手が完全に独立する | 100万ノード級で5秒ごとに全複製は速度の最優先に反する |
| 既存の「子の一覧をロックの下で写し取る」規約を明文化し、全読み手に適用する（採用） | 走査中に木を読む処理は必ず `lock (node.Children)` の下で一覧を写す | 既に `ResultFormatter` が実装済みで追加の費用がない | 規約を破る読み手を増やさない注意が要る。試験で担保する |
| 描画をタブごとに最新の1件だけ有効にする（採用） | 新しい描画を始めるとき同じタブの前の描画を取り消し、取り消された描画は画面に反映しない | 古い描画による上書きを防ぎ、完了時の最終結果の表示を保証する | 取り消しの通知を描画の各段に渡す必要がある |

## Design Decisions

### Decision: 事前カウントは BCL の列挙に置き換え、深さは自前の再帰で数える
- **Alternatives**: (1) `app.manifest` で `longPathAware` を宣言し P/Invoke を残す (2) `EnumerationOptions.RecurseSubdirectories = true` で一括列挙し深さで絞る (3) 自前の再帰で1階層ずつ BCL 列挙（採用）
- **Rationale**: (1) は Windows のレジストリ `LongPathsEnabled` にも依存し要件2.1 に反する。(2) は深さの上限より下まで列挙してしまい、事前カウントを浅く抑える目的（decisions.md「進捗率は事前カウントした範囲で数える」）に反して遅くなる。(3) は既存の深さの意味をそのまま保てる
- **Trade-offs**: 速度の作り込みはしない（`scan-performance`）

### Decision: 走査のスキップは集めて、完了時に1回でまとめてログに書く
- **Rationale**: スキップは NAS で数千件になりうる。1件ごとに `Logger.Log`（ファイルへの追記とロック）を呼ぶと走査を遅くする。並行の集合に集め、完了時（取り消し・中断を含む）に件数と全件を1回で書く。種類（アクセス拒否・消えた・入出力の失敗）を区別して記録する
- **Trade-offs**: 走査中に異常終了するとスキップの記録が残らない。中断の経路でも書き出すことで緩和する

### Decision: 意図して無視する失敗は、決まった書式のコメントで理由を残す
- **Rationale**: A の10件は「捨ててよい」理由がコードから読めることが保守上重要。`// 意図して無視: <理由>` の書式にそろえ、機械的に数えられるようにする（要件5.1 の検証）
- **Trade-offs**: 書式の順守はレビューと検索で確かめる

### Decision: `Config.txt` の解析の失敗は、起動時と走査開始時に知らせる
- **Rationale**: 最初の読み込みはバインディング経由で偶発的に起きるため、そこで画面に出すと時点が不定になる。起動の初期化の後と、走査のたびの再読み込みの後という明示的な2箇所で失敗を確かめ、既定の設定で動いていることをダイアログで知らせる（decisions.md「起動時の例外は、内容を画面に出す」と整合）。同じ失敗を繰り返し知らせないよう、内容が同じ間は一度だけにする
- **Trade-offs**: 新しい訳文のキーが1つ増える。`localization-completeness` の網羅の検証（13言語）を通す

### Decision: 検証は既存の走査の検証ツール（GoldenBaseline）の自己検証に加える
- **Rationale**: テストプロジェクトはアプリのアセンブリを参照しない方針（dotnet10-migration の要件6.5）。アプリの部品を直接呼べる既存の仕組みは GoldenBaseline だけで、その自己検証はテストから実行される。事前カウント・走査と描画の読み取りの並行・描画の取り消しの判定を自己検証に加える。本体に触れる面は既存の規約どおり `Scan/` 層に置く
- **Trade-offs**: 「走査結果の検証ツール」に描画の取り消しの判定が入る。本格的な単体試験の置き場は `architecture-refactoring` で見直す（Revalidation Trigger）

## Risks & Mitigations
- 事前カウントの集合が本スキャンとずれる → 列挙の選択肢を明示し、フィクスチャで数の一致を試験する
- 取り消しの通知を渡し忘れた段が残り、取り消された描画が画面に反映される → 画面へ反映する直前に、その描画がまだ最新かを確かめる
- A の理由のコメントが形骸化する → 分類表を research に残し、タスクでは箇所ごとに扱いを決める
- 例外の扱いの変更で集計値が変わる → 期待値データとの比較（物理サイズ換算の有無の両方）を各段で行う

## References
- dotnet10-migration の research.md「2.6 走査結果の差の確認と期待値データの更新」「5.4 移行全体の確認」
- `.kiro/steering/decisions.md`「描画の取り消しを、タブ間で干渉させない」「進捗率は、事前カウントした範囲で数える」「事前カウントを省いたときは、残り時間を出さない」「起動時の例外は、内容を画面に出す」
- [EnumerationOptions](https://learn.microsoft.com/dotnet/api/system.io.enumerationoptions) — `AttributesToSkip` の既定は隠し・システムを除くため、明示が必要
