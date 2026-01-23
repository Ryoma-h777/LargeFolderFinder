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
                // 無視
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
                // 無視
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
                // 無視
            }
        }

    }
}
