using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace LargeFolderFinder.ScanBench;

/// <summary>
/// ボリュームの目録を <c>FSCTL_QUERY_FILE_LAYOUT</c> でまとめて読む方式が
/// 実際に使えるかどうかを確かめるための、早い段階の確認用の入口
/// （tasks.md 1.2。<c>ScanBench layout-probe</c> 下位コマンドの本体）。
/// </summary>
/// <remarks>
/// <para>
/// <b>この部品は使い捨てである。</b> tasks.md 5.1 で本来の読み取りの部品
/// （<c>Services/VolumeLayoutReader.cs</c> と <c>Helpers/Win32Volume.cs</c>）ができ、
/// 6.1 で計測の道具に方式の指定が入った時点で、このファイルと
/// <c>Program</c> 側の下位コマンドの分岐を<b>取り除く</b>。
/// 重複した OS の直接呼び出しを本体と道具の両方に残さないための約束である。
/// </para>
/// <para>
/// ボリュームは<b>読み取りだけ</b>で開く。書き込みの呼び出しは使わない。
/// 列挙で得た名前は経路の組み立てと <see cref="FileInfo.Length"/> との突き合わせにだけ使い、
/// 画面にも記録にも出さない（公開リポジトリのため。出すのは件数・時間・型の情報だけ）。
/// </para>
/// <para>
/// 定数は Windows SDK のヘッダ（<c>um/winioctl.h</c>、<c>um/winnt.h</c>、<c>um/fileapi.h</c>、
/// <c>um/WinBase.h</c>）を直接読んで写した。とくに <c>QUERY_FILE_LAYOUT_*</c> のフラグは
/// 資料によって値の食い違いがあるため、ヘッダの値を唯一の根拠とする。
/// </para>
/// </remarks>
internal static class LayoutProbe
{
    // ---- CreateFileW / DeviceIoControl の定数（winnt.h / fileapi.h / WinBase.h から確認） ----

    /// <summary>ファイルの中身を読む権利（winnt.h: FILE_READ_DATA）。</summary>
    private const uint FileReadData = 0x0001;

    /// <summary>ファイルの属性だけを読む権利（winnt.h: FILE_READ_ATTRIBUTES）。</summary>
    private const uint FileReadAttributes = 0x0080;

    /// <summary>共有の指定（winnt.h: FILE_SHARE_READ | FILE_SHARE_WRITE）。</summary>
    private const uint FileShareReadWrite = 0x00000001 | 0x00000002;

    /// <summary>既にあるものだけを開く（fileapi.h: OPEN_EXISTING）。</summary>
    private const uint OpenExisting = 3;

    /// <summary>フォルダをハンドルとして開くための指定（WinBase.h: FILE_FLAG_BACKUP_SEMANTICS）。</summary>
    private const uint FileFlagBackupSemantics = 0x02000000;

    // ---- FSCTL_QUERY_FILE_LAYOUT（winioctl.h から確認） ----

    /// <summary>winioctl.h: FILE_DEVICE_FILE_SYSTEM。</summary>
    private const uint FileDeviceFileSystem = 0x00000009;

    /// <summary>winioctl.h: METHOD_NEITHER。</summary>
    private const uint MethodNeither = 3;

    /// <summary>winioctl.h の関数番号。<c>CTL_CODE(FILE_DEVICE_FILE_SYSTEM, 157, METHOD_NEITHER, FILE_ANY_ACCESS)</c>。</summary>
    private const uint QueryFileLayoutFunction = 157;

    /// <summary>
    /// <c>FSCTL_QUERY_FILE_LAYOUT</c> の制御コード。
    /// <c>CTL_CODE</c> の定義（<c>(DeviceType &lt;&lt; 16) | (Access &lt;&lt; 14) | (Function &lt;&lt; 2) | Method</c>）に
    /// winioctl.h の値を当てはめて求めた 0x00090277。
    /// </summary>
    private const uint FsctlQueryFileLayout =
        (FileDeviceFileSystem << 16) | (QueryFileLayoutFunction << 2) | MethodNeither;

    /// <summary>winioctl.h: QUERY_FILE_LAYOUT_RESTART。内部の位置を先頭に戻す。</summary>
    private const uint QueryFileLayoutRestart = 0x00000001;

    /// <summary>winioctl.h: QUERY_FILE_LAYOUT_INCLUDE_NAMES。</summary>
    private const uint QueryFileLayoutIncludeNames = 0x00000002;

    /// <summary>winioctl.h: QUERY_FILE_LAYOUT_INCLUDE_STREAMS。</summary>
    private const uint QueryFileLayoutIncludeStreams = 0x00000004;

    /// <summary>winioctl.h: QUERY_FILE_LAYOUT_INCLUDE_EXTRA_INFO。</summary>
    private const uint QueryFileLayoutIncludeExtraInfo = 0x00000010;

    /// <summary>winioctl.h: QUERY_FILE_LAYOUT_INCLUDE_STREAMS_WITH_NO_CLUSTERS_ALLOCATED。</summary>
    private const uint QueryFileLayoutIncludeStreamsWithNoClustersAllocated = 0x00000020;

    /// <summary>winioctl.h: QUERY_FILE_LAYOUT_FILTER_TYPE_NONE。ボリューム全体を返す。</summary>
    private const uint QueryFileLayoutFilterTypeNone = 0;

    /// <summary>winioctl.h: FILE_LAYOUT_NAME_ENTRY_DOS。8.3 の短縮名の印。</summary>
    private const uint FileLayoutNameEntryDos = 0x00000002;

    /// <summary>winioctl.h: STREAM_LAYOUT_ENTRY_RESIDENT。目録の中に実体が入っているストリーム。</summary>
    private const uint StreamLayoutEntryResident = 0x00000004;

    /// <summary>NTFS の <c>$DATA</c> 属性の種別コード。</summary>
    private const uint AttributeTypeCodeData = 0x80;

    /// <summary>NTFS の <c>$REPARSE_POINT</c> 属性の種別コード。</summary>
    private const uint AttributeTypeCodeReparsePoint = 0xC0;

    /// <summary>
    /// 比べるための控えの呼び出し（winioctl.h: FSCTL_IS_VOLUME_MOUNTED、
    /// <c>CTL_CODE(FILE_DEVICE_FILE_SYSTEM, 10, METHOD_BUFFERED, FILE_ANY_ACCESS)</c> = 0x00090028）。
    /// ハンドルがファイルシステムに届いていることを確かめるために使う。
    /// </summary>
    private const uint FsctlIsVolumeMounted = (FileDeviceFileSystem << 16) | (10u << 2);

    /// <summary>
    /// 比べるための控えの呼び出し（winioctl.h: FSCTL_GET_NTFS_VOLUME_DATA、
    /// <c>CTL_CODE(FILE_DEVICE_FILE_SYSTEM, 25, METHOD_BUFFERED, FILE_ANY_ACCESS)</c> = 0x00090064）。
    /// 生の目録の解析に必要な呼び出しで、どの権限まで通るかを一緒に見る。
    /// </summary>
    private const uint FsctlGetNtfsVolumeData = (FileDeviceFileSystem << 16) | (25u << 2);

    /// <summary>winnt.h: SYNCHRONIZE。</summary>
    private const uint Synchronize = 0x00100000;

    /// <summary>winnt.h: READ_CONTROL。</summary>
    private const uint ReadControl = 0x00020000;

    /// <summary>winnt.h: GENERIC_READ。</summary>
    private const uint GenericRead = 0x80000000;

    /// <summary>winnt.h: MAXIMUM_ALLOWED。呼び出し側に許される権利をすべて要求する。</summary>
    private const uint MaximumAllowed = 0x02000000;

    /// <summary>winioctl.h: QUERY_FILE_LAYOUT_INCLUDE_EXTENTS。</summary>
    private const uint QueryFileLayoutIncludeExtents = 0x00000008;

    /// <summary>winioctl.h: QUERY_FILE_LAYOUT_FILTER_TYPE_FILEID。ファイル参照番号の範囲で絞る。</summary>
    private const uint QueryFileLayoutFilterTypeFileId = 2;

    /// <summary>列挙の終わりを表すエラー（ERROR_HANDLE_EOF）。</summary>
    private const int ErrorHandleEof = 38;

    /// <summary>列挙の終わりとして返ることがあるエラー（ERROR_NO_MORE_FILES）。</summary>
    private const int ErrorNoMoreFiles = 18;

    // ---- 出力の構造体の大きさと位置（winioctl.h の定義から、x64 の詰め方で求めた） ----

    /// <summary><c>QUERY_FILE_LAYOUT_INPUT</c> の大きさ（フィルタなしでも同じ大きさを渡す）。</summary>
    private const int QueryFileLayoutInputSize = 32;

    /// <summary><c>QUERY_FILE_LAYOUT_OUTPUT</c> の大きさ。</summary>
    private const int QueryFileLayoutOutputSize = 16;

    /// <summary>ルートのフォルダのファイル参照番号の下位48ビット（目録の索引）。</summary>
    private const ulong RootFileIndex = 5;

    /// <summary>ファイル参照番号の下位48ビットを取り出す覆い。</summary>
    private const ulong FileIndexMask = 0x0000FFFFFFFFFFFFUL;

    /// <summary>ファイルシステムが自分の管理に使う予約された目録の索引の数。</summary>
    private const ulong ReservedFileIndexCount = 16;

    /// <summary>1件も返せないことを避けるための、列挙の受け皿の既定の大きさ（バイト）。</summary>
    private const int DefaultOutputBufferSize = 4 * 1024 * 1024;

    /// <summary>種類ごとに <see cref="FileInfo.Length"/> と突き合わせる既定の件数。</summary>
    private const int DefaultSampleCount = 25;

    /// <summary>完走したことを表す終了コード。</summary>
    private const int ExitCodeSuccess = 0;

    /// <summary>目録の読み取りができなかったことを表す終了コード。</summary>
    private const int ExitCodeLayoutUnavailable = 1;

    /// <summary>引数の誤りを表す終了コード。</summary>
    private const int ExitCodeInvalidUsage = 2;

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out ByHandleFileInformation lpFileInformation);

    /// <summary><c>GetFileInformationByHandle</c> が返す情報。必要な項目だけ名前を付けている。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    /// <summary>試した「開き方 × 権限」の1組と、その結果。</summary>
    private sealed class AccessTrial
    {
        public AccessTrial(string targetLabel, bool openRootDirectory, string accessLabel, uint desiredAccess)
        {
            TargetLabel = targetLabel;
            OpenRootDirectory = openRootDirectory;
            AccessLabel = accessLabel;
            DesiredAccess = desiredAccess;
        }

        /// <summary>何を開いたかの説明（ボリュームのデバイスか、ルートのフォルダか）。</summary>
        public string TargetLabel { get; }

        /// <summary>ルートのフォルダとして開くか（<c>false</c> ならボリュームのデバイス）。</summary>
        public bool OpenRootDirectory { get; }

        /// <summary>出力に出す権限の説明。</summary>
        public string AccessLabel { get; }

        /// <summary><c>CreateFileW</c> に渡す権限。</summary>
        public uint DesiredAccess { get; }

        /// <summary>ハンドルを開けたか。</summary>
        public bool Opened { get; set; }

        /// <summary>開けなかったときのエラー番号。</summary>
        public int OpenError { get; set; }

        /// <summary>控えの呼び出し（FSCTL_IS_VOLUME_MOUNTED）が通ったか。</summary>
        public bool ControlSucceeded { get; set; }

        /// <summary>控えの呼び出しが通らなかったときのエラー番号。</summary>
        public int ControlError { get; set; }

        /// <summary>生の目録の解析に使う呼び出し（FSCTL_GET_NTFS_VOLUME_DATA）が通ったか。</summary>
        public bool VolumeDataSucceeded { get; set; }

        /// <summary>上記が通らなかったときのエラー番号。</summary>
        public int VolumeDataError { get; set; }

        /// <summary>開けたハンドルで目録の列挙を始められたか。</summary>
        public bool LayoutSucceeded { get; set; }

        /// <summary>列挙を始められなかったときのエラー番号。</summary>
        public int LayoutError { get; set; }

        /// <summary>最初の1回の呼び出しで返った件数。</summary>
        public int FirstBatchEntries { get; set; }
    }

    /// <summary>突き合わせのために取っておく、目録の1件の控え。</summary>
    private readonly struct SampleEntry
    {
        public SampleEntry(ulong parentFileId, string name, long endOfFile, long allocationSize)
        {
            ParentFileId = parentFileId;
            Name = name;
            EndOfFile = endOfFile;
            AllocationSize = allocationSize;
        }

        /// <summary>親のファイル参照番号。経路の組み立てに使う。</summary>
        public ulong ParentFileId { get; }

        /// <summary>名前。経路の組み立てにだけ使い、出力には出さない。</summary>
        public string Name { get; }

        /// <summary>無名のストリームの論理サイズ。</summary>
        public long EndOfFile { get; }

        /// <summary>無名のストリームの確保サイズ。</summary>
        public long AllocationSize { get; }
    }

    /// <summary>ボリューム全体の列挙で集めた数え上げの結果。</summary>
    private sealed class LayoutStatistics
    {
        /// <summary>列挙で返った項目の総数。</summary>
        public long TotalEntries;

        /// <summary>フォルダの数。</summary>
        public long DirectoryCount;

        /// <summary>ファイルの数。</summary>
        public long FileCount;

        /// <summary>予約された目録の索引（ルートを除く）の数。</summary>
        public long ReservedCount;

        /// <summary>短縮名を除いた名前が2つ以上ある項目（ハードリンク）の数。</summary>
        public long HardLinkedCount;

        /// <summary>短縮名の総数。</summary>
        public long DosNameCount;

        /// <summary>短縮名を除いた名前の総数。</summary>
        public long RealNameCount;

        /// <summary>リパースポイントの数。</summary>
        public long ReparsePointCount;

        /// <summary>圧縮された項目の数。</summary>
        public long CompressedCount;

        /// <summary>まばら（スパース）な項目の数。</summary>
        public long SparseCount;

        /// <summary>無名のストリームが目録の中に入っていた項目の数。</summary>
        public long ResidentCount;

        /// <summary>無名のストリームが返らなかったファイルの数。</summary>
        public long FilesWithoutUnnamedStream;

        /// <summary>リパースポイントの属性を持つ項目の数。</summary>
        public long ReparseStreamCount;

        /// <summary>名前付きのストリーム（ADS）の総数。</summary>
        public long NamedDataStreamCount;

        /// <summary>無名のストリームの論理サイズの合計。</summary>
        public long TotalLogicalSize;

        /// <summary>列挙に要した呼び出しの回数。</summary>
        public int IoctlCalls;

        /// <summary>更新日時が取れた項目があったか。</summary>
        public bool ExtraInfoSeen;
    }

    /// <summary>
    /// <c>layout-probe</c> 下位コマンドの入口。
    /// </summary>
    /// <param name="args">下位コマンドの名前を除いた引数。</param>
    /// <returns>終了コード。</returns>
    public static int Run(string[] args)
    {
        string? driveArgument = null;
        int sampleCount = DefaultSampleCount;
        int bufferSize = DefaultOutputBufferSize;
        bool accessOnly = false;
        bool denyTest = true;

        for (int i = 0; i < args.Length; i++)
        {
            string current = args[i];
            switch (current)
            {
                case "--sample":
                case "--buffer":
                    if (i + 1 >= args.Length || !int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value <= 0)
                    {
                        Console.Error.WriteLine($"{current} には正の整数を指定してください。");
                        return ExitCodeInvalidUsage;
                    }

                    if (current == "--sample")
                    {
                        sampleCount = value;
                    }
                    else
                    {
                        bufferSize = value;
                    }

                    i++;
                    break;

                case "--access-only":
                    accessOnly = true;
                    break;

                case "--no-deny-test":
                    denyTest = false;
                    break;

                default:
                    if (current.StartsWith("--", StringComparison.Ordinal) || driveArgument is not null)
                    {
                        Console.Error.WriteLine($"引数を解釈できません: {current}");
                        return ExitCodeInvalidUsage;
                    }

                    driveArgument = current;
                    break;
            }
        }

        if (driveArgument is null)
        {
            Console.Error.WriteLine("対象のドライブを指定してください（例: C:）。");
            return ExitCodeInvalidUsage;
        }

        string? driveLetter = NormalizeDriveLetter(driveArgument);
        if (driveLetter is null)
        {
            Console.Error.WriteLine("対象はローカルのドライブ（例: C:）で指定してください。");
            return ExitCodeInvalidUsage;
        }

        Console.WriteLine("=== 目録の読み取りの確認（FSCTL_QUERY_FILE_LAYOUT） ===");
        Console.WriteLine($"対象のドライブ: {driveLetter}:");
        Console.WriteLine($"いま管理者か: {(IsElevated() ? "はい" : "いいえ")}");
        Console.WriteLine($"ファイルシステム: {TryGetFileSystemName(driveLetter)}");
        Console.WriteLine($"制御コード FSCTL_QUERY_FILE_LAYOUT: 0x{FsctlQueryFileLayout:X8}");
        Console.WriteLine();

        IReadOnlyList<AccessTrial> trials = RunAccessTrials(driveLetter);
        PrintAccessTrials(trials);

        AccessTrial? usable = null;
        foreach (AccessTrial trial in trials)
        {
            if (trial.LayoutSucceeded)
            {
                usable = trial;
                break;
            }
        }

        if (usable is null)
        {
            Console.WriteLine();
            Console.WriteLine("どの組み合わせでも目録の列挙を始められなかった。入力の作り方を変えて切り分ける。");
            RunInputVariants(driveLetter);
            Console.WriteLine();
            Console.WriteLine("結論: どの組み合わせでも目録の列挙を始められなかった。");
            return ExitCodeLayoutUnavailable;
        }

        Console.WriteLine();
        Console.WriteLine($"結論: 最初に成功したのは「{usable.TargetLabel} / {usable.AccessLabel}」。以降の確認はこの組み合わせで行う。");

        if (accessOnly)
        {
            return ExitCodeSuccess;
        }

        return RunFullProbe(driveLetter, usable, sampleCount, bufferSize, denyTest);
    }

    /// <summary>
    /// 権限の組み合わせを弱いものから順に試し、それぞれでボリュームを開いて
    /// 目録の列挙を始められるかどうかを確かめる。
    /// </summary>
    private static IReadOnlyList<AccessTrial> RunAccessTrials(string driveLetter)
    {
        // 「何を開くか」×「どの権限で開くか」を総当たりで試す。
        // 目録の列挙が通るために必要な最小の条件を、思い込みでなく実測で決めるため。
        (string Label, bool Root)[] targets =
        {
            (@"ボリュームのデバイス \\.\X:", false),
            (@"ルートのフォルダ X:\", true),
        };

        (string Label, uint Value)[] accesses =
        {
            ("0（権限なし）", 0),
            ("SYNCHRONIZE", Synchronize),
            ("READ_CONTROL", ReadControl),
            ("FILE_READ_ATTRIBUTES", FileReadAttributes),
            ("FILE_READ_ATTRIBUTES|SYNCHRONIZE", FileReadAttributes | Synchronize),
            ("FILE_READ_DATA", FileReadData),
            ("FILE_READ_DATA|FILE_READ_ATTRIBUTES|SYNCHRONIZE", FileReadData | FileReadAttributes | Synchronize),
            ("GENERIC_READ", GenericRead),
            ("MAXIMUM_ALLOWED", MaximumAllowed),
        };

        List<AccessTrial> trials = new List<AccessTrial>();
        foreach ((string targetLabel, bool root) in targets)
        {
            foreach ((string accessLabel, uint access) in accesses)
            {
                trials.Add(new AccessTrial(targetLabel, root, accessLabel, access));
            }
        }

        string devicePath = @"\\.\" + driveLetter + ":";
        string rootPath = driveLetter + @":\";
        byte[] outputBuffer = new byte[1024 * 1024];

        foreach (AccessTrial trial in trials)
        {
            using SafeFileHandle handle = CreateFileW(
                trial.OpenRootDirectory ? rootPath : devicePath,
                trial.DesiredAccess,
                FileShareReadWrite,
                IntPtr.Zero,
                OpenExisting,
                trial.OpenRootDirectory ? FileFlagBackupSemantics : 0,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                trial.OpenError = Marshal.GetLastWin32Error();
                continue;
            }

            trial.Opened = true;

            // ハンドルがファイルシステムまで届いていることを、単純な呼び出しで先に確かめる。
            if (TrySimpleFsctl(handle, FsctlIsVolumeMounted, out int controlError))
            {
                trial.ControlSucceeded = true;
            }
            else
            {
                trial.ControlError = controlError;
            }

            // 生の目録の解析に使う呼び出しも一緒に見る（どの権限まで通るかの比較のため）。
            if (TrySimpleFsctl(handle, FsctlGetNtfsVolumeData, out int volumeDataError))
            {
                trial.VolumeDataSucceeded = true;
            }
            else
            {
                trial.VolumeDataError = volumeDataError;
            }

            // 本命。開けただけでは意味がないので、その場で1回だけ列挙を試す。
            if (TryQueryOnce(handle, outputBuffer, includeRestart: true, out int entries, out int error))
            {
                trial.LayoutSucceeded = true;
                trial.FirstBatchEntries = entries;
            }
            else
            {
                trial.LayoutError = error;
            }
        }

        return trials;
    }

    /// <summary>
    /// 目録の列挙が失敗したとき、入力の作り方（指定の組み合わせ・入力の大きさ・絞り込みの種類・
    /// 受け皿の大きさ）を変えて総当たりで試し、失敗の原因が権限なのか入力なのかを切り分ける。
    /// </summary>
    private static void RunInputVariants(string driveLetter)
    {
        (string Label, string Path, uint Flags)[] handles =
        {
            (@"ボリュームのデバイス \\.\X:", @"\\.\" + driveLetter + ":", 0),
            (@"ルートのフォルダ X:\", driveLetter + @":\", FileFlagBackupSemantics),
        };

        uint full =
            QueryFileLayoutRestart |
            QueryFileLayoutIncludeNames |
            QueryFileLayoutIncludeStreams |
            QueryFileLayoutIncludeExtraInfo |
            QueryFileLayoutIncludeStreamsWithNoClustersAllocated;

        (string Label, uint Flags, uint FilterType, uint FilterCount, bool FileIdFilter, int InputSize, int OutputSize)[] variants =
        {
            ("RESTART のみ", QueryFileLayoutRestart, QueryFileLayoutFilterTypeNone, 0, false, 32, 1 << 20),
            ("RESTART|NAMES", QueryFileLayoutRestart | QueryFileLayoutIncludeNames, QueryFileLayoutFilterTypeNone, 0, false, 32, 1 << 20),
            ("RESTART|NAMES|STREAMS", QueryFileLayoutRestart | QueryFileLayoutIncludeNames | QueryFileLayoutIncludeStreams, QueryFileLayoutFilterTypeNone, 0, false, 32, 1 << 20),
            ("既定の組み合わせ", full, QueryFileLayoutFilterTypeNone, 0, false, 32, 1 << 20),
            ("既定＋EXTENTS", full | QueryFileLayoutIncludeExtents, QueryFileLayoutFilterTypeNone, 0, false, 32, 1 << 20),
            ("既定・入力16バイト", full, QueryFileLayoutFilterTypeNone, 0, false, 16, 1 << 20),
            ("既定・入力40バイト", full, QueryFileLayoutFilterTypeNone, 0, false, 40, 1 << 20),
            ("既定・受け皿64KiB", full, QueryFileLayoutFilterTypeNone, 0, false, 32, 64 * 1024),
            ("既定・受け皿16MiB", full, QueryFileLayoutFilterTypeNone, 0, false, 32, 16 << 20),
            ("参照番号の範囲で絞る", full, QueryFileLayoutFilterTypeFileId, 1, true, 32, 1 << 20),
        };

        byte[] outputBuffer = new byte[16 << 20];

        Console.WriteLine();
        Console.WriteLine("開き方\t入力の作り方\t結果");
        foreach ((string handleLabel, string path, uint createFlags) in handles)
        {
            using SafeFileHandle handle = CreateFileW(
                path,
                MaximumAllowed,
                FileShareReadWrite,
                IntPtr.Zero,
                OpenExisting,
                createFlags,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                Console.WriteLine($"{handleLabel}\t-\t開けなかった（{DescribeError(Marshal.GetLastWin32Error())}）");
                continue;
            }

            foreach ((string variantLabel, uint flags, uint filterType, uint filterCount, bool fileIdFilter, int inputSize, int outputSize) in variants)
            {
                bool ok = TryQueryRaw(
                    handle,
                    flags,
                    filterType,
                    filterCount,
                    fileIdFilter,
                    inputSize,
                    outputBuffer,
                    outputSize,
                    out int entries,
                    out int error);
                string result = ok ? $"成功（{entries} 件）" : $"失敗（{DescribeError(error)}）";
                Console.WriteLine($"{handleLabel}\t{variantLabel}\t{result}");
            }
        }
    }

    /// <summary>入力を持たない単純な FSCTL を1回呼ぶ（ハンドルの素性を確かめるため）。</summary>
    private static bool TrySimpleFsctl(SafeFileHandle handle, uint controlCode, out int error)
    {
        error = 0;
        byte[] outputBuffer = new byte[256];
        GCHandle pin = GCHandle.Alloc(outputBuffer, GCHandleType.Pinned);
        try
        {
            bool ok = DeviceIoControl(
                handle,
                controlCode,
                IntPtr.Zero,
                0,
                pin.AddrOfPinnedObject(),
                (uint)outputBuffer.Length,
                out _,
                IntPtr.Zero);
            if (!ok)
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }

            return true;
        }
        finally
        {
            pin.Free();
        }
    }

    /// <summary>試した組み合わせの結果を表に出す。</summary>
    private static void PrintAccessTrials(IReadOnlyList<AccessTrial> trials)
    {
        Console.WriteLine("開き方\t権限\t開く\tIS_VOLUME_MOUNTED\tGET_NTFS_VOLUME_DATA\tQUERY_FILE_LAYOUT\t備考");
        foreach (AccessTrial trial in trials)
        {
            string openResult = trial.Opened ? "成功" : $"失敗（{DescribeError(trial.OpenError)}）";
            string controlResult = !trial.Opened
                ? "-"
                : trial.ControlSucceeded ? "成功" : $"失敗（{DescribeError(trial.ControlError)}）";
            string volumeDataResult = !trial.Opened
                ? "-"
                : trial.VolumeDataSucceeded ? "成功" : $"失敗（{DescribeError(trial.VolumeDataError)}）";
            string layoutResult = !trial.Opened
                ? "-"
                : trial.LayoutSucceeded ? "成功" : $"失敗（{DescribeError(trial.LayoutError)}）";
            string note = trial.LayoutSucceeded
                ? $"最初の1回で {trial.FirstBatchEntries} 件"
                : string.Empty;
            Console.WriteLine($"{trial.TargetLabel}\t{trial.AccessLabel}\t{openResult}\t{controlResult}\t{volumeDataResult}\t{layoutResult}\t{note}");
        }
    }

    /// <summary>
    /// ボリューム全体の列挙を1回だけ行い、所要時間・件数・取れた値・
    /// <see cref="FileInfo.Length"/> との一致・アクセス権の無い場所の扱いを確かめる。
    /// </summary>
    private static int RunFullProbe(string driveLetter, AccessTrial usable, int sampleCount, int bufferSize, bool denyTest)
    {
        string volumeRoot = driveLetter + @":\";

        // アクセス権の無いフォルダの確認は、1回の列挙で済ませるために先に用意しておく。
        DenyFixture? fixture = null;
        if (denyTest)
        {
            fixture = DenyFixture.TryCreate(volumeRoot);
        }

        try
        {
            using SafeFileHandle handle = CreateFileW(
                usable.OpenRootDirectory ? volumeRoot : @"\\.\" + driveLetter + ":",
                usable.DesiredAccess,
                FileShareReadWrite,
                IntPtr.Zero,
                OpenExisting,
                usable.OpenRootDirectory ? FileFlagBackupSemantics : 0,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                Console.Error.WriteLine($"ボリュームを開き直せませんでした（{DescribeError(Marshal.GetLastWin32Error())}）。");
                return ExitCodeLayoutUnavailable;
            }

            LayoutStatistics statistics = new LayoutStatistics();
            Dictionary<ulong, (string Name, ulong Parent)> directories = new Dictionary<ulong, (string, ulong)>(capacity: 1 << 19);
            Dictionary<string, List<SampleEntry>> samples = new Dictionary<string, List<SampleEntry>>(StringComparer.Ordinal)
            {
                ["通常のファイル"] = new List<SampleEntry>(),
                ["目録の中に実体があるファイル"] = new List<SampleEntry>(),
                ["リパースポイント"] = new List<SampleEntry>(),
                ["圧縮されたファイル"] = new List<SampleEntry>(),
            };
            List<SampleEntry> deniedChildren = new List<SampleEntry>();

            Stopwatch stopwatch = Stopwatch.StartNew();
            string? failure = Enumerate(
                handle,
                bufferSize,
                statistics,
                directories,
                samples,
                sampleCount,
                fixture?.DirectoryFileId ?? 0,
                deniedChildren);
            stopwatch.Stop();

            if (failure is not null)
            {
                Console.Error.WriteLine(failure);
                return ExitCodeLayoutUnavailable;
            }

            PrintStatistics(statistics, stopwatch.Elapsed, directories.Count);
            PrintSampleComparison(volumeRoot, directories, samples);
            PrintDenyResult(fixture, deniedChildren);
            return ExitCodeSuccess;
        }
        finally
        {
            fixture?.Dispose();
        }
    }

    /// <summary>
    /// ボリューム全体を最後まで列挙し、数え上げと控えを集める。
    /// </summary>
    /// <returns>失敗したときの説明。最後まで読めたら <c>null</c>。</returns>
    private static string? Enumerate(
        SafeFileHandle handle,
        int bufferSize,
        LayoutStatistics statistics,
        Dictionary<ulong, (string Name, ulong Parent)> directories,
        Dictionary<string, List<SampleEntry>> samples,
        int sampleCount,
        ulong deniedDirectoryFileId,
        List<SampleEntry> deniedChildren)
    {
        byte[] outputBuffer = new byte[bufferSize];
        bool restart = true;

        while (true)
        {
            if (!TryQueryOnce(handle, outputBuffer, restart, out _, out int error))
            {
                if (error == ErrorHandleEof || error == ErrorNoMoreFiles)
                {
                    return null;
                }

                return $"列挙の途中で失敗しました（{DescribeError(error)}）。{statistics.TotalEntries} 件まで読めていました。";
            }

            restart = false;
            statistics.IoctlCalls++;

            // 通常、列挙の終わりは失敗（ERROR_HANDLE_EOF）として返る。
            // 万一「成功したが0件」が続くと終わらなくなるため、その形でも打ち切る。
            if (ReadUInt32(outputBuffer, 0) == 0)
            {
                return null;
            }

            string? parseFailure = ParseBatch(
                outputBuffer,
                statistics,
                directories,
                samples,
                sampleCount,
                deniedDirectoryFileId,
                deniedChildren);
            if (parseFailure is not null)
            {
                return parseFailure;
            }
        }
    }

    /// <summary>
    /// <c>FSCTL_QUERY_FILE_LAYOUT</c> を1回だけ呼ぶ。
    /// </summary>
    /// <returns>呼び出しが成功したら <c>true</c>。</returns>
    private static bool TryQueryOnce(SafeFileHandle handle, byte[] outputBuffer, bool includeRestart, out int entries, out int error)
    {
        uint flags =
            QueryFileLayoutIncludeNames |
            QueryFileLayoutIncludeStreams |
            QueryFileLayoutIncludeExtraInfo |
            QueryFileLayoutIncludeStreamsWithNoClustersAllocated;
        if (includeRestart)
        {
            flags |= QueryFileLayoutRestart;
        }

        return TryQueryRaw(
            handle,
            flags,
            QueryFileLayoutFilterTypeNone,
            filterEntryCount: 0,
            useFileIdFilter: false,
            inputSize: QueryFileLayoutInputSize,
            outputBuffer,
            outputSize: outputBuffer.Length,
            out entries,
            out error);
    }

    /// <summary>
    /// <c>FSCTL_QUERY_FILE_LAYOUT</c> を、入力の作り方を細かく指定して1回だけ呼ぶ。
    /// 失敗の原因が権限なのか入力の作り方なのかを切り分けるために使う。
    /// </summary>
    private static bool TryQueryRaw(
        SafeFileHandle handle,
        uint flags,
        uint filterType,
        uint filterEntryCount,
        bool useFileIdFilter,
        int inputSize,
        byte[] outputBuffer,
        int outputSize,
        out int entries,
        out int error)
    {
        entries = 0;
        error = 0;

        byte[] inputBuffer = new byte[Math.Max(inputSize, QueryFileLayoutInputSize)];
        WriteUInt32(inputBuffer, 0, filterEntryCount);  // FilterEntryCount / NumberOfPairs
        WriteUInt32(inputBuffer, 4, flags);             // Flags
        WriteUInt32(inputBuffer, 8, filterType);        // FilterType
        WriteUInt32(inputBuffer, 12, 0);                // Reserved
        if (useFileIdFilter)
        {
            // FILE_REFERENCE_RANGE を1つ置き、ボリューム上のすべての参照番号を対象にする。
            WriteUInt64(inputBuffer, 16, 0);
            WriteUInt64(inputBuffer, 24, ulong.MaxValue);
        }

        GCHandle inputPin = GCHandle.Alloc(inputBuffer, GCHandleType.Pinned);
        GCHandle outputPin = GCHandle.Alloc(outputBuffer, GCHandleType.Pinned);
        try
        {
            bool ok = DeviceIoControl(
                handle,
                FsctlQueryFileLayout,
                inputPin.AddrOfPinnedObject(),
                (uint)inputSize,
                outputPin.AddrOfPinnedObject(),
                (uint)outputSize,
                out _,
                IntPtr.Zero);

            if (!ok)
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }
        }
        finally
        {
            outputPin.Free();
            inputPin.Free();
        }

        entries = (int)ReadUInt32(outputBuffer, 0);
        return true;
    }

    /// <summary>
    /// 1回の呼び出しで返った塊を読み解き、数え上げと控えに反映する。
    /// </summary>
    /// <returns>読み解きに失敗したときの説明。成功したら <c>null</c>。</returns>
    private static string? ParseBatch(
        byte[] buffer,
        LayoutStatistics statistics,
        Dictionary<ulong, (string Name, ulong Parent)> directories,
        Dictionary<string, List<SampleEntry>> samples,
        int sampleCount,
        ulong deniedDirectoryFileId,
        List<SampleEntry> deniedChildren)
    {
        uint fileEntryCount = ReadUInt32(buffer, 0);
        uint firstFileOffset = ReadUInt32(buffer, 4);
        if (fileEntryCount == 0 || firstFileOffset == 0)
        {
            return null;
        }

        int entryOffset = checked((int)firstFileOffset);
        for (uint index = 0; index < fileEntryCount; index++)
        {
            if (entryOffset < QueryFileLayoutOutputSize || entryOffset + 40 > buffer.Length)
            {
                return $"目録の読み解きで位置が範囲を外れました（項目の位置 {entryOffset}）。構造体の読み方が誤っている可能性があります。";
            }

            uint nextFileOffset = ReadUInt32(buffer, entryOffset + 4);
            uint fileAttributes = ReadUInt32(buffer, entryOffset + 12);
            ulong fileId = ReadUInt64(buffer, entryOffset + 16);
            uint firstNameOffset = ReadUInt32(buffer, entryOffset + 24);
            uint firstStreamOffset = ReadUInt32(buffer, entryOffset + 28);
            uint extraInfoOffset = ReadUInt32(buffer, entryOffset + 32);

            statistics.TotalEntries++;
            bool isDirectory = (fileAttributes & (uint)FileAttributes.Directory) != 0;
            bool isReparsePoint = (fileAttributes & (uint)FileAttributes.ReparsePoint) != 0;
            bool isCompressed = (fileAttributes & (uint)FileAttributes.Compressed) != 0;
            bool isSparse = (fileAttributes & (uint)FileAttributes.SparseFile) != 0;

            if (isDirectory)
            {
                statistics.DirectoryCount++;
            }
            else
            {
                statistics.FileCount++;
            }

            if (isReparsePoint)
            {
                statistics.ReparsePointCount++;
            }

            if (isCompressed)
            {
                statistics.CompressedCount++;
            }

            if (isSparse)
            {
                statistics.SparseCount++;
            }

            ulong fileIndex = fileId & FileIndexMask;
            if (fileIndex < ReservedFileIndexCount && fileIndex != RootFileIndex)
            {
                statistics.ReservedCount++;
            }

            if (extraInfoOffset != 0)
            {
                statistics.ExtraInfoSeen = true;
            }

            // --- 名前（複数＝ハードリンク、短縮名は印つき） ---
            string? primaryName = null;
            ulong primaryParent = 0;
            int realNamesForThisEntry = 0;
            if (firstNameOffset != 0)
            {
                int nameOffset = entryOffset + checked((int)firstNameOffset);
                while (true)
                {
                    if (nameOffset < 0 || nameOffset + 24 > buffer.Length)
                    {
                        return $"名前の読み解きで位置が範囲を外れました（名前の位置 {nameOffset}）。";
                    }

                    uint nextNameOffset = ReadUInt32(buffer, nameOffset + 0);
                    uint nameFlags = ReadUInt32(buffer, nameOffset + 4);
                    ulong parentFileId = ReadUInt64(buffer, nameOffset + 8);
                    int nameLength = checked((int)ReadUInt32(buffer, nameOffset + 16));

                    if (nameOffset + 24 + nameLength > buffer.Length)
                    {
                        return $"名前の長さが範囲を外れました（長さ {nameLength}）。";
                    }

                    bool isDosName = (nameFlags & FileLayoutNameEntryDos) != 0;
                    if (isDosName)
                    {
                        statistics.DosNameCount++;
                    }
                    else
                    {
                        statistics.RealNameCount++;
                        realNamesForThisEntry++;
                        if (primaryName is null)
                        {
                            primaryName = Encoding.Unicode.GetString(buffer, nameOffset + 24, nameLength);
                            primaryParent = parentFileId;
                        }
                    }

                    if (nextNameOffset == 0)
                    {
                        break;
                    }

                    nameOffset += checked((int)nextNameOffset);
                }
            }

            if (realNamesForThisEntry > 1)
            {
                statistics.HardLinkedCount++;
            }

            if (isDirectory && primaryName is not null)
            {
                directories[fileId] = (primaryName, primaryParent);
            }

            // --- ストリーム（無名のストリームの論理サイズと確保サイズ） ---
            bool foundUnnamedData = false;
            long endOfFile = 0;
            long allocationSize = 0;
            bool resident = false;
            if (firstStreamOffset != 0)
            {
                int streamOffset = entryOffset + checked((int)firstStreamOffset);
                while (true)
                {
                    if (streamOffset < 0 || streamOffset + 48 > buffer.Length)
                    {
                        return $"ストリームの読み解きで位置が範囲を外れました（ストリームの位置 {streamOffset}）。";
                    }

                    uint nextStreamOffset = ReadUInt32(buffer, streamOffset + 4);
                    uint streamFlags = ReadUInt32(buffer, streamOffset + 8);
                    long streamAllocationSize = (long)ReadUInt64(buffer, streamOffset + 16);
                    long streamEndOfFile = (long)ReadUInt64(buffer, streamOffset + 24);
                    uint attributeTypeCode = ReadUInt32(buffer, streamOffset + 36);
                    int identifierLength = checked((int)ReadUInt32(buffer, streamOffset + 44));

                    if (streamOffset + 48 + identifierLength > buffer.Length)
                    {
                        return $"ストリームの名前の長さが範囲を外れました（長さ {identifierLength}）。";
                    }

                    string identifier = identifierLength > 0
                        ? Encoding.Unicode.GetString(buffer, streamOffset + 48, identifierLength)
                        : string.Empty;

                    if (attributeTypeCode == AttributeTypeCodeData)
                    {
                        // 無名のストリームの名前は "::$DATA"。名前付き（ADS）は ":名前:$DATA" になる。
                        bool unnamed = identifier.Length == 0 || identifier.StartsWith("::", StringComparison.Ordinal);
                        if (unnamed)
                        {
                            foundUnnamedData = true;
                            endOfFile = streamEndOfFile;
                            allocationSize = streamAllocationSize;
                            resident = (streamFlags & StreamLayoutEntryResident) != 0;
                        }
                        else
                        {
                            statistics.NamedDataStreamCount++;
                        }
                    }
                    else if (attributeTypeCode == AttributeTypeCodeReparsePoint)
                    {
                        statistics.ReparseStreamCount++;
                    }

                    if (nextStreamOffset == 0)
                    {
                        break;
                    }

                    streamOffset += checked((int)nextStreamOffset);
                }
            }

            if (!isDirectory)
            {
                if (foundUnnamedData)
                {
                    statistics.TotalLogicalSize += endOfFile;
                    if (resident)
                    {
                        statistics.ResidentCount++;
                    }
                }
                else
                {
                    statistics.FilesWithoutUnnamedStream++;
                }
            }

            // --- 突き合わせのための控え ---
            if (primaryName is not null && !isDirectory && foundUnnamedData)
            {
                SampleEntry sample = new SampleEntry(primaryParent, primaryName, endOfFile, allocationSize);
                string category =
                    isReparsePoint ? "リパースポイント" :
                    isCompressed ? "圧縮されたファイル" :
                    resident ? "目録の中に実体があるファイル" :
                    "通常のファイル";
                List<SampleEntry> bucket = samples[category];
                if (bucket.Count < sampleCount)
                {
                    bucket.Add(sample);
                }

                if (deniedDirectoryFileId != 0 && primaryParent == deniedDirectoryFileId)
                {
                    deniedChildren.Add(sample);
                }
            }

            if (nextFileOffset == 0)
            {
                break;
            }

            entryOffset += checked((int)nextFileOffset);
        }

        return null;
    }

    /// <summary>数え上げの結果を出す。ファイル名は出さない。</summary>
    private static void PrintStatistics(LayoutStatistics statistics, TimeSpan elapsed, int directoryMapCount)
    {
        Console.WriteLine();
        Console.WriteLine("--- ボリューム全体の列挙 ---");
        Console.WriteLine($"所要時間\t{elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture)} 秒");
        Console.WriteLine($"呼び出しの回数\t{statistics.IoctlCalls}");
        Console.WriteLine($"項目の総数\t{statistics.TotalEntries}");
        Console.WriteLine($"ファイル\t{statistics.FileCount}");
        Console.WriteLine($"フォルダ\t{statistics.DirectoryCount}");
        Console.WriteLine($"名前を取れたフォルダ\t{directoryMapCount}");
        Console.WriteLine($"予約された目録の索引（ルートを除く）\t{statistics.ReservedCount}");
        Console.WriteLine($"短縮名を除いた名前の総数\t{statistics.RealNameCount}");
        Console.WriteLine($"短縮名の総数\t{statistics.DosNameCount}");
        Console.WriteLine($"名前が2つ以上ある項目（ハードリンク）\t{statistics.HardLinkedCount}");
        Console.WriteLine($"リパースポイント\t{statistics.ReparsePointCount}");
        Console.WriteLine($"圧縮された項目\t{statistics.CompressedCount}");
        Console.WriteLine($"まばらな項目\t{statistics.SparseCount}");
        Console.WriteLine($"目録の中に実体があるファイル\t{statistics.ResidentCount}");
        Console.WriteLine($"無名のストリームが返らなかったファイル\t{statistics.FilesWithoutUnnamedStream}");
        Console.WriteLine($"リパースポイントの属性\t{statistics.ReparseStreamCount}");
        Console.WriteLine($"名前付きのストリーム（ADS）\t{statistics.NamedDataStreamCount}");
        Console.WriteLine($"無名のストリームの論理サイズの合計\t{statistics.TotalLogicalSize} バイト");
        Console.WriteLine($"更新日時（EXTRA_INFO）が取れたか\t{(statistics.ExtraInfoSeen ? "取れた" : "取れなかった")}");
    }

    /// <summary>
    /// 控えた項目について、無名のストリームの論理サイズが <see cref="FileInfo.Length"/> と一致するかを確かめる。
    /// 経路は組み立てるが出力には出さない。
    /// </summary>
    private static void PrintSampleComparison(
        string volumeRoot,
        Dictionary<ulong, (string Name, ulong Parent)> directories,
        Dictionary<string, List<SampleEntry>> samples)
    {
        Console.WriteLine();
        Console.WriteLine("--- 無名のストリームの論理サイズと FileInfo.Length の突き合わせ ---");
        Console.WriteLine("種類\t比べた件数\t一致\t不一致\t経路を組めなかった\t開けなかった");

        foreach (KeyValuePair<string, List<SampleEntry>> pair in samples)
        {
            int compared = 0;
            int matched = 0;
            int mismatched = 0;
            int unresolved = 0;
            int unopened = 0;
            List<string> mismatchDetails = new List<string>();

            foreach (SampleEntry sample in pair.Value)
            {
                string? path = BuildPath(volumeRoot, directories, sample.ParentFileId, sample.Name);
                if (path is null)
                {
                    unresolved++;
                    continue;
                }

                try
                {
                    FileInfo info = new FileInfo(path);
                    if (!info.Exists)
                    {
                        unopened++;
                        continue;
                    }

                    compared++;
                    if (info.Length == sample.EndOfFile)
                    {
                        matched++;
                    }
                    else
                    {
                        mismatched++;
                        if (mismatchDetails.Count < 5)
                        {
                            mismatchDetails.Add($"目録 {sample.EndOfFile} / FileInfo {info.Length}");
                        }
                    }
                }
                catch (IOException)
                {
                    unopened++;
                }
                catch (UnauthorizedAccessException)
                {
                    unopened++;
                }
            }

            Console.WriteLine($"{pair.Key}\t{compared}\t{matched}\t{mismatched}\t{unresolved}\t{unopened}");
            foreach (string detail in mismatchDetails)
            {
                Console.WriteLine($"  不一致の例: {detail}");
            }
        }
    }

    /// <summary>アクセス権の無いフォルダの中身が返ったかどうかを出す。</summary>
    private static void PrintDenyResult(DenyFixture? fixture, List<SampleEntry> deniedChildren)
    {
        Console.WriteLine();
        Console.WriteLine("--- アクセス権の無いフォルダの扱い ---");
        if (fixture is null)
        {
            Console.WriteLine("読み取りを拒否したフォルダを用意できなかったため、確かめていない。");
            return;
        }

        if (!fixture.EnumerationDenied)
        {
            Console.WriteLine("用意したフォルダの列挙が拒否されなかったため、確かめになっていない。");
            return;
        }

        Console.WriteLine($"用意した中身のファイル数\t{fixture.ExpectedFileCount}");
        Console.WriteLine($"目録に返った中身のファイル数\t{deniedChildren.Count}");
        long total = 0;
        foreach (SampleEntry child in deniedChildren)
        {
            total += child.EndOfFile;
        }

        Console.WriteLine($"目録が返した論理サイズの合計\t{total} バイト（用意した合計 {fixture.ExpectedTotalBytes} バイト）");
        bool ok = deniedChildren.Count == fixture.ExpectedFileCount && total == fixture.ExpectedTotalBytes;
        Console.WriteLine($"結果\t{(ok ? "アクセス権の無いフォルダの中身も、件数とサイズの両方が正しく返った" : "返らなかった、または値が合わなかった")}");
    }

    /// <summary>
    /// ファイル参照番号の連なりから完全な経路を組み立てる。
    /// 経路は突き合わせにだけ使い、出力には出さない。
    /// </summary>
    private static string? BuildPath(
        string volumeRoot,
        Dictionary<ulong, (string Name, ulong Parent)> directories,
        ulong parentFileId,
        string name)
    {
        List<string> parts = new List<string>();
        ulong current = parentFileId;
        for (int depth = 0; depth < 256; depth++)
        {
            if ((current & FileIndexMask) == RootFileIndex)
            {
                parts.Reverse();
                StringBuilder builder = new StringBuilder(volumeRoot);
                foreach (string part in parts)
                {
                    builder.Append(part).Append('\\');
                }

                builder.Append(name);
                return builder.ToString();
            }

            if (!directories.TryGetValue(current, out (string Name, ulong Parent) entry))
            {
                return null;
            }

            parts.Add(entry.Name);
            current = entry.Parent;
        }

        return null;
    }

    /// <summary>
    /// 読み取りを自分自身に拒否したフォルダを一時的に用意し、後始末まで受け持つ。
    /// 管理者権限は要らない（自分の SID への Deny ACE のため）。
    /// </summary>
    private sealed class DenyFixture : IDisposable
    {
        private readonly string _directoryPath;

        private DenyFixture(string directoryPath, ulong directoryFileId, int expectedFileCount, long expectedTotalBytes, bool enumerationDenied)
        {
            _directoryPath = directoryPath;
            DirectoryFileId = directoryFileId;
            ExpectedFileCount = expectedFileCount;
            ExpectedTotalBytes = expectedTotalBytes;
            EnumerationDenied = enumerationDenied;
        }

        /// <summary>用意したフォルダのファイル参照番号。</summary>
        public ulong DirectoryFileId { get; }

        /// <summary>用意した中身のファイル数。</summary>
        public int ExpectedFileCount { get; }

        /// <summary>用意した中身の論理サイズの合計。</summary>
        public long ExpectedTotalBytes { get; }

        /// <summary>実際に列挙が拒否されることを確かめられたか。</summary>
        public bool EnumerationDenied { get; }

        /// <summary>
        /// 一時フォルダの下に、中身を作ってから自分自身の読み取りを拒否したフォルダを用意する。
        /// 用意できないときは <c>null</c> を返す。
        /// </summary>
        public static DenyFixture? TryCreate(string volumeRoot)
        {
            string tempRoot = Path.GetTempPath();
            if (!tempRoot.StartsWith(volumeRoot, StringComparison.OrdinalIgnoreCase))
            {
                // 一時フォルダが対象のボリュームに無いときは確かめられない。
                return null;
            }

            string directoryPath = Path.Combine(tempRoot, "lff-layout-probe-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(directoryPath);

                // 中身は大きさの違う3つにする。サイズまで一致することを確かめられるようにするため。
                int[] sizes = { 3, 1024, 70000 };
                long totalBytes = 0;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte[] content = new byte[sizes[i]];
                    File.WriteAllBytes(Path.Combine(directoryPath, "item" + i.ToString(CultureInfo.InvariantCulture) + ".bin"), content);
                    totalBytes += sizes[i];
                }

                ulong fileId = GetDirectoryFileId(directoryPath);
                if (fileId == 0)
                {
                    Directory.Delete(directoryPath, recursive: true);
                    return null;
                }

                DirectoryInfo directory = new DirectoryInfo(directoryPath);
                DirectorySecurity security = directory.GetAccessControl();
                security.AddAccessRule(CreateDenyRule());
                directory.SetAccessControl(security);

                bool denied = false;
                try
                {
                    Directory.GetFiles(directoryPath);
                }
                catch (UnauthorizedAccessException)
                {
                    denied = true;
                }

                return new DenyFixture(directoryPath, fileId, sizes.Length, totalBytes, denied);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>Deny ACE を外し、用意したフォルダを消す。</summary>
        public void Dispose()
        {
            try
            {
                DirectoryInfo directory = new DirectoryInfo(_directoryPath);
                DirectorySecurity security = directory.GetAccessControl();
                security.RemoveAccessRule(CreateDenyRule());
                directory.SetAccessControl(security);
                Directory.Delete(_directoryPath, recursive: true);
            }
            catch (IOException)
            {
                // 後始末に失敗しても確認そのものは続けられる。
            }
            catch (UnauthorizedAccessException)
            {
                // 同上。
            }
        }

        /// <summary>フォルダのファイル参照番号を取る。</summary>
        private static ulong GetDirectoryFileId(string directoryPath)
        {
            using SafeFileHandle handle = CreateFileW(
                directoryPath,
                FileReadAttributes,
                FileShareReadWrite,
                IntPtr.Zero,
                OpenExisting,
                FileFlagBackupSemantics,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                return 0;
            }

            if (!GetFileInformationByHandle(handle, out ByHandleFileInformation information))
            {
                return 0;
            }

            return ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow;
        }

        /// <summary>自分自身の SID に対する読み取りの拒否の規則を組み立てる。</summary>
        private static FileSystemAccessRule CreateDenyRule()
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            SecurityIdentifier? sid = identity.User;
            if (sid is null)
            {
                throw new InvalidOperationException("実行しているアカウントの SID を取得できませんでした。");
            }

            return new FileSystemAccessRule(
                sid,
                FileSystemRights.ListDirectory |
                FileSystemRights.ReadData |
                FileSystemRights.ReadAttributes |
                FileSystemRights.ReadExtendedAttributes |
                FileSystemRights.ExecuteFile,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Deny);
        }
    }

    /// <summary>引数のドライブの指定を1文字に直す。直せないときは <c>null</c>。</summary>
    private static string? NormalizeDriveLetter(string argument)
    {
        string trimmed = argument.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        char letter = char.ToUpperInvariant(trimmed[0]);
        if (letter < 'A' || letter > 'Z')
        {
            return null;
        }

        if (trimmed.Length > 1 && trimmed[1] != ':')
        {
            return null;
        }

        return letter.ToString();
    }

    /// <summary>ファイルシステムの名前を取る。取れないときは説明を返す。</summary>
    private static string TryGetFileSystemName(string driveLetter)
    {
        try
        {
            DriveInfo drive = new DriveInfo(driveLetter);
            return drive.DriveFormat;
        }
        catch (IOException)
        {
            return "（取得できなかった）";
        }
        catch (UnauthorizedAccessException)
        {
            return "（取得できなかった）";
        }
    }

    /// <summary>いま管理者として動いているかを返す。</summary>
    private static bool IsElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>エラー番号を「番号（意味）」の形にする。</summary>
    private static string DescribeError(int error)
    {
        string message;
        try
        {
            message = new System.ComponentModel.Win32Exception(error).Message;
        }
        catch (InvalidOperationException)
        {
            message = "（意味を取得できなかった）";
        }

        return $"{error}: {message}";
    }

    private static uint ReadUInt32(byte[] buffer, int offset)
    {
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset));
    }

    private static ulong ReadUInt64(byte[] buffer, int offset)
    {
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(offset));
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), value);
    }

    private static void WriteUInt64(byte[] buffer, int offset, ulong value)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(offset), value);
    }

    /// <summary><c>layout-probe</c> の使い方を出す。</summary>
    public static void PrintUsage()
    {
        Console.WriteLine("使い方: ScanBench layout-probe <drive> [--sample N] [--buffer BYTES] [--access-only] [--no-deny-test]");
        Console.WriteLine();
        Console.WriteLine("  <drive>          対象のドライブ（例: C:）。読み取りしか行わない");
        Console.WriteLine("  --sample N       FileInfo.Length と突き合わせる件数（種類ごと）。省略時は 25");
        Console.WriteLine("  --buffer BYTES   列挙の受け皿の大きさ（バイト）。省略時は 4 MiB");
        Console.WriteLine("  --access-only    権限の組み合わせの確認だけを行い、全件の列挙はしない");
        Console.WriteLine("  --no-deny-test   読み取りを拒否したフォルダの確認を行わない");
        Console.WriteLine();
        Console.WriteLine("この下位コマンドは tasks.md 1.2 の早い段階の確認のための使い捨てで、");
        Console.WriteLine("5.1 と 6.1 で本来の部品ができたら取り除く。ファイル名は出力しない");
    }
}
