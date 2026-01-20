using System;
using System.Text;
using System.Windows;
using System.Threading;
using Microsoft.Win32;
using System.Windows.Media;
using System.Windows.Documents;
using System.Globalization;
using System.Windows.Controls;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using YamlDotNet.Serialization;

namespace LargeFolderFinder
{
    /// <summary>
    /// メインクラス
    /// </summary>
    public partial class MainWindow : Window
    {
        private CancellationTokenSource? _cts;
        private FolderInfo? _lastScanResult;
        private bool _isScanning = false; // 明示的なフラグ
        private DispatcherTimer? _memoryTimer;

        public MainWindow()
        {
            try
            {
                Logger.Log(AppConstants.LogAppStarted);
                InitializeComponent();
                InitializeLocalization();
                LoadCache();
                UpdateLanguageMenu();
                InitializeUnitComboBox();
                ApplyLocalization();

                // 初回描画完了後にメモリを絞る
                this.ContentRendered += (s, e) => OptimizeMemory();

                // アイドル時のメモリ増加（WPF の内部的な動作や Automation 等によるもの）を抑制するため、
                // 低頻度（数分ごと）でバックグラウンドでのメモリ最適化を実行する
                InitializeMemoryTimer();
            }
            catch (Exception ex)
            {
                Logger.Log(AppConstants.LogInitError, ex);
                MessageBox.Show(
                    $"{LocalizationManager.Instance.GetText(LanguageKey.InitializationError)}\n{ex.Message}\n\n{LocalizationManager.Instance.GetText(LanguageKey.DetailLabel)}\n{ex.StackTrace}",
                    LocalizationManager.Instance.GetText(LanguageKey.AboutTitle),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Log(AppConstants.LogBrowseButtonClicked);
                //#if Dotnet48 // .Net 48用の処理
                var dialog = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
                {
                    Description = LocalizationManager.Instance.GetText(LanguageKey.FolderLabel),
                    UseDescriptionForTitle = true, // タイトルとしてDescriptionを使用
                    SelectedPath = PathTextBox.Text // 初期ディレクトリ設定
                };

                if (dialog.ShowDialog() == true)
                {
                    PathTextBox.Text = dialog.SelectedPath;
                    Logger.Log($"Folder selected via Ookii: {dialog.SelectedPath}");
                }
                //#endif // Dotnet48
            }
            catch (Exception ex)
            {
                Logger.Log("Error in BrowseButton_Click with Ookii.Dialogs", ex);
                MessageBox.Show($"{LocalizationManager.Instance.GetText(LanguageKey.LabelError)}\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        /// <summary>
        /// Scan ボタン実行時の処理
        /// </summary>
        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isScanning) return;
            var lm = LocalizationManager.Instance;

            if (string.IsNullOrWhiteSpace(PathTextBox.Text) || !System.IO.Directory.Exists(PathTextBox.Text))
            {
                MessageBox.Show(lm.GetText(LanguageKey.PathInvalidError), lm.GetText(LanguageKey.LabelError), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (!double.TryParse(ThresholdTextBox.Text, out double thresholdVal))
            {
                MessageBox.Show(lm.GetText(LanguageKey.ThresholdInvalidError), lm.GetText(LanguageKey.LabelError), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            AppConstants.SizeUnit selectedUnit = AppConstants.SizeUnit.GB;
            if (UnitComboBox.SelectedItem is AppConstants.SizeUnit unit)
            {
                selectedUnit = unit;
            }
            long thresholdBytes = (long)(thresholdVal * AppConstants.GetBytesPerUnit(selectedUnit));

            var config = Config.Load();
            SaveCache();
            _hasShownProgressError = false;

            _cts = new CancellationTokenSource();
            _isScanning = true;
            RunButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            OutputTextBox.Document.Blocks.Clear();
            ScanProgressBar.Visibility = Visibility.Visible;
            ScanProgressBar.Value = 0;
            StatusTextBlock.Text = lm.GetText(LanguageKey.FolderCountStatus);

            Logger.Log(string.Format(AppConstants.LogScanStart, PathTextBox.Text, thresholdVal + selectedUnit.ToString()));
            var sw = Stopwatch.StartNew();

            try
            {
                var scanner = new Scanner();

                int totalFolders = 0;
                if (!config.SkipFolderCount)
                {
                    totalFolders = await Scanner.CountFoldersAsync(PathTextBox.Text, config.MaxDepthForCount, _cts.Token);
                }

                var progress = new Progress<ScanProgress>(p =>
                {
                    try
                    {
                        string statusMsg;
                        if (!config.SkipFolderCount && totalFolders > 0)
                        {
                            double percentage = (double)p.ProcessedFolders / totalFolders * 100;
                            ScanProgressBar.Value = percentage;

                            string remainingStr = "";
                            if (p.EstimatedTimeRemaining.HasValue)
                            {
                                var remaining = p.EstimatedTimeRemaining.Value;
                                if (remaining.TotalHours >= 1)
                                {
                                    remainingStr = string.Format(lm.GetText(LanguageKey.RemainingTimeH),
                                        (int)remaining.TotalHours, lm.GetText(LanguageKey.UnitHour),
                                        remaining.Minutes, lm.GetText(LanguageKey.UnitMinute));
                                }
                                else if (remaining.TotalMinutes >= 1)
                                {
                                    remainingStr = string.Format(lm.GetText(LanguageKey.RemainingTimeM),
                                        (int)remaining.TotalMinutes, lm.GetText(LanguageKey.UnitMinute),
                                        remaining.Seconds, lm.GetText(LanguageKey.UnitSecond));
                                }
                                else
                                {
                                    remainingStr = string.Format(lm.GetText(LanguageKey.RemainingTimeS),
                                        (int)remaining.TotalSeconds, lm.GetText(LanguageKey.UnitSecond));
                                }
                            }

                            TimeSpan elapsedTs = sw.Elapsed;
                            string elapsedStr = FormatDuration(elapsedTs);
                            string totalFoldersStr = totalFolders > 0 ? totalFolders.ToString() : lm.GetText(LanguageKey.Unknown);
                            string folderProgress = $"{p.ProcessedFolders}/{totalFoldersStr}";

                            // YAML のフォーマットを使用して組み立て
                            string processedPart = string.Format(lm.GetText(LanguageKey.ProcessedFolders), folderProgress);
                            string elapsedPart = string.Format(lm.GetText(LanguageKey.ElapsedFormat), elapsedStr, lm.GetText(LanguageKey.UnitElapsed));

                            string timePart = string.IsNullOrEmpty(remainingStr)
                                ? elapsedPart
                                : string.Format(lm.GetText(LanguageKey.TimeStatusFormat), elapsedPart, remainingStr);

                            statusMsg = string.Format(lm.GetText(LanguageKey.ScanningProgressFormat),
                                lm.GetText(LanguageKey.ScanningStatus), percentage, processedPart, timePart);
                        }
                        else
                        {
                            TimeSpan elapsedTs = sw.Elapsed;
                            string elapsedStr = FormatDuration(elapsedTs);
                            string processedPart = string.Format(lm.GetText(LanguageKey.ProcessedFolders), p.ProcessedFolders);
                            string elapsedPart = string.Format(lm.GetText(LanguageKey.ElapsedFormat), elapsedStr, lm.GetText(LanguageKey.UnitElapsed));

                            // 進行不能時（総数不明）
                            statusMsg = string.Format(lm.GetText(LanguageKey.ScanningProgressIndeterminateFormat), processedPart, elapsedPart);
                            ScanProgressBar.IsIndeterminate = true;
                        }

                        StatusTextBlock.Text = statusMsg;

                        // リアルタイム表示の更新
                        if (p.CurrentResult != null)
                        {
                            _lastScanResult = p.CurrentResult;
                            RenderResult();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(AppConstants.LogScanProgressError, ex);
                        if (!_hasShownProgressError)
                        {
                            var lm = LocalizationManager.Instance;
                            _hasShownProgressError = true;
                            MessageBox.Show($"{lm.GetText(LanguageKey.ProgressErrorLabel)} {ex.Message}\n{ex.StackTrace}", lm.GetText(LanguageKey.DebugInfoTitle));
                        }
                    }
                });

                if (config.SkipFolderCount)
                {
                    StatusTextBlock.Text = lm.GetText(LanguageKey.ScanningStatus);
                    ScanProgressBar.IsIndeterminate = true;
                }
                else
                {
                    StatusTextBlock.Text = string.Format(lm.GetText(LanguageKey.ScanningStatusWithCountFormat),
                        lm.GetText(LanguageKey.ScanningStatus), totalFolders, lm.GetText(LanguageKey.UnitFolder));
                }

                var result = await Scanner.RunScan(PathTextBox.Text, thresholdBytes, totalFolders, config.MaxDepthForCount, config.UseParallelScan, config.UsePhysicalSize, progress, _cts.Token);

                _lastScanResult = result;
                sw.Stop();
                string timeStr = FormatDuration(sw.Elapsed);
                StatusTextBlock.Text = $"{lm.GetText(LanguageKey.FinishedStatus)} [{string.Format(lm.GetText(LanguageKey.ProcessingTime), timeStr)}]";
                Logger.Log(string.Format(AppConstants.LogScanSuccess, timeStr));
            }
            catch (OperationCanceledException)
            {
                ScanProgressBar.Visibility = Visibility.Collapsed;
                StatusTextBlock.Text = lm.GetText(LanguageKey.CancelledStatus);
            }
            catch (Exception ex)
            {
                Logger.Log(AppConstants.LogScanError, ex);
#if DEBUG
                Logger.OpenLogFile();
#endif
                ScanProgressBar.Visibility = Visibility.Collapsed;
                StatusTextBlock.Text = lm.GetText(LanguageKey.LabelError) + ex.Message;
                // MessageBox はログに出力されるため不要との指示により削除
            }
            finally
            {
                _isScanning = false;
                RunButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
                _cts?.Dispose();
                _cts = null;
                ScanProgressBar.Visibility = Visibility.Collapsed;
                ScanProgressBar.IsIndeterminate = false;
                RenderResult(); // フラグを false にした後に確実に描画
                OptimizeMemory();
            }
        }

        /// <summary>
        /// キャンセルボタン実行時処理
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }

        private void MenuConfig_Click(object sender, RoutedEventArgs e)
        {
            OpenConfigButton_Click(sender, e);
        }

        private void Input_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                // Move focus to avoid validation issues or ensure binding updates if any
                // FocusManager.SetFocusedElement(this, RunButton); 
                // However, directly clicking is usually fine.
                ScanButton_Click(sender, e);
            }
        }



        public static bool IsAdministrator()
        {
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
            {
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
        }

        private void MenuReadme_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var lm = LocalizationManager.Instance;
                string currentLang = lm.CurrentLanguage;
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string readmeDir = Path.Combine(baseDir, AppConstants.ReadmeDirectoryName);

                // 優先順位: 1.現在の言語, 2.英語, 3.日本語
                string[] targetLangs = { currentLang, "en", "ja" };
                string? selectedPath = null;

                foreach (var lang in targetLangs)
                {
                    string path = Path.Combine(readmeDir, AppConstants.GetReadmeFileName(lang));
                    if (File.Exists(path))
                    {
                        selectedPath = path;
                        break;
                    }
                }

                if (selectedPath != null)
                {
                    // Process.Start replaced with TextViewer
                    Logger.Log($"Opened Readme: {Path.GetFileName(selectedPath)}");
                    OpenOrActivateTextViewer(selectedPath);
                }
                else
                {
                    Logger.Log("Readme not found.");
                    throw new FileNotFoundException(lm.GetText(LanguageKey.ReadmeNotFound));
                }
            }
            catch (Exception ex)
            {
                var lm = LocalizationManager.Instance;
                Logger.Log(AppConstants.LogReadmeError, ex);
                MessageBox.Show($"{lm.GetText(LanguageKey.ReadmeError)}{ex.Message}", lm.GetText(LanguageKey.LabelError), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuAppLicense_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Log($"Opened App License via menu.");
                var lm = LocalizationManager.Instance;
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string licensePath = Path.Combine(baseDir, AppConstants.LicenseDirectoryName, AppConstants.AppLicenseFileName);

                if (File.Exists(licensePath))
                {
                    // Process.Start replaced with TextViewer
                    new TextViewer(licensePath).ShowDialog();
                }
                else
                {
                    throw new FileNotFoundException(lm.GetText(LanguageKey.LicenseNotFoundError), licensePath);
                }
            }
            catch (Exception ex)
            {
                var lm = LocalizationManager.Instance;
                Logger.Log("Failed to open App License.", ex);
                MessageBox.Show($"{lm.GetText(LanguageKey.ReadmeError)}{ex.Message}", lm.GetText(LanguageKey.LabelError), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuThirdPartyLicenses_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Log($"Opened ThirdPartyLicenses via menu.");
                var lm = LocalizationManager.Instance;
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string licensePath = Path.Combine(baseDir, AppConstants.LicenseDirectoryName, AppConstants.ThirdPartyNoticesFileName);

                if (File.Exists(licensePath))
                {
                    // Process.Start replaced with TextViewer
                    new TextViewer(licensePath).ShowDialog();
                }
                else
                {
                    throw new FileNotFoundException(string.Format(lm.GetText(LanguageKey.LicenseNotFoundError), Path.GetFileName(licensePath)));
                }
            }
            catch (Exception ex)
            {
                var lm = LocalizationManager.Instance;
                Logger.Log(AppConstants.LogThirdPartyNoticesError, ex);
                MessageBox.Show($"{lm.GetText(LanguageKey.LicenseNotFoundError)}{ex.Message}", lm.GetText(LanguageKey.LabelError), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuFile_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            try
            {
                MenuOpenLogSub.Items.Clear();
                string logsDir = AppConstants.LogsDirectoryPath;
                if (System.IO.Directory.Exists(logsDir))
                {
                    var files = System.IO.Directory.GetFiles(logsDir, "*_Log.txt")
                                         .Select(f => new System.IO.FileInfo(f))
                                         .OrderByDescending(f => f.CreationTime)
                                         .ToList();

                    foreach (var file in files)
                    {
                        var item = new MenuItem
                        {
                            Header = file.Name,
                            Tag = file.FullName
                        };
                        item.Click += MenuLogItem_Click;
                        MenuOpenLogSub.Items.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to populate log menu", ex);
            }
        }

        private void MenuLogItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is string path)
            {
                if (System.IO.File.Exists(path))
                {
                    Logger.Log($"Opened Log: {Path.GetFileName(path)}");
                    OpenOrActivateTextViewer(path);
                }
            }
        }

        private void OpenOrActivateTextViewer(string path)
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (window is TextViewer viewer && string.Equals(viewer.FilePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    if (window.WindowState == WindowState.Minimized)
                    {
                        window.WindowState = WindowState.Normal;
                    }
                    Logger.Log($"Already Opened TextViewer for {Path.GetFileName(path)}");
                    window.Activate();
                    return;
                }
            }
            Logger.Log($"Opened TextViewer for {Path.GetFileName(path)}");
            new TextViewer(path).Show();
        }

        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            Logger.Log("Opened About dialog");
            var lm = LocalizationManager.Instance;
            var format = lm.GetText(LanguageKey.AboutMessage);
            var message = string.Format(format, AppInfo.Title, AppInfo.Version, AppInfo.Copyright);
            MessageBox.Show(message, lm.GetText(LanguageKey.AboutTitle), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void MenuRestartAdmin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Process.GetCurrentProcess().MainModule?.FileName,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                if (psi.FileName != null)
                {
                    Process.Start(psi);
                    Application.Current.Shutdown();
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // ユーザーがキャンセルした場合などはここに来る
                // 何もしない
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to restart as admin", ex);
                var lm = LocalizationManager.Instance;
                MessageBox.Show($"{lm.GetText(LanguageKey.LabelError)}{ex.Message}", lm.GetText(LanguageKey.LabelError), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            Logger.Log("Application exiting via menu...");
            Application.Current.Shutdown();
        }

        private void OpenConfigButton_Click(object sender, RoutedEventArgs e)
        {
            var lm = LocalizationManager.Instance;
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConstants.ConfigFileName);
                if (!File.Exists(configPath))
                {
                    Config.Load();
                }

                if (File.Exists(configPath))
                {
                    Logger.Log("Opened Advanced Settings (Config.txt)");
                    Process.Start(new ProcessStartInfo(configPath) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Logger.Log(AppConstants.LogConfigError, ex);
                MessageBox.Show($"{lm.GetText(LanguageKey.ConfigError)}{ex.Message}", lm.GetText(LanguageKey.LabelError), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string text = new TextRange(OutputTextBox.Document.ContentStart, OutputTextBox.Document.ContentEnd).Text;
                if (string.IsNullOrWhiteSpace(text)) return;

                Clipboard.SetText(text);
                ShowNotification(LocalizationManager.Instance.GetText(LanguageKey.CopyNotification));
            }
            catch (Exception ex)
            {
                var lm = LocalizationManager.Instance;
                Logger.Log(AppConstants.LogClipboardError, ex);
                MessageBox.Show($"{lm.GetText(LanguageKey.ClipboardError)}{ex.Message}", lm.GetText(LanguageKey.LabelError), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ShowNotification(string message)
        {
            NotificationTextBlock.Text = message;
            await Task.Delay(3000);
            if (NotificationTextBlock.Text == message)
            {
                NotificationTextBlock.Text = "";
            }
        }

        /// <summary>
        /// アプリ起動時の言語初期化
        /// </summary>
        private void InitializeLocalization()
        {
            try
            {
                Logger.Log(AppConstants.LogInitStart);
                var settings = CacheData.Load();
                if (settings != null && !string.IsNullOrEmpty(settings.Language))
                {
                    // 以前の "Japanese" 等のレガシー値が入っている場合は、ファイルがないため
                    // LocalizationManager 側で無視される（フォールバック）か、
                    // もしくはここで明示的に対応が必要だが、ユーザー要望により無視して
                    // OS設定（LocalizationManagerの初期値）を優先させてもよい。
                    // ただし、明示的に指定されている有効な言語コード(ja, en等)であれば適用する。
                    LocalizationManager.Instance.CurrentLanguage = settings.Language;
                }
                else
                {
                    // キャッシュがない場合、または言語設定が空の場合
                    // LocalizationManager は既に OS 設定等を元に言語を決定しているはずなので、
                    // その値をキャッシュに保存してファイルを作成しておく。
                    // これにより、初回起動直後から言語設定がファイルに反映された状態になる。
                    var defaultSettings = new CacheData
                    {
                        Language = LocalizationManager.Instance.CurrentLanguage
                    };
                    defaultSettings.Save();
                }
                Logger.Log(AppConstants.LogInitSuccess);
            }
            catch (Exception ex)
            {
                Logger.Log(AppConstants.LogInitError, ex);
            }
        }

        /// <summary>
        /// 言語切替メニュークリック時
        /// </summary>
        private void MenuLanguage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is MenuItem menuItem && menuItem.Tag is string lang)
                {
                    Logger.Log(string.Format(AppConstants.LogLangChangeStart, lang));
                    LocalizationManager.Instance.CurrentLanguage = lang;
                    SaveCache();
                    ApplyLocalization();

                    if (_lastScanResult != null)
                    {
                        RenderResult();
                    }
                    OptimizeMemory(); // Added
                    Logger.Log(string.Format(AppConstants.LogLangChangeSuccess, lang));
                }
            }
            catch (Exception ex)
            {
                Logger.Log(AppConstants.LogLangChangeError, ex);
            }
        }

        /// <summary>
        /// Config.txt に基づいて言語メニューを動的に更新する
        /// </summary>
        private void UpdateLanguageMenu()
        {
            try
            {
                if (MenuLanguage == null) return;
                Logger.Log(AppConstants.LogUpdateMenuStart);

                MenuLanguage.Items.Clear();
                var languages = LocalizationManager.Instance.GetAvailableLanguages();

                foreach (var lang in languages)
                {
                    var item = new MenuItem
                    {
                        Header = lang.MenuText,
                        Tag = lang.Code
                    };
                    item.Click += MenuLanguage_Click;
                    MenuLanguage.Items.Add(item);
                }
                Logger.Log(AppConstants.LogUpdateMenuSuccess);
            }
            catch (Exception ex)
            {
                Logger.Log(AppConstants.LogUpdateMenuError, ex);
            }
        }

        /// <summary>
        /// ローカライズを UI に適用する
        /// </summary>
        private void ApplyLocalization()
        {
            try
            {
                Logger.Log(AppConstants.LogApplyLocStart);
                var lm = LocalizationManager.Instance;

                this.Title = lm.GetText(LanguageKey.Title);

                // Menu
                MenuFile.Header = lm.GetText(LanguageKey.MenuFile);
                MenuOpenConfig.Header = lm.GetText(LanguageKey.MenuOpenConfig);
                MenuOpenLogSub.Header = lm.GetText(LanguageKey.MenuOpenLogSub);
                MenuExit.Header = lm.GetText(LanguageKey.MenuExit);
                MenuRestartAdmin.Header = lm.GetText(LanguageKey.MenuRestartAdmin);
                MenuRestartAdmin.IsEnabled = !IsAdministrator(); // 管理者なら無効化

                MenuHelp.Header = lm.GetText(LanguageKey.MenuHelp);
                MenuOpenReadme.Header = lm.GetText(LanguageKey.MenuOpenReadme);
                MenuLicense.Header = lm.GetText(LanguageKey.MenuLicense);
                MenuAppLicense.Header = lm.GetText(LanguageKey.MenuAppLicense);
                MenuThirdPartyLicenses.Header = lm.GetText(LanguageKey.MenuThirdPartyLicenses);
                MenuAbout.Header = lm.GetText(LanguageKey.MenuAbout);

                // Controls
                FolderLabel.Text = lm.GetText(LanguageKey.FolderLabel);

                // Labels
                string unitStr = (UnitComboBox?.SelectedItem as AppConstants.SizeUnit? ?? AppConstants.SizeUnit.GB).ToString();
                SearchSizeLabel.Text = string.Format(lm.GetText(LanguageKey.SearchSizeLabel), unitStr);
                SearchSizeLabel.ToolTip = lm.GetText(LanguageKey.SearchSizeToolTip);
                SeparatorLabel.Text = lm.GetText(LanguageKey.SeparatorLabel);
                SeparatorLabel.ToolTip = string.Format(lm.GetText(LanguageKey.SeparatorToolTip), unitStr);

                // Buttons
                RunButton.Content = lm.GetText(LanguageKey.ScanButton);
                CancelButton.Content = lm.GetText(LanguageKey.CancelButton);
                CancelButton.ToolTip = lm.GetText(LanguageKey.CancelToolTip);
                OpenConfigButton.ToolTip = lm.GetText(LanguageKey.OpenConfigToolTip);
                CopyButton.ToolTip = lm.GetText(LanguageKey.CopyToolTip);

                // Status
                // スキャン中に言語を切り替えた場合、ステータスを「準備完了」に戻さないようにする
                if (_cts == null)
                {
                    StatusTextBlock.Text = lm.GetText(LanguageKey.ReadyStatus);
                }
                else
                {
                    StatusTextBlock.Text = lm.GetText(LanguageKey.ScanningStatus);
                }
                Logger.Log(AppConstants.LogApplyLocSuccess);
            }
            catch (Exception ex)
            {
                Logger.Log(AppConstants.LogApplyLocError, ex);
            }
        }

        private void SeparatorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SeparatorComboBox == null || TabWidthPanel == null) return;
            bool isTabMode = SeparatorComboBox.SelectedIndex == 0;
            TabWidthPanel.Visibility = isTabMode ? Visibility.Visible : Visibility.Collapsed;
            RenderResult();
        }

        /// <summary>
        /// タブ幅変更時の処理
        /// </summary>
        private void TabWidthTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RenderResult();
        }

        private void InitializeUnitComboBox()
        {
            if (UnitComboBox == null) return;
            UnitComboBox.ItemsSource = Enum.GetValues(typeof(AppConstants.SizeUnit));
            UnitComboBox.SelectedIndex = 2; // Default to GB
        }

        private void UnitComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UnitComboBox == null || ThresholdTextBox == null) return;

            // 単位変更時に数値を変換する (例: 1 GB -> 1024 MB)
            // 初期化時など RemovedItems が空の場合はスキップ
            if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is AppConstants.SizeUnit oldUnit &&
                e.AddedItems.Count > 0 && e.AddedItems[0] is AppConstants.SizeUnit newUnit)
            {
                if (double.TryParse(ThresholdTextBox.Text, out double currentVal))
                {
                    long oldBytes = AppConstants.GetBytesPerUnit(oldUnit);
                    long newBytes = AppConstants.GetBytesPerUnit(newUnit);

                    // バイト数で計算してから新しい単位に戻す
                    double totalBytes = currentVal * oldBytes;
                    double newValue = totalBytes / newBytes;

                    // 小数点以下の表示制御（必要に応じて）
                    // 単純な ToString() でも多くの場合は問題ないが、読みやすさのために調整
                    ThresholdTextBox.Text = newValue.ToString();
                }
            }

            ApplyLocalization();
            RenderResult();
        }

        private static bool _hasShownProgressError = false;

        private void RenderResult()
        {
            try
            {
                if (_lastScanResult == null || OutputTextBox == null || SeparatorComboBox == null || UnitComboBox == null)
                {
                    if (OutputTextBox != null)
                    {
                        OutputTextBox.Document.Blocks.Clear();
                    }
                    return;
                }

                var lm = LocalizationManager.Instance;
                double thresholdVal = 0;
                double.TryParse(ThresholdTextBox.Text, out thresholdVal);

                AppConstants.SizeUnit selectedUnit = AppConstants.SizeUnit.GB;
                if (UnitComboBox.SelectedItem is AppConstants.SizeUnit unit)
                {
                    selectedUnit = unit;
                }
                long thresholdBytes = (long)(thresholdVal * AppConstants.GetBytesPerUnit(selectedUnit));

                bool isScanning = _isScanning && _cts != null;

                var sb = new StringBuilder();
                int maxLineLen = CalculateMaxLineLength(_lastScanResult, 0, true, true, thresholdBytes, isScanning, selectedUnit);
                int tabWidth = 8;
                if (int.TryParse(TabWidthTextBox.Text, out int parsedTabWidth) && parsedTabWidth > 0)
                {
                    tabWidth = parsedTabWidth;
                }
                int targetColumn = ((maxLineLen / tabWidth) + 1) * tabWidth;
                bool useSpaces = SeparatorComboBox.SelectedIndex == 1;

                PrintTreeRecursive(sb, _lastScanResult, indent: string.Empty, isLast: true, isRoot: true, targetColumn, useSpaces, tabWidth, thresholdBytes, isScanning, selectedUnit);

                OutputTextBox.Document.Blocks.Clear();
                var paragraph = new Paragraph(new Run(sb.ToString()));
                OutputTextBox.Document.Blocks.Add(paragraph);
            }
            catch (Exception ex)
            {
                Logger.Log(AppConstants.LogRenderError, ex);
                if (!_hasShownProgressError)
                {
                    var lm = LocalizationManager.Instance;
                    _hasShownProgressError = true;
                    MessageBox.Show($"{lm.GetText(LanguageKey.RenderErrorLabel)} {ex.Message}\n{ex.StackTrace}", lm.GetText(LanguageKey.DebugInfoTitle));
                }
            }
        }

        private int CalculateMaxLineLength(FolderInfo node, int indentLen, bool isRoot, bool isLast, long thresholdBytes, bool isScanning, AppConstants.SizeUnit unit)
        {
            if (node == null) return 0;
            // 閾値未満は計算に含めない（非表示のため）
            if (!isRoot && node.Size < thresholdBytes) return 0;

            // ... (rest of logic same until size part if size was included in calculation, 
            // but actually size is appended at the end. Wait, print logic appends size at the end.
            // CalculateMaxLineLength calculates string width of the NAME part + indentation?
            // Let's check original.
            // Original: currentLen = indentLen + prefixLen + GetStringWidth(node.Name);
            // It seems it calculates UP TO the name. The tab calculation happens after name.
            // But wait, "targetColumn" is calculated from maxLineLen.
            // So maxLineLen IS the max length of the name part.
            // So CalculateMaxLineLength DOES NOT depend on Unit directly for the calculation, 
            // BUT it filters children based on thresholdBytes.
            // So I just need to update the recursive call signature.

            int prefixLen = 0;
            if (!isRoot)
            {
                string branch = isLast ? AppConstants.TreeLastBranch : AppConstants.TreeBranch;
                prefixLen = GetStringWidth(branch);
            }
            int currentLen = indentLen + prefixLen + GetStringWidth(node.Name);
            int max = currentLen;

            int childIndentLen = indentLen;
            if (!isRoot)
            {
                string indentPart = isLast
                    ? AppConstants.TreeSpace + AppConstants.TreeSpace
                    : AppConstants.TreeVertical + AppConstants.TreeSpace;
                childIndentLen += GetStringWidth(indentPart);
            }

            if (node.Children != null)
            {
                List<FolderInfo> childrenCopy;
                lock (node.Children) { childrenCopy = node.Children.ToList(); }

                var visibleChildren = childrenCopy.Where(c => c.Size >= thresholdBytes).OrderBy(c => c.Name).ToList();
                for (int i = 0; i < visibleChildren.Count; i++)
                {
                    bool isChildLast = (i == visibleChildren.Count - 1);
                    int childMax = CalculateMaxLineLength(visibleChildren[i], childIndentLen, false, isChildLast, thresholdBytes, isScanning, unit);
                    if (childMax > max) max = childMax;
                }
            }
            return max;
        }

        private void PrintTreeRecursive(StringBuilder sb, FolderInfo node, string indent, bool isLast, bool isRoot, int targetColumn, bool useSpaces, int tabWidth, long thresholdBytes, bool isScanning, AppConstants.SizeUnit unit)
        {
            if (node == null) return;

            var lm = LocalizationManager.Instance;

            // スキャン完了後、ルートフォルダ自体が閾値未満であれば、フォルダ名を出さずに NotFound メッセージのみを出す
            if (isRoot && !isScanning && node.Size < thresholdBytes)
            {
                sb.Append(AppConstants.TreeLastBranch).AppendLine(lm.GetText(LanguageKey.NotFoundMessage));
                return;
            }

            // ルート以外で閾値未満なら非表示
            if (!isRoot && node.Size < thresholdBytes) return;

            double sizeVal = (double)node.Size / AppConstants.GetBytesPerUnit(unit);
            var nfi = (System.Globalization.NumberFormatInfo)System.Globalization.CultureInfo.InvariantCulture.NumberFormat.Clone();
            nfi.NumberGroupSeparator = AppConstants.DigitSeparator;
            nfi.NumberDecimalDigits = 0;

            string sizeNum = sizeVal.ToString("N0", nfi);
            string sizeStr = $"{sizeNum} {unit.ToString()}".PadLeft(AppConstants.BaseSizeLength);

            string lineContent = isRoot ?
                node.Name :
                indent + (isLast ? AppConstants.TreeLastBranch : AppConstants.TreeBranch) + node.Name;

            sb.Append(lineContent);
            int currentLen = GetStringWidth(lineContent);

            if (useSpaces)
            {
                while (currentLen < targetColumn)
                {
                    int nextTab = ((currentLen / tabWidth) + 1) * tabWidth;
                    int spacesNeeded = nextTab - currentLen;
                    sb.Append(AppConstants.TreeSpace[0], spacesNeeded);
                    currentLen = nextTab;
                }
            }
            else
            {
                while (currentLen < targetColumn)
                {
                    sb.Append('\t');
                    int nextTab = ((currentLen / tabWidth) + 1) * tabWidth;
                    currentLen = nextTab;
                }
            }

            sb.Append(sizeStr);
            sb.AppendLine();

            string childIndent = indent;
            if (!isRoot)
            {
                childIndent += (isLast ? AppConstants.TreeSpace + AppConstants.TreeSpace : AppConstants.TreeVertical + AppConstants.TreeSpace);
            }

            List<FolderInfo> childrenCopy;
            lock (node.Children) { childrenCopy = node.Children.ToList(); }

            var visibleChildren = childrenCopy.Where(c => c.Size >= thresholdBytes).OrderBy(c => c.Name).ToList();
            for (int i = 0; i < visibleChildren.Count; i++)
            {
                PrintTreeRecursive(sb, visibleChildren[i], childIndent, isLast: i == visibleChildren.Count - 1, isRoot: false, targetColumn, useSpaces, tabWidth, thresholdBytes, isScanning, unit);
            }

            if (isRoot)
            {
                if (isScanning)
                {
                    // スキャン中
                    sb.AppendLine().AppendLine(string.Format(lm.GetText(LanguageKey.LiveScanningMessage), unit));
                }
                else if (visibleChildren.Count == 0)
                {
                    // スキャン完了しており、且つ表示すべき子フォルダが1つもない場合
                    sb.Append(AppConstants.TreeLastBranch).AppendLine(lm.GetText(LanguageKey.NotFoundMessage));
                }
            }
        }

        private int GetStringWidth(string s)
        {
            int width = 0;
            foreach (char c in s)
            {
                width += ((c >= 0x00 && c < 0x81)
                 || (c == 0xf8f0)
                 || (c >= 0xff61 && c < 0xffa0)
                 || (c >= 0xf8f1 && c < 0xf8f4))
                    ? 1 : 2;
            }
            return width;
        }

        private string FormatDuration(TimeSpan ts)
        {
            var lm = LocalizationManager.Instance;
            if (ts.TotalHours >= 1)
            {
                return $"{(int)ts.TotalHours}{lm.GetText(LanguageKey.UnitHour)}{ts.Minutes}{lm.GetText(LanguageKey.UnitMinute)}{ts.Seconds}{lm.GetText(LanguageKey.UnitSecond)}";
            }
            else if (ts.TotalMinutes >= 1)
            {
                return $"{(int)ts.TotalMinutes}{lm.GetText(LanguageKey.UnitMinute)}{ts.Seconds}{lm.GetText(LanguageKey.UnitSecond)}";
            }
            else if (ts.TotalSeconds >= 1)
            {
                return $"{(int)ts.TotalSeconds}{lm.GetText(LanguageKey.UnitSecond)}";
            }
            else
            {
                return $"{ts.Milliseconds}{lm.GetText(LanguageKey.UnitMillisecond)}";
            }
        }

        private void LoadCache()
        {
            try
            {
                Logger.Log(AppConstants.LogCacheLoadStart);
                var settings = CacheData.Load();
                if (settings != null)
                {
                    if (!string.IsNullOrWhiteSpace(settings.LastFolderPath))
                    {
                        PathTextBox.Text = settings.LastFolderPath;
                    }
                    ThresholdTextBox.Text = settings.LastThresholdGB.ToString();

                    if (UnitComboBox != null)
                    {
                        UnitComboBox.SelectedItem = settings.Unit;
                    }

                    if (settings.SeparatorIndex >= 0 && settings.SeparatorIndex < SeparatorComboBox.Items.Count)
                    {
                        SeparatorComboBox.SelectedIndex = settings.SeparatorIndex;
                    }
                    TabWidthTextBox.Text = settings.TabWidth.ToString();
                }
                OptimizeMemory(); // Added
                Logger.Log(AppConstants.LogCacheLoadSuccess);
            }
            catch (Exception ex)
            {
                Logger.Log(AppConstants.LogCacheLoadError, ex);
            }
        }

        private void SaveCache()
        {
            try
            {
                Logger.Log(AppConstants.LogCacheSaveStart);
                double threshold = AppConstants.DefaultThreshold;
                double.TryParse(ThresholdTextBox.Text, out threshold);
                int tabWidth = 8;
                int.TryParse(TabWidthTextBox.Text, out tabWidth);

                AppConstants.SizeUnit unit = AppConstants.SizeUnit.GB;
                if (UnitComboBox.SelectedItem is AppConstants.SizeUnit selected)
                {
                    unit = selected;
                }

                var settings = new CacheData
                {
                    LastFolderPath = PathTextBox.Text,
                    LastThresholdGB = threshold,
                    SeparatorIndex = SeparatorComboBox.SelectedIndex,
                    TabWidth = tabWidth,
                    Language = LocalizationManager.Instance.CurrentLanguage,
                    Unit = unit
                };
                settings.Save();
                Logger.Log(AppConstants.LogCacheSaveSuccess);
            }
            catch (Exception ex)
            {
                Logger.Log(AppConstants.LogCacheSaveError, ex);
            }
        }

        #region Helper Methods (Memory Optimization)

        /// <summary>
        /// 不要なメモリを解放し、OS に返却します。
        /// </summary>
        private void OptimizeMemory()
        {
            try
            {
                // 管理メモリの強制回収
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);

                // ワーキングセットの最小化を OS に要求
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    using (var process = System.Diagnostics.Process.GetCurrentProcess())
                    {
                        LargeFolderFinder.Win32.SetProcessWorkingSetSize(process.Handle, -1, -1);
                    }
                }
                Logger.Log(AppConstants.LogOptimizeMemory);
            }
            catch
            {
                // エラーは無視
            }
        }

        private void InitializeMemoryTimer()
        {
            _memoryTimer = new DispatcherTimer(DispatcherPriority.Background);
            _memoryTimer.Interval = TimeSpan.FromMinutes(AppConstants.MemoryOptimizeIntervalMinutes);
            _memoryTimer.Tick += (s, e) => OptimizeMemory();
            _memoryTimer.Start();
        }

        #endregion
    }
}
