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
        RegisterCoverageCheckerChecks(runner);
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
            "LanguageFileReader: 値の無いキーは読み込みに成功し、訳文が null になる",
            () =>
            {
                // YAML で「Title:」と書いて値を省いた形。実測では読み込みは成功し、キーは存在し、
                // 値は空文字列ではなく null になる（2026-09-18 実測）。
                // CoverageChecker が「値が null のキーを欠落として扱う」判断はこの挙動に立っているため、
                // YamlDotNet の版が変わって挙動が変わったら、この項目で気づけるようにしておく。
                var content = LanguageFileReader.Parse("empty-value.yaml", "Title:\nMenuFile: \"File\"\n");

                AssertReadable(content);
                AssertEntryCount(content, 2);
                SelfAssert.That(
                    content.Entries!.ContainsKey("Title"),
                    "値を省いたキー Title は、読み込み結果に存在するはずです。");
                SelfAssert.That(
                    content.Entries["Title"] == null,
                    $"値を省いたキー Title の訳文は null を期待しましたが、" +
                    $"「{content.Entries["Title"]}」でした。");
                AssertEntry(content, "MenuFile", "File");
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
                var content = LanguageFileReader.Parse("nested.yaml", "Outer:\n  Inner: \"1\"\n");

                AssertUnreadable(content, "YamlException");
                // 入れ子でも最上位のキーは Outer の1回だけなので、重複としては数えない。
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
                // 入れ子の中で Inner が2回現れるが、数える対象は最上位のキー（Outer の1回だけ）なので重複は無い。
                var content = LanguageFileReader.Parse("nested-dup.yaml", "Outer:\n  Inner: 1\n  Inner: 2\n");

                AssertUnreadable(content, "YamlException");
                AssertNoDuplicates(content);
            });
    }

    /// <summary>
    /// 自己検証で使う「期待するキーの一覧」（LanguageKey の定義順に相当する）。
    /// </summary>
    /// <remarks>
    /// 並びをわざと辞書順とずらしてある（Title が MenuFile より前）。
    /// 欠落の並びが「キー名の順」ではなく「定義順」であることを確かめられるようにするため。
    /// </remarks>
    private static readonly string[] CoverageKeys = { "Title", "MenuFile", "TimeStatusFormat" };

    /// <summary>問題の無い英語の言語ファイルの内容。差し込み位置の基準に使う。</summary>
    private const string ReferenceText =
        "Title: \"Large Folder Finder\"\nMenuFile: \"File\"\nTimeStatusFormat: \"{0} {1}\"\n";

    /// <summary>
    /// CoverageChecker の検証項目を登録する
    /// （design.md: Testing Strategy / Unit Tests 3・4、requirements.md: 1.1、2.2、3.1〜3.5、3.9、3.10、4.3）。
    /// </summary>
    /// <remarks>
    /// ファイルは作らず、<see cref="LanguageFileReader.Parse"/> で合成した読み込み結果を渡す。
    /// 判定の中核がファイルシステムを扱わないことは、この呼び出し方自体が示している。
    /// </remarks>
    private static void RegisterCoverageCheckerChecks(SelfCheckRunner runner)
    {
        runner.Add(
            "CoverageChecker: 5種類の問題を1つずつ仕込むと、それぞれちょうど1件ずつ報告される",
            () =>
            {
                var report = CoverageChecker.Check(
                    CoverageKeys,
                    new[]
                    {
                        LanguageFileReader.Parse(CoverageChecker.ReferenceFileName, ReferenceText),
                        // 読み込み不能: 閉じていない引用符。
                        LanguageFileReader.Parse("broken.yaml", "Title: \"unterminated\nMenuFile: [1, 2\n"),
                        // 重複: Title が2回。
                        LanguageFileReader.Parse(
                            "dup.yaml",
                            "Title: \"T\"\nMenuFile: \"M\"\nTimeStatusFormat: \"{0} {1}\"\nTitle: \"T2\"\n"),
                        // 欠落: MenuFile が無い。
                        LanguageFileReader.Parse("missing.yaml", "Title: \"T\"\nTimeStatusFormat: \"{0} {1}\"\n"),
                        // 孤児: LanguageKey に無い OldKey がある。
                        LanguageFileReader.Parse(
                            "orphan.yaml",
                            "Title: \"T\"\nMenuFile: \"M\"\nTimeStatusFormat: \"{0} {1}\"\nOldKey: \"x\"\n"),
                        // 差し込み位置のずれ: {1} が無い。
                        LanguageFileReader.Parse(
                            "mismatch.yaml",
                            "Title: \"T\"\nMenuFile: \"M\"\nTimeStatusFormat: \"{0} Vergangen\"\n"),
                    });

                // ファイル名の序数順で並ぶ。mismatch.yaml は missing.yaml より前
                // （4文字目が m と s のため）。
                AssertProblems(
                    report,
                    "broken.yaml/Unreadable/-",
                    "dup.yaml/Duplicate/Title",
                    "mismatch.yaml/PlaceholderMismatch/TimeStatusFormat",
                    "missing.yaml/Missing/MenuFile",
                    "orphan.yaml/Orphan/OldKey");

                SelfAssert.That(
                    report.LanguageCount == 6,
                    $"検証した言語ファイルの数は 6 を期待しましたが、{report.LanguageCount} でした。");
                SelfAssert.That(
                    report.KeyCount == CoverageKeys.Length,
                    $"キーの数は {CoverageKeys.Length} を期待しましたが、{report.KeyCount} でした。");
                SelfAssert.That(
                    report.IsReferenceUsable,
                    "英語のファイルは読み込めているので、基準は使えるはずです。");
            });

        runner.Add(
            "CoverageChecker: 1件目の問題で打ち切らず、同じファイルの複数の問題をすべて報告する",
            () =>
            {
                var report = CoverageChecker.Check(
                    CoverageKeys,
                    new[]
                    {
                        LanguageFileReader.Parse(CoverageChecker.ReferenceFileName, ReferenceText),
                        // 1本のファイルに重複・欠落・孤児・差し込み位置のずれを同時に仕込む。
                        LanguageFileReader.Parse(
                            "de.yaml",
                            "MenuFile: \"M\"\nTimeStatusFormat: \"{0}\"\nMenuFile: \"M2\"\nOldKey: \"x\"\n"),
                    });

                AssertProblems(
                    report,
                    "de.yaml/Duplicate/MenuFile",
                    "de.yaml/Missing/Title",
                    "de.yaml/Orphan/OldKey",
                    "de.yaml/PlaceholderMismatch/TimeStatusFormat");
            });

        runner.Add(
            "CoverageChecker: 読み込めなかったファイルは読み込み不能の1件だけにする（重複を報告しない）",
            () =>
            {
                // 読み込み不能かつ重複キーがある形。LanguageFileReader は読み込み不能でも
                // 重複キーの一覧を返すため、報告から落とすのは判定の層の役目である
                // （design.md: CoverageChecker の事後条件）。
                var unreadableWithDuplicates = LanguageFileReader.Parse("xx.yaml", "AA: [1, 2]\nAA: [3]\n");

                SelfAssert.That(
                    unreadableWithDuplicates.Entries == null,
                    "この入力は読み込み不能になる前提ですが、読み込めてしまいました。前提が崩れています。");
                SelfAssert.That(
                    unreadableWithDuplicates.DuplicateKeys.Count > 0,
                    "この入力は重複キーを伴う前提ですが、重複が挙がりませんでした。前提が崩れています。");

                var report = CoverageChecker.Check(
                    CoverageKeys,
                    new[]
                    {
                        LanguageFileReader.Parse(CoverageChecker.ReferenceFileName, ReferenceText),
                        unreadableWithDuplicates,
                    });

                AssertProblems(report, "xx.yaml/Unreadable/-");
            });

        runner.Add(
            "CoverageChecker: 読み込み不能の理由を問題の補足に残す",
            () =>
            {
                var unreadable = LanguageFileReader.Parse("broken.yaml", "Title: \"unterminated\nMenuFile: [1, 2\n");

                var report = CoverageChecker.Check(
                    CoverageKeys,
                    new[]
                    {
                        LanguageFileReader.Parse(CoverageChecker.ReferenceFileName, ReferenceText),
                        unreadable,
                    });

                AssertProblems(report, "broken.yaml/Unreadable/-");
                SelfAssert.That(
                    report.Problems[0].Detail == unreadable.UnreadableReason,
                    $"読み込み不能の補足には読み込めなかった理由をそのまま期待しましたが、" +
                    $"「{report.Problems[0].Detail}」でした。");
            });

        runner.Add(
            "CoverageChecker: 英語が読み込めないと差し込み位置を比べず、基準が使えないことを残す",
            () =>
            {
                var report = CoverageChecker.Check(
                    CoverageKeys,
                    new[]
                    {
                        // 英語が読み込めない。
                        LanguageFileReader.Parse(CoverageChecker.ReferenceFileName, "Title: \"unterminated\n"),
                        // 英語が読めていれば差し込み位置のずれとして挙がる内容。
                        LanguageFileReader.Parse(
                            "de.yaml",
                            "Title: \"T\"\nMenuFile: \"M\"\nTimeStatusFormat: \"{0} Vergangen\"\n"),
                    });

                SelfAssert.That(
                    !report.IsReferenceUsable,
                    "英語が読み込めないので、基準は使えないはずです。");

                // 英語の読み込み不能だけが挙がり、差し込み位置は1件も比べられない。
                AssertProblems(report, "en.yaml/Unreadable/-");
            });

        runner.Add(
            "CoverageChecker: 英語のファイルが1本も無いときも差し込み位置を比べない",
            () =>
            {
                var report = CoverageChecker.Check(
                    CoverageKeys,
                    new[]
                    {
                        LanguageFileReader.Parse(
                            "de.yaml",
                            "Title: \"T\"\nMenuFile: \"M\"\nTimeStatusFormat: \"{0} Vergangen\"\n"),
                    });

                SelfAssert.That(
                    !report.IsReferenceUsable,
                    "英語のファイルが無いので、基準は使えないはずです。");
                AssertProblems(report);
            });

        runner.Add(
            "CoverageChecker: 問題の無い入力では問題が0件になる",
            () =>
            {
                var report = CoverageChecker.Check(
                    CoverageKeys,
                    new[]
                    {
                        LanguageFileReader.Parse(CoverageChecker.ReferenceFileName, ReferenceText),
                        LanguageFileReader.Parse(
                            "ja.yaml",
                            // 並び順が英語と違っても、差し込み位置の番号の組が同じなら問題にしない。
                            "TimeStatusFormat: \"{1} / {0}\"\nMenuFile: \"ファイル\"\nTitle: \"大きいフォルダーの検索\"\n"),
                    });

                AssertProblems(report);
                SelfAssert.That(
                    report.IsReferenceUsable,
                    "英語のファイルは読み込めているので、基準は使えるはずです。");
                SelfAssert.That(
                    report.LanguageCount == 2 && report.KeyCount == CoverageKeys.Length,
                    $"検証した範囲は 言語 2・キー {CoverageKeys.Length} を期待しましたが、" +
                    $"言語 {report.LanguageCount}・キー {report.KeyCount} でした。");
            });

        runner.Add(
            "CoverageChecker: 並び順をファイル名・種類・キーの定義順（孤児はキー名の順）で固定する",
            () =>
            {
                var report = CoverageChecker.Check(
                    CoverageKeys,
                    new[]
                    {
                        // ファイル名の順とは違う並びで渡し、並べ替えが行われることを確かめる。
                        LanguageFileReader.Parse(
                            "zz.yaml",
                            "MenuFile: \"M\"\nTimeStatusFormat: \"{0}\"\nMenuFile: \"M2\"\n" +
                            "OldKey: \"x\"\nAnotherKey: \"y\"\n"),
                        LanguageFileReader.Parse(CoverageChecker.ReferenceFileName, ReferenceText),
                        LanguageFileReader.Parse("aa.yaml", "Title: \"unterminated\n"),
                    });

                AssertProblems(
                    report,
                    // ファイル名の序数順: aa.yaml → en.yaml → zz.yaml。
                    "aa.yaml/Unreadable/-",
                    // 同じファイルの中は種類の順: 重複 → 欠落 → 孤児 → 差し込み位置。
                    "zz.yaml/Duplicate/MenuFile",
                    "zz.yaml/Missing/Title",
                    // 孤児はキー名の序数順（AnotherKey → OldKey）。
                    "zz.yaml/Orphan/AnotherKey",
                    "zz.yaml/Orphan/OldKey",
                    "zz.yaml/PlaceholderMismatch/TimeStatusFormat");
            });

        runner.Add(
            "CoverageChecker: 欠落はキー名の順ではなくキーの定義順で並ぶ",
            () =>
            {
                var report = CoverageChecker.Check(
                    CoverageKeys,
                    new[]
                    {
                        LanguageFileReader.Parse(CoverageChecker.ReferenceFileName, ReferenceText),
                        // Title と MenuFile の両方が欠けている。定義順では Title が先。
                        LanguageFileReader.Parse("de.yaml", "TimeStatusFormat: \"{0} {1}\"\n"),
                    });

                AssertProblems(
                    report,
                    "de.yaml/Missing/Title",
                    "de.yaml/Missing/MenuFile");
            });

        runner.Add(
            "CoverageChecker: 差し込み位置のずれに英語側と当該言語側の組を添える",
            () =>
            {
                var report = CoverageChecker.Check(
                    CoverageKeys,
                    new[]
                    {
                        LanguageFileReader.Parse(CoverageChecker.ReferenceFileName, ReferenceText),
                        LanguageFileReader.Parse(
                            "de.yaml",
                            "Title: \"T\"\nMenuFile: \"M\"\nTimeStatusFormat: \"{0} Vergangen\"\n"),
                    });

                AssertProblems(report, "de.yaml/PlaceholderMismatch/TimeStatusFormat");

                var detail = report.Problems[0].Detail;
                SelfAssert.That(
                    detail == "英語={0},{1} 当該={0}",
                    $"差し込み位置の補足は「英語={{0}},{{1}} 当該={{0}}」を期待しましたが、「{detail}」でした。");
            });

        runner.Add(
            "CoverageChecker: 訳文が片方にしか無いキーは差し込み位置を比べない",
            () =>
            {
                var report = CoverageChecker.Check(
                    CoverageKeys,
                    new[]
                    {
                        LanguageFileReader.Parse(CoverageChecker.ReferenceFileName, ReferenceText),
                        // TimeStatusFormat が無い。欠落としてだけ挙がり、差し込み位置のずれにはしない。
                        LanguageFileReader.Parse("de.yaml", "Title: \"T\"\nMenuFile: \"M\"\n"),
                        // 英語に無い孤児のキー。孤児としてだけ挙がり、差し込み位置のずれにはしない。
                        LanguageFileReader.Parse(
                            "fr.yaml",
                            "Title: \"T\"\nMenuFile: \"M\"\nTimeStatusFormat: \"{0} {1}\"\nOldKey: \"{9}\"\n"),
                    });

                AssertProblems(
                    report,
                    "de.yaml/Missing/TimeStatusFormat",
                    "fr.yaml/Orphan/OldKey");
            });

        runner.Add(
            "CoverageChecker: 英語と当該言語の双方にあるキーは、LanguageKey に無くても差し込み位置を比べる",
            () =>
            {
                // 差し込み位置の比較の対象は「英語の訳文が存在するキー」である
                // （design.md: CoverageChecker の事後条件、requirements.md: 3.4）。
                var report = CoverageChecker.Check(
                    CoverageKeys,
                    new[]
                    {
                        LanguageFileReader.Parse(
                            CoverageChecker.ReferenceFileName,
                            ReferenceText + "OldKey: \"{0} of {1}\"\n"),
                        LanguageFileReader.Parse(
                            "de.yaml",
                            "Title: \"T\"\nMenuFile: \"M\"\nTimeStatusFormat: \"{0} {1}\"\nOldKey: \"{0}\"\n"),
                    });

                // どちらのファイルでも OldKey は孤児であり、加えて de.yaml では差し込み位置がずれている。
                AssertProblems(
                    report,
                    "de.yaml/Orphan/OldKey",
                    "de.yaml/PlaceholderMismatch/OldKey",
                    "en.yaml/Orphan/OldKey");
            });

        runner.Add(
            "CoverageChecker: 訳文が無い（null の）キーを欠落として報告し、差し込み位置は比べない",
            () =>
            {
                // キーはあっても訳文の値が無い状態は、アプリでは英語に落ちずに例外になる
                // （LocalizationManager.GetText が値ありとみなして Replace を呼ぶため）。
                // そのため欠落として報告する（design.md: CoverageChecker の事後条件、
                // requirements.md: 3.1、3.2）。
                // 一方、PlaceholderParser.ParseIndexes は null を渡されると例外を投げる契約のため
                // （tasks.md: Implementation Notes、タスク2.1のレビュー）、差し込み位置の比較からは外す。
                // 欠落として別に挙がるので、報告が二重になることもない。
                var entries = new Dictionary<string, string>
                {
                    ["Title"] = "T",
                    ["MenuFile"] = "M",
                    ["TimeStatusFormat"] = null!,
                };

                var report = CoverageChecker.Check(
                    CoverageKeys,
                    new[]
                    {
                        LanguageFileReader.Parse(CoverageChecker.ReferenceFileName, ReferenceText),
                        new LanguageFileContent("de.yaml", entries, null, null),
                    });

                // 欠落1件だけ。差し込み位置のずれは挙がらない（例外にもならない）。
                AssertProblems(report, "de.yaml/Missing/TimeStatusFormat");
            });

        runner.Add(
            "CoverageChecker: 英語側の訳文が無いキーは英語の欠落として挙げ、差し込み位置は比べない",
            () =>
            {
                var referenceEntries = new Dictionary<string, string>
                {
                    ["Title"] = "Large Folder Finder",
                    ["MenuFile"] = "File",
                    ["TimeStatusFormat"] = null!,
                };

                var report = CoverageChecker.Check(
                    CoverageKeys,
                    new[]
                    {
                        new LanguageFileContent(CoverageChecker.ReferenceFileName, referenceEntries, null, null),
                        // 英語に訳文があれば差し込み位置のずれとして挙がる内容。
                        // 英語側が null なので比較そのものが行われないことを、この形で確かめる。
                        LanguageFileReader.Parse(
                            "de.yaml",
                            "Title: \"T\"\nMenuFile: \"M\"\nTimeStatusFormat: \"{0} Vergangen\"\n"),
                    });

                // 英語自身の欠落1件のみ。de.yaml の差し込み位置は比べられない。
                AssertProblems(report, "en.yaml/Missing/TimeStatusFormat");
            });

        runner.Add(
            "CoverageChecker: 事前条件に反する引数を受け付けない",
            () =>
            {
                var files = new[] { LanguageFileReader.Parse(CoverageChecker.ReferenceFileName, ReferenceText) };

                AssertThrows<ArgumentNullException>(
                    () => CoverageChecker.Check(null!, files),
                    "期待するキーの一覧が null");
                AssertThrows<ArgumentNullException>(
                    () => CoverageChecker.Check(CoverageKeys, null!),
                    "読み込み結果の一覧が null");
                AssertThrows<ArgumentException>(
                    () => CoverageChecker.Check(Array.Empty<string>(), files),
                    "期待するキーの一覧が空");
                AssertThrows<ArgumentException>(
                    () => CoverageChecker.Check(new[] { "Title", "Title" }, files),
                    "期待するキーの一覧に重複がある");
            });
    }

    /// <summary>
    /// 検証結果の問題の一覧が、期待する並びと内容に完全に一致することを確かめる。
    /// </summary>
    /// <param name="report">確かめる検証結果。</param>
    /// <param name="expected">
    /// 期待する問題を「ファイル名/種類/キー」の形で並べたもの。キーが無い問題は「-」で表す。
    /// </param>
    private static void AssertProblems(CheckReport report, params string[] expected)
    {
        var actual = report.Problems.Select(DescribeProblem).ToArray();

        SelfAssert.That(
            actual.SequenceEqual(expected),
            $"報告された問題は [{string.Join(" , ", expected)}] を期待しましたが、" +
            $"[{string.Join(" , ", actual)}] でした。");
    }

    /// <summary>1件の問題を、比較しやすい「ファイル名/種類/キー」の形に直す。</summary>
    private static string DescribeProblem(Problem problem)
    {
        return $"{problem.FileName}/{problem.Kind}/{problem.Key ?? "-"}";
    }

    /// <summary>指定した型の例外が送出されることを確かめる。</summary>
    /// <typeparam name="TException">期待する例外の型。</typeparam>
    /// <param name="action">実行する処理。</param>
    /// <param name="description">何を渡した場合かの説明（失敗時の文に使う）。</param>
    private static void AssertThrows<TException>(Action action, string description)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            throw new SelfCheckFailedException(
                $"{description} の場合は {typeof(TException).Name} を期待しましたが、" +
                $"{ex.GetType().Name} が送出されました。");
        }

        throw new SelfCheckFailedException(
            $"{description} の場合は {typeof(TException).Name} を期待しましたが、例外は送出されませんでした。");
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
