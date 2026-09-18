using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace LargeFolderFinder.LocalizationCheck.Check;

/// <summary>
/// 訳文から <c>string.Format</c> の差し込み位置の番号の組を取り出す部品
/// （design.md: Components and Interfaces / Check / PlaceholderParser）。
/// </summary>
/// <remarks>
/// 判定の中核として、ファイルもコンソールも扱わない純粋な部品にしている
/// （design.md: Architecture Integration「判定の中核と入出力の分離」）。
/// </remarks>
public static class PlaceholderParser
{
    /// <summary>
    /// 差し込み位置とエスケープされた波括弧を、左から順に1つずつ拾うためのパターン。
    /// </summary>
    /// <remarks>
    /// 選択肢の順序に意味がある。エスケープ（<c>{{</c>・<c>}}</c>）を先に置くことで、
    /// <c>{{0}}</c> の内側が差し込み位置として拾われることを防ぐ。
    /// 差し込み位置は <c>{番号}</c>、<c>{番号,幅}</c>、<c>{番号:書式}</c>、<c>{番号,幅:書式}</c> を認める。
    /// 幅は左寄せを表す負の値も取り得る。書式に波括弧は含めない（閉じ括弧の無い訳文で、
    /// 後続の差し込み位置まで飲み込まないようにするため）。
    /// 番号は ASCII の数字に限る（<c>\d</c> は全角数字なども拾ってしまうため使わない）。
    /// </remarks>
    private static readonly Regex PlaceholderPattern = new Regex(
        @"\{\{|\}\}|\{([0-9]+)(?:,-?[0-9]+)?(?::[^{}]*)?\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>訳文に含まれる差し込み位置の番号を、重複を除いて昇順で返す。</summary>
    /// <param name="text">1つのキーに対応する訳文。</param>
    /// <returns>差し込み位置の番号（重複なし・昇順）。差し込み位置が無ければ空。</returns>
    /// <exception cref="ArgumentNullException">text が null の場合。</exception>
    public static IReadOnlyList<int> ParseIndexes(string text)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        // SortedSet により、重複の除去と昇順への並べ替えを同時に行う。
        var indexes = new SortedSet<int>();

        foreach (Match match in PlaceholderPattern.Matches(text))
        {
            var numberGroup = match.Groups[1];
            if (!numberGroup.Success)
            {
                // エスケープされた波括弧（{{ または }}）。差し込み位置ではないので無視する。
                continue;
            }

            // 桁数が int に収まらない番号は string.Format でも扱えないため、番号として数えない。
            if (int.TryParse(numberGroup.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            {
                indexes.Add(index);
            }
        }

        return indexes.ToArray();
    }
}
