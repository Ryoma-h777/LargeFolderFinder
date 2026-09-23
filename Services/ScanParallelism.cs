using System;
using System.IO;

namespace LargeFolderFinder
{
    /// <summary>
    /// 走査の並列度（ワーカー数）を決める規則。
    /// 画面・計測の道具・検証ツールで同じ決め方になるよう1箇所にまとめる。
    /// </summary>
    /// <remarks>
    /// 状態を持たない純粋な判定なので、検証ツールから直接確かめられるよう public にしている。
    /// パスの判定はネットワークへ接続せず、パスの形とドライブの種類だけで行う。
    /// </remarks>
    public static class ScanParallelism
    {
        /// <summary>設定で指定できるワーカー数の上限。これを超える設定値はこの値に丸める</summary>
        private const int MaxConfiguredThreads = 64;

        /// <summary>自動のときのネットワーク（UNC・ネットワークドライブ）の既定のワーカー数</summary>
        private const int NetworkAutoThreads = 16;

        /// <summary>自動のときのローカルの下限のワーカー数（論理プロセッサ数がこれより少なくてもここまでは使う）</summary>
        private const int LocalAutoMinThreads = 4;

        /// <summary>自動のときのローカルの上限のワーカー数</summary>
        private const int LocalAutoMaxThreads = 16;

        /// <summary>
        /// 走査のワーカー数を決める。逐次の設定なら1、設定値が正ならその値（上限を超えたら丸める）、
        /// 0 以下なら自動（ネットワークは既定値、ローカルは論理プロセッサ数を下限〜上限に丸めた値）。
        /// </summary>
        /// <param name="rootPath">走査の起点のパス。自動のときにネットワークかどうかの判定に使う</param>
        /// <param name="useParallel">並列の走査の設定。false なら設定値に関わらず1（要件3.2）</param>
        /// <param name="configuredThreads">設定のワーカー数。0 は自動</param>
        /// <returns>1 以上のワーカー数</returns>
        public static int Resolve(string rootPath, bool useParallel, int configuredThreads)
        {
            // 逐次の設定が最優先。設定値がいくつでも1つずつ順に列挙する（要件3.2）
            if (!useParallel)
            {
                return 1;
            }

            if (configuredThreads > 0)
            {
                if (configuredThreads > MaxConfiguredThreads)
                {
                    // 丸めたときだけ記録する（走査のたびに出さない）
                    Logger.Log($"走査の並列度の設定 {configuredThreads} は上限を超えるため、{MaxConfiguredThreads} に丸めました。");
                    return MaxConfiguredThreads;
                }

                return configuredThreads;
            }

            if (configuredThreads < 0)
            {
                // 負の値も範囲外。自動として扱い、丸めたことを記録する
                Logger.Log($"走査の並列度の設定 {configuredThreads} は範囲外のため、自動（0）として扱います。");
            }

            return IsNetworkPath(rootPath)
                ? NetworkAutoThreads
                : Math.Clamp(Environment.ProcessorCount, LocalAutoMinThreads, LocalAutoMaxThreads);
        }

        /// <summary>
        /// UNC パス、またはネットワークドライブ上のパスなら true。判定できなければ false（ローカルとして扱う）。
        /// </summary>
        /// <param name="rootPath">判定するパス</param>
        /// <remarks>
        /// 実在しないホストやつながっていない共有でも接続を試みず、パスの形とドライブの種類だけで判定する。
        /// </remarks>
        public static bool IsNetworkPath(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return false;
            }

            try
            {
                string path = rootPath.Trim();

                // 拡張長パスの接頭辞（\\?\ と \\.\）は外してから判定する。\\?\UNC\server\share はネットワーク
                if (HasDevicePrefix(path))
                {
                    string rest = path.Substring(4);

                    if (rest.StartsWith("UNC", StringComparison.OrdinalIgnoreCase) &&
                        (rest.Length == 3 || IsSeparator(rest[3])))
                    {
                        return true;
                    }

                    path = rest;
                }
                else if (path.Length >= 2 && IsSeparator(path[0]) && IsSeparator(path[1]))
                {
                    // \\server\share 形式の UNC パス
                    return true;
                }

                if (string.IsNullOrEmpty(path))
                {
                    return false;
                }

                string? root = Path.GetPathRoot(path);

                if (string.IsNullOrEmpty(root))
                {
                    // 相対パスなど、どのドライブか決められないものはローカルとして扱う
                    return false;
                }

                if (root.Length >= 2 && IsSeparator(root[0]) && IsSeparator(root[1]))
                {
                    // 接頭辞を外した結果が UNC の形だった場合
                    return true;
                }

                return new DriveInfo(root).DriveType == DriveType.Network;
            }
            catch (Exception)
            {
                // 意図して無視: 存在しないドライブや扱えない形のパスは判定できないだけで、走査は続けられる。
                // 判定の失敗で走査を止めないよう、ローカルとして扱う（自動の並列度はローカルの既定値になる）
                return false;
            }
        }

        /// <summary>拡張長パス・デバイスパスの接頭辞（\\?\ または \\.\）で始まるか</summary>
        private static bool HasDevicePrefix(string path)
        {
            return path.Length >= 4 &&
                   IsSeparator(path[0]) &&
                   IsSeparator(path[1]) &&
                   (path[2] == '?' || path[2] == '.') &&
                   IsSeparator(path[3]);
        }

        /// <summary>パスの区切り文字（\ と /）か</summary>
        private static bool IsSeparator(char c)
        {
            return c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
        }
    }
}
