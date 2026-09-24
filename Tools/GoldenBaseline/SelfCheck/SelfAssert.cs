using System;

namespace LargeFolderFinder.GoldenBaseline.SelfCheck;

/// <summary>
/// 検証項目を「飛ばした」ことを伝える例外（ntfs-mft-scan 要件7.1）。
/// 前提が成り立たず確かめられなかった場合に送出する。
/// <see cref="SelfCheckRunner"/> はこの例外を捕捉し、失敗ではなく飛ばした項目として扱う。
/// </summary>
internal sealed class SelfCheckSkippedException : Exception
{
    public SelfCheckSkippedException(string reason) : base(reason)
    {
    }
}

/// <summary>
/// 検証項目の中で条件の成否を宣言するための最小限のアサーションヘルパー。
/// 外部のテストフレームワークには依存しない。
/// </summary>
internal static class SelfAssert
{
    /// <summary>
    /// 条件が偽であれば、message を理由として例外を送出する。
    /// SelfCheckRunner はこの例外を捕捉し、検証項目の失敗として扱う。
    /// </summary>
    public static void That(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>
    /// この検証項目を飛ばす。reason はそのまま報告に出るため、なぜ確かめられないのかと、
    /// どうすれば確かめられるのかが分かる文にする（ntfs-mft-scan 要件7.1）。
    /// </summary>
    public static void Skip(string reason)
    {
        throw new SelfCheckSkippedException(reason);
    }

    /// <summary>
    /// 条件が真のときだけ、この検証項目を飛ばす。偽のときは何もしない。
    /// </summary>
    public static void SkipIf(bool condition, string reason)
    {
        if (condition)
        {
            Skip(reason);
        }
    }
}
