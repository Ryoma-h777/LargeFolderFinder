using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LargeFolderFinder.GoldenBaseline.Model;

namespace LargeFolderFinder.GoldenBaseline.Compare;

/// <summary>
/// 期待値データと実測データを突き合わせ、差分を分類する契約（design.md: Compare/BaselineComparer）。
/// </summary>
public interface IBaselineComparer
{
    /// <summary>
    /// 期待値データと実測データを突き合わせ、判定結果と差分の全件を返す。
    /// </summary>
    /// <param name="expected">期待値データ。</param>
    /// <param name="actual">実測（走査結果を射影した）データ。</param>
    /// <returns>判定結果と差分の全件を保持する DiffReport。</returns>
    DiffReport Compare(GoldenDocument expected, GoldenDocument actual);
}

/// <summary>
/// 期待値と実測を突き合わせ、差分を分類する実装（design.md: Compare/BaselineComparer）。
/// 走査条件（物理サイズ換算の有無）の照合をエントリの突き合わせより先に行い、
/// 不一致の場合は突き合わせを行わずに設定不一致として報告する（要件6.2, 6.3）。
/// 比較は対称であり、expected / actual いずれの並び順にも依存しない。
/// </summary>
public sealed class BaselineComparer : IBaselineComparer
{
    /// <inheritdoc />
    public DiffReport Compare(GoldenDocument expected, GoldenDocument actual)
    {
        if (expected is null)
        {
            throw new ArgumentNullException(nameof(expected));
        }

        if (actual is null)
        {
            throw new ArgumentNullException(nameof(actual));
        }

        // design.md「比較の判定」フロー: 走査条件の照合をエントリの突き合わせより先に行う。
        // 物理サイズ換算の有無が異なると全エントリが不一致になり報告が無意味になるため、
        // ここで打ち切って設定不一致として報告する（要件6.2, 6.3）。
        if (expected.Header.UsePhysicalSize != actual.Header.UsePhysicalSize)
        {
            return new DiffReport(BaselineVerdict.SettingsMismatch, Array.Empty<DiffEntry>());
        }

        var entries = ClassifyEntries(expected.Entries, actual.Entries);
        var verdict = entries.Count == 0 ? BaselineVerdict.Match : BaselineVerdict.Different;

        return new DiffReport(verdict, entries);
    }

    /// <summary>
    /// 期待値と実測のエントリを相対パスで突き合わせ、サイズ不一致・欠落・新規・種別不一致の4種に分類する。
    /// 相対パスの列挙順は序数(Ordinal)で安定させ、出力を決定的にする。
    /// </summary>
    private static List<DiffEntry> ClassifyEntries(IReadOnlyList<GoldenEntry> expectedEntries, IReadOnlyList<GoldenEntry> actualEntries)
    {
        var expectedByPath = expectedEntries.ToDictionary(entry => entry.RelativePath, StringComparer.Ordinal);
        var actualByPath = actualEntries.ToDictionary(entry => entry.RelativePath, StringComparer.Ordinal);

        var relativePaths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var relativePath in expectedByPath.Keys)
        {
            relativePaths.Add(relativePath);
        }

        foreach (var relativePath in actualByPath.Keys)
        {
            relativePaths.Add(relativePath);
        }

        var diffEntries = new List<DiffEntry>();

        foreach (var relativePath in relativePaths)
        {
            bool hasExpected = expectedByPath.TryGetValue(relativePath, out var expectedEntry);
            bool hasActual = actualByPath.TryGetValue(relativePath, out var actualEntry);

            if (hasExpected && !hasActual)
            {
                // 期待値データに存在して実測に存在しない（要件4.3）。
                diffEntries.Add(new DiffEntry(DiffKind.Missing, relativePath, string.Empty, string.Empty));
                continue;
            }

            if (!hasExpected && hasActual)
            {
                // 実測に存在して期待値データに存在しない（要件4.4）。
                diffEntries.Add(new DiffEntry(DiffKind.Unexpected, relativePath, string.Empty, string.Empty));
                continue;
            }

            // ここに到達する時点で双方に存在する。
            if (expectedEntry!.Kind != actualEntry!.Kind)
            {
                // 種別が一致しない場合、双方の種別を報告する（要件4.5）。
                diffEntries.Add(new DiffEntry(
                    DiffKind.KindMismatch,
                    relativePath,
                    expectedEntry.Kind.ToString(),
                    actualEntry.Kind.ToString()));
                continue;
            }

            if (expectedEntry.SizeInBytes != actualEntry.SizeInBytes)
            {
                // サイズが一致しない場合、期待されたサイズと実際のサイズの双方を報告する（要件4.2）。
                diffEntries.Add(new DiffEntry(
                    DiffKind.SizeMismatch,
                    relativePath,
                    expectedEntry.SizeInBytes.ToString(CultureInfo.InvariantCulture),
                    actualEntry.SizeInBytes.ToString(CultureInfo.InvariantCulture)));
            }
        }

        return diffEntries;
    }
}
