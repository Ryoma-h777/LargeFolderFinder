using System;

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
    }
}
