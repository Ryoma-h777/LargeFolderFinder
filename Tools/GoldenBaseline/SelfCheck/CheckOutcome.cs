using System;

namespace LargeFolderFinder.GoldenBaseline.SelfCheck;

/// <summary>
/// 1件の自己検証項目の実行結果を表す。
/// 成否と、失敗時の理由のみを保持する不変のデータ型。
/// </summary>
public sealed class CheckOutcome
{
    /// <summary>検証項目の名前。</summary>
    public string Name { get; }

    /// <summary>検証が成功したかどうか。</summary>
    public bool Passed { get; }

    /// <summary>失敗した場合の理由。成功した場合は null。</summary>
    public string? FailureReason { get; }

    public CheckOutcome(string name, bool passed, string? failureReason)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("検証項目の名前が空です。", nameof(name));
        }

        Name = name;
        Passed = passed;
        FailureReason = failureReason;
    }
}
