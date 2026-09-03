using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LargeFolderFinder.GoldenBaseline.Scan;

/// <summary>
/// 被テストアプリの走査を固定条件で実行する契約（design.md: Scan/ScanRunner）。
/// 本体の内部構造に触れる面をこのファイルと GoldenProjector（タスク4.2）に限定する。
/// </summary>
/// <remarks>
/// 実装にあたり、design.md が要求する情報のうち本体からは直接得られないものが2点ある。
/// いずれも本体（<see cref="global::LargeFolderFinder.Scanner"/> 等）を改変せず、
/// このファイルの内側だけで独立に測定・判定することで満たしている。
///
/// 1. 適用したクラスタサイズ（要件6.1, 6.4）: 本体の <c>Scanner.GetClusterSize</c> は private であり、
///    内部が使う <c>Win32</c> クラスも internal のため、本ツールのアセンブリからは呼び出せない。
///    そこで本ファイルに独自の <c>GetDiskFreeSpaceW</c> P/Invoke 宣言を持ち、本体と全く同じ計算式
///    （セクタ数×セクタサイズ）で同一ボリュームに対して独立に測定する。同一の Win32 API を
///    同一の入力（ドライブのルート）で呼ぶ決定的な処理であるため、本体が内部で使った値と
///    常に一致する。
/// 2. スキップされた対象（要件2.4）: 本体の走査は `catch { /* アクセス拒否は無視 */ }` で
///    アクセス拒否を握りつぶしており、外部にスキップ事実を通知する経路が存在しない。
///    そこで本体の走査とは別に、同じ基準フォルダを独立に一段ずつ列挙し、
///    <see cref="UnauthorizedAccessException"/> が発生した経路だけを「スキップされた対象」として
///    記録する。本体の走査結果そのものには一切手を加えない（要件5.1, 5.5）。
/// </remarks>
public interface IScanRunner
{
    /// <summary>
    /// <paramref name="rootPath"/> を固定条件（抽出サイズ閾値ゼロ、ファイル表示あり、
    /// 表示条件は一切適用しない）で走査する。
    /// </summary>
    /// <param name="rootPath">走査対象の基準フォルダのパス。実在している必要がある（Preconditions）。</param>
    /// <param name="usePhysicalSize">物理サイズ換算を適用するかどうか。</param>
    /// <returns>
    /// 走査結果。同一の入力とディスク状態に対しては常に同一の集計結果を返す
    /// （Postconditions、要件2.3）。
    /// </returns>
    ScanOutcome Run(string rootPath, bool usePhysicalSize);
}

/// <summary>
/// <see cref="IScanRunner.Run"/> の結果（design.md: ScanRunner Service Interface）。
/// 本体の走査結果に一切手を加えない。欠落があっても補完せず、正しさの判定も行わない
/// （要件5.1, 5.5）。
/// </summary>
public sealed class ScanOutcome
{
    /// <summary>走査結果のルートノード。本体 <see cref="global::LargeFolderFinder.FolderInfo"/> をそのまま保持する。</summary>
    public global::LargeFolderFinder.FolderInfo Root { get; }

    /// <summary>
    /// 物理サイズ換算に適用したクラスタサイズ（バイト）。
    /// <see cref="IScanRunner.Run"/> の <c>usePhysicalSize</c> が false のときは 0。
    /// </summary>
    public long ClusterSizeInBytes { get; }

    /// <summary>
    /// アクセスできずスキップされた対象の一覧（要件2.4）。本体からは得られないため、
    /// このツールが独立に検出した結果である。
    /// </summary>
    public IReadOnlyList<string> SkippedPaths { get; }

    /// <summary>
    /// ScanOutcome を構築する。
    /// </summary>
    /// <param name="root">走査結果のルートノード。</param>
    /// <param name="clusterSizeInBytes">適用したクラスタサイズ（バイト）。0以上である必要がある。</param>
    /// <param name="skippedPaths">アクセスできずスキップされた対象の一覧。</param>
    public ScanOutcome(global::LargeFolderFinder.FolderInfo root, long clusterSizeInBytes, IReadOnlyList<string> skippedPaths)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));

        if (clusterSizeInBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clusterSizeInBytes), clusterSizeInBytes, "クラスタサイズは0以上である必要があります。");
        }

        ClusterSizeInBytes = clusterSizeInBytes;
        SkippedPaths = skippedPaths ?? throw new ArgumentNullException(nameof(skippedPaths));
    }
}

/// <summary>
/// <see cref="IScanRunner"/> の実装。本体 <see cref="global::LargeFolderFinder.Scanner"/> を
/// 固定条件（抽出サイズ閾値ゼロ、ファイル表示あり）で呼び出す。
/// </summary>
public sealed class ScanRunner : IScanRunner
{
    /// <summary>
    /// クラスタサイズを独立に測定するための Win32 API 宣言。
    /// 本体の <c>Win32</c> クラスは internal であり本ツールのアセンブリから参照できないため、
    /// このファイルに限定して同一シグネチャを再宣言する（上記 remarks の1点目）。
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetDiskFreeSpace(
        string lpRootPathName,
        out uint lpSectorsPerCluster,
        out uint lpBytesPerSector,
        out uint lpNumberOfFreeClusters,
        out uint lpTotalNumberOfClusters);

    /// <inheritdoc />
    public ScanOutcome Run(string rootPath, bool usePhysicalSize)
    {
        if (string.IsNullOrEmpty(rootPath))
        {
            throw new ArgumentException("基準フォルダのパスが空です。", nameof(rootPath));
        }

        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"基準フォルダが見つかりません: {rootPath}");
        }

        long clusterSizeInBytes = usePhysicalSize ? MeasureClusterSize(rootPath) : 0L;

        // 本体は Config.Instance 経由で並列実行の可否を決める（design.md: ScanRunner Implementation
        // Notes）。並列でも集計結果は同一になるため、期待値の同一性には影響しない。
        bool useParallel = global::LargeFolderFinder.Config.Instance.UseParallelScan;

        // 抽出サイズの閾値をゼロ、ファイル表示を有効として走査する（要件2.2）。
        // 本体の RunScan はファイルを常にノードとして追加し（表示時のフィルタリングのみ別レイヤーで
        // 行う設計になっており）、閾値による枝刈りも現状コメントアウトされているため、
        // ここで thresholdBytes に 0 を渡すことが「表示条件を一切適用しない」ことの実体になる。
        var progress = new Progress<global::LargeFolderFinder.ScanProgress>();

        // RunScan は非同期メソッドである。呼び出し元のコンテキスト（同期呼び出し元に
        // SynchronizationContext が存在する場合）に関わらずデッドロックしないよう、
        // Task.Run でスレッドプール上に切り離してから同期的に待機する。
        var scanTask = Task.Run(() => global::LargeFolderFinder.Scanner.RunScan(
            rootPath,
            thresholdBytes: 0L,
            totalFolders: 0,
            maxDepth: int.MaxValue,
            useParallel: useParallel,
            usePhysicalSize: usePhysicalSize,
            progress: progress,
            token: CancellationToken.None));

        global::LargeFolderFinder.FolderInfo? root = scanTask.GetAwaiter().GetResult();

        if (root is null)
        {
            throw new InvalidOperationException("走査結果を取得できませんでした（本体の RunScan が null を返しました）。");
        }

        // 本体の走査はアクセス拒否を黙って握りつぶすため、独立に同じ基準フォルダを列挙し直し、
        // アクセス拒否が発生した経路だけを記録する（上記 remarks の2点目）。走査結果そのもの
        // （root）には一切手を加えない。
        var skippedPaths = new List<string>();
        DetectSkippedPaths(rootPath, skippedPaths);

        return new ScanOutcome(root, clusterSizeInBytes, skippedPaths);
    }

    /// <summary>
    /// 本体の <c>Scanner.GetClusterSize</c> と同じ計算式で、独立にクラスタサイズを測定する。
    /// </summary>
    private static long MeasureClusterSize(string path)
    {
        string? rootPath = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(rootPath))
        {
            return 0L;
        }

        if (GetDiskFreeSpace(rootPath, out uint sectorsPerCluster, out uint bytesPerSector, out _, out _))
        {
            return (long)sectorsPerCluster * bytesPerSector;
        }

        return 0L;
    }

    /// <summary>
    /// <paramref name="path"/> 配下を独立に一段ずつ列挙し、<see cref="UnauthorizedAccessException"/> が
    /// 発生した経路を <paramref name="skippedPaths"/> に記録する。列挙できた配下は再帰的に辿る。
    /// 本体の走査ロジックには一切依存しない、別経路での検出である。
    /// </summary>
    private static void DetectSkippedPaths(string path, List<string> skippedPaths)
    {
        string[] subDirectories;

        try
        {
            // ファイル・フォルダの双方の列挙可否を確認する。本体の FindFirstFile ベースの列挙も
            // 同一の Deny ACE の影響を受けるため、挙動は一致する。
            _ = Directory.GetFiles(path);
            subDirectories = Directory.GetDirectories(path);
        }
        catch (UnauthorizedAccessException)
        {
            skippedPaths.Add(path);
            return;
        }

        foreach (var subDirectory in subDirectories)
        {
            var info = new DirectoryInfo(subDirectory);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                // 本体も再解析ポイントは辿らない（Scanner.ScanRecursiveInternal 参照）。
                continue;
            }

            DetectSkippedPaths(subDirectory, skippedPaths);
        }
    }
}
