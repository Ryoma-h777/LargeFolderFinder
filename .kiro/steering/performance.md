# パフォーマンス方針

このプロジェクトでは **速度は機能そのもの**です（product.md の優先順位 1）。ここでは走査性能に関する実装パターン、計測手順、判断基準をまとめます。

## 処理の全体像

スキャンは 2 段構えです。どちらも BCL の列挙を使います（事前カウントは `scan-correctness` で Win32 API から置き換えた。長いパスを数え損ねていたため。tech.md の「Win32 API 直接呼び出し」）。

| 段階 | 実装 | 列挙方式 | 目的 |
|---|---|---|---|
| ① 事前カウント | `Scanner.CountFoldersAsync` → `FolderCounter` | **`DirectoryInfo.EnumerateDirectories`**（`EnumerationOptions` を明示） | 進捗率の分母を得る |
| ② 本スキャン | `Scanner.RunScan` → `ScanRecursiveInternal` | **`DirectoryInfo.EnumerateFiles` / `EnumerateDirectories`** | サイズ集計とツリー構築 |

### ① 事前カウント
- 1階層ずつ自前の再帰で列挙し、`Config.MaxDepthForCount`（既定 3）で**深さを打ち切る**。全階層を数えると事前カウント自体が本スキャン並みの時間になるため（`RecurseSubdirectories = true` の一括列挙は上限より下まで潜るので使わない）
- 本スキャンと同じ集合を数えるため、`AttributesToSkip = ReparsePoint`（既定の隠し・システムの除外を外す）、`IgnoreInaccessible = true` を指定する
- 速度の作り込み（Win32 時代の `FIND_FIRST_EX_LARGE_FETCH` 相当など）はしていない。置き換えの前後の速度は計測していない（`scan-performance` の範囲）
- `Config.SkipFolderCount = true` でこの段階を丸ごと省略できる（起動は速いが進捗率が出ない）

### ② 本スキャン
- ファイルは `EnumerateFiles`、サブディレクトリは `EnumerateDirectories` で列挙
- **リパースポイント（`FileAttributes.ReparsePoint`）は必ず除外**する。シンボリックリンク経由の無限再帰を防ぐため
- 列挙の失敗は、アクセス拒否・見つからない・入出力の失敗に限って捕まえ、そのフォルダの残りを飛ばして走査を継続する。1 フォルダーの権限不足で全体を止めない。スキップは `ScanSkipRecorder`（並行の集合）に集め、走査の終わりに1回だけログに書く（1件ごとにログを書くと走査が遅くなるため）

## 並列処理のパターン

`Config.UseParallelScan`（既定 true）で切り替えます。

```csharp
if (useParallel)
{
    Parallel.ForEach(directories, new ParallelOptions { CancellationToken = token }, subDir => { ... 再帰 ... });
}
else
{
    foreach (var subDir in directories) { ... 再帰 ... }
}
```

**方針と制約**:

- 並列度は明示指定せず **スレッドプールに委ねる**（`MaxDegreeOfParallelism` は設定していない）。再帰の各階層で `Parallel.ForEach` が入れ子になるため、階層が深いと並列度が読みにくい。**並列度を触る変更は、必ず実測とセットで行うこと**
- `CancellationToken` は `ParallelOptions` に渡し、再帰の入口で `ThrowIfCancellationRequested()` する
- **共有状態の保護は 2 通りを使い分ける**
  - 子ノードのリスト追加 → `lock (currentNode.Children)`
  - サイズの加算 → `Interlocked.Add`（`FolderInfo.AddSize` が親へ波及させる）
- 逐次実行は、HDD のシークの詰まりを避けるために設けた経路で、「並列が原因の不具合か」を切り分けるための退避経路でもある。消さないこと

**低速なネットワーク越し（NAS）では並列が効きます**が、ローカル SSD では過剰なスレッドが逆効果になり得ます。HDD ベースの NAS などでシークの詰まりが疑われる場合は、上の逐次実行を試す余地があります。既定値を変える判断は実測で行ってください。

## 進捗通知のスロットリング

進捗を出しすぎると UI スレッドを詰まらせ、走査そのものが遅くなります。

- **`IProgress<ScanProgress>` の報告は 5 秒間隔**（初回のみ即時）。二重チェックロック（`lock (progressCounter)` の前後で条件判定）で多重報告を抑止
- **ログ出力は 20 秒間隔**（`Logger`）
- 残り時間は **指数移動平均（`Alpha = 0.1`）** で、直近の速度を重視して推定する。スキャンの進み具合によって速度が変わり、開始時からの平均速度では予測が不安定になるため
- 進捗には暫定ツリー（`ProgressCounter.RootNode`）を載せ、走査中でも途中結果を見せる。暫定ツリーを読む処理は、子の一覧を `lock (node.Children)` の下で写し取ってから使う（`ResultFormatter` の規約。自己検証で並行に読んで確かめている）
- 走査が正常に完了したときだけ、間隔とは別に最後の報告（`IsFinal = true`、最後の数とスキップの一覧、暫定ツリーは載せない）を1回送る

**新しい進捗表示を足す場合も、この 5 秒 / 20 秒の枠内に相乗りさせてください。**独自の高頻度通知を追加しないこと。

## 表示側のパフォーマンス

結果は数十万行に達し得ます。

- **出力リストの仮想化は必須**: `VirtualizingStackPanel.IsVirtualizing="True"` + `VirtualizationMode="Recycling"`。両レイアウトの XAML に設定済み。**外さないこと**
- **フィルタ入力は 300ms デバウンス**（`_filterDebounceTimer`）。1 文字ごとの再描画を避ける
- 整形処理（`ResultFormatter`）も `Parallel.ForEach` + `Partitioner.Create` で分割している
- 表示条件の変更（フィルタ・ソート・単位・折りたたみ）で**再スキャンを走らせない**。保存済みツリーに対して適用する

## メモリ

- `OptimizeMemory()` が `GC.Collect()` + `GC.WaitForPendingFinalizers()` + `Win32.SetProcessWorkingSetSize(-1, -1)` を実行する
- 呼び出しタイミング: 初回描画完了時（`ContentRendered`）と、`DispatcherTimer` による**1 分間隔**のアイドル時
- 常駐アプリとして待機中のフットプリントを小さく見せる意図。**走査中の性能改善が目的ではない**ので、走査ループ内から呼ばないこと
- 永続化は MessagePack + LZ4BlockArray 圧縮でセッション復元を速くしている（圧縮によるデータ量の削減は、コード中のコメントでは 50〜70% とされるが、計測の記録はない）

## 計測の手順

自動テストが無いため、**速度に影響する変更は必ず実測で確認**します。

1. **Release ビルド**で計測する（`dotnet build LargeFolderFinder.sln -c Release -warnaserror`）
2. 対象と条件を固定する。最低でも「ローカルドライブ」「ネットワークドライブ」の 2 系統
3. **計測の道具 `Tools/ScanBench` を使う**（画面を介さずに走査だけを繰り返し測る）。画面込みの所要時間を見たいときは、アプリの表示と `Logger` の `LogScanSuccess` を使う
4. **OS のファイルキャッシュの影響を排除する**。同一パスの 2 回目以降は大幅に速くなるため、初回計測値と 2 回目以降を区別して記録する
5. 変更前後を同一条件で比較する。絶対値ではなく差分で判断する。**判断は複数回の中央値で行う**（背景の負荷で1回だけ大きく外れることがある）
6. `Config.txt` の設定（`UseParallelScan` / `ScanThreads` / `SkipFolderCount` / `UsePhysicalSize`）を記録に残す
7. 記録は `.kiro/specs/scan-performance/measurements.md` に、環境・対象のラベル・条件・各回の値・中央値・要約値の形で残す。**利用者固有の絶対パス・利用者名・NAS の名前は書かない**

### 計測の道具（`Tools/ScanBench`）の使い方

```
Tools/ScanBench/bin/Release/net10.0-windows/ScanBench.exe <対象のフォルダ> [--runs N] [--threads N] [--buffer BYTES] [--sequential] [--physical-size] [--label 対象の説明] [--no-digest]
```

- 出力はタブ区切りで、条件の行（`#` で始まる）→ 見出し → 1回ごとの行。**パスは出さない**ので、そのまま記録に貼れる
- `--threads N` で並列度を、`--buffer BYTES` で列挙のバッファの大きさを固定して試せる（いずれも 0 は既定で、0 のときは本体が決める）。既定値を決めるための振り分けに使う
- 並列度の列（**ワーカー数**・**同時の列挙の最大**・**バッファ**）で、その回がどの並列度で走ったかを確かめる。
  ワーカー数と同時の列挙の最大は走査の最後の報告から取った実際の値で、バッファの「既定」は .NET の既定の大きさである。
  条件の行の「並列度: 自動」は、本体が対象（ネットワークかローカルか）から既定値を決めたことを表す
- 同じプロセスで `--runs` の回数だけ繰り返し、1回目を「初回」、2回目以降を「温まった」として区別して出す
- **要約値**（全ノードの相対パス・種別・サイズから作る値）で、走査の結果が変わっていないことを確かめる。並列と逐次、変更の前と後で同じ対象の要約値が一致すれば、集計値は変わっていない
  - ただし**動いているシステムのドライブでは、走査のたびに中身が変わるので要約値は一致しない**。その場合はフォルダ数・ファイル数・スキップ数の概数で見る
- **メモリを比べる回は `--no-digest --runs 1`** にする。要約値の一覧はメモリを押し上げ（45万ファイルで約 148MB）、最大の作業セットはプロセスの開始からの最大でリセットできないため、回をまたぐと増えてしまう
- 変更の前の値と比べるには、基準のコミットで道具をビルドしたものを使う（基準のコミットは measurements.md に書く）

### WizTree との比較（2026-09-22 利用者の決定で目標に加えた）

このアプリの存在意義のため、**WizTree と同等以上**を目標とします（場面ごとの目標は roadmap.md の「速度の目標」）。

- WizTree は、ローカルの NTFS を管理者として走査するときだけ MFT を直接読んで非常に速い。それ以外（NAS・NTFS 以外・管理者でない）は通常の列挙になる。**比較は必ず場面を分けて記録する**
- 同じ対象を、同じ条件（管理者か否か、初回か2回目以降か）で両方走査し、所要時間を並べる。WizTree の所要時間は画面の表示を使う
- WizTree は利用者の PC に入れて測る。配布物には同梱しない
- 記録の形: 対象（種類・容量・ファイル数・接続方式）、条件、WizTree の時間、このアプリの時間、このアプリの版

### 基準値（README 公開値）

回帰を判定する際の目安です。これを明確に下回る場合は変更を見直します。

| 対象 | 規模 | 所要時間 |
|---|---|---|
| PC ローカル | 約 400GB / 約 117 万ファイル | 5 〜 13 秒 |
| NAS | 約 1TB / 約 7 万ファイル | 23 秒 |
| NAS | 約 20TB / 約 140 万ファイル | 約 18 〜 30 分 |

**README の実測値を更新する場合は、計測環境（CPU・接続方式）も併記してください。**低スペック機での計測値を無条件に置き換えると、性能が退化したように見えます。

## 変更時のチェックリスト

- [ ] 列挙処理にリパースポイントの除外を入れたか
- [ ] `CancellationToken` を末端まで引き回したか
- [ ] 共有状態を `lock` または `Interlocked` で保護したか
- [ ] 進捗通知を新たに高頻度で発火させていないか
- [ ] 仮想化設定を壊していないか
- [ ] Release ビルドで、ローカルとネットワークの両方を実測したか

## 既知の注意点

- `AppConstants.MemoryOptimizeIntervalMinutes`（5）は**どこからも参照されていません**。実際のタイマーは `MainWindow.InitializeMemoryTimer()` で 1 分固定です。間隔を変える際は定数側ではなくタイマー実装を見てください
- `Scanner.PruneTree` は閾値未満の枝を刈るメソッドですが、**現在は呼び出されていません**（全ノードを保持し、表示時にフィルタする方針に変更されたため）。メモリ使用量とのトレードオフを再検討する際の起点になります

---
_パターンと基準を記述する。計測結果そのものは README と各セッションのログに残す_
