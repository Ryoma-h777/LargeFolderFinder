using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LargeFolderFinder.GoldenBaseline.Compare;
using LargeFolderFinder.GoldenBaseline.Fixture;
using LargeFolderFinder.GoldenBaseline.Io;
using LargeFolderFinder.GoldenBaseline.Model;
using LargeFolderFinder.GoldenBaseline.Scan;
using LargeFolderFinder.GoldenBaseline.SelfCheck;

namespace LargeFolderFinder.GoldenBaseline;

/// <summary>
/// GoldenBaseline ツールのエントリポイント。
/// サブコマンドの振り分けと終了コードの決定のみを担う（design.md: CLI/Program）。
/// </summary>
internal static class Program
{
    /// <summary>判定結果: 一致（design.md: Program Responsibilities）。</summary>
    private const int ExitCodeMatch = 0;

    /// <summary>判定結果: 差分あり（design.md: Program Responsibilities）。</summary>
    private const int ExitCodeDifferent = 1;

    /// <summary>判定結果: 設定不一致または実行時エラー（design.md: Program Responsibilities）。</summary>
    private const int ExitCodeError = 2;

    /// <summary>
    /// このツールが書き出す期待値データの形式バージョン。GoldenSerializer が読み書きできる版と一致させる
    /// （初版は 1。タスク7.1 で BaseFolderPathLength をヘッダに追加したため 2 へ上げた）。
    /// </summary>
    private const int GoldenFormatVersion = 2;

    /// <summary>既定の基準フォルダ名の接頭辞（tasks.md Implementation Notes の既存規約 gb_fix_*）。</summary>
    private const string DefaultRootPrefix = "gb_fix_";

    /// <summary>既定の基準フォルダ名の長さ調整に使う文字（乱数の後ろに並べる）。</summary>
    private const char DefaultRootPaddingChar = 'x';

    /// <summary>
    /// 既定の基準フォルダの実効絶対パス長（Path.GetFullPath 後の文字数）の固定値。
    /// 走査で項目が欠落する境界は「親フォルダの絶対パスが258文字以上だと、その直下を一覧できない」ことだけであり
    /// （2026-09-16 実測）、期待値データの内容は基準フォルダの長さに左右される。そのため長さを固定する。
    /// 80 を選ぶ理由は、現行の期待値（エントリ17件・既知の欠落4件）がそのまま再現する範囲が実効長 56〜104 文字で、
    /// その中央付近にあたり、ツールが異常終了する両端の長さ（54・55・105・106 文字）から十分離れているため。
    /// </summary>
    internal const int FixedBaseFolderPathLength = 80;

    private static int Main(string[] args)
    {
        // 標準出力・標準エラーのエンコーディングを明示的にUTF-8（BOMなし）へ固定する。
        // 既定（システムのOEMコードページ依存）のままだと、標準出力がリダイレクトされたとき
        // （このツール自身がサブプロセスとして起動され、自己検証から結果を読み取られる場合など）に
        // 読み取り側との既定エンコーディングの解決が食い違い、日本語の報告が文字化けすることがある。
        // 明示的に固定することで、起動方法（コンソール直接実行 / リダイレクト経由）に依存しない
        // 決定的な挙動にする。
        try
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (IOException)
        {
            // コンソールを持たない特殊な起動方法など、エンコーディングを変更できない環境では
            // 既定のエンコーディングのまま動作を継続する。
        }

        if (args.Length == 0 || args[0] == "--help")
        {
            PrintUsage();
            return ExitCodeMatch;
        }

        switch (args[0])
        {
            case "selfcheck":
                return RunSelfCheck();
            case "build-fixture":
                return RunBuildFixture(args);
            case "generate":
                return RunGenerate(args);
            case "compare":
                return RunCompare(args);
            case "update":
                return RunUpdate(args);
            default:
                Console.Error.WriteLine($"未知のコマンドです: {args[0]}");
                PrintUsage();
                return ExitCodeError;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("GoldenBaseline - Large Folder Finder の走査結果を検証するツール");
        Console.WriteLine();
        Console.WriteLine("使い方: GoldenBaseline <コマンド> [オプション]");
        Console.WriteLine();
        Console.WriteLine("コマンド:");
        Console.WriteLine("  selfcheck                登録済みの自己検証項目をまとめて実行する");
        Console.WriteLine("  build-fixture             標準フィクスチャを生成し、未生成の項目を報告してから後始末する");
        Console.WriteLine("  generate --out <path>    標準フィクスチャを走査し、期待値ファイルを新規に書き出す");
        Console.WriteLine("  compare --golden <path>  標準フィクスチャを走査し、期待値ファイルと比較する");
        Console.WriteLine("  update --golden <path>   現行の期待値との差分を提示したうえで期待値ファイルを更新する");
        Console.WriteLine("  --help                    この使い方を表示する");
        Console.WriteLine();
        Console.WriteLine("オプション:");
        Console.WriteLine($"  --root <path>     フィクスチャを生成する基準フォルダ。省略時は %TEMP% 配下に実効絶対パス長{FixedBaseFolderPathLength}文字で自動生成する");
        Console.WriteLine("  --out <path>      生成した期待値ファイルの書き出し先（generate のみ必須）");
        Console.WriteLine("  --golden <path>   比較・更新の対象とする期待値ファイル（compare / update のみ必須）");
        Console.WriteLine("  --physical-size   物理サイズ換算を有効にして走査する（省略時は無効）");
        Console.WriteLine();
        Console.WriteLine("終了コード: 0=一致 / 1=差分あり / 2=設定不一致または実行時エラー");
    }

    /// <summary>
    /// 登録済みの自己検証項目をすべて実行し、結果を標準出力へ報告する。
    /// 1件でも失敗があれば非ゼロの終了コードを返す。
    /// </summary>
    private static int RunSelfCheck()
    {
        var runner = new SelfCheckRunner();
        SelfChecks.Register(runner);
        var outcomes = runner.RunAll();

        foreach (var outcome in outcomes)
        {
            var mark = outcome.Passed ? "OK" : "NG";
            Console.WriteLine($"[{mark}] {outcome.Name}");
            if (!outcome.Passed)
            {
                Console.WriteLine($"      理由: {outcome.FailureReason}");
            }
        }

        int failureCount = outcomes.Count(o => !o.Passed);
        Console.WriteLine();
        Console.WriteLine($"{outcomes.Count} 件中 {failureCount} 件が失敗しました。");

        return failureCount == 0 ? 0 : 1;
    }

    // ==================================================================
    // build-fixture
    // ==================================================================

    /// <summary>
    /// 標準フィクスチャを単独で生成し、未生成の項目とその理由を報告してから後始末する（要件3.6）。
    /// 比較の概念を持たないため、完走した場合は常に一致相当（0）を返す。
    /// </summary>
    private static int RunBuildFixture(string[] args)
    {
        try
        {
            var (options, _) = ParseArgs(args, 1, new HashSet<string> { "--root" }, new HashSet<string>());
            string root = ResolveRoot(options);

            var spec = FixtureSpec.Standard;
            var builder = new FixtureBuilder();

            try
            {
                var buildResult = builder.Build(spec, root);
                PrintFixtureBuildReport(root, buildResult);
            }
            finally
            {
                // 期待値ファイル以外の副作用を残さない。フィクスチャは実行の最後に必ず後始末する
                // （design.md: Program Batch/Job Contract）。
                builder.TearDown(spec, root);
            }

            return ExitCodeMatch;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"エラー: {ex.Message}");
            return ExitCodeError;
        }
    }

    // ==================================================================
    // generate
    // ==================================================================

    /// <summary>
    /// 標準フィクスチャを生成・走査し、期待値ファイルを新規に書き出す
    /// （design.md System Flows「期待値の生成」）。
    /// </summary>
    private static int RunGenerate(string[] args)
    {
        try
        {
            var (options, flags) = ParseArgs(
                args,
                1,
                new HashSet<string> { "--root", "--out" },
                new HashSet<string> { "--physical-size" });

            string outPath = RequireOption(options, "--out");
            string root = ResolveRoot(options);
            bool usePhysicalSize = flags.Contains("--physical-size");

            ValidateOutputDirectory(outPath);

            var spec = FixtureSpec.Standard;
            var builder = new FixtureBuilder();

            try
            {
                var buildResult = builder.Build(spec, root);
                PrintFixtureBuildReport(root, buildResult);

                var scanRunner = new ScanRunner();
                var outcome = scanRunner.Run(root, usePhysicalSize);

                var header = BuildHeader(spec, root, usePhysicalSize, outcome.ClusterSizeInBytes, buildResult);

                var projector = new GoldenProjector();
                var document = projector.Project(outcome, header);

                Console.WriteLine();
                Console.WriteLine("[期待値の生成]");
                Console.WriteLine($"物理サイズ換算: {(usePhysicalSize ? "有効" : "無効")}（クラスタサイズ: {outcome.ClusterSizeInBytes} バイト）");
                Console.WriteLine($"スキップされた対象: {outcome.SkippedPaths.Count} 件");
                foreach (var skipped in outcome.SkippedPaths)
                {
                    Console.WriteLine($"  - {skipped}");
                }

                // 既知の欠落の識別結果は期待値の生成の報告に含める。独立したサブコマンドとしては公開しない
                // （tasks.md 5.1）。
                var analyzer = new KnownIssueAnalyzer();
                var findings = analyzer.Analyze(spec, document);
                Console.WriteLine($"既知の欠落（境界条件に由来）: {findings.Count} 件");
                foreach (var finding in findings)
                {
                    Console.WriteLine($"  - {finding.RelativePath}（原因: {finding.Trait}）");
                }

                var serializer = new GoldenSerializer();
                serializer.Write(document, outPath);
                Console.WriteLine($"期待値ファイルを書き出しました: {outPath}（エントリ数: {document.Entries.Count}）");
            }
            finally
            {
                builder.TearDown(spec, root);
            }

            return ExitCodeMatch;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"エラー: {ex.Message}");
            return ExitCodeError;
        }
    }

    // ==================================================================
    // compare / update
    // ==================================================================

    /// <summary>
    /// 標準フィクスチャを生成・走査し、既存の期待値ファイルと比較する
    /// （design.md System Flows「比較の判定」）。
    /// </summary>
    private static int RunCompare(string[] args)
    {
        try
        {
            var (options, flags) = ParseArgs(
                args,
                1,
                new HashSet<string> { "--root", "--golden" },
                new HashSet<string> { "--physical-size" });

            string goldenPath = RequireOption(options, "--golden");
            string root = ResolveRoot(options);
            bool usePhysicalSize = flags.Contains("--physical-size");

            var run = BuildComparisonRun(goldenPath, root, usePhysicalSize);

            Console.WriteLine();
            PrintDiffReport("[比較]", run.Report);

            return VerdictToExitCode(run.Report.Verdict);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"エラー: {ex.Message}");
            return ExitCodeError;
        }
    }

    /// <summary>
    /// 現行の期待値との差分を提示したうえで、標準フィクスチャの現在の走査結果で期待値ファイルを更新する
    /// （要件5.3, 5.4）。設定不一致のときは、以後の比較が無意味になるため更新しない。
    /// </summary>
    /// <remarks>
    /// タスク5.2（更新前に差分を提示する機能の拡張）は、この <see cref="BuildComparisonRun"/> が
    /// 既に差分（<see cref="DiffReport"/>）と更新後の内容（<see cref="ComparisonRun.Actual"/>）の
    /// 両方を書き出し前に保持している点を土台として拡張できる。
    /// </remarks>
    private static int RunUpdate(string[] args)
    {
        try
        {
            var (options, flags) = ParseArgs(
                args,
                1,
                new HashSet<string> { "--root", "--golden" },
                new HashSet<string> { "--physical-size" });

            string goldenPath = RequireOption(options, "--golden");
            string root = ResolveRoot(options);
            bool usePhysicalSize = flags.Contains("--physical-size");

            var run = BuildComparisonRun(goldenPath, root, usePhysicalSize);

            Console.WriteLine();
            PrintDiffReport("[更新前の差分]", run.Report);

            if (run.Report.Verdict == BaselineVerdict.SettingsMismatch)
            {
                Console.WriteLine("設定（物理サイズ換算の有無、または基準フォルダの実効絶対パス長）が一致しないため、期待値ファイルは更新しませんでした。");
                return VerdictToExitCode(run.Report.Verdict);
            }

            var serializer = new GoldenSerializer();
            serializer.Write(run.Actual, goldenPath);
            Console.WriteLine($"期待値ファイルを更新しました: {goldenPath}（エントリ数: {run.Actual.Entries.Count}）");

            return VerdictToExitCode(run.Report.Verdict);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"エラー: {ex.Message}");
            return ExitCodeError;
        }
    }

    /// <summary>
    /// compare / update が共有する、フィクスチャの生成・走査・射影・突き合わせの一連の処理。
    /// フィクスチャの後始末はこのメソッドの内部で必ず行う。
    /// </summary>
    private static ComparisonRun BuildComparisonRun(string goldenPath, string root, bool usePhysicalSize)
    {
        var serializer = new GoldenSerializer();

        // 期待値ファイルは、フィクスチャを組み立てる前に読み込む。読めない場合はフィクスチャを
        // 一切作らずに早期に失敗させる（Error Handling: 読めない期待値ファイル → 終了コード2）。
        GoldenDocument expected;
        try
        {
            expected = serializer.Read(goldenPath);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is GoldenFormatException)
        {
            throw new GoldenCliArgumentException($"期待値ファイルを読み込めません（{goldenPath}）: {ex.Message}");
        }

        var spec = FixtureSpec.Standard;
        var builder = new FixtureBuilder();

        GoldenDocument actual;
        try
        {
            var buildResult = builder.Build(spec, root);
            PrintFixtureBuildReport(root, buildResult);

            var scanRunner = new ScanRunner();
            var outcome = scanRunner.Run(root, usePhysicalSize);

            var header = BuildHeader(spec, root, usePhysicalSize, outcome.ClusterSizeInBytes, buildResult);

            var projector = new GoldenProjector();
            actual = projector.Project(outcome, header);
        }
        finally
        {
            builder.TearDown(spec, root);
        }

        // 走査条件の照合をエントリの突き合わせより先に行う（design.md System Flows「比較の判定」）。
        var comparer = new BaselineComparer();
        var report = comparer.Compare(expected, actual);

        return new ComparisonRun(expected, actual, report);
    }

    /// <summary>compare / update の突き合わせ結果一式。</summary>
    private sealed class ComparisonRun
    {
        public GoldenDocument Expected { get; }
        public GoldenDocument Actual { get; }
        public DiffReport Report { get; }

        public ComparisonRun(GoldenDocument expected, GoldenDocument actual, DiffReport report)
        {
            Expected = expected;
            Actual = actual;
            Report = report;
        }
    }

    private static int VerdictToExitCode(BaselineVerdict verdict)
    {
        switch (verdict)
        {
            case BaselineVerdict.Match:
                return ExitCodeMatch;
            case BaselineVerdict.Different:
                return ExitCodeDifferent;
            case BaselineVerdict.SettingsMismatch:
                return ExitCodeError;
            default:
                throw new InvalidOperationException($"未知の判定結果です: {verdict}");
        }
    }

    private static void PrintDiffReport(string title, DiffReport report)
    {
        Console.WriteLine(title);

        switch (report.Verdict)
        {
            case BaselineVerdict.Match:
                Console.WriteLine("判定: 一致");
                return;
            case BaselineVerdict.SettingsMismatch:
                // 物理サイズ換算の有無や基準フォルダの実効絶対パス長が異なると全エントリが不一致になり
                // 報告が無意味になるため、突き合わせを行わない（design.md System Flows「比較の判定」、要件6.2, 6.3、タスク7.1）。
                Console.WriteLine("判定: 設定不一致（物理サイズ換算の有無、または基準フォルダの実効絶対パス長が期待値データの生成時と異なります。エントリの突き合わせは行っていません）");
                return;
            case BaselineVerdict.Different:
                Console.WriteLine($"判定: 差分あり（{report.Entries.Count} 件）");
                foreach (var entry in report.Entries)
                {
                    Console.WriteLine($"  - [{entry.Kind}] {entry.RelativePath} 期待値={entry.ExpectedValue} 実際={entry.ActualValue}");
                }

                return;
            default:
                throw new InvalidOperationException($"未知の判定結果です: {report.Verdict}");
        }
    }

    // ==================================================================
    // 共通ヘルパー
    // ==================================================================

    /// <summary>
    /// 期待値データのヘッダを組み立てる。未生成項目は「相対パス: 例外の型名」の形だけを記録し、
    /// 例外メッセージ（基準フォルダの絶対パス・ユーザー名・OS の表示言語による文言を含む）は含めない。
    /// 詳細な理由は <see cref="PrintFixtureBuildReport"/> で標準出力にのみ出す
    /// （design.md: Program の責務、要件2.3・3.7、2026-09-15 ユーザー決定）。
    /// </summary>
    private static GoldenHeader BuildHeader(FixtureSpec spec, string root, bool usePhysicalSize, long clusterSizeInBytes, FixtureBuildResult buildResult)
    {
        return new GoldenHeader(
            GoldenFormatVersion,
            spec.Name,
            MeasureEffectivePathLength(root),
            DateTimeOffset.UtcNow,
            usePhysicalSize,
            clusterSizeInBytes,
            buildResult.IsComplete,
            buildResult.Omissions.Select(o => $"{o.RelativePath}: {o.ExceptionTypeName}").ToList());
    }

    private static void PrintFixtureBuildReport(string root, FixtureBuildResult result)
    {
        Console.WriteLine("[フィクスチャの生成]");
        Console.WriteLine($"基準フォルダ: {root}");

        if (result.IsComplete)
        {
            Console.WriteLine("すべての項目を生成しました。未生成の項目はありません。");
            return;
        }

        Console.WriteLine($"未生成の項目: {result.Omissions.Count} 件");
        foreach (var omission in result.Omissions)
        {
            Console.WriteLine($"  - {omission.RelativePath}: {omission.Reason}");
        }
    }

    /// <summary>
    /// 基準パスを解決する。明示指定があればそれを用い（長さは強制せず、実際の長さがヘッダに記録される）、
    /// なければ %TEMP% 配下に実効絶対パス長を固定した既定パスを生成する
    /// （design.md: FixtureBuilder.Build の Preconditions「基準側で長さを消費すると生成できる階層が浅くなる」、
    /// tasks.md Implementation Notes の既存規約 gb_fix_* に合わせる。長さの固定はタスク7.1）。
    /// </summary>
    internal static string ResolveRoot(IReadOnlyDictionary<string, string> options)
    {
        if (options.TryGetValue("--root", out var root))
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new GoldenCliArgumentException("--root の値が空です。");
            }

            // 明示指定された基準フォルダは、長さを強制せずそのまま尊重する。実際の長さはヘッダに記録される。
            return root;
        }

        return BuildDefaultRoot(Path.GetTempPath());
    }

    /// <summary>
    /// 既定の基準フォルダのパスを、指定された置き場の下に組み立てる。
    /// </summary>
    /// <remarks>
    /// 名前には乱数（GUID 由来）を残し、同時実行や後始末漏れとの衝突を避ける。長さの調整はその後ろのパディングで行い、
    /// 実効絶対パス長が常に <see cref="FixedBaseFolderPathLength"/> になるようにする（タスク7.1）。
    /// </remarks>
    internal static string BuildDefaultRoot(string tempDirectory)
    {
        string probe = Path.Combine(tempDirectory, DefaultRootPrefix + Guid.NewGuid().ToString("N").Substring(0, 8));
        int probeLength = MeasureEffectivePathLength(probe);
        int paddingLength = FixedBaseFolderPathLength - probeLength;

        if (paddingLength < 0)
        {
            throw new GoldenCliArgumentException(
                $"既定の置き場（{tempDirectory}）が長すぎるため、基準フォルダの実効絶対パス長を {FixedBaseFolderPathLength} 文字に固定できません" +
                $"（名前を最短にしても {probeLength} 文字になります）。" +
                $"--root に、実効絶対パス長（Path.GetFullPath 後の文字数）が {FixedBaseFolderPathLength} 文字になるパスを指定してください。");
        }

        string root = probe + new string(DefaultRootPaddingChar, paddingLength);

        // パディングの長さは実効長から逆算しているが、置き場の表記によっては正規化の結果が線形にならないこともありうる。
        // 期待値の内容を左右する値なので、組み立てた結果を測り直して固定値と一致することを確かめる。
        int actualLength = MeasureEffectivePathLength(root);
        if (actualLength != FixedBaseFolderPathLength)
        {
            throw new GoldenCliArgumentException(
                $"既定の基準フォルダの実効絶対パス長を {FixedBaseFolderPathLength} 文字に固定できませんでした（実際: {actualLength} 文字）。" +
                $"--root に、実効絶対パス長が {FixedBaseFolderPathLength} 文字になるパスを指定してください。");
        }

        return root;
    }

    /// <summary>
    /// パスの実効絶対パス長（文字数）を測る。8.3 短縮名（例: USERNA~1）や相対表記は .NET の正規化で展開されるため、
    /// 文字列そのままの長さではなく <see cref="Path.GetFullPath(string)"/> を通した後の文字数で測る（タスク7.1）。
    /// </summary>
    internal static int MeasureEffectivePathLength(string path)
    {
        return Path.GetFullPath(path).Length;
    }

    private static string RequireOption(IReadOnlyDictionary<string, string> options, string name)
    {
        if (!options.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new GoldenCliArgumentException($"オプション '{name}' の指定が必要です。");
        }

        return value;
    }

    /// <summary>
    /// GoldenSerializer.Write の Precondition（出力先ディレクトリが存在すること）を、
    /// 呼び出し前に検証する（design.md: Io/GoldenSerializer Preconditions）。
    /// </summary>
    private static void ValidateOutputDirectory(string outPath)
    {
        string directory = Path.GetDirectoryName(outPath) ?? string.Empty;
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            throw new GoldenCliArgumentException($"出力先ディレクトリが存在しません: {directory}");
        }
    }

    /// <summary>
    /// コマンドライン引数を解析する。フラグ（値を持たない）とオプション（値を1つ持つ）を区別する。
    /// </summary>
    /// <param name="args">プロセス全体の引数。</param>
    /// <param name="startIndex">サブコマンド名の次から解析を始めるインデックス。</param>
    /// <param name="valueOptionNames">値を1つ伴うオプション名の集合。</param>
    /// <param name="flagOptionNames">値を伴わないフラグ名の集合。</param>
    private static (Dictionary<string, string> Options, HashSet<string> Flags) ParseArgs(
        string[] args,
        int startIndex,
        HashSet<string> valueOptionNames,
        HashSet<string> flagOptionNames)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);

        int i = startIndex;
        while (i < args.Length)
        {
            string token = args[i];

            if (flagOptionNames.Contains(token))
            {
                flags.Add(token);
                i++;
                continue;
            }

            if (valueOptionNames.Contains(token))
            {
                if (i + 1 >= args.Length)
                {
                    throw new GoldenCliArgumentException($"オプション '{token}' には値の指定が必要です。");
                }

                options[token] = args[i + 1];
                i += 2;
                continue;
            }

            throw new GoldenCliArgumentException($"未知のオプションです: '{token}'");
        }

        return (options, flags);
    }

    /// <summary>
    /// コマンドライン引数の誤りを表す例外。呼び出し元（各 Run* メソッド）で捕捉し、
    /// 終了コード2として扱う（design.md Error Handling「入力の誤り」）。
    /// </summary>
    internal sealed class GoldenCliArgumentException : Exception
    {
        public GoldenCliArgumentException(string message) : base(message)
        {
        }
    }
}
