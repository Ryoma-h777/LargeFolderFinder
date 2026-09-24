using System;
using System.Collections.Generic;

namespace LargeFolderFinder
{
    // 進捗情報を格納するクラス
    public class ScanProgress
    {
        public int ProcessedFolders { get; set; }
        public int TotalFolders { get; set; }
        public TimeSpan? EstimatedTimeRemaining { get; set; }
        public string StatusMessage { get; set; } = "";
        public FolderInfo? CurrentResult { get; set; }

        /// <summary>
        /// 走査の正常な完了時に送る最後の報告かどうか（既定は偽で、途中の報告を表す）
        /// </summary>
        public bool IsFinal { get; set; }

        /// <summary>
        /// 走査中に列挙できずにスキップした対象の一覧（既定は空）
        /// </summary>
        public IReadOnlyList<ScanSkip> Skipped { get; set; } = Array.Empty<ScanSkip>();

        /// <summary>
        /// 走査に実際に使ったワーカーの数。最後の報告（<see cref="IsFinal"/> が真）だけで意味を持ち、
        /// 途中の報告では 0
        /// </summary>
        public int WorkerCount { get; set; }

        /// <summary>
        /// 走査中に同時に行われた列挙の数の最大。最後の報告（<see cref="IsFinal"/> が真）だけで
        /// 意味を持ち、途中の報告では 0
        /// </summary>
        public int PeakConcurrentEnumerations { get; set; }

        /// <summary>
        /// 実際に結果を作った走査の方式（ntfs-mft-scan 要件2.3）。
        /// 最後の報告（<see cref="IsFinal"/> が真）だけで意味を持ち、途中の報告では既定の
        /// <see cref="ScanMethodKind.NormalEnumeration"/> のまま。
        /// </summary>
        /// <remarks>
        /// 目録の走査を始められずに通常の走査へ切り替えたときは、切り替えた先の方式（通常の走査）が入る。
        /// </remarks>
        public ScanMethodKind Method { get; set; }
    }
}
