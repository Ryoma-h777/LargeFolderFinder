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

        /// <summary>
        /// 設定ファイルの読み込みに未通知の失敗があれば、理由を添えて利用者にダイアログで知らせる。
        /// 起動の後と走査の開始時の両方から呼ぶ。通知済みの同じ失敗では何もしない。
        /// </summary>
        internal static void NotifyConfigLoadErrorIfAny()
        {
            if (!Config.TryTakeUnnotifiedError(out string error)) return;

            var lm = LocalizationManager.Instance;
            // 既定の設定で動作を続けられるため、アイコンは警告にする
            MessageBox.Show(
                string.Format(lm.GetText(LanguageKey.ConfigParseError), error),
                lm.GetText(LanguageKey.LabelError),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
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

            // 設定の解析に失敗していれば知らせる。走査は既定の設定で続ける（同じ失敗は一度だけ知らせる）
            NotifyConfigLoadErrorIfAny();

            // Update session timestamp and rename file
            if (!string.IsNullOrEmpty(_model.FileName))
            {
                SessionFileManager.Delete(_model.FileName!);
            }
            _model.CreatedAt = DateTime.Now;
            _model.FileName = SessionFileManager.Save(_model);

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
                        bool hasTotal = !config.SkipFolderCount && totalFolders > 0;

                        // 完了の印つきの最後の報告: 走査は終わり、成功の経路の描画がこのあと続く。
                        // 状態を走査中の文言に戻さず描画中の文言にし、結果（CurrentResult は null）は描画しない。
                        // 最終結果の描画は成功の経路の RenderResult（最新の要求だけを反映する仕組み）に任せる
                        if (p.IsFinal)
                        {
                            if (hasTotal)
                            {
                                // 事前カウントを行った走査はバーを埋める
                                _view.ScanProgressBar.IsIndeterminate = false;
                                _view.ScanProgressBar.Value = 100;
                            }
                            // 事前カウントを省いた走査は不定のまま（成功の経路の finally で非表示に戻る）
                            _view.StatusTextBlock.Text = lm.GetText(LanguageKey.RenderingStatus);
                            return;
                        }

                        string statusMsg;
                        if (hasTotal)
                        {
                            // 事前カウントの後にフォルダが増えた場合などでも 100% を超えないよう頭打ちにする（バーと文言で同じ値を使う）
                            double percentage = Math.Min(100, (double)p.ProcessedFolders / totalFolders * 100);
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
                        // 途中経過の更新の失敗は記録するだけで、走査は止めない（利用者にダイアログは出さない）
                        Logger.Log(AppConstants.LogScanProgressError, ex);
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
        /// 同じタブで進行中の以前の描画は取り消し、最新の描画だけを画面に反映します。
        /// 取り消しによる終了は記録せず、それ以外の失敗はログに記録して一覧を直前の状態のまま残します
        /// （投げっぱなしで呼ばれても失敗が消えないよう、例外を呼び出し元へ投げません）。
        /// </summary>
        public async Task RenderResult()
        {
            var view = _view;
            var session = _model;

            if (view == null || session?.Result == null) return;
            if (view.OutputListBox == null) return;

            // 同じタブの前の描画を取り消し、この描画の取り消しの通知を得る（別のタブの描画には触れない）
            CancellationToken token = session.RenderCancellation.Begin();

            // 描画の途中で結果が差し替えられても一貫した木を描くよう、開始時点の根を控える
            FolderInfo root = session.Result;

            // コピーの操作が、この描画のクリップボード用テキストの完成を待てるよう、開始の時点（UI スレッド）で
            // 待ち先を差し替える。生成を始める前に描画が終わった場合（取り消し・失敗）は finally で完了にする
            var copyTextReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.CopyTextGenerationTask = copyTextReady.Task;
            bool copyTextStarted = false;

            try
            {
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

                // 整形はバックグラウンドで行い、すべての段階に取り消しの通知を渡す
                var items = await Task.Run(() =>
                {
                    // Filter Cache構築
                    var filterCache = new ConcurrentDictionary<FolderInfo, System.Collections.Generic.List<FolderInfo>>();

                    _formatter.BuildFilterCache(
                        root,
                        filterCache,
                        sizeThreshold,
                        includeFiles,
                        sortTarget,
                        sortDirection,
                        token,
                        filter);
                    token.ThrowIfCancellationRequested();

                    // Calculate max length for alignment if needed
                    int targetColumn = 0;
                    if (useSpaces)
                    {
                        targetColumn = _formatter.CalculateMaxLineLength(
                            root,
                            filterCache,
                            0,
                            true,
                            true,
                            sizeThreshold,
                            includeFiles,
                            token);
                        targetColumn += 4;
                    }
                    token.ThrowIfCancellationRequested();

                    // クリップボード用テキストの生成（非同期）。次の描画が始まるかタブを閉じると取り消される。
                    // 待つ側（コピーの操作）が取り消しの例外を受けないよう、Task.Run には通知を渡さず中で確かめる。
                    // 取り消し元は次の Begin で破棄されるため、通知の確かめは IsCancellationRequested と IsLatest だけで行う
                    copyTextStarted = true;
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            if (token.IsCancellationRequested) return;
                            var sb = new StringBuilder();
                            _formatter.PrintTreeRecursive(
                               sb,
                               root,
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
                               token);

                            // 最新の描画のものだけを残し、古い描画のテキストで上書きしない
                            if (session.RenderCancellation.IsLatest(token))
                            {
                                session.CachedCopyText = sb.ToString();
                            }
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            // 意図して無視: 次の描画が始まったかタブを閉じたための取り消しで、失敗ではない
                        }
                        catch (Exception ex)
                        {
                            Logger.Log("クリップボード用テキストの生成に失敗しました。", ex);
                        }
                        finally
                        {
                            // コピーの操作の待ちを解く（取り消し・失敗でも待ち続けさせない）
                            copyTextReady.TrySetResult();
                        }
                    });

                    // ListView Items Generation
                    var generated = _formatter.GenerateListItemsRecursive(
                        root,
                        filterCache,
                        "",
                        false,
                        true,
                        0,
                        false,
                        tabWidth,
                        sizeThreshold,
                        unit,
                        includeFiles,
                        token
                    ).ToList();
                    token.ThrowIfCancellationRequested();
                    return generated;
                });
                // 注: Task.Run に通知を渡すと内部で処理が登録されるため渡さない（取り消し元は次の Begin で破棄される）。
                // 取り消しは中の各段階で確かめる

                // UI スレッドに戻った後、画面へ反映する直前に最新かを確かめる。最新でなければ反映しない
                if (!session.RenderCancellation.IsLatest(token)) return;

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
                        string countText = session.IsCounting ? "" : $" {lm.GetText(LanguageKey.FolderCountStatus)}: {root.CountFolderRecursive():N0}";
                        view.StatusTextBlock.Text = $"{lm.GetText(LanguageKey.FinishedStatus)} {string.Format(lm.GetText(LanguageKey.ProcessingTime), _formatter.FormatDuration(session.LastScanDuration))} ({session.TotalFilesScanned:N0} files){countText}";
                    }
                    else
                    {
                        view.StatusTextBlock.Text = lm.GetText(LanguageKey.ReadyStatus);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // 新しい描画またはタブを閉じたことによる取り消し。意図した終了のため記録しない
            }
            catch (Exception ex)
            {
                // 想定外の失敗。内容と処理を記録し、一覧は直前の状態のまま残す
                Logger.Log($"結果の描画に失敗しました（対象: {session.Path}）。一覧は直前の状態のまま残します。", ex);

                // 最新の描画の失敗のときだけ、状態表示に失敗を示して描画中の表示を戻す
                if (session.RenderCancellation.IsLatest(token))
                {
                    var lm = LocalizationManager.Instance;
                    view.StatusTextBlock.Text = $"{lm.GetText(LanguageKey.LabelError)}{ex.Message}";
                    if (!session.IsScanning)
                    {
                        view.ScanProgressBar.Visibility = Visibility.Collapsed;
                        view.ScanProgressBar.IsIndeterminate = false;
                    }
                }
            }
            finally
            {
                // 生成を始める前に描画が終わった（取り消し・失敗）ときは、コピーの操作の待ちをここで解く
                if (!copyTextStarted) copyTextReady.TrySetResult();
            }
        }
    }
}
