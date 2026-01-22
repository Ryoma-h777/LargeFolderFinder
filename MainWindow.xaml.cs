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
using System.Windows.Controls.Primitives;

namespace LargeFolderFinder
{
    /// <summary>
    /// メインクラス
    /// </summary>
    public partial class MainWindow : Window
    {
        private CancellationTokenSource? _cts;
        private CancellationTokenSource? _renderCts;
        private FolderInfo? _lastScanResult;
        private bool _isScanning = false;
        private DispatcherTimer? _memoryTimer;
        private IMainLayoutView? _layoutView;
        private bool _hasShownProgressError = false;

        public MainWindow()
        {
            try
            {
                Logger.Log(AppConstants.LogAppStarted);
                InitializeComponent();
                InitializeLocalization();
                LoadCache(); // ここで ApplyLayout が呼ばれる
                UpdateLanguageMenu();

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

        internal void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_layoutView == null) return;
            try
            {
                Logger.Log(AppConstants.LogBrowseButtonClicked);
                var dialog = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
                {
                    Description = LocalizationManager.Instance.GetText(LanguageKey.FolderLabel),
                    UseDescriptionForTitle = true,
                    SelectedPath = _layoutView.PathTextBox.Text
                };

                if (dialog.ShowDialog() == true)
                {
                    _layoutView.PathTextBox.Text = dialog.SelectedPath;
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
            if (_isScanning || _layoutView == null) return;
            var lm = LocalizationManager.Instance;

            string path = _layoutView.PathTextBox.Text;
            if (string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path))
            {
                MessageBox.Show(
                    lm.GetText(LanguageKey.PathInvalidError),
                    lm.GetText(LanguageKey.LabelError),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            if (!double.TryParse(_layoutView.SearchSizeTextBox.Text, out double thresholdVal))
            {
                MessageBox.Show(
                    lm.GetText(LanguageKey.ThresholdInvalidError),
                    lm.GetText(LanguageKey.LabelError),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            AppConstants.SizeUnit selectedUnit = AppConstants.SizeUnit.GB;
            if (_layoutView.UnitComboBox.SelectedIndex >= 0)
            {
                selectedUnit = (AppConstants.SizeUnit)_layoutView.UnitComboBox.SelectedIndex;
            }
            long thresholdBytes = (long)(thresholdVal * AppConstants.GetBytesPerUnit(selectedUnit));

            var config = Config.Load();
            SaveCache(); // 検索履歴の保存
            _hasShownProgressError = false;

            _cts = new CancellationTokenSource();
            _isScanning = true;
            _layoutView.RunButton.IsEnabled = false;
            _layoutView.CancelButton.IsEnabled = true;
            _layoutView.OutputTextBox.Clear();
            _layoutView.ScanProgressBar.Visibility = Visibility.Visible;
            _layoutView.ScanProgressBar.Value = 0;
            _layoutView.StatusTextBlock.Text = lm.GetText(LanguageKey.FolderCountStatus);

            Logger.Log(string.Format(AppConstants.LogScanStart, path, thresholdVal + selectedUnit.ToString()));
            var sw = Stopwatch.StartNew();

            try
            {
                int totalFolders = config.SkipFolderCount ? 0 : await Scanner.CountFoldersAsync(path, config.MaxDepthForCount, _cts.Token);
                var progress = new Progress<ScanProgress>(p =>
                {
                    try
                    {
                        string statusMsg;
                        if (!config.SkipFolderCount && totalFolders > 0)
                        {
                            double percentage = (double)p.ProcessedFolders / totalFolders * 100;
                            _layoutView.ScanProgressBar.Value = percentage;
                            _layoutView.ScanProgressBar.IsIndeterminate = false;

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
                            _layoutView.ScanProgressBar.IsIndeterminate = true;
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

                        _layoutView.StatusTextBlock.Text = statusMsg;

                        if (p.CurrentResult != null)
                        {
                            _lastScanResult = p.CurrentResult;
                            _ = RenderResult(); // Fire and forget
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
                    _layoutView.StatusTextBlock.Text = lm.GetText(LanguageKey.ScanningStatus);
                    _layoutView.ScanProgressBar.IsIndeterminate = true;
                }
                else
                {
                    _layoutView.StatusTextBlock.Text = string.Format(
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
                    _cts.Token);

                _lastScanResult = result;
                await RenderResult();
                // UIの描画完了を待ってから時間を止める
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                sw.Stop();

                if (_layoutView != null)
                {
                    _layoutView.StatusTextBlock.Text = $"{lm.GetText(LanguageKey.FinishedStatus)} " +
                        $"[{string.Format(lm.GetText(LanguageKey.ProcessingTime), FormatDuration(sw.Elapsed))}]";
                }
                Logger.Log(string.Format(AppConstants.LogScanSuccess, FormatDuration(sw.Elapsed)));

                SaveCache(); // 検索結果の保存

                OptimizeMemory();
                _cts?.Dispose();
                _cts = null;
            }
            catch (OperationCanceledException)
            {
                Logger.Log("Canceled.");
                _layoutView.StatusTextBlock.Text = lm.GetText(LanguageKey.CancelledStatus);
            }
            catch (Exception ex)
            {
                Logger.Log(AppConstants.LogScanError, ex);
                _layoutView.StatusTextBlock.Text = lm.GetText(LanguageKey.LabelError) + ex.Message;
            }
            finally
            {
                _isScanning = false;
                _layoutView.RunButton.IsEnabled = true;
                _layoutView.CancelButton.IsEnabled = false;
                _layoutView.ScanProgressBar.Visibility = Visibility.Collapsed;
                _layoutView.ScanProgressBar.IsIndeterminate = false;
                _ = RenderResult();
                OptimizeMemory();
                _cts?.Dispose();
                _cts = null;
            }
        }

        internal void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Log("Canceling scan...");
            _cts?.Cancel();
        }

        internal void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_layoutView == null) return;
            try
            {
                string text = _layoutView.OutputTextBox.Text;
                if (string.IsNullOrWhiteSpace(text)) return;
                Clipboard.SetText(text);
                ShowNotification(LocalizationManager.Instance.GetText(LanguageKey.CopyNotification));
            }
            catch (Exception ex)
            {
                Logger.Log(AppConstants.LogClipboardError, ex);
                MessageBox.Show(
                    $"{LocalizationManager.Instance.GetText(LanguageKey.ClipboardError)}{ex.Message}",
                    "Error");
            }
        }

        private async void ShowNotification(string message)
        {
            if (_layoutView == null) return;
            _layoutView.NotificationTextBlock.Text = message;
            await Task.Delay(3000);
            if (_layoutView.NotificationTextBlock.Text == message)
                _layoutView.NotificationTextBlock.Text = "";
        }

        public void ApplyLayout(AppConstants.LayoutType layoutMode)
        {
            if (MainContentHolder == null) return;

            UserControl view = layoutMode == AppConstants.LayoutType.Horizontal ?
                (UserControl)new HorizontalLayoutView(this) :
                (UserControl)new VerticalLayoutView(this);
            _layoutView = (IMainLayoutView)view;
            MainContentHolder.Content = view;

            MenuLayoutVertical.IsChecked = layoutMode == AppConstants.LayoutType.Vertical;
            MenuLayoutHorizontal.IsChecked = layoutMode == AppConstants.LayoutType.Horizontal;

            InitializeComboBoxes();
            ApplyLocalization();

            var cache = CacheData.Load();
            if (cache != null && cache.Sessions.Count > 0)
            {
                int idx = cache.SelectedIndex;
                if (idx < 0 || idx >= cache.Sessions.Count) idx = 0;
                var session = cache.Sessions[idx];

                _layoutView.PathTextBox.Text = session.Path;
                _layoutView.SearchSizeTextBox.Text = session.Threshold.ToString();
                _layoutView.UnitComboBox.SelectedIndex = (int)session.Unit;
                _layoutView.SortComboBox.SelectedIndex = (int)session.SortTarget;
                _layoutView.SortDirectionComboBox.SelectedIndex = (int)session.SortDirection;
                _layoutView.IncludeFilesCheckBox.IsChecked = session.IncludeFiles;
                _layoutView.SeparatorComboBox.SelectedIndex = session.SeparatorIndex;
                _layoutView.TabWidthTextBox.Text = session.TabWidth.ToString();
            }
        }

        private void InitializeComboBoxes()
        {
            if (_layoutView == null) return;

            _layoutView.UnitComboBox.Items.Clear();
            foreach (var u in Enum.GetNames(typeof(AppConstants.SizeUnit)))
                _layoutView.UnitComboBox.Items.Add(u);

            _layoutView.SortComboBox.Items.Clear();
            _layoutView.SortComboBox.Items.Add(LocalizationManager.Instance.GetText(LanguageKey.TargetSize));
            _layoutView.SortComboBox.Items.Add(LocalizationManager.Instance.GetText(LanguageKey.TargetName));
            _layoutView.SortComboBox.Items.Add(LocalizationManager.Instance.GetText(LanguageKey.TargetDate));

            _layoutView.SortDirectionComboBox.Items.Clear();
            _layoutView.SortDirectionComboBox.Items.Add(LocalizationManager.Instance.GetText(LanguageKey.DirectionAsc));
            _layoutView.SortDirectionComboBox.Items.Add(LocalizationManager.Instance.GetText(LanguageKey.DirectionDesc));

            _layoutView.SeparatorComboBox.Items.Clear();
            _layoutView.SeparatorComboBox.Items.Add("Tab");
            _layoutView.SeparatorComboBox.Items.Add("Space");

            _layoutView.SeparatorComboBox.SelectionChanged += (s, e) =>
            {
                _layoutView.TabWidthArea.Visibility = _layoutView.SeparatorComboBox.SelectedIndex == (int)AppConstants.Separator.Tab
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                _ = RenderResult();
            };

            // Re-bind other events if needed
            // Re-bind other events if needed
            _layoutView.SearchSizeTextBox.TextChanged += (s, e) => { _ = RenderResult(); };
            _layoutView.UnitComboBox.SelectionChanged += (s, e) => { _ = RenderResult(); };
            _layoutView.SortComboBox.SelectionChanged += (s, e) => { _ = RenderResult(); };
            _layoutView.SortDirectionComboBox.SelectionChanged += (s, e) => { _ = RenderResult(); };
            _layoutView.IncludeFilesCheckBox.Click += (s, e) => { _ = RenderResult(); };
            _layoutView.TabWidthTextBox.TextChanged += (s, e) => { _ = RenderResult(); };
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

            _layoutView?.ApplyLocalization(lm);
            Logger.Log(AppConstants.LogApplyLocSuccess);
        }

        private async Task RenderResult()
        {
            if (_lastScanResult == null || _layoutView == null) return;
            try
            {
                // UI入力の取得（メインスレッド）
                if (!double.TryParse(_layoutView.SearchSizeTextBox.Text, out double thresholdVal)) return;

                AppConstants.SizeUnit unit = (AppConstants.SizeUnit)_layoutView.UnitComboBox.SelectedIndex;
                long thresholdBytes = (long)(thresholdVal * AppConstants.GetBytesPerUnit(unit));
                bool includeFiles = _layoutView.IncludeFilesCheckBox.IsChecked == true;
                var sortTarget = (AppConstants.SortTarget)_layoutView.SortComboBox.SelectedIndex;
                var sortDirection = (AppConstants.SortDirection)_layoutView.SortDirectionComboBox.SelectedIndex;
                bool useSpaces = _layoutView.SeparatorComboBox.SelectedIndex == (int)AppConstants.Separator.Space;

                int tabWidth = AppConstants.DefaultTabWidth;
                int.TryParse(_layoutView.TabWidthTextBox.Text, out tabWidth);

                // 前回の処理をキャンセル
                _renderCts?.Cancel();
                _renderCts = new CancellationTokenSource();
                var token = _renderCts.Token;

                // UIフェードバック開始
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                string originalStatus = _layoutView.StatusTextBlock.Text;
                _layoutView.StatusTextBlock.Text = LocalizationManager.Instance.GetText(LanguageKey.RenderingStatus);

                try
                {
                    // バックグラウンドでレイアウト計算と文字列生成を実行
                    string resultText = await Task.Run(() =>
                    {
                        if (token.IsCancellationRequested) return "";

                        // 1. フィルタリングとソートの結果をキャッシュ（一貫性確保と高速化）
                        var filterCache = new Dictionary<FolderInfo, List<FolderInfo>>();
                        BuildFilterCache(_lastScanResult, filterCache, thresholdBytes, includeFiles, sortTarget, sortDirection, token);

                        if (token.IsCancellationRequested) return "";

                        // 2. 最大行長の計算（ソート順序非依存・キャッシュ利用）
                        int maxLineLen = CalculateMaxLineLength(
                            _lastScanResult,
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
                            _lastScanResult,
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
                        _layoutView.OutputTextBox.Text = resultText;
                    }
                }
                finally
                {
                    // UIフェードバック終了
                    System.Windows.Input.Mouse.OverrideCursor = null;
                    // キャンセルされていない場合のみステータスを戻す（キャンセルされた場合は「キャンセル」などが表示される可能性があるため...
                    // 実際にはRenderResult自体がキャンセルされるわけではなく内部のタスクがキャンセルされる。
                    // 新しいRenderResultが走っている場合は、そちらがステータスを上書きしているはずなので、
                    // ここで戻すべきかどうかは微妙だが、_renderCtsが自分のものであれば戻す、などの制御が必要。
                    // しかし単純化のため、完了時はとりあえず元に戻す。
                    if (!token.IsCancellationRequested)
                    {
                        _layoutView.StatusTextBlock.Text = originalStatus;
                    }
                }
            }
            catch (OperationCanceledException) { /* Ignored */ }
            catch (Exception ex) { Logger.Log("Render error", ex); }
        }

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

            // インデント幅の統一: Lastの場合もスペース3つ分（TreeSpace=1, TreeVertical=2相当だが、ここではスペース3つ）にする
            // TreeVertical(2) + TreeSpace(1) = 3
            // TreeSpace(1) + TreeSpace(1) + TreeSpace(1) = 3
            int childIndentLen = indentLen + (isRoot
                ? 0
                : GetStringWidth(isLast
                    ? AppConstants.TreeSpace + AppConstants.TreeSpace + AppConstants.TreeSpace
                    : AppConstants.TreeVertical + AppConstants.TreeSpace));

            if (node.Children != null)
            {
                // キャッシュからフィルタリング済みリストを取得（ソートは行長計算に関係ないが、フィルタリング結果は必要）
                List<FolderInfo>? list = null;
                if (filterCache.TryGetValue(node, out var cachedList))
                {
                    list = cachedList;
                }
                else
                {
                    // キャッシュが無い場合のフォールバック（通常発生しないはずだが安全のため）
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
                // キャッシュからソート済みリストを取得
                List<FolderInfo>? list = null;
                if (filterCache.TryGetValue(node, out var cachedList))
                {
                    list = cachedList;
                }
                else
                {
                    // フォールバック（通常は呼ばれない）
                    lock (node.Children)
                    {
                        list = node.Children.Where(c => c.Size >= thresholdBytes && (includeFiles || !c.IsFile)).ToList();
                    }
                }

                if (list != null)
                {
                    // インデント幅の統一: Lastの場合もスペース3つ分
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
                    // フィルタリング
                    var query = node.Children.Where(c => c.Size >= thresholdBytes && (includeFiles || !c.IsFile));

                    // ソート
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

                // 再帰的にキャッシュ構築（可視要素のみ）
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
                // 半角(1):全角(2)
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
                var c = CacheData.Load();
                ApplyLayout(c?.LayoutMode ?? AppConstants.LayoutType.Vertical);

                if (c != null)
                {
                    // Restore Window Position/Size
                    if (!double.IsNaN(c.WindowTop) && !double.IsNaN(c.WindowLeft) &&
                        !double.IsNaN(c.WindowWidth) && !double.IsNaN(c.WindowHeight))
                    {
                        this.Top = c.WindowTop;
                        this.Left = c.WindowLeft;
                        this.Width = c.WindowWidth;
                        this.Height = c.WindowHeight;
                    }

                    // Restore WindowState
                    if (c.WindowState == (int)WindowState.Maximized)
                    {
                        this.WindowState = WindowState.Maximized;
                    }
                }

                if (c != null && c.Sessions.Count > 0)
                {
                    int idx = c.SelectedIndex;
                    if (idx < 0 || idx >= c.Sessions.Count) idx = 0;
                    var session = c.Sessions[idx];
                    if (session.Result != null)
                    {
                        _lastScanResult = session.Result;
                        // Initial render
                        _ = RenderResult();
                    }
                }
                Logger.Log(AppConstants.LogCacheLoadSuccess);
            }
            catch (Exception ex)
            {
                Logger.Log(AppConstants.LogCacheLoadError, ex);
            }
        }

        private void SaveCache()
        {
            if (_layoutView == null)
            {
                return;
            }

            Logger.Log(AppConstants.LogCacheSaveStart);
            try
            {
                double.TryParse(_layoutView.SearchSizeTextBox.Text, out double th);
                int.TryParse(_layoutView.TabWidthTextBox.Text, out int tw);

                var cache = CacheData.Load() ?? new CacheData();
                cache.Language = LocalizationManager.Instance.CurrentLanguage;
                cache.LayoutMode = MenuLayoutHorizontal.IsChecked
                        ? AppConstants.LayoutType.Horizontal
                        : AppConstants.LayoutType.Vertical;

                // Save Window Geometry
                if (this.WindowState == WindowState.Maximized)
                {
                    cache.WindowTop = this.RestoreBounds.Top;
                    cache.WindowLeft = this.RestoreBounds.Left;
                    cache.WindowWidth = this.RestoreBounds.Width;
                    cache.WindowHeight = this.RestoreBounds.Height;
                    cache.WindowState = (int)WindowState.Maximized;
                }
                else
                {
                    // Normal or Minimized (treat Minimized as Normal using RestoreBounds)
                    var rect = this.WindowState == WindowState.Minimized ? this.RestoreBounds : new Rect(this.Left, this.Top, this.Width, this.Height);
                    cache.WindowTop = rect.Top;
                    cache.WindowLeft = rect.Left;
                    cache.WindowWidth = rect.Width;
                    cache.WindowHeight = rect.Height;
                    cache.WindowState = (int)WindowState.Normal;
                }

                var session = new SearchSession
                {
                    Path = _layoutView.PathTextBox.Text,
                    Threshold = th,
                    SeparatorIndex = _layoutView.SeparatorComboBox.SelectedIndex,
                    TabWidth = tw,
                    Unit = (AppConstants.SizeUnit)_layoutView.UnitComboBox.SelectedIndex,
                    IncludeFiles = _layoutView.IncludeFilesCheckBox.IsChecked == true,
                    SortTarget = (AppConstants.SortTarget)_layoutView.SortComboBox.SelectedIndex,
                    SortDirection = (AppConstants.SortDirection)_layoutView.SortDirectionComboBox.SelectedIndex,
                    Result = _lastScanResult
                };

                if (cache.Sessions.Count == 0)
                {
                    cache.Sessions.Add(session);
                    cache.SelectedIndex = 0;
                }
                else
                {
                    if (cache.SelectedIndex < 0 || cache.SelectedIndex >= cache.Sessions.Count)
                        cache.SelectedIndex = 0;
                    cache.Sessions[cache.SelectedIndex] = session;
                }

                cache.Save();
                Logger.Log(AppConstants.LogCacheSaveSuccess);
            }
            catch (Exception ex)
            {
                Logger.Log(AppConstants.LogCacheSaveError, ex);
            }
        }

        private void UpdateLanguageMenu()
        {
            try
            {
                Logger.Log(AppConstants.LogUpdateMenuStart);
                MenuLanguage.Items.Clear();
                foreach (var l in LocalizationManager.Instance.GetAvailableLanguages())
                {
                    var item = new MenuItem
                    {
                        Header = l.MenuText,
                        Tag = l.Code
                    };
                    item.Click += (s, e) =>
                    {
                        Logger.Log(string.Format(AppConstants.LogLangChangeStart, item.Tag));
                        LocalizationManager.Instance.CurrentLanguage = (string)item.Tag;
                        SaveCache();
                        ApplyLocalization();
                        if (_lastScanResult != null)
                        {
                            _ = RenderResult();
                        }
                    };
                    MenuLanguage.Items.Add(item);
                }
                Logger.Log(AppConstants.LogUpdateMenuSuccess);
            }
            catch (Exception ex)
            {
                Logger.Log(AppConstants.LogUpdateMenuError, ex);
            }
        }

        private void MenuView_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource != sender) return;
            try
            {
#if DEBUG
                Logger.Log("MenuView_SubmenuOpened");
#endif
                MenuOpenLogSub.Items.Clear();
                var logsDir = AppConstants.LogsDirectoryPath;
                if (Directory.Exists(logsDir))
                {
                    var logFiles = Directory.GetFiles(logsDir, "*_Log.txt")
                        .Select(p => new FileInfo(p))
                        .OrderByDescending(p => p.CreationTime);
                    foreach (var f in logFiles)
                    {
                        var item = new MenuItem { Header = f.Name, Tag = f.FullName };
                        item.Click += (s, ev) => OpenOrActivateTextViewer((string)item.Tag);
                        MenuOpenLogSub.Items.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Error Menu/Log/... view failed", ex);
            }
        }
        internal void MenuConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConstants.ConfigFileName);
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    Logger.Log(Path.GetFileName(path));
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Error opening...", ex);
                var lm = LocalizationManager.Instance;
                MessageBox.Show(
                    $"{lm.GetText(LanguageKey.ConfigError)}\n{ex.Message}",
                    lm.GetText(LanguageKey.LabelError),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        private void MenuReadme_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string langCode = LocalizationManager.Instance.CurrentLanguage;
                if (string.IsNullOrEmpty(langCode)) langCode = "en";

                // 言語コードだけでマッチ (例: ja, en)
                string filename = AppConstants.GetReadmeFileName(langCode);
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConstants.ReadmeDirectoryName, filename);

                if (!File.Exists(path))
                {
                    // フォールバック: 英語
                    path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConstants.ReadmeDirectoryName, AppConstants.GetReadmeFileName("en"));
                }

                if (File.Exists(path))
                {
                    OpenOrActivateTextViewer(path);
                    Logger.Log($"Opened {Path.GetFileName(path)}");
                }
                else
                {
                    var lm = LocalizationManager.Instance;
                    MessageBox.Show(
                        $"{lm.GetText(LanguageKey.ReadmeNotFound)}\n{path}",
                        lm.GetText(LanguageKey.LabelError),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error opening...", ex);
                var lm = LocalizationManager.Instance;
                MessageBox.Show(
                    $"{lm.GetText(LanguageKey.ReadmeError)}\n{ex.Message}",
                    lm.GetText(LanguageKey.LabelError),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            var lm = LocalizationManager.Instance;
            MessageBox.Show(
                string.Format(lm.GetText(LanguageKey.AboutMessage), AppInfo.Title, AppInfo.Version, AppInfo.Copyright),
                lm.GetText(LanguageKey.AboutTitle)
            );
        }
        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
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
            catch (Exception ex)
            {
                Logger.Log("Error restarting as admin", ex);
            }
        }
        private void MenuLayoutVertical_Click(object sender, RoutedEventArgs e)
        {
            ApplyLayout(AppConstants.LayoutType.Vertical);
        }
        private void MenuLayoutHorizontal_Click(object sender, RoutedEventArgs e)
        {
            ApplyLayout(AppConstants.LayoutType.Horizontal);
        }
        private void OpenOrActivateTextViewer(string path)
        {
            foreach (Window w in Application.Current.Windows)
            {
                if (w is TextViewer v && string.Equals(v.FilePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    w.Activate();
                    return;
                }
            }
            new TextViewer(path).Show();
        }

        private void OptimizeMemory()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Win32.SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
            }
            catch (Exception ex)
            {
                Logger.Log("Error optimizing memory", ex);
            }
            Logger.Log(AppConstants.LogOptimizeMemory);
        }

        private void InitializeMemoryTimer()
        {
            _memoryTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMinutes(AppConstants.MemoryOptimizeIntervalMinutes)
            };
            _memoryTimer.Tick += (s, e) => OptimizeMemory();
            _memoryTimer.Start();
        }

        private void MenuAppLicense_Click(object sender, RoutedEventArgs e)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConstants.LicenseDirectoryName, AppConstants.AppLicenseFileName);
            try
            {
                if (File.Exists(path))
                {
                    OpenOrActivateTextViewer(path);
                    Logger.Log($"Opened {Path.GetFileName(path)}");
                }
                else
                {
                    var lm = LocalizationManager.Instance;
                    MessageBox.Show(
                        lm.GetText(LanguageKey.LicenseNotFoundError),
                        lm.GetText(LanguageKey.LabelError),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error opening... {path}", ex);
                var lm = LocalizationManager.Instance;
                MessageBox.Show(
                    $"{lm.GetText(LanguageKey.LabelError)}\n{ex.Message}",
                    lm.GetText(LanguageKey.LabelError),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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
                    Logger.Log($"Opened {Path.GetFileName(path)}");
                }
                else
                {
                    var lm = LocalizationManager.Instance;
                    MessageBox.Show(
                        $"{lm.GetText(LanguageKey.LicenseNotFoundError)}\n{path}",
                        lm.GetText(LanguageKey.LabelError),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error opening...", ex);
                var lm = LocalizationManager.Instance;
                MessageBox.Show(
                    $"{lm.GetText(LanguageKey.LabelError)}\n{ex.Message}",
                    lm.GetText(LanguageKey.LabelError),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
