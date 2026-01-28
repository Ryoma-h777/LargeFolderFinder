using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MessagePack;

namespace LargeFolderFinder
{
    /// <summary>
    /// 検索セッションデータ（MessagePack形式で保存）
    /// </summary>
    [MessagePackObject(AllowPrivate = true)]
    public class SessionData : INotifyPropertyChanged
    {
        [IgnoreMember]
        private DateTime _createdAt = DateTime.Now;
        /// <summary>セッション作成日時（ファイル名にも使用）</summary>
        [Key(0)]
        public DateTime CreatedAt
        {
            get => _createdAt;
            set => SetProperty(ref _createdAt, value);
        }

        [IgnoreMember]
        private string _path = "";
        /// <summary>検索パス</summary>
        [Key(1)]
        public string Path
        {
            get => _path;
            set
            {
                if (SetProperty(ref _path, value))
                {
                    OnPropertyChanged(nameof(TabTitle));
                }
            }
        }

        [IgnoreMember]
        private double _threshold = AppConstants.DefaultThreshold;
        /// <summary>サイズ閾値</summary>
        [Key(2)]
        public double Threshold
        {
            get => _threshold;
            set => SetProperty(ref _threshold, value);
        }

        [IgnoreMember]
        private AppConstants.SizeUnit _unit = AppConstants.SizeUnit.GB;
        /// <summary>サイズ単位</summary>
        [Key(3)]
        public AppConstants.SizeUnit Unit
        {
            get => _unit;
            set => SetProperty(ref _unit, value);
        }

        [IgnoreMember]
        private bool _includeFiles = false;
        /// <summary>ファイルを含めるかどうか</summary>
        [Key(4)]
        public bool IncludeFiles
        {
            get => _includeFiles;
            set => SetProperty(ref _includeFiles, value);
        }

        [IgnoreMember]
        private AppConstants.SortTarget _sortTarget = AppConstants.SortTarget.Size;
        /// <summary>ソート対象</summary>
        [Key(5)]
        public AppConstants.SortTarget SortTarget
        {
            get => _sortTarget;
            set => SetProperty(ref _sortTarget, value);
        }

        [IgnoreMember]
        private AppConstants.SortDirection _sortDirection = AppConstants.SortDirection.Descending;
        /// <summary>ソート方向</summary>
        [Key(6)]
        public AppConstants.SortDirection SortDirection
        {
            get => _sortDirection;
            set => SetProperty(ref _sortDirection, value);
        }

        [IgnoreMember]
        private int _separatorIndex = 1;
        /// <summary>区切り文字インデックス（0: Tab, 1: Space）</summary>
        [Key(7)]
        public int SeparatorIndex
        {
            get => _separatorIndex;
            set => SetProperty(ref _separatorIndex, value);
        }

        [IgnoreMember]
        private int _tabWidth = AppConstants.DefaultTabWidth;
        /// <summary>タブ幅</summary>
        [Key(8)]
        public int TabWidth
        {
            get => _tabWidth;
            set => SetProperty(ref _tabWidth, value);
        }

        [IgnoreMember]
        private string _filterText = "";
        /// <summary>フィルタ文字列</summary>
        [Key(10)]
        public string FilterText
        {
            get => _filterText;
            set => SetProperty(ref _filterText, value);
        }

        [IgnoreMember]
        private int _filterModeIndex = 0;
        /// <summary>フィルタモード（0: Normal, 1: Regex）</summary>
        [Key(11)]
        public int FilterModeIndex
        {
            get => _filterModeIndex;
            set => SetProperty(ref _filterModeIndex, value);
        }

        [IgnoreMember]
        private FolderInfo? _result;
        /// <summary>検索結果（FolderInfoツリー）</summary>
        [Key(9)]
        public FolderInfo? Result
        {
            get => _result;
            set => SetProperty(ref _result, value);
        }

        /// <summary>
        /// ファイル名を生成（ScanYYYYMMDDHHmmssffff.msgpack形式）
        /// </summary>
        public string GenerateFileName()
        {
            return $"Scan{CreatedAt:yyyyMMddHHmmssfff}.msgpack";
        }

        /// <summary>
        /// タブタイトルを取得
        /// </summary>
        [IgnoreMember]
        public string TabTitle
        {
            get
            {
                if (string.IsNullOrEmpty(Path)) return "New Tab";
                string name = System.IO.Path.GetFileName(Path.TrimEnd(
                    System.IO.Path.DirectorySeparatorChar,
                    System.IO.Path.AltDirectorySeparatorChar));
                return string.IsNullOrEmpty(name) ? Path : name;
            }
        }

        /// <summary>
        /// タブのツールチップ（作成日時 - パス）
        /// </summary>
        [IgnoreMember]
        public string TabTooltip
        {
            get
            {
                var pathDisplay = string.IsNullOrEmpty(Path) ? "No Path" : Path;
                return $"{CreatedAt:yyyy/MM/dd HH:mm:ss} - {pathDisplay}";
            }
        }

        [IgnoreMember]
        private object? _currentView;
        [IgnoreMember]
        public object? CurrentView
        {
            get => _currentView;
            set => SetProperty(ref _currentView, value);
        }

        [IgnoreMember]
        public System.Threading.CancellationTokenSource? Cts { get; set; }

        [IgnoreMember]
        private bool _isScanning;
        [IgnoreMember]
        public bool IsScanning
        {
            get => _isScanning;
            set => SetProperty(ref _isScanning, value);
        }

        [IgnoreMember]
        public string? CachedCopyText { get; set; }

        [IgnoreMember]
        public System.Threading.CancellationTokenSource? RenderCts { get; set; }

        [IgnoreMember]
        public string? LastStatus { get; set; }

        [IgnoreMember]
        public Task? CopyTextGenerationTask { get; set; }

        [IgnoreMember]
        public System.Threading.CancellationTokenSource? CopyCts { get; set; }

        [IgnoreMember]
        public TimeSpan LastScanDuration { get; set; }

        [IgnoreMember]
        public long TotalFilesScanned { get; set; }

        [IgnoreMember]
        public bool IsCounting { get; set; }

        [IgnoreMember]
        private bool _isLoading;
        [IgnoreMember]
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        [IgnoreMember]
        public string? FileName { get; set; }

        public void CopyFrom(SessionData other)
        {
            this.CreatedAt = other.CreatedAt;
            this.Path = other.Path;
            this.Threshold = other.Threshold;
            this.Unit = other.Unit;
            this.IncludeFiles = other.IncludeFiles;
            this.SortTarget = other.SortTarget;
            this.SortDirection = other.SortDirection;
            this.SeparatorIndex = other.SeparatorIndex;
            this.TabWidth = other.TabWidth;
            this.FilterText = other.FilterText;
            this.FilterModeIndex = other.FilterModeIndex;
            this.Result = other.Result;
            this.FileName = other.FileName;
            // Do not copy IsLoading, we handle it manually or it's implicitly false from loaded data? 
            // Loaded data usually has IsLoading=false (default). 
            // But we should explicit set it to false AFTER copy to trigger UI update last.
        }

        #region INotifyPropertyChanged Implementation
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(storage, value))
            {
                return false;
            }
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }
        #endregion
    }
}
