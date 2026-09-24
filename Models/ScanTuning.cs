namespace LargeFolderFinder
{
    /// <summary>
    /// 走査の調整値。アプリは設定から <see cref="ThreadCount"/> だけを渡し、
    /// 計測の道具は値を変えて試すために両方を渡す。
    /// </summary>
    /// <param name="ThreadCount">走査のワーカー数。0 は自動（対象がネットワークかローカルかで既定値が変わる）</param>
    /// <param name="EnumerationBufferSize">列挙のバッファの大きさ（バイト）。0 は .NET の既定</param>
    /// <param name="ForcedMethod">
    /// 走査の方式の指定。null（既定）なら方式は <see cref="ScanMethodSelector"/> の決め方に委ねる。
    /// **試験と計測のための指定**で、画面からは渡さない（目録の走査を強制して切り替えを確かめる、方式を比べて測る）。
    /// 指定しても使えない条件（ボリュームを開けないなど）では通常の走査へ切り替わる（要件2.2）。
    /// </param>
    public sealed record ScanTuning(
        int ThreadCount = 0,
        int EnumerationBufferSize = 0,
        ScanMethodKind? ForcedMethod = null);
}
