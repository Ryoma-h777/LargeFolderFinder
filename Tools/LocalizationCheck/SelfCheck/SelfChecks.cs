using System;
using System.Collections.Generic;
using System.Linq;
using LargeFolderFinder.LocalizationCheck.Check;

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
/// 検証項目が宣言した条件が満たされなかったことを表す例外。
/// 検証項目の中で起きた「想定どおりの失敗」と「予期しない例外」を
/// <see cref="SelfCheckRunner"/> が区別できるようにするために用意している。
/// </summary>
internal sealed class SelfCheckFailedException : Exception
{
    public SelfCheckFailedException(string message) : base(message)
    {
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
            throw new SelfCheckFailedException(message);
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
            catch (SelfCheckFailedException ex)
            {
                // 検証項目が宣言した条件を満たさなかった場合。理由はそのまま読める文になっている。
                outcomes.Add(new CheckOutcome(name, false, ex.Message));
            }
            catch (Exception ex)
            {
                // 予期しない例外。原因を追えるよう、理由に例外の型名を添える。
                outcomes.Add(new CheckOutcome(name, false, $"予期しない例外 {ex.GetType().Name}: {ex.Message}"));
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
/// </remarks>
internal static class SelfChecks
{
    public static void Register(SelfCheckRunner runner)
    {
        if (runner == null)
        {
            throw new ArgumentNullException(nameof(runner));
        }

        RegisterPlaceholderParserChecks(runner);
    }

    /// <summary>
    /// PlaceholderParser の検証項目を登録する
    /// （design.md: Testing Strategy / Unit Tests 1、requirements.md: 2.2、3.4）。
    /// </summary>
    private static void RegisterPlaceholderParserChecks(SelfCheckRunner runner)
    {
        runner.Add(
            "PlaceholderParser: 番号だけの差し込み位置を認識する",
            () => AssertIndexes("{0}", 0));

        runner.Add(
            "PlaceholderParser: 書式付きの差し込み位置を認識する",
            () => AssertIndexes("{1:F0}", 1));

        runner.Add(
            "PlaceholderParser: 幅付きの差し込み位置を認識する",
            () =>
            {
                AssertIndexes("{2,10}", 2);
                // 左寄せを表す負の幅も幅として扱う。
                AssertIndexes("{2,-10}", 2);
            });

        runner.Add(
            "PlaceholderParser: 幅と書式付きの差し込み位置を認識する",
            () => AssertIndexes("{3,-10:F1}", 3));

        runner.Add(
            "PlaceholderParser: 二重の波括弧はエスケープとして無視する",
            () =>
            {
                AssertIndexes("{{0}}");
                AssertIndexes("{{}}");
                // エスケープの内側に本物の差し込み位置がある場合は、その番号だけを取り出す。
                AssertIndexes("{{{0}}}", 0);
            });

        runner.Add(
            "PlaceholderParser: 同じ番号が複数回現れても重複を除く",
            () => AssertIndexes("{0} / {0} / {1}", 0, 1));

        runner.Add(
            "PlaceholderParser: 差し込み位置が無ければ空を返す",
            () =>
            {
                AssertIndexes("スキャン中……");
                AssertIndexes(string.Empty);
            });

        runner.Add(
            "PlaceholderParser: 出現順によらず番号を昇順で返す",
            () => AssertIndexes("{2} {0} {1}", 0, 1, 2));

        runner.Add(
            "PlaceholderParser: 設計の例（書式付きを含む状態表示）の番号の組を返す",
            () => AssertIndexes("{0} {1:F0}%  [{2}]  [{3}]", 0, 1, 2, 3));

        runner.Add(
            "PlaceholderParser: 設計の例（区切りの無い連続した差し込み位置）の番号の組を返す",
            () => AssertIndexes("Noch etwa {0}{1} {2}{3}", 0, 1, 2, 3));
    }

    /// <summary>
    /// <see cref="PlaceholderParser.ParseIndexes"/> の結果が、期待する番号の並びと
    /// 順序も含めて一致することを確かめる。
    /// </summary>
    /// <param name="text">検証する訳文。</param>
    /// <param name="expected">期待する差し込み位置の番号（重複なし・昇順）。</param>
    private static void AssertIndexes(string text, params int[] expected)
    {
        var actual = PlaceholderParser.ParseIndexes(text);

        SelfAssert.That(
            actual.SequenceEqual(expected),
            $"訳文「{text}」の差し込み位置は [{string.Join(",", expected)}] を期待しましたが、" +
            $"[{string.Join(",", actual)}] でした。");
    }
}
