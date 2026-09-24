using System;
using System.Security.Principal;

namespace LargeFolderFinder
{
    /// <summary>
    /// いまのプロセスが管理者として動いているかを返す部品（ntfs-mft-scan 要件3.4）。
    /// 走査の方式の判断に使う。
    /// 起動のときの昇格はアプリのマニフェスト（requestedExecutionLevel を highestAvailable にする）が行うため、
    /// この部品は判定だけを持ち、昇格を試みる処理は持たない。
    /// </summary>
    public static class AdminRights
    {
        /// <summary>
        /// 昇格の状態は起動のときに決まり、プロセスの寿命の間は変わらないため、一度だけ調べて保持する。
        /// </summary>
        private static readonly bool ElevatedState = DetectElevated();

        /// <summary>いま管理者として動いているか。</summary>
        public static bool IsElevated => ElevatedState;

        /// <summary>
        /// いまのプロセスの利用者が管理者の役割を持つかを調べる。
        /// 走査の方式の決定で必ず読む値なので、調べられなかった場合も例外を外に出さず
        /// 「管理者ではない」として扱う（安全側に倒し、管理者を必要とする走査を選ばない）。
        /// この関数が例外を投げないことで、静的な初期化が失敗することもない。
        /// </summary>
        private static bool DetectElevated()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception)
            {
                // 意図して無視: 権限の状態を調べられない環境では「管理者ではない」として扱う。
                // ここは起動の直後にも読まれるため、ログの仕組みには依存しない。
                return false;
            }
        }
    }
}
