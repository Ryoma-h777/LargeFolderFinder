using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LargeFolderFinder.ScanBench;

/// <summary>
/// 走査の結果の木から、集計値が同じかどうかを1つの短い値で比べられる要約値を作る
/// （design.md: Components and Interfaces / Tools / ScanBench「要約値」）。
/// </summary>
/// <remarks>
/// 要約値は、全ノードの「ルートからの相対パス・ファイルかどうか・サイズ」を1行にし、
/// 序数順（<see cref="StringComparer.Ordinal"/>）に並べた一覧の SHA-256 の先頭16文字である。
/// 並列と逐次、変更の前と後で同じ対象の要約値が一致すれば集計値が一致している
/// （requirements.md: 1.1, 1.2, 1.3 を実データで確かめる手段）。
///
/// ルートのノードの <c>Name</c> は完全パスを保持する規約のため、相対パスはルートを空文字として
/// 子の名前から組み立てる。こうすることで、公開リポジトリに残す記録に利用者のパスが出ない
/// （design.md「出力にパスそのものは含めない」）。
///
/// 木の辿り方は明示のスタックによる反復である。数百万ノード・深い階層でも再帰の深さで
/// 落ちないようにするためで、要約値は並べ替えの後に決まるので辿る順には依存しない。
///
/// 要約値を作るには全ノードの1行分をいったん一覧に持つ必要があり、その一覧は走査の結果の木と
/// 同じくらいの大きさになりうる。数百万ファイルの対象でメモリの列（最大の作業セット）を測るときは
/// この一覧が値を押し上げてしまうため、要約値を作らずに数だけ数える経路を用意している
/// （<c>includeDigest</c> を偽にする。ScanBench の <c>--no-digest</c>）。
/// </remarks>
internal sealed class ResultDigest
{
    /// <summary>相対パスの区切り。実際の区切り文字に依らず常にこの1文字を使う。</summary>
    private const char PathSeparator = '/';

    /// <summary>ファイルのノードを表す印。</summary>
    private const string FileMark = "F";

    /// <summary>フォルダのノードを表す印。</summary>
    private const string FolderMark = "D";

    /// <summary>要約値として残す16進の文字数。</summary>
    private const int DigestLength = 16;

    private ResultDigest(string? value, long folderCount, long fileCount, long totalBytes)
    {
        Value = value;
        FolderCount = folderCount;
        FileCount = fileCount;
        TotalBytes = totalBytes;
    }

    /// <summary>
    /// 要約値（SHA-256 の先頭16文字、小文字の16進）。要約値を作らずに数えたときは null。
    /// </summary>
    public string? Value { get; }

    /// <summary>フォルダのノードの数（ルートを含む）。</summary>
    public long FolderCount { get; }

    /// <summary>ファイルのノードの数。</summary>
    public long FileCount { get; }

    /// <summary>ルートのノードの合計バイト数。</summary>
    public long TotalBytes { get; }

    /// <summary>
    /// 走査の結果の木から要約値とノードの数を作る。
    /// </summary>
    /// <param name="root">走査の結果のルートのノード。</param>
    /// <param name="includeDigest">
    /// 要約値を作るかどうか。偽のときは一覧を持たずにノードの数と合計だけを数え、
    /// <see cref="Value"/> は null になる。数百万ファイルの対象でメモリを測るときに使う。
    /// </param>
    /// <returns>要約値とノードの数。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> が null の場合。</exception>
    public static ResultDigest Compute(global::LargeFolderFinder.FolderInfo root, bool includeDigest)
    {
        if (root == null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        // 序数順に並べ替えてから一括で混ぜるため、全ノードの1行分をいったん集める。
        // 要約値を作らないときは一覧そのものを確保しない（メモリを押し上げないため）。
        List<string>? lines = includeDigest ? new List<string>() : null;
        long folderCount = 0;
        long fileCount = 0;

        var stack = new Stack<PendingNode>();
        stack.Push(new PendingNode(root, string.Empty));

        while (stack.Count > 0)
        {
            PendingNode pending = stack.Pop();
            global::LargeFolderFinder.FolderInfo node = pending.Node;

            if (node.IsFile)
            {
                fileCount++;
            }
            else
            {
                folderCount++;
            }

            lines?.Add(string.Concat(
                pending.RelativePath,
                "\t",
                node.IsFile ? FileMark : FolderMark,
                "\t",
                node.Size.ToString(CultureInfo.InvariantCulture)));

            List<global::LargeFolderFinder.FolderInfo> children = node.Children;
            if (children == null)
            {
                continue;
            }

            foreach (global::LargeFolderFinder.FolderInfo child in children)
            {
                // 要約値を作らないときは相対パスを組み立てない。数えるだけなら要らず、
                // 数百万ノードでは文字列の確保がそのままメモリの押し上げになるためである。
                string childPath = lines == null
                    ? string.Empty
                    : pending.RelativePath.Length == 0
                        ? child.Name
                        : pending.RelativePath + PathSeparator + child.Name;
                stack.Push(new PendingNode(child, childPath));
            }
        }

        string? value = null;
        if (lines != null)
        {
            lines.Sort(StringComparer.Ordinal);
            value = HashLines(lines);
        }

        return new ResultDigest(value, folderCount, fileCount, root.Size);
    }

    /// <summary>
    /// 並べ替えた一覧を1行ずつ混ぜ、SHA-256 の先頭16文字を小文字の16進で返す。
    /// </summary>
    /// <param name="lines">序数順に並べ終えた1行分の一覧。</param>
    private static string HashLines(List<string> lines)
    {
        // 一覧全体を1本の文字列に連結すると数百万行で巨大な確保になるため、1行ずつ混ぜる。
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string line in lines)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(line));
            hash.AppendData(LineTerminator);
        }

        byte[] digest = hash.GetHashAndReset();
        return Convert.ToHexString(digest).Substring(0, DigestLength).ToLowerInvariant();
    }

    /// <summary>行の区切り。行の境目をあいまいにしないために1行ごとに混ぜる。</summary>
    private static readonly byte[] LineTerminator = new byte[] { (byte)'\n' };

    /// <summary>辿る途中のノードと、そのノードのルートからの相対パス。</summary>
    private readonly struct PendingNode
    {
        public PendingNode(global::LargeFolderFinder.FolderInfo node, string relativePath)
        {
            Node = node;
            RelativePath = relativePath;
        }

        /// <summary>辿る対象のノード。</summary>
        public global::LargeFolderFinder.FolderInfo Node { get; }

        /// <summary>ルートからの相対パス。ルート自身は空文字。</summary>
        public string RelativePath { get; }
    }
}
