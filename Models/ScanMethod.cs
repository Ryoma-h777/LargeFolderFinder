namespace LargeFolderFinder
{
    /// <summary>
    /// 走査の方式（ntfs-mft-scan 要件1.1, 2.1）。
    /// </summary>
    public enum ScanMethodKind
    {
        /// <summary>通常の走査。フォルダを1つずつ列挙して木を組み立てる（どの環境でも使える）</summary>
        NormalEnumeration = 0,

        /// <summary>ドライブの目録（NTFS の MFT）をまとめて読む速い走査。ローカルの NTFS を管理者として走査するときだけ使える</summary>
        VolumeLayout = 1,
    }

    /// <summary>
    /// 走査の方式の決定の結果（ntfs-mft-scan 要件2.1, 2.3, 2.4）。
    /// </summary>
    /// <param name="Method">選んだ走査の方式</param>
    /// <param name="Reason">
    /// なぜその方式を選んだかが1行で分かる日本語の文。ログと走査の記録にそのまま出す。
    /// </param>
    /// <remarks>
    /// 昇格は起動のときにアプリのマニフェストが行うため（利用者の決定・2026-09-25）、
    /// 「管理者になれば速くなる」ことを画面へ伝える項目は持たない（要件3.4, 3.5）。
    /// </remarks>
    public sealed record ScanMethodDecision(ScanMethodKind Method, string Reason);
}
