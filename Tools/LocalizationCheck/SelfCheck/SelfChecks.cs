using System;
using System.Collections.Generic;
using System.Linq;
using LargeFolderFinder.LocalizationCheck.Check;
using LargeFolderFinder.LocalizationCheck.Io;
using LargeFolderFinder.LocalizationCheck.Model;

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
        RegisterLanguageFileReaderChecks(runner);
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

    /// <summary>
    /// LanguageFileReader の検証項目を登録する
    /// （design.md: Testing Strategy / Unit Tests 2、requirements.md: 3.5、3.10、4.4）。
    /// </summary>
    /// <remarks>
    /// 期待する結果は、設計時に YamlDotNet 16.3.0 で実測した挙動
    /// （research.md「YamlDotNet の読み込みの挙動（実測）」）に一致させている。
    /// ファイルを作らず、合成した文字列を <see cref="LanguageFileReader.Parse"/> に渡して確かめる。
    /// </remarks>
    private static void RegisterLanguageFileReaderChecks(SelfCheckRunner runner)
    {
        runner.Add(
            "LanguageFileReader: 正常な文書をキーと訳文の対として読み込む",
            () =>
            {
                var content = LanguageFileReader.Parse(
                    "en.yaml",
                    "Title: \"Large Folder Finder\"\nMenuFile: \"File\"\n");

                AssertReadable(content);
                SelfAssert.That(
                    content.FileName == "en.yaml",
                    $"ファイル名は en.yaml を期待しましたが、{content.FileName} でした。");
                AssertEntryCount(content, 2);
                AssertEntry(content, "Title", "Large Folder Finder");
                AssertEntry(content, "MenuFile", "File");
                AssertNoDuplicates(content);
            });

        runner.Add(
            "LanguageFileReader: 数値に見える値も文字列として読み込む",
            () =>
            {
                var content = LanguageFileReader.Parse("de.yaml", "A: 12\n");

                AssertReadable(content);
                AssertEntry(content, "A", "12");
                AssertNoDuplicates(content);
            });

        runner.Add(
            "LanguageFileReader: 構文の誤りを読み込み不能として理由に型名を残す",
            () =>
            {
                var content = LanguageFileReader.Parse(
                    "broken.yaml",
                    "A: \"unterminated\nB: [1, 2\n");

                AssertUnreadable(content, "SyntaxErrorException");
                AssertNoDuplicates(content);
            });

        runner.Add(
            "LanguageFileReader: 空の文書とコメントだけの文書を「中身が空」として読み込み不能にする",
            () =>
            {
                // 実測では、いずれも例外にはならず読み込み結果が null になる。
                var emptyContent = LanguageFileReader.Parse("empty.yaml", string.Empty);
                AssertUnreadable(emptyContent, "中身が空");
                AssertNoDuplicates(emptyContent);

                var commentOnlyContent = LanguageFileReader.Parse("comment.yaml", "# コメントだけ\n");
                AssertUnreadable(commentOnlyContent, "中身が空");
                AssertNoDuplicates(commentOnlyContent);
            });

        runner.Add(
            "LanguageFileReader: 入れ子の構造を読み込み不能として理由に型名を残す",
            () =>
            {
                var content = LanguageFileReader.Parse("nested.yaml", "A:\n  B: \"1\"\n");

                AssertUnreadable(content, "YamlException");
                // 入れ子でも最上位のキーは A の1回だけなので、重複としては数えない。
                AssertNoDuplicates(content);
            });

        runner.Add(
            "LanguageFileReader: 重複キーは読み込みに成功したうえで重複として数える",
            () =>
            {
                var content = LanguageFileReader.Parse(
                    "dup.yaml",
                    "A: \"1\"\nB: \"2\"\nA: \"3\"\n");

                // 実測どおり、読み込みは成功し、後に書かれた値が残る。
                AssertReadable(content);
                AssertEntryCount(content, 2);
                AssertEntry(content, "A", "3");
                AssertEntry(content, "B", "2");
                AssertDuplicates(content, "A");
            });

        runner.Add(
            "LanguageFileReader: 3回以上現れたキーも1件の重複として1回だけ挙げる",
            () =>
            {
                var content = LanguageFileReader.Parse(
                    "dup3.yaml",
                    "A: \"1\"\nA: \"2\"\nB: \"3\"\nA: \"4\"\n");

                AssertReadable(content);
                AssertEntry(content, "A", "4");
                AssertDuplicates(content, "A");
            });

        runner.Add(
            "LanguageFileReader: 複数のキーが重複したら出現順に挙げる",
            () =>
            {
                var content = LanguageFileReader.Parse(
                    "dup2.yaml",
                    "B: \"1\"\nA: \"2\"\nB: \"3\"\nA: \"4\"\n");

                AssertReadable(content);
                AssertDuplicates(content, "B", "A");
            });

        runner.Add(
            "LanguageFileReader: 別のキー名と同じ訳文をキーと数えない",
            () =>
            {
                // 訳文の文字列が別のキー名と一致する形。重複の数え上げで値を読み飛ばさないと、
                // この訳文「B」をキーとして数えてしまい、B が重複として誤って挙がる。
                // 実データの言語ファイルでも起きる形のため、その分岐をここで押さえる。
                var content = LanguageFileReader.Parse(
                    "value-as-key.yaml",
                    "A: \"B\"\nB: \"C\"\n");

                AssertReadable(content);
                AssertEntryCount(content, 2);
                AssertEntry(content, "A", "B");
                AssertEntry(content, "B", "C");
                AssertNoDuplicates(content);
            });

        runner.Add(
            "LanguageFileReader: 入れ子の中の重複は数えない（最上位のキーだけを数える）",
            () =>
            {
                // 入れ子の中で B が2回現れるが、数える対象は最上位のキー（A の1回だけ）なので重複は無い。
                var content = LanguageFileReader.Parse("nested-dup.yaml", "A:\n  B: 1\n  B: 2\n");

                AssertUnreadable(content, "YamlException");
                AssertNoDuplicates(content);
            });
    }

    /// <summary>読み込みに成功していることを確かめる。</summary>
    private static void AssertReadable(LanguageFileContent content)
    {
        SelfAssert.That(
            content.Entries != null,
            $"{content.FileName} は読み込めるはずですが、読み込み不能でした（理由: {content.UnreadableReason}）。");
    }

    /// <summary>読み込み不能で、その理由に指定の語が含まれることを確かめる。</summary>
    /// <param name="content">確かめる読み込み結果。</param>
    /// <param name="expectedReasonPart">理由に含まれるべき語（例外の型名など）。</param>
    private static void AssertUnreadable(LanguageFileContent content, string expectedReasonPart)
    {
        SelfAssert.That(
            content.Entries == null,
            $"{content.FileName} は読み込み不能になるはずですが、読み込めてしまいました。");

        SelfAssert.That(
            content.UnreadableReason != null && content.UnreadableReason.Contains(expectedReasonPart),
            $"{content.FileName} の読み込み不能の理由には「{expectedReasonPart}」を期待しましたが、" +
            $"「{content.UnreadableReason}」でした。");
    }

    /// <summary>キーと訳文の対の数を確かめる。</summary>
    private static void AssertEntryCount(LanguageFileContent content, int expectedCount)
    {
        SelfAssert.That(
            content.Entries != null && content.Entries.Count == expectedCount,
            $"{content.FileName} のキーの数は {expectedCount} を期待しましたが、" +
            $"{content.Entries?.Count.ToString() ?? "（読み込み不能）"} でした。");
    }

    /// <summary>指定のキーの訳文が期待どおりであることを確かめる。</summary>
    private static void AssertEntry(LanguageFileContent content, string key, string expectedValue)
    {
        SelfAssert.That(
            content.Entries != null && content.Entries.TryGetValue(key, out var actual) && actual == expectedValue,
            $"{content.FileName} のキー {key} の訳文は「{expectedValue}」を期待しましたが、" +
            $"「{GetEntryOrNull(content, key) ?? "（無し）"}」でした。");
    }

    /// <summary>重複キーが1件も無いことを確かめる。</summary>
    private static void AssertNoDuplicates(LanguageFileContent content)
    {
        SelfAssert.That(
            content.DuplicateKeys.Count == 0,
            $"{content.FileName} に重複キーは無いはずですが、" +
            $"[{string.Join(",", content.DuplicateKeys)}] が挙がりました。");
    }

    /// <summary>重複キーが、期待する並び（出現順）と一致することを確かめる。</summary>
    private static void AssertDuplicates(LanguageFileContent content, params string[] expected)
    {
        SelfAssert.That(
            content.DuplicateKeys.SequenceEqual(expected),
            $"{content.FileName} の重複キーは [{string.Join(",", expected)}] を期待しましたが、" +
            $"[{string.Join(",", content.DuplicateKeys)}] でした。");
    }

    /// <summary>失敗時の説明に使うため、キーに対応する訳文を取り出す。無ければ null。</summary>
    private static string? GetEntryOrNull(LanguageFileContent content, string key)
    {
        if (content.Entries != null && content.Entries.TryGetValue(key, out var value))
        {
            return value;
        }

        return null;
    }
}
