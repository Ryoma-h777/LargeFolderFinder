using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LargeFolderFinder
{
    // スレッドセーフなカウンター用のクラス
    internal class ProgressCounter
    {
        /// <summary>まだ一度も報告・記録していないことを表す時刻の値</summary>
        public const long NotReported = long.MinValue;

        public int Value;
        public double SmoothedFoldersPerSecond = -1.0;
        public int LastReportedCount = 0;

        /// <summary>
        /// 最後に途中の報告を送った時刻（単調の時計のミリ秒）。まだ送っていなければ <see cref="NotReported"/>
        /// </summary>
        /// <remarks>
        /// フォルダごとに時刻を取るため、壁時計（DateTime.Now）ではなく安価で単調な時計を使う。
        /// 意味（前回の報告からの経過）は従来と同じ
        /// </remarks>
        public long LastReportMs = NotReported;

        /// <summary>最後にログへ書いた時刻（単調の時計のミリ秒）。まだ書いていなければ <see cref="NotReported"/></summary>
        public long LastLogMs = NotReported;

        public FolderInfo? RootNode; // リアルタイム表示用のルート
    }

    public class Scanner
    {
        /// <summary>途中の進捗を報告する間隔（ミリ秒）</summary>
        private const long ReportIntervalMs = 5_000;

        /// <summary>進捗をログへ書く間隔（ミリ秒）</summary>
        private const long LogIntervalMs = 20_000;

        /// <summary>
        /// 事前カウント。深さ0〜maxDepth のフォルダ数（起点を含む）を返す。
        /// 数え方は本スキャンと同じ集合になるよう FolderCounter に委ねる
        /// </summary>
        public static async Task<int> CountFoldersAsync(string path, int maxDepth, CancellationToken token)
        {
            return await Task.Run(() => FolderCounter.Count(path, maxDepth, token), token);
        }

        /// <summary>
        /// 指定したパスを走査し、結果の木のルートノードを返す。
        /// </summary>
        /// <remarks>
        /// 末尾の tuning は走査の調整値（ワーカー数・列挙のバッファの大きさ・走査の方式の指定）で、
        /// 省略時は自動と .NET の既定、方式は <see cref="ScanMethodSelector"/> の決め方に委ねる。
        /// 通常の走査は決まった数の専用ワーカーが共有の作業の列からフォルダを取り出して行う（<see cref="DirectoryWalker"/>）。
        /// 目録の走査を選んだ（または指定された）ときは、始められなければ理由をログに記録して
        /// 通常の走査でやり直す（要件2.2）。
        /// </remarks>
        public static async Task<FolderInfo?> RunScan(string path, long thresholdBytes, int totalFolders, int maxDepth, bool useParallel, bool usePhysicalSize, IProgress<ScanProgress> progress, CancellationToken token, ScanTuning? tuning = null)
        {
            var progressCounter = new ProgressCounter();

            // 進捗の間引きと残り時間の推定に使う時計。フォルダごとに取るため単調で安価なものを使う
            long startMs = Environment.TickCount64;

            // クラスタサイズを取得
            long clusterSize = 0;
            if (usePhysicalSize)
            {
                clusterSize = GetClusterSize(path);
            }

            // 走査の方式を決める（要件1.1, 2.1, 2.4, 3.4）。試験と計測のための指定があればそれに従う
            ScanMethodKind requestedMethod = ResolveScanMethod(path, tuning);

            // 並列度は設定と対象（ネットワークかローカルか）から決める。逐次の設定が最優先
            int workerCount = ScanParallelism.Resolve(path, useParallel, tuning?.ThreadCount ?? 0);
            int enumerationBufferSize = tuning?.EnumerationBufferSize ?? 0;

            // 列挙できなかった対象を集め、完了・取り消し・中断のいずれでも finally で1回だけログへ書く
            var skipRecorder = new ScanSkipRecorder();

            try
            {
                return await Task.Run(() =>
                {
                    if (requestedMethod == ScanMethodKind.VolumeLayout)
                    {
                        // 要件2.2: 目録の走査を始められないときは、理由をログに記録して通常の走査でやり直す。
                        // 利用者には結果と方式だけが見えるため、ここで走査を止めない
                        string failureReason = BeginVolumeLayoutScan();

                        Logger.Log(
                            $"{DescribeScanMethod(ScanMethodKind.VolumeLayout)}を始められないため、" +
                            $"{DescribeScanMethod(ScanMethodKind.NormalEnumeration)}に切り替えます。理由: {failureReason}");
                    }

                    var dir = new DirectoryInfo(path);
                    // ルートノードを先行作成
                    var rootNode = new FolderInfo(dir.FullName, 0, false, dir.LastWriteTime);
                    progressCounter.RootNode = rootNode;

                    var walkResult = DirectoryWalker.Walk(
                        rootNode,
                        dir.FullName,
                        new DirectoryWalkOptions(workerCount, usePhysicalSize, clusterSize, enumerationBufferSize),
                        skipRecorder,
                        (depth, folderPath) => OnFolderCompleted(
                            depth, folderPath, totalFolders, maxDepth, startMs, progressCounter, progress),
                        token);

                    // 最終的に閾値未満の枝を剪定しない（全ノード保持）
                    // PruneTree(rootNode, thresholdBytes);

                    // 並列度の事実は、スキップの書き出しと同じく走査の終わりに1回だけログへ書く
                    Logger.Log($"走査の並列度: ワーカー数 {walkResult.WorkerCount}、同時の列挙の最大 {walkResult.PeakConcurrentEnumerations}");

                    // 要件2.3: 実際に結果を作った方式と件数を、走査の終わりに1回だけログへ書く
                    Logger.Log($"走査の終わり: 方式 {DescribeScanMethod(ScanMethodKind.NormalEnumeration)}、数えたフォルダ {progressCounter.Value} 件");

                    // 正常に完了したときだけ、完了の印・最後の数・スキップの一覧・並列度を載せた進捗を1回報告する。
                    // 取り消し・例外のときは上の呼び出しから例外が伝わるため、ここには来ない。
                    // この報告は走査のスレッドで送る。UI の同期コンテキストへ投げられる報告が、
                    // RunScan の続き（await の後）より先に並ぶようにするため
                    ReportFinalProgress(
                        progressCounter.Value,
                        totalFolders,
                        skipRecorder,
                        progress,
                        walkResult.WorkerCount,
                        walkResult.PeakConcurrentEnumerations,
                        ScanMethodKind.NormalEnumeration);

                    return rootNode; // 閾値に関わらずルートノードを返す
                }, token);
            }
            finally
            {
                skipRecorder.Flush(path);
            }
        }

        /// <summary>
        /// この走査で使う方式を決める（要件1.1, 2.1, 2.4, 3.4）。決めた方式と理由は走査の始めに1回だけログへ書く。
        /// </summary>
        /// <param name="path">走査の起点のパス</param>
        /// <param name="tuning">走査の調整値。方式の指定があればそれに従う（試験と計測のため。画面からは渡らない）</param>
        private static ScanMethodKind ResolveScanMethod(string path, ScanTuning? tuning)
        {
            if (tuning?.ForcedMethod is ScanMethodKind forced)
            {
                Logger.Log($"走査の方式の指定: {DescribeScanMethod(forced)}（試験と計測のための指定）");
                return forced;
            }

            var decision = ScanMethodSelector.Decide(path, Config.Instance.UseMftScan, AdminRights.IsElevated);
            Logger.Log($"走査の方式の決定: {DescribeScanMethod(decision.Method)}。理由: {decision.Reason}");
            return decision.Method;
        }

        /// <summary>
        /// 目録の走査を始める（ボリュームを開いて目録の列挙を始める）。始められなければその理由を返す（要件2.2）。
        /// </summary>
        /// <returns>始められなかった理由（1行の日本語）。呼び出し側はこれをログに記録して通常の走査へ切り替える</returns>
        /// <remarks>
        /// いまはボリュームを開いて目録を列挙する部品が無いため、常に「始められない」を返す。
        /// 読み取りの部品と木の組み立てが入ると、この関数が開いて列挙し、成功した場合の経路が増える。
        /// 失敗したときの扱い（理由を記録して通常の走査でやり直す）は、そのときも変わらない。
        /// </remarks>
        private static string BeginVolumeLayoutScan()
        {
            return "ボリュームを開いて目録を読み出す部品がまだ無いため、目録の列挙を始められませんでした。";
        }

        /// <summary>
        /// 走査の方式を、ログに出す短い日本語の言葉で表す。
        /// </summary>
        /// <remarks>
        /// 画面の表示は言語ファイルの文言（<see cref="LocalizationManager.GetScanMethodKey"/>）を使う。
        /// ログは日本語で残す決まりなので、ここでは翻訳を通さない。
        /// </remarks>
        private static string DescribeScanMethod(ScanMethodKind method)
        {
            return method switch
            {
                ScanMethodKind.VolumeLayout => "目録の走査",
                _ => "通常の走査",
            };
        }

        /// <summary>
        /// フォルダを1つ処理し終えたときの進捗の数え方と報告の間引き。走査のワーカーから並行に呼ばれる
        /// </summary>
        /// <remarks>
        /// 数えるのは深さが事前カウントの範囲（<paramref name="maxDepth"/> 以下）のフォルダだけで、
        /// 報告は5秒間隔（最初の1回は即時）に間引く。どちらも従来の走査と同じ
        /// </remarks>
        private static void OnFolderCompleted(
            int depth,
            string folderPath,
            int totalFolders,
            int maxDepth,
            long startMs,
            ProgressCounter progressCounter,
            IProgress<ScanProgress> progress)
        {
            if (depth <= maxDepth)
            {
                Interlocked.Increment(ref progressCounter.Value);
            }

            int currentProcessed = progressCounter.Value;

            // 5秒間隔で更新 (最初の1回は即時更新)
            if (currentProcessed == 1 || HasElapsed(progressCounter.LastReportMs, ReportIntervalMs))
            {
                lock (progressCounter)
                {
                    if (currentProcessed == 1 || HasElapsed(progressCounter.LastReportMs, ReportIntervalMs))
                    {
                        ReportProgress(currentProcessed, totalFolders, startMs, progressCounter, progress, folderPath);
                    }
                }
            }
        }

        /// <summary>
        /// 単調の時計で、前回の時刻から指定の間隔が過ぎたかを判定する。
        /// 前回がまだ無い（<see cref="ProgressCounter.NotReported"/>）ときは過ぎたものとして扱う
        /// </summary>
        private static bool HasElapsed(long lastMs, long intervalMs)
        {
            if (lastMs == ProgressCounter.NotReported)
            {
                return true;
            }

            return Environment.TickCount64 - lastMs >= intervalMs;
        }

        /// <summary>
        /// クラスタサイズを取得する
        /// </summary>
        private static long GetClusterSize(string path)
        {
            try
            {
                string? rootPath = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(rootPath))
                {
                    return 0;
                }

                uint sectorsPerCluster, bytesPerSector, numberOfFreeClusters, totalNumberOfClusters;
                if (Win32.GetDiskFreeSpace(rootPath, out sectorsPerCluster, out bytesPerSector, out numberOfFreeClusters, out totalNumberOfClusters))
                {
                    return sectorsPerCluster * bytesPerSector;
                }
            }
            catch
            {
                // 意図して無視: クラスタサイズを得られないときは0を返し、物理サイズに換算しない（既定の動き）
            }
            return 0;
        }

        private static void PruneTree(FolderInfo node, long thresholdBytes)
        {
            lock (node.Children)
            {
                for (int i = node.Children.Count - 1; i >= 0; i--)
                {
                    var child = node.Children[i];
                    if (child.Size < thresholdBytes)
                    {
                        node.Children.RemoveAt(i);
                    }
                    else
                    {
                        PruneTree(child, thresholdBytes);
                    }
                }
            }
        }

        /// <summary>
        /// 走査の正常な完了時の最後の進捗を1回報告する。途中の報告の間隔（5秒）やログ（20秒）の計時には関わらない。
        /// </summary>
        /// <remarks>
        /// 結果の木（CurrentResult）は載せない。完了後の木は RunScan の戻り値で渡るため、
        /// ここで載せると受け手が同じ木の描画を二重に始めてしまう。
        /// <paramref name="method"/> には**実際に結果を作った方式**を載せる（要件2.3）。
        /// </remarks>
        private static void ReportFinalProgress(int processed, int total, ScanSkipRecorder skipRecorder, IProgress<ScanProgress> progress, int workerCount, int peakConcurrentEnumerations, ScanMethodKind method)
        {
            progress?.Report(new ScanProgress
            {
                ProcessedFolders = processed,
                TotalFolders = total,
                EstimatedTimeRemaining = null,
                CurrentResult = null,
                IsFinal = true,
                Skipped = skipRecorder.Snapshot(),
                WorkerCount = workerCount,
                PeakConcurrentEnumerations = peakConcurrentEnumerations,
                Method = method
            });
        }

        private static void ReportProgress(int processed, int total, long startMs, ProgressCounter counter, IProgress<ScanProgress> progress, string currentPath)
        {
            TimeSpan? estimatedRemaining = null;
            long nowMs = Environment.TickCount64;

            // 統計情報の計算など
            if (processed >= 10 || counter.LastReportedCount > 0)
            {
                if (counter.LastReportMs == ProgressCounter.NotReported)
                {
                    double elapsedTotalSeconds = (nowMs - startMs) / 1000.0;
                    counter.SmoothedFoldersPerSecond = processed / elapsedTotalSeconds;
                }
                else
                {
                    double elapsedSinceLastSeconds = (nowMs - counter.LastReportMs) / 1000.0;
                    if (elapsedSinceLastSeconds > 0)
                    {
                        double currentSpeed = (processed - counter.LastReportedCount) / elapsedSinceLastSeconds;
                        const double Alpha = 0.1;
                        if (counter.SmoothedFoldersPerSecond < 0)
                        {
                            counter.SmoothedFoldersPerSecond = currentSpeed;
                        }
                        else
                        {
                            counter.SmoothedFoldersPerSecond = (Alpha * currentSpeed) + ((1.0 - Alpha) * counter.SmoothedFoldersPerSecond);
                        }
                    }
                }

                counter.LastReportedCount = processed;
                counter.LastReportMs = nowMs;

                int remaining = total - processed;
                if (counter.SmoothedFoldersPerSecond > 0 && remaining > 0)
                {
                    estimatedRemaining = TimeSpan.FromSeconds(remaining / counter.SmoothedFoldersPerSecond);
                }
            }

            // 20秒ごとのログ出力 (5秒ごとの更新タイミングでチェック)
            if (HasElapsed(counter.LastLogMs, LogIntervalMs))
            {
                double percent = total > 0 ? (double)processed / total * 100.0 : 0;
                var elapsed = TimeSpan.FromMilliseconds(nowMs - startMs);
                string remStr = estimatedRemaining.HasValue ? estimatedRemaining.Value.ToString(@"hh\:mm\:ss") : "Unknown";

                // プログレスバー相当の情報 + 現在のパス
                string logMsg = $"Progress: {processed}/{total} ({percent:F1}%) - Elapsed: {elapsed:hh\\:mm\\:ss} - Remaining: {remStr} - Checking: {currentPath}";
                Logger.Log(logMsg);

                counter.LastLogMs = nowMs;
            }

            progress?.Report(new ScanProgress
            {
                ProcessedFolders = processed,
                TotalFolders = total,
                EstimatedTimeRemaining = estimatedRemaining,
                CurrentResult = counter.RootNode // 暫定ツリーを報告
            });
        }
    }
}
