namespace LargeFolderFinder
{
    /// <summary>
    /// 走査中にスキップした対象の種類
    /// </summary>
    public enum ScanSkipKind
    {
        /// <summary>アクセスが拒否された</summary>
        AccessDenied,

        /// <summary>対象が見つからなかった</summary>
        NotFound,

        /// <summary>入出力の失敗</summary>
        IoError
    }

    /// <summary>
    /// 走査中にスキップした対象の1件
    /// </summary>
    /// <param name="Path">スキップした対象のパス</param>
    /// <param name="Kind">スキップの種類</param>
    /// <param name="Reason">スキップの理由（例外のメッセージなど）</param>
    public sealed record ScanSkip(string Path, ScanSkipKind Kind, string Reason);
}
