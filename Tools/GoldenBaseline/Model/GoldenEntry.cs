using System;

namespace LargeFolderFinder.GoldenBaseline.Model;

/// <summary>
/// 期待値データのエントリ種別（ファイルまたはフォルダ）を表す。
/// </summary>
public enum GoldenEntryKind
{
    /// <summary>フォルダ。</summary>
    Folder,

    /// <summary>ファイル。</summary>
    File
}

/// <summary>
/// 期待値データの1エントリを表す不変のデータ型。
/// 基準フォルダからの相対パス・種別・バイト単位のサイズの3項目のみを保持する。
/// 更新日時と所有者は実行環境によって変わり偽陽性を生むため、型として持たせない（要件1.1, 1.2, 1.5）。
/// </summary>
public sealed class GoldenEntry
{
    /// <summary>基準フォルダからの相対パス。区切りは \ に統一する。</summary>
    public string RelativePath { get; }

    /// <summary>エントリの種別（ファイルまたはフォルダ）。</summary>
    public GoldenEntryKind Kind { get; }

    /// <summary>バイト単位のサイズ。64ビット整数で保持し、表示単位への変換は行わない。</summary>
    public long SizeInBytes { get; }

    /// <summary>
    /// GoldenEntry を構築する。
    /// </summary>
    /// <param name="relativePath">基準フォルダからの相対パス。</param>
    /// <param name="kind">エントリの種別。</param>
    /// <param name="sizeInBytes">バイト単位のサイズ。0以上である必要がある。</param>
    public GoldenEntry(string relativePath, GoldenEntryKind kind, long sizeInBytes)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            throw new ArgumentException("相対パスが空です。", nameof(relativePath));
        }

        if (sizeInBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeInBytes), sizeInBytes, "サイズは0以上である必要があります。");
        }

        RelativePath = relativePath;
        Kind = kind;
        SizeInBytes = sizeInBytes;
    }
}
