using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LargeFolderFinder
{
    /// <summary>
    /// Search session data including parameters and results
    /// </summary>
    public class SearchSession
    {
        public string Path { get; set; } = "";
        public double Threshold { get; set; } = AppConstants.DefaultThreshold;
        public AppConstants.SizeUnit Unit { get; set; } = AppConstants.SizeUnit.GB;
        public bool IncludeFiles { get; set; } = false;
        public AppConstants.SortTarget SortTarget { get; set; } = AppConstants.SortTarget.Size;
        public AppConstants.SortDirection SortDirection { get; set; } = AppConstants.SortDirection.Descending;
        public int SeparatorIndex { get; set; } = 1; // 1: Space (default)
        public int TabWidth { get; set; } = AppConstants.DefaultTabWidth;
        public FolderInfo? Result { get; set; }
    }

    /// <summary>
    /// Application settings management class (Cache.txt)
    /// </summary>
    public class CacheData
    {
        public string Language { get; set; } = "";
        public AppConstants.LayoutType LayoutMode { get; set; } = AppConstants.LayoutType.Vertical;

        public List<SearchSession> Sessions { get; set; } = new List<SearchSession>();
        public int SelectedIndex { get; set; } = 0;

        private static string SettingsFilePath =>
            Path.Combine(AppConstants.AppDataDirectory, AppConstants.CacheFileName);

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
                // Ignore load errors
            }
            return null;
        }

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
            catch (Exception ex)
            {
                Logger.Log("Failed Save Cache.", ex);
            }
        }
    }
}
