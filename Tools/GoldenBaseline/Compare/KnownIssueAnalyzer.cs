using System;
using System.Collections.Generic;
using System.Linq;
using LargeFolderFinder.GoldenBaseline.Fixture;
using LargeFolderFinder.GoldenBaseline.Model;

namespace LargeFolderFinder.GoldenBaseline.Compare;

/// <summary>
/// フィクスチャ定義（真値）と観測結果の差から、既知の不具合に由来する欠落を識別する契約
/// （design.md: Compare/KnownIssueAnalyzer）。
/// </summary>
public interface IKnownIssueAnalyzer
{
    /// <summary>
    /// <paramref name="spec"/> が持つ真値と、<paramref name="observed"/> が持つ観測済みエントリを突き合わせ、
    /// 欠落した項目のうち既知の不具合に由来すると判定できるものを列挙する。
    /// </summary>
    /// <param name="spec">「何が存在するはずか」の真値を持つフィクスチャの宣言的定義。</param>
    /// <param name="observed">走査結果を射影した観測済みの期待値データ。</param>
    /// <returns>
    /// 既知の不具合に由来すると判定された欠落の一覧。<paramref name="spec"/> に定義されていて
    /// <paramref name="observed"/> に存在しない項目のうち、境界条件（<see cref="FixtureTrait"/>）を根拠に
    /// 説明できるものだけを含む。既知の不具合として説明できない欠落（例えば <see cref="FixtureTrait.Ordinary"/>
    /// のみを持つ項目の欠落）は含めない（design.md: 走査結果の正しさを判定しない、要件5.5）。
    /// </returns>
    IReadOnlyList<KnownIssueFinding> Analyze(FixtureSpec spec, GoldenDocument observed);
}

/// <summary>
/// 1件の既知の欠落を表す不変のデータ型（design.md: Compare/KnownIssueAnalyzer - Service Interface）。
/// </summary>
public sealed class KnownIssueFinding
{
    /// <summary>欠落した項目の、フィクスチャ基準フォルダからの相対パス。</summary>
    public string RelativePath { get; }

    /// <summary>欠落の原因と推定される境界条件。</summary>
    public FixtureTrait Trait { get; }

    /// <summary>
    /// KnownIssueFinding を構築する。
    /// </summary>
    /// <param name="relativePath">欠落した項目の相対パス。</param>
    /// <param name="trait">欠落の原因と推定される境界条件。</param>
    public KnownIssueFinding(string relativePath, FixtureTrait trait)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            throw new ArgumentException("相対パスが空です。", nameof(relativePath));
        }

        RelativePath = relativePath;
        Trait = trait;
    }
}

/// <summary>
/// <see cref="IKnownIssueAnalyzer"/> の実装（design.md: Compare/KnownIssueAnalyzer）。
/// </summary>
/// <remarks>
/// <para>
/// この部品の存在意義は、期待値ファイルへの手作業の注釈を不要にすることにある
/// （research.md: Decision: 期待値の真値をフィクスチャ定義から導く）。そのため、
/// 個々の相対パスをコード中に直接書かない。判定はすべて <see cref="FixtureSpec"/> が
/// 各項目に持たせた <see cref="FixtureTrait"/> の一覧のみを根拠に行う。フィクスチャ定義
/// （<see cref="FixtureSpec.Standard"/> など）を変更すれば、識別結果は自動的に追随する。
/// </para>
/// <para>
/// 「既知の不具合」として扱う境界条件は <see cref="KnownIssueTraits"/> に列挙した1件（
/// <see cref="FixtureTrait.LongPath"/>）のみである。この判断の根拠は次の2点。
/// </para>
/// <list type="bullet">
/// <item>
/// requirements.md の Project Description（現状）および要件5.1 が名指しする「既知の不具合」は
/// 「260文字超のパスが集計から漏れる」という長いパスの不具合であり、これが本フィーチャーの
/// 前提となっている唯一の既知バグである。
/// </item>
/// <item>
/// <see cref="FixtureTrait.AccessDenied"/>（読み取り権限のないフォルダ）は要件2.4・3.4が
/// 意図して定義した「スキップして記録する」という正常系の挙動であり、不具合ではない。
/// これを既知の不具合として扱うと、意図された仕様と不具合の区別が曖昧になる。
/// また現行の <see cref="FixtureSpec.Standard"/> では AccessDenied フォルダ自身は
/// 走査結果のツリーに（中身が空のまま）現れるため、通常はこの分類が実際に問題になる
/// 場面もない。
/// </item>
/// </list>
/// <para>
/// <see cref="FixtureTrait.Japanese"/>・<see cref="FixtureTrait.Empty"/>・
/// <see cref="FixtureTrait.ZeroByte"/>・<see cref="FixtureTrait.Ordinary"/> のみを理由とする
/// 欠落は、既知の不具合では説明できない欠落であり、この部品は沈黙する（列挙しない）。
/// これは見落としではなく意図的な設計である。既知の不具合で説明できない欠落は、
/// 走査結果の正しさを判定しないという要件5.5の制約のもとでは、この部品が代わりに
/// 何かを主張してよい対象ではなく、むしろ開発者が別途注目すべき重要な発見である。
/// </para>
/// </remarks>
public sealed class KnownIssueAnalyzer : IKnownIssueAnalyzer
{
    /// <summary>
    /// 既知の不具合の原因として認める境界条件。ここに列挙した順に判定し、最初に一致したものを
    /// <see cref="KnownIssueFinding.Trait"/> として採用する。
    /// 複数の既知バグが将来追加された場合の優先順位付けのため、単一の値ではなく一覧として保持する。
    /// </summary>
    private static readonly IReadOnlyList<FixtureTrait> KnownIssueTraits = new[]
    {
        FixtureTrait.LongPath,
    };

    /// <inheritdoc />
    public IReadOnlyList<KnownIssueFinding> Analyze(FixtureSpec spec, GoldenDocument observed)
    {
        if (spec is null)
        {
            throw new ArgumentNullException(nameof(spec));
        }

        if (observed is null)
        {
            throw new ArgumentNullException(nameof(observed));
        }

        var observedPaths = new HashSet<string>(
            observed.Entries.Select(e => e.RelativePath),
            StringComparer.Ordinal);

        var findings = new List<KnownIssueFinding>();

        foreach (var item in spec.Items)
        {
            if (observedPaths.Contains(item.RelativePath))
            {
                // 観測されている項目は欠落ではない。誤検出を避けるため対象外とする。
                continue;
            }

            // 欠落している項目が持つ境界条件のうち、既知の不具合の原因として認められるものを探す。
            // FixtureSpec 側のデータ（Traits）だけを根拠にしており、相対パスのハードコードは行わない。
            FixtureTrait? causeTrait = null;
            foreach (var knownIssueTrait in KnownIssueTraits)
            {
                if (item.Traits.Contains(knownIssueTrait))
                {
                    causeTrait = knownIssueTrait;
                    break;
                }
            }

            if (causeTrait is null)
            {
                // 既知の不具合では説明できない欠落。この部品は判定を行わず沈黙する（要件5.5）。
                continue;
            }

            findings.Add(new KnownIssueFinding(item.RelativePath, causeTrait.Value));
        }

        return findings;
    }
}
