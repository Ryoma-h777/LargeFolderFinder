using Xunit;

namespace LargeFolderFinder.Tests;

/// <summary>
/// 翻訳の網羅の検証ツール（LocalizationCheck）を呼び、言語ファイルの検証と自己検証の結果を成否として示すテスト
/// （design.md TestProject、要件6.5、7.3）。
/// </summary>
public sealed class LocalizationTests
{
    /// <summary>検証ツールの名前。</summary>
    private const string ToolName = "LocalizationCheck";

    /// <summary>
    /// リポジトリの言語ファイルに、翻訳の抜け・余分なキーなどの問題が無いこと（check の終了コードが 0）。
    /// </summary>
    [Fact]
    public void Check_NoProblems()
    {
        var result = ToolRunner.Run(ToolName, "check");

        ToolRunner.AssertVerified(result, "言語ファイルの網羅の検証");
    }

    /// <summary>
    /// LocalizationCheck の自己検証がすべて成功すること（selfcheck の終了コードが 0）。
    /// </summary>
    [Fact]
    public void SelfCheck_AllPass()
    {
        var result = ToolRunner.Run(ToolName, "selfcheck");

        ToolRunner.AssertVerified(result, "LocalizationCheck の自己検証");
    }
}
