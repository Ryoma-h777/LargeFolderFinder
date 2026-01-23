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
using MessagePack;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LargeFolderFinder
{
    /// <summary>
    /// メインクラス
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private CancellationTokenSource? _renderCts;
        private DispatcherTimer? _memoryTimer;
        private bool _hasShownProgressError = false;

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

                InitializeLocalization();
                LoadCache();
                UpdateLanguageMenu();

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
                    $"{LocalizationManager.Instance.GetText(LanguageKey.DetailLabel)}\n{ex.StackTrace}",
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

            view.SortComboBox.Items.Clear();
            view.SortComboBox.Items.Add(LocalizationManager.Instance.GetText(LanguageKey.TargetSize));
            view.SortComboBox.Items.Add(LocalizationManager.Instance.GetText(LanguageKey.TargetName));
            view.SortComboBox.Items.Add(LocalizationManager.Instance.GetText(LanguageKey.TargetDate));

            view.SortDirectionComboBox.Items.Clear();
            view.SortDirectionComboBox.Items.Add(LocalizationManager.Instance.GetText(LanguageKey.DirectionAsc));
            view.SortDirectionComboBox.Items.Add(LocalizationManager.Instance.GetText(LanguageKey.DirectionDesc));

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

            view.SearchSizeTextBox.TextChanged += (s, e) => { _ = RenderResult(session); };
            view.UnitComboBox.SelectionChanged += (s, e) => { _ = RenderResult(session); };
            view.SortComboBox.SelectionChanged += (s, e) => { _ = RenderResult(session); };
            view.SortDirectionComboBox.SelectionChanged += (s, e) => { _ = RenderResult(session); };
            view.IncludeFilesCheckBox.Click += (s, e) => { _ = RenderResult(session); };
            view.TabWidthTextBox.TextChanged += (s, e) => { _ = RenderResult(session); };

            // Set Initial Values from Session
            view.SearchSizeTextBox.Text = session.Threshold.ToString();
            view.UnitComboBox.SelectedIndex = (int)session.Unit;
            view.SortComboBox.SelectedIndex = (int)session.SortTarget;
            view.SortDirectionComboBox.SelectedIndex = (int)session.SortDirection;
            view.IncludeFilesCheckBox.IsChecked = session.IncludeFiles;
            view.SeparatorComboBox.SelectedIndex = session.SeparatorIndex;
            view.TabWidthTextBox.Text = session.TabWidth.ToString();

            // Initial Visibility
            view.TabWidthArea.Visibility = session.SeparatorIndex == (int)AppConstants.Separator.Tab ? Visibility.Visible : Visibility.Collapsed;

            // Apply Localization
            view.ApplyLocalization(LocalizationManager.Instance);
        }

        private void SessionTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Only handle if the source is the TabControl itself (not inner controls)
            if (e.OriginalSource == SessionTabControl)
            {
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
                runButton.IsEnabled = !isScanning;
                cancelButton.IsEnabled = isScanning;
            }
            else
            {
                pathTextBox.Text = "";
                runButton.IsEnabled = true;
                cancelButton.IsEnabled = false;
            }
        }

        internal void BrowseButton_Click(object sender, RoutedEventArgs e)
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

        internal async void ScanButton_Click(object sender, RoutedEventArgs e)
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

            // Sync Path to Session
            session.Path = path;

            // Sync Settings from View to Session
            if (!double.TryParse(view.SearchSizeTextBox.Text, out double thresholdVal))
            {
                MessageBox.Show(
                    lm.GetText(LanguageKey.ThresholdInvalidError),
                    lm.GetText(LanguageKey.LabelError),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            AppConstants.SizeUnit selectedUnit = AppConstants.SizeUnit.GB;
            if (view.UnitComboBox.SelectedIndex >= 0)
            {
                selectedUnit = (AppConstants.SizeUnit)view.UnitComboBox.SelectedIndex;
            }
            long thresholdBytes = (long)(thresholdVal * AppConstants.GetBytesPerUnit(selectedUnit));

            // Store Settings Back to Session
            session.Threshold = thresholdVal;
            session.Unit = selectedUnit;
            session.SortTarget = (AppConstants.SortTarget)view.SortComboBox.SelectedIndex;
            session.SortDirection = (AppConstants.SortDirection)view.SortDirectionComboBox.SelectedIndex;
            session.IncludeFiles = view.IncludeFilesCheckBox.IsChecked == true;
            session.SeparatorIndex = view.SeparatorComboBox.SelectedIndex;
            if (int.TryParse(view.TabWidthTextBox.Text, out int tw)) session.TabWidth = tw;


            var config = Config.Load();
            SaveCache(); // 検索履歴の保存
            _hasShownProgressError = false;

            session.Cts = new CancellationTokenSource();
            session.IsScanning = true;
            UpdateHeaderFromSession(); // Update buttons

            view.OutputTextBox.Clear();
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

                            string elapsedStr = FormatDuration(sw.Elapsed);
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
                            string elapsedStr = FormatDuration(sw.Elapsed);
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
                            MessageBox.Show($"{lm.GetText(LanguageKey.ProgressErrorLabel)} {ex.Message}", "Debug");
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

                if (view != null)
                {
                    view.StatusTextBlock.Text = $"{lm.GetText(LanguageKey.FinishedStatus)} " +
                        $"[{string.Format(lm.GetText(LanguageKey.ProcessingTime), FormatDuration(sw.Elapsed))}]";
                }
                Logger.Log(string.Format(AppConstants.LogScanSuccess, FormatDuration(sw.Elapsed)));

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

        internal void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            var session = CurrentSession;
            if (session != null && session.IsScanning)
            {
                Logger.Log("Canceling scan...");
                session.Cts?.Cancel();
            }
        }

        internal void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            var view = CurrentLayoutView;
            if (view == null) return;
            try
            {
                string text = view.OutputTextBox.Text;
                if (string.IsNullOrWhiteSpace(text)) return;
                Clipboard.SetText(text);
                ShowNotification(view, LocalizationManager.Instance.GetText(LanguageKey.CopyNotification));
            }
            catch (Exception ex)
            {
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

        public void ApplyLayout(AppConstants.LayoutType layoutMode)
        {
            // Re-create View for ALL sessions
            foreach (var session in Sessions)
            {
                UserControl view = layoutMode == AppConstants.LayoutType.Horizontal ?
                    (UserControl)new HorizontalLayoutView(this) :
                    (UserControl)new VerticalLayoutView(this);

                // Initialize View with Session data
                InitializeView((IMainLayoutView)view, session);
                session.CurrentView = view;
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

            foreach (var session in Sessions)
            {
                (session.CurrentView as IMainLayoutView)?.ApplyLocalization(lm);
            }
            Logger.Log(AppConstants.LogApplyLocSuccess);
        }

        // Renamed/signature changed to accept Session
        private async Task RenderResult(SessionData session)
        {
            if (session == null || session.Result == null || session.CurrentView == null) return;
            var view = session.CurrentView as IMainLayoutView;
            if (view == null) return;

            try
            {
                // UI入力の取得（メインスレッド）
                // Use session values or View values? 
                // We should use View values as they are the source of truth for display settings currently.
                // Sync View -> Session already done in ScanButton, but for display settings, they might change without scan.
                // So let's Update Session from View before render, OR just use View values directly for render logic.
                // Using View values directly is safer for "What you see is what you get".

                if (!double.TryParse(view.SearchSizeTextBox.Text, out double thresholdVal)) return;

                AppConstants.SizeUnit unit = (AppConstants.SizeUnit)view.UnitComboBox.SelectedIndex;
                long thresholdBytes = (long)(thresholdVal * AppConstants.GetBytesPerUnit(unit));
                bool includeFiles = view.IncludeFilesCheckBox.IsChecked == true;
                var sortTarget = (AppConstants.SortTarget)view.SortComboBox.SelectedIndex;
                var sortDirection = (AppConstants.SortDirection)view.SortDirectionComboBox.SelectedIndex;
                bool useSpaces = view.SeparatorComboBox.SelectedIndex == (int)AppConstants.Separator.Space;

                int tabWidth = AppConstants.DefaultTabWidth;
                int.TryParse(view.TabWidthTextBox.Text, out tabWidth);

                // 前回の処理をキャンセル
                _renderCts?.Cancel();
                _renderCts = new CancellationTokenSource();
                var token = _renderCts.Token;

                // UIフェードバック開始
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                string originalStatus = view.StatusTextBlock.Text;
                view.StatusTextBlock.Text = LocalizationManager.Instance.GetText(LanguageKey.RenderingStatus);

                try
                {
                    // バックグラウンドでレイアウト計算と文字列生成を実行
                    string resultText = await Task.Run(() =>
                    {
                        if (token.IsCancellationRequested) return "";

                        // 1. フィルタリングとソートの結果をキャッシュ（一貫性確保と高速化）
                        var filterCache = new Dictionary<FolderInfo, List<FolderInfo>>();
                        BuildFilterCache(session.Result, filterCache, thresholdBytes, includeFiles, sortTarget, sortDirection, token);

                        if (token.IsCancellationRequested) return "";

                        // 2. 最大行長の計算（ソート順序非依存・キャッシュ利用）
                        int maxLineLen = CalculateMaxLineLength(
                            session.Result,
                            filterCache, // キャッシュを使用
                            0,
                            true,
                            true,
                            thresholdBytes,
                            includeFiles);

                        if (token.IsCancellationRequested) return "";

                        int targetColumn = ((maxLineLen / tabWidth) + 1) * tabWidth;
                        var sb = new StringBuilder();

                        // 3. 文字列の生成（ソート順序依存・キャッシュ利用）
                        PrintTreeRecursive(
                            sb,
                            session.Result,
                            filterCache, // キャッシュを使用
                            "",
                            true,
                            true,
                            targetColumn,
                            useSpaces,
                            tabWidth,
                            thresholdBytes,
                            unit,
                            includeFiles);

                        return sb.ToString();
                    }, token);

                    if (!token.IsCancellationRequested)
                    {
                        view.OutputTextBox.Text = resultText;
                    }
                }
                finally
                {
                    // UIフェードバック終了
                    System.Windows.Input.Mouse.OverrideCursor = null;

                    if (!token.IsCancellationRequested)
                    {
                        view.StatusTextBlock.Text = originalStatus;
                    }
                }
            }
            catch (OperationCanceledException) { /* Ignored */ }
            catch (Exception ex) { Logger.Log("Render error", ex); }
        }

        // ... Helper methods (CalculateMaxLineLength, PrintTreeRecursive, etc) remain mostly same ...
        // I will copy them to include in the full file.

        private int CalculateMaxLineLength(
            FolderInfo node,
            Dictionary<FolderInfo, List<FolderInfo>> filterCache,
            int indentLen,
            bool isRoot,
            bool isLast,
            long thresholdBytes,
            bool includeFiles)
        {
            if (node == null ||
                (!isRoot && node.Size < thresholdBytes) ||
                (!isRoot && node.IsFile && !includeFiles)) return 0;

            int prefixLen = isRoot ? 0 : GetStringWidth(isLast ? AppConstants.TreeLastBranch : AppConstants.TreeBranch);
            int currentLen = indentLen + prefixLen + GetStringWidth(node.Name);
            int max = currentLen;

            // インデント幅の統一: Lastの場合もスペース3つ分
            int childIndentLen = indentLen + (isRoot
                ? 0
                : GetStringWidth(isLast
                    ? AppConstants.TreeSpace + AppConstants.TreeSpace + AppConstants.TreeSpace
                    : AppConstants.TreeVertical + AppConstants.TreeSpace));

            if (node.Children != null)
            {
                List<FolderInfo>? list = null;
                if (filterCache.TryGetValue(node, out var cachedList))
                {
                    list = cachedList;
                }
                else
                {
                    lock (node.Children)
                    {
                        list = node.Children.Where(c => c.Size >= thresholdBytes && (includeFiles || !c.IsFile)).ToList();
                    }
                }

                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        int childMax = CalculateMaxLineLength(
                            list[i],
                            filterCache,
                            childIndentLen,
                            isRoot: false,
                            isLast: i == list.Count - 1,
                            thresholdBytes,
                            includeFiles);
                        if (childMax > max) max = childMax;
                    }
                }
            }
            return max;
        }

        private void PrintTreeRecursive(
            StringBuilder sb,
            FolderInfo node,
            Dictionary<FolderInfo, List<FolderInfo>> filterCache,
            string indent,
            bool isLast,
            bool isRoot,
            int targetColumn,
            bool useSpaces,
            int tabWidth,
            long thresholdBytes,
            AppConstants.SizeUnit unit,
            bool includeFiles)
        {
            if (node == null ||
                (!isRoot && node.Size < thresholdBytes) ||
                (!isRoot && node.IsFile && !includeFiles))
                return;

            if (isRoot && node.Size < thresholdBytes)
            {
                sb.Append(AppConstants.TreeLastBranch).AppendLine(LocalizationManager.Instance.GetText(LanguageKey.NotFoundMessage));
                return;
            }

            string sizeStr = $"{(double)node.Size / AppConstants.GetBytesPerUnit(unit):N0} {unit}".PadLeft(AppConstants.BaseSizeLength);
            string line = isRoot
                ? node.Name
                : indent + (isLast ? AppConstants.TreeLastBranch : AppConstants.TreeBranch) + node.Name;
            sb.Append(line);

            int curLen = GetStringWidth(line);
            while (curLen < targetColumn)
            {
                if (useSpaces)
                    sb.Append(' ');
                else
                    sb.Append('\t');
                curLen += (useSpaces ? 1 : tabWidth - (curLen % tabWidth));
            }

            sb.Append(sizeStr).AppendLine();

            if (node.Children != null)
            {
                List<FolderInfo>? list = null;
                if (filterCache.TryGetValue(node, out var cachedList))
                {
                    list = cachedList;
                }
                else
                {
                    lock (node.Children)
                    {
                        list = node.Children.Where(c => c.Size >= thresholdBytes && (includeFiles || !c.IsFile)).ToList();
                    }
                }

                if (list != null)
                {
                    string childIndent = indent + (isRoot
                        ? ""
                        : (isLast
                            ? AppConstants.TreeSpace + AppConstants.TreeSpace + AppConstants.TreeSpace
                            : AppConstants.TreeVertical + AppConstants.TreeSpace));

                    for (int i = 0; i < list.Count; i++)
                    {
                        PrintTreeRecursive(
                            sb,
                            node: list[i],
                            filterCache,
                            childIndent,
                            isLast: i == list.Count - 1,
                            isRoot: false,
                            targetColumn,
                            useSpaces,
                            tabWidth,
                            thresholdBytes,
                            unit,
                            includeFiles);
                    }
                }
            }
        }

        private void BuildFilterCache(
            FolderInfo node,
            Dictionary<FolderInfo, List<FolderInfo>> cache,
            long thresholdBytes,
            bool includeFiles,
            AppConstants.SortTarget sortTarget,
            AppConstants.SortDirection sortDirection,
            CancellationToken token)
        {
            if (token.IsCancellationRequested || node == null) return;

            if (node.Children != null)
            {
                List<FolderInfo> visible;
                lock (node.Children)
                {
                    var query = node.Children.Where(c => c.Size >= thresholdBytes && (includeFiles || !c.IsFile));

                    switch (sortTarget)
                    {
                        case AppConstants.SortTarget.Size:
                            query = sortDirection == AppConstants.SortDirection.Ascending
                                ? query.OrderBy(c => c.Size)
                                : query.OrderByDescending(c => c.Size);
                            break;
                        case AppConstants.SortTarget.Name:
                            query = sortDirection == AppConstants.SortDirection.Ascending
                                ? query.OrderBy(c => c.Name)
                                : query.OrderByDescending(c => c.Name);
                            break;
                        case AppConstants.SortTarget.Date:
                            query = sortDirection == AppConstants.SortDirection.Ascending
                                ? query.OrderBy(c => c.LastModified)
                                : query.OrderByDescending(c => c.LastModified);
                            break;
                    }
                    visible = query.ToList();
                }

                cache[node] = visible;

                foreach (var child in visible)
                {
                    BuildFilterCache(child, cache, thresholdBytes, includeFiles, sortTarget, sortDirection, token);
                }
            }
        }

        private int GetStringWidth(string str)
        {
            int width = 0;
            foreach (char c in str)
            {
                width += (c < 0x81 || (c >= 0xff61 && c < 0xffa0)) ? 1 : 2;
            }
            return width;
        }

        private string FormatDuration(TimeSpan ts)
        {
            var lm = LocalizationManager.Instance;
            return ts switch
            {
                TimeSpan t when t.TotalHours > 1 => $"{(int)ts.TotalHours}{lm.GetText(LanguageKey.UnitHour)}{ts.Minutes}{lm.GetText(LanguageKey.UnitMinute)}",
                TimeSpan t when t.TotalMinutes > 1 => $"{(int)ts.TotalMinutes}{lm.GetText(LanguageKey.UnitMinute)}{ts.Seconds}{lm.GetText(LanguageKey.UnitSecond)}",
                TimeSpan t when t.TotalSeconds > 1 => $"{(int)ts.TotalSeconds}{lm.GetText(LanguageKey.UnitSecond)}",
                _ => $"{ts.Milliseconds}{lm.GetText(LanguageKey.UnitMillisecond)}"
            };
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
            Logger.Log(AppConstants.LogCacheLoadStart);
            try
            {
                var settings = AppSettings.Load();

                Sessions.Clear();
                if (settings != null)
                {
                    // Restore Window Geometry and State
                    if (!double.IsNaN(settings.WindowTop)) this.Top = settings.WindowTop;
                    if (!double.IsNaN(settings.WindowLeft)) this.Left = settings.WindowLeft;
                    if (!double.IsNaN(settings.WindowWidth)) this.Width = settings.WindowWidth;
                    if (!double.IsNaN(settings.WindowHeight)) this.Height = settings.WindowHeight;

                    if (settings.WindowState == 1) this.WindowState = WindowState.Minimized;
                    else if (settings.WindowState == 2) this.WindowState = WindowState.Maximized;

                    // Load Sessions
                    if (settings.SessionFileNames != null)
                    {
                        foreach (var name in settings.SessionFileNames)
                        {
                            var s = SessionFileManager.Load(name);
                            if (s != null) Sessions.Add(s);
                        }
                    }
                    SelectedIndex = settings.SelectedIndex;

                    // Apply Settings
                    LocalizationManager.Instance.CurrentLanguage = settings.Language;
                    ApplyLayout(settings.LayoutMode);
                }
                else
                {
                    // Initial/Default
                    Sessions.Add(new SessionData() { Path = "c:\\" });
                    SelectedIndex = 0;
                    ApplyLayout(AppConstants.LayoutType.Vertical);
                }

                if (SelectedIndex < 0 || SelectedIndex >= Sessions.Count) SelectedIndex = 0;

                // Ensure at least one session
                if (Sessions.Count == 0)
                {
                    Sessions.Add(new SessionData() { Path = "c:\\" });
                    SelectedIndex = 0;
                }

                UpdateHeaderFromSession();
                Logger.Log(AppConstants.LogCacheLoadSuccess);
            }
            catch (Exception ex)
            {
                Logger.Log(AppConstants.LogCacheLoadError, ex);
                Sessions.Add(new SessionData() { Path = "c:\\" });
                SelectedIndex = 0;
                ApplyLayout(AppConstants.LayoutType.Vertical);
            }
        }

        private void SaveCache()
        {
            try
            {
                var settings = new AppSettings();
                settings.Language = LocalizationManager.Instance.CurrentLanguage;
                settings.LayoutMode = MenuLayoutHorizontal.IsChecked ? AppConstants.LayoutType.Horizontal : AppConstants.LayoutType.Vertical;

                settings.WindowTop = this.Top;
                settings.WindowLeft = this.Left;
                settings.WindowWidth = this.Width;
                settings.WindowHeight = this.Height;

                if (this.WindowState == WindowState.Maximized) settings.WindowState = 2;
                else if (this.WindowState == WindowState.Minimized) settings.WindowState = 1;
                else settings.WindowState = 0;

                settings.SelectedIndex = SelectedIndex;

                var fileNames = new List<string>();
                foreach (var s in Sessions)
                {
                    string name = SessionFileManager.Save(s);
                    if (!string.IsNullOrEmpty(name)) fileNames.Add(name);
                }
                settings.SessionFileNames = fileNames.ToArray();

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

            UpdateHeaderFromSession();
            UpdateAddButtonState();
        }

        private void UpdateAddButtonState()
        {
            if (AddTabButton != null)
                AddTabButton.IsEnabled = Sessions.Count < 10;
        }

        private void TabCloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SessionData session)
            {
                // Cancel partial scan if any?
                session.Cts?.Cancel();

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

        private void MenuConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Directory.Exists(AppConstants.AppDataDirectory))
                {
                    Directory.CreateDirectory(AppConstants.AppDataDirectory);
                }
                Process.Start("explorer.exe", AppConstants.AppDataDirectory);
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to open config folder", ex);
            }
        }

        private void MenuReadme_Click(object sender, RoutedEventArgs e)
        {
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
                    MessageBox.Show("Readme file not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to open Readme", ex);
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
                    MessageBox.Show("License file not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to show app license", ex);
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
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            string versionStr = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";

            string message = LocalizationManager.Instance.GetText(LanguageKey.AboutMessage)
                .Replace("{Version}", versionStr)
                .Replace("{Date}", DateTime.Now.ToString("yyyy/MM/dd")) // Use current date or build date
                .Replace("\\n", "\n");

            MessageBox.Show(message, LocalizationManager.Instance.GetText(LanguageKey.AboutTitle), MessageBoxButton.OK, MessageBoxImage.Information);
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
    }
}
