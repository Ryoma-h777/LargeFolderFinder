using System.Collections.Generic;
using YamlDotNet.Serialization;
using MessagePack;

namespace LargeFolderFinder
{
    /// <summary>
    /// フォルダ情報を保持するクラス
    /// </summary>
    [MessagePackObject(AllowPrivate = true)]
    public class FolderInfo
    {
        /// <summary>
        /// フォルダ名またはファイル名
        /// </summary>
        [Key(0)]
        public string Name { get; set; }

        /// <summary>
        /// ツリービューで展開されているかどうか
        /// </summary>
        [Key(5)]
        public bool IsExpanded { get; set; } = true;

        /// <summary>
        /// ファイルかどうか（true: ファイル, false: フォルダ）
        /// </summary>
        [Key(1)]
        public bool IsFile { get; set; } = false;

        /// <summary>
        /// 最終更新日時
        /// </summary>
        [Key(2)]
        public System.DateTime LastModified { get; set; }

        /// <summary>
        /// 所有者（Windowsアカウント名）
        /// </summary>
        [Key(6)]
        public string Owner { get; set; }

        [IgnoreMember]
        private long _size;

        /// <summary>
        /// サイズ（バイト）
        /// </summary>
        [Key(3)]
        public long Size
        {
            get => _size;
            set => _size = value;
        }

        /// <summary>
        /// 子要素のリスト
        /// </summary>
        [Key(4)]
        public List<FolderInfo> Children { get; set; } = new List<FolderInfo>();

        [YamlIgnore]
        [IgnoreMember]
        public FolderInfo? Parent { get; set; }

        /// <summary>
        /// コンストラクタ（デフォルト）
        /// </summary>
        public FolderInfo()
        {
            Name = "";
            Owner = "";
            LastModified = System.DateTime.MinValue;
        }

        /// <summary>
        /// パラメータを指定して初期化します。
        /// </summary>
        /// <param name="name">フォルダまたはファイル名</param>
        /// <param name="size">サイズ（バイト）</param>
        /// <param name="isFile">ファイルの場合はtrue</param>
        /// <param name="lastModified">最終更新日時</param>
        public FolderInfo(string name, long size, bool isFile = false, System.DateTime? lastModified = null)
        {
            Name = name;
            Owner = "";
            _size = size;
            IsFile = isFile;
            LastModified = lastModified ?? System.DateTime.MinValue;
        }

        /// <summary>
        /// サイズをスレッドセーフに加算し、親ノードへ通知します。
        /// </summary>
        public void AddSize(long bytes)
        {
            System.Threading.Interlocked.Add(ref _size, bytes);
            Parent?.AddSize(bytes);
        }

        public string GetFullPath()
        {
            if (Parent == null) return Name; // Root holds full path
            return System.IO.Path.Combine(Parent.GetFullPath(), Name);
        }
        public void RestoreParentReferences()
        {
            if (Children != null)
            {
                foreach (var child in Children)
                {
                    child.Parent = this;
                    child.RestoreParentReferences();
                }
            }
        }
        public long CountFolderRecursive()
        {
            long count = 1; // Self
            if (Children != null)
            {
                foreach (var child in Children)
                {
                    count += child.CountFolderRecursive();
                }
            }
            return count;
        }

    }
}
