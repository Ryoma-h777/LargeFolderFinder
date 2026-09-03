using System;
using System.Collections.Generic;

namespace LargeFolderFinder.GoldenBaseline.Model;

/// <summary>
/// ヘッダとエントリ集合を束ねる、期待値データ全体を表す不変のデータ型。
/// </summary>
public sealed class GoldenDocument
{
    /// <summary>走査条件のメタデータ。</summary>
    public GoldenHeader Header { get; }

    /// <summary>期待値エントリの一覧。並び順は Serializer が保証する。</summary>
    public IReadOnlyList<GoldenEntry> Entries { get; }

    /// <summary>
    /// GoldenDocument を構築する。
    /// </summary>
    /// <param name="header">走査条件のメタデータ。</param>
    /// <param name="entries">期待値エントリの一覧。RelativePath が重複してはならない。</param>
    public GoldenDocument(GoldenHeader header, IReadOnlyList<GoldenEntry> entries)
    {
        Header = header ?? throw new ArgumentNullException(nameof(header));
        if (entries is null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        // 不変条件: 同一の RelativePath が重複して現れない。
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!seen.Add(entry.RelativePath))
            {
                throw new ArgumentException($"相対パス '{entry.RelativePath}' が重複しています。", nameof(entries));
            }
        }

        Entries = entries;
    }
}
