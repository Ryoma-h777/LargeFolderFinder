using System;
using System.Collections.Generic;

namespace LargeFolderFinder.GoldenBaseline.Model;

/// <summary>
/// 期待値データのヘッダを表す不変のデータ型。
/// 走査条件のメタデータ（形式のバージョン、基準の論理名、生成日時、物理サイズ換算の有無、
/// 換算に用いたクラスタサイズ、フィクスチャの完全性と未生成項目）を保持する（要件2.5, 3.7, 6.1, 6.4）。
/// </summary>
public sealed class GoldenHeader
{
    /// <summary>期待値データの形式バージョン。</summary>
    public int FormatVersion { get; }

    /// <summary>基準フォルダの論理名。実パスではなく論理名を用い、環境差を持ち込まない。</summary>
    public string BaseFolderLabel { get; }

    /// <summary>期待値データの生成日時。</summary>
    public DateTimeOffset GeneratedAt { get; }

    /// <summary>物理サイズ換算を適用したかどうか。</summary>
    public bool UsePhysicalSize { get; }

    /// <summary>物理サイズ換算に用いたクラスタサイズ（バイト）。UsePhysicalSize が false のときは 0。</summary>
    public long ClusterSizeInBytes { get; }

    /// <summary>フィクスチャが完全に生成されたかどうか。</summary>
    public bool FixtureComplete { get; }

    /// <summary>フィクスチャの生成時に生成できなかった項目の一覧。</summary>
    public IReadOnlyList<string> FixtureOmissions { get; }

    /// <summary>
    /// GoldenHeader を構築する。
    /// </summary>
    public GoldenHeader(
        int formatVersion,
        string baseFolderLabel,
        DateTimeOffset generatedAt,
        bool usePhysicalSize,
        long clusterSizeInBytes,
        bool fixtureComplete,
        IReadOnlyList<string> fixtureOmissions)
    {
        if (string.IsNullOrEmpty(baseFolderLabel))
        {
            throw new ArgumentException("基準の論理名が空です。", nameof(baseFolderLabel));
        }

        if (clusterSizeInBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clusterSizeInBytes), clusterSizeInBytes, "クラスタサイズは0以上である必要があります。");
        }

        FormatVersion = formatVersion;
        BaseFolderLabel = baseFolderLabel;
        GeneratedAt = generatedAt;
        UsePhysicalSize = usePhysicalSize;
        ClusterSizeInBytes = clusterSizeInBytes;
        FixtureComplete = fixtureComplete;
        FixtureOmissions = fixtureOmissions ?? throw new ArgumentNullException(nameof(fixtureOmissions));
    }
}
