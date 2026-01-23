using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace LargeFolderFinder
{
    // スレッドセーフなカウンター用のクラス
    internal class ProgressCounter
    {
        public int Value;
        public double SmoothedFoldersPerSecond = -1.0;
        public int LastReportedCount = 0;
        public DateTime LastReportTime = DateTime.MinValue;
        public DateTime LastLogTime = DateTime.MinValue;
        public FolderInfo? RootNode; // リアルタイム表示用のルート
    }

    public class Scanner
    {
        public static async Task<int> CountFoldersAsync(string path, int maxDepth, CancellationToken token)
        {
            return await Task.Run(() =>
            {
                return CountFoldersRecursive(path, 0, maxDepth, token);
            }, token);
        }

        private static int CountFoldersRecursive(string path, int currentDepth, int maxDepth, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            int count = 1;

            if (currentDepth >= maxDepth) return count;

            string searchPath = Path.Combine(path, "*");
            IntPtr hFind = Win32.FindFirstFileEx(
                searchPath,
                Win32.FINDEX_INFO_LEVELS.FindExInfoBasic,
                out Win32.WIN32_FIND_DATA findData,
                Win32.FINDEX_SEARCH_OPS.FindExSearchNameMatch,
                IntPtr.Zero,
                Win32.FIND_FIRST_EX_LARGE_FETCH);

            if (hFind == (IntPtr)(-1)) return count;

            try
            {
                do
                {
                    if ((findData.dwFileAttributes & Win32.FILE_ATTRIBUTE_DIRECTORY) != 0)
                    {
                        if (findData.cFileName != "." && findData.cFileName != ".." &&
                            (findData.dwFileAttributes & Win32.FILE_ATTRIBUTE_REPARSE_POINT) == 0)
                        {
                            string subPath = Path.Combine(path, findData.cFileName);
                            count += CountFoldersRecursive(subPath, currentDepth + 1, maxDepth, token);
                        }
                    }
                } while (Win32.FindNextFile(hFind, out findData));
            }
            finally
            {
                Win32.FindClose(hFind);
            }

            return count;
        }

        public static async Task<FolderInfo?> RunScan(string path, long thresholdBytes, int totalFolders, int maxDepth, bool useParallel, bool usePhysicalSize, IProgress<ScanProgress> progress, CancellationToken token)
        {
            var progressCounter = new ProgressCounter();
            DateTime startTime = DateTime.Now;

            // クラスタサイズを取得
            long clusterSize = 0;
            if (usePhysicalSize)
            {
                clusterSize = GetClusterSize(path);
            }

            return await Task.Run(() =>
            {
                var dir = new DirectoryInfo(path);
                // ルートノードを先行作成
                var rootNode = new FolderInfo(dir.FullName, 0, false, dir.LastWriteTime);
                progressCounter.RootNode = rootNode;

                ScanRecursiveInternal(dir, thresholdBytes, totalFolders, 0, maxDepth, useParallel, usePhysicalSize, clusterSize, progressCounter, startTime, progress, token, rootNode);

                // 最終的に閾値未満の枝を剪定
                // 最終的に閾値未満の枝を剪定しない（全ノード保持）
                // PruneTree(rootNode, thresholdBytes);

                return rootNode; // 閾値に関わらずルートノードを返す
            }, token);
        }

        private static long ScanRecursiveInternal(
            DirectoryInfo dir,
            long thresholdBytes,
            int totalFolders,
            int currentDepth,
            int maxDepth,
            bool useParallel,
            bool usePhysicalSize,
            long clusterSize,
            ProgressCounter progressCounter,
            DateTime startTime,
            IProgress<ScanProgress> progress,
            CancellationToken token,
            FolderInfo currentNode)
        {
            token.ThrowIfCancellationRequested();

            if (currentDepth <= maxDepth)
            {
                Interlocked.Increment(ref progressCounter.Value);
            }

            int currentProcessed = progressCounter.Value;

            // 5秒間隔で更新 (最初の1回は即時更新)
            if (currentProcessed == 1 || (DateTime.Now - progressCounter.LastReportTime).TotalSeconds >= 5.0)
            {
                lock (progressCounter)
                {
                    if (currentProcessed == 1 || (DateTime.Now - progressCounter.LastReportTime).TotalSeconds >= 5.0)
                    {
                        ReportProgress(currentProcessed, totalFolders, startTime, progressCounter, progress, dir.FullName);
                    }
                }
            }

            long myFilesSize = 0;
            // 1. ファイルの合計
            try
            {
                foreach (var f in dir.EnumerateFiles())
                {
                    long size = f.Length;

                    // クラスタサイズでアライメント(ディスク上のサイズ)
                    if (usePhysicalSize && clusterSize > 0)
                    {
                        size = (size + clusterSize - 1) / clusterSize * clusterSize;
                    }

                    myFilesSize += size;

                    // ファイルのサイズに関わらずノードとして追加（表示時にフィルタリング）
                    // if (size >= thresholdBytes)
                    {
                        var fileNode = new FolderInfo(f.Name, size, true, f.LastWriteTime) { Parent = currentNode };
                        lock (currentNode.Children) { currentNode.Children.Add(fileNode); }
                    }
                }
            }
            catch { /* アクセス拒否は無視 */ }

            // 見つかったファイルサイズを即座に加算(親まで波及)
            currentNode.AddSize(myFilesSize);

            // 2. サブディレクトリの探索
            try
            {
                var directories = dir.EnumerateDirectories()
                    .Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0);

                if (useParallel)
                {
                    Parallel.ForEach(directories, new ParallelOptions { CancellationToken = token }, (subDir) =>
                    {
                        var childNode = new FolderInfo(subDir.Name, 0, false, subDir.LastWriteTime) { Parent = currentNode };
                        lock (currentNode.Children) { currentNode.Children.Add(childNode); }

                        ScanRecursiveInternal(subDir, thresholdBytes, totalFolders, currentDepth + 1, maxDepth, useParallel, usePhysicalSize, clusterSize, progressCounter, startTime, progress, token, childNode);
                    });
                }
                else
                {
                    foreach (var subDir in directories)
                    {
                        var childNode = new FolderInfo(subDir.Name, 0, false, subDir.LastWriteTime) { Parent = currentNode };
                        lock (currentNode.Children) { currentNode.Children.Add(childNode); }

                        ScanRecursiveInternal(subDir, thresholdBytes, totalFolders, currentDepth + 1, maxDepth, useParallel, usePhysicalSize, clusterSize, progressCounter, startTime, progress, token, childNode);
                    }
                }
            }
            catch { /* 無視 */ }

            return currentNode.Size;
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
                // エラー時は0を返す
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

        private static void ReportProgress(int processed, int total, DateTime startTime, ProgressCounter counter, IProgress<ScanProgress> progress, string currentPath)
        {
            TimeSpan? estimatedRemaining = null;
            DateTime now = DateTime.Now;

            // 統計情報の計算など
            if (processed >= 10 || counter.LastReportedCount > 0)
            {
                if (counter.LastReportTime == DateTime.MinValue)
                {
                    var elapsedTotal = now - startTime;
                    counter.SmoothedFoldersPerSecond = processed / elapsedTotal.TotalSeconds;
                }
                else
                {
                    var elapsedSinceLast = now - counter.LastReportTime;
                    if (elapsedSinceLast.TotalSeconds > 0)
                    {
                        double currentSpeed = (processed - counter.LastReportedCount) / elapsedSinceLast.TotalSeconds;
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
                counter.LastReportTime = now;

                int remaining = total - processed;
                if (counter.SmoothedFoldersPerSecond > 0 && remaining > 0)
                {
                    estimatedRemaining = TimeSpan.FromSeconds(remaining / counter.SmoothedFoldersPerSecond);
                }
            }

            // 20秒ごとのログ出力 (5秒ごとの更新タイミングでチェック)
            if ((now - counter.LastLogTime).TotalSeconds >= 20.0)
            {
                double percent = total > 0 ? (double)processed / total * 100.0 : 0;
                var elapsed = now - startTime;
                string remStr = estimatedRemaining.HasValue ? estimatedRemaining.Value.ToString(@"hh\:mm\:ss") : "Unknown";

                // プログレスバー相当の情報 + 現在のパス
                string logMsg = $"Progress: {processed}/{total} ({percent:F1}%) - Elapsed: {elapsed:hh\\:mm\\:ss} - Remaining: {remStr} - Checking: {currentPath}";
                Logger.Log(logMsg);

                counter.LastLogTime = now;
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
