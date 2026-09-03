using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LargeFolderFinder.GoldenBaseline.Io;
using LargeFolderFinder.GoldenBaseline.Model;

namespace LargeFolderFinder.GoldenBaseline.Fixture;

/// <summary>
/// フィクスチャの生成と後始末の契約（design.md: Fixture/FixtureBuilder）。
/// <see cref="FixtureSpec"/>（タスク3.1）が定義した「何が存在するはずか」の真値をもとに、
/// 実際にファイルシステム上へ構造を生成し、検証後に確実に取り除く。
/// </summary>
public interface IFixtureBuilder
{
    /// <summary>
    /// <paramref name="spec"/> が定義する構造を <paramref name="rootPath"/> 配下に生成する。
    /// 生成できなかった項目があっても中断せず、残りの項目の生成を継続する（要件3.6）。
    /// </summary>
    /// <param name="spec">生成すべき構造の宣言的定義。</param>
    /// <param name="rootPath">生成先の基準フォルダのパス。短く保つ必要がある（Preconditions）。</param>
    /// <returns>生成結果。1件でも生成できなかった項目があれば <see cref="FixtureBuildResult.IsComplete"/> が偽になる。</returns>
    FixtureBuildResult Build(FixtureSpec spec, string rootPath);

    /// <summary>
    /// <paramref name="rootPath"/> 配下に生成された構造を後始末する。
    /// <see cref="Build"/> が部分的に失敗した後でも安全に呼び出せる（Invariants）。
    /// </summary>
    /// <param name="spec">後始末の対象（特にアクセス拒否を解除すべきフォルダ）を特定するために使う定義。</param>
    /// <param name="rootPath">後始末対象の基準フォルダのパス。</param>
    void TearDown(FixtureSpec spec, string rootPath);
}

/// <summary>
/// 生成できなかった1項目とその理由（要件3.6）。
/// </summary>
public sealed class FixtureOmission
{
    /// <summary>生成できなかった項目の相対パス。</summary>
    public string RelativePath { get; }

    /// <summary>生成できなかった理由。</summary>
    public string Reason { get; }

    /// <summary>
    /// FixtureOmission を構築する。
    /// </summary>
    /// <param name="relativePath">生成できなかった項目の相対パス。</param>
    /// <param name="reason">生成できなかった理由。</param>
    public FixtureOmission(string relativePath, string reason)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            throw new ArgumentException("相対パスが空です。", nameof(relativePath));
        }

        if (string.IsNullOrEmpty(reason))
        {
            throw new ArgumentException("理由が空です。", nameof(reason));
        }

        RelativePath = relativePath;
        Reason = reason;
    }
}

/// <summary>
/// <see cref="IFixtureBuilder.Build"/> の結果（design.md: FixtureBuilder Service Interface）。
/// </summary>
public sealed class FixtureBuildResult
{
    /// <summary>
    /// <see cref="FixtureSpec.Items"/> のすべてがディスク上に存在するとき真（Postconditions）。
    /// <see cref="Omissions"/> が1件でもあれば偽になる。
    /// </summary>
    public bool IsComplete => Omissions.Count == 0;

    /// <summary>生成できなかった項目の一覧（要件3.6）。</summary>
    public IReadOnlyList<FixtureOmission> Omissions { get; }

    /// <summary>
    /// FixtureBuildResult を構築する。
    /// </summary>
    /// <param name="omissions">生成できなかった項目の一覧。生成できなかった項目がなければ空でよい。</param>
    public FixtureBuildResult(IReadOnlyList<FixtureOmission> omissions)
    {
        Omissions = omissions ?? throw new ArgumentNullException(nameof(omissions));
    }
}

/// <summary>
/// <see cref="IFixtureBuilder"/> の実装。
/// 生成・削除はすべて <see cref="LongPath.Extend"/> を通した経路でのみ行う
/// （research.md「長いパスのフィクスチャをどう生成するか」）。
/// アクセス拒否の付与・解除は <see cref="IAccessControlGate"/>（タスク3.2）に委譲する。
/// </summary>
public sealed class FixtureBuilder : IFixtureBuilder
{
    private readonly IAccessControlGate _accessControlGate;

    /// <summary>
    /// 既定の <see cref="AccessControlGate"/> を用いて FixtureBuilder を構築する。
    /// </summary>
    public FixtureBuilder() : this(new AccessControlGate())
    {
    }

    /// <summary>
    /// アクセス制御ゲートを差し替え可能な形で FixtureBuilder を構築する。
    /// </summary>
    /// <param name="accessControlGate">アクセス拒否の付与・解除に用いるゲート。</param>
    public FixtureBuilder(IAccessControlGate accessControlGate)
    {
        _accessControlGate = accessControlGate ?? throw new ArgumentNullException(nameof(accessControlGate));
    }

    /// <inheritdoc />
    public FixtureBuildResult Build(FixtureSpec spec, string rootPath)
    {
        if (spec is null)
        {
            throw new ArgumentNullException(nameof(spec));
        }

        if (string.IsNullOrEmpty(rootPath))
        {
            throw new ArgumentException("基準フォルダのパスが空です。", nameof(rootPath));
        }

        var omissions = new List<FixtureOmission>();

        // 基準フォルダ自体の生成。ここが失敗すると配下の項目は一切生成できないため、
        // 全項目を同一理由の未生成として報告し、以降の処理は行わない。
        try
        {
            Directory.CreateDirectory(LongPath.Extend(rootPath));
        }
        catch (Exception ex)
        {
            string reason = DescribeFailure("基準フォルダの生成に失敗しました", ex);
            foreach (var item in spec.Items)
            {
                omissions.Add(new FixtureOmission(item.RelativePath, reason));
            }

            return new FixtureBuildResult(omissions);
        }

        // 親フォルダが子より先に処理されるよう、相対パスの階層が浅い順に並べ替える。
        // FixtureSpec は「親が定義に含まれる」ことのみを保証し、Items の並び順までは保証しないため、
        // ここで明示的に順序を作る。OrderBy は安定ソートなので、同じ深さの項目同士の
        // 元の並び順は保たれる。
        var orderedItems = spec.Items
            .OrderBy(item => CountSegments(item.RelativePath))
            .ToList();

        // アクセス拒否は全項目の生成が終わった後にまとめて適用する。先に適用すると、
        // 拒否設定によってその配下の項目の生成が妨げられる可能性があるため
        // （Standard 定義では空フォルダのみだが、将来の定義変更に対しても安全にする）。
        var accessDeniedFolders = new List<FixtureItem>();

        foreach (var item in orderedItems)
        {
            string plainFullPath = Path.Combine(rootPath, item.RelativePath);
            string extendedPath = LongPath.Extend(plainFullPath);

            try
            {
                if (item.Kind == GoldenEntryKind.Folder)
                {
                    Directory.CreateDirectory(extendedPath);
                }
                else
                {
                    // File.WriteAllBytes でバイト配列を確保する代わりに FileStream.SetLength を使う。
                    // 中身の内容そのものは検証対象外（サイズのみが問われる）であり、
                    // 大きなサイズでもメモリを消費しない経路にしている。
                    using (var stream = new FileStream(extendedPath, FileMode.Create, FileAccess.Write))
                    {
                        stream.SetLength(item.ContentSizeInBytes);
                    }
                }
            }
            catch (Exception ex)
            {
                omissions.Add(new FixtureOmission(item.RelativePath, DescribeFailure("生成に失敗しました", ex)));
                continue;
            }

            if (item.Kind == GoldenEntryKind.Folder && item.Traits.Contains(FixtureTrait.AccessDenied))
            {
                accessDeniedFolders.Add(item);
            }
        }

        foreach (var item in accessDeniedFolders)
        {
            string plainFullPath = Path.Combine(rootPath, item.RelativePath);

            try
            {
                // AccessControlGate は Deny ACE の付与・除去のみを行う部品であり、
                // 拡張長パスの扱いを持たない（design.md: FixtureBuilder のみが LongPath を扱う）。
                // access_denied_folder は境界条件上つねに短いパスであるため、プレーンなパスで渡す。
                _accessControlGate.DenyRead(plainFullPath);
            }
            catch (Exception ex)
            {
                omissions.Add(new FixtureOmission(item.RelativePath, DescribeFailure("読み取り拒否設定の付与に失敗しました", ex)));
            }
        }

        return new FixtureBuildResult(omissions);
    }

    /// <inheritdoc />
    public void TearDown(FixtureSpec spec, string rootPath)
    {
        if (spec is null)
        {
            throw new ArgumentNullException(nameof(spec));
        }

        if (string.IsNullOrEmpty(rootPath))
        {
            throw new ArgumentException("基準フォルダのパスが空です。", nameof(rootPath));
        }

        // 段階1: Deny ACE の除去。削除より必ず先に行う（1段階では削除が失敗するため。
        // research.md「権限のないフォルダの生成と後始末」）。
        // Build が部分的にしか成功していない場合、AccessDenied 対象のフォルダが
        // 実際には生成されていないことがあるため、実在確認をしてから呼び出す。
        foreach (var item in spec.Items)
        {
            if (item.Kind != GoldenEntryKind.Folder || !item.Traits.Contains(FixtureTrait.AccessDenied))
            {
                continue;
            }

            string plainFullPath = Path.Combine(rootPath, item.RelativePath);
            string extendedPath = LongPath.Extend(plainFullPath);

            if (!Directory.Exists(extendedPath))
            {
                continue;
            }

            try
            {
                _accessControlGate.RestoreRead(plainFullPath);
            }
            catch
            {
                // 解除に失敗しても削除は必ず試みる。削除の成否こそが後始末の最終的な結果である。
            }
        }

        // 段階2: 削除。拡張長パス経由で行う（260文字超のフォルダは通常のパスでは削除できない）。
        string extendedRoot = LongPath.Extend(rootPath);
        if (Directory.Exists(extendedRoot))
        {
            Directory.Delete(extendedRoot, recursive: true);
        }
    }

    /// <summary>
    /// 相対パスの階層の深さ（区切り文字 \ で分割した要素数）を数える。
    /// </summary>
    private static int CountSegments(string relativePath)
    {
        return relativePath.Split('\\').Length;
    }

    /// <summary>
    /// 例外を、理由文字列として簡潔に表現する。
    /// </summary>
    private static string DescribeFailure(string prefix, Exception ex)
    {
        return $"{prefix}: {ex.GetType().Name}: {ex.Message}";
    }
}
