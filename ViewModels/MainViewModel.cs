using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LargeFolderFinder;

namespace LargeFolderFinder.ViewModels
{
    /// <summary>
    /// アプリケーション全体のビューモデル。
    /// セッション管理、タブ操作、設定の読み込み/保存を担当します。
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        /// <summary>
        /// セッション（タブ）のコレクション
        /// </summary>
        public ObservableCollection<SessionData> Sessions { get; } = new ObservableCollection<SessionData>();

        private int _selectedIndex;
        /// <summary>
        /// 現在選択されているタブのインデックス
        /// </summary>
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (_selectedIndex != value)
                {
                    _selectedIndex = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentSession));
                }
            }
        }

        /// <summary>
        /// 現在選択されているセッション（タブ）のデータ
        /// </summary>
        public SessionData? CurrentSession
        {
            get
            {
                if (SelectedIndex >= 0 && SelectedIndex < Sessions.Count)
                    return Sessions[SelectedIndex];
                return null;
            }
        }

        private AppConstants.LayoutType _layoutMode = AppConstants.LayoutType.Vertical;

        /// <summary>
        /// 現在のレイアウトモード（垂直/水平）
        /// </summary>
        public AppConstants.LayoutType LayoutMode
        {
            get => _layoutMode;
            set
            {
                if (_layoutMode != value)
                {
                    _layoutMode = value;
                    OnPropertyChanged();
                }
            }
        }

        public MainViewModel()
        {
        }

        /// <summary>
        /// ViewModelの初期化処理
        /// </summary>
        public void Initialize()
        {
            Logger.Log("MainViewModel initialized");
        }

        /// <summary>
        /// キャッシュおよび設定をロードし、セッションを復元します。
        /// </summary>
        public void LoadCache()
        {
            Logger.Log("LoadCache: Start");
            try
            {
                var settings = AppSettings.Load();
                Logger.Log($"LoadCache: Settings loaded (Result: {(settings != null ? "Success" : "Null")})");

                Sessions.Clear();

                // Load LayoutMode
                if (settings != null)
                {
                    LayoutMode = settings.LayoutMode;
                }

                // Load All Sessions from Disk
                var files = SessionFileManager.GetAllSessionFileNames();
                Logger.Log($"LoadCache: Found {files.Length} session files.");
                Array.Sort(files);

                foreach (var file in files)
                {
                    Logger.Log($"LoadCache: Adding placeholder for {file}");
                    // Try to restore path from settings to show tab title immediately
                    string initialPath = "Loading...";
                    if (settings?.SessionInfos != null)
                    {
                        var info = settings.SessionInfos.FirstOrDefault(x => x.FileName == file);
                        if (info != null && !string.IsNullOrEmpty(info.Path))
                        {
                            initialPath = info.Path;
                        }
                    }

                    Sessions.Add(new SessionData
                    {
                        Path = initialPath,
                        IsLoading = true,
                        FileName = file
                    });
                }

                if (Sessions.Count == 0)
                {
                    Logger.Log("LoadCache: No sessions found, adding default tab.");
                    AddNewTab();
                }

                int settingsIndex = settings?.SelectedIndex ?? 0;
                if (settingsIndex < 0 || settingsIndex >= Sessions.Count)
                    settingsIndex = Math.Min(Math.Max(0, Sessions.Count - 1), 0);

                SelectedIndex = settingsIndex;

                // UI Thread Dispatcher
                var dispatcher = Application.Current.Dispatcher;
                var loadTargets = new List<SessionData>(Sessions);
                int activeIdx = SelectedIndex;
                Logger.Log($"LoadCache: Starting async load for {loadTargets.Count} sessions. ActiveIdx: {activeIdx}");

                Task.Run(() =>
                {
                    void LoadSingle(SessionData target)
                    {
                        if (string.IsNullOrEmpty(target.FileName)) return;

                        Logger.Log($"LoadCache: Loading file {target.FileName}");
                        try
                        {
                            var loaded = SessionFileManager.Load(target.FileName!);
                            dispatcher.Invoke(() =>
                            {
                                if (loaded != null)
                                {
                                    Logger.Log($"LoadCache: Loaded {target.FileName} successfully. Path: {loaded.Path}");
                                    target.CopyFrom(loaded);
                                    target.IsLoading = false;
                                    target.FileName = target.FileName;

                                    if (target.ViewModel is SessionViewModel vm)
                                    {
                                        vm.SyncSessionToView();
                                        _ = vm.RenderResult();
                                    }
                                }
                                else
                                {
                                    Logger.Log($"LoadCache: Failed to load content for {target.FileName}");
                                    target.IsLoading = false;
                                    target.Path = "Load Failed";
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"LoadCache: Exception in LoadSingle for {target.FileName}", ex);
                            // Avoid infinite loading state
                            dispatcher.Invoke(() =>
                            {
                                target.IsLoading = false;
                                target.HasLoadError = true;
                            });
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
                if (Sessions.Count == 0) AddNewTab();
            }
        }

        /// <summary>
        /// 現在のアプリケーションの状態（ウィンドウ位置、設定、全セッション）を保存します。
        /// </summary>
        /// <param name="top">ウィンドウの上位置</param>
        /// <param name="left">ウィンドウの左位置</param>
        /// <param name="width">ウィンドウの幅</param>
        /// <param name="height">ウィンドウの高さ</param>
        /// <param name="windowStateValue">ウィンドウの状態値</param>
        /// <param name="fontSize">フォントサイズ</param>
        public void SaveCache(double top, double left, double width, double height, int windowStateValue, double fontSize)
        {
            try
            {
                var active = CurrentSession;

                foreach (var session in Sessions)
                {
                    if (session.ViewModel is SessionViewModel vm)
                    {
                        // Sync UI settings to session (including Path if active)
                        vm.SyncViewToSession(session.Path, session == active);
                    }
                }

                var settings = new AppSettings
                {
                    Language = LocalizationManager.Instance.CurrentLanguage,
                    LayoutMode = this.LayoutMode,

                    WindowTop = top,
                    WindowLeft = left,
                    WindowWidth = width,
                    WindowHeight = height,
                    WindowState = windowStateValue,
                    SelectedIndex = SelectedIndex,
                    FontSize = fontSize
                };

                var filenames = new List<string>();
                var sessionInfos = new List<SessionInfo>();

                // Save each Session
                foreach (var session in Sessions)
                {
                    string? filename;
                    if ((session.IsLoading || session.HasLoadError) && !string.IsNullOrEmpty(session.FileName))
                    {
                        filename = session.FileName;
                    }
                    else
                    {
                        filename = SessionFileManager.Save(session);
                        if (filename != null) session.FileName = filename;
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

                Task.Run(() => SessionFileManager.DeleteOldSessions(30));
            }
            catch (Exception ex)
            {
                Logger.Log(AppConstants.LogCacheSaveError, ex);
            }
        }

        /// <summary>
        /// 新しいタブ（セッション）を追加します。
        /// </summary>
        public void AddNewTab()
        {
            try
            {
                if (Sessions.Count >= 10) return;

                var newSession = new SessionData() { Path = "c:\\" };
                Sessions.Add(newSession);
                SelectedIndex = Sessions.Count - 1;
                Logger.Log("Added new tab");
            }
            catch (Exception ex)
            {
                Logger.Log("Error in AddNewTab", ex);
            }
        }

        /// <summary>
        /// 指定されたセッション（タブ）を閉じ、データを削除します。
        /// </summary>
        /// <param name="session">閉じるセッション</param>
        public void CloseTab(SessionData session)
        {
            try
            {
                if (session == null) return;

                // Cancel operations
                session.Cts?.Cancel();
                if (session.ViewModel is SessionViewModel vm)
                {
                    vm.CancelScan();
                }

                // Delete cache file
                if (!string.IsNullOrEmpty(session.FileName))
                {
                    SessionFileManager.Delete(session.FileName!);
                }

                int idx = Sessions.IndexOf(session);
                if (idx != -1)
                {
                    Sessions.Remove(session);
                    Logger.Log($"Closed tab. Remaining: {Sessions.Count}");

                    if (Sessions.Count == 0)
                    {
                        AddNewTab();
                    }
                    else if (idx == SelectedIndex || SelectedIndex >= Sessions.Count)
                    {
                        SelectedIndex = Math.Min(idx, Sessions.Count - 1);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Error in CloseTab", ex);
            }
        }
    }
}
