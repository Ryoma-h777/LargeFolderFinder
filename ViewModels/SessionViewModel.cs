using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Windows.Controls;
using LargeFolderFinder; // For Models/Services/Helpers which are still in items namespace

namespace LargeFolderFinder.ViewModels
{
    /// <summary>
    /// セッション（タブ）ごとのロジックを管理するViewModel
    /// </summary>
    public class SessionViewModel : INotifyPropertyChanged
    {
        private readonly SessionData _model;
        private readonly IMainLayoutView _view; // Viewへの参照（MVP/Controllerパターン的利用）
        private readonly ResultFormatter _formatter = new ResultFormatter();
        private bool _hasShownProgressError = false;

        public SessionData Model => _model;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public SessionViewModel(SessionData model, IMainLayoutView view)
        {
            _model = model;
            _view = view;
            _model.ViewModel = this; // 相互参照
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // MainWindow.xaml.cs から移行したロジック

        /// <summary>
        /// スキャン処理を実行します (ScanButton_Click 相当)
        /// </summary>
        /// <summary>
        /// 指定されたパスでスキャン処理を非同期に開始します。
        /// 設定の同期、プログレスバーの初期化、スキャンの実行、結果のレンダリングを行います。
        /// </summary>
        /// <param name="pathFromUi">スキャン対象のパス</param>
        public async Task ScanAsync(string pathFromUi)
        {
            if (_model.IsScanning) return;
            if (_view == null) return;

            var lm = LocalizationManager.Instance;
            string path = pathFromUi; // UIから渡されたパスを使用

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
            if (!SyncViewToSession(path, true))
            {
                return;
            }

            // Restore/Recalculate values used in ScanButton_Click
            double thresholdVal = _model.Threshold;
            AppConstants.SizeUnit selectedUnit = _model.Unit;
            long thresholdBytes = (long)(thresholdVal * AppConstants.GetBytesPerUnit(selectedUnit));

            var config = Config.Load();

            // Update session timestamp and rename file
            if (!string.IsNullOrEmpty(_model.FileName))
            {
                SessionFileManager.Delete(_model.FileName!);
            }
            _model.CreatedAt = DateTime.Now;
            _model.FileName = SessionFileManager.Save(_model);

            _hasShownProgressError = false;

            _model.Cts = new CancellationTokenSource();
            _model.IsScanning = true;

            _view.OutputListBox.ItemsSource = null;
            _view.ScanProgressBar.Visibility = Visibility.Visible;
            _view.ScanProgressBar.Value = 0;
            _view.StatusTextBlock.Text = lm.GetText(LanguageKey.FolderCountStatus);

            Logger.Log(string.Format(AppConstants.LogScanStart, path, thresholdVal + selectedUnit.ToString()));
            var sw = Stopwatch.StartNew();

            try
            {
                int totalFolders = config.SkipFolderCount ? 0 : await Scanner.CountFoldersAsync(path, config.MaxDepthForCount, _model.Cts.Token);
                var progress = new Progress<ScanProgress>(p =>
                {
                    try
                    {
                        string statusMsg;
                        if (!config.SkipFolderCount && totalFolders > 0)
                        {
                            double percentage = (double)p.ProcessedFolders / totalFolders * 100;
                            _view.ScanProgressBar.Value = percentage;
                            _view.ScanProgressBar.IsIndeterminate = false;

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
                            _view.ScanProgressBar.IsIndeterminate = true;
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

                        _view.StatusTextBlock.Text = statusMsg;

                        if (p.CurrentResult != null)
                        {
                            _model.Result = p.CurrentResult;
                            _ = RenderResult(); // Fire and forget
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
                    _view.StatusTextBlock.Text = lm.GetText(LanguageKey.ScanningStatus);
                    _view.ScanProgressBar.IsIndeterminate = true;
                }
                else
                {
                    _view.StatusTextBlock.Text = string.Format(
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
                    _model.Cts.Token);

                // Success
                _model.Result = result;
                await RenderResult();

                // UIの描画完了を待ってから時間を止める
                await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                sw.Stop();
                _model.LastScanDuration = sw.Elapsed;
                _model.TotalFilesScanned = _model.Result?.CountFolderRecursive() ?? 0;

                if (_view != null)
                {
                    _view.StatusTextBlock.Text = $"{lm.GetText(LanguageKey.FinishedStatus)} " +
                        $"{string.Format(lm.GetText(LanguageKey.ProcessingTime), _formatter.FormatDuration(sw.Elapsed))}";
                }
                Logger.Log(string.Format(AppConstants.LogScanSuccess, _formatter.FormatDuration(sw.Elapsed)));

                // SaveCache is skipped here as per Plan

                GC.Collect();
                _model.Cts?.Dispose();
                _model.Cts = null;
            }
            catch (OperationCanceledException)
            {
                Logger.Log("Canceled.");
                if (_view != null) _view.StatusTextBlock.Text = lm.GetText(LanguageKey.CancelledStatus);
            }
            catch (Exception ex)
            {
                Logger.Log(AppConstants.LogScanError, ex);
                if (_view != null)
                    _view.StatusTextBlock.Text = lm.GetText(LanguageKey.LabelError) + ex.Message;
                // Restore global settings if error
                SyncViewToSession(path, true);
            }
            finally
            {
                _model.IsScanning = false;

                if (_view != null)
                {
                    _view.ScanProgressBar.Visibility = Visibility.Collapsed;
                    _view.ScanProgressBar.IsIndeterminate = false;
                }
                _ = RenderResult();

                GC.Collect();
                _model.Cts?.Dispose();
                _model.Cts = null;
            }
        }

        /// <summary>
        /// 実行中のスキャンをキャンセルします。
        /// </summary>
        public void CancelScan()
        {
            if (_model.IsScanning)
            {
                Logger.Log("Canceling scan...");
                _model.Cts?.Cancel();
            }
        }

        /// <summary>
        /// ビュー（UI）の設定値をセッションデータに同期します。
        /// </summary>
        /// <param name="currentPath">現在のパス（テキストボックスの値）</param>
        /// <param name="isCurrentSession">このセッションが現在アクティブかどうか</param>
        /// <returns>同期に成功した場合は true、検証エラーなどで失敗した場合は false</returns>
        public bool SyncViewToSession(string currentPath, bool isCurrentSession)
        {
            if (_model == null) return false;
            var view = _view;
            if (view == null) return false;

            // UI settings sync (Per-session view items)
            if (!double.TryParse(view.MinSizeTextBox.Text, out double thresholdVal))
            {
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
            _model.Threshold = thresholdVal;

            if (view.UnitComboBox.SelectedIndex >= 0)
            {
                _model.Unit = (AppConstants.SizeUnit)view.UnitComboBox.SelectedIndex;
            }

            _model.IncludeFiles = view.IncludeFilesCheckBox.IsChecked == true;
            _model.SeparatorIndex = view.SeparatorComboBox.SelectedIndex;
            if (int.TryParse(view.TabWidthTextBox.Text, out int tw))
            {
                _model.TabWidth = tw;
            }

            if (view.FilterTextBox != null)
            {
                _model.FilterText = view.FilterTextBox.Text;
            }
            if (view.FilterModeComboBox != null)
            {
                _model.FilterModeIndex = view.FilterModeComboBox.SelectedIndex;
            }

            // Global settings sync (Sync only if this IS the active session being viewed/scanned)
            if (isCurrentSession)
            {
                _model.Path = currentPath;
            }

            return true;
        }

        /// <summary>
        /// セッションの値をビューに反映（セッション→ビュー）
        /// </summary>
        public void SyncSessionToView()
        {
            if (_view == null || _model == null) return;

            // UI settings sync
            _view.MinSizeTextBox.Text = _model.Threshold.ToString();
            _view.UnitComboBox.SelectedIndex = (int)_model.Unit;
            _view.IncludeFilesCheckBox.IsChecked = _model.IncludeFiles;
            _view.SeparatorComboBox.SelectedIndex = _model.SeparatorIndex;
            _view.TabWidthTextBox.Text = _model.TabWidth.ToString();

            if (_view.FilterTextBox != null)
            {
                _view.FilterTextBox.Text = _model.FilterText;
            }
            if (_view.FilterModeComboBox != null)
            {
                _view.FilterModeComboBox.SelectedIndex = _model.FilterModeIndex;
            }

            // Status Bar
            if (!_model.IsScanning)
            {
                var lm = LocalizationManager.Instance;
                if (_model.LastScanDuration != TimeSpan.Zero || _model.TotalFilesScanned > 0)
                {
                    string countText = _model.IsCounting ? "" : $" {lm.GetText(LanguageKey.FolderCountStatus)}: {(_model.Result?.CountFolderRecursive() ?? 0):N0}";
                    _view.StatusTextBlock.Text = $"{lm.GetText(LanguageKey.FinishedStatus)} {string.Format(lm.GetText(LanguageKey.ProcessingTime), _formatter.FormatDuration(_model.LastScanDuration))} ({_model.TotalFilesScanned:N0} files){countText}";
                }
                else
                {
                    _view.StatusTextBlock.Text = lm.GetText(LanguageKey.ReadyStatus);
                }
            }
        }

        /// <summary>
        /// セッションの結果（SessionData.Result）を元に、UI（リストボックス等）を再描画します。
        /// フィルタリング、ソート、クリップボード用テキスト生成もここで行います。
        /// </summary>
        public async Task RenderResult()
        {
            var view = _view;
            var session = _model;

            if (view == null || session?.Result == null) return;
            if (view.OutputListBox == null) return;

            // Update Progress Bar
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
                    var filterCache = new ConcurrentDictionary<FolderInfo, System.Collections.Generic.List<FolderInfo>>();

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
                    Application.Current.Dispatcher.Invoke(() =>
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

                            // Restore status if not scanning
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
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var lm = LocalizationManager.Instance;
                        view.StatusTextBlock.Text = $"{lm.GetText(LanguageKey.LabelError)}{ex.Message}";
                        Logger.Log($"Render error: {ex}");
                        view.ScanProgressBar.Visibility = Visibility.Collapsed;
                    });
                }
            });
        }
    }
}
