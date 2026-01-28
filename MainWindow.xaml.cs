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
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using YamlDotNet.Serialization;
using MessagePack;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace LargeFolderFinder
{
    /// <summary>
    /// メインクラス
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private DispatcherTimer? _memoryTimer;
        private DispatcherTimer? _filterDebounceTimer;
        private bool _hasShownProgressError = false;
        private readonly ResultFormatter _formatter = new ResultFormatter();
        private AppConstants.LayoutType _currentLayoutMode = AppConstants.LayoutType.Vertical; // Default

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public ObservableCollection<SessionData> Sessions { get; set; } = new ObservableCollection<SessionData>();

        // SelectedIndex Dependency Property to allow binding to TabControl
        public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
            nameof(SelectedIndex), typeof(int), typeof(MainWindow),
            new PropertyMetadata(0));

        public int SelectedIndex
        {
            get => (int)GetValue(SelectedIndexProperty);
            set => SetValue(SelectedIndexProperty, value);
        }

        public SessionData? CurrentSession
        {
            get
            {
                if (SelectedIndex >= 0 && SelectedIndex < Sessions.Count)
                    return Sessions[SelectedIndex];
                return null;
            }
        }

        public IMainLayoutView? CurrentLayoutView => CurrentSession?.CurrentView as IMainLayoutView;

        public MainWindow()
        {
            try
            {
                Logger.Log(AppConstants.LogAppStarted);
                InitializeComponent();
                this.DataContext = this;

                // Register Commands
                this.CommandBindings.Add(new CommandBinding(LocalCommands.ShowOwner, ShowOwner_Executed));

                InitializeLocalization();
                LoadCache();
                UpdateLanguageMenu();
                ApplyLocalization();

                // Initialize Debounce Timer
                _filterDebounceTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(300)
                };
                _filterDebounceTimer.Tick += (s, e) =>
                {
                    _filterDebounceTimer.Stop();
                    if (SessionTabControl.SelectedItem is SessionData session)
                    {
                        _ = RenderResult(session);
                    }
                };

                // Binding updates
                Sessions.CollectionChanged += (s, e) =>
                {
                    Logger.Log("Sessions collection changed");
                    // Handle any collection changes if needed
                };

                // 初回描画完了後にメモリを絞る
                this.ContentRendered += (s, e) => OptimizeMemory();

                // アイドル時のメモリ最適化実行
                InitializeMemoryTimer();
            }
            catch (Exception ex)
            {
                Logger.Log(AppConstants.LogInitError, ex);
                MessageBox.Show(
                    $"{LocalizationManager.Instance.GetText(LanguageKey.InitializationError)}\n" +
                    $"{ex.Message}\n\n" +
                    $"Error Detail:\n{ex.StackTrace}",
                    LocalizationManager.Instance.GetText(LanguageKey.AboutTitle),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void InitializeView(IMainLayoutView view, SessionData session)
        {
            if (view == null) return;

            // Bind events logic from the View to MainWindow logic
            // Note: View controls already call _mainWindow.ScanButton_Click etc via XAML/CodeBehind wiring.
            // We just need to initialize ComboBoxes and set values.

            view.UnitComboBox.Items.Clear();
            foreach (var u in Enum.GetNames(typeof(AppConstants.SizeUnit)))
                view.UnitComboBox.Items.Add(u);


            view.SeparatorComboBox.Items.Clear();
            view.SeparatorComboBox.Items.Add("Tab");
            view.SeparatorComboBox.Items.Add("Space");

            // Event Handlers for Re-rendering
            view.SeparatorComboBox.SelectionChanged += (s, e) =>
            {
                view.TabWidthArea.Visibility = view.SeparatorComboBox.SelectedIndex == (int)AppConstants.Separator.Tab
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                _ = RenderResult(session);
            };

            view.MinSizeTextBox.TextChanged += (s, e) => { _ = RenderResult(session); };
            view.UnitComboBox.SelectionChanged += (s, e) => { _ = RenderResult(session); };
            view.IncludeFilesCheckBox.Click += (s, e) => { _ = RenderResult(session); };
            view.TabWidthTextBox.TextChanged += (s, e) => { _ = RenderResult(session); };

            // FontSize Logic
            view.FontSizeTextBox.TextChanged += (s, e) =>
            {
                if (double.TryParse(view.FontSizeTextBox.Text, out double newSize) && newSize > 0)
                {
                    view.OutputListBox.FontSize = newSize;
                    var config = AppSettings.Load() ?? new AppSettings();
                    config.FontSize = newSize;
                    config.Save();
                }
            };

            // Filter Events
            if (view.FilterTextBox != null)
            {
                view.FilterTextBox.TextChanged += (s, e) => OnSettingChanged(s, e);
            }
            if (view.FilterModeComboBox != null)
            {
                view.FilterModeComboBox.SelectionChanged += (s, e) => OnSettingChanged(s, e);
            }

            // Set Initial Values from Session
            view.MinSizeTextBox.Text = session.Threshold.ToString();
            view.UnitComboBox.SelectedIndex = (int)session.Unit;
            view.IncludeFilesCheckBox.IsChecked = session.IncludeFiles;
            view.SeparatorComboBox.SelectedIndex = session.SeparatorIndex;
            view.TabWidthTextBox.Text = session.TabWidth.ToString();

            // Set Filter Values
            if (view.FilterTextBox != null)
            {
                view.FilterTextBox.Text = session.FilterText;
            }
            if (view.FilterModeComboBox != null)
            {
                view.FilterModeComboBox.SelectedIndex = session.FilterModeIndex;
            }

            // Initial Visibility
            view.TabWidthArea.Visibility = session.SeparatorIndex == (int)AppConstants.Separator.Tab ? Visibility.Visible : Visibility.Collapsed;

            // Initialize FontSize
            var settings = AppSettings.Load() ?? new AppSettings();
            view.FontSizeTextBox.Text = settings.FontSize.ToString();
            view.OutputListBox.FontSize = settings.FontSize;

            // Apply Localization
            view.ApplyLocalization(LocalizationManager.Instance);

            // Initial Loading State
            if (view.LoadingOverlay != null)
            {
                view.LoadingOverlay.Visibility = session.IsLoading ? Visibility.Visible : Visibility.Collapsed;
            }

            // Bind PropertyChanged for IsLoading
            session.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SessionData.IsLoading))
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (view.LoadingOverlay != null)
                        {
                            view.LoadingOverlay.Visibility = session.IsLoading ? Visibility.Visible : Visibility.Collapsed;
                        }
                    });
                }
            };
        }

        internal void OnSettingChanged(object sender, RoutedEventArgs e)
        {
            if (SessionTabControl.SelectedItem is SessionData session)
            {
                // 重い処理なのでデバウンスする
                _filterDebounceTimer?.Stop();
                _filterDebounceTimer?.Start();
            }
        }

        internal void OnFilterTextChanged(object sender, TextChangedEventArgs e) => OnSettingChanged(sender, e);
        internal void OnFilterModeChanged(object sender, SelectionChangedEventArgs e) => OnSettingChanged(sender, e);
        internal void OutputListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { /* Placeholder */ }

        private void SessionTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Only handle if the source is the TabControl itself (not inner controls)
            if (e.OriginalSource == SessionTabControl)
            {
                // Sync OLD session before switching
                if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is SessionData oldSession)
                {
                    SyncViewToSession(oldSession, true);
                }

                if (SessionTabControl.SelectedItem is SessionData session)
                {
                    EnsureViewCreated(session);
                    // Restore path from session to global textbox
                    pathTextBox.Text = session.Path;
                }
                UpdateHeaderFromSession();
            }
        }

        private void UpdateHeaderFromSession()
        {
            var session = CurrentSession;
            if (session != null)
            {
                pathTextBox.Text = session.Path;

                // Update buttons state
                bool isScanning = session.IsScanning;
                scanButton.IsEnabled = !isScanning;
                cancelButton.IsEnabled = isScanning;
            }
            else
            {
                pathTextBox.Text = "";
                scanButton.IsEnabled = true;
                cancelButton.IsEnabled = false;
            }
        }

        internal void BrowseButton_Click(object? sender, RoutedEventArgs e)
        {
            // Always update current session
            var session = CurrentSession;
            // Create new session if none? (Though expected to always have one)
            if (session == null) return;

            try
            {
                Logger.Log(AppConstants.LogBrowseButtonClicked);
                var dialog = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
                {
                    Description = LocalizationManager.Instance.GetText(LanguageKey.FolderLabel),
                    UseDescriptionForTitle = true,
                    SelectedPath = pathTextBox.Text
                };

                if (dialog.ShowDialog() == true)
                {
                    pathTextBox.Text = dialog.SelectedPath;
                    session.Path = dialog.SelectedPath;
                    OnPropertyChanged(nameof(Sessions)); // Force update if needed, but path is manually synced
                    Logger.Log($"Folder selected via Ookii: {dialog.SelectedPath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Error in BrowseButton_Click", ex);
                MessageBox.Show(
                    $"{LocalizationManager.Instance.GetText(LanguageKey.LabelError)}\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        internal async void ScanButton_Click(object? sender, RoutedEventArgs e)
        {
            var session = CurrentSession;
            if (session == null) return;
            var view = CurrentLayoutView;
            if (view == null) return;

            if (session.IsScanning) return;

            var lm = LocalizationManager.Instance;

            string path = pathTextBox.Text;
            if (string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path))
            {
                MessageBox.Show(
                    lm.GetText(LanguageKey.PathInvalidError),
                    lm.GetText(LanguageKey.LabelError),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            // Sync Settings from View and Path to Session
            if (!SyncViewToSession(session, true))
            {
                return;
            }

            // Restore/Recalculate values used in ScanButton_Click
            double thresholdVal = session.Threshold;
            AppConstants.SizeUnit selectedUnit = session.Unit;
            long thresholdBytes = (long)(thresholdVal * AppConstants.GetBytesPerUnit(selectedUnit));

            var config = Config.Load();
            SaveCache(); // 検索履歴の保存

            // Update session timestamp and rename file (User Requirement: Update date on re-scan)
            if (!string.IsNullOrEmpty(session.FileName))
            {
                SessionFileManager.Delete(session.FileName!);
            }
            session.CreatedAt = DateTime.Now;
            // Update filename immediately
            session.FileName = SessionFileManager.Save(session);

            _hasShownProgressError = false;

            session.Cts = new CancellationTokenSource();
            session.IsScanning = true;
            UpdateHeaderFromSession(); // Update buttons

            view.OutputListBox.ItemsSource = null;
            view.ScanProgressBar.Visibility = Visibility.Visible;
            view.ScanProgressBar.Value = 0;
            view.StatusTextBlock.Text = lm.GetText(LanguageKey.FolderCountStatus);

            Logger.Log(string.Format(AppConstants.LogScanStart, path, thresholdVal + selectedUnit.ToString()));
            var sw = Stopwatch.StartNew();

            try
            {
                int totalFolders = config.SkipFolderCount ? 0 : await Scanner.CountFoldersAsync(path, config.MaxDepthForCount, session.Cts.Token);
                var progress = new Progress<ScanProgress>(p =>
                {
                    try
                    {
                        string statusMsg;
                        if (!config.SkipFolderCount && totalFolders > 0)
                        {
                            double percentage = (double)p.ProcessedFolders / totalFolders * 100;
                            view.ScanProgressBar.Value = percentage;
                            view.ScanProgressBar.IsIndeterminate = false;

                            string remainingStr = "";
                            if (p.EstimatedTimeRemaining.HasValue)
                            {
                                var remaining = p.EstimatedTimeRemaining.Value;
                                if (remaining.TotalHours >= 1)
                                    remainingStr = string.Format(
                                        lm.GetText(LanguageKey.RemainingTimeH),
                                        (int)remaining.TotalHours,
                                        lm.GetText(LanguageKey.UnitHour),
                                        remaining.Minutes,
                                        lm.GetText(LanguageKey.UnitMinute));
                                else if (remaining.TotalMinutes >= 1)
                                    remainingStr = string.Format(
                                        lm.GetText(LanguageKey.RemainingTimeM),
                                        (int)remaining.TotalMinutes,
                                        lm.GetText(LanguageKey.UnitMinute),
                                        remaining.Seconds,
                                        lm.GetText(LanguageKey.UnitSecond));
                                else
                                    remainingStr = string.Format(
                                        lm.GetText(LanguageKey.RemainingTimeS),
                                        (int)remaining.TotalSeconds,
                                        lm.GetText(LanguageKey.UnitSecond));
                            }

                            string elapsedStr = _formatter.FormatDuration(sw.Elapsed);
                            string folderProgress = $"{p.ProcessedFolders}/" +
                                $"{(totalFolders > 0 ? totalFolders.ToString() : lm.GetText(LanguageKey.Unknown))}";
                            string processedPart = string.Format(lm.GetText(LanguageKey.ProcessedFolders), folderProgress);
                            string elapsedPart = string.Format(
                                lm.GetText(LanguageKey.ElapsedFormat),
                                elapsedStr,
                                lm.GetText(LanguageKey.UnitElapsed));
                            string timePart = string.IsNullOrEmpty(remainingStr)
                                ? elapsedPart
                                : string.Format(lm.GetText(LanguageKey.TimeStatusFormat), elapsedPart, remainingStr);
                            statusMsg = string.Format(
                                lm.GetText(LanguageKey.ScanningProgressFormat),
                                lm.GetText(LanguageKey.ScanningStatus),
                                percentage,
                                processedPart,
                                timePart);
                        }
                        else
                        {
                            view.ScanProgressBar.IsIndeterminate = true;
                            string elapsedStr = _formatter.FormatDuration(sw.Elapsed);
                            string processedPart = string.Format(lm.GetText(LanguageKey.ProcessedFolders), p.ProcessedFolders);
                            string elapsedPart = string.Format(
                                lm.GetText(LanguageKey.ElapsedFormat),
                                elapsedStr,
                                lm.GetText(LanguageKey.UnitElapsed));
                            statusMsg = string.Format(
                                lm.GetText(LanguageKey.ScanningProgressIndeterminateFormat),
                                processedPart,
                                elapsedPart);
                        }

                        view.StatusTextBlock.Text = statusMsg;

                        if (p.CurrentResult != null)
                        {
                            session.Result = p.CurrentResult;
                            _ = RenderResult(session); // Fire and forget
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(AppConstants.LogScanProgressError, ex);
                        if (!_hasShownProgressError)
                        {
                            _hasShownProgressError = true;
                            MessageBox.Show($"Failed to update progress: {ex.Message}", "Debug");
                        }
                    }
                });

                if (config.SkipFolderCount)
                {
                    view.StatusTextBlock.Text = lm.GetText(LanguageKey.ScanningStatus);
                    view.ScanProgressBar.IsIndeterminate = true;
                }
                else
                {
                    view.StatusTextBlock.Text = string.Format(
                        lm.GetText(LanguageKey.ScanningStatusWithCountFormat),
                        lm.GetText(LanguageKey.ScanningStatus),
                        totalFolders,
                        lm.GetText(LanguageKey.UnitFolder));
                }

                // Scan処理を実行
                var result = await Scanner.RunScan(
                    path,
                    thresholdBytes,
                    totalFolders,
                    config.MaxDepthForCount,
                    config.UseParallelScan,
                    config.UsePhysicalSize,
                    progress,
                    session.Cts.Token);

                session.Result = result;
                await RenderResult(session);
                // UIの描画完了を待ってから時間を止める
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                sw.Stop();
                session.LastScanDuration = sw.Elapsed;
                session.TotalFilesScanned = session.Result?.CountFolderRecursive() ?? 0;

                if (view != null)
                {
                    view.StatusTextBlock.Text = $"{lm.GetText(LanguageKey.FinishedStatus)} " +
                        $"{string.Format(lm.GetText(LanguageKey.ProcessingTime), _formatter.FormatDuration(sw.Elapsed))}";
                }
                Logger.Log(string.Format(AppConstants.LogScanSuccess, _formatter.FormatDuration(sw.Elapsed)));

                SaveCache(); // 検索結果の保存

                OptimizeMemory();
                session.Cts?.Dispose();
                session.Cts = null;
            }
            catch (OperationCanceledException)
            {
                Logger.Log("Canceled.");
                view.StatusTextBlock.Text = lm.GetText(LanguageKey.CancelledStatus);
            }
            catch (Exception ex)
            {
                Logger.Log(AppConstants.LogScanError, ex);
                view.StatusTextBlock.Text = lm.GetText(LanguageKey.LabelError) + ex.Message;
                // Restore global settings (like pathTextBox) if an error occurs
                SyncViewToSession(session, true);
            }
            finally
            {
                session.IsScanning = false;
                UpdateHeaderFromSession(); // Reset buttons

                view.ScanProgressBar.Visibility = Visibility.Collapsed;
                view.ScanProgressBar.IsIndeterminate = false;
                _ = RenderResult(session);
                OptimizeMemory();
                session.Cts?.Dispose();
                session.Cts = null;
            }
        }

        internal void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            var session = CurrentSession;
            if (session != null && session.IsScanning)
            {
                Logger.Log("Canceling scan...");
                session.Cts?.Cancel();
            }
        }

        private bool SyncViewToSession(SessionData session, bool isCurrentSession)
        {
            if (session == null) return false;
            var view = session.CurrentView as IMainLayoutView;
            if (view == null) return false;

            // UI settings sync (Per-session view items)
            if (!double.TryParse(view.MinSizeTextBox.Text, out double thresholdVal))
            {
                // Only show error if this is the active session we are trying to scan
                if (isCurrentSession)
                {
                    var lm = LocalizationManager.Instance;
                    MessageBox.Show(
                        lm.GetText(LanguageKey.ThresholdInvalidError),
                        lm.GetText(LanguageKey.LabelError),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                return false;
            }
            session.Threshold = thresholdVal;

            if (view.UnitComboBox.SelectedIndex >= 0)
            {
                session.Unit = (AppConstants.SizeUnit)view.UnitComboBox.SelectedIndex;
            }

            session.IncludeFiles = view.IncludeFilesCheckBox.IsChecked == true;
            session.SeparatorIndex = view.SeparatorComboBox.SelectedIndex;
            if (int.TryParse(view.TabWidthTextBox.Text, out int tw))
            {
                session.TabWidth = tw;
            }

            if (view.FilterTextBox != null)
            {
                session.FilterText = view.FilterTextBox.Text;
            }
            if (view.FilterModeComboBox != null)
            {
                session.FilterModeIndex = view.FilterModeComboBox.SelectedIndex;
            }

            // Global settings sync (Sync only if this IS the active session being viewed/scanned)
            if (isCurrentSession)
            {
                session.Path = pathTextBox.Text;
            }

            return true;
        }

        /// <summary>
        /// セッションデータをビューに同期（セッション→ビュー）
        /// </summary>
        private void SyncSessionToView(SessionData session)
        {
            if (session == null) return;
            var view = session.CurrentView as IMainLayoutView;
            if (view == null) return;

            // セッションの値をUIに反映
            view.MinSizeTextBox.Text = session.Threshold.ToString();
            view.UnitComboBox.SelectedIndex = (int)session.Unit;
            view.IncludeFilesCheckBox.IsChecked = session.IncludeFiles;
            view.SeparatorComboBox.SelectedIndex = session.SeparatorIndex;
            view.TabWidthTextBox.Text = session.TabWidth.ToString();

            if (view.FilterTextBox != null)
            {
                view.FilterTextBox.Text = session.FilterText;
            }
            if (view.FilterModeComboBox != null)
            {
                view.FilterModeComboBox.SelectedIndex = session.FilterModeIndex;
            }

            // Sync global pathTextBox
            pathTextBox.Text = session.Path;

            // Restore Status Bar
            if (!session.IsScanning)
            {
                var lm = LocalizationManager.Instance;
                if (session.LastScanDuration != TimeSpan.Zero || session.TotalFilesScanned > 0)
                {
                    string countText = session.IsCounting ? "" : $" {lm.GetText(LanguageKey.FolderCountStatus)}: {(session.Result?.CountFolderRecursive() ?? 0):N0}";
                    view.StatusTextBlock.Text = $"{lm.GetText(LanguageKey.FinishedStatus)} {string.Format(lm.GetText(LanguageKey.ProcessingTime), _formatter.FormatDuration(session.LastScanDuration))} ({session.TotalFilesScanned:N0} files){countText}";
                }
                else
                {
                    view.StatusTextBlock.Text = lm.GetText(LanguageKey.ReadyStatus);
                }
            }
        }

        internal async void CopyButton_Click(object? sender, RoutedEventArgs e)
        {
            var view = CurrentLayoutView;
            if (view == null) return;
            // Get current session safely
            if (SelectedIndex < 0 || SelectedIndex >= Sessions.Count) return;
            var session = Sessions[SelectedIndex];

            try
            {
                // バックグラウンド生成待ち
                if (session.CopyTextGenerationTask != null && !session.CopyTextGenerationTask.IsCompleted)
                {
                    System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                    await session.CopyTextGenerationTask;
                    System.Windows.Input.Mouse.OverrideCursor = null;
                }

                string? text = session.CachedCopyText;
                if (string.IsNullOrWhiteSpace(text)) return;

                Clipboard.SetText(text);
                ShowNotification(view, LocalizationManager.Instance.GetText(LanguageKey.CopyNotification));
            }
            catch (Exception ex)
            {
                System.Windows.Input.Mouse.OverrideCursor = null;
                Logger.Log(AppConstants.LogClipboardError, ex);
                MessageBox.Show(
                    $"{LocalizationManager.Instance.GetText(LanguageKey.ClipboardError)}{ex.Message}",
                    "Error");
            }
        }

        private async void ShowNotification(IMainLayoutView view, string message)
        {
            if (view == null) return;
            view.NotificationTextBlock.Text = message;
            await Task.Delay(3000);
            if (view.NotificationTextBlock.Text == message)
                view.NotificationTextBlock.Text = "";
        }

        private void EnsureViewCreated(SessionData session)
        {
            if (session.CurrentView == null)
            {
                UserControl view = _currentLayoutMode == AppConstants.LayoutType.Horizontal ?
                    (UserControl)new HorizontalLayoutView(this) :
                    (UserControl)new VerticalLayoutView(this);
                InitializeView((IMainLayoutView)view, session);
                session.CurrentView = view;

                // Ensure content is rendered immediately
                _ = RenderResult(session);
            }
        }

        public void ApplyLayout(AppConstants.LayoutType layoutMode)
        {
            _currentLayoutMode = layoutMode;

            // Lazy Update: Only update sessions that already have a view, or the current one.
            // This prevents instantiating 100 views on startup.
            foreach (var session in Sessions)
            {
                // If view exists (already loaded) or it is the likely active one (we might not know yet on startup, 
                // but SelectionChanged will catch it later if we miss it here).
                // Actually, just updating existing ones is safer. 
                // Any null views will be created by EnsureViewCreated when selected.
                if (session.CurrentView != null)
                {
                    UserControl view = layoutMode == AppConstants.LayoutType.Horizontal ?
                        (UserControl)new HorizontalLayoutView(this) :
                        (UserControl)new VerticalLayoutView(this);

                    InitializeView((IMainLayoutView)view, session);
                    session.CurrentView = view;
                }
            }

            // Force TabControl to refresh content? 
            // Since CurrentView property changed, if it raises notification... 
            // CurrentView is not observable property of Session. Session should likely support notification or we refresh bindings.
            // Since we replaced the CLR object in simple class, binding might not update.
            // We should refresh Sessions collection or make CurrentView NotifyPropertyChanged.
            // For now, let's reset ItemsSource or notify.

            // To be safe, we can just replace the collection content or trigger refresh
            // But doing it for all might flicker.

            // Let's assume user only changes layout infrequently.
            SessionTabControl.Items.Refresh();

            MenuLayoutVertical.IsChecked = layoutMode == AppConstants.LayoutType.Vertical;
            MenuLayoutHorizontal.IsChecked = layoutMode == AppConstants.LayoutType.Horizontal;

            ApplyLocalization();
        }

        private void ApplyLocalization()
        {
#if DEBUG
            Logger.Log(AppConstants.LogApplyLocStart);
#endif
            var lm = LocalizationManager.Instance;
            this.Title = lm.GetText(LanguageKey.Title);
            MenuFile.Header = lm.GetText(LanguageKey.MenuFile);
            MenuOpenConfig.Header = lm.GetText(LanguageKey.MenuOpenConfig);
            MenuOpenLogSub.Header = lm.GetText(LanguageKey.MenuOpenLogSub);
            MenuExit.Header = lm.GetText(LanguageKey.MenuExit);
            MenuRestartAdmin.Header = lm.GetText(LanguageKey.MenuRestartAdmin);
            MenuRestartAdmin.ToolTip = lm.GetText(LanguageKey.MenuRestartAdminToolTip);
            MenuHelp.Header = lm.GetText(LanguageKey.MenuHelp);
            MenuOpenReadme.Header = lm.GetText(LanguageKey.MenuOpenReadme);
            MenuAbout.Header = lm.GetText(LanguageKey.MenuAbout);
            MenuLicense.Header = lm.GetText(LanguageKey.MenuLicense);
            MenuAppLicense.Header = lm.GetText(LanguageKey.MenuAppLicense);
            MenuThirdPartyLicenses.Header = lm.GetText(LanguageKey.MenuThirdPartyLicenses);
            MenuView.Header = lm.GetText(LanguageKey.MenuView);
            MenuLayout.Header = lm.GetText(LanguageKey.MenuLayout);
            MenuLayoutVertical.Header = lm.GetText(LanguageKey.MenuLayoutVertical);
            MenuLayoutHorizontal.Header = lm.GetText(LanguageKey.MenuLayoutHorizontal);

            browseButton.Content = lm.GetText(LanguageKey.BrowseButton);
            scanButton.ToolTip = lm.GetText(LanguageKey.ScanButtonToolTip);
            cancelButton.ToolTip = lm.GetText(LanguageKey.CancelButtonToolTip);
            configButton.ToolTip = lm.GetText(LanguageKey.OpenConfigToolTip);
            viewHeaderLabel.Text = lm.GetText(LanguageKey.FolderLabel);

            foreach (var session in Sessions)
            {
                (session.CurrentView as IMainLayoutView)?.ApplyLocalization(lm);
                _ = RenderResult(session);
            }
            Logger.Log(AppConstants.LogApplyLocSuccess);
        }

        // Renamed/signature changed to accept Session
        private async Task RenderResult(SessionData session)
        {
            var view = session.CurrentView as IMainLayoutView;

            if (view == null || session?.Result == null) return;
            if (view.OutputListBox == null) return;

            // Update Progress Bar
            // Only update status if NOT scanning (to avoid overwriting scan progress)
            if (!session.IsScanning)
            {
                view.ScanProgressBar.Visibility = Visibility.Visible;
                view.ScanProgressBar.IsIndeterminate = true;
                view.StatusTextBlock.Text = LocalizationManager.Instance.GetText(LanguageKey.RenderingStatus);
            }

            // UI設定読み込み
            long sizeThreshold;
            if (!long.TryParse(view.MinSizeTextBox.Text, out sizeThreshold) || sizeThreshold < 0) sizeThreshold = 0;
            AppConstants.SizeUnit unit = (AppConstants.SizeUnit)view.UnitComboBox.SelectedIndex;
            sizeThreshold = (long)(sizeThreshold * AppConstants.GetBytesPerUnit(unit));

            // Filtering
            string filterText = view.FilterTextBox?.Text ?? "";
            bool isRegex = view.FilterModeComboBox?.SelectedIndex == 1; // 0:Normal, 1:Regex
            var filter = new TreeFilter(filterText, isRegex);

            bool includeFiles = view.IncludeFilesCheckBox.IsChecked == true;
            int tabWidth = 4;
            int.TryParse(view.TabWidthTextBox.Text, out tabWidth);
            if (tabWidth < 1) tabWidth = 1;
            var sortTarget = session.SortTarget;
            var sortDirection = session.SortDirection;
            bool useSpaces = view.SeparatorComboBox.SelectedIndex == (int)AppConstants.Separator.Space;

            // Run on background thread
            await Task.Run(() =>
            {
                try
                {
                    // Filter Cache構築
                    var filterCache = new ConcurrentDictionary<FolderInfo, List<FolderInfo>>();

                    _formatter.BuildFilterCache(
                        session.Result,
                        filterCache,
                        sizeThreshold,
                        includeFiles,
                        sortTarget,
                        sortDirection,
                        CancellationToken.None,
                        filter);

                    // Clipboard Text Generation (Async)
                    session.CopyCts?.Cancel();
                    session.CopyCts = new CancellationTokenSource();
                    var copyToken = session.CopyCts.Token;

                    // Calculate max length for alignment if needed
                    int targetColumn = 0;
                    if (useSpaces)
                    {
                        targetColumn = _formatter.CalculateMaxLineLength(
                            session.Result,
                            filterCache,
                            0,
                            true,
                            true,
                            sizeThreshold,
                            includeFiles);
                        targetColumn += 4;
                    }

                    session.CopyTextGenerationTask = Task.Run(() =>
                    {
                        if (copyToken.IsCancellationRequested) return;
                        var sb = new StringBuilder();
                        _formatter.PrintTreeRecursive(
                           sb,
                           session.Result,
                           filterCache,
                           "",
                           true, // isLast
                           true, // isRoot
                           targetColumn,
                           useSpaces,
                           tabWidth,
                           sizeThreshold,
                           unit,
                           includeFiles,
                           copyToken);

                        if (!copyToken.IsCancellationRequested)
                        {
                            session.CachedCopyText = sb.ToString();
                        }
                    }, copyToken);

                    // ListView Items Generation
                    var items = _formatter.GenerateListItemsRecursive(
                        session.Result,
                        filterCache,
                        "",
                        false,
                        true,
                        0,
                        false,
                        tabWidth,
                        sizeThreshold,
                        unit,
                        includeFiles
                    ).ToList();

                    // UI Update
                    Dispatcher.Invoke(() =>
                    {
                        // Preserve selection and focus state
                        var previousNode = (view.OutputListBox.SelectedItem as FolderRowItem)?.Node;
                        bool hadFocus = view.OutputListBox.IsKeyboardFocusWithin;

                        if (view.OutputListBox is ListView lv)
                        {
                            lv.ItemsSource = items;
                        }
                        else
                        {
                            view.OutputListBox.ItemsSource = items;
                        }

                        // Restore selection
                        if (previousNode != null)
                        {
                            var newItem = items.FirstOrDefault(x => x.Node == previousNode);
                            if (newItem != null)
                            {
                                view.OutputListBox.SelectedItem = newItem;
                                view.OutputListBox.ScrollIntoView(newItem);

                                if (hadFocus)
                                {
                                    view.OutputListBox.UpdateLayout();
                                    if (view.OutputListBox.ItemContainerGenerator.ContainerFromItem(newItem) is ListBoxItem container)
                                    {
                                        container.Focus();
                                    }
                                }
                            }
                        }

                        if (!session.IsScanning)
                        {
                            view.ScanProgressBar.Visibility = Visibility.Collapsed;
                            view.ScanProgressBar.IsIndeterminate = false;

                            // Restore status if not scanning (e.g. after filter change)
                            var lm = LocalizationManager.Instance;
                            if (session.LastScanDuration != TimeSpan.Zero || session.TotalFilesScanned > 0)
                            {
                                string countText = session.IsCounting ? "" : $" {lm.GetText(LanguageKey.FolderCountStatus)}: {session.Result.CountFolderRecursive():N0}";
                                view.StatusTextBlock.Text = $"{lm.GetText(LanguageKey.FinishedStatus)} {string.Format(lm.GetText(LanguageKey.ProcessingTime), _formatter.FormatDuration(session.LastScanDuration))} ({session.TotalFilesScanned:N0} files){countText}";
                            }
                            else
                            {
                                view.StatusTextBlock.Text = lm.GetText(LanguageKey.ReadyStatus);
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var lm = LocalizationManager.Instance;
                        view.StatusTextBlock.Text = $"{lm.GetText(LanguageKey.LabelError)}{ex.Message}";
                        Logger.Log($"Render error: {ex}");
                        view.ScanProgressBar.Visibility = Visibility.Collapsed;
                    });
                }
            });
        }



        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            SaveCache();
        }

        private void InitializeLocalization()
        {
            /* Handled in CTOR and LoadCache */
        }

        private void LoadCache()
        {
            Logger.Log("LoadCache: Start");
            try
            {
                var settings = AppSettings.Load();
                Logger.Log($"LoadCache: Settings loaded (Result: {(settings != null ? "Success" : "Null")})");

                Sessions.Clear();

                if (settings != null)
                {
                    // Restore Window Geometry and State
                    if (!double.IsNaN(settings.WindowTop)) this.Top = settings.WindowTop;
                    SelectedIndex = settings.SelectedIndex;
                }

                // Load All Sessions from Disk
                var files = SessionFileManager.GetAllSessionFileNames();
                Logger.Log($"LoadCache: Found {files.Length} session files.");
                Array.Sort(files);

                foreach (var file in files)
                {
                    Logger.Log($"LoadCache: Adding placeholder for {file}");
                    Sessions.Add(new SessionData
                    {
                        Path = "Loading...",
                        IsLoading = true,
                        FileName = file
                    });
                }

                if (Sessions.Count == 0)
                {
                    Logger.Log("LoadCache: No sessions found, adding default tab.");
                    AddNewTab();
                }

                if (SelectedIndex < 0 || SelectedIndex >= Sessions.Count)
                    SelectedIndex = Math.Min(Math.Max(0, Sessions.Count - 1), 0);
                if (Sessions.Count > 0 && SelectedIndex < 0) SelectedIndex = 0;

                // Ensure the view for the selected session is created immediately
                if (SelectedIndex >= 0 && SelectedIndex < Sessions.Count)
                {
                    EnsureViewCreated(Sessions[SelectedIndex]);
                }

                InitializeLocalization();

                // 3. Start Async Loading
                var loadTargets = new List<SessionData>(Sessions);
                int activeIdx = SelectedIndex;
                Logger.Log($"LoadCache: Starting async load for {loadTargets.Count} sessions. ActiveIdx: {activeIdx}");

                Task.Run(() =>
                {
                    void LoadSingle(SessionData target)
                    {
                        if (string.IsNullOrEmpty(target.FileName))
                        {
                            Logger.Log("LoadCache: LoadSingle skipped (empty filename)");
                            return;
                        }

                        Logger.Log($"LoadCache: Loading file {target.FileName}");
                        try
                        {
                            var loaded = SessionFileManager.Load(target.FileName!);
                            Dispatcher.Invoke(() =>
                            {
                                if (loaded != null)
                                {
                                    Logger.Log($"LoadCache: Loaded {target.FileName} successfully. Path: {loaded.Path}");
                                    target.CopyFrom(loaded);
                                    target.IsLoading = false;
                                    // Ensure FileName is correct
                                    target.FileName = target.FileName;

                                    SyncSessionToView(target);
                                    if (target.CurrentView != null)
                                    {
                                        _ = RenderResult(target);
                                    }
                                }
                                else
                                {
                                    Logger.Log($"LoadCache: Failed to load content for {target.FileName} (Load returned null)");
                                    target.IsLoading = false;
                                    target.Path = "Load Failed";
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"LoadCache: Exception in LoadSingle for {target.FileName}", ex);
                        }
                    }

                    // Load Active First
                    if (activeIdx >= 0 && activeIdx < loadTargets.Count)
                    {
                        LoadSingle(loadTargets[activeIdx]);
                    }

                    // Load Others
                    for (int i = 0; i < loadTargets.Count; i++)
                    {
                        if (i == activeIdx) continue;
                        LoadSingle(loadTargets[i]);
                    }
                });
            }
            catch (Exception ex)
            {
                string message = "Error during initialization: ";
                Logger.Log(message, ex);
                MessageBox.Show($"{message} {ex.Message}");
                if (Sessions.Count == 0) AddNewTab();
            }
        }

        private void SaveCache()
        {
            try
            {
                // Sync UI to Session for all sessions with active views
                // アクティブセッションは必ず同期する（ビューの有無に関わらず、pathTextBox等のグローバルUIから取得）
                var active = CurrentSession;

                // アクティブセッションのパスを先に更新（ビューがなくても保存できるように）
                if (active != null)
                {
                    active.Path = pathTextBox.Text;
                }

                foreach (var session in Sessions)
                {
                    // アクティブセッション以外で、ビューが存在する場合のみ同期
                    if (session != active && session.CurrentView != null)
                    {
                        SyncViewToSession(session, false);
                    }
                    // アクティブセッションはビューがあればUI全体を同期
                    else if (session == active && session.CurrentView != null)
                    {
                        SyncViewToSession(session, true);
                    }
                    // ビューが存在しないセッションは既にセッションオブジェクトにデータが格納されている
                }

                var settings = new AppSettings();
                settings.Language = LocalizationManager.Instance.CurrentLanguage;
                settings.LayoutMode = MenuLayoutHorizontal.IsChecked == true ? AppConstants.LayoutType.Horizontal : AppConstants.LayoutType.Vertical;

                settings.WindowTop = this.Top;
                settings.WindowLeft = this.Left;
                settings.WindowWidth = this.Width;
                settings.WindowHeight = this.Height;

                if (this.WindowState == WindowState.Maximized) settings.WindowState = 2;
                else if (this.WindowState == WindowState.Minimized) settings.WindowState = 1;
                else settings.WindowState = 0;

                settings.SelectedIndex = SelectedIndex;

                // Sync FontSize from View
                var view = CurrentLayoutView;
                if (view != null && double.TryParse(view.FontSizeTextBox.Text, out double fs))
                {
                    settings.FontSize = fs;
                }
                else
                {
                    // Fallback to existing or default
                    var old = AppSettings.Load();
                    settings.FontSize = old?.FontSize ?? 14.0;
                }

                var filenames = new List<string>();
                var sessionInfos = new List<SessionInfo>();

                // Save each Session
                foreach (var session in Sessions)
                {
                    string? filename;
                    if (session.IsLoading && !string.IsNullOrEmpty(session.FileName))
                    {
                        // Keep original file if not loaded yet
                        filename = session.FileName;
                    }
                    else
                    {
                        // Save and get new/current filename
                        filename = SessionFileManager.Save(session);
                        if (filename != null) session.FileName = filename; // Update property
                    }

                    if (!string.IsNullOrEmpty(filename))
                    {
                        filenames.Add(filename!);
                        sessionInfos.Add(new SessionInfo { FileName = filename!, Path = session.Path });
                    }
                }
                settings.SessionFileNames = filenames.ToArray();
                settings.SessionInfos = sessionInfos;

                settings.Save();

                // Clean up old sessions asynchronously
                Task.Run(() => SessionFileManager.DeleteOldSessions(30));
            }
            catch (Exception ex)
            {
                Logger.Log(AppConstants.LogCacheSaveError, ex);
            }
        }



        private void OptimizeMemory()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    Win32.SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
                }
            }
            catch { }
        }

        private void InitializeMemoryTimer()
        {
            _memoryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(1)
            };
            _memoryTimer.Tick += (s, e) => OptimizeMemory();
            _memoryTimer.Start();
        }

        private void UpdateLanguageMenu()
        {
            // Simple implementation or reference existing
            // ...
            // Rebuild Language Menu from Languages folder
            MenuLanguage.Items.Clear();
            var languages = LocalizationManager.Instance.GetAvailableLanguages();
            string currentLang = LocalizationManager.Instance.CurrentLanguage;

            foreach (var langConfig in languages)
            {
                var lang = langConfig.Code;
                var item = new MenuItem
                {
                    Header = langConfig.MenuText,
                    IsCheckable = true,
                    IsChecked = lang == currentLang
                };
                item.Click += (s, e) => ChangeLanguage(lang);
                MenuLanguage.Items.Add(item);
            }
        }

        private void ChangeLanguage(string lang)
        {
            LocalizationManager.Instance.CurrentLanguage = lang;
            ApplyLocalization();

            var settings = AppSettings.Load() ?? new AppSettings();
            settings.Language = lang;
            settings.Save();

            UpdateLanguageMenu();
        }

        public void SortBy(AppConstants.SortTarget target)
        {
            var session = CurrentSession;
            if (session == null || session.CurrentView == null) return;

            // Toggle direction if same target, otherwise default to Ascending (or Descending for Size/Date?)
            // Usually Size/Date -> Descending, Name/Type -> Ascending
            if (session.SortTarget == target)
            {
                session.SortDirection = session.SortDirection == AppConstants.SortDirection.Ascending
                    ? AppConstants.SortDirection.Descending
                    : AppConstants.SortDirection.Ascending;
            }
            else
            {
                if (target == AppConstants.SortTarget.Size || target == AppConstants.SortTarget.Date)
                    session.SortDirection = AppConstants.SortDirection.Descending;
                else
                    session.SortDirection = AppConstants.SortDirection.Ascending;

                session.SortTarget = target;
            }

            // Sync Visuals
            (session.CurrentView as IMainLayoutView)?.UpdateSortVisuals(session.SortTarget, session.SortDirection);

            // Re-render
            _ = RenderResult(session);
        }

        // Show Owner Logic
        public void ShowOwner(FolderInfo node)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    string path = node.GetFullPath();
                    string owner = "";
                    try
                    {
                        if (node.IsFile)
                        {
                            owner = File.GetAccessControl(path).GetOwner(typeof(System.Security.Principal.NTAccount)).ToString();
                        }
                        else
                        {
                            owner = new DirectoryInfo(path).GetAccessControl().GetOwner(typeof(System.Security.Principal.NTAccount)).ToString();
                        }
                    }
                    catch
                    {
                        owner = "(Unknown)";
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        node.Owner = owner;
                        // Update UI if the node is currently visible in the active view
                        var view = CurrentLayoutView;
                        if (view != null)
                        {
                            var rowItem = view.OutputListBox.Items.OfType<FolderRowItem>().FirstOrDefault(i => i.Node == node);
                            if (rowItem != null)
                            {
                                rowItem.Owner = owner;
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error processing owner for {node.Name}: {ex.Message}");
                }
            });
        }

        private void ShowOwner_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var view = CurrentLayoutView;
            if (view == null) return;

            var selectedItems = view.OutputListBox.SelectedItems.OfType<FolderRowItem>().ToList();
            if (!selectedItems.Any() && e.Parameter is FolderRowItem singleItem)
            {
                selectedItems.Add(singleItem);
            }

            foreach (var item in selectedItems)
            {
                ShowOwner(item.Node);
            }
        }

        public void OpenItem(FolderInfo node)
        {
            try
            {
                string path = node.GetFullPath();
                if (System.IO.Directory.Exists(path) || System.IO.File.Exists(path))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to open item: " + node.Name, ex);
                // System.Windows.MessageBox.Show("Failed to open: " + ex.Message); // Optional
            }
        }

        // AddTab Logic
        private void AddTabButton_Click(object sender, RoutedEventArgs e)
        {
            AddNewTab();
        }

        private void AddNewTab()
        {
            if (Sessions.Count >= 10) return;

            var newSession = new SessionData() { Path = "c:\\" };
            Sessions.Add(newSession);
            SelectedIndex = Sessions.Count - 1;

            UserControl view = MenuLayoutHorizontal.IsChecked
                ? (UserControl)new HorizontalLayoutView(this)
                : (UserControl)new VerticalLayoutView(this);
            InitializeView((IMainLayoutView)view, newSession);
            newSession.CurrentView = view;

            // Set Ready Status
            if (view is IMainLayoutView mainView)
            {
                mainView.StatusTextBlock.Text = LocalizationManager.Instance.GetText(LanguageKey.ReadyStatus);
            }

            UpdateHeaderFromSession();
            UpdateAddButtonState();
        }

        private void UpdateAddButtonState()
        {
            var btn = SessionTabControl.Template?.FindName("AddTabButton", SessionTabControl) as Button;
            if (btn != null)
                btn.IsEnabled = Sessions.Count < 10;
        }

        private void TabCloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SessionData session)
            {
                // Cancel partial scan if any?
                session.Cts?.Cancel();

                // Delete cache file
                if (!string.IsNullOrEmpty(session.FileName))
                {
                    SessionFileManager.Delete(session.FileName!);
                }
                else
                {
                    SessionFileManager.Delete(session.GenerateFileName());
                }

                int idx = Sessions.IndexOf(session);
                Sessions.Remove(session);

                if (Sessions.Count == 0)
                {
                    AddNewTab(); // Add default
                }
                else if (idx == SelectedIndex) // If we closed active tab
                {
                    SelectedIndex = Math.Min(idx, Sessions.Count - 1);
                }
                UpdateAddButtonState();
            }
        }

        // Input Logic (Path TextBox)
        private void Input_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                ScanButton_Click(sender, e);
            }
        }

        // --- Existing Menu Event Handlers ---
        private void MenuLayoutVertical_Click(object sender, RoutedEventArgs e)
        {
            ApplyLayout(AppConstants.LayoutType.Vertical);
        }

        private void MenuLayoutHorizontal_Click(object sender, RoutedEventArgs e)
        {
            ApplyLayout(AppConstants.LayoutType.Horizontal);
        }

        private void Config_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConstants.ConfigFileName);
                if (!File.Exists(configPath)) Config.Load(); // なかったら生成 (Load目的ではなく)

                Process.Start(new ProcessStartInfo(configPath) { UseShellExecute = true });
                Logger.Log("Opened Advanced Settings (Config.txt)");
            }
            catch (Exception ex)
            {
                var lm = LocalizationManager.Instance;
                Logger.Log($"{lm.GetText(LanguageKey.ConfigError)}", ex);
                MessageBox.Show($"{lm.GetText(LanguageKey.ConfigError)}{ex.Message}", lm.GetText(LanguageKey.LabelError), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuReadme_Click(object sender, RoutedEventArgs e)
        {
            var lm = LocalizationManager.Instance;
            try
            {
                string lang = LocalizationManager.Instance.CurrentLanguage;
                // Fallback to "en" if empty
                if (string.IsNullOrEmpty(lang)) lang = "en";

                // Assuming format like "Readme_ja.txt"
                string fileName = AppConstants.GetReadmeFileName(lang);
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConstants.ReadmeDirectoryName, fileName);

                if (!File.Exists(path))
                {
                    path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConstants.ReadmeDirectoryName, "Readme_en.txt");
                }

                if (File.Exists(path))
                {
                    OpenOrActivateTextViewer(path);
                }
                else
                {
                    throw new Exception($"Readme file not found. {path}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"{lm.GetText(LanguageKey.ReadmeError)}", ex);
                MessageBox.Show($"{lm.GetText(LanguageKey.ReadmeError)}{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuRestartAdmin_Click(object sender, RoutedEventArgs e)
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (exe != null)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(exe) { Verb = "runas", UseShellExecute = true });
                    Application.Current.Shutdown();
                }
                catch (Exception ex)
                {
                    Logger.Log("Failed to restart as admin", ex);
                }
            }
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            SaveCache();
            Application.Current.Shutdown();
        }

        private void MenuAppLicense_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConstants.LicenseDirectoryName, AppConstants.AppLicenseFileName);
                if (File.Exists(path))
                {
                    OpenOrActivateTextViewer(path);
                }
                else
                {
                    throw new Exception($"License file not found. {path}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to show app license", ex);
                MessageBox.Show($"Failed to show app license\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuThirdPartyLicenses_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConstants.LicenseDirectoryName, AppConstants.ThirdPartyNoticesFileName);
                if (File.Exists(path))
                {
                    OpenOrActivateTextViewer(path);
                }
                else
                {
                    MessageBox.Show("Third-party notices file not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to show third party licenses", ex);
            }
        }

        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            var lm = LocalizationManager.Instance;
            var format = lm.GetText(LanguageKey.AboutMessage);
            var message = string.Format(format, AppInfo.Title, AppInfo.Copyright, AppInfo.Version);

            MessageBox.Show(message, lm.GetText(LanguageKey.AboutTitle), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void MenuView_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            try
            {
                // Rebuild Log Submenu
                MenuOpenLogSub.Items.Clear();
                string logDir = AppConstants.LogsDirectoryPath;

                if (Directory.Exists(logDir))
                {
                    var files = Directory.GetFiles(logDir, $"*.{AppConstants.LogsExtension}")
                                         .OrderByDescending(f => File.GetCreationTime(f))
                                         .Take(10); // Show recent 10 logs

                    foreach (var file in files)
                    {
                        var item = new MenuItem { Header = Path.GetFileName(file) };
                        item.Click += (s, args) =>
                        {
                            OpenOrActivateTextViewer(file);
                        };
                        MenuOpenLogSub.Items.Add(item);
                    }
                }

                if (MenuOpenLogSub.Items.Count == 0)
                {
                    var item = new MenuItem { Header = "(No logs)", IsEnabled = false };
                    MenuOpenLogSub.Items.Add(item);
                }

                MenuOpenLogSub.Items.Add(new Separator());

                var openFolderItem = new MenuItem { Header = "Open Log Folder..." };
                openFolderItem.Click += (s, args) =>
                {
                    try
                    {
                        if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                        Process.Start("explorer.exe", logDir);
                    }
                    catch { }
                };
                MenuOpenLogSub.Items.Add(openFolderItem);
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to update log menu", ex);
            }
        }

        public void SetFolderExpansion(IEnumerable<FolderRowItem> items, bool isExpanded)
        {
            var session = CurrentSession;
            if (session == null) return;

            bool changed = false;
            foreach (var item in items)
            {
                if (!item.IsFile && item.Node.IsExpanded != isExpanded)
                {
                    item.Node.IsExpanded = isExpanded;
                    changed = true;
                }
            }

            if (changed)
            {
                _ = RenderResult(session);
            }
        }

        public void ToggleFolderExpansion(IEnumerable<FolderRowItem> items)
        {
            var session = CurrentSession;
            if (session == null) return;

            bool changed = false;
            foreach (var item in items)
            {
                if (!item.IsFile)
                {
                    item.Node.IsExpanded = !item.Node.IsExpanded;
                    changed = true;
                }
            }

            if (changed)
            {
                _ = RenderResult(session);
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
        private void MenuOpenLogSub_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Directory.Exists(AppConstants.LogsDirectoryPath))
                {
                    Process.Start(new ProcessStartInfo(AppConstants.LogsDirectoryPath) { UseShellExecute = true });
                }
                else
                {
                    MessageBox.Show(LocalizationManager.Instance.GetText(LanguageKey.InitializationError) + " (Log dir not found)",
                        LocalizationManager.Instance.GetText(LanguageKey.LabelError), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to open logs directory.", ex);
                MessageBox.Show(ex.Message, LocalizationManager.Instance.GetText(LanguageKey.LabelError), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }

    public class FolderRowItem : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }

        public FolderInfo Node { get; set; }
        public string DisplayText { get; set; }
        public string SizeText { get; set; }
        public bool IsExpanded => Node.IsExpanded;
        public bool IsFile => Node.IsFile;

        private FolderRowItem()
        {
            Node = null!;
            DisplayText = "";
            SizeText = "";
            IndentedName = "";
            DisplayType = "";
            DisplaySize = "";
            Owner = "";
        }

        public string IndentedName { get; set; }
        public DateTime DisplayDate { get; set; }
        public string DisplayType { get; set; }
        public string DisplaySize { get; set; }

        private string _owner = "";
        public string Owner
        {
            get => _owner;
            set
            {
                if (_owner != value)
                {
                    _owner = value;
                    OnPropertyChanged();
                }
            }
        }

        public FolderRowItem(FolderInfo node, string displayText, string sizeText, string indentedName, string displayType)
        {
            Node = node;
            DisplayText = displayText;
            SizeText = sizeText;
            IndentedName = indentedName;
            DisplayDate = node.LastModified;
            DisplayType = displayType;
            DisplaySize = sizeText.Trim();
            _owner = node.Owner ?? "";
        }

        public override string ToString()
        {
            return DisplayText;
        }
        private void MenuOpenLogSub_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Directory.Exists(AppConstants.LogsDirectoryPath))
                {
                    Process.Start(new ProcessStartInfo(AppConstants.LogsDirectoryPath) { UseShellExecute = true });
                }
                else
                {
                    MessageBox.Show(LocalizationManager.Instance.GetText(LanguageKey.InitializationError) + " (Log dir not found)",
                        LocalizationManager.Instance.GetText(LanguageKey.LabelError), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to open logs directory.", ex);
                MessageBox.Show(ex.Message, LocalizationManager.Instance.GetText(LanguageKey.LabelError), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
