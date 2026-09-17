# パフォーマンス方針

このプロジェクトでは **速度は機能そのもの**です（product.md の優先順位 1）。ここでは走査性能に関する実装パターン、計測手順、判断基準をまとめます。

## 処理の全体像

スキャンは 2 段構えです。**それぞれ列挙方式が違う**ことに注意してください。

| 段階 | 実装 | 列挙方式 | 目的 |
|---|---|---|---|
| ① 事前カウント | `Scanner.CountFoldersAsync` | **Win32 API**（`FindFirstFileEx` / `FindNextFile`） | 進捗率の分母を得る |
| ② 本スキャン | `Scanner.RunScan` → `ScanRecursiveInternal` | **`DirectoryInfo.EnumerateFiles` / `EnumerateDirectories`** | サイズ集計とツリー構築 |

### ① 事前カウント（Win32）
- `FindExInfoBasic`（代替名を取得しない）+ `FIND_FIRST_EX_LARGE_FETCH`（バッファ拡大）で列挙コストを下げる
- `Config.MaxDepthForCount`（既定 3）で**深さを打ち切る**。全階層を数えると事前カウント自体が本スキャン並みの時間になるため
- ハンドルは `try` / `finally` で必ず `FindClose`
- `Config.SkipFolderCount = true` でこの段階を丸ごと省略できる（起動は速いが進捗率が出ない）

### ② 本スキャン
- ファイルは `EnumerateFiles`、サブディレクトリは `EnumerateDirectories` で列挙
- **リパースポイント（`FileAttributes.ReparsePoint`）は必ず除外**する。シンボリックリンク経由の無限再帰を防ぐため
- アクセス拒否は `catch { }` で握りつぶして走査を継続する。1 フォルダーの権限不足で全体を止めない

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
- 進捗には暫定ツリー（`ProgressCounter.RootNode`）を載せ、走査中でも途中結果を見せる

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

1. **Release ビルド**で計測する（`dotnet build LargeFolderFinder.csproj -c Release`）
2. 対象と条件を固定する。最低でも「ローカルドライブ」「ネットワークドライブ」の 2 系統
3. アプリが計測した経過時間を使う（`SessionViewModel` の `Stopwatch` → UI 表示 + `Logger` の `LogScanSuccess`）
4. **OS のファイルキャッシュの影響を排除する**。同一パスの 2 回目以降は大幅に速くなるため、初回計測値と 2 回目以降を区別して記録する
5. 変更前後を同一条件で比較する。絶対値ではなく差分で判断する
6. `Config.txt` の設定（`UseParallelScan` / `SkipFolderCount` / `UsePhysicalSize`）を記録に残す

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
