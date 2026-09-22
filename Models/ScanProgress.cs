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
    }
}
