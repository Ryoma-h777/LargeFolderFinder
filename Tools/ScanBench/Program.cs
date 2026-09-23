using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LargeFolderFinder.ScanBench;

/// <summary>
/// 計測の指定（<c>&lt;path&gt; [--runs N] [--threads N] [--buffer BYTES] [--sequential] [--physical-size]
/// [--no-digest] [--label TEXT]</c>）の解釈の結果を表す不変のデータ型
/// （design.md: Components and Interfaces / Tools / ScanBench「使い方」）。
/// </summary>
/// <remarks>
/// 引数の解釈だけを切り出しているのは、ファイルシステムに触れずに解釈の正しさを確かめられるようにするためである。
/// 誤りは例外ではなく <see cref="ErrorMessage"/> として返し、入口が終了コードに変える。
/// </remarks>
internal sealed class BenchArguments
{
    /// <summary>繰り返しの回数を指定するオプションの名前。</summary>
    private const string RunsOption = "--runs";

    /// <summary>対象の説明を指定するオプションの名前。</summary>
    private const string LabelOption = "--label";

    /// <summary>走査の並列度（ワーカー数）を指定するオプションの名前。</summary>
    private const string ThreadsOption = "--threads";

    /// <summary>列挙のバッファの大きさ（バイト）を指定するオプションの名前。</summary>
    private const string BufferOption = "--buffer";

    /// <summary>逐次で走査することを指定するオプションの名前。</summary>
    private const string SequentialOption = "--sequential";

    /// <summary>物理サイズ換算を指定するオプションの名前。</summary>
    private const string PhysicalSizeOption = "--physical-size";

    /// <summary>要約値を作らないことを指定するオプションの名前。</summary>
    private const string NoDigestOption = "--no-digest";

    /// <summary>繰り返しの回数を省略したときの既定値。</summary>
    private const int DefaultRuns = 3;

    /// <summary>ラベルを省略したときの既定値。</summary>
    private const string DefaultLabel = "(ラベルなし)";

    private BenchArguments(
        string? rootPath,
        int runs,
        string label,
        bool sequential,
        bool usePhysicalSize,
        bool computeDigest,
        int threadCount,
        int enumerationBufferSize,
        string? errorMessage)
    {
        RootPath = rootPath;
        Runs = runs;
        Label = label;
        Sequential = sequential;
        UsePhysicalSize = usePhysicalSize;
        ComputeDigest = computeDigest;
        ThreadCount = threadCount;
        EnumerationBufferSize = enumerationBufferSize;
        ErrorMessage = errorMessage;
    }

    /// <summary>走査の起点のフォルダ。誤りがあれば null。</summary>
    public string? RootPath { get; }

    /// <summary>同じプロセスで繰り返し走査する回数。</summary>
    public int Runs { get; }

    /// <summary>出力に載せる対象の説明。利用者が付ける。</summary>
    public string Label { get; }

    /// <summary>逐次で走査するかどうか。</summary>
    public bool Sequential { get; }

    /// <summary>物理サイズ換算を適用するかどうか。</summary>
    public bool UsePhysicalSize { get; }

    /// <summary>
    /// 要約値を作るかどうか。<c>--no-digest</c> で偽になる。
    /// 要約値を作るための一覧がメモリの列を押し上げるため、メモリを比べたいときは偽にする。
    /// </summary>
    public bool ComputeDigest { get; }

    /// <summary>
    /// 走査に渡す並列度（ワーカー数）。0 は自動で、本体が対象から既定値を決める。
    /// 逐次の指定があるときは本体の規則で 1 になる（この値は無視される）。
    /// </summary>
    public int ThreadCount { get; }

    /// <summary>走査に渡す列挙のバッファの大きさ（バイト）。0 は .NET の既定。</summary>
    public int EnumerationBufferSize { get; }

    /// <summary>引数の誤りの説明。誤りが無ければ null。</summary>
    public string? ErrorMessage { get; }

    /// <summary>引数に誤りが無いかどうか。</summary>
    public bool IsValid => ErrorMessage == null;

    /// <summary>
    /// 計測の指定を解釈する。
    /// </summary>
    /// <param name="args">コマンド行の引数すべて。</param>
    /// <returns>解釈の結果。誤りがあれば <see cref="ErrorMessage"/> を伴う結果。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> が null の場合。</exception>
    public static BenchArguments Parse(IReadOnlyList<string> args)
    {
        if (args == null)
        {
            throw new ArgumentNullException(nameof(args));
        }

        string? rootPath = null;
        int runs = DefaultRuns;
        string? label = null;
        bool sequential = false;
        bool usePhysicalSize = false;
        bool computeDigest = true;
        int threadCount = 0;
        int enumerationBufferSize = 0;

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];

            switch (arg)
            {
                case SequentialOption:
                    sequential = true;
                    continue;

                case PhysicalSizeOption:
                    usePhysicalSize = true;
                    continue;

                case NoDigestOption:
                    computeDigest = false;
                    continue;

                case RunsOption:
                    if (i + 1 >= args.Count)
                    {
                        return Error($"{RunsOption} の値がありません。");
                    }

                    if (!int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedRuns)
                        || parsedRuns < 1)
                    {
                        return Error($"{RunsOption} には1以上の整数を指定してください: {args[i + 1]}");
                    }

                    runs = parsedRuns;
                    i++;
                    continue;

                case ThreadsOption:
                    if (i + 1 >= args.Count)
                    {
                        return Error($"{ThreadsOption} の値がありません。");
                    }

                    if (!int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedThreads)
                        || parsedThreads < 0)
                    {
                        return Error($"{ThreadsOption} には0以上の整数を指定してください（0 は自動）: {args[i + 1]}");
                    }

                    threadCount = parsedThreads;
                    i++;
                    continue;

                case BufferOption:
                    if (i + 1 >= args.Count)
                    {
                        return Error($"{BufferOption} の値がありません。");
                    }

                    if (!int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedBuffer)
                        || parsedBuffer < 0)
                    {
                        return Error($"{BufferOption} には0以上の整数（バイト）を指定してください（0 は .NET の既定）: {args[i + 1]}");
                    }

                    enumerationBufferSize = parsedBuffer;
                    i++;
                    continue;

                case LabelOption:
                    if (i + 1 >= args.Count)
                    {
                        return Error($"{LabelOption} の値がありません。");
                    }

                    label = args[i + 1];
                    i++;
                    continue;
            }

            if (arg.StartsWith("-", StringComparison.Ordinal))
            {
                // 打ち間違えた指定を黙って読み飛ばすと、意図と違う条件のまま計測を記録してしまう。
                return Error($"未知の引数です: {arg}");
            }

            if (rootPath != null)
            {
                // どちらを計測したいのかが定まらないため、片方を選ばずに誤りとする。
                return Error($"走査の起点が2回以上指定されています: {arg}");
            }

            rootPath = arg;
        }

        if (rootPath == null)
        {
            return Error("走査の起点のフォルダを指定してください。");
        }

        return new BenchArguments(
            rootPath,
            runs,
            label ?? DefaultLabel,
            sequential,
            usePhysicalSize,
            computeDigest,
            threadCount,
            enumerationBufferSize,
            null);
    }

    private static BenchArguments Error(string message)
    {
        return new BenchArguments(null, DefaultRuns, DefaultLabel, false, false, true, 0, 0, message);
    }
}

/// <summary>
/// 1回の走査で測った値。出力の1行のもとになる。
/// </summary>
internal sealed class RunMeasurement
{
    public RunMeasurement(
        long elapsedMilliseconds,
        long peakWorkingSetBytes,
        long managedHeapBytes,
        int? skippedCount,
        int? workerCount,
        int? peakConcurrentEnumerations,
        ResultDigest digest)
    {
        ElapsedMilliseconds = elapsedMilliseconds;
        PeakWorkingSetBytes = peakWorkingSetBytes;
        ManagedHeapBytes = managedHeapBytes;
        SkippedCount = skippedCount;
        WorkerCount = workerCount;
        PeakConcurrentEnumerations = peakConcurrentEnumerations;
        Digest = digest;
    }

    /// <summary>走査だけに要した時間（ミリ秒）。要約値の計算は含めない。</summary>
    public long ElapsedMilliseconds { get; }

    /// <summary>走査の直後に読んだプロセスの最大の作業セット（バイト）。</summary>
    public long PeakWorkingSetBytes { get; }

    /// <summary>走査の直後に読んだ管理ヒープの大きさ（バイト）。</summary>
    public long ManagedHeapBytes { get; }

    /// <summary>最後の報告が伝えたスキップの件数。報告が届かなかったときは null。</summary>
    public int? SkippedCount { get; }

    /// <summary>最後の報告が伝えた、実際に走査に使ったワーカーの数。報告が届かなかったときは null。</summary>
    public int? WorkerCount { get; }

    /// <summary>最後の報告が伝えた、同時に行われた列挙の数の最大。報告が届かなかったときは null。</summary>
    public int? PeakConcurrentEnumerations { get; }

    /// <summary>結果の木の要約値とノードの数。</summary>
    public ResultDigest Digest { get; }
}

/// <summary>
/// 画面を介さずに、同じ対象を同じ条件で繰り返し走査し、所要時間・メモリ・要約値を1行ずつ出す入口
/// （design.md: Components and Interfaces / Tools / ScanBench）。
/// </summary>
/// <remarks>
/// この道具は配布物には含まれない。アプリ本体の csproj は <c>Tools\**</c> を除いており、
/// <c>build/Publish.ps1</c> は本体の csproj だけを発行するため、発行物にこのツールは入らない。
///
/// 走査は読み取りしか行わない。対象のフォルダに書き込みや削除はしない。
/// </remarks>
internal static class Program
{
    /// <summary>完走したことを表す終了コード。</summary>
    private const int ExitCodeSuccess = 0;

    /// <summary>走査が失敗したことを表す終了コード。</summary>
    private const int ExitCodeScanFailed = 1;

    /// <summary>引数や前提の誤りで計測できないことを表す終了コード。</summary>
    private const int ExitCodeInvalidUsage = 2;

    /// <summary>1バイトを MB に直す割る数。</summary>
    private const double BytesPerMegabyte = 1024.0 * 1024.0;

    private static int Main(string[] args)
    {
        // 標準出力を UTF-8（BOM なし）へ固定する。既定のままだと出力をリダイレクトして記録に残すときに
        // 日本語の見出しが文字化けすることがある（GoldenBaseline と同じ扱い）。
        try
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (IOException)
        {
            // コンソールを持たない起動方法ではエンコーディングを変更できない。既定のまま続ける。
        }

        if (args.Length == 0 || args[0] == "--help")
        {
            PrintUsage();
            return args.Length == 0 ? ExitCodeInvalidUsage : ExitCodeSuccess;
        }

        try
        {
            return RunBenchCore(args);
        }
        catch (Exception ex)
        {
            // 想定外の例外で未定義の終了コードを返すと、呼び出し側から失敗を判定できなくなる。
            Console.Error.WriteLine($"計測中に予期しない例外が発生しました {ex.GetType().Name}: {ex.Message}");
            return ExitCodeScanFailed;
        }
    }

    /// <summary>
    /// 計測の本体。引数を解釈し、指定の回数だけ同じプロセスで走査して1行ずつ出す。
    /// </summary>
    /// <param name="args">コマンド行の引数すべて。</param>
    /// <returns>終了コード。</returns>
    private static int RunBenchCore(string[] args)
    {
        BenchArguments parsed = BenchArguments.Parse(args);
        if (!parsed.IsValid)
        {
            Console.Error.WriteLine(parsed.ErrorMessage);
            PrintUsage();
            return ExitCodeInvalidUsage;
        }

        string rootPath = parsed.RootPath!;
        if (!Directory.Exists(rootPath))
        {
            // 起点のパスは記録には出さないが、指定の誤りは利用者の手元で直す必要があるため標準エラーには出す。
            Console.Error.WriteLine($"走査の起点のフォルダが見つかりません: {rootPath}");
            return ExitCodeInvalidUsage;
        }

        PrintConditions(parsed);
        PrintHeader();

        for (int runIndex = 1; runIndex <= parsed.Runs; runIndex++)
        {
            RunMeasurement measurement = MeasureOnce(
                rootPath,
                parsed.Sequential,
                parsed.UsePhysicalSize,
                parsed.ComputeDigest,
                parsed.ThreadCount,
                parsed.EnumerationBufferSize);
            PrintMeasurement(parsed, runIndex, measurement);

            if (measurement.SkippedCount == null)
            {
                Console.Error.WriteLine($"{runIndex} 回目: 走査の最後の報告が届かなかったため、スキップ数を出せませんでした。");
            }
        }

        return ExitCodeSuccess;
    }

    /// <summary>
    /// 1回だけ走査し、所要時間・メモリ・スキップ数・要約値を測る。
    /// </summary>
    /// <param name="rootPath">走査の起点のフォルダ。</param>
    /// <param name="sequential">逐次で走査するかどうか。</param>
    /// <param name="usePhysicalSize">物理サイズ換算を適用するかどうか。クラスタサイズの扱いは本体に任せる。</param>
    /// <param name="computeDigest">要約値を作るかどうか。偽でもノードの数とメモリは従来どおり測る。</param>
    /// <param name="threadCount">走査に渡す並列度。0 は自動。</param>
    /// <param name="enumerationBufferSize">走査に渡す列挙のバッファの大きさ（バイト）。0 は .NET の既定。</param>
    /// <returns>この回の計測。</returns>
    private static RunMeasurement MeasureOnce(
        string rootPath,
        bool sequential,
        bool usePhysicalSize,
        bool computeDigest,
        int threadCount,
        int enumerationBufferSize)
    {
        // 前の回の結果の木と要約値の作業用の一覧を回収してから測る。こうしないと、前の回の残りが
        // この回の管理ヒープに混ざり、回ごとの比較にならない。時計の前に行うので所要時間には入らない。
        CollectGarbage();

        // 進捗は Progress<T> ではなく、報告されたその場で記録する同期の受け手で受ける。
        // Progress<T> は同期コンテキストの無いスレッドでは報告をスレッドプールへ投げるため、
        // 走査の完了の時点で最後の報告がまだ届いていない競合が起きる
        // （Tools/GoldenBaseline/Scan/ScanRunner.cs の RunForFinalProgress と同じ理由）。
        var recorder = new FinalProgressRecorder();

        var stopwatch = Stopwatch.StartNew();

        // 事前カウントはしない（totalFolders = 0、maxDepth = int.MaxValue）。抽出サイズの閾値も0にして
        // 表示条件を一切適用しない（design.md: ScanBench）。
        // RunScan は非同期メソッドである。呼び出し元のコンテキストに関わらずデッドロックしないよう、
        // Task.Run でスレッドプール上に切り離してから同期的に待つ。
        Task<global::LargeFolderFinder.FolderInfo?> scanTask = Task.Run(() => global::LargeFolderFinder.Scanner.RunScan(
            rootPath,
            thresholdBytes: 0L,
            totalFolders: 0,
            maxDepth: int.MaxValue,
            useParallel: !sequential,
            usePhysicalSize: usePhysicalSize,
            progress: recorder,
            token: CancellationToken.None,
            tuning: new global::LargeFolderFinder.ScanTuning(threadCount, enumerationBufferSize)));

        global::LargeFolderFinder.FolderInfo? root = scanTask.GetAwaiter().GetResult();

        stopwatch.Stop();

        if (root == null)
        {
            throw new InvalidOperationException("走査結果を取得できませんでした（本体の RunScan が null を返しました）。");
        }

        // メモリは時計を止めた直後に読む。最大の作業セットはプロセスの開始からの最大であり、
        // 同じプロセスで繰り返すと前の回の分を含む（回ごとの上限ではなく、その回までの上限を表す）。
        // 要約値を作る一覧も走査の結果と同じくらいの大きさになりうるため、2回目以降のこの値には
        // 前の回の要約値の計算の分も混ざる。数百万ファイルの対象でメモリを比べるときは --no-digest で
        // 要約値を作らずに測り（要件6.1 の比較を濁さない）、作業セットを回ごとに比べたいときは
        // さらに --runs 1 で1回ずつ別のプロセスとして測る。走査の結果として残るメモリだけを
        // 見たいときは、要約値の計算より前に測る管理ヒープの列を使う。
        long peakWorkingSet;
        using (var process = Process.GetCurrentProcess())
        {
            peakWorkingSet = process.PeakWorkingSet64;
        }

        // 走査の結果として残り続けるメモリを見るため、回収してから測る（要件6.1）。
        long managedHeap = GC.GetTotalMemory(forceFullCollection: true);

        // 要約値の計算は時計を止めた後、メモリを読んだ後に行う。計測の時間にもメモリの列にも含めない。
        // --no-digest のときは要約値のための一覧を持たずに数だけ数える。
        ResultDigest digest = ResultDigest.Compute(root, computeDigest);

        // ワーカー数と同時の列挙の最大は、走査の側が最後の報告にだけ載せる（途中の報告では 0）。
        global::LargeFolderFinder.ScanProgress? final = recorder.LastFinal;
        int? skippedCount = final?.Skipped.Count;

        return new RunMeasurement(
            stopwatch.ElapsedMilliseconds,
            peakWorkingSet,
            managedHeap,
            skippedCount,
            final?.WorkerCount,
            final?.PeakConcurrentEnumerations,
            digest);
    }

    /// <summary>
    /// 前の回の結果を回収する。世代をまたいで残った木を確実に手放すため2回呼ぶ。
    /// </summary>
    private static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>
    /// 計測の条件を、記録に残したときに読み手が条件を取り違えないように先に出す。
    /// 起点のパスは出さず、利用者が付けたラベルで表す。
    /// </summary>
    private static void PrintConditions(BenchArguments parsed)
    {
        Console.WriteLine(
            $"# ScanBench\t対象: {parsed.Label}" +
            $"\t走査: {(parsed.Sequential ? "逐次" : "並列")}" +
            $"\t物理サイズ換算: {(parsed.UsePhysicalSize ? "あり" : "なし")}" +
            $"\t要約値: {(parsed.ComputeDigest ? "作る" : "作らない（--no-digest。メモリの列を濁さないため）")}" +
            $"\t並列度: {FormatThreadCount(parsed.ThreadCount)}" +
            $"\t列挙のバッファ: {FormatBufferSize(parsed.EnumerationBufferSize)}" +
            $"\t回数: {parsed.Runs.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>出力の見出しの行を出す。列はタブで区切る。</summary>
    private static void PrintHeader()
    {
        Console.WriteLine(string.Join(
            "\t",
            "ラベル",
            "版",
            "回",
            "ワーカー数",
            "同時の列挙の最大",
            "バッファ",
            "所要時間(ms)",
            "フォルダ数",
            "ファイル数",
            "総バイト数",
            "スキップ数",
            "最大の作業セット(MB)",
            "走査後の管理ヒープ(MB)",
            "要約値"));
    }

    /// <summary>
    /// 1回の計測を1行で出す。パスそのものは出さない（公開リポジトリに記録を残すため）。
    /// </summary>
    /// <param name="parsed">計測の指定。</param>
    /// <param name="runIndex">何回目か。1から始まる。</param>
    /// <param name="measurement">この回の計測。</param>
    private static void PrintMeasurement(BenchArguments parsed, int runIndex, RunMeasurement measurement)
    {
        // 1回目は「初回」、2回目以降は「温まった」と区別する（design.md: ScanBench）。
        string phase = runIndex == 1 ? "初回" : "温まった";

        Console.WriteLine(string.Join(
            "\t",
            parsed.Label,
            global::LargeFolderFinder.AppInfo.Version,
            $"{runIndex.ToString(CultureInfo.InvariantCulture)}/{phase}",
            measurement.WorkerCount?.ToString(CultureInfo.InvariantCulture) ?? "-",
            measurement.PeakConcurrentEnumerations?.ToString(CultureInfo.InvariantCulture) ?? "-",
            FormatBufferSize(parsed.EnumerationBufferSize),
            measurement.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
            measurement.Digest.FolderCount.ToString(CultureInfo.InvariantCulture),
            measurement.Digest.FileCount.ToString(CultureInfo.InvariantCulture),
            measurement.Digest.TotalBytes.ToString(CultureInfo.InvariantCulture),
            measurement.SkippedCount?.ToString(CultureInfo.InvariantCulture) ?? "-",
            FormatMegabytes(measurement.PeakWorkingSetBytes),
            FormatMegabytes(measurement.ManagedHeapBytes),
            measurement.Digest.Value ?? "-"));
    }

    /// <summary>バイト数を MB の小数1桁で表す。</summary>
    private static string FormatMegabytes(long bytes)
    {
        return (bytes / BytesPerMegabyte).ToString("F1", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 列挙のバッファの大きさを表示の形にする。0 は .NET の既定なので「既定」と出す
    /// （記録を読む人が「バッファを指定して測った回」と取り違えないようにするため）。
    /// </summary>
    private static string FormatBufferSize(int bufferSize)
    {
        return bufferSize > 0 ? bufferSize.ToString(CultureInfo.InvariantCulture) : "既定";
    }

    /// <summary>
    /// 走査に渡した並列度を表示の形にする。0 は本体が場面から決めるので「自動」と出す。
    /// 実際に使われたワーカーの数は、最後の報告から取る「ワーカー数」の列で分かる。
    /// </summary>
    private static string FormatThreadCount(int threadCount)
    {
        return threadCount > 0 ? threadCount.ToString(CultureInfo.InvariantCulture) : "自動";
    }

    private static void PrintUsage()
    {
        Console.WriteLine("ScanBench - Large Folder Finder の走査を繰り返し測る計測の道具（配布物には含まれない）");
        Console.WriteLine();
        Console.WriteLine("使い方: ScanBench <path> [--runs N] [--threads N] [--buffer BYTES] [--sequential] [--physical-size]");
        Console.WriteLine("                  [--no-digest] [--label TEXT]");
        Console.WriteLine();
        Console.WriteLine("引数:");
        Console.WriteLine("  <path>             走査の起点のフォルダ。読み取りしか行わない");
        Console.WriteLine();
        Console.WriteLine("オプション:");
        Console.WriteLine("  --runs N           同じプロセスで繰り返す回数。省略時は3");
        Console.WriteLine("  --threads N        走査の並列度（ワーカー数）。0 は自動（本体が対象から既定値を決める）。省略時は0。");
        Console.WriteLine("                     --sequential と一緒に指定すると、本体の規則で逐次（1）が優先される");
        Console.WriteLine("  --buffer BYTES     列挙に渡すバッファの大きさ（バイト）。0 は .NET の既定。省略時は0");
        Console.WriteLine("  --sequential       逐次で走査する。省略時は並列");
        Console.WriteLine("  --physical-size    物理サイズ換算を適用する");
        Console.WriteLine("  --no-digest        要約値を作らず、要約値の列を「-」にする。数とメモリは従来どおり測る。");
        Console.WriteLine("                     数百万ファイルの対象でメモリの列を比べるときに使う");
        Console.WriteLine("  --label TEXT       出力に載せる対象の説明（例: 「システムドライブ全体」）。省略時は「(ラベルなし)」");
        Console.WriteLine("  --help             この使い方を表示する");
        Console.WriteLine();
        Console.WriteLine("出力: タブ区切りの1行ずつ。1回目は「初回」、2回目以降は「温まった」と区別する。");
        Console.WriteLine("      パスそのものは出さない。対象は --label で表す");
        Console.WriteLine();
        Console.WriteLine("並列度とバッファの列:");
        Console.WriteLine("  ワーカー数            走査が実際に使った専用ワーカーの数（最後の報告から取る）");
        Console.WriteLine("  同時の列挙の最大      同時に行われた列挙の数の最大。ワーカー数以下になる");
        Console.WriteLine("  バッファ              列挙に渡したバッファの大きさ。「既定」は .NET の既定");
        Console.WriteLine("  条件の行の「並列度: 自動」は、本体が対象から既定値を決めたことを表す。");
        Console.WriteLine("  --threads で固定したときは、条件の行にその値が出る（実際に使われた数はワーカー数の列で分かる）");
        Console.WriteLine();
        Console.WriteLine("メモリの列の読み方:");
        Console.WriteLine("  最大の作業セット      プロセスの開始からの最大。同じプロセスで繰り返すと前の回の分を含む。");
        Console.WriteLine("                        回ごとに比べたいときは --runs 1 で1回ずつ別のプロセスとして測る");
        Console.WriteLine("  走査後の管理ヒープ    走査の結果として残るメモリ。回ごとに独立して比べられる");
        Console.WriteLine("  いずれも要約値を作る前に測るが、要約値の一覧は作業セットの最大に残る。");
        Console.WriteLine("  メモリを比べる計測では --no-digest を使い、集計値の一致は別に要約値ありで確かめる");
        Console.WriteLine();
        Console.WriteLine("終了コード: 0=完走 / 1=走査が失敗 / 2=引数や前提の誤り");
    }

    /// <summary>
    /// 進捗の報告のうち最後の報告（<c>IsFinal</c>）を、報告されたスレッドの上でその場で記録する受け手。
    /// </summary>
    private sealed class FinalProgressRecorder : IProgress<global::LargeFolderFinder.ScanProgress>
    {
        /// <summary>並列の走査から並行に呼ばれうるため、記録をこのロックで守る。</summary>
        private readonly object _gate = new object();

        private global::LargeFolderFinder.ScanProgress? _lastFinal;

        /// <summary>受け取った最後の報告のうち最新のもの。1件も無ければ null。</summary>
        public global::LargeFolderFinder.ScanProgress? LastFinal
        {
            get { lock (_gate) { return _lastFinal; } }
        }

        /// <inheritdoc />
        public void Report(global::LargeFolderFinder.ScanProgress value)
        {
            if (value is null || !value.IsFinal)
            {
                // 途中の報告は計測に使わない。走査の妨げにならないよう、その場で何もせずに返る。
                return;
            }

            lock (_gate)
            {
                _lastFinal = value;
            }
        }
    }
}
