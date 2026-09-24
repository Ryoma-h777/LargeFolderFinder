using System;

namespace LargeFolderFinder.GoldenBaseline.SelfCheck;

/// <summary>
/// 1件の自己検証項目の実行結果の状態（ntfs-mft-scan 要件7.1）。
/// 「飛ばした」は前提が成り立たずに確かめられなかったことを表し、失敗としては数えない。
/// </summary>
public enum CheckStatus
{
    /// <summary>検証が成功した。</summary>
    Passed,

    /// <summary>検証が失敗した。</summary>
    Failed,

    /// <summary>前提が成り立たないため検証を飛ばした（失敗としては数えない）。</summary>
    Skipped,
}

/// <summary>
/// 1件の自己検証項目の実行結果を表す。
/// 状態と、失敗または飛ばした理由のみを保持する不変のデータ型。
/// </summary>
public sealed class CheckOutcome
{
    /// <summary>検証項目の名前。</summary>
    public string Name { get; }

    /// <summary>検証の結果の状態。</summary>
    public CheckStatus Status { get; }

    /// <summary>失敗した場合、または飛ばした場合の理由。成功した場合は null。</summary>
    public string? Reason { get; }

    private CheckOutcome(string name, CheckStatus status, string? reason)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("検証項目の名前が空です。", nameof(name));
        }

        Name = name;
        Status = status;
        Reason = reason;
    }

    /// <summary>成功した結果を作る。</summary>
    public static CheckOutcome Pass(string name) => new(name, CheckStatus.Passed, null);

    /// <summary>失敗した結果を作る。</summary>
    public static CheckOutcome Fail(string name, string reason) => new(name, CheckStatus.Failed, reason);

    /// <summary>飛ばした結果を作る（失敗としては数えない）。</summary>
    public static CheckOutcome Skip(string name, string reason) => new(name, CheckStatus.Skipped, reason);
}
