using System;
using System.Collections.Generic;
using LargeFolderFinder.GoldenBaseline.Model;

namespace LargeFolderFinder.GoldenBaseline.Fixture;

/// <summary>
/// フィクスチャ項目が表現する境界条件の種類。
/// FixtureBuilder（タスク3.3）が生成する項目の性質を表し、
/// KnownIssueAnalyzer（タスク4.3）が走査結果との差分から既知の欠落を判定する根拠にもなる（要件5.2）。
/// </summary>
public enum FixtureTrait
{
    /// <summary>
    /// 走査で配下が観測されなくなりうる「長いパス」の項目であることを表す印（要件3.1）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 実測（2026-09-16）では、走査で項目が欠落する境界は「親フォルダの絶対パスが258文字以上だと、
    /// その直下を一覧できない」の1つだけであり、フォルダとファイルに差はない。ファイル自身の絶対パスが
    /// 292文字でもサイズまで取得できる。以前ここに書かれていた「ディレクトリは248文字・ファイルは260文字」
    /// という2つの境界は誤りだった（タスク7.2で訂正）。
    /// </para>
    /// <para>
    /// 一方で、フィクスチャの<b>生成</b>側にはこれとは別の上限が依然として存在する。
    /// <c>Directory.CreateDirectory</c> や <c>FileStream</c> にプレーンなパスを渡すと260文字で拒否されるため、
    /// 生成・削除は必ず <c>LongPath.Extend</c> を通す。走査の境界と生成の上限は別の話であり、混同しないこと。
    /// </para>
    /// <para>
    /// この印は KnownIssueAnalyzer が既知の欠落を判定する唯一の根拠であり、期待値と検証への影響が大きいため、
    /// 名前と定義そのものは変更していない（タスク7.2の判断）。
    /// </para>
    /// </remarks>
    LongPath,

    /// <summary>日本語を含む名前（要件3.2）。</summary>
    Japanese,

    /// <summary>空のフォルダ（要件3.3）。</summary>
    Empty,

    /// <summary>サイズ0のファイル（要件3.3）。</summary>
    ZeroByte,

    /// <summary>読み取り権限のないフォルダ（要件3.4）。</summary>
    AccessDenied,

    /// <summary>境界条件に該当しない、対比のための通常の項目。</summary>
    Ordinary
}

/// <summary>
/// フィクスチャとして生成すべき1項目の宣言。データとしてのみ存在し、
/// この型自体はファイルシステムへ一切触れない。実際の生成は FixtureBuilder（タスク3.3）が担う。
/// </summary>
public sealed class FixtureItem
{
    /// <summary>フィクスチャ基準フォルダからの相対パス。区切りは \ に統一する。</summary>
    public string RelativePath { get; }

    /// <summary>項目の種別（ファイルまたはフォルダ）。</summary>
    public GoldenEntryKind Kind { get; }

    /// <summary>ファイルの内容サイズ（バイト）。Folder の場合は常に0。</summary>
    public long ContentSizeInBytes { get; }

    /// <summary>この項目が持つ境界条件の一覧。少なくとも1件を持つ。</summary>
    public IReadOnlyList<FixtureTrait> Traits { get; }

    /// <summary>
    /// FixtureItem を構築する。
    /// </summary>
    /// <param name="relativePath">フィクスチャ基準フォルダからの相対パス。</param>
    /// <param name="kind">項目の種別。</param>
    /// <param name="contentSizeInBytes">ファイルの内容サイズ（バイト）。Folder の場合は0でなければならない。</param>
    /// <param name="traits">この項目が持つ境界条件の一覧。空であってはならない。</param>
    public FixtureItem(string relativePath, GoldenEntryKind kind, long contentSizeInBytes, IReadOnlyList<FixtureTrait> traits)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            throw new ArgumentException("相対パスが空です。", nameof(relativePath));
        }

        if (contentSizeInBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contentSizeInBytes), contentSizeInBytes, "内容のサイズは0以上である必要があります。");
        }

        if (kind == GoldenEntryKind.Folder && contentSizeInBytes != 0)
        {
            throw new ArgumentException("フォルダの内容サイズは常に0である必要があります。", nameof(contentSizeInBytes));
        }

        if (traits is null || traits.Count == 0)
        {
            throw new ArgumentException("境界条件の種類が1件も指定されていません。", nameof(traits));
        }

        RelativePath = relativePath;
        Kind = kind;
        ContentSizeInBytes = contentSizeInBytes;
        Traits = traits;
    }
}

/// <summary>
/// 生成すべきフィクスチャ構造の宣言的定義。
/// 「何が存在するはずか」の真値であり、FixtureBuilder（タスク3.3）が生成の根拠として用い、
/// KnownIssueAnalyzer（タスク4.3）が走査結果との差分から既知の欠落を導出する根拠にもなる（要件3.5, 5.2）。
/// この型自体はファイルシステムへ一切触れない。
/// </summary>
public sealed class FixtureSpec
{
    /// <summary>フィクスチャ定義の論理名。GoldenHeader.BaseFolderLabel に対応する。</summary>
    public string Name { get; }

    /// <summary>生成すべき項目の一覧。</summary>
    public IReadOnlyList<FixtureItem> Items { get; }

    /// <summary>
    /// FixtureSpec を構築する。
    /// 不変条件として、相対パスの一意性と、親フォルダが必ず Items に含まれることを検証する
    /// （design.md: FixtureSpec の Invariants）。
    /// </summary>
    /// <param name="name">フィクスチャ定義の論理名。</param>
    /// <param name="items">生成すべき項目の一覧。</param>
    public FixtureSpec(string name, IReadOnlyList<FixtureItem> items)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("フィクスチャ定義の論理名が空です。", nameof(name));
        }

        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        // 不変条件1: 相対パスの一意性。
        var itemsByPath = new Dictionary<string, FixtureItem>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is null)
            {
                throw new ArgumentException("項目に null が含まれています。", nameof(items));
            }

            if (itemsByPath.ContainsKey(item.RelativePath))
            {
                throw new ArgumentException($"相対パス '{item.RelativePath}' が重複しています。", nameof(items));
            }

            itemsByPath.Add(item.RelativePath, item);
        }

        // 不変条件2: 親フォルダが必ず Items に含まれる。
        foreach (var item in items)
        {
            foreach (var ancestorPath in EnumerateAncestorPaths(item.RelativePath))
            {
                if (!itemsByPath.TryGetValue(ancestorPath, out var ancestor))
                {
                    throw new ArgumentException(
                        $"項目 '{item.RelativePath}' の親フォルダ '{ancestorPath}' が定義に含まれていません。",
                        nameof(items));
                }

                if (ancestor.Kind != GoldenEntryKind.Folder)
                {
                    throw new ArgumentException(
                        $"項目 '{item.RelativePath}' の親 '{ancestorPath}' がフォルダとして定義されていません。",
                        nameof(items));
                }
            }
        }

        Name = name;
        Items = items;
    }

    /// <summary>
    /// 相対パスから、その直上の親までの祖先パスを浅い順に列挙する（項目自身は含まない）。
    /// 例: "a\b\c\file.txt" → "a", "a\b", "a\b\c"
    /// </summary>
    private static IEnumerable<string> EnumerateAncestorPaths(string relativePath)
    {
        var segments = relativePath.Split('\\');
        for (int depth = 1; depth < segments.Length; depth++)
        {
            yield return string.Join("\\", segments, 0, depth);
        }
    }

    /// <summary>
    /// requirements.md 3.1〜3.4 が求める境界条件をすべて含む、検証用の標準フィクスチャ定義。
    /// タスク3.3（生成）・4.3（既知の欠落識別）・5.3（初回の期待値生成）から真値として参照される。
    /// </summary>
    public static FixtureSpec Standard { get; } = CreateStandard();

    /// <summary>
    /// <see cref="Standard"/> の内容を組み立てる。呼び出すたびに同一の構造を返す（要件3.5の根拠）。
    /// 「同じ定義から常に同じ構造が導かれる」ことを外部から複数回呼び出して確認できるよう internal で公開する。
    /// </summary>
    internal static FixtureSpec CreateStandard()
    {
        var items = new List<FixtureItem>();

        // --- 対比のための通常の項目 ---
        // 境界条件だけでは「普通のものは正しく数えられるか」が確認できないため、通常のフォルダ・ファイルも必ず含める。
        items.Add(Item(@"normal", GoldenEntryKind.Folder, 0L, FixtureTrait.Ordinary));

        // クラスタ境界（NTFS の一般的な既定値である4096バイトを仮定）との関係が異なる3種類のサイズを混在させる。
        // 実際のクラスタサイズは走査時（ScanRunner）に測定される値を用いるため、
        // ここでの4096は「人が見て分かりやすい」ための設計上の仮定であり、境界の実測値そのものではない。
        items.Add(Item(@"normal\file_small.txt", GoldenEntryKind.File, 10L, FixtureTrait.Ordinary));           // クラスタ未満 → 物理サイズは切り上げられるはず
        items.Add(Item(@"normal\file_one_cluster.bin", GoldenEntryKind.File, 4096L, FixtureTrait.Ordinary));   // クラスタちょうど → 論理と物理が一致するはず
        items.Add(Item(@"normal\file_two_clusters.bin", GoldenEntryKind.File, 4097L, FixtureTrait.Ordinary));  // クラスタを1バイト超える → 物理サイズは次のクラスタへ切り上がるはず

        // --- サイズ0のファイル ---
        items.Add(Item(@"normal\file_zero_byte.dat", GoldenEntryKind.File, 0L, FixtureTrait.ZeroByte));

        // --- 空のフォルダ ---
        items.Add(Item(@"empty_folder", GoldenEntryKind.Folder, 0L, FixtureTrait.Empty));

        // --- 読み取り権限のないフォルダ ---
        items.Add(Item(@"access_denied_folder", GoldenEntryKind.Folder, 0L, FixtureTrait.AccessDenied));

        // --- 日本語を含む名前 ---
        items.Add(Item(@"日本語フォルダ", GoldenEntryKind.Folder, 0L, FixtureTrait.Japanese));
        items.Add(Item(@"日本語フォルダ\日本語ファイル.txt", GoldenEntryKind.File, 500L, FixtureTrait.Japanese));

        // --- 長いパス（ASCII）---
        // 走査で項目が欠落する境界は、実測（2026-09-16）では「親フォルダの絶対パスが258文字以上だと、
        // その直下を一覧できない」の1つだけで、フォルダとファイルに差はない（research.md: 日本語を含む長いパスの扱い）。
        // 数え方は文字数（WCHAR）。1階層50文字 × 5階層 + 区切り4文字 = 相対254文字の連鎖とし、
        // 実効絶対パス長を固定した基準フォルダ（タスク7.1）の下でこの境界を確実にまたぐようにしている。
        AddNestedChain(
            items,
            levels: new (char Ch, FixtureTrait[] Traits)[]
            {
                ('a', new[] { FixtureTrait.Ordinary }),
                ('b', new[] { FixtureTrait.Ordinary }),
                ('c', new[] { FixtureTrait.Ordinary }),
                ('d', new[] { FixtureTrait.Ordinary }),
                ('e', new[] { FixtureTrait.LongPath }),
            },
            segmentLength: 50,
            fileName: "boundary_file.bin",
            fileSizeInBytes: 8L,
            fileTraits: new[] { FixtureTrait.LongPath });

        // --- 長いパス（日本語）---
        // 境界は日本語でもASCIIでも同じ文字数で現れる
        // （research.md: 日本語247文字と ASCII 247文字が同じ境界で失敗することを実測済み）。
        // ASCII と同一のセグメント長・階層数で構成し、文字種の違いが境界に影響しないことを表現する。
        AddNestedChain(
            items,
            levels: new (char Ch, FixtureTrait[] Traits)[]
            {
                ('あ', new[] { FixtureTrait.Japanese }),
                ('い', new[] { FixtureTrait.Japanese }),
                ('う', new[] { FixtureTrait.Japanese }),
                ('え', new[] { FixtureTrait.Japanese }),
                ('お', new[] { FixtureTrait.Japanese, FixtureTrait.LongPath }),
            },
            segmentLength: 50,
            fileName: "境界ファイル.bin",
            fileSizeInBytes: 8L,
            fileTraits: new[] { FixtureTrait.Japanese, FixtureTrait.LongPath });

        return new FixtureSpec("fixture-v1", items);
    }

    /// <summary>
    /// 指定した文字と長さでネストしたフォルダの階層を items に追加し、
    /// 最深フォルダの直下に1件のファイルを追加する。
    /// 各階層の親フォルダも合わせて追加するため、FixtureSpec の「親フォルダが必ず含まれる」不変条件を満たす。
    /// </summary>
    private static void AddNestedChain(
        List<FixtureItem> items,
        (char Ch, FixtureTrait[] Traits)[] levels,
        int segmentLength,
        string fileName,
        long fileSizeInBytes,
        FixtureTrait[] fileTraits)
    {
        string path = string.Empty;
        foreach (var level in levels)
        {
            string segment = new string(level.Ch, segmentLength);
            path = path.Length == 0 ? segment : path + "\\" + segment;

            items.Add(Item(path, GoldenEntryKind.Folder, 0L, level.Traits));
        }

        string filePath = path + "\\" + fileName;
        items.Add(Item(filePath, GoldenEntryKind.File, fileSizeInBytes, fileTraits));
    }

    private static FixtureItem Item(string relativePath, GoldenEntryKind kind, long contentSizeInBytes, params FixtureTrait[] traits)
    {
        return new FixtureItem(relativePath, kind, contentSizeInBytes, traits);
    }
}
