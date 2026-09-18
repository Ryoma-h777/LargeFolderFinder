using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LargeFolderFinder.LocalizationCheck.Check;
using LargeFolderFinder.LocalizationCheck.Io;
using LargeFolderFinder.LocalizationCheck.Model;
using LargeFolderFinder.LocalizationCheck.SelfCheck;

namespace LargeFolderFinder.LocalizationCheck;

/// <summary>
/// check コマンドの引数（<c>--dir &lt;path&gt;</c>）の解釈の結果を表す不変のデータ型
/// （design.md: Entry / Program の Batch / Job Contract「入力」）。
/// </summary>
/// <remarks>
/// 引数の解釈だけを切り出しているのは、ファイルシステムに触れずに自己検証で確かめられるようにするためである。
/// 誤りは例外ではなく <see cref="ErrorMessage"/> として返し、入口が終了コード 2 に変える。
/// </remarks>
internal sealed class CheckArguments
{
    /// <summary>フォルダの指定に使うオプションの名前。</summary>
    private const string DirectoryOption = "--dir";

    /// <summary><c>--dir</c> で指定された言語フォルダ。指定が無ければ null。</summary>
    public string? LanguageFolder { get; }

    /// <summary>引数の誤りの説明。誤りが無ければ null。</summary>
    public string? ErrorMessage { get; }

    /// <summary>引数に誤りが無いかどうか。</summary>
    public bool IsValid => ErrorMessage == null;

    private CheckArguments(string? languageFolder, string? errorMessage)
    {
        LanguageFolder = languageFolder;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// check コマンドに続く引数を解釈する。
    /// </summary>
    /// <param name="args">コマンド名（<c>check</c>）を除いた残りの引数。</param>
    /// <returns>解釈の結果。誤りがあれば <see cref="ErrorMessage"/> を伴う結果。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> が null の場合。</exception>
    public static CheckArguments Parse(IReadOnlyList<string> args)
    {
        if (args == null)
        {
            throw new ArgumentNullException(nameof(args));
        }

        string? languageFolder = null;

        for (int i = 0; i < args.Count; i++)
        {
            var arg = args[i];

            if (arg != DirectoryOption)
            {
                // 未知のオプションも、オプションではない余分な引数も、意図の分からない指定である。
                // 黙って読み飛ばすと、打ち間違えた指定が無視されたまま別のフォルダを検証してしまう。
                return Error($"未知の引数です: {arg}");
            }

            if (languageFolder != null)
            {
                // どちらのフォルダを検証したいのかが定まらないため、片方を選ばずに誤りとする。
                return Error($"{DirectoryOption} が2回以上指定されています。");
            }

            // 値が続かない、次が別のオプションである、値が空である、のいずれも「値が無い」とみなす。
            // 次のオプションを値として取り込むと、指定漏れが検証対象の取り違えに化ける。
            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return Error($"{DirectoryOption} に言語フォルダの指定がありません。");
            }

            var value = args[i + 1];
            if (value.Length == 0)
            {
                return Error($"{DirectoryOption} に指定された言語フォルダが空です。");
            }

            languageFolder = value;
            i++;
        }

        return new CheckArguments(languageFolder, null);
    }

    /// <summary>
    /// 引数の誤りを表す結果を作る。誤りのときはフォルダの指定を返さない
    /// （途中まで読めた指定で検証を続けてしまわないようにするため）。
    /// </summary>
    private static CheckArguments Error(string message)
    {
        return new CheckArguments(null, message);
    }
}

/// <summary>
/// LocalizationCheck ツールのエントリポイント。
/// コマンドの解釈、言語フォルダの特定と列挙、終了コードの決定を担う
/// （design.md: Components and Interfaces / Entry / Program）。
/// 判定と整形そのものは Check・Io の部品に任せ、ここでは行わない。
/// </summary>
internal static class Program
{
    /// <summary>判定結果: 問題なし（design.md: Batch / Job Contract の終了コード）。</summary>
    private const int ExitCodeNoProblem = 0;

    /// <summary>
    /// 判定結果: 問題あり（欠落、孤児、差し込み位置、重複、en 以外の読み込み不能のいずれか）
    /// （design.md: Batch / Job Contract の終了コード）。
    /// </summary>
    private const int ExitCodeProblemFound = 1;

    /// <summary>
    /// 判定結果: 検証不能（言語フォルダが無い、言語ファイルが0本、en.yaml が無いか読み込めない、
    /// 引数の誤り、予期しない例外）（design.md: Batch / Job Contract の終了コード）。
    /// </summary>
    private const int ExitCodeNotVerifiable = 2;

    /// <summary>リポジトリのルートの目印に使うソリューションファイルの名前。</summary>
    private const string SolutionFileName = "LargeFolderFinder.sln";

    private static int Main(string[] args)
    {
        // 標準出力・標準エラーのエンコーディングを明示的に UTF-8（BOM なし）へ固定する。
        // 既定（システムの OEM コードページ依存）のままだと、標準出力がリダイレクトされたときに
        // 読み取り側との既定エンコーディングの解決が食い違い、日本語の報告が文字化けすることがある
        // （GoldenBaseline と同じ扱い）。
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
            return ExitCodeNoProblem;
        }

        switch (args[0])
        {
            case "check":
                // コマンド名（args[0]）を除いた残りを check の引数として渡す。
                var checkArgs = new string[args.Length - 1];
                Array.Copy(args, 1, checkArgs, 0, checkArgs.Length);
                return RunCheck(checkArgs);
            case "selfcheck":
                return RunSelfCheck();
            default:
                Console.Error.WriteLine($"未知のコマンドです: {args[0]}");
                PrintUsage();
                return ExitCodeNotVerifiable;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("LocalizationCheck - Large Folder Finder の言語ファイルの網羅性を検証するツール");
        Console.WriteLine();
        Console.WriteLine("使い方: LocalizationCheck <コマンド> [オプション]");
        Console.WriteLine();
        Console.WriteLine("コマンド:");
        Console.WriteLine("  check [--dir <path>]  言語フォルダ内の全言語ファイルを検証する");
        Console.WriteLine("  selfcheck             登録済みの自己検証項目をまとめて実行する");
        Console.WriteLine("  --help                この使い方を表示する");
        Console.WriteLine();
        Console.WriteLine("オプション:");
        Console.WriteLine("  --dir <path>   検証する言語フォルダ。省略時はリポジトリの Resources/Languages を使う");
        Console.WriteLine();
        Console.WriteLine("終了コード: 0=問題なし / 1=問題あり / 2=検証不能");
    }

    /// <summary>
    /// 言語フォルダ全体を検証する（design.md: System Flows「検証フロー」）。
    /// </summary>
    /// <param name="args">コマンド名を除いた check の引数。</param>
    /// <returns>終了コード（0=問題なし、1=問題あり、2=検証不能）。</returns>
    /// <remarks>
    /// 読み込み（LanguageFileReader）→ 判定（CoverageChecker）→ 報告（ReportWriter）の順に呼び、
    /// 報告を標準出力へ出す。ファイルシステムとコンソールを扱うのはこの入口だけである
    /// （design.md: Architecture Integration「依存の向き」）。
    /// 読み取りしか行わないため、実行によって言語ファイルは変わらない（requirements.md: 4.4）。
    /// </remarks>
    private static int RunCheck(string[] args)
    {
        try
        {
            return RunCheckCore(args);
        }
        catch (Exception ex)
        {
            // 予期しない例外は検証不能とする（design.md: Batch / Job Contract の終了コード）。
            // 例外で落ちて 0 以外の未定義の終了コードを返すと、CI から判定できなくなる（requirements.md: 3.7）。
            Console.Error.WriteLine($"検証中に予期しない例外が発生しました {ex.GetType().Name}: {ex.Message}");
            return ExitCodeNotVerifiable;
        }
    }

    /// <summary>
    /// 検証の本体。想定内の検証不能はここで終了コード 2 を返し、想定外の例外は呼び出し側で受ける。
    /// </summary>
    /// <param name="args">コマンド名を除いた check の引数。</param>
    /// <returns>終了コード。</returns>
    private static int RunCheckCore(string[] args)
    {
        var parsedArgs = CheckArguments.Parse(args);
        if (!parsedArgs.IsValid)
        {
            Console.Error.WriteLine(parsedArgs.ErrorMessage);
            PrintUsage();
            return ExitCodeNotVerifiable;
        }

        // --dir の指定があればそれを使い、無ければリポジトリの言語フォルダを探す
        // （design.md: Batch / Job Contract「入力」）。
        var languageFolder = parsedArgs.LanguageFolder ?? FindRepositoryLanguageFolder();
        if (languageFolder == null)
        {
            Console.Error.WriteLine(
                $"言語フォルダが見つかりません（実行ファイルの位置から親へたどって {SolutionFileName} を含むフォルダを探しました）。" +
                "--dir で言語フォルダを指定してください。");
            return ExitCodeNotVerifiable;
        }

        if (!Directory.Exists(languageFolder))
        {
            Console.Error.WriteLine($"言語フォルダがありません: {languageFolder}");
            return ExitCodeNotVerifiable;
        }

        // フォルダ直下の *.yaml だけを、ファイル名の序数順に並べる（下位フォルダは見ない）。
        // 列挙の規則はアプリの LocalizationManager（Directory.GetFiles(dir, "*.yaml")）に合わせている。
        // 言語の一覧を持たずにその場のファイルを対象にするため、言語の増減に手作業の更新が要らない
        // （requirements.md: 3.8）。
        var filePaths = Directory.GetFiles(languageFolder, "*.yaml", SearchOption.TopDirectoryOnly);
        Array.Sort(filePaths, (left, right) =>
            string.CompareOrdinal(Path.GetFileName(left), Path.GetFileName(right)));

        if (filePaths.Length == 0)
        {
            Console.Error.WriteLine($"言語フォルダに *.yaml がありません: {languageFolder}");
            return ExitCodeNotVerifiable;
        }

        // 期待するキーは、アプリの列挙型の定義順で得る（design.md: Batch / Job Contract「入力」）。
        // 値を明示していない列挙のため、Enum.GetNames が返す値の順は宣言順と一致する。
        // この一覧はアプリのビルド出力から得るため、出力が古いとキーの数が実際と食い違う。
        // 報告の要約にキーの数を添えているのは、それを読み手が確かめられるようにするためである。
        var expectedKeys = Enum.GetNames(typeof(LanguageKey));

        var contents = new List<LanguageFileContent>(filePaths.Length);
        foreach (var filePath in filePaths)
        {
            // 読み込みの失敗は結果の中の理由になり、例外としては出てこない。
            // 1本が読めなくても残りの言語の検証は続く（requirements.md: 3.5、3.9）。
            contents.Add(LanguageFileReader.Read(filePath));
        }

        var report = CoverageChecker.Check(expectedKeys, contents);

        // 報告は常に標準出力へ出す。英語が使えないときも、他の言語について分かったことは出す
        // （design.md: System Flows「判定の分岐」）。
        foreach (var line in ReportWriter.Format(report))
        {
            Console.WriteLine(line);
        }

        if (!report.IsReferenceUsable)
        {
            // 差し込み位置を比べる基準が無く、検証が完結していない。
            // 問題の有無にかかわらず検証不能とする（design.md: System Flows「判定の分岐」）。
            return ExitCodeNotVerifiable;
        }

        return report.Problems.Count == 0 ? ExitCodeNoProblem : ExitCodeProblemFound;
    }

    /// <summary>
    /// 実行ファイルの位置から親へたどり、ソリューションファイルを含むフォルダの言語フォルダを返す。
    /// 見つからなければ null を返す（design.md: Batch / Job Contract「入力」）。
    /// </summary>
    /// <remarks>
    /// ツールはリポジトリ内の <c>Tools\LocalizationCheck\bin\...</c> から実行されるため、
    /// 祖先のいずれかがリポジトリのルートになる（GoldenBaseline の期待値の探し方と同じ考え方）。
    /// </remarks>
    private static string? FindRepositoryLanguageFolder()
    {
        var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return Path.Combine(directory.FullName, "Resources", "Languages");
            }

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>
    /// 登録済みの自己検証項目をすべて実行し、結果を標準出力へ報告する
    /// （design.md: Components and Interfaces / SelfChecks）。
    /// 1件でも失敗があれば問題あり（1）、すべて成功であれば問題なし（0）を返す。
    /// </summary>
    private static int RunSelfCheck()
    {
        var runner = new SelfCheckRunner();
        SelfChecks.Register(runner);
        var outcomes = runner.RunAll();

        int failureCount = 0;
        foreach (var outcome in outcomes)
        {
            var mark = outcome.Passed ? "OK" : "NG";
            Console.WriteLine($"[{mark}] {outcome.Name}");
            if (!outcome.Passed)
            {
                Console.WriteLine($"      理由: {outcome.FailureReason}");
                failureCount++;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{outcomes.Count} 件中 {failureCount} 件が失敗しました。");

        return failureCount == 0 ? ExitCodeNoProblem : ExitCodeProblemFound;
    }
}
