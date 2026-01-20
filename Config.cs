using System;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LargeFolderFinder
{
    /// <summary>
    /// 詳しい人向けの高度な設定を管理するクラス (Config.yaml)
    /// </summary>
    public class Config
    {
        public int MaxDepthForCount { get; set; } = 3;
        public bool UseParallelScan { get; set; } = true;
        public bool SkipFolderCount { get; set; } = false;
        public bool UsePhysicalSize { get; set; } = true;


        private static string ConfigFilePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConstants.ConfigFileName);

        /// <summary>
        /// config.yaml から設定を読み込む。
        /// </summary>
        public static Config Load()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    using (var fs = new FileStream(ConfigFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs))
                    {
                        var deserializer = new DeserializerBuilder()
                            .WithNamingConvention(PascalCaseNamingConvention.Instance)
                            .Build();
                        var config = deserializer.Deserialize<Config>(sr);
                        if (config != null)
                        {
                            return config;
                        }
                    }
                }
            }
            catch
            {
                // 読み取りエラー時はデフォルト値を返す
            }

            var defaultConfig = new Config();
            if (!File.Exists(ConfigFilePath))
            {
                defaultConfig.Save();
            }
            return defaultConfig;
        }

        /// <summary>
        /// 設定を config.yaml に保存する
        /// </summary>
        public void Save()
        {
            try
            {
                var serializer = new SerializerBuilder()
                    .WithNamingConvention(PascalCaseNamingConvention.Instance)
                    .Build();
                string yaml = serializer.Serialize(this);
                File.WriteAllText(ConfigFilePath, yaml);
            }
            catch
            {
                // 保存エラーは無視
            }
        }
    }
}
