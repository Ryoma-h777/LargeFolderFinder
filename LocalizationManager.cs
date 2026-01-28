using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;
using System.Globalization;

namespace LargeFolderFinder
{
    /// <summary>
    /// ローカライズテキストのキー定義（YAMLファイルと順序を一致させること）
    /// </summary>
    public enum LanguageKey
    {
        Title,
        // ####### Header (Menu) #######
        // ## File
        MenuFile,
        MenuOpenConfig,
        MenuOpenLogSub,
        MenuRestartAdmin,
        MenuRestartAdminToolTip,
        MenuExit,
        // # View
        MenuView,
        MenuLayout,
        MenuLayoutVertical,
        MenuLayoutHorizontal,
        // # Help
        MenuHelp,
        MenuOpenReadme,
        MenuLicense,
        MenuAppLicense,
        MenuThirdPartyLicenses,
        MenuAbout,

        // # Viewer (Log, Readme, License)
        ViewerCopy,
        ViewerOpenFolder,
        ViewerClose,
        // # Version Modal
        AboutTitle,
        AboutMessage,
        DialogInfo,

        // ####### Scan Settings #######
        // ## Line 1 (Folder Browser)
        FolderLabel,
        BrowseButton,
        // # Scan Button
        ScanButtonToolTip,
        CancelButtonToolTip,
        // # Advance Setting Button
        OpenConfigToolTip,

        // ####### View #######
        ViewLabel,
        // # MinSize
        MinSizeLabel,
        MinSizeToolTip,
        // # FolderName<---Space--->Size
        SeparatorLabel,
        SeparatorToolTip,
        TabWidthLabel,
        // # Clipboard Copy Button
        CopyToolTip,
        IncludeFiles,
        // # Filter
        FilterLabel,
        FilterMode_Normal,
        FilterMode_Regex,
        FilterTooltip,
        FilterPlaceholder,

        // ####### Result #######
        LiveScanningMessage,

        // ####### Footer (Status & Progress) #######
        ReadyStatus,
        ScanningStatus,
        CancelledStatus,
        FinishedStatus,
        NotFoundMessage,
        FolderCountStatus,
        RenderingStatus,
        Unknown, // 進捗率表示用: 総数を把握していない時に表示されます。
        LabelError,
        UnitFolder,
        // # time
        RemainingTimeH,
        RemainingTimeM,
        RemainingTimeS,
        ProcessedFolders,
        ProcessingTime,
        UnitHour,
        UnitMinute,
        UnitSecond,
        UnitMillisecond,
        UnitElapsed,
        // # View Formatter
        ScanningProgressFormat,
        ScanningProgressIndeterminateFormat,
        ScanningStatusWithCountFormat,
        ElapsedFormat,
        TimeStatusFormat,

        // # tempolaty Notification
        CopyNotification,

        // ####### Error Messages #######
        ConfigError,
        ReadmeError,
        PathInvalidError,
        ThresholdInvalidError,
        InitializationError,
        ClipboardError,
        ContextOpen,
        HeaderName,
        HeaderSize,
        HeaderDate,
        HeaderType,
        HeaderOwner,
        ContextShowOwner,
    }

    /// <summary>
    /// ローカライズ（多言語対応）テキストを管理するクラス
    /// </summary>
    public class LocalizationManager
    {
        private static LocalizationManager? _instance;
        private Dictionary<string, Dictionary<string, string>> _loadedTexts = new();
        private List<LanguageConfig> _availableLanguages = new();
        private string _currentLanguage = "";

        public static LocalizationManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new LocalizationManager();
                    _instance.Initialize();
                }
                return _instance;
            }
        }

        public string CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                if (_currentLanguage != value && !string.IsNullOrEmpty(value))
                {
                    _currentLanguage = value;
                }
            }
        }

        private void Initialize()
        {
            try
            {
                string languagesDirPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages");
                if (Directory.Exists(languagesDirPath))
                {
                    var files = Directory.GetFiles(languagesDirPath, "*.yaml");
                    foreach (var filePath in files)
                    {
                        var code = Path.GetFileNameWithoutExtension(filePath);
                        string menuText = code;
                        try
                        {
                            var culture = new CultureInfo(code);
                            menuText = culture.NativeName;
                            // 先頭を大文字にする (fr, es などは小文字始まりのため)
                            if (menuText.Length > 1)
                            {
                                menuText = char.ToUpper(menuText[0]) + menuText.Substring(1);
                            }
                        }
                        catch
                        {
                            // CultureInfo が取得できない場合はファイル名をそのまま使用
                        }
                        _availableLanguages.Add(new LanguageConfig { Code = code, MenuText = menuText });
                    }
                }

                // 初期言語の決定（設定がない場合）
                if (string.IsNullOrEmpty(_currentLanguage))
                {
                    try
                    {
                        var currentCulture = CultureInfo.CurrentUICulture;

                        // 1. 完全一致 (例: ja-JP)
                        if (_availableLanguages.Any(l => l.Code.Equals(currentCulture.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            _currentLanguage = _availableLanguages.First(l => l.Code.Equals(currentCulture.Name, StringComparison.OrdinalIgnoreCase)).Code;
                        }
                        // 2. 親言語一致 (例: ja)
                        else if (_availableLanguages.Any(l => l.Code.Equals(currentCulture.Parent.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            _currentLanguage = _availableLanguages.First(l => l.Code.Equals(currentCulture.Parent.Name, StringComparison.OrdinalIgnoreCase)).Code;
                        }
                        // 3. 英語 (en)
                        else if (_availableLanguages.Any(l => l.Code.Equals("en", StringComparison.OrdinalIgnoreCase)))
                        {
                            _currentLanguage = "en";
                        }
                        // 4. 最初に見つかった言語
                        else if (_availableLanguages.Any())
                        {
                            _currentLanguage = _availableLanguages[0].Code;
                        }
                    }
                    catch
                    {
                        // エラー時は英語にフォールバック
                        if (_availableLanguages.Any(l => l.Code == "en")) _currentLanguage = "en";
                    }
                }
            }
            catch (Exception)
            {
                // エラー時は何もしない
            }
        }

        private void EnsureLoaded(string languageName)
        {
            if (string.IsNullOrEmpty(languageName) || _loadedTexts.ContainsKey(languageName)) return;

            try
            {
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages", languageName + ".yaml");
                if (File.Exists(filePath))
                {
                    using (var reader = new StreamReader(filePath))
                    {
                        var deserializer = new DeserializerBuilder().Build();
                        var dict = deserializer.Deserialize<Dictionary<string, string>>(reader);
                        if (dict != null)
                        {
                            _loadedTexts[languageName] = dict;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // ロード失敗
            }
            finally
            {
                GC.Collect();
            }
        }

        public List<LanguageConfig> GetAvailableLanguages()
        {
            return _availableLanguages;
        }

        public string GetText(LanguageKey key)
        {
            string keyName = key.ToString();
            string lang = _currentLanguage;

            if (string.IsNullOrEmpty(lang)) return keyName;

            EnsureLoaded(lang);

            if (_loadedTexts.TryGetValue(lang, out var texts))
            {
                if (texts.TryGetValue(keyName, out var text))
                {
                    return text.Replace("\\n", "\n");
                }
            }

            // フォールバック（en.yaml が存在すれば使用）
            if (lang != "en")
            {
                EnsureLoaded("en");
                if (_loadedTexts.TryGetValue("en", out var enTexts))
                {
                    if (enTexts.TryGetValue(keyName, out var text))
                    {
                        return text.Replace("\\n", "\n");
                    }
                }
            }

            return keyName;
        }
    }
}
