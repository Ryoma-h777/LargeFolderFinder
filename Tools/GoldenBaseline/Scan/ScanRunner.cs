using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
/// <see cref="ScanRunner.RunForFinalProgress"/> の結果。本体の走査の戻り値と、走査が送った最後の報告をそのまま持つ。
/// </summary>
public sealed class FinalProgressOutcome
{
    /// <summary>本体の RunScan の戻り値（走査結果のルートノード）。</summary>
    public global::LargeFolderFinder.FolderInfo? Root { get; }

    /// <summary>受け取った最後の報告（<c>IsFinal</c> が真）。1件も届かなければ null。</summary>
    public global::LargeFolderFinder.ScanProgress? FinalProgress { get; }

    /// <summary>受け取った最後の報告の件数。正常な完了なら1になるはず。</summary>
    public int FinalReportCount { get; }

    /// <summary>
    /// FinalProgressOutcome を構築する。
    /// </summary>
    public FinalProgressOutcome(
        global::LargeFolderFinder.FolderInfo? root,
        global::LargeFolderFinder.ScanProgress? finalProgress,
        int finalReportCount)
    {
        Root = root;
        FinalProgress = finalProgress;
        FinalReportCount = finalReportCount;
    }
}

/// <summary>
/// <see cref="ScanRunner.RunWithForcedMethod"/> の結果。走査の方式を指定して走らせたときの結果・最後の報告と、
/// その走査の間に本体のログへ増えた文字列を持つ（ntfs-mft-scan 要件2.2, 2.3）。
/// </summary>
/// <param name="Root">本体の RunScan の戻り値（走査結果のルートノード）</param>
/// <param name="FinalProgress">受け取った最後の報告（<c>IsFinal</c> が真）。1件も届かなければ null</param>
/// <param name="FinalReportCount">受け取った最後の報告の件数。正常な完了なら1になるはず</param>
/// <param name="ProgressReportCount">受け取った途中の報告の件数</param>
/// <param name="InterimMethodReportCount">途中の報告に既定でない走査の方式が載っていた回数</param>
/// <param name="AddedLogText">この走査の間に本体のログへ増えた文字列（読めなければ空文字列）</param>
public sealed record ForcedMethodOutcome(
    global::LargeFolderFinder.FolderInfo? Root,
    global::LargeFolderFinder.ScanProgress? FinalProgress,
    int FinalReportCount,
    int ProgressReportCount,
    int InterimMethodReportCount,
    string AddedLogText);

/// <summary>
/// <see cref="ScanRunner.RunUntilCancelled"/> の結果。走査がどう終わったかと、届いた報告の件数を持つ
/// （scan-performance 要件1.6）。
/// </summary>
public sealed class CancelledScanOutcome
{
    /// <summary>走査が <see cref="OperationCanceledException"/>（およびその派生）で終わったかどうか。</summary>
    public bool Cancelled { get; }

    /// <summary>取り消し以外の例外で終わったときの、その例外。取り消しまたは正常終了なら null。</summary>
    public Exception? Failure { get; }

    /// <summary>受け取った最後の報告（<c>IsFinal</c> が真）の件数。取り消されたなら0のはず。</summary>
    public int FinalReportCount { get; }

    /// <summary>受け取った途中の報告の件数。取り消しの引き金が実際に引かれたことの裏づけに用いる。</summary>
    public int ProgressReportCount { get; }

    /// <summary>
    /// CancelledScanOutcome を構築する。
    /// </summary>
    public CancelledScanOutcome(bool cancelled, Exception? failure, int finalReportCount, int progressReportCount)
    {
        Cancelled = cancelled;
        Failure = failure;
        FinalReportCount = finalReportCount;
        ProgressReportCount = progressReportCount;
    }
}

/// <summary>
/// <see cref="ScanRunner.RunWithConcurrentFilterReads"/> の結果。走査の戻り値と、走査と並行に行った読み取りの記録を持つ。
/// </summary>
public sealed class ConcurrentReadOutcome
{
    /// <summary>本体の RunScan の戻り値（走査結果のルートノード）。</summary>
    public global::LargeFolderFinder.FolderInfo? Root { get; }

    /// <summary>進捗で渡り、読み取りに使った途中のツリーのルートノード。渡らなければ null。</summary>
    public global::LargeFolderFinder.FolderInfo? ReadRoot { get; }

    /// <summary>フィルタの前処理をかけた回数。</summary>
    public int ReadCount { get; }

    /// <summary>そのうち、始まりから終わりまで走査が続いていた（走査と並行に読めた）回数。</summary>
    public int ReadsWhileScanning { get; }

    /// <summary>読み取りで起きた例外（上限まで）。壊れずに読めていれば空。</summary>
    public IReadOnlyList<Exception> ReadFailures { get; }

    /// <summary>
    /// ConcurrentReadOutcome を構築する。
    /// </summary>
    public ConcurrentReadOutcome(
        global::LargeFolderFinder.FolderInfo? root,
        global::LargeFolderFinder.FolderInfo? readRoot,
        int readCount,
        int readsWhileScanning,
        IReadOnlyList<Exception> readFailures)
    {
        Root = root;
        ReadRoot = readRoot;
        ReadCount = readCount;
        ReadsWhileScanning = readsWhileScanning;
        ReadFailures = readFailures;
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
    /// 本体の事前カウント（<see cref="global::LargeFolderFinder.Scanner.CountFoldersAsync"/>）を
    /// 深さの上限 <paramref name="maxDepth"/> で呼び、その数をそのまま返す（scan-correctness 要件1.1, 1.2, 2.1）。
    /// 本体に触れる呼び出しをこの層に閉じ込めるための入口であり、数に手を加えない。
    /// </summary>
    /// <param name="rootPath">数える起点のフォルダのパス。実在している必要がある。</param>
    /// <param name="maxDepth">深さの上限。起点を深さ0とする。</param>
    /// <returns>起点を含む、深さ0〜<paramref name="maxDepth"/> のフォルダ数。</returns>
    public int CountFolders(string rootPath, int maxDepth)
    {
        if (string.IsNullOrEmpty(rootPath))
        {
            throw new ArgumentException("基準フォルダのパスが空です。", nameof(rootPath));
        }

        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"基準フォルダが見つかりません: {rootPath}");
        }

        // RunScan の呼び出しと同じく、呼び出し元のコンテキストに関わらずデッドロックしないよう
        // スレッドプール上に切り離してから同期的に待機する。
        var countTask = Task.Run(() => global::LargeFolderFinder.Scanner.CountFoldersAsync(
            rootPath,
            maxDepth,
            CancellationToken.None));

        return countTask.GetAwaiter().GetResult();
    }

    /// <summary>
    /// 本体の OS 呼び出しの宣言をまとめた型（<c>LargeFolderFinder.Win32</c>）が宣言している
    /// メンバー（メソッド・フィールド・入れ子の型）の名前を、反射で取得して返す（scan-correctness 要件2.1, 2.2）。
    /// 型は internal のため、本体のアセンブリから型名で取得する（InternalsVisibleTo は使わない）。
    /// 本体に触れる呼び出しをこの層に閉じ込めるための入口であり、名前に手を加えない。
    /// </summary>
    /// <returns>宣言されたメンバーの名前の集合（継承したメンバーは含まない）。</returns>
    /// <exception cref="TypeLoadException">本体のアセンブリに型が見つからないとき。</exception>
    public static IReadOnlySet<string> GetWin32DeclaredMemberNames()
    {
        const BindingFlags DeclaredOnly = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        Type win32 = typeof(global::LargeFolderFinder.Scanner).Assembly.GetType(
            "LargeFolderFinder.Win32",
            throwOnError: true)!;

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (MemberInfo member in win32.GetMembers(DeclaredOnly))
        {
            names.Add(member.Name);
        }

        return names;
    }

    /// <summary>
    /// 本体の走査（<see cref="global::LargeFolderFinder.Scanner.RunScan"/>）を深さの上限 <paramref name="maxDepth"/> で呼び、
    /// 走査が送った最後の報告（<c>IsFinal</c> が真の進捗）を受け取って返す（scan-correctness 要件1.4, 5.3）。
    /// 本体に触れる呼び出しをこの層に閉じ込めるための入口であり、報告の中身に手を加えない。
    /// </summary>
    /// <remarks>
    /// 進捗は <see cref="Progress{T}"/> ではなく、報告されたその場で記録する同期の受け手で受ける。
    /// <see cref="Progress{T}"/> は同期コンテキストの無いスレッドでは報告をスレッドプールへ投げるため、
    /// RunScan の完了の時点で最後の報告がまだ届いていない（あるいは完了の後に届く）競合が起きる。
    /// </remarks>
    /// <param name="rootPath">走査の起点のフォルダのパス。実在している必要がある。</param>
    /// <param name="maxDepth">深さの上限。起点を深さ0とする。事前カウントと同じ値を渡す。</param>
    /// <param name="useParallel">並列で走査するかどうか。</param>
    /// <param name="tuning">
    /// 走査の調整値（ワーカー数・列挙のバッファの大きさ）。null なら本体の既定（自動）。
    /// 並列度を指定して確かめる項目（scan-performance 要件1.3, 3.1, 3.2）のために渡す。
    /// </param>
    public FinalProgressOutcome RunForFinalProgress(
        string rootPath,
        int maxDepth,
        bool useParallel,
        global::LargeFolderFinder.ScanTuning? tuning = null)
    {
        if (string.IsNullOrEmpty(rootPath))
        {
            throw new ArgumentException("基準フォルダのパスが空です。", nameof(rootPath));
        }

        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"基準フォルダが見つかりません: {rootPath}");
        }

        var recorder = new SynchronousProgressRecorder();

        // Run と同じく、呼び出し元のコンテキストに関わらずデッドロックしないようスレッドプール上に切り離して待つ
        var scanTask = Task.Run(() => global::LargeFolderFinder.Scanner.RunScan(
            rootPath,
            thresholdBytes: 0L,
            totalFolders: 0,
            maxDepth: maxDepth,
            useParallel: useParallel,
            usePhysicalSize: false,
            progress: recorder,
            token: CancellationToken.None,
            tuning: tuning));

        global::LargeFolderFinder.FolderInfo? root = scanTask.GetAwaiter().GetResult();

        return new FinalProgressOutcome(root, recorder.LastFinal, recorder.FinalCount);
    }

    /// <summary>
    /// 本体の走査（<see cref="global::LargeFolderFinder.Scanner.RunScan"/>）を、走査の方式を指定して走らせ、
    /// 最後の報告と、その走査の間に本体のログへ増えた文字列を返す（ntfs-mft-scan 要件2.2, 2.3）。
    /// 本体に触れる呼び出しをこの層に閉じ込めるための入口であり、報告もログの文言も加工しない。
    /// </summary>
    /// <remarks>
    /// 方式の指定は走査の調整値（<c>ScanTuning.ForcedMethod</c>）で渡す。試験と計測のための指定であり、
    /// 画面からは渡らない。<paramref name="forcedMethod"/> に null を渡すと、方式は本体の決め方に委ねられる。
    /// ログは走査の前後の中身を比べ、増えた分だけを返す（この走査が書いた行だけを見られるようにするため）。
    /// 報告は <see cref="Progress{T}"/> ではなく同期の受け手で受ける。理由は
    /// <see cref="RunForFinalProgress"/> と同じ（スレッドプールへ投げると届く時点が競合する）。
    /// </remarks>
    /// <param name="rootPath">走査の起点のフォルダのパス。実在している必要がある。</param>
    /// <param name="forcedMethod">指定する走査の方式。null なら指定しない（本体の決め方に従う）。</param>
    public ForcedMethodOutcome RunWithForcedMethod(
        string rootPath,
        global::LargeFolderFinder.ScanMethodKind? forcedMethod)
    {
        if (string.IsNullOrEmpty(rootPath))
        {
            throw new ArgumentException("基準フォルダのパスが空です。", nameof(rootPath));
        }

        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"基準フォルダが見つかりません: {rootPath}");
        }

        var recorder = new MethodAwareProgressRecorder();
        string logBefore = ReadCurrentLogText();

        // 他の入口と同じく、呼び出し元のコンテキストに関わらずデッドロックしないようスレッドプール上に切り離して待つ
        var scanTask = Task.Run(() => global::LargeFolderFinder.Scanner.RunScan(
            rootPath,
            thresholdBytes: 0L,
            totalFolders: 0,
            maxDepth: int.MaxValue,
            useParallel: true,
            usePhysicalSize: false,
            progress: recorder,
            token: CancellationToken.None,
            tuning: new global::LargeFolderFinder.ScanTuning(0, 0, forcedMethod)));

        global::LargeFolderFinder.FolderInfo? root = scanTask.GetAwaiter().GetResult();

        string logAfter = ReadCurrentLogText();
        string addedLog = logAfter.StartsWith(logBefore, StringComparison.Ordinal)
            ? logAfter.Substring(logBefore.Length)
            : logAfter;

        return new ForcedMethodOutcome(
            root,
            recorder.LastFinal,
            recorder.FinalCount,
            recorder.ProgressCount,
            recorder.InterimMethodReportCount,
            addedLog);
    }

    /// <summary>
    /// 本体の現在のログファイルの中身を、書き込み中でも読める共有の指定で読む。読めなければ空文字列を返す。
    /// </summary>
    private static string ReadCurrentLogText()
    {
        string path = global::LargeFolderFinder.Logger.CurrentLogFilePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (IOException)
        {
            // 意図して無視: ログを読めないときは「増えた分が無い」として扱い、判定は呼び出し側（自己検証）に委ねる
            return string.Empty;
        }
    }

    /// <summary>
    /// 本体の走査（<see cref="global::LargeFolderFinder.Scanner.RunScan"/>）を走らせ、
    /// **走査が途中である時点**（最初の途中の報告が届いた時点）で取り消して、その終わり方を返す
    /// （scan-performance 要件1.6）。本体に触れる呼び出しをこの層に閉じ込めるための入口である。
    /// </summary>
    /// <remarks>
    /// 取り消しは進捗の受け手の中で行う。途中の報告は走査のスレッドの上でその場で呼ばれるため、
    /// 「走査がまだ続いている時点で取り消す」ことが時間に頼らずに決まる
    /// （最初の報告は最初のフォルダを終えた時点で送られる）。
    /// 報告は <see cref="Progress{T}"/> ではなく同期の受け手で受ける。理由は
    /// <see cref="RunForFinalProgress"/> と同じ（スレッドプールへ投げると届く時点が競合する）。
    /// </remarks>
    /// <param name="rootPath">走査の起点のフォルダのパス。実在している必要がある。取り消しが確実に途中で起きるよう、十分な大きさの木を渡す。</param>
    /// <param name="useParallel">並列で走査するかどうか。</param>
    /// <param name="tuning">走査の調整値。null なら本体の既定（自動）。</param>
    public CancelledScanOutcome RunUntilCancelled(
        string rootPath,
        bool useParallel,
        global::LargeFolderFinder.ScanTuning? tuning = null)
    {
        if (string.IsNullOrEmpty(rootPath))
        {
            throw new ArgumentException("基準フォルダのパスが空です。", nameof(rootPath));
        }

        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"基準フォルダが見つかりません: {rootPath}");
        }

        using (var cts = new CancellationTokenSource())
        {
            var recorder = new CancellingProgressRecorder(cts);

            // 走らせる側は取り消さない（取り消すのは走査に渡した token のみ）ため、
            // Task.Run には CancellationToken.None を渡す
            var scanTask = Task.Run(
                () => global::LargeFolderFinder.Scanner.RunScan(
                    rootPath,
                    thresholdBytes: 0L,
                    totalFolders: 0,
                    maxDepth: int.MaxValue,
                    useParallel: useParallel,
                    usePhysicalSize: false,
                    progress: recorder,
                    token: cts.Token,
                    tuning: tuning),
                CancellationToken.None);

            bool cancelled = false;
            Exception? failure = null;

            try
            {
                _ = scanTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // 取り消しの例外（TaskCanceledException を含む）。期待する終わり方
                cancelled = true;
            }
            catch (Exception ex)
            {
                // 取り消し以外の終わり方は隠さず、そのまま呼び出し元へ事実として渡す
                failure = ex;
            }

            return new CancelledScanOutcome(cancelled, failure, recorder.FinalCount, recorder.ProgressCount);
        }
    }

    /// <summary>
    /// 最初の途中の報告を受け取った時点で走査を取り消す受け手。報告の件数も数える。
    /// </summary>
    private sealed class CancellingProgressRecorder : IProgress<global::LargeFolderFinder.ScanProgress>
    {
        private readonly CancellationTokenSource _cts;

        /// <summary>並列の走査から並行に呼ばれうるため、件数をこのロックで守る</summary>
        private readonly object _gate = new object();

        private int _finalCount;
        private int _progressCount;

        /// <summary>取り消しを1回だけ行うための印（0 なら未実行）</summary>
        private int _cancelRequested;

        public CancellingProgressRecorder(CancellationTokenSource cts)
        {
            _cts = cts ?? throw new ArgumentNullException(nameof(cts));
        }

        /// <summary>受け取った最後の報告（<c>IsFinal</c>）の件数</summary>
        public int FinalCount
        {
            get { lock (_gate) { return _finalCount; } }
        }

        /// <summary>受け取った途中の報告の件数</summary>
        public int ProgressCount
        {
            get { lock (_gate) { return _progressCount; } }
        }

        /// <inheritdoc />
        public void Report(global::LargeFolderFinder.ScanProgress value)
        {
            if (value is null)
            {
                return;
            }

            lock (_gate)
            {
                if (value.IsFinal)
                {
                    _finalCount++;
                    return;
                }

                _progressCount++;
            }

            if (Interlocked.Exchange(ref _cancelRequested, 1) == 0)
            {
                _cts.Cancel();
            }
        }
    }

    /// <summary>
    /// 進捗の報告をその場で記録し、最後の報告の中身と件数に加えて、
    /// 途中の報告に走査の方式が載っていた回数も数える受け手（ntfs-mft-scan 要件2.3）。
    /// </summary>
    /// <remarks>
    /// 走査の方式は最後の報告だけで意味を持つ（design.md: Models / ScanProgress）。
    /// 途中の報告に方式が載っていないことを確かめるため、既定でない方式が載った途中の報告を数える。
    /// </remarks>
    private sealed class MethodAwareProgressRecorder : IProgress<global::LargeFolderFinder.ScanProgress>
    {
        /// <summary>並列の走査から並行に呼ばれうるため、記録をこのロックで守る</summary>
        private readonly object _gate = new object();

        private global::LargeFolderFinder.ScanProgress? _lastFinal;
        private int _finalCount;
        private int _progressCount;
        private int _interimMethodReportCount;

        /// <summary>受け取った最後の報告のうち最新のもの。1件も無ければ null</summary>
        public global::LargeFolderFinder.ScanProgress? LastFinal
        {
            get { lock (_gate) { return _lastFinal; } }
        }

        /// <summary>受け取った最後の報告（<c>IsFinal</c>）の件数</summary>
        public int FinalCount
        {
            get { lock (_gate) { return _finalCount; } }
        }

        /// <summary>受け取った途中の報告の件数</summary>
        public int ProgressCount
        {
            get { lock (_gate) { return _progressCount; } }
        }

        /// <summary>途中の報告に既定でない走査の方式が載っていた回数</summary>
        public int InterimMethodReportCount
        {
            get { lock (_gate) { return _interimMethodReportCount; } }
        }

        /// <inheritdoc />
        public void Report(global::LargeFolderFinder.ScanProgress value)
        {
            if (value is null)
            {
                return;
            }

            lock (_gate)
            {
                if (value.IsFinal)
                {
                    _lastFinal = value;
                    _finalCount++;
                    return;
                }

                _progressCount++;
                if (value.Method != global::LargeFolderFinder.ScanMethodKind.NormalEnumeration)
                {
                    _interimMethodReportCount++;
                }
            }
        }
    }

    /// <summary>
    /// 進捗の報告を、報告されたスレッドの上でその場で記録する受け手。最後の報告（<c>IsFinal</c>）の件数と中身を持つ。
    /// </summary>
    private sealed class SynchronousProgressRecorder : IProgress<global::LargeFolderFinder.ScanProgress>
    {
        /// <summary>並列の走査から並行に呼ばれうるため、記録をこのロックで守る</summary>
        private readonly object _gate = new object();

        private global::LargeFolderFinder.ScanProgress? _lastFinal;
        private int _finalCount;

        /// <summary>受け取った最後の報告のうち最新のもの。1件も無ければ null</summary>
        public global::LargeFolderFinder.ScanProgress? LastFinal
        {
            get { lock (_gate) { return _lastFinal; } }
        }

        /// <summary>受け取った最後の報告の件数</summary>
        public int FinalCount
        {
            get { lock (_gate) { return _finalCount; } }
        }

        /// <inheritdoc />
        public void Report(global::LargeFolderFinder.ScanProgress value)
        {
            if (value is null || !value.IsFinal)
            {
                return;
            }

            lock (_gate)
            {
                _lastFinal = value;
                _finalCount++;
            }
        }
    }

    /// <summary>
    /// 本体の走査（<see cref="global::LargeFolderFinder.Scanner.RunScan"/>）を実行しながら、進捗で渡る途中のツリー（<c>CurrentResult</c>）に
    /// 結果の整形のフィルタの前処理（<see cref="global::LargeFolderFinder.ResultFormatter.BuildFilterCache"/>）を
    /// 走査が終わるまで別のスレッドで繰り返しかけ、読み取りで起きた例外と回数を返す（scan-correctness 要件3.1, 3.2）。
    /// 本体に触れる呼び出しをこの層に閉じ込めるための入口であり、整形の中身には手を加えない。
    /// </summary>
    /// <remarks>
    /// 途中のツリーは、走査が最初のフォルダを終えたときの最初の報告（その後は5秒ごと）で渡る。
    /// 読み取りはその時点から走査の完了まで続けるため、走査が5秒以内に終わっても並行の読み取りが起きる。
    /// 読み取りのたびに並べ替えの鍵・向き・ファイルを含めるかを切り替え、整形の全ての並べ替えの経路を通す。
    /// </remarks>
    /// <param name="rootPath">走査の起点のフォルダのパス。実在している必要がある。</param>
    /// <param name="useParallel">並列で走査するかどうか。</param>
    public ConcurrentReadOutcome RunWithConcurrentFilterReads(string rootPath, bool useParallel)
    {
        if (string.IsNullOrEmpty(rootPath))
        {
            throw new ArgumentException("基準フォルダのパスが空です。", nameof(rootPath));
        }

        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"基準フォルダが見つかりません: {rootPath}");
        }

        var tap = new PartialTreeTap();

        // Run と同じく、呼び出し元のコンテキストに関わらずデッドロックしないようスレッドプール上に切り離して走らせる
        var scanTask = Task.Run(() => global::LargeFolderFinder.Scanner.RunScan(
            rootPath,
            thresholdBytes: 0L,
            totalFolders: 0,
            maxDepth: ConcurrentReadMaxDepth,
            useParallel: useParallel,
            usePhysicalSize: false,
            progress: tap,
            token: CancellationToken.None));

        // 読み取りは走査と並行に進めるため、専用のスレッドで行う
        var readTask = Task.Factory.StartNew(
            () => ReadPartialTreeUntilScanEnds(tap, scanTask),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        global::LargeFolderFinder.FolderInfo? root = scanTask.GetAwaiter().GetResult();
        ConcurrentReadOutcome reads = readTask.GetAwaiter().GetResult();

        return new ConcurrentReadOutcome(root, reads.ReadRoot, reads.ReadCount, reads.ReadsWhileScanning, reads.ReadFailures);
    }

    /// <summary>並行の読み取りの検証で走査に渡す深さの上限。本体の走査は深さで打ち切らず、数え方にだけ使う</summary>
    private const int ConcurrentReadMaxDepth = 64;

    /// <summary>並行の読み取りで控えておく例外の件数の上限（同じ例外が大量に続く場合に備える）</summary>
    private const int ConcurrentReadMaxRecordedFailures = 20;

    /// <summary>
    /// 途中のツリーが渡るのを待ち、走査が終わるまでそのツリーに結果の整形のフィルタの前処理を繰り返しかける。
    /// </summary>
    private static ConcurrentReadOutcome ReadPartialTreeUntilScanEnds(PartialTreeTap tap, Task scanTask)
    {
        // 途中のツリーが渡るか、走査が（渡す前に）終わるまで待つ
        while (!tap.TreeAvailable.Wait(1))
        {
            if (scanTask.IsCompleted)
            {
                break;
            }
        }

        global::LargeFolderFinder.FolderInfo? tree = tap.Tree;
        var failures = new List<Exception>();
        int readCount = 0;
        int readsWhileScanning = 0;

        if (tree == null)
        {
            return new ConcurrentReadOutcome(null, null, readCount, readsWhileScanning, failures);
        }

        var formatter = new global::LargeFolderFinder.ResultFormatter();
        var sortTargets = (global::LargeFolderFinder.AppConstants.SortTarget[])Enum.GetValues(typeof(global::LargeFolderFinder.AppConstants.SortTarget));
        var sortDirections = (global::LargeFolderFinder.AppConstants.SortDirection[])Enum.GetValues(typeof(global::LargeFolderFinder.AppConstants.SortDirection));

        while (true)
        {
            bool scanningBefore = !scanTask.IsCompleted;

            try
            {
                // 描画と同じく、読み取りのたびに新しい写しの入れ物を作る
                var cache = new System.Collections.Concurrent.ConcurrentDictionary<global::LargeFolderFinder.FolderInfo, List<global::LargeFolderFinder.FolderInfo>>();
                formatter.BuildFilterCache(
                    tree,
                    cache,
                    thresholdBytes: 0L,
                    includeFiles: readCount % 2 == 0,
                    sortTarget: sortTargets[readCount % sortTargets.Length],
                    sortDirection: sortDirections[(readCount / sortTargets.Length) % sortDirections.Length],
                    token: CancellationToken.None);
            }
            catch (Exception ex)
            {
                if (failures.Count < ConcurrentReadMaxRecordedFailures)
                {
                    failures.Add(ex);
                }
            }

            readCount++;

            // 読み取りの始まりから終わりまで走査が続いていた回だけを、並行に読めた回として数える
            bool scanningAfter = !scanTask.IsCompleted;
            if (scanningBefore && scanningAfter)
            {
                readsWhileScanning++;
            }

            if (!scanningAfter)
            {
                break;
            }
        }

        return new ConcurrentReadOutcome(null, tree, readCount, readsWhileScanning, failures);
    }

    /// <summary>
    /// 進捗の報告のうち、途中のツリー（<c>CurrentResult</c>）が載った最初の報告からツリーを受け取る受け手。
    /// 報告は走査のスレッドで呼ばれるため、ここでは受け取って知らせるだけにし、走査を止めない。
    /// </summary>
    private sealed class PartialTreeTap : IProgress<global::LargeFolderFinder.ScanProgress>
    {
        private global::LargeFolderFinder.FolderInfo? _tree;

        /// <summary>途中のツリーを受け取ったときに立つ印</summary>
        public ManualResetEventSlim TreeAvailable { get; } = new ManualResetEventSlim(false);

        /// <summary>受け取った途中のツリー。まだ受け取っていなければ null</summary>
        public global::LargeFolderFinder.FolderInfo? Tree => Volatile.Read(ref _tree);

        /// <inheritdoc />
        public void Report(global::LargeFolderFinder.ScanProgress value)
        {
            if (value is null || value.IsFinal || value.CurrentResult is null)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _tree, value.CurrentResult, null) == null)
            {
                TreeAvailable.Set();
            }
        }
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
                // 本体も再解析ポイントは辿らない（DirectoryWalker 参照）。
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
