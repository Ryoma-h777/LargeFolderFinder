using System;
using System.Collections.Generic;

namespace LargeFolderFinder.GoldenBaseline.Compare;

/// <summary>
/// 差分の種別（design.md: Compare/DiffReport - State Management）。
/// </summary>
public enum DiffKind
{
    /// <summary>期待値と実測でサイズが一致しない。</summary>
    SizeMismatch,

    /// <summary>期待値データに存在して実測（走査結果）に存在しない（欠落）。</summary>
    Missing,

    /// <summary>実測（走査結果）に存在して期待値データに存在しない（新規）。</summary>
    Unexpected,

    /// <summary>期待値と実測で種別（ファイルまたはフォルダ）が一致しない。</summary>
    KindMismatch
}

/// <summary>
/// 1件の差分を表す不変のデータ型（design.md: Compare/DiffReport - State Management）。
/// </summary>
public sealed class DiffEntry
{
    /// <summary>差分の種別。</summary>
    public DiffKind Kind { get; }

    /// <summary>基準フォルダからの相対パス。</summary>
    public string RelativePath { get; }

    /// <summary>期待された値の文字列表現。該当しない場合は空文字。</summary>
    public string ExpectedValue { get; }

    /// <summary>実際の値の文字列表現。該当しない場合は空文字。</summary>
    public string ActualValue { get; }

    /// <summary>
    /// DiffEntry を構築する。
    /// </summary>
    /// <param name="kind">差分の種別。</param>
    /// <param name="relativePath">基準フォルダからの相対パス。</param>
    /// <param name="expectedValue">期待された値の文字列表現。該当しない場合は空文字。</param>
    /// <param name="actualValue">実際の値の文字列表現。該当しない場合は空文字。</param>
    public DiffEntry(DiffKind kind, string relativePath, string expectedValue, string actualValue)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            throw new ArgumentException("相対パスが空です。", nameof(relativePath));
        }

        Kind = kind;
        RelativePath = relativePath;
        ExpectedValue = expectedValue ?? throw new ArgumentNullException(nameof(expectedValue));
        ActualValue = actualValue ?? throw new ArgumentNullException(nameof(actualValue));
    }
}

/// <summary>
/// 比較の判定結果（design.md: Compare/DiffReport - State Management）。
/// </summary>
public enum BaselineVerdict
{
    /// <summary>差分が1件もなく一致した。</summary>
    Match,

    /// <summary>走査条件は一致しているが、エントリに差分がある。</summary>
    Different,

    /// <summary>走査条件（物理サイズ換算の有無）が一致せず、エントリの突き合わせを行わなかった。</summary>
    SettingsMismatch
}

/// <summary>
/// 比較結果を表す不変のデータ型。判定結果と差分の全件を保持する（design.md: Compare/DiffReport）。
/// </summary>
public sealed class DiffReport
{
    /// <summary>判定結果。</summary>
    public BaselineVerdict Verdict { get; }

    /// <summary>差分の全件。差分がない場合、または設定不一致で突き合わせを行わなかった場合は空。</summary>
    public IReadOnlyList<DiffEntry> Entries { get; }

    /// <summary>
    /// DiffReport を構築する。
    /// </summary>
    /// <param name="verdict">判定結果。</param>
    /// <param name="entries">差分の全件。</param>
    public DiffReport(BaselineVerdict verdict, IReadOnlyList<DiffEntry> entries)
    {
        Verdict = verdict;
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
    }
}
