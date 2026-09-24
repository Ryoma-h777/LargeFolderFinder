using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace LargeFolderFinder.GoldenBaseline.Scan;

/// <summary>
/// 本体の権限の判定（<see cref="global::LargeFolderFinder.AdminRights"/>）と、発行の元になる実行ファイルに
/// 埋め込まれたマニフェストを、決まった手順で観測して値だけを返す入口（ntfs-mft-scan タスク2.1、要件3.1〜3.4）。
/// 本体に触れる呼び出しと OS の直接呼び出しを Scan 層に閉じ込めるためのものであり、判定は呼び出し側（自己検証）が行う。
/// ここで行うのはいずれも読み取りだけで、権限の昇格は試みない。
/// </summary>
public static class AdminRightsProbe
{
    /// <summary>プロセスのトークンを問い合わせのために開くときの権限（TOKEN_QUERY）。</summary>
    private const uint TokenQuery = 0x0008;

    /// <summary>GetTokenInformation の情報の種類。昇格しているかの印（TokenElevation）。</summary>
    private const int TokenElevationClass = 20;

    /// <summary>GetTokenInformation の情報の種類。昇格の種類（TokenElevationType）。</summary>
    private const int TokenElevationTypeClass = 18;

    /// <summary>実行ファイルに埋め込まれたマニフェストの資源の種類（RT_MANIFEST）。</summary>
    private const int ResourceTypeManifest = 24;

    /// <summary>実行ファイル自身のマニフェストの資源の番号（CREATEPROCESS_MANIFEST_RESOURCE_ID）。</summary>
    private const int ManifestResourceId = 1;

    /// <summary>資源を読むためだけに読み込む（コードは実行しない）指定。</summary>
    private const uint LoadLibraryAsDataFile = 0x00000002;

    /// <summary>資源を読むためだけに読み込む（並びを整えて対応付ける）指定。</summary>
    private const uint LoadLibraryAsImageResource = 0x00000020;

    /// <summary>本体の実行ファイルの名前。検証ツールの出力先にも複写される。</summary>
    private const string AppExeFileName = "LargeFolderFinder.exe";

    /// <summary>
    /// 本体の権限の判定を2回読み、独立した2つの OS の問い合わせ（WindowsPrincipal と、プロセスのトークン）の
    /// 結果を添えて返す。本体の判定が例外を投げた場合も、その型と文言を値として返す（この入口からは投げない）。
    /// </summary>
    public static AdminRightsObservation Observe()
    {
        bool? first = null;
        bool? second = null;
        string? exceptionType = null;
        string? exceptionMessage = null;

        try
        {
            first = global::LargeFolderFinder.AdminRights.IsElevated;
            second = global::LargeFolderFinder.AdminRights.IsElevated;
        }
        catch (Exception ex)
        {
            exceptionType = ex.GetType().FullName;
            exceptionMessage = ex.Message;
        }

        return new AdminRightsObservation(
            first,
            second,
            exceptionType,
            exceptionMessage,
            ReadPrincipalIsElevated(),
            QueryTokenElevation(),
            DescribePublicSurface());
    }

    /// <summary>
    /// 検証ツールの出力先に複写された本体の実行ファイルから、埋め込まれたマニフェストの本文を取り出す。
    /// 実行ファイルが無い場合や資源が無い場合も、その事実を値として返す（例外を投げない）。
    /// </summary>
    public static AppManifestObservation ReadAppManifest()
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppExeFileName);
        if (!File.Exists(path))
        {
            return new AppManifestObservation(path, false, null, "本体の実行ファイルが見つかりません。");
        }

        IntPtr module = LoadLibraryEx(path, IntPtr.Zero, LoadLibraryAsDataFile | LoadLibraryAsImageResource);
        if (module == IntPtr.Zero)
        {
            return new AppManifestObservation(path, true, null, $"実行ファイルを資源として読み込めません（エラー {Marshal.GetLastWin32Error()}）。");
        }

        try
        {
            IntPtr info = FindResource(module, (IntPtr)ManifestResourceId, (IntPtr)ResourceTypeManifest);
            if (info == IntPtr.Zero)
            {
                return new AppManifestObservation(path, true, null, $"マニフェストの資源が見つかりません（エラー {Marshal.GetLastWin32Error()}）。");
            }

            uint size = SizeofResource(module, info);
            IntPtr handle = LoadResource(module, info);
            if (size == 0 || handle == IntPtr.Zero)
            {
                return new AppManifestObservation(path, true, null, $"マニフェストの資源を読めません（大きさ {size}、エラー {Marshal.GetLastWin32Error()}）。");
            }

            IntPtr data = LockResource(handle);
            if (data == IntPtr.Zero)
            {
                return new AppManifestObservation(path, true, null, "マニフェストの資源の場所を取得できません。");
            }

            var bytes = new byte[size];
            Marshal.Copy(data, bytes, 0, (int)size);
            string text = new UTF8Encoding(false).GetString(bytes).TrimStart(ByteOrderMark);
            return new AppManifestObservation(path, true, text, null);
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    /// <summary>マニフェストの本文の先頭に付くことがあるバイト順の印。</summary>
    private const char ByteOrderMark = (char)0xFEFF;

    /// <summary>
    /// いまのプロセスが管理者の役割を持つかを <c>WindowsIdentity</c>/<c>WindowsPrincipal</c> で独立に調べる。
    /// 本体の判定と同じ仕組みを検証ツール側で実装し、部品が仕組みどおりに答えているかを照合するために使う。
    /// </summary>
    private static bool ReadPrincipalIsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// プロセスのトークンに直接問い合わせ、昇格しているかの印と昇格の種類を返す。
    /// <c>WindowsPrincipal</c> とは別の OS の問い合わせなので、本体の判定の裏取りに使える。
    /// </summary>
    private static TokenElevationObservation QueryTokenElevation()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out IntPtr token))
        {
            return new TokenElevationObservation(false, null, null, Marshal.GetLastWin32Error());
        }

        try
        {
            uint size = sizeof(uint);
            if (!GetTokenInformation(token, TokenElevationClass, out uint elevated, size, out _))
            {
                return new TokenElevationObservation(false, null, null, Marshal.GetLastWin32Error());
            }

            int? elevationType = GetTokenInformation(token, TokenElevationTypeClass, out uint type, size, out _)
                ? (int)type
                : null;

            return new TokenElevationObservation(true, elevated != 0, elevationType, 0);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    /// <summary>
    /// 本体の権限の判定の型が外に見せている面（公開されている要素の並び）を返す。
    /// 昇格を試みる入口を公開していないことを確かめるために使う（design.md Helpers &gt; AdminRights）。
    /// </summary>
    private static AdminRightsSurface DescribePublicSurface()
    {
        Type type = typeof(global::LargeFolderFinder.AdminRights);
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        IReadOnlyList<string> members = type.GetMembers(Flags)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        PropertyInfo? property = type.GetProperty(
            nameof(global::LargeFolderFinder.AdminRights.IsElevated),
            BindingFlags.Public | BindingFlags.Static);

        return new AdminRightsSurface(
            type.IsAbstract && type.IsSealed,
            type.IsPublic,
            members,
            property != null && property.PropertyType == typeof(bool),
            property != null && property.CanRead && property.GetMethod!.IsStatic,
            property != null && property.SetMethod == null);
    }

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryEx(string lpLibFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", EntryPoint = "FindResourceW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindResource(IntPtr hModule, IntPtr lpName, IntPtr lpType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LockResource(IntPtr hResData);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        out uint tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}

/// <summary>プロセスのトークンへの問い合わせの結果。</summary>
/// <param name="Queried">問い合わせが成功したか</param>
/// <param name="IsElevated">昇格しているかの印。問い合わせに失敗したときは null</param>
/// <param name="ElevationType">昇格の種類（1=既定、2=完全、3=制限）。取得できなければ null</param>
/// <param name="LastError">問い合わせに失敗したときの OS のエラー番号。成功なら 0</param>
public sealed record TokenElevationObservation(bool Queried, bool? IsElevated, int? ElevationType, int LastError);

/// <summary>本体の権限の判定の型が外に見せている面。</summary>
/// <param name="IsStaticClass">静的なクラスか</param>
/// <param name="IsPublic">公開されている型か</param>
/// <param name="PublicMemberNames">公開されている要素の名前（並びは名前順）</param>
/// <param name="IsBooleanProperty"><c>IsElevated</c> が真偽の値を返すか</param>
/// <param name="IsStaticReadable"><c>IsElevated</c> が静的に読めるか</param>
/// <param name="HasNoSetter"><c>IsElevated</c> に書き込みの入口が無いか</param>
public sealed record AdminRightsSurface(
    bool IsStaticClass,
    bool IsPublic,
    IReadOnlyList<string> PublicMemberNames,
    bool IsBooleanProperty,
    bool IsStaticReadable,
    bool HasNoSetter);

/// <summary><see cref="AdminRightsProbe.Observe"/> の結果。</summary>
/// <param name="FirstRead">本体の判定の1回目の値。例外が出たときは null</param>
/// <param name="SecondRead">本体の判定の2回目の値。例外が出たときは null</param>
/// <param name="ExceptionType">本体の判定が投げた例外の型。投げなければ null</param>
/// <param name="ExceptionMessage">本体の判定が投げた例外の文言。投げなければ null</param>
/// <param name="PrincipalIsElevated">検証ツール側で独立に調べた管理者の役割の有無</param>
/// <param name="Token">プロセスのトークンへの問い合わせの結果</param>
/// <param name="Surface">本体の権限の判定の型が外に見せている面</param>
public sealed record AdminRightsObservation(
    bool? FirstRead,
    bool? SecondRead,
    string? ExceptionType,
    string? ExceptionMessage,
    bool PrincipalIsElevated,
    TokenElevationObservation Token,
    AdminRightsSurface Surface);

/// <summary><see cref="AdminRightsProbe.ReadAppManifest"/> の結果。</summary>
/// <param name="ExePath">読もうとした実行ファイルのパス</param>
/// <param name="ExeExists">実行ファイルがあったか</param>
/// <param name="ManifestText">取り出せたマニフェストの本文。取り出せなければ null</param>
/// <param name="Error">取り出せなかった理由。取り出せたなら null</param>
public sealed record AppManifestObservation(string ExePath, bool ExeExists, string? ManifestText, string? Error);
