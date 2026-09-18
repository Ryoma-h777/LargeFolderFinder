using System;
using System.Collections.Generic;
using System.Linq;
using LargeFolderFinder.LocalizationCheck.Model;

namespace LargeFolderFinder.LocalizationCheck.Check;

/// <summary>
/// 期待するキーの一覧と各言語ファイルの読み込み結果から、すべての問題を列挙する部品
/// （design.md: Components and Interfaces / Check / CoverageChecker）。
/// </summary>
/// <remarks>
/// <para>
/// 判定の中核として、ファイルもコンソールも扱わない純粋な部品にしている
/// （design.md: Architecture Integration「判定の中核と入出力の分離」）。
/// 入出力の層（Io）も入口（Program、SelfChecks）も参照しない。読み込み結果の型
/// <see cref="LanguageFileContent"/> が Model にあるのはこのためである。
/// </para>
/// <para>
/// 1件目の問題で打ち切らず、渡されたすべてのファイル・すべてのキーを調べる
/// （requirements.md: 3.1、3.9）。
/// </para>
/// </remarks>
public static class CoverageChecker
{
    /// <summary>
    /// 英語の言語ファイル名。差し込み位置の基準に使う。
    /// </summary>
    public const string ReferenceFileName = "en.yaml";

    /// <summary>
    /// 期待するキーの一覧と各言語ファイルの読み込み結果から、すべての問題を列挙する。
    /// </summary>
    /// <param name="expectedKeys">LanguageKey の名前（定義順）</param>
    /// <param name="files">言語ファイルごとの読み込み結果</param>
    /// <returns>
    /// 見つかったすべての問題と、検証した範囲。問題の並びは、ファイル名（序数順）→ 種類 →
    /// キーの定義順（定義に無いキーはキー名の序数順）で固定される。
    /// </returns>
    /// <exception cref="ArgumentNullException">引数が null の場合。</exception>
    /// <exception cref="ArgumentException">
    /// 事前条件に反する場合（<paramref name="expectedKeys"/> が空、または重複を含む）。
    /// </exception>
    public static CheckReport Check(IReadOnlyList<string> expectedKeys, IReadOnlyList<LanguageFileContent> files)
    {
        if (expectedKeys == null)
        {
            throw new ArgumentNullException(nameof(expectedKeys));
        }

        if (files == null)
        {
            throw new ArgumentNullException(nameof(files));
        }

        // 事前条件: 期待するキーの一覧は空でなく、重複を含まない
        // （design.md: CoverageChecker の事前条件）。
        // 空のまま進むと、すべての訳文が孤児として報告され、結果が意味を成さない。
        if (expectedKeys.Count == 0)
        {
            throw new ArgumentException("期待するキーの一覧が空です。", nameof(expectedKeys));
        }

        // キーの定義順を引くための対応表。重複の検出も兼ねる。
        var keyOrder = new Dictionary<string, int>(expectedKeys.Count, StringComparer.Ordinal);
        for (int i = 0; i < expectedKeys.Count; i++)
        {
            if (keyOrder.ContainsKey(expectedKeys[i]))
            {
                throw new ArgumentException(
                    $"期待するキーの一覧に重複があります: {expectedKeys[i]}", nameof(expectedKeys));
            }

            keyOrder[expectedKeys[i]] = i;
        }

        // 差し込み位置の基準となる英語の訳文。使えないときは null のままにする。
        var referenceEntries = FindReferenceEntries(files);

        var problems = new List<Problem>();
        foreach (var file in files)
        {
            CollectProblems(file, expectedKeys, keyOrder, referenceEntries, problems);
        }

        // 同じ入力に対して常に同じ並びになるよう、最後に並びを固定する
        // （design.md: Model / CheckReport の不変条件）。
        // 1つのファイル・1つの種類の中に同じキーの問題が2件できることはないため、
        // 比較の結果が引き分けになる組は無く、並べ替えの安定性に依存せずに並びが定まる。
        problems.Sort(new ProblemComparer(keyOrder));

        return new CheckReport(
            problems,
            files.Count,
            expectedKeys.Count,
            isReferenceUsable: referenceEntries != null);
    }

    /// <summary>
    /// 差し込み位置の基準に使う英語の訳文を探す。
    /// 英語のファイルが無い、または読み込めないときは null を返す。
    /// </summary>
    /// <remarks>
    /// ファイル名の比較で大文字小文字を区別しないのは、Windows のファイル名がそうであり、
    /// <c>EN.yaml</c> と <c>en.yaml</c> が同じファイルを指すためである。
    /// </remarks>
    private static IReadOnlyDictionary<string, string>? FindReferenceEntries(IReadOnlyList<LanguageFileContent> files)
    {
        foreach (var file in files)
        {
            if (string.Equals(file.FileName, ReferenceFileName, StringComparison.OrdinalIgnoreCase))
            {
                // 読み込めていないときは基準として使えない（Entries が null）。
                return file.Entries;
            }
        }

        return null;
    }

    /// <summary>
    /// 1つの言語ファイルについて、見つかった問題を <paramref name="problems"/> に加える。
    /// </summary>
    /// <param name="file">対象の言語ファイルの読み込み結果。</param>
    /// <param name="expectedKeys">期待するキーの一覧（定義順）。</param>
    /// <param name="keyOrder">期待するキーとその定義順の対応表（キーの有無の判定にも使う）。</param>
    /// <param name="referenceEntries">差し込み位置の基準となる英語の訳文。使えないときは null。</param>
    /// <param name="problems">見つかった問題の集約先。</param>
    private static void CollectProblems(
        LanguageFileContent file,
        IReadOnlyList<string> expectedKeys,
        IReadOnlyDictionary<string, int> keyOrder,
        IReadOnlyDictionary<string, string>? referenceEntries,
        List<Problem> problems)
    {
        if (file.Entries == null)
        {
            // 読み込めなかったファイルは読み込み不能を1件だけ持ち、
            // そのファイルのキーに関する他の問題は作らない（design.md: CoverageChecker の事後条件）。
            // 読み込み不能でも重複キーの一覧は渡ってくるが、キーの一覧全体が信用できない以上、
            // 重複だけを取り出して報告しても読み手を惑わせるだけである。
            problems.Add(new Problem(ProblemKind.Unreadable, file.FileName, null, file.UnreadableReason));
            return;
        }

        // 重複（requirements.md: 3.10）。読み込みの可否とは別に数えられている。
        foreach (var duplicateKey in file.DuplicateKeys)
        {
            problems.Add(new Problem(ProblemKind.Duplicate, file.FileName, duplicateKey, null));
        }

        // 欠落（requirements.md: 3.1、3.2）。
        // キーが無い場合だけでなく、キーはあっても訳文の値が無い（null の）場合も欠落として扱う
        // （design.md: CoverageChecker の事後条件）。
        // YAML で「Key:」と値を省くと、読み込みは成功しキーは存在するが値は null になる。
        // この状態のアプリは英語に置き換えず、LocalizationManager.GetText の中で例外になるため、
        // 訳文が無いのと同じ扱いにしないと、利用者から見た不具合を見逃すことになる。
        foreach (var expectedKey in expectedKeys)
        {
            if (!file.Entries.TryGetValue(expectedKey, out var translation) || translation == null)
            {
                problems.Add(new Problem(ProblemKind.Missing, file.FileName, expectedKey, null));
            }
        }

        // 孤児（requirements.md: 3.3）。期待するキーに無いものを挙げる
        // （キー名の序数順に並べるのは最後の並べ替えの役目）。
        foreach (var key in file.Entries.Keys)
        {
            if (!keyOrder.ContainsKey(key))
            {
                problems.Add(new Problem(ProblemKind.Orphan, file.FileName, key, null));
            }
        }

        // 差し込み位置（requirements.md: 3.4）。
        // 英語が使えないときは比べない（design.md: CoverageChecker の事後条件）。
        if (referenceEntries == null)
        {
            return;
        }

        // 英語の訳文が存在するキーを対象にする（design.md: CoverageChecker の事後条件）。
        // LanguageKey に無いキーでも、英語と当該言語の双方にあれば比べる（requirements.md: 3.4）。
        foreach (var pair in referenceEntries)
        {
            var referenceText = pair.Value;

            // 英語側と当該言語側の両方に訳文があるときだけ比べる。
            // PlaceholderParser.ParseIndexes は null を渡されると例外を投げる契約のため、
            // 訳文が無い（値が null の）キーはここで除く（tasks.md: タスク2.1のレビュー）。
            // 除いても見逃しにはならない。訳文が無いことは上の欠落として既に挙がっており、
            // ここで比べないことが、同じ事象を2件に分けて報告しないことにもなっている。
            if (referenceText == null)
            {
                continue;
            }

            if (!file.Entries.TryGetValue(pair.Key, out var text) || text == null)
            {
                continue;
            }

            var referenceIndexes = PlaceholderParser.ParseIndexes(referenceText);
            var indexes = PlaceholderParser.ParseIndexes(text);

            // どちらも重複なしの昇順で返るため、並びの比較がそのまま組の比較になる。
            if (referenceIndexes.SequenceEqual(indexes))
            {
                continue;
            }

            problems.Add(new Problem(
                ProblemKind.PlaceholderMismatch,
                file.FileName,
                pair.Key,
                $"英語={DescribeIndexes(referenceIndexes)} 当該={DescribeIndexes(indexes)}"));
        }
    }

    /// <summary>
    /// 差し込み位置の番号の組を、報告に載せる形（例: <c>{0},{1}</c>）の文字列に直す。
    /// </summary>
    /// <param name="indexes">差し込み位置の番号（重複なし・昇順）。</param>
    /// <returns>番号の組の文字列。1つも無いときは「（なし）」。</returns>
    private static string DescribeIndexes(IReadOnlyList<int> indexes)
    {
        if (indexes.Count == 0)
        {
            return "（なし）";
        }

        return string.Join(",", indexes.Select(index => $"{{{index}}}"));
    }

    /// <summary>
    /// 問題の並びを、ファイル名（序数順）→ 種類 → キーの定義順で決める比較子
    /// （design.md: Model / CheckReport の並びの規定）。
    /// </summary>
    /// <remarks>
    /// 期待するキーに無いキー（孤児など）は、定義順を持たないため定義済みのキーより後に置き、
    /// その中ではキー名の序数順で並べる。
    /// </remarks>
    private sealed class ProblemComparer : IComparer<Problem>
    {
        private readonly IReadOnlyDictionary<string, int> _keyOrder;

        public ProblemComparer(IReadOnlyDictionary<string, int> keyOrder)
        {
            _keyOrder = keyOrder;
        }

        public int Compare(Problem? x, Problem? y)
        {
            if (x == null || y == null)
            {
                throw new ArgumentException("並べ替えの対象に null が含まれています。");
            }

            int byFileName = string.CompareOrdinal(x.FileName, y.FileName);
            if (byFileName != 0)
            {
                return byFileName;
            }

            // 種類の順は ProblemKind の宣言順（読み込み不能 → 重複 → 欠落 → 孤児 → 差し込み位置）。
            int byKind = ((int)x.Kind).CompareTo((int)y.Kind);
            if (byKind != 0)
            {
                return byKind;
            }

            int byKeyOrder = RankOf(x.Key).CompareTo(RankOf(y.Key));
            if (byKeyOrder != 0)
            {
                return byKeyOrder;
            }

            return string.CompareOrdinal(x.Key, y.Key);
        }

        /// <summary>
        /// キーの定義順を返す。キーを持たない問題は先頭、定義に無いキーは末尾に寄せる。
        /// </summary>
        private int RankOf(string? key)
        {
            if (key == null)
            {
                // 読み込み不能のようにキーを持たない問題。同じ種類の中では先に置く。
                return -1;
            }

            return _keyOrder.TryGetValue(key, out var order) ? order : int.MaxValue;
        }
    }
}
