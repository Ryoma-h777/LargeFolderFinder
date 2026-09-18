using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Xunit;

namespace LargeFolderFinder.Tests;

/// <summary>
/// 検証ツールを1回実行した結果（終了コード・標準出力・標準エラー）を表す不変のデータ型。
/// </summary>
public sealed class ToolResult
{
    /// <summary>検証ツールの終了コード。</summary>
    public int ExitCode { get; }

    /// <summary>検証ツールの標準出力の全文。</summary>
    public string StandardOutput { get; }

    /// <summary>検証ツールの標準エラーの全文。</summary>
    public string StandardError { get; }

    /// <summary>
    /// ToolResult を構築する。
    /// </summary>
    /// <param name="exitCode">終了コード。</param>
    /// <param name="standardOutput">標準出力の全文。</param>
    /// <param name="standardError">標準エラーの全文。</param>
    public ToolResult(int exitCode, string standardOutput, string standardError)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput ?? string.Empty;
        StandardError = standardError ?? string.Empty;
    }

    /// <summary>
    /// テストの失敗・スキップの理由に添える、終了コードと出力をまとめた文字列を返す。
    /// </summary>
    public string Describe()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"終了コード: {ExitCode}");
        builder.AppendLine("--- 標準出力 ---");
        builder.AppendLine(StandardOutput.TrimEnd());
        builder.AppendLine("--- 標準エラー ---");
        builder.AppendLine(StandardError.TrimEnd());
        return builder.ToString();
    }
}

/// <summary>
/// 検証ツールの実行ファイルを探して子プロセスで実行し、終了コードと出力を返す部品
/// （design.md TestProject の ToolRunner）。判定の仕組みは検証ツールの側にあり、ここでは作り直さない（要件6.5）。
/// </summary>
public static class ToolRunner
{
    /// <summary>判定結果: 問題なし（両ツール共通の終了コード）。</summary>
    public const int ExitCodeSuccess = 0;

    /// <summary>判定結果: 検証できなかった（GoldenBaseline は「設定不一致または実行時エラー」、LocalizationCheck は「検証不能」）。</summary>
    public const int ExitCodeNotVerifiable = 2;

    /// <summary>検証ツールの対象フレームワークのフォルダ名（ツールには RID を付けていないため、この直下に exe が出る）。</summary>
    private const string ToolTargetFrameworkFolder = "net10.0-windows";

    /// <summary>
    /// 1回の実行を待つ上限。走査の自己検証は数十秒で終わるため、止まったツールでテストが終わらなくなることだけを防ぐ。
    /// </summary>
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 終了コード 2 のうち、実行環境の制約（権限のないフォルダを作れない、パスの長さの制約など）に由来すると
    /// 判断できる出力の印。いずれも GoldenBaseline が報告する文言で、出力のどこかに含まれていれば環境の制約とみなす
    /// （design.md TestProject「環境の制約の扱い」、要件6.4）。
    /// </summary>
    private static readonly IReadOnlyList<string> EnvironmentConstraintMarkers = new[]
    {
        // フィクスチャの項目を生成できなかった（権限のないフォルダを作れない、パスが長すぎる等）ときの報告の見出し
        "未生成の項目:",
        // 既定の置き場（%TEMP%）が長すぎて、基準フォルダの実効絶対パス長を固定できなかったときのエラー
        "文字に固定できません",
    };

    /// <summary>
    /// 検証ツールの実行ファイルを探し、リポジトリのルートを作業フォルダとして子プロセスで実行する。
    /// </summary>
    /// <param name="toolName">検証ツールの名前（例: GoldenBaseline）。実行ファイル名とフォルダ名を兼ねる。</param>
    /// <param name="arguments">検証ツールに渡す引数。</param>
    /// <returns>終了コード・標準出力・標準エラー。</returns>
    public static ToolResult Run(string toolName, params string[] arguments)
    {
        var (executablePath, repositoryRoot) = FindExecutable(toolName);

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // 検証ツールは標準出力・標準エラーを UTF-8（BOM なし）に固定しているため、それに合わせて読む
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            Assert.Fail($"検証ツールを起動できませんでした: {executablePath}");
        }

        // 標準出力と標準エラーを同時に読み切らないと、片方のバッファが詰まって子プロセスが止まるため、両方を非同期で読む
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(ExecutionTimeout))
        {
            // 自分が起動したプロセス（とその子）だけを終了させる
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Assert.Fail($"検証ツールが {ExecutionTimeout.TotalMinutes} 分以内に終わりませんでした: {executablePath} {string.Join(" ", arguments)}");
        }

        // 引数なしの WaitForExit で、非同期の読み取りが出力の最後まで届くのを待つ
        process.WaitForExit();
        return new ToolResult(process.ExitCode, standardOutputTask.GetAwaiter().GetResult(), standardErrorTask.GetAwaiter().GetResult());
    }

    /// <summary>
    /// 検証ツールの結果を成否として示す。終了コード 0 は成功、終了コード 2 のうち実行環境の制約に由来するものは
    /// 成功と区別してスキップ、それ以外（1 や、原因が環境の制約と判断できない 2）は失敗にする（要件6.2、6.4）。
    /// </summary>
    /// <param name="result">検証ツールの実行結果。</param>
    /// <param name="description">何を確かめたかの説明（失敗・スキップの理由の先頭に付ける）。</param>
    public static void AssertVerified(ToolResult result, string description)
    {
        if (result.ExitCode == ExitCodeSuccess)
        {
            return;
        }

        if (IsEnvironmentConstraint(result))
        {
            Assert.Skip($"{description}: 実行環境の制約（権限、パスの長さ）のため検証できませんでした。成功ではありません。{Environment.NewLine}{result.Describe()}");
        }

        Assert.Fail($"{description}: 検証ツールが成功しませんでした。{Environment.NewLine}{result.Describe()}");
    }

    /// <summary>
    /// 終了コード 2 の原因が、実行環境の制約（検証ツールが報告する「生成できなかった項目」やパスの長さの制約）だと
    /// 出力から判断できるかを返す。終了コードが 2 以外なら常に偽。
    /// </summary>
    /// <param name="result">検証ツールの実行結果。</param>
    public static bool IsEnvironmentConstraint(ToolResult result)
    {
        if (result.ExitCode != ExitCodeNotVerifiable)
        {
            return false;
        }

        return EnvironmentConstraintMarkers.Any(marker =>
            result.StandardOutput.Contains(marker, StringComparison.Ordinal)
            || result.StandardError.Contains(marker, StringComparison.Ordinal));
    }

    /// <summary>
    /// テストの出力フォルダから親へたどり、<c>Tools/&lt;名前&gt;/bin/&lt;構成&gt;/net10.0-windows/&lt;名前&gt;.exe</c> を探す。
    /// 構成はこのテスト自身のビルド構成（Debug / Release）にそろえる。見つからなければテストを失敗にする。
    /// </summary>
    /// <param name="toolName">検証ツールの名前。</param>
    /// <returns>実行ファイルの絶対パスと、それを見つけたリポジトリのルート。</returns>
    private static (string ExecutablePath, string RepositoryRoot) FindExecutable(string toolName)
    {
        string configuration = GetBuildConfiguration();
        string relativePath = Path.Combine("Tools", toolName, "bin", configuration, ToolTargetFrameworkFolder, toolName + ".exe");

        var searched = new List<string>();
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            searched.Add(candidate);
            if (File.Exists(candidate))
            {
                return (candidate, directory.FullName);
            }
        }

        Assert.Fail(
            $"検証ツールの実行ファイルが見つかりません（構成: {configuration}）。ソリューションを同じ構成でビルドしてください。" +
            $"{Environment.NewLine}探した場所:{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", searched)}");
        throw new InvalidOperationException("到達しない");
    }

    /// <summary>
    /// このテストのアセンブリのビルド構成（SDK が既定で付ける AssemblyConfiguration 属性の値）を返す。
    /// </summary>
    private static string GetBuildConfiguration()
    {
        string? configuration = typeof(ToolRunner).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration;
        if (string.IsNullOrEmpty(configuration))
        {
            Assert.Fail("テストのアセンブリからビルド構成（AssemblyConfiguration）を読み取れませんでした。");
        }

        return configuration!;
    }
}
