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
    /// そうした欠落は <see cref="FindUnexplainedOmissions"/> が別に列挙する。
    /// </returns>
    IReadOnlyList<KnownIssueFinding> Analyze(FixtureSpec spec, GoldenDocument observed);

    /// <summary>
    /// <paramref name="spec"/> に定義されていて <paramref name="observed"/> に存在せず、
    /// かつ既知の不具合（<see cref="Analyze"/> の判定）でも説明できない欠落を列挙する。
    /// </summary>
    /// <param name="spec">「何が存在するはずか」の真値を持つフィクスチャの宣言的定義。</param>
    /// <param name="observed">走査結果を射影した観測済みの期待値データ。</param>
    /// <returns>
    /// 説明できない欠落の一覧。<see cref="Analyze"/> が返す既知の欠落とは排他であり、
    /// 両者を合わせたものが「定義にあるのに観測されなかった項目」の全体になる。
    /// </returns>
    /// <remarks>
    /// この列挙は走査結果の正しさを判定しない（要件5.5）。判定の代わりに、説明のつかない欠落が
    /// 黙って期待値から消えないよう、識別できる情報（相対パスと境界条件）とともに記録する
    /// （要件5.2、タスク7.2）。基準フォルダの絶対パスが長い環境では、境界条件の印が付いていない
    /// 階層まで観測されなくなるため、この一覧が空でなくなることは実際に起こりうる。
    /// </remarks>
    IReadOnlyList<UnexplainedOmission> FindUnexplainedOmissions(FixtureSpec spec, GoldenDocument observed);
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
/// 1件の「説明できない欠落」を表す不変のデータ型（タスク7.2）。
/// 原因を断定しないため、<see cref="KnownIssueFinding"/> のような単一の根拠は持たず、
/// その項目が持つ境界条件をそのまま添えるにとどめる（要件5.5: 正しさの判定を行わない）。
/// </summary>
public sealed class UnexplainedOmission
{
    /// <summary>欠落した項目の、フィクスチャ基準フォルダからの相対パス。</summary>
    public string RelativePath { get; }

    /// <summary>その項目がフィクスチャ定義で持っていた境界条件の一覧（原因の断定ではない）。</summary>
    public IReadOnlyList<FixtureTrait> Traits { get; }

    /// <summary>
    /// UnexplainedOmission を構築する。
    /// </summary>
    /// <param name="relativePath">欠落した項目の相対パス。</param>
    /// <param name="traits">その項目が持っていた境界条件の一覧。</param>
    public UnexplainedOmission(string relativePath, IReadOnlyList<FixtureTrait> traits)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            throw new ArgumentException("相対パスが空です。", nameof(relativePath));
        }

        RelativePath = relativePath;
        Traits = traits ?? throw new ArgumentNullException(nameof(traits));
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
/// 長いパスの配下が集計から漏れるという不具合であり、これが本フィーチャーの
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
/// 欠落は、既知の不具合では説明できない欠落であり、<see cref="Analyze"/> は何も主張しない
/// （列挙しない）。これは見落としではなく意図的な設計である。既知の不具合で説明できない欠落は、
/// 走査結果の正しさを判定しないという要件5.5の制約のもとでは、この部品が代わりに
/// 何かを主張してよい対象ではなく、むしろ開発者が別途注目すべき重要な発見である。
/// </para>
/// <para>
/// ただし「主張しない」ことと「黙って消す」ことは別である（タスク7.2）。基準フォルダの絶対パスが
/// 長い環境では、印の付いていない階層まで観測されなくなり、何の説明もないまま期待値から消える。
/// そこで <see cref="FindUnexplainedOmissions"/> を用意し、原因を断定しないまま
/// 「説明できない欠落」として識別できる情報とともに残す（要件5.2・5.5）。
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
        var findings = new List<KnownIssueFinding>();

        foreach (var item in EnumerateMissingItems(spec, observed))
        {
            FixtureTrait? causeTrait = FindKnownIssueTrait(item);

            if (causeTrait is null)
            {
                // 既知の不具合では説明できない欠落。この部品は判定を行わず沈黙する（要件5.5）。
                // 黙って消さないための記録は FindUnexplainedOmissions が担う。
                continue;
            }

            findings.Add(new KnownIssueFinding(item.RelativePath, causeTrait.Value));
        }

        return findings;
    }

    /// <inheritdoc />
    public IReadOnlyList<UnexplainedOmission> FindUnexplainedOmissions(FixtureSpec spec, GoldenDocument observed)
    {
        var omissions = new List<UnexplainedOmission>();

        foreach (var item in EnumerateMissingItems(spec, observed))
        {
            if (FindKnownIssueTrait(item) != null)
            {
                // 既知の欠落として説明できる項目は、こちらには含めない（Analyze と排他にする）。
                continue;
            }

            omissions.Add(new UnexplainedOmission(item.RelativePath, item.Traits));
        }

        return omissions;
    }

    /// <summary>
    /// フィクスチャ定義にあるのに観測されなかった項目を、定義の順序のまま列挙する。
    /// </summary>
    private static IEnumerable<FixtureItem> EnumerateMissingItems(FixtureSpec spec, GoldenDocument observed)
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

        // 観測されている項目は欠落ではない。誤検出を避けるため対象外とする。
        return spec.Items.Where(item => !observedPaths.Contains(item.RelativePath)).ToList();
    }

    /// <summary>
    /// 欠落した項目が持つ境界条件のうち、既知の不具合の原因として認められるものを返す。
    /// 該当がなければ null を返す。FixtureSpec 側のデータ（Traits）だけを根拠にしており、
    /// 相対パスのハードコードは行わない。
    /// </summary>
    private static FixtureTrait? FindKnownIssueTrait(FixtureItem item)
    {
        foreach (var knownIssueTrait in KnownIssueTraits)
        {
            if (item.Traits.Contains(knownIssueTrait))
            {
                return knownIssueTrait;
            }
        }

        return null;
    }
}
