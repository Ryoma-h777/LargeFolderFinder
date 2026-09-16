using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LargeFolderFinder.GoldenBaseline.Io;

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
///
/// 独立に列挙し直す経路は、本体と同じくプレーンなパス（拡張長プレフィクスなし）で行うため、
/// 本体と同じ長さの制約を受ける。**実測（2026-09-16）では、親フォルダの絶対パスが258文字以上に
/// なると Win32 の列挙（<c>FindFirstFileEx(フォルダ + "\*")</c>）が MAX_PATH=260 を超えて失敗する。**
/// これはアクセス拒否とは別の事象であり、両者を取り違えないよう別々に記録する（タスク7.2）。
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
    /// <para>
    /// ここに入るのは「アクセス拒否（<see cref="UnauthorizedAccessException"/>）で列挙できなかった」
    /// 対象だけである。長さのせいで列挙できなかった対象は別事象であり、
    /// <see cref="UnenumerablePaths"/> に分けて記録する（取り違えると要件2.4 の
    /// 「アクセスできない項目」の意味がぼやけるため。タスク7.2）。
    /// </para>
    /// </summary>
    public IReadOnlyList<string> SkippedPaths { get; }

    /// <summary>
    /// 長さのせいで列挙できなかったフォルダの一覧（タスク7.2）。
    /// <para>
    /// 実測（2026-09-16）の境界は「親フォルダの絶対パスが258文字以上だと、その直下を一覧できない」の
    /// 1つだけで、フォルダとファイルに差はない。このとき本体の走査ではその配下が丸ごと結果から消える。
    /// 本部品は事実の記録に徹し、正しさの判定は行わない（要件5.5）。
    /// </para>
    /// </summary>
    public IReadOnlyList<string> UnenumerablePaths { get; }

    /// <summary>
    /// ScanOutcome を構築する。
    /// </summary>
    /// <param name="root">走査結果のルートノード。</param>
    /// <param name="clusterSizeInBytes">適用したクラスタサイズ（バイト）。0以上である必要がある。</param>
    /// <param name="skippedPaths">アクセスできずスキップされた対象の一覧。</param>
    /// <param name="unenumerablePaths">長さのせいで列挙できなかった対象の一覧。</param>
    public ScanOutcome(
        global::LargeFolderFinder.FolderInfo root,
        long clusterSizeInBytes,
        IReadOnlyList<string> skippedPaths,
        IReadOnlyList<string> unenumerablePaths)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));

        if (clusterSizeInBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clusterSizeInBytes), clusterSizeInBytes, "クラスタサイズは0以上である必要があります。");
        }

        ClusterSizeInBytes = clusterSizeInBytes;
        SkippedPaths = skippedPaths ?? throw new ArgumentNullException(nameof(skippedPaths));
        UnenumerablePaths = unenumerablePaths ?? throw new ArgumentNullException(nameof(unenumerablePaths));
    }
}

/// <summary>
/// 列挙・属性取得に失敗した理由の分類（タスク7.2）。
/// 記録先の振り分けを1箇所に集約するために用いる。
/// </summary>
internal enum EnumerationFailureKind
{
    /// <summary>アクセス拒否。要件2.4・3.4 が意図した正常系であり、「スキップされた対象」として記録する。</summary>
    AccessDenied,

    /// <summary>長さのせいで触れない。本体の既知の不具合が現れる箇所であり、スキップとは区別して記録する。</summary>
    PathLength,

    /// <summary>上記のいずれでもない、想定していない失敗。記録せず、そのまま送出する。</summary>
    Unexpected
}

/// <summary>
/// <see cref="IScanRunner"/> の実装。本体 <see cref="global::LargeFolderFinder.Scanner"/> を
/// 固定条件（抽出サイズ閾値ゼロ、ファイル表示あり）で呼び出す。
/// </summary>
public sealed class ScanRunner : IScanRunner
{
    /// <summary>
    /// Win32 の <c>GetFileAttributesW</c> が失敗したときに返す値（INVALID_FILE_ATTRIBUTES）。
    /// net48 の <see cref="FileSystemInfo.Attributes"/> は、絶対パスが260文字以上のフォルダに対して
    /// 例外ではなくこの値（-1）をそのまま返す。ビットとしては再解析ポイントの印も立っているように
    /// 見えるため、明示的に判別しないと「再解析ポイントだから辿らない」と取り違える（タスク7.2）。
    /// </summary>
    private const FileAttributes InvalidFileAttributes = (FileAttributes)(-1);

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
        var unenumerablePaths = new List<string>();
        DetectSkippedPaths(rootPath, skippedPaths, unenumerablePaths);

        return new ScanOutcome(root, clusterSizeInBytes, skippedPaths, unenumerablePaths);
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
    /// <paramref name="path"/> 配下を独立に一段ずつ列挙し、列挙できなかった経路を理由ごとに記録する。
    /// 列挙できた配下は再帰的に辿る。本体の走査ロジックには一切依存しない、別経路での検出である。
    /// </summary>
    /// <param name="path">列挙対象のフォルダのパス。本体と同じくプレーンなパスで扱う。</param>
    /// <param name="skippedPaths">アクセス拒否で列挙できなかった対象の記録先（要件2.4）。</param>
    /// <param name="unenumerablePaths">長さのせいで列挙できなかった対象の記録先（タスク7.2）。</param>
    /// <remarks>
    /// 記録先を2つに分けているのは、「アクセス拒否によるスキップ」と「長さのせいで列挙できない」が
    /// 別の事象だからである。前者は要件2.4・3.4 が意図した正常系であり、後者は本体の既知の不具合
    /// （長いパスの配下が集計から消える）が現れている箇所である。取り違えると、期待値の読み手が
    /// 欠落の原因を誤って解釈する。
    /// なお、想定していない理由での失敗（例えば列挙の直前に対象が削除された場合）は握りつぶさず
    /// そのまま送出する（design.md Error Handling:「失敗を隠さないことを最優先」）。
    /// </remarks>
    internal static void DetectSkippedPaths(string path, List<string> skippedPaths, List<string> unenumerablePaths)
    {
        string[] subDirectories;

        try
        {
            // ファイル・フォルダの双方の列挙可否を確認する。本体の FindFirstFile ベースの列挙も
            // 同一の Deny ACE の影響を受けるため、挙動は一致する。
            _ = Directory.GetFiles(path);
            subDirectories = Directory.GetDirectories(path);
        }
        catch (Exception ex)
        {
            RecordEnumerationFailure(ex, path, skippedPaths, unenumerablePaths);
            return;
        }

        foreach (var subDirectory in subDirectories)
        {
            FileAttributes attributes;

            try
            {
                attributes = new DirectoryInfo(subDirectory).Attributes;
            }
            catch (Exception ex)
            {
                // 属性の取得の失敗も、列挙の失敗と同じ規則で分類する。理由を問わず一括で
                // 「長さのせい」にすると、アクセス拒否（要件2.4 のスキップ）や、列挙の直前に対象が
                // 消えた場合まで取り違える。
                RecordEnumerationFailure(ex, subDirectory, skippedPaths, unenumerablePaths);
                continue;
            }

            if (attributes == InvalidFileAttributes)
            {
                // 絶対パスが260文字以上のフォルダでは属性の取得が Win32 の段階で失敗し、
                // net48 では例外ではなく -1 が返る。ビット演算では再解析ポイントと区別がつかないため、
                // 先に明示的に判別する（タスク7.2）。長さのせいでないなら記録せず送出される。
                // 検査を通した値だけを記録するため、検査を省くと記録する値そのものが得られない。
                string lengthInducedPath = EnsureAttributeFailureIsLengthInduced(subDirectory);

                unenumerablePaths.Add(lengthInducedPath);
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                // 本体も再解析ポイントは辿らない（Scanner.ScanRecursiveInternal 参照）。
                continue;
            }

            DetectSkippedPaths(subDirectory, skippedPaths, unenumerablePaths);
        }
    }

    /// <summary>
    /// 失敗を分類し、対応する記録先へ振り分ける。<see cref="EnumerationFailureKind.Unexpected"/> の場合は
    /// 記録せず、元のスタックのまま送出する（design.md Error Handling:「失敗を隠さないことを最優先」）。
    /// </summary>
    /// <remarks>
    /// 列挙の失敗と属性取得の失敗の双方がこの1箇所を通る。振り分けの規則を2箇所に分けて書くと、
    /// 片方だけが非対称に緩くなる（差し戻しの指摘）。
    /// </remarks>
    internal static void RecordEnumerationFailure(
        Exception exception,
        string path,
        List<string> skippedPaths,
        List<string> unenumerablePaths)
    {
        switch (ClassifyEnumerationFailure(exception, path))
        {
            case EnumerationFailureKind.AccessDenied:
                skippedPaths.Add(path);
                return;

            case EnumerationFailureKind.PathLength:
                unenumerablePaths.Add(path);
                return;

            default:
                ExceptionDispatchInfo.Capture(exception).Throw();
                return;
        }
    }

    /// <summary>
    /// 属性の取得が値 -1（INVALID_FILE_ATTRIBUTES）で失敗したとき、それが長さに起因することを確かめる。
    /// 長さに起因しないなら <see cref="DirectoryNotFoundException"/> を送出する。
    /// </summary>
    /// <remarks>
    /// -1 は例外の形を取らないため、そのまま「長さのせい」と決めつけると、列挙の直後に対象が消えた場合まで
    /// 「長さのせいで列挙できなかった対象」として記録してしまう（取り違え）。例外経路
    /// （<see cref="ClassifyEnumerationFailure"/>）と同じ実在確認を通すことで、兄弟の分岐との非対称をなくす。
    /// 判定そのものを自己検証から照合できるよう internal で公開する。
    /// </remarks>
    /// <param name="path">属性を取得できなかった対象のパス（プレーンな表記）。</param>
    /// <returns>長さに起因すると確かめられた <paramref name="path"/> そのもの。</returns>
    /// <exception cref="DirectoryNotFoundException">
    /// 拡張長パス経由でも実在しない場合。長さに起因すると断定できないため、記録せずに送出する。
    /// </exception>
    internal static string EnsureAttributeFailureIsLengthInduced(string path)
    {
        if (DirectoryExistsThroughExtendedPath(path))
        {
            return path;
        }

        throw new DirectoryNotFoundException(
            $"列挙の途中で対象を辿れなくなりました（属性を取得できず、拡張長パス経由でも実在しません）: {path}");
    }

    /// <summary>
    /// 列挙・属性取得の失敗を分類する。副作用を持たないため、自己検証から直接呼び出して
    /// 分類規則そのものを照合できる（差し戻しの指摘: 実在確認を壊す変異が実行経路からは検出できない）。
    /// </summary>
    /// <param name="exception">発生した例外。</param>
    /// <param name="path">失敗した対象のパス（プレーンな表記）。</param>
    internal static EnumerationFailureKind ClassifyEnumerationFailure(Exception exception, string path)
    {
        if (exception is null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        if (exception is UnauthorizedAccessException)
        {
            // アクセス拒否は要件2.4・3.4 が意図した正常系。パスの長さとは無関係に、常にこの分類になる。
            return EnumerationFailureKind.AccessDenied;
        }

        if (IsPathLengthFailure(exception, path))
        {
            return EnumerationFailureKind.PathLength;
        }

        return EnumerationFailureKind.Unexpected;
    }

    /// <summary>
    /// 失敗が「長さのせいで触れない」ことに起因するかを判定する。
    /// </summary>
    /// <remarks>
    /// <see cref="DirectoryNotFoundException"/> は「本当に存在しない」ときにも発生するため、
    /// 型だけで判断すると両者を取り違える。<see cref="DirectoryExistsThroughExtendedPath"/> で実在を
    /// 確かめ、実在するときに限り長さに起因すると判定する。実在しなければ false を返し、
    /// 呼び出し元は例外をそのまま送出する（握りつぶさない）。
    /// </remarks>
    internal static bool IsPathLengthFailure(Exception exception, string path)
    {
        if (exception is null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        if (!(exception is DirectoryNotFoundException || exception is PathTooLongException))
        {
            return false;
        }

        return DirectoryExistsThroughExtendedPath(path);
    }

    /// <summary>
    /// 拡張長パス経由でフォルダが実在するかを返す。
    /// </summary>
    /// <remarks>
    /// プレーンなパスでは絶対260文字以上のフォルダの実在を確認できず、常に「存在しない」と判定される。
    /// 「長さのせいで触れない」ことの判定にプレーンなパスを使うと、判定が常に偽になり意味を失うため、
    /// 必ず <see cref="LongPath.Extend"/> を通す。
    /// </remarks>
    internal static bool DirectoryExistsThroughExtendedPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        try
        {
            return Directory.Exists(LongPath.Extend(path));
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException || ex is System.Security.SecurityException)
        {
            // パスとして解釈できない綴りは、実在の確認ができない。長さに起因するとは断定しない。
            return false;
        }
    }
}
