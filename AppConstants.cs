namespace LargeFolderFinder
{
    /// <summary>
    /// 定数クラス
    /// </summary>
    public static class AppConstants
    {
        /// <summary>
        /// ユーザーデータの保存先ディレクトリ ("User/AppData/Local/組織名/アプリ名)
        /// </summary>
        public static string AppDataDirectory => System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            AppInfo.Organization,
            AppInfo.Title);

        /// <summary>
        /// ログ保存先ディレクトリ ("User/AppData/Local/組織名/アプリ名/Logs)
        /// </summary>
        /// <summary>
        /// ログ保存先ディレクトリ ("User/AppData/Local/アプリ名/Logs)
        /// </summary>
        public static string LogsDirectoryPath =>
            System.IO.Path.Combine(AppDataDirectory, LogsDirectoryName);

        // 設定ファイル関連の定数
        public const string CacheFileName = "Cache.txt"; // 旧形式（互換性のため残す）
        public const string SettingsFileName = "Settings.msgpack"; // 新しいMessagePack形式の設定ファイル
        public const string SessionsDirectoryName = "Sessions"; // セッションファイル保存ディレクトリ
        public const string SessionFileFormat = "Scan{0:yyyyMMdd_HHmm_ssfff}.msgpack";
        public const string LogsDirectoryName = "Logs";
        public const string LogsFileNameWithoutExtension = "yyyyMMdd_HHmm_ss";
        public const string LogsExtension = "log";
        public const int LogFilesMax = 4;

        public static string GetReadmeFileName(string lang) => $"Readme_{lang}.txt";
        public const string ReadmeDirectoryName = "Readme";
        public const string ConfigFileName = "Config.txt";
        public const string LicenseDirectoryName = "License";
        public const string AppLicenseFileName = "LICENSE.txt";
        public const string ThirdPartyNoticesFileName = "ThirdPartyNotices.txt";
        // removed BytesInGB
        public const double DefaultThreshold = 100.0;
        // removed SizeUnit

        public enum SizeUnit
        {
            B,
            KB,
            MB,
            GB,
            TB
        }

        public enum SortTarget
        {
            Size,
            Name,
            Date,
            Type
        }
        public enum SortDirection
        {
            Ascending,
            Descending
        }

        public static long GetBytesPerUnit(SizeUnit unit)
        {
            return unit switch
            {
                SizeUnit.KB => 1024L,
                SizeUnit.MB => 1024L * 1024L,
                SizeUnit.GB => 1024L * 1024L * 1024L,
                SizeUnit.TB => 1024L * 1024L * 1024L * 1024L,
                _ => 1L
            };
        }

        public enum LayoutType
        {
            Vertical,
            Horizontal
        }

        public enum Separator
        {
            Tab,
            Space
        }

        // 桁区切りとフォーマット
        public const string DigitSeparator = ",";
        public const string SizeFormat = "{0, 10:N0} {1}";
        public const int DefaultTabWidth = 8;

        // 罫線定数
        public const string TreeBranch = "┣";
        public const string TreeLastBranch = "┗";
        public const string TreeVertical = "┃";
        public const string TreeSpace = " ";

        /// <summary>
        /// サイズ表示の文字揃えの基準文字数
        /// ユーザー要件: 空白1文字 + 数字4桁 + "," + " GB"分 = 9文字
        /// </summary>
        public const int BaseSizeLength = 9;

        /// <summary>
        /// メモリ最適化を実行する間隔（分）
        /// </summary>
        public const int MemoryOptimizeIntervalMinutes = 5;

        // Log Messages (English only)
        public const string LogAppStarted = "Application started.";
        public const string LogAppExited = "Application exited.";
        public const string LogInitStart = "Initializing localization...";
        public const string LogInitSuccess = "Localization initialized successfully.";
        public const string LogInitError = "Initialization error occurred.";
        public const string LogCacheLoadStart = "Loading cache data...";
        public const string LogCacheLoadSuccess = "Cache data loaded successfully.";
        public const string LogCacheLoadError = "Failed to load cache data.";
        public const string LogCacheSaveStart = "Saving cache data...";
        public const string LogCacheSaveSuccess = "Cache data saved successfully.";
        public const string LogCacheSaveError = "Failed to save cache data.";
        public const string LogApplyLocStart = "Applying localization to UI...";
        public const string LogApplyLocSuccess = "Localization applied to UI successfully.";
        public const string LogApplyLocError = "Failed to apply localization to UI.";
        public const string LogUpdateMenuStart = "Updating language menu...";
        public const string LogUpdateMenuSuccess = "Language menu updated successfully.";
        public const string LogUpdateMenuError = "Failed to update language menu.";
        public const string LogLangChangeStart = "Changing language to: {0}";
        public const string LogScanStart = "Scan started. Path: {0}, Threshold: {1}";
        public const string LogScanSuccess = "Scan finished successfully. Time: {0}";
        public const string LogScanError = "Scan error occurred.";
        public const string LogScanProgressError = "Progress handler error occurred.";
        public const string LogRenderError = "RenderResult error occurred.";
        public const string LogClipboardError = "Failed to copy to clipboard.";
        public const string LogOptimizeMemory = "Memory optimized.";
        public const string LogBrowseButtonClicked = "Browse button clicked.";

    }
}
