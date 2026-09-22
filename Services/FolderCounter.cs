using System;
using System.IO;
using System.Security;
using System.Threading;

namespace LargeFolderFinder
{
    /// <summary>
    /// 事前カウント。深さの上限まで、本スキャンと同じ集合のフォルダを数える
    /// </summary>
    /// <remarks>
    /// 本スキャン（Scanner.ScanRecursiveInternal）と同じく BCL の列挙を使うため、
    /// 長いパス（日本語を含む）も OS の設定や実行ファイルの宣言に関わらず数えられる
    /// </remarks>
    internal static class FolderCounter
    {
        /// <summary>
        /// 子フォルダの列挙の選択肢。リパースポイントだけを除き、隠し・システム属性のフォルダは数える
        /// （既定の AttributesToSkip は隠し・システムを除くため、明示する）
        /// </summary>
        private static readonly EnumerationOptions ChildDirectoryOptions = new EnumerationOptions
        {
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            RecurseSubdirectories = false,
        };

        /// <summary>深さ0〜maxDepth のフォルダ数（起点を含む）を返す。</summary>
        /// <param name="rootPath">数える起点のフォルダのパス。実在している必要がある</param>
        /// <param name="maxDepth">深さの上限。起点を深さ0とする。0以上を想定する</param>
        /// <param name="token">取り消しの通知。各階層で確かめる</param>
        public static int Count(string rootPath, int maxDepth, CancellationToken token)
        {
            if (string.IsNullOrEmpty(rootPath))
            {
                throw new ArgumentException("起点のフォルダのパスが空です。", nameof(rootPath));
            }

            // 深さの上限は Config.txt の値がそのまま渡るため、負の値でも止めない。
            // 旧来の事前カウントと同じく、上限0と同じ扱い（起点だけを数える）になる
            return CountRecursive(new DirectoryInfo(rootPath), 0, maxDepth, token);
        }

        /// <summary>
        /// dir 自身を1と数え、深さの上限に達していなければ子フォルダを数えて足す
        /// </summary>
        private static int CountRecursive(DirectoryInfo dir, int currentDepth, int maxDepth, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            int count = 1;

            if (currentDepth >= maxDepth) return count;

            try
            {
                foreach (var subDir in dir.EnumerateDirectories("*", ChildDirectoryOptions))
                {
                    count += CountRecursive(subDir, currentDepth + 1, maxDepth, token);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is SecurityException)
            {
                // 意図して無視: 列挙できないフォルダは自身だけを数え、配下は数えない（本スキャンと同じ）。
                // スキップの記録は本スキャンが同じ対象について行うため、ここでは記録しない
            }

            return count;
        }
    }
}
