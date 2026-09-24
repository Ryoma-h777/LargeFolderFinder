using System;
using System.IO;

namespace LargeFolderFinder
{
    /// <summary>
    /// 走査の方式を決める規則（ntfs-mft-scan 要件1.1, 2.1, 2.4, 3.4）。
    /// 設定・対象がローカルの NTFS か・いま管理者かの3つから、方式とその理由を返す。
    /// </summary>
    /// <remarks>
    /// 状態を持たない純粋な判定なので、検証ツールから直接確かめられるよう public にしている
    /// （<see cref="ScanParallelism"/> と同じ扱い）。
    /// 管理者かどうかは引数で渡すため、管理者でない環境でも決め方そのものを確かめられる。
    /// 対象が小さいときに通常の走査を選ぶ基準（要件5.3）は、基準の値を管理者での計測で決めるため、
    /// この規則にはまだ入っていない（design.md「走査の方式の決定」の流れ図の「対象が小さいか」）。
    /// </remarks>
    public static class ScanMethodSelector
    {
        /// <summary>目録の走査に使えるファイルシステムの名前</summary>
        private const string NtfsFileSystemName = "NTFS";

        /// <summary>
        /// 対象がローカルの NTFS のドライブかを判定できなかったときの理由。
        /// 判定できないときは安全側に倒して通常の走査を選ぶ。
        /// </summary>
        private const string UndecidableVolumeReason =
            "対象がローカルの NTFS のドライブかを判定できなかったため、通常の走査を選びました。";

        /// <summary>
        /// 走査の方式を決める。
        /// </summary>
        /// <param name="rootPath">走査の起点のパス。ローカルの NTFS かどうかの判定に使う</param>
        /// <param name="useMftScan">目録の走査を使う設定（<see cref="Config.UseMftScan"/>）。false なら常に通常の走査（要件2.4）</param>
        /// <param name="isElevated">いま管理者として動いているか（<see cref="AdminRights.IsElevated"/>）。false なら通常の走査（要件3.4）</param>
        /// <returns>選んだ方式と、その理由（日本語の1行）</returns>
        public static ScanMethodDecision Decide(string rootPath, bool useMftScan, bool isElevated)
        {
            // 要件2.4: 使わない設定が有効なら、他の条件を見ずに通常の走査
            if (!useMftScan)
            {
                return new ScanMethodDecision(
                    ScanMethodKind.NormalEnumeration,
                    "設定で目録の走査を使わないため、通常の走査を選びました。");
            }

            // 要件2.1: ネットワーク上の場所・NTFS 以外・判定できない場合は通常の走査
            (bool isLocalNtfs, string volumeReason) = InspectVolume(rootPath);

            if (!isLocalNtfs)
            {
                return new ScanMethodDecision(ScanMethodKind.NormalEnumeration, volumeReason);
            }

            // 要件3.4: 管理者として動いていなければ通常の走査。昇格を促さない（昇格は起動のときだけ）
            if (!isElevated)
            {
                return new ScanMethodDecision(
                    ScanMethodKind.NormalEnumeration,
                    "管理者として動いていないため、通常の走査を選びました。");
            }

            // 要件1.1: 対象がローカルの NTFS で、管理者として動いているときだけ目録の走査
            return new ScanMethodDecision(
                ScanMethodKind.VolumeLayout,
                "対象がローカルの NTFS のドライブで、管理者として動いているため、目録の走査を選びました。");
        }

        /// <summary>
        /// 対象がローカル（固定・取り外し可能）の NTFS のドライブの上にあれば true。
        /// 判定できなければ false（通常の走査に倒す）。
        /// </summary>
        /// <param name="rootPath">判定するパス</param>
        /// <remarks>
        /// 実在しないホストやつながっていない共有でも接続を試みず、パスの形とドライブの情報だけで判定する。
        /// </remarks>
        public static bool IsLocalNtfsVolume(string rootPath)
        {
            return InspectVolume(rootPath).IsLocalNtfs;
        }

        /// <summary>
        /// 対象のドライブを調べ、ローカルの NTFS かどうかと、そうでない場合の理由を返す。
        /// </summary>
        /// <remarks>
        /// ファイルシステム名とドライブの種類は <see cref="DriveInfo"/> から得る
        /// （<see cref="DriveInfo.DriveFormat"/> は OS のボリュームの情報の問い合わせをそのまま返すため、
        /// 本体に OS の直接呼び出しを増やさずに判定できる）。
        /// 判定のためにボリュームを開くことはしない。
        /// </remarks>
        private static (bool IsLocalNtfs, string Reason) InspectVolume(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return (false, UndecidableVolumeReason);
            }

            // ネットワークの判定は並列度の決め方と同じ規則を使う（接続せず、パスの形とドライブの種類だけで判定する）
            if (ScanParallelism.IsNetworkPath(rootPath))
            {
                return (false, "対象がネットワーク上の場所のため、通常の走査を選びました。");
            }

            try
            {
                string? root = Path.GetPathRoot(rootPath.Trim());

                if (string.IsNullOrEmpty(root))
                {
                    // 相対パスなど、どのドライブか決められないもの
                    return (false, UndecidableVolumeReason);
                }

                var drive = new DriveInfo(root);

                if (!drive.IsReady)
                {
                    // 媒体が入っていない・つながっていないドライブはファイルシステム名を読めない
                    return (false, UndecidableVolumeReason);
                }

                if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable)
                {
                    return (false, $"対象のドライブの種類（{drive.DriveType}）ではボリュームの目録を読めないため、通常の走査を選びました。");
                }

                string fileSystem = drive.DriveFormat;

                if (!string.Equals(fileSystem, NtfsFileSystemName, StringComparison.OrdinalIgnoreCase))
                {
                    return (false, $"対象のドライブのファイルシステムが NTFS ではない（{fileSystem}）ため、通常の走査を選びました。");
                }

                return (true, string.Empty);
            }
            catch (Exception)
            {
                // 意図して無視: 存在しないドライブや扱えない形のパスは判定できないだけで、走査は続けられる。
                // 判定の失敗で走査を止めないよう、通常の走査に倒す
                return (false, UndecidableVolumeReason);
            }
        }
    }
}
