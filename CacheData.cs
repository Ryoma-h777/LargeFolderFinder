using System;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LargeFolderFinder
{
    /// <summary>
    /// アプリケーション設定を管理するクラス (Cache.yaml)
    /// </summary>
    public class CacheData
    {
        /// <summary>
        /// 最後に選択されたフォルダパス
        /// </summary>
        public string LastFolderPath { get; set; } = "";

        /// <summary>
        /// 言語設定
        /// </summary>
        public string Language { get; set; } = "";
        public double LastThresholdGB { get; set; } = AppConstants.DefaultThreshold;
        public int SeparatorIndex { get; set; } = 1; // 1: Space (デフォルト)
        public int TabWidth { get; set; } = 8;
        public AppConstants.SizeUnit Unit { get; set; } = AppConstants.SizeUnit.GB;
        public bool IncludeFiles { get; set; } = false;
        public AppConstants.SortTarget SortTarget { get; set; } = AppConstants.SortTarget.Size;
        public AppConstants.SortDirection SortDirection { get; set; } = AppConstants.SortDirection.Descending;

        /// <summary>
        /// 設定ファイルのパスを取得
        /// </summary>
        private static string SettingsFilePath =>
            Path.Combine(AppConstants.AppDataDirectory, AppConstants.CacheFileName);

        /// <summary>
        /// 設定ファイルから設定を読み込む
        /// </summary>
        /// <returns>読み込まれた設定。失敗した場合はnull</returns>
        public static CacheData? Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    using (var reader = new StreamReader(SettingsFilePath))
                    {
                        var deserializer = new DeserializerBuilder()
                            .WithNamingConvention(PascalCaseNamingConvention.Instance)
                            .Build();
                        return deserializer.Deserialize<CacheData>(reader);
                    }
                }
            }
            catch (Exception)
            {
                // 読み込みエラーは無視
            }
            return null;
        }

        /// <summary>
        /// 設定をファイルに保存する
        /// </summary>
        public void Save()
        {
            try
            {
                var serializer = new SerializerBuilder()
                    .WithNamingConvention(PascalCaseNamingConvention.Instance)
                    .Build();
                if (!Directory.Exists(AppConstants.AppDataDirectory))
                {
                    Directory.CreateDirectory(AppConstants.AppDataDirectory);
                }
                string yaml = serializer.Serialize(this);
                File.WriteAllText(SettingsFilePath, yaml);
            }
            catch (Exception)
            {
                // 保存エラーは無視
            }
        }
    }
}
