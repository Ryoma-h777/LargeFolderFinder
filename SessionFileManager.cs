using System;
using System.IO;
using MessagePack;

namespace LargeFolderFinder
{
    /// <summary>
    /// セッションファイルの読み書きを管理するクラス
    /// </summary>
    public static class SessionFileManager
    {
        // LZ4圧縮を有効化（データサイズを50-70%削減）
        private static readonly MessagePackSerializerOptions LZ4Options =
            MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);

        private static string SessionsDirectory =>
            Path.Combine(AppConstants.AppDataDirectory, AppConstants.SessionsDirectoryName);

        /// <summary>
        /// セッションデータを保存する
        /// </summary>
        /// <param name="session">保存するセッションデータ</param>
        /// <returns>保存されたファイル名</returns>
        public static string Save(SessionData session)
        {
            try
            {
                // Sessionsディレクトリが存在しない場合は作成
                if (!Directory.Exists(SessionsDirectory))
                {
                    Directory.CreateDirectory(SessionsDirectory);
                }

                string fileName = session.GenerateFileName();
                string filePath = Path.Combine(SessionsDirectory, fileName);

                byte[] bytes = MessagePackSerializer.Serialize(session, LZ4Options);
                File.WriteAllBytes(filePath, bytes);

                return fileName;
            }
            catch (Exception ex)
            {
                Logger.Log($"セッションファイルの保存に失敗しました: {session.Path}", ex);
                return "";
            }
        }

        /// <summary>
        /// セッションデータを読み込む
        /// </summary>
        /// <param name="fileName">読み込むファイル名</param>
        /// <returns>読み込まれたセッションデータ、失敗時はnull</returns>
        public static SessionData? Load(string fileName)
        {
            try
            {
                string filePath = Path.Combine(SessionsDirectory, fileName);

                if (!File.Exists(filePath))
                {
                    Logger.Log($"セッションファイルが見つかりません: {fileName}");
                    return null;
                }

                byte[] bytes = File.ReadAllBytes(filePath);
                var session = MessagePackSerializer.Deserialize<SessionData>(bytes, LZ4Options);
                session?.Result?.RestoreParentReferences();
                return session;
            }
            catch (Exception ex)
            {
                Logger.Log($"セッションファイルの読み込みに失敗しました: {fileName}", ex);
                return null;
            }
        }

        /// <summary>
        /// セッションファイルを削除する
        /// </summary>
        /// <param name="fileName">削除するファイル名</param>
        public static void Delete(string fileName)
        {
            try
            {
                string filePath = Path.Combine(SessionsDirectory, fileName);

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"セッションファイルの削除に失敗しました: {fileName}", ex);
            }
        }

        /// <summary>
        /// 古いセッションファイルを削除する（オプション機能）
        /// </summary>
        /// <param name="daysToKeep">保持する日数</param>
        public static void DeleteOldSessions(int daysToKeep = 30)
        {
            try
            {
                if (!Directory.Exists(SessionsDirectory))
                {
                    return;
                }

                DateTime cutoffDate = DateTime.Now.AddDays(-daysToKeep);
                string[] files = Directory.GetFiles(SessionsDirectory, "Scan*.msgpack");

                foreach (string file in files)
                {
                    FileInfo fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTime < cutoffDate)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("古いセッションファイルの削除に失敗しました。", ex);
            }
        }

        /// <summary>
        /// すべてのセッションファイル名を取得する
        /// </summary>
        public static string[] GetAllSessionFileNames()
        {
            try
            {
                if (!Directory.Exists(SessionsDirectory))
                {
                    return Array.Empty<string>();
                }

                string[] fullPaths = Directory.GetFiles(SessionsDirectory, "Scan*.msgpack");
                string[] fileNames = new string[fullPaths.Length];

                for (int i = 0; i < fullPaths.Length; i++)
                {
                    fileNames[i] = Path.GetFileName(fullPaths[i]);
                }

                return fileNames;
            }
            catch (Exception ex)
            {
                Logger.Log("セッションファイル一覧の取得に失敗しました。", ex);
                return Array.Empty<string>();
            }
        }
    }
}
