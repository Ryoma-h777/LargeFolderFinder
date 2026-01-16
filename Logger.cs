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
                string fileName = $"{DateTime.Now:yyyyMMdd_HHmm_ss}_Log.txt";
                _logFilePath = Path.Combine(logsDir, fileName);

                CleanupOldLogs(logsDir);
            }
            catch
            {
                _logFilePath = string.Empty;
            }
        }

        private static void CleanupOldLogs(string logsDir)
        {
            try
            {
                var files = Directory.GetFiles(logsDir, "*_Log.txt")
                                     .Select(f => new FileInfo(f))
                                     .OrderByDescending(f => f.CreationTime)
                                     .ToList();

                // 2ファイルまで保持、それより古いものは削除
                if (files.Count > 2)
                {
                    for (int i = 2; i < files.Count; i++)
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
            }
            catch
            {
                // 無視
            }
        }

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
        /// <summary>
        /// 指定されたパスのログファイルを開きます
        /// </summary>
        public static void OpenSpecificLogFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
            }
            catch
            {
                // 無視
            }
        }
    }
}
