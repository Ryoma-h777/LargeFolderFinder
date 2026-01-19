using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace LargeFolderFinder
{
    /// <summary>
    /// フォルダ情報を保持するクラス
    /// </summary>
    public class FolderInfo
    {
        public string Name { get; set; }

        private long _size;
        public long Size
        {
            get => _size;
            set => _size = value;
        }

        public List<FolderInfo> Children { get; set; } = new List<FolderInfo>();

        [YamlIgnore]
        public FolderInfo? Parent { get; set; }

        public FolderInfo(string name, long size)
        {
            Name = name;
            _size = size;
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
