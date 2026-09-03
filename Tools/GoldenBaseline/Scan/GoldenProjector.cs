using System;
using System.Collections.Generic;
using LargeFolderFinder.GoldenBaseline.Model;

namespace LargeFolderFinder.GoldenBaseline.Scan;

/// <summary>
/// 走査結果ツリー（<see cref="ScanOutcome"/>）を期待値データ（<see cref="GoldenDocument"/>）へ射影する契約
/// （design.md: Scan/GoldenProjector）。
/// </summary>
/// <remarks>
/// <para>
/// ルートノード自身はエントリとして含めない。理由は次の2点。
/// </para>
/// <list type="bullet">
/// <item>
/// 本体 <see cref="global::LargeFolderFinder.Scanner"/>（<c>Services/Scanner.cs</c>）は、走査結果ツリーの
/// ルートノードの <c>Name</c> に基準フォルダの絶対パスをそのまま格納する
/// （<c>new FolderInfo(dir.FullName, ...)</c>）。ルート自身をエントリとして射影しようとすると、
/// 「基準からの相対パス」（要件1.1）であるべき <see cref="GoldenEntry.RelativePath"/> に絶対パスが
/// 混入してしまい、要件と矛盾する。
/// </item>
/// <item>
/// 基準フォルダの論理名は <see cref="GoldenHeader.BaseFolderLabel"/> が別途保持している
/// （design.md: GoldenHeader）。ルート自身の情報はヘッダ側の責務であり、エントリ側で重複して
/// 表現する必要がない。
/// </item>
/// </list>
/// <para>
/// この扱いは、本ツールの自己検証で ScanRunner の結果を平坦化する際に使う内部ヘルパー
/// （タスク4.1、<c>SelfChecks.FlattenScanTree</c>）が採る扱いとも一致する。
/// </para>
/// </remarks>
public interface IGoldenProjector
{
    /// <summary>
    /// <paramref name="outcome"/> の走査結果ツリーを、<paramref name="header"/> をヘッダとして持つ
    /// <see cref="GoldenDocument"/> へ射影する。
    /// </summary>
    /// <param name="outcome">射影元の走査結果。この呼び出しでは一切変更しない（要件5.1）。</param>
    /// <param name="header">期待値データのヘッダとして用いるメタデータ。</param>
    /// <returns>射影された期待値データ。</returns>
    GoldenDocument Project(ScanOutcome outcome, GoldenHeader header);
}

/// <summary>
/// <see cref="IGoldenProjector"/> の実装（design.md: Scan/GoldenProjector）。
/// </summary>
public sealed class GoldenProjector : IGoldenProjector
{
    /// <inheritdoc />
    public GoldenDocument Project(ScanOutcome outcome, GoldenHeader header)
    {
        if (outcome is null)
        {
            throw new ArgumentNullException(nameof(outcome));
        }

        if (header is null)
        {
            throw new ArgumentNullException(nameof(header));
        }

        var entries = new List<GoldenEntry>();

        // 走査結果ツリー（outcome.Root）を読み取るのみで、一切変更しない（要件5.1）。
        Walk(outcome.Root, relativePath: null, entries);

        // GoldenDocument のコンストラクタが RelativePath の重複を検出して拒否するため、
        // ここで重複排除処理を別途行う必要はない（design.md: GoldenDocument Invariants）。
        return new GoldenDocument(header, entries);
    }

    /// <summary>
    /// 走査結果ツリーを再帰的に辿り、ルート自身を除く各ノードを <see cref="GoldenEntry"/> へ射影して追加する。
    /// 走査結果（<paramref name="node"/>）には一切手を加えない（要件5.1）。欠落を補完することもない。
    /// </summary>
    /// <param name="node">現在たどっているノード。</param>
    /// <param name="relativePath">
    /// <paramref name="node"/> の基準フォルダからの相対パス。区切りは <c>\</c> に統一する。
    /// ルートノードを表す呼び出しでは null を渡す。
    /// </param>
    /// <param name="entries">射影結果を追加していく先。</param>
    private static void Walk(global::LargeFolderFinder.FolderInfo node, string? relativePath, List<GoldenEntry> entries)
    {
        if (relativePath is not null)
        {
            // サイズはバイト値をそのまま採る。整形経路（ResultFormatter 等）を通さない
            // （design.md: GoldenProjector Responsibilities & Constraints）。
            // 更新日時（node.LastModified）と所有者（node.Owner）は読み取らない（要件1.5, 5.1 の趣旨）。
            var kind = node.IsFile ? GoldenEntryKind.File : GoldenEntryKind.Folder;
            entries.Add(new GoldenEntry(relativePath, kind, node.Size));
        }

        if (node.Children is null)
        {
            return;
        }

        foreach (var child in node.Children)
        {
            // 基準からの相対パスを組み立て、区切りを \ に統一する
            // （design.md: GoldenProjector Responsibilities & Constraints）。
            string childRelativePath = relativePath is null ? child.Name : relativePath + "\\" + child.Name;
            Walk(child, childRelativePath, entries);
        }
    }
}
