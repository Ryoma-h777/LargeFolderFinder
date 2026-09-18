using System;
using System.Collections.Generic;

namespace LargeFolderFinder.LocalizationCheck.Model;

/// <summary>
/// 問題の種類。報告の並び順もこの順とする（design.md: Model / CheckReport）。
/// </summary>
public enum ProblemKind
{
    /// <summary>読み込み不能（requirements.md 3.5）。</summary>
    Unreadable,

    /// <summary>同じキーが2回以上現れる（requirements.md 3.10）。</summary>
    Duplicate,

    /// <summary>LanguageKey のキーが言語ファイルに無い（requirements.md 3.2）。</summary>
    Missing,

    /// <summary>LanguageKey に無いキーが言語ファイルにある（requirements.md 3.3）。</summary>
    Orphan,

    /// <summary>差し込み位置の番号の組が英語と異なる（requirements.md 3.4）。</summary>
    PlaceholderMismatch,
}

/// <summary>
/// 検証で見つかった1件の問題を表す不変のデータ型。
/// </summary>
public sealed class Problem
{
    /// <summary>問題の種類。</summary>
    public ProblemKind Kind { get; }

    /// <summary>問題が見つかった言語ファイルの名前（例: "de.yaml"）。</summary>
    public string FileName { get; }

    /// <summary>対象のキーの名前。ファイル全体の問題（読み込み不能）のときは null。</summary>
    public string? Key { get; }

    /// <summary>
    /// 補足の情報。読み込み不能のときはその理由、差し込み位置のずれのときは英語側と当該言語側の組。
    /// 補足が不要な種類（欠落、孤児、重複）のときは null。
    /// </summary>
    public string? Detail { get; }

    /// <param name="kind">問題の種類。</param>
    /// <param name="fileName">言語ファイルの名前。空にはできない。</param>
    /// <param name="key">対象のキーの名前。ファイル全体の問題のときは null。</param>
    /// <param name="detail">補足の情報。不要なときは null。</param>
    public Problem(ProblemKind kind, string fileName, string? key, string? detail)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            throw new ArgumentException("言語ファイルの名前が空です。", nameof(fileName));
        }

        Kind = kind;
        FileName = fileName;
        Key = key;
        Detail = detail;
    }
}

/// <summary>
/// 検証結果全体を表す不変のデータ型。見つかった問題と、検証した範囲を保持する。
/// </summary>
/// <remarks>
/// 不変条件: <see cref="Problems"/> は同じ内容の入力に対して常に同じ並びになる
/// （ファイル名の序数順 → 種類 → LanguageKey の定義順。孤児はキー名の序数順）。
/// 並びを決めるのは判定の層（CoverageChecker）であり、この型は渡された並びをそのまま保つ。
/// </remarks>
public sealed class CheckReport
{
    /// <summary>見つかった問題の一覧。問題が無いときは空。</summary>
    public IReadOnlyList<Problem> Problems { get; }

    /// <summary>検証した言語ファイルの数（読み込み不能だったものを含む）。</summary>
    public int LanguageCount { get; }

    /// <summary>LanguageKey が定義するキーの数。</summary>
    public int KeyCount { get; }

    /// <summary>差し込み位置の基準となる英語のファイルが存在し、読み込めたかどうか。</summary>
    public bool IsReferenceUsable { get; }

    /// <param name="problems">見つかった問題の一覧（並びは呼び出し側が決める）。</param>
    /// <param name="languageCount">検証した言語ファイルの数。</param>
    /// <param name="keyCount">LanguageKey が定義するキーの数。</param>
    /// <param name="isReferenceUsable">英語のファイルが基準として使えたかどうか。</param>
    public CheckReport(IReadOnlyList<Problem> problems, int languageCount, int keyCount, bool isReferenceUsable)
    {
        if (problems == null)
        {
            throw new ArgumentNullException(nameof(problems));
        }

        if (languageCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(languageCount), "言語ファイルの数が負の値です。");
        }

        if (keyCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(keyCount), "キーの数が負の値です。");
        }

        // 渡された一覧を複製して保持し、生成後に外部から並びや要素を変えられないようにする。
        Problems = new List<Problem>(problems).AsReadOnly();
        LanguageCount = languageCount;
        KeyCount = keyCount;
        IsReferenceUsable = isReferenceUsable;
    }
}
