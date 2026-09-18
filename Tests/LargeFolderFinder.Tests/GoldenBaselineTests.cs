using Xunit;

namespace LargeFolderFinder.Tests;

/// <summary>
/// 走査結果の検証ツール（GoldenBaseline）を呼び、期待値データとの突き合わせと自己検証の結果を成否として示すテスト
/// （design.md TestProject、要件6.2、6.4、6.5）。
/// </summary>
public sealed class GoldenBaselineTests
{
    /// <summary>検証ツールの名前。</summary>
    private const string ToolName = "GoldenBaseline";

    /// <summary>コミット済みの期待値データ（リポジトリのルートからの相対パス）。</summary>
    private const string GoldenPath = "baselines/fixture-v1.golden.txt";

    /// <summary>
    /// 標準フィクスチャの走査結果が、コミット済みの期待値データと一致すること（compare の終了コードが 0）。
    /// </summary>
    [Fact]
    public void Compare_MatchesCommittedGolden()
    {
        var result = ToolRunner.Run(ToolName, "compare", "--golden", GoldenPath);

        ToolRunner.AssertVerified(result, $"期待値データ（{GoldenPath}）との突き合わせ");
    }

    /// <summary>
    /// GoldenBaseline の自己検証がすべて成功すること（selfcheck の終了コードが 0）。
    /// </summary>
    [Fact]
    public void SelfCheck_AllPass()
    {
        var result = ToolRunner.Run(ToolName, "selfcheck");

        ToolRunner.AssertVerified(result, "GoldenBaseline の自己検証");
    }

    // ------------------------------------------------------------------
    // 終了コード 2 をスキップにするかの判定（要件6.4）。環境の制約は手元で再現しにくいため、
    // GoldenBaseline が実際に出す文言を与えて、判定の規則そのものを確かめる。
    // ------------------------------------------------------------------

    /// <summary>
    /// フィクスチャの項目を生成できなかった報告を伴う終了コード 2 は、環境の制約と判定されること。
    /// 基準フォルダを作れないと、GoldenBaseline は全項目を未生成として報告し、続く走査が基準フォルダの不在で失敗する
    /// （FixtureBuilder.Build と ScanRunner.Run の実際の文言）。
    /// </summary>
    [Fact]
    public void EnvironmentConstraint_FixtureOmissionsWithExitCode2_IsSkipTarget()
    {
        var result = new ToolResult(
            2,
            "[フィクスチャの生成]\n基準フォルダ: C:\\Temp\\gb_fix_x\n未生成の項目: 17 件\n  - normal: 基準フォルダの生成に失敗しました: UnauthorizedAccessException: Access to the path is denied.\n",
            "エラー: 基準フォルダが見つかりません: C:\\Temp\\gb_fix_x\n");

        Assert.True(ToolRunner.IsEnvironmentConstraint(result));
    }

    /// <summary>
    /// 既定の置き場が長すぎて基準フォルダの長さを固定できなかった終了コード 2 は、環境の制約と判定されること。
    /// </summary>
    [Fact]
    public void EnvironmentConstraint_TempPathTooLongWithExitCode2_IsSkipTarget()
    {
        var result = new ToolResult(
            2,
            string.Empty,
            "エラー: 既定の置き場（C:\\very\\long\\temp\\）が長すぎるため、基準フォルダの実効絶対パス長を 80 文字に固定できません（名前を最短にしても 95 文字になります）。\n");

        Assert.True(ToolRunner.IsEnvironmentConstraint(result));
    }

    /// <summary>
    /// 原因が環境の制約と判断できない終了コード 2（期待値ファイルを読めない、設定不一致など）は、スキップにせず失敗にすること。
    /// </summary>
    [Fact]
    public void EnvironmentConstraint_OtherExitCode2_IsNotSkipTarget()
    {
        var unreadableGolden = new ToolResult(2, string.Empty, "エラー: 期待値ファイルを読み込めません（missing.txt）: ファイルがありません\n");
        var settingsMismatch = new ToolResult(2, "[比較]\n判定: 設定不一致（物理サイズ換算の有無、または基準フォルダの実効絶対パス長が期待値データの生成時と異なります。エントリの突き合わせは行っていません）\n", string.Empty);

        Assert.False(ToolRunner.IsEnvironmentConstraint(unreadableGolden));
        Assert.False(ToolRunner.IsEnvironmentConstraint(settingsMismatch));
    }

    /// <summary>
    /// 環境の制約の文言があっても、終了コードが 2 でなければ（差分ありの 1 など）スキップにしないこと。
    /// </summary>
    [Fact]
    public void EnvironmentConstraint_NonExitCode2_IsNotSkipTarget()
    {
        var different = new ToolResult(1, "未生成の項目: 1 件\n[比較]\n判定: 差分あり（1 件）\n", string.Empty);

        Assert.False(ToolRunner.IsEnvironmentConstraint(different));
    }
}
