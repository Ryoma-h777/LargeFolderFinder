# Research & Design Decisions

## Summary
- **Feature**: `scan-performance`
- **Discovery Scope**: Extension（既存の走査の処理の作り替え。外部の依存は増やさない）
- **Key Findings**:
  - brief の最大の疑い「ファイルごとに祖先へ `Interlocked.Add`」は事実と違う。`AddSize` はフォルダごとに1回（祖先の数だけの Interlocked）。ファイルごとに行っているのは、`FileInfo` と完全パスの文字列の確保、`FolderInfo` の確保、`lock (Children)` の3つ
  - 並列化は全階層で入れ子の `Parallel.ForEach`（上限なし）。Microsoft は入れ子の並列化と、ブロックする I/O での上限なしの並列化を避けるよう明記している。SMB の列挙は往復の遅延が支配するため、同時に問い合わせる数（上限つき）が速さを決める
  - 相手の WizTree も MFT を読めない場面で既にマルチスレッド化している（v4.21 で SSD 4倍以上、v4.28 でネットワークなど最大2倍）。勝つには列挙そのものの無駄を削り、並列度を場面に合わせる必要がある

## Research Log

### 現状の走査のコード（2026-09-22、scan-correctness の完了後）
- **Findings**:
  - `ScanRecursiveInternal`（Services/Scanner.cs）はフォルダごとに `EnumerateFiles()` と `EnumerateDirectories()` の**2回**列挙する。ファイルごとに `FileInfo`（完全パスの文字列を含む）と `FolderInfo` を確保し、`lock (currentNode.Children)` で1件ずつ足す
  - `AddSize` はフォルダごとに1回で、祖先の数だけ `Interlocked.Add`。ルートのノードには全フォルダから加算が集まる
  - 進捗の判定でフォルダごとに `DateTime.Now` を呼ぶ
  - 走査中は5秒ごとに途中の木全体を画面が描き直す（`SessionViewModel` の進捗の受け手 → `RenderResult`）。描き直しは走査と CPU を取り合う
  - `FolderInfo` の保存形式は MessagePack の `[Key(0..6)]`（Name, IsFile, LastModified, Size, Children, IsExpanded, Owner）。`Parent` は保存しない。形式の版の欄は無い。読めないときは例外を捕まえて null を返し、ログに記録する（`SessionFileManager.Load`）
  - 描画・コピー・検証ツールは `Children` を下へ辿るだけで `Parent` を使わない。`Parent` を使うのは `AddSize`、`GetFullPath`（所有者の表示・項目を開く）、`RestoreParentReferences`（読み込み時）
  - 途中の木を読む側（`ResultFormatter` の4箇所、検証ツールの並行読み取りの自己検証）は `lock (node.Children)` の下で写し取る規約に依存する
  - 計時・ベンチマークのコードは無い（`Stopwatch` は画面の所要時間の表示だけ）
- **Implications**: 保存形式と `FolderInfo` の公開の形を変えずに、列挙と確保とロックの回数を減らす余地が大きい。形式を変えなければ要件7 は既存の動きで満たせ、版の番号を上げる必要も無い

### `FileSystemEnumerable<T>`
- **Sources**: dotnet/runtime の FileSystemEnumerator.Windows.cs・FileSystemEntry.Windows.cs・EnumerationOptions.cs、corefx#25426、learn.microsoft.com の EnumerationOptions.BufferSize
- **Findings**:
  - Windows では `NtQueryDirectoryFile`（FILE_FULL_DIR_INFORMATION）の結果の構造体から `Length`・`Attributes`・`LastWriteTimeUtc` を読むだけで、追加のシステムコールも確保も無い。`FileName` は `ReadOnlySpan<char>`
  - `DirectoryInfo.EnumerateFiles` + `FileInfo.Length` は1件ごとに `FileInfo` と完全パスの文字列を作る
  - `new EnumerationOptions()` の既定は `AttributesToSkip = Hidden | System`。**集計ではずれるので 0（または ReparsePoint のみを再帰で除く判定）を明示する**
  - `BufferSize` の既定は 4096。公式は「リモート共有では大きなバッファが有用（16K）」とするが、WinDirStat はリモートで小さいバッファで速くなったと記録しており、実測で決める
  - 内蔵の再帰は単一スレッドの幅優先。並列化するなら1フォルダ単位で列挙し、再帰は自前で行う
  - dotnet/runtime#108381: Synology NAS（約120万ファイル）で約5分後に `IOException`（予期しないネットワークエラー）。未解決。自前の再帰では成功した報告あり
- **Implications**: 1フォルダを**1回の列挙**でファイルとフォルダの両方を得る。ファイルは `FileInfo` を作らず `Length` と更新日時を構造体から読む

### SMB 越しの列挙と並列度
- **Sources**: MS-SMB2 QUERY_DIRECTORY、Windows Server のファイルサーバの性能調整、JAM Software（TreeSize）、Robocopy /MT の測定記事、NetApp KB
- **Findings**:
  - 1フォルダの列挙に CREATE・QUERY_DIRECTORY・CLOSE の往復が要り、**遅延が支配する**。帯域はほぼ効かない。同時に複数のフォルダを問い合わせることが本質的な高速化になる
  - TreeSize はネットワークで既定2スレッド（Professional は最大32、CPU 負荷で自動調整）。Robocopy /MT は既定8
  - 初期値の目安は SMB で 8〜16、ローカルの SSD で CPU 数程度（推論。実測で決める）
- **Implications**: 並列度はネットワークとローカルで別の既定値を持ち、`Config.txt` で上書きできるようにする

### 入れ子の並列化とスレッドプール
- **Sources**: learn.microsoft.com「Potential Pitfalls in Data and Task Parallelism」「ParallelOptions.MaxDegreeOfParallelism」「Debug threadpool starvation」
- **Findings**: 入れ子の `Parallel.ForEach` は過剰な並列化の典型。`MaxDegreeOfParallelism = -1` は上限なしで、長くブロックする本体ではスレッドの注入が続く。ブロックする I/O は専用のスレッド（または LongRunning）で行うのがよい
- **Implications**: 決まった数の専用ワーカーが共有の作業の列からフォルダを取り出して列挙する形にする。深さや広さに関わらず同時の列挙の数は上限以内になる（要件3.1）

### WizTree の MFT を使わない場面
- **Sources**: diskanalyzer.com の What's New、WizTree vs WinDirStat
- **Findings**: 非管理者・非 NTFS・ネットワークでは通常の列挙。v4.21（2024-10）で「SSD で4倍以上」、v4.28（2025-11）で「特定条件で最大2倍」。絶対値の公表は無い。スレッド数の設定は公表されていない
- **Implications**: 比較は利用者の PC で、同じ対象・同じ条件・複数回の中央値で行う

### 公平な計測
- **Sources**: Sysinternals RAMMap、LanmanWorkstation のキャッシュの設定の既定値
- **Findings**:
  - ローカルの冷えた状態は再起動の直後が最も確実。RAMMap の Empty Standby List は管理者が要る
  - SMB クライアントのキャッシュは既定 10秒程度（ディレクトリリースがあれば最長 600秒）。NAS 側のメタデータのキャッシュはクライアントから消せない
- **Implications**: 計測は「初回（再起動の直後）」と「2回目以降（温まった状態）」を分け、複数回の中央値で比べる。NAS は両ツールを交互に複数回走らせる

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|---|---|---|---|---|
| A. 現行の入れ子の並列化に上限だけ付ける | 各 `Parallel.ForEach` に `MaxDegreeOfParallelism` | 変更が最小 | 階層ごとに上限が掛け算になり、全体の上限にならない | 要件3.1 を満たせない |
| B. 決まった数のワーカーと共有の作業の列（採用） | フォルダを列に積み、N 本の専用スレッドが取り出して1回の列挙で処理 | 全体の同時の列挙の数が N 以下。SMB の往復を重ねられる。終わりの判定が単純（未処理の数） | 自前の終了判定と例外の伝え方を作る必要 | TreeSize・WinDirStat と同じ考え方 |
| C. `Parallel.ForEachAsync` と非同期の列挙 | 非同期で列挙 | スレッドを占有しない | .NET の列挙は同期の API のみ。非同期にしても中でブロックする | 採らない |
| D. 木を構造体の配列に作り替える | 親の番号・サイズの配列 | メモリと GC が大きく減る | 画面・保存・検証ツールまで波及し、保存形式が変わる | `architecture-refactoring` 以降の候補。本スペックでは採らない |

## Design Decisions

### Decision: 保存形式と `FolderInfo` の公開の形を変えない
- **Context**: 要件7（保存と復元）、要件1（集計値の保全）。`FolderInfo` は画面・保存・検証ツールが広く使う
- **Alternatives**: (1) 構造体の配列への作り替え（D） (2) 現状の形のまま、作り方だけを変える
- **Selected**: (2)。ノードはこれまでどおり `FolderInfo` で、ファイルのノードも作る（画面の「ファイルを含む」と保存に要る）
- **Rationale**: 速さの主因は列挙の回数・`FileInfo` の確保・入れ子の並列化・ファイルごとのロックで、ノードの形ではない（推論。基準の計測で確かめる）。形を変えないので保存の互換が保たれ、版の番号を上げずに済む
- **Trade-offs**: ノードごとのオブジェクトの負担は残る。数百万ファイルでのメモリの削減は限定的
- **Follow-up**: 基準の計測で GC の時間の割合が大きければ、`architecture-refactoring` への申し送りにする

### Decision: 決まった数の専用ワーカーによる走査（Option B）
- **Selected**: 走査の開始時に並列度 N を決め、N 本の専用スレッドが共有の作業の列からフォルダを取り出す。フォルダは1回の列挙でファイルとサブフォルダを得る。子のノードはフォルダの処理の中で局所の一覧に集め、`lock (Children)` は1フォルダにつき1回だけ取って一括で足す。フォルダのファイルの合計は従来どおり `AddSize` で祖先へ1回伝える（途中の木にサイズを出すため）
- **Rationale**: 要件3.1（深さ・広さに関わらない上限）、要件2（速さ）。途中の木を読む規約（`lock (Children)`）はそのまま守れる
- **Follow-up**: ルートへの `Interlocked` の集中が計測で目立つ場合は、途中の表示を合計だけにして最後に下から積み上げる方式を検討する（本スペックの中で、計測の結果として判断）→ 次の決定で結論を出した

### Decision: ルートのノードへの加算の集中は、別の方式に変えない（タスク 3.4 の計測の結果）
- **Context**: 要件2.1・2.3・6.1。`FolderInfo.AddSize` はフォルダの処理の終わりに1回呼ばれ、自分と全祖先へ
  `Interlocked.Add` する。ルートのノードには全フォルダからの加算が集まるため、ワーカーを増やすと
  同じキャッシュラインへの書き込みで詰まる疑いがあった（brief の最大の疑いの残り）
- **Alternatives**: (1) いまのまま（走査中に祖先へ伝える） (2) 走査中は各フォルダの合計だけを持ち、
  走査の終わりに下から積み上げる
- **Selected**: (1)。**別の方式には変えない**
- **Rationale**（計測の根拠は `measurements.md` の3章）:
  - 加算の回数を実データで数えた。「ファイル数の多いシステムのフォルダ」は 132,707 フォルダ・
    起点からの深さの合計 598,512（平均 4.51・最大 13）で、`Interlocked.Add` の総数は多くても
    598,512 + 132,707 ≒ 73 万回、そのうち**ルートに集まるのは多くても 132,707 回**（ファイルを持つフォルダごとに1回）
  - この対象の走査は 16 ワーカーで 2,371 ms。ルートへの加算が完全に直列化し、競合した書き込みを
    1回 200 ns と厳しく見ても 132,707 × 200 ns ≒ 27 ms で、所要時間の **約 1%** に収まる
  - 競合が効いていれば、競合の無い逐次（ワーカー1本、加算の回数は同じ）が相対的に有利になるはずだが、
    実測は 16 ワーカーが逐次の 2.34 倍速い。加算ではなく列挙の待ちが支配している
  - 要件2.1・2.3 は (1) のままで満たせた（4通りすべて 1.63〜1.74 倍速い。遅くなった場面は無い）。
    要件6.1（完了時のメモリ）も満たしている
- **Trade-offs**: (2) にすると走査中の途中の木にサイズが出せなくなる（走査の終わりまで 0 のまま）。
  利用者に見える動きが変わるうえ、`ResultFormatter` が途中の木を読む前提にも触れる。
  得られる速さは上の見積りで最大 1% 程度なので、割に合わない
- **Follow-up**: ノードの形そのものを変える案（Architecture Pattern Evaluation の D）は
  `architecture-refactoring` の候補として残る。そこで木の持ち方を変えるなら、合計の持ち方も一緒に見直す

### Decision: 並列度の既定値と上書き
- **Selected**: `Config.txt` に `ScanThreads`（0 = 自動）を足す。自動のとき、ネットワーク（UNC パスまたはネットワークドライブ）は 16、ローカルは論理プロセッサ数（4〜16 に丸める）を初期値とし、ベンチマークの結果で確定する。`UseParallelScan = false` は 1
- **Rationale**: 要件3.1〜3.3。TreeSize と Robocopy の既定値、SMB の往復の遅延が支配する性質

### Decision: 計測の道具を新しい検証ツールとして用意する
- **Context**: 要件2・4・5・6 は計測が前提。既存のコードに計時の仕組みが無い
- **Selected**: `Tools/ScanBench`（コンソール）。本体の `Scanner.RunScan` を同じ条件で繰り返し呼び、所要時間・フォルダ数・ファイル数・総バイト数・最大の作業セット・走査後の管理ヒープの量・結果の要約値（全ノードのパスとサイズから作る要約）を1行ずつ出す。並列度・逐次・物理サイズ換算・列挙のバッファの大きさを引数で変えられる
- **Rationale**: 画面を介さずに走査だけを繰り返し測れる。要約値で、並列と逐次、変更の前と後の集計値の一致を実データで確かめられる（要件1.1〜1.3 を期待値データのフィクスチャより大きな木で補う）
- **Trade-offs**: 画面の描き直しの影響は含まない。アプリでの所要時間は別途記録する

## Risks & Mitigations
- **1回の列挙にまとめることで、列挙の途中の失敗の扱いが変わる** — 従来はファイルの列挙とフォルダの列挙が別で、片方の途中の失敗でもう片方は影響を受けなかった。まとめると途中の失敗で残りのファイルとフォルダの両方を飛ばす。アクセス拒否は開く時点で失敗するので結果は同じ。途中の失敗（ネットワークの切断）は従来から結果が不定。設計に明記し、スキップの記録は従来どおり
- **ネットワーク・ローカルの判定の誤り** — 判定できないときはローカルの既定値を使う。`ScanThreads` で上書きできる
- **並列度を上げたことで NAS に負担** — 上限は 16 から始め、計測で調整する。`Config.txt` で下げられる
- **dotnet/runtime#108381（長い列挙での予期しないネットワークエラー）** — フォルダ単位の列挙なので、1回の列挙は短い。起きてもそのフォルダのスキップとして記録され、走査は続く
- **速い環境での自己検証の不安定さ（scan-correctness の 2.5）** — 走査が速くなると並行読み取りの回数が減る。合成の木を大きくするか、判定を調整する

## References
- [FileSystemEnumerator.Windows.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/IO/Enumeration/FileSystemEnumerator.Windows.cs)
- [FileSystemEntry.Windows.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/IO/Enumeration/FileSystemEntry.Windows.cs)
- [EnumerationOptions.BufferSize](https://learn.microsoft.com/en-us/dotnet/api/system.io.enumerationoptions.buffersize)
- [corefx#25426](https://github.com/dotnet/corefx/pull/25426) — FileSystemEnumerable の導入
- [dotnet/runtime#108381](https://github.com/dotnet/runtime/issues/108381) — NAS での長い列挙の失敗
- [Potential Pitfalls in Data and Task Parallelism](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/potential-pitfalls-in-data-and-task-parallelism)
- [ParallelOptions.MaxDegreeOfParallelism](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.paralleloptions.maxdegreeofparallelism)
- [MS-SMB2 QUERY_DIRECTORY](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-smb2/10906442-294c-46d3-8515-c277efe1f752)
- [File server performance tuning](https://learn.microsoft.com/en-us/windows-server/administration/performance-tuning/role/file-server)
- [TreeSize features](https://www.jam-software.com/treesize/features.shtml)
- [WizTree What's New](https://diskanalyzer.com/whats-new)
- [WinDirStat CHANGELOG](https://github.com/windirstat/windirstat/blob/master/CHANGELOG.md)
