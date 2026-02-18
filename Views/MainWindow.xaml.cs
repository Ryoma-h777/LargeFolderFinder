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
using LargeFolderFinder.ViewModels;

namespace LargeFolderFinder
{
    /// <summary>
    /// メインクラス
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private MainViewModel _viewModel = new MainViewModel();
        private DispatcherTimer? _memoryTimer;
        private DispatcherTimer? _filterDebounceTimer;
        private readonly ResultFormatter _formatter = new ResultFormatter();
        private AppConstants.LayoutType _currentLayoutMode = AppConstants.LayoutType.Vertical;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public ObservableCollection<SessionData> Sessions => _viewModel.Sessions;

        // Delegate to ViewModel
        public int SelectedIndex
        {
            get => _viewModel.SelectedIndex;
            set => _viewModel.SelectedIndex = value;
        }

        public SessionData? CurrentSession => _viewModel.CurrentSession;

        public IMainLayoutView? CurrentLayoutView => CurrentSession?.CurrentView as IMainLayoutView;

        public MainWindow()
        {
            try
            {
                Logger.Log(AppConstants.LogAppStarted);
                InitializeComponent();

                this.DataContext = _viewModel;

                _viewModel.Initialize();

                // Register Commands
                this.CommandBindings.Add(new CommandBinding(LocalCommands.ShowOwner, ShowOwner_Executed));

                // InitializeLocalization(); // Handled by VM or ApplyLogic
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

                // VM Sessions collection change?
                // We rely on VM to manage sessions.

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

        /// <summary>
        /// セッション用のビュー（ユーザーコントロール）を初期化し、イベントハンドラを設定します。
        /// </summary>
        private void InitializeView(IMainLayoutView view, SessionData session)
        {
            try
            {
                if (view == null) return;

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
            catch (Exception ex)
            {
                Logger.Log("Error in InitializeView", ex);
            }
        }

        internal void OnSettingChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SessionTabControl.SelectedItem is SessionData session)
                {
                    // 重い処理なのでデバウンスする
                    _filterDebounceTimer?.Stop();
                    _filterDebounceTimer?.Start();
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Error in OnSettingChanged", ex);
            }
        }

        internal void OnFilterTextChanged(object sender, TextChangedEventArgs e) => OnSettingChanged(sender, e);
        internal void OnFilterModeChanged(object sender, SelectionChangedEventArgs e) => OnSettingChanged(sender, e);
        internal void OutputListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { /* Placeholder */ }

        private void SessionTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
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
            catch (Exception ex)
            {
                Logger.Log("Error in SessionTabControl_SelectionChanged", ex);
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
            try
            {
                var session = CurrentSession;
                if (session == null) return;

                if (session.ViewModel is SessionViewModel vm)
                {
                    await vm.ScanAsync(pathTextBox.Text);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Error in ScanButton_Click", ex);
                MessageBox.Show($"{LocalizationManager.Instance.GetText(LanguageKey.LabelError)}\n{ex.Message}");
            }
        }

        internal void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var session = CurrentSession;
                if (session != null && session.ViewModel is SessionViewModel vm)
                {
                    vm.CancelScan();
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Error in CancelButton_Click", ex);
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
                session.ViewModel = new SessionViewModel(session, (IMainLayoutView)view);

                // Ensure content is rendered immediately
                _ = RenderResult(session);
            }
        }

        public void ApplyLayout(AppConstants.LayoutType layoutMode)
        {
            try
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

                        // 修正: ViewModelを再生成して新しいViewと紐付ける
                        session.ViewModel = new SessionViewModel(session, (IMainLayoutView)view);

                        InitializeView((IMainLayoutView)view, session);
                        session.CurrentView = view;
                    }
                }

                // Force TabControl to refresh content? 
                SessionTabControl.Items.Refresh();

                MenuLayoutVertical.IsChecked = layoutMode == AppConstants.LayoutType.Vertical;
                MenuLayoutHorizontal.IsChecked = layoutMode == AppConstants.LayoutType.Horizontal;

                ApplyLocalization();
            }
            catch (Exception ex)
            {
                Logger.Log("Error in ApplyLayout", ex);
            }
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
            if (session != null && session.ViewModel is SessionViewModel vm)
            {
                await vm.RenderResult();
            }
        }



        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            SaveCache();
        }


        /// <summary>
        /// キャッシュ及び設定をロードします
        /// </summary>
        private void LoadCache()
        {
            _viewModel.LoadCache();

            // Ensure view created for active
            if (SelectedIndex >= 0 && SelectedIndex < Sessions.Count)
            {
                // Dispatcher to wait for collection? 
                // VM LoadCache runs async for loading content, but populates placeholders synchronously (before async task).
                // So Sessions should have items.
                EnsureViewCreated(Sessions[SelectedIndex]);
            }
        }

        /// <summary>
        /// キャッシュ及び設定を保存します
        /// </summary>
        private void SaveCache()
        {
            // Sync current path first
            var active = CurrentSession;
            if (active != null) active.Path = pathTextBox.Text;

            int windowStateValue = 0;
            if (this.WindowState == WindowState.Maximized) windowStateValue = 2;
            else if (this.WindowState == WindowState.Minimized) windowStateValue = 1;

            // FontSize lookup
            double fontSize = 14.0;
            var view = CurrentLayoutView;
            if (view != null && double.TryParse(view.FontSizeTextBox.Text, out double fs))
            {
                fontSize = fs;
            }
            else
            {
                var old = AppSettings.Load();
                fontSize = old?.FontSize ?? 14.0;
            }

            _viewModel.SaveCache(
                this.Top,
                this.Left,
                this.Width,
                this.Height,
                windowStateValue,
                fontSize);
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
            try
            {
                LocalizationManager.Instance.CurrentLanguage = lang;
                ApplyLocalization();

                var settings = AppSettings.Load() ?? new AppSettings();
                settings.Language = lang;
                settings.Save();

                UpdateLanguageMenu();
            }
            catch (Exception ex)
            {
                Logger.Log("Error in ChangeLanguage", ex);
            }
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
            try
            {
                AddNewTab();
            }
            catch (Exception ex)
            {
                Logger.Log("Error in AddTabButton_Click", ex);
            }
        }

        private void AddNewTab()
        {
            try
            {
                _viewModel.AddNewTab();
                // Ensure View created for new tab (which is now selected)
                if (CurrentSession != null)
                {
                    EnsureViewCreated(CurrentSession);
                    UpdateHeaderFromSession();
                }
                UpdateAddButtonState();
            }
            catch (Exception ex)
            {
                Logger.Log("Error in AddNewTab", ex);
            }
        }

        private void UpdateAddButtonState()
        {
            var btn = SessionTabControl.Template?.FindName("AddTabButton", SessionTabControl) as Button;
            if (btn != null)
                btn.IsEnabled = Sessions.Count < 10;
        }

        private void TabCloseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.Tag is SessionData session)
                {
                    _viewModel.CloseTab(session);
                    // Ensure View created for new selection if any?
                    // SelectionChanged will handle it.
                    UpdateAddButtonState();
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Error in TabCloseButton_Click", ex);
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
            try
            {
                ApplyLayout(AppConstants.LayoutType.Vertical);
            }
            catch (Exception ex)
            {
                Logger.Log("Error in MenuLayoutVertical_Click", ex);
            }
        }

        private void MenuLayoutHorizontal_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ApplyLayout(AppConstants.LayoutType.Horizontal);
            }
            catch (Exception ex)
            {
                Logger.Log("Error in MenuLayoutHorizontal_Click", ex);
            }
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

    }
}
