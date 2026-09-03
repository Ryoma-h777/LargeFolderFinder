using System;
using System.Linq;
using LargeFolderFinder.GoldenBaseline.SelfCheck;

namespace LargeFolderFinder.GoldenBaseline;

/// <summary>
/// GoldenBaseline ツールのエントリポイント。
/// サブコマンドの振り分けと終了コードの決定のみを担う（design.md: Program）。
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help")
        {
            PrintUsage();
            return 0;
        }

        switch (args[0])
        {
            case "selfcheck":
                return RunSelfCheck();
            default:
                Console.Error.WriteLine($"未知のコマンドです: {args[0]}");
                PrintUsage();
                return 2;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("GoldenBaseline - Large Folder Finder の走査結果を検証するツール");
        Console.WriteLine();
        Console.WriteLine("使い方: GoldenBaseline <コマンド>");
        Console.WriteLine("  selfcheck   登録済みの自己検証項目をまとめて実行する");
        Console.WriteLine("  --help      この使い方を表示する");
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
}
