using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace LargeFolderFinder
{
    /// <summary>
    /// 木の組み立てに使う走査の条件。
    /// </summary>
    /// <param name="WorkerCount">フォルダを処理する専用ワーカーの数。1 以上</param>
    /// <param name="UsePhysicalSize">ファイルのサイズをクラスタサイズに切り上げるかどうか</param>
    /// <param name="ClusterSize">クラスタサイズ（バイト）。0 なら切り上げない</param>
    /// <param name="EnumerationBufferSize">列挙のバッファの大きさ（バイト）。0 は .NET の既定</param>
    internal readonly record struct DirectoryWalkOptions(
        int WorkerCount, bool UsePhysicalSize, long ClusterSize, int EnumerationBufferSize);

    /// <summary>
    /// 木の組み立ての結果のうち、並列度に関する事実。
    /// </summary>
    /// <param name="WorkerCount">実際に使ったワーカーの数</param>
    /// <param name="PeakConcurrentEnumerations">同時に行われた列挙の数の最大（<paramref name="WorkerCount"/> 以下）</param>
    internal readonly record struct DirectoryWalkResult(int WorkerCount, int PeakConcurrentEnumerations);

    /// <summary>
    /// 決まった数の専用ワーカーが共有の作業の列からフォルダを取り出し、
    /// 1フォルダを1回の列挙で処理して <see cref="FolderInfo"/> の木を組み立てる
    /// </summary>
    /// <remarks>
    /// 全体の同時の列挙の数をワーカー数以下に保つため、深さごとの入れ子の並列化ではなく、
    /// 作業の列（後入れ先出し）と決まった数のワーカーで処理する。
    /// ワーカーはブロックする入出力を行うため、スレッドプールではなく専用のスレッドを使う。
    /// 途中の木を読む側（<c>ResultFormatter</c>）の規約を守るため、子のノードは
    /// <c>lock (node.Children)</c> の下で足す（1フォルダにつき1回だけ取って一括で足す）。
    /// </remarks>
    internal sealed class DirectoryWalker
    {
        /// <summary>作業の列の1件。処理するフォルダのノード・そのフォルダの完全パス・起点からの深さ</summary>
        private readonly record struct WorkItem(FolderInfo Node, string FullPath, int Depth);

        /// <summary>
        /// 1回の列挙で得る項目の中身。列挙の結果から読める値だけを持ち、
        /// ファイルごとの情報の取り直しと完全パスの文字列を作らない
        /// </summary>
        private readonly record struct DirectoryEntry(
            string Name, bool IsDirectory, bool IsReparsePoint, long Length, DateTime LastWriteTime);

        /// <summary>作業の列。後入れ先出しにして、列に溜まるフォルダの数を抑える（深さ優先に近い順）</summary>
        private readonly ConcurrentStack<WorkItem> _queue = new ConcurrentStack<WorkItem>();

        /// <summary>取り出せる仕事の数の合図。終わるときは全ワーカーを起こすために使う</summary>
        private readonly SemaphoreSlim _available = new SemaphoreSlim(0);

        private readonly DirectoryWalkOptions _options;
        private readonly EnumerationOptions _enumerationOptions;
        private readonly ScanSkipRecorder _skipRecorder;
        private readonly Action<int, string> _onFolderCompleted;
        private readonly CancellationToken _token;

        /// <summary>積んだが処理を終えていないフォルダの数。0 になったら走査は終わり</summary>
        private int _pending;

        /// <summary>0 以外なら全ワーカーが終わる（完了・取り消し・想定外の例外のいずれか）</summary>
        private int _stopped;

        /// <summary>いま列挙を行っているワーカーの数</summary>
        private int _activeEnumerations;

        /// <summary><see cref="_activeEnumerations"/> の最大</summary>
        private int _peakConcurrentEnumerations;

        /// <summary>最初の想定外の例外。捕まえた時点のスタックを保って投げ直すために控える</summary>
        private ExceptionDispatchInfo? _failure;

        private DirectoryWalker(
            DirectoryWalkOptions options,
            ScanSkipRecorder skipRecorder,
            Action<int, string> onFolderCompleted,
            CancellationToken token)
        {
            _options = options;
            _skipRecorder = skipRecorder;
            _onFolderCompleted = onFolderCompleted;
            _token = token;

            _enumerationOptions = new EnumerationOptions
            {
                // 既定は隠し・システムを除くため 0 を明示する（事前カウントと同じ集合にする）
                AttributesToSkip = 0,

                // 列挙の失敗は例外で受け取り、スキップとして記録して続ける
                IgnoreInaccessible = false,

                // 潜るのは自前の作業の列で行う。「.」「..」は返さない
                RecurseSubdirectories = false,
                ReturnSpecialDirectories = false,

                // 0 は .NET の既定の大きさを使う
                BufferSize = options.EnumerationBufferSize > 0 ? options.EnumerationBufferSize : 0,
            };
        }

        /// <summary>
        /// ルートのノードから木を組み立てる。すべてのフォルダを処理し終えるまで戻らない。
        /// </summary>
        /// <param name="rootNode">走査の起点のノード。子を持たないこと</param>
        /// <param name="rootPath">走査の起点のフォルダの完全パス</param>
        /// <param name="options">走査の条件。<see cref="DirectoryWalkOptions.WorkerCount"/> は 1 以上</param>
        /// <param name="skipRecorder">列挙できなかった対象の記録先</param>
        /// <param name="onFolderCompleted">
        /// フォルダを1つ処理し終えるたびに、その深さとフォルダの完全パスを渡して呼ぶ
        /// （ワーカーのスレッドから並行に呼ばれる）
        /// </param>
        /// <param name="token">取り消しの通知。各フォルダの取り出しの前に確かめる</param>
        /// <returns>実際のワーカー数と、同時の列挙の数の最大</returns>
        /// <exception cref="OperationCanceledException">取り消されたとき</exception>
        public static DirectoryWalkResult Walk(
            FolderInfo rootNode,
            string rootPath,
            DirectoryWalkOptions options,
            ScanSkipRecorder skipRecorder,
            Action<int, string> onFolderCompleted,
            CancellationToken token)
        {
            if (rootNode == null) throw new ArgumentNullException(nameof(rootNode));
            if (string.IsNullOrEmpty(rootPath)) throw new ArgumentException("走査の起点のパスが空です。", nameof(rootPath));
            if (skipRecorder == null) throw new ArgumentNullException(nameof(skipRecorder));
            if (onFolderCompleted == null) throw new ArgumentNullException(nameof(onFolderCompleted));
            if (options.WorkerCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(options), options.WorkerCount, "ワーカー数は1以上である必要があります。");
            }

            var walker = new DirectoryWalker(options, skipRecorder, onFolderCompleted, token);
            return walker.Run(rootNode, rootPath);
        }

        /// <summary>
        /// 専用のワーカーを起こして、作業の列が空になる（未処理が0になる）まで処理する
        /// </summary>
        private DirectoryWalkResult Run(FolderInfo rootNode, string rootPath)
        {
            _token.ThrowIfCancellationRequested();

            Push(new WorkItem(rootNode, rootPath, 0));

            int workerCount = _options.WorkerCount;

            try
            {
                var workers = new Thread[workerCount];

                for (int i = 0; i < workerCount; i++)
                {
                    // ブロックする入出力を行うため、スレッドプールではなく専用のスレッドで走らせる。
                    // アプリの終了を妨げないよう背景のスレッドにする
                    workers[i] = new Thread(WorkerLoop)
                    {
                        IsBackground = true,
                        Name = $"LargeFolderFinder.Scan.{i + 1}",
                    };
                    workers[i].Start();
                }

                foreach (var worker in workers)
                {
                    worker.Join();
                }
            }
            finally
            {
                // 全ワーカーが終わってからでないと合図を捨てられない
                _available.Dispose();
            }

            // 想定外の例外は最初の1件をそのまま（AggregateException に包まず）投げ直す
            _failure?.Throw();

            // 取り消しは、走り終えた後に気づいた場合も含めて取り消しとして扱う
            _token.ThrowIfCancellationRequested();

            return new DirectoryWalkResult(workerCount, Volatile.Read(ref _peakConcurrentEnumerations));
        }

        /// <summary>
        /// ワーカー1本の処理。どんな例外でもスレッドの外に出さず、全体を止める形にして
        /// <see cref="Run"/> が戻らなくなることを防ぐ
        /// </summary>
        private void WorkerLoop()
        {
            try
            {
                WorkerLoopCore();
            }
            catch (OperationCanceledException)
            {
                // 取り消しは失敗として控えず、全体を止めるだけにする（RunScan からは取り消しの例外が出る）
                RequestStop();
            }
            catch (Exception ex)
            {
                // 想定外の例外は最初の1件だけを控えて、全体を止める
                Interlocked.CompareExchange(ref _failure, ExceptionDispatchInfo.Capture(ex), null);
                RequestStop();
            }
        }

        /// <summary>
        /// 合図を待って作業の列から取り出し、フォルダを1つずつ処理する。
        /// 未処理が0になるか、終わりの合図が出たら戻る
        /// </summary>
        private void WorkerLoopCore()
        {
            while (true)
            {
                // 仕事が積まれるか、終わりの合図で起きる。未処理が0になると合図がワーカー数だけ出るため、
                // 全ワーカーが待ったまま仕事が来ない状態で固まることはない
                _available.Wait();

                if (Volatile.Read(ref _stopped) != 0)
                {
                    return;
                }

                // 取り消しはフォルダの取り出しの前に確かめる
                if (_token.IsCancellationRequested)
                {
                    RequestStop();
                    return;
                }

                if (!_queue.TryPop(out WorkItem item))
                {
                    // 合図の数と列の中身は対応するため通常は起きない。取り違えで固まらないよう次の合図を待つ
                    continue;
                }

                ProcessFolder(item);

                // 未処理を1つ減らし、0 になったら全ワーカーを終わらせる
                if (Interlocked.Decrement(ref _pending) == 0)
                {
                    RequestStop();
                    return;
                }
            }
        }

        /// <summary>
        /// フォルダ1つを1回の列挙で処理し、子のノードを一括で足して、合計を祖先へ伝える
        /// </summary>
        private void ProcessFolder(in WorkItem item)
        {
            FolderInfo node = item.Node;
            var children = new List<FolderInfo>();
            List<WorkItem>? subFolders = null;
            long filesSize = 0;

            int active = Interlocked.Increment(ref _activeEnumerations);
            UpdatePeakConcurrentEnumerations(active);

            try
            {
                // 1回の列挙でファイルとサブフォルダの両方を得る（ファイルごとの情報の取り直しをしない）
                var entries = new FileSystemEnumerable<DirectoryEntry>(item.FullPath, TransformEntry, _enumerationOptions);

                foreach (DirectoryEntry entry in entries)
                {
                    if (entry.IsDirectory)
                    {
                        // リパースポイントのフォルダは潜らず、ノードにもしない（旧来の走査と事前カウントと同じ集合）
                        if (entry.IsReparsePoint)
                        {
                            continue;
                        }

                        var childNode = new FolderInfo(entry.Name, 0, false, entry.LastWriteTime) { Parent = node };
                        children.Add(childNode);

                        subFolders ??= new List<WorkItem>();
                        subFolders.Add(new WorkItem(childNode, Path.Combine(item.FullPath, entry.Name), item.Depth + 1));
                    }
                    else
                    {
                        long size = entry.Length;

                        // クラスタサイズでアライメント（ディスク上のサイズ）。式は旧来のまま
                        if (_options.UsePhysicalSize && _options.ClusterSize > 0)
                        {
                            size = (size + _options.ClusterSize - 1) / _options.ClusterSize * _options.ClusterSize;
                        }

                        filesSize += size;

                        // ファイルのサイズに関わらずノードとして追加（表示時にフィルタリング）
                        children.Add(new FolderInfo(entry.Name, size, true, entry.LastWriteTime) { Parent = node });
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                // このフォルダの列挙の残りを飛ばして続ける。1回の列挙にまとめたため、
                // 列挙の途中の失敗では残りのファイルとサブフォルダの両方を飛ばす（それまでに得た分は残す）。
                // それ以外の例外は走査の想定外の失敗として上へ伝える
                _skipRecorder.Record(item.FullPath, ex);
            }
            finally
            {
                Interlocked.Decrement(ref _activeEnumerations);
            }

            // 子のノードは1フォルダにつき1回のロックで一括して足す（途中の木を読む側の規約を守る）
            if (children.Count > 0)
            {
                lock (node.Children)
                {
                    node.Children.AddRange(children);
                }
            }

            // このフォルダのファイルの合計を、処理の終わりに1回だけ祖先へ伝える
            if (filesSize != 0)
            {
                node.AddSize(filesSize);
            }

            // サブフォルダは親に足してから作業の列に積む
            if (subFolders != null)
            {
                PushRange(subFolders);
            }

            _onFolderCompleted(item.Depth, item.FullPath);
        }

        /// <summary>
        /// 列挙の1項目から、木の組み立てに要る値だけを取り出す。
        /// ここで <see cref="FileInfo"/> や完全パスの文字列を作らない
        /// </summary>
        private static DirectoryEntry TransformEntry(ref FileSystemEntry entry)
        {
            return new DirectoryEntry(
                entry.FileName.ToString(),
                entry.IsDirectory,
                (entry.Attributes & FileAttributes.ReparsePoint) != 0,
                entry.Length,

                // 旧来の FileInfo.LastWriteTime と同じくローカル時刻（Kind は Local）にする
                entry.LastWriteTimeUtc.LocalDateTime);
        }

        /// <summary>作業の列に1件積む</summary>
        private void Push(WorkItem item)
        {
            Interlocked.Increment(ref _pending);
            _queue.Push(item);
            _available.Release();
        }

        /// <summary>作業の列にまとめて積む</summary>
        private void PushRange(List<WorkItem> items)
        {
            Interlocked.Add(ref _pending, items.Count);
            _queue.PushRange(items.ToArray());
            _available.Release(items.Count);
        }

        /// <summary>
        /// 全ワーカーを終わらせる。待っているワーカーを起こすため、合図をワーカー数だけ出す
        /// </summary>
        private void RequestStop()
        {
            if (Interlocked.Exchange(ref _stopped, 1) == 0)
            {
                _available.Release(_options.WorkerCount);
            }
        }

        /// <summary>同時の列挙の数の最大を更新する</summary>
        private void UpdatePeakConcurrentEnumerations(int active)
        {
            int peak = Volatile.Read(ref _peakConcurrentEnumerations);

            while (active > peak)
            {
                int original = Interlocked.CompareExchange(ref _peakConcurrentEnumerations, active, peak);
                if (original == peak)
                {
                    return;
                }

                peak = original;
            }
        }
    }
}
