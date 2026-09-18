using System;
using System.Collections.Generic;
using LargeFolderFinder.LocalizationCheck.Model;

namespace LargeFolderFinder.LocalizationCheck.Io;

/// <summary>
/// 検証結果を、人が読める1問題1行の報告と要約に整形する部品
/// （design.md: Components and Interfaces / Io / ReportWriter）。
/// </summary>
/// <remarks>
/// <para>
/// 入出力の層に属するが、ファイルにもコンソールにも書かず、行の文字列の一覧を返すだけにしている。
/// どこへ出すかを決めるのは入口（Program）の役目であり、この部品は入口を参照しない
/// （design.md: Architecture Integration「依存の向き」）。
/// </para>
/// <para>
/// 問題の並びは <see cref="CheckReport.Problems"/> のまま変えない。並びを決めるのは判定の層
/// （CoverageChecker）であり、ここで並べ直すと不変条件の責任が二重になる
/// （design.md: Model / CheckReport の不変条件）。
/// </para>
/// </remarks>
public static class ReportWriter
{
    /// <summary>
    /// 英語が基準として使えないときに、要約の直前に出す行（design.md: Io / ReportWriter の要約）。
    /// </summary>
    private const string ReferenceUnusableLine =
        "[検証不能] en.yaml が無いか読み込めないため、差し込み位置を検証できません";

    /// <summary>報告の各行を返す。最終行は要約。</summary>
    /// <param name="report">整形する検証結果。</param>
    /// <returns>
    /// 1件の問題につき1行と、最終行の要約。英語が基準として使えないときは、
    /// 要約の直前に検証不能の行が入る。
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="report"/> が null の場合。</exception>
    public static IReadOnlyList<string> Format(CheckReport report)
    {
        if (report == null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        // 問題の行 + 検証不能の行（あれば）+ 要約の1行。
        var lines = new List<string>(report.Problems.Count + 2);

        // 最初の問題で打ち切らず、渡されたすべての問題を1行ずつ出す（requirements.md: 3.9）。
        foreach (var problem in report.Problems)
        {
            lines.Add(FormatProblem(problem));
        }

        if (!report.IsReferenceUsable)
        {
            lines.Add(ReferenceUnusableLine);
        }

        lines.Add(FormatSummary(report));

        return lines.AsReadOnly();
    }

    /// <summary>
    /// 1件の問題を1行に整形する（design.md: Io / ReportWriter の行の形式）。
    /// </summary>
    /// <remarks>
    /// 形は種類によらず <c>[種類] ファイル名: 本文</c> で共通とし、本文だけが種類で変わる。
    /// 読み込み不能はキーを持たず理由だけ、重複・欠落・孤児はキーだけ、
    /// 差し込み位置はキーに続けて英語側と当該言語側の組が並ぶ。
    /// </remarks>
    /// <param name="problem">整形する問題。</param>
    /// <returns>報告の1行。</returns>
    private static string FormatProblem(Problem problem)
    {
        return $"[{LabelOf(problem.Kind)}] {problem.FileName}: {BodyOf(problem)}";
    }

    /// <summary>
    /// 問題の行の本文を組み立てる。キーと補足のうち、在るものだけを空白で連ねる。
    /// </summary>
    /// <param name="problem">整形する問題。</param>
    /// <returns>行の本文。キーも補足も無いときは、行が尻切れにならないよう「（詳細なし）」とする。</returns>
    private static string BodyOf(Problem problem)
    {
        var parts = new List<string>(2);

        if (!string.IsNullOrEmpty(problem.Key))
        {
            parts.Add(problem.Key!);
        }

        if (!string.IsNullOrEmpty(problem.Detail))
        {
            parts.Add(problem.Detail!);
        }

        if (parts.Count == 0)
        {
            return "（詳細なし）";
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// 問題の種類を、報告に載せる日本語の見出しに直す。
    /// </summary>
    /// <param name="kind">問題の種類。</param>
    /// <returns>見出しの語。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未知の種類が渡された場合。</exception>
    private static string LabelOf(ProblemKind kind)
    {
        switch (kind)
        {
            case ProblemKind.Unreadable:
                return "読み込み不能";
            case ProblemKind.Duplicate:
                return "重複";
            case ProblemKind.Missing:
                return "欠落";
            case ProblemKind.Orphan:
                return "孤児";
            case ProblemKind.PlaceholderMismatch:
                return "差し込み位置";
            default:
                // 種類を増やしたときに、見出しの追加を忘れたことをその場で気づけるようにする。
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知の問題の種類です。");
        }
    }

    /// <summary>
    /// 要約の行を組み立てる（requirements.md: 3.6）。
    /// </summary>
    /// <param name="report">整形する検証結果。</param>
    /// <returns>
    /// 問題が無いときは「問題はありません（言語 13、キー 81）」、
    /// 問題があるときは「問題 N 件（言語 13、キー 81）」の形の1行。
    /// </returns>
    private static string FormatSummary(CheckReport report)
    {
        // 検証した範囲（言語の数とキーの数）は、問題の有無にかかわらず常に添える。
        // 「問題なし」が、検証できていないことの裏返しでないことを読み手が確かめられるようにするため。
        var scope = $"（言語 {report.LanguageCount}、キー {report.KeyCount}）";

        if (report.Problems.Count == 0)
        {
            return $"問題はありません{scope}";
        }

        return $"問題 {report.Problems.Count} 件{scope}";
    }
}
