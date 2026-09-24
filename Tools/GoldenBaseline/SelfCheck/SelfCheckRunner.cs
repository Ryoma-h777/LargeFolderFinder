using System;
using System.Collections.Generic;
using System.Text;

namespace LargeFolderFinder.GoldenBaseline.SelfCheck;

/// <summary>
/// 外部パッケージに依存しない、最小限の自己検証ハーネス。
/// 検証項目（名前と条件判定）を登録し、まとめて実行して結果を集計する。
/// </summary>
public sealed class SelfCheckRunner
{
    private readonly List<(string Name, Action Assertion)> _checks = new();

    /// <summary>
    /// 検証項目を登録する。assertion が例外を送出した場合、その項目は失敗として扱われる。
    /// ただし <see cref="SelfAssert.Skip(string)"/> で飛ばした場合は失敗にならない。
    /// </summary>
    public void Add(string name, Action assertion)
    {
        _checks.Add((name, assertion));
    }

    /// <summary>
    /// 登録済みの検証項目をすべて実行し、状態を含む結果の一覧を返す。
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
                outcomes.Add(CheckOutcome.Pass(name));
            }
            catch (SelfCheckSkippedException ex)
            {
                // 前提が成り立たず確かめられなかった項目。名前と理由は報告するが、失敗としては数えない
                // （ntfs-mft-scan 要件7.1）。
                outcomes.Add(CheckOutcome.Skip(name, ex.Message));
            }
            catch (Exception ex)
            {
                outcomes.Add(CheckOutcome.Fail(name, ex.Message));
            }
        }

        return outcomes;
    }

    /// <summary>
    /// 実行結果を人が読む報告の文にまとめ、失敗の件数と飛ばした件数を返す（ntfs-mft-scan 要件7.1）。
    /// 飛ばした項目は名前と理由を出すが、失敗としては数えないため、終了コードを決める
    /// <paramref name="failureCount"/> には含めない。
    /// </summary>
    /// <param name="outcomes">実行結果の一覧。</param>
    /// <param name="failureCount">失敗した項目の件数（終了コードの判断に使う）。</param>
    /// <param name="skippedCount">飛ばした項目の件数（終了コードには影響しない）。</param>
    /// <returns>1項目ごとの行とまとめの行を含む報告の文。末尾は改行で終わる。</returns>
    public static string BuildReport(IReadOnlyList<CheckOutcome> outcomes, out int failureCount, out int skippedCount)
    {
        failureCount = 0;
        skippedCount = 0;

        var builder = new StringBuilder();

        foreach (var outcome in outcomes)
        {
            switch (outcome.Status)
            {
                case CheckStatus.Failed:
                    failureCount++;
                    builder.AppendLine($"[NG] {outcome.Name}");
                    builder.AppendLine($"      理由: {outcome.Reason}");
                    break;

                case CheckStatus.Skipped:
                    skippedCount++;
                    builder.AppendLine($"[SKIP] {outcome.Name}");
                    builder.AppendLine($"      飛ばした理由: {outcome.Reason}");
                    break;

                default:
                    builder.AppendLine($"[OK] {outcome.Name}");
                    break;
            }
        }

        builder.AppendLine();
        builder.AppendLine($"{outcomes.Count} 件中 {failureCount} 件が失敗、{skippedCount} 件を飛ばしました。");

        return builder.ToString();
    }
}
