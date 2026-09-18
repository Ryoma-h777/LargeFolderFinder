using System;
using System.Collections.Generic;

namespace LargeFolderFinder.LocalizationCheck.SelfCheck;

/// <summary>
/// 1件の自己検証項目の実行結果を表す不変のデータ型。
/// </summary>
public sealed class CheckOutcome
{
    /// <summary>検証項目の名前。</summary>
    public string Name { get; }

    /// <summary>検証が成功したかどうか。</summary>
    public bool Passed { get; }

    /// <summary>失敗した場合の理由。成功した場合は null。</summary>
    public string? FailureReason { get; }

    public CheckOutcome(string name, bool passed, string? failureReason)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("検証項目の名前が空です。", nameof(name));
        }

        Name = name;
        Passed = passed;
        FailureReason = failureReason;
    }
}

/// <summary>
/// 検証項目の中で条件の成否を宣言するための最小限のアサーションヘルパー。
/// 外部のテストフレームワークには依存しない。
/// </summary>
internal static class SelfAssert
{
    /// <summary>
    /// 条件が偽であれば、message を理由として例外を送出する。
    /// <see cref="SelfCheckRunner"/> はこの例外を捕捉し、検証項目の失敗として扱う。
    /// </summary>
    public static void That(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

/// <summary>
/// 外部パッケージに依存しない、最小限の自己検証ハーネス。
/// 検証項目（名前と条件判定）を登録し、登録順に実行して結果を集める。
/// </summary>
public sealed class SelfCheckRunner
{
    private readonly List<(string Name, Action Assertion)> _checks = new();

    /// <summary>
    /// 検証項目を登録する。assertion が例外を送出した場合、その項目は失敗として扱われる。
    /// </summary>
    public void Add(string name, Action assertion)
    {
        _checks.Add((name, assertion));
    }

    /// <summary>
    /// 登録済みの検証項目をすべて登録順に実行し、成否を含む結果の一覧を返す。
    /// 1件の失敗が残りの項目の実行を妨げることはない。
    /// </summary>
    public IReadOnlyList<CheckOutcome> RunAll()
    {
        var outcomes = new List<CheckOutcome>(_checks.Count);

        foreach (var (name, assertion) in _checks)
        {
            try
            {
                assertion();
                outcomes.Add(new CheckOutcome(name, true, null));
            }
            catch (Exception ex)
            {
                outcomes.Add(new CheckOutcome(name, false, ex.Message));
            }
        }

        return outcomes;
    }
}

/// <summary>
/// 自己検証項目の登録をまとめる場所（design.md: Components and Interfaces / SelfChecks）。
/// </summary>
/// <remarks>
/// 判定と報告の部品（PlaceholderParser、LanguageFileReader、CoverageChecker、ReportWriter）が
/// 実装され次第、対応する検証項目をここに追加していく（tasks.md: 2.1〜2.4）。
/// 部品が1つも無い現時点では登録する項目は0件であり、selfcheck は0件成功として終了コード 0 を返す。
/// </remarks>
internal static class SelfChecks
{
    public static void Register(SelfCheckRunner runner)
    {
        if (runner == null)
        {
            throw new ArgumentNullException(nameof(runner));
        }
    }
}
