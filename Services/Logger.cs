using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;

namespace LargeFolderFinder
{
    /// <summary>
    /// ログ出力機能を管理するクラス
    /// </summary>
    public static class Logger
    {
        private static readonly object _lock = new object();
        private static readonly string _logFilePath;

        static Logger()
        {
            try
            {
                string logsDir = AppConstants.LogsDirectoryPath;

                if (!Directory.Exists(logsDir))
                {
                    Directory.CreateDirectory(logsDir);
                }

                // ファイル名: YYYYMMDD_HHmm_ss_Log.txt
                string fileName = $"{DateTime.Now.ToString(AppConstants.LogsFileNameWithoutExtension)}.{AppConstants.LogsExtension}";
                _logFilePath = Path.Combine(logsDir, fileName);

                CleanupOldLogs(logsDir);
            }
            catch
            {
                // 意図して無視: ログの準備の失敗はログに書けない（循環する）。ログのパスを空にして以降の書き込みを止め、アプリは続ける
                _logFilePath = string.Empty;
            }
        }

        public static string CurrentLogFilePath => _logFilePath;

        private static void CleanupOldLogs(string logsDir)
        {
            try
            {
                var files = Directory.GetFiles(logsDir, $"*.{AppConstants.LogsExtension}")
                                     .Select(f => new FileInfo(f))
                                     .OrderByDescending(f => f.CreationTime)
                                     .ToList();
                int maxLogFiles = 4;
                // maxLogFilesファイルまで保持、それより古いものは削除
                if (files.Count >= maxLogFiles)
                {
                    for (int i = maxLogFiles; i < files.Count; i++)
                    {
                        files[i].Delete();
                    }
                }
            }
            catch
            {
                // 意図して無視: 古いログの削除の失敗をログに書くと循環する。削除できなかったログは次回の起動で再び削除を試みる
            }
        }

        /// <summary>
        /// ログをファイルに書き込みます
        /// </summary>
        public static void Log(string message, Exception? ex = null)
        {
            if (string.IsNullOrEmpty(_logFilePath)) return;

            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string logEntry = $"[{timestamp}] {message}";

                if (ex != null)
                {
                    logEntry += $"\nException: {ex.GetType().Name} - {ex.Message}\nStackTrace:\n{ex.StackTrace}\n";
                    if (ex.InnerException != null)
                    {
                        logEntry += $"InnerException: {ex.InnerException.Message}\n";
                    }
                }

                lock (_lock)
                {
                    File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
                }

                LogWritten?.Invoke();
            }
            catch
            {
                // 意図して無視: ログの書き込みの失敗をログに書くと循環する。ログが残らないだけでアプリは続ける
            }
        }

        public static event Action? LogWritten;

        /// <summary>
        /// 現在のログファイルを開きます（DEBUG ビルド時のみ）
        /// </summary>
        [Conditional("DEBUG")]
        public static void OpenLogFile()
        {
            try
            {
                if (File.Exists(_logFilePath))
                {
                    Process.Start(new ProcessStartInfo(_logFilePath) { UseShellExecute = true });
                }
            }
            catch
            {
                // 意図して無視: ログを開く失敗をログに書くと循環する（開発用の機能で、開けなくてもアプリは続ける）
            }
        }

    }
}
