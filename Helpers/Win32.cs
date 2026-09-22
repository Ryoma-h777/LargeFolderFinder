using System;
using System.Runtime.InteropServices;

namespace LargeFolderFinder
{
    /// <summary>
    /// 本体が使う OS 呼び出しの宣言をまとめたクラス。
    /// 物理サイズ換算に使うクラスタサイズの取得と、メモリの切り詰めの2つだけを持つ
    /// </summary>
    static class Win32
    {
        /// <summary>
        /// プロセスのワーキングセットを切り詰めるためのAPI（最小値・最大値に -1 を渡すと可能な限り解放する）
        /// </summary>
        [DllImport("kernel32.dll", EntryPoint = "SetProcessWorkingSetSize")]
        public static extern int SetProcessWorkingSetSize(IntPtr process, int minimumWorkingSetSize, int maximumWorkingSetSize);

        /// <summary>
        /// クラスタサイズを取得するためのAPI
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool GetDiskFreeSpace(string lpRootPathName, out uint lpSectorsPerCluster,
            out uint lpBytesPerSector, out uint lpNumberOfFreeClusters, out uint lpTotalNumberOfClusters);
    }
}
