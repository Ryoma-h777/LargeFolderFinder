using System;
using System.IO;

namespace LargeFolderFinder.GoldenBaseline.Io;

/// <summary>
/// 通常のパスを拡張長形式（\\?\ プレフィクス）へ変換する唯一の箇所（design.md: Io/LongPath）。
/// レジストリの LongPathsEnabled と exe の longPathAware マニフェストの両方を迂回できるのは
/// \\?\ プレフィクスのみであり、net48 でも通常の System.IO API にそのまま渡すだけで動作する
/// （research.md: 長いパスのフィクスチャをどう生成するか）。
/// この部品以外は拡張長パスを扱わない。期待値データに記録する相対パスにも決して現れない。
/// </summary>
public static class LongPath
{
    /// <summary>拡張長パスのプレフィクス。</summary>
    private const string ExtendedPrefix = @"\\?\";

    /// <summary>拡張長UNCパスのプレフィクス。</summary>
    private const string ExtendedUncPrefix = @"\\?\UNC\";

    /// <summary>通常のUNCパスのプレフィクス。</summary>
    private const string UncPrefix = @"\\";

    /// <summary>
    /// 絶対パスへ正規化したうえで拡張長プレフィクスを付与する。
    /// \\?\ 自体は正規化されないため、必ず正規化を先に行ってから付与する
    /// （順序を逆にすると付与が無効になる。research.md参照）。
    /// </summary>
    /// <param name="path">変換対象のパス。空であってはならない。相対表記（. や ..）を含んでいてもよい。</param>
    /// <returns>\\?\ または \\?\UNC\ で始まる拡張長パス。</returns>
    public static string Extend(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("パスが空です。", nameof(path));
        }

        // 既に拡張長形式（\\?\ または \\?\UNC\）であれば、二重付与を避けてそのまま返す。
        // \\?\ は正規化されないため、既に付与済みのパスを再度 GetFullPath に通してはならない。
        if (path.StartsWith(ExtendedPrefix, StringComparison.Ordinal))
        {
            return path;
        }

        // 正規化は \\?\ を付与する前に必ず行う。
        string fullPath = Path.GetFullPath(path);

        if (fullPath.StartsWith(UncPrefix, StringComparison.Ordinal))
        {
            // UNC パス（\\server\share\...）は \\?\UNC\server\share\... の形式にする。
            return ExtendedUncPrefix + fullPath.Substring(UncPrefix.Length);
        }

        return ExtendedPrefix + fullPath;
    }
}
