using System.Collections.Generic;
using YamlDotNet.Serialization;
using MessagePack;

namespace LargeFolderFinder
{
    /// <summary>
    /// フォルダ情報を保持するクラス
    /// </summary>
    [MessagePackObject]
    public class FolderInfo
    {
        [Key(0)]
        public string Name { get; set; }

        [Key(1)]
        public bool IsFile { get; set; } = false;

        [Key(2)]
        public System.DateTime LastModified { get; set; }

        [IgnoreMember]
        private long _size;

        [Key(3)]
        public long Size
        {
            get => _size;
            set => _size = value;
        }

        [Key(4)]
        public List<FolderInfo> Children { get; set; } = new List<FolderInfo>();

        [YamlIgnore]
        [IgnoreMember]
        public FolderInfo? Parent { get; set; }

        public FolderInfo()
        {
            Name = "";
            LastModified = System.DateTime.MinValue;
        }

        public FolderInfo(string name, long size, bool isFile = false, System.DateTime? lastModified = null)
        {
            Name = name;
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
    }
}
