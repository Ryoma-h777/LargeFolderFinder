namespace LargeFolderFinder
{
    /// <summary>
    /// 走査の調整値。アプリは設定から <see cref="ThreadCount"/> だけを渡し、
    /// 計測の道具は値を変えて試すために両方を渡す。
    /// </summary>
    /// <param name="ThreadCount">走査のワーカー数。0 は自動（対象がネットワークかローカルかで既定値が変わる）</param>
    /// <param name="EnumerationBufferSize">列挙のバッファの大きさ（バイト）。0 は .NET の既定</param>
    public sealed record ScanTuning(int ThreadCount = 0, int EnumerationBufferSize = 0);
}
