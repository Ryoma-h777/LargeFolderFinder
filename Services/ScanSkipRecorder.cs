using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace LargeFolderFinder
{
    /// <summary>
    /// 走査中に列挙できなかった対象を種類つきで集め、走査の終わりに1回でログへ書く
    /// </summary>
    /// <remarks>
    /// スキップは NAS などで数千件になりうるため、1件ごとにログへ書かず（ファイルへの追記とロックで走査が遅くなる）、
    /// 並行の集合に集めて <see cref="Flush"/> でまとめて書く。
    /// 同じフォルダがファイルの列挙とフォルダの列挙の両方で失敗することがあるため、同じパスは1件にまとめ、
    /// 最初に記録した種類と理由を残す
    /// </remarks>
    internal sealed class ScanSkipRecorder
    {
        /// <summary>並列の走査から並行に積まれる記録。パスごとに最初の1件と、その記録の順番を持つ</summary>
        private readonly ConcurrentDictionary<string, (long Sequence, ScanSkip Skip)> _skips =
            new ConcurrentDictionary<string, (long Sequence, ScanSkip Skip)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>記録の順番を振るための連番</summary>
        private long _sequence;

        /// <summary>書き出し済みなら1。2回目以降の <see cref="Flush"/> を何もしないために使う</summary>
        private int _flushed;

        /// <summary>
        /// 例外の型から種類を決めて記録する。並行に呼べる。
        /// </summary>
        /// <param name="path">列挙できなかった対象のパス</param>
        /// <param name="ex">列挙で起きた例外。アクセス拒否・見つからない・入出力の失敗のいずれかであること</param>
        /// <exception cref="ArgumentException">記録の対象でない種類の例外が渡されたとき</exception>
        public void Record(string path, Exception ex)
        {
            if (ex == null) throw new ArgumentNullException(nameof(ex));

            ScanSkipKind kind = ex switch
            {
                UnauthorizedAccessException => ScanSkipKind.AccessDenied,
                DirectoryNotFoundException => ScanSkipKind.NotFound,
                FileNotFoundException => ScanSkipKind.NotFound,
                IOException => ScanSkipKind.IoError,
                // 記録の対象外の例外は、呼び出し側が捕まえずに上へ伝える約束。ここに来たら呼び出し側の誤り
                _ => throw new ArgumentException($"スキップとして記録できない種類の例外です: {ex.GetType().Name}", nameof(ex), ex),
            };

            // 既に同じパスの記録があれば何もしない（最初の種類と理由を残す）
            long sequence = Interlocked.Increment(ref _sequence);
            _skips.TryAdd(path, (sequence, new ScanSkip(path, kind, ex.Message)));
        }

        /// <summary>
        /// 記録の写しを、パスごとに1件、記録した順に返す。
        /// </summary>
        public IReadOnlyList<ScanSkip> Snapshot()
        {
            return _skips.Values
                .OrderBy(entry => entry.Sequence)
                .Select(entry => entry.Skip)
                .ToArray();
        }

        /// <summary>
        /// 件数と全件を1回の Logger.Log で書く。2回目以降は何もしない。
        /// </summary>
        /// <param name="scanRootPath">走査の起点のパス（ログの見出しに載せる）</param>
        public void Flush(string scanRootPath)
        {
            if (Interlocked.Exchange(ref _flushed, 1) != 0) return;

            var skips = Snapshot();

            // 0件でも1行書く。書き出しが走ったこと（スキップが無かったこと）をログから確かめられるようにするため
            var sb = new StringBuilder();
            sb.Append($"走査のスキップ: {skips.Count} 件（起点: {scanRootPath}）");
            foreach (var skip in skips)
            {
                sb.Append('\n');
                sb.Append($"  [{skip.Kind}] {skip.Path} - {skip.Reason}");
            }

            Logger.Log(sb.ToString());
        }
    }
}
