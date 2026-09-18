<#
.SYNOPSIS
    発行した Large Folder Finder の exe が起動してウィンドウを出すことを確かめる。

.DESCRIPTION
    exe を起動し、プロセスが生きていて WPF のウィンドウ（クラス名が HwndWrapper で始まる、
    表示中の最上位ウィンドウ）が出るまで待つ。ウィンドウが出たら少し待って生きていることを
    確かめ、そのウィンドウに閉じる要求（WM_CLOSE）を送る。決められた時間内に終わらなければ、
    このスクリプトが起動したそのプロセスだけを終了させる（名前でプロセスを探して終了させることはしない）。

    次の場合は失敗（終了コード 1）とする。exe の標準エラー・標準出力と終了コードを添えて報告する。
    - exe が見つからない、または起動できない（壊れたファイルなど）
    - ウィンドウが出る前にプロセスが終了した
    - 時間内にウィンドウが出なかった
    - エラーのダイアログ（クラス名 #32770 のウィンドウ。アプリの初期化の失敗や、
      ランタイムが無いときのホストの案内など）が出た

    Windows PowerShell 5.1 でも動く書き方にしている。成功したときは終了コード 0 で終わる。

    注意: 起動したアプリは、アプリデータ（%LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder）
    に設定とログを書き、古いログを消す。CI は使い捨ての環境なので問題ないが、開発者の手元で使うときは、
    事前にこのフォルダを退避し、終わったら元に戻すこと。また、利用者が起動している
    Large Folder Finder は閉じてから使うこと（このスクリプトはそれに触れないが、同じデータを書き合うため）。

.PARAMETER ExePath
    起動を確かめる exe のパス（必須）。相対パスは現在の場所が基準。

.PARAMETER TimeoutSeconds
    ウィンドウが出るまで待つ時間と、閉じる要求の後に終わるまで待つ時間（秒）。既定 20。1〜600。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File build/Test-Launch.ps1 -ExePath artifacts/publish/SelfContained/LargeFolderFinder.exe

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File build/Test-Launch.ps1 -ExePath artifacts/publish/FrameworkDependent/LargeFolderFinder.exe -TimeoutSeconds 60
#>
param(
    [string]$ExePath,
    [string]$TimeoutSeconds = '20'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

# ウィンドウが出てから、生きていることを確かめるまで待つ時間（ミリ秒）
$AliveCheckMilliseconds = 2000
# ウィンドウの確認の間隔（ミリ秒）
$PollMilliseconds = 200

# このスクリプトが起動したプロセス（これ以外のプロセスには触れない）
$script:ownProcess = $null
$script:stdoutTask = $null
$script:stderrTask = $null

# 自分が起動したプロセスがまだ動いていれば、そのプロセスだけを終了させる
function Stop-OwnProcess {
    if ($null -eq $script:ownProcess) { return }
    try {
        if (-not $script:ownProcess.HasExited) {
            Write-Host "起動したプロセス（PID $($script:ownProcess.Id)）を終了させます。"
            $script:ownProcess.Kill()
            [void]$script:ownProcess.WaitForExit(10000)
        }
    }
    catch {
        [Console]::Error.WriteLine("警告: 起動したプロセス（PID $($script:ownProcess.Id)）を終了させられませんでした: $($_.Exception.Message)")
    }
}

# exe の標準出力・標準エラーのうち、終了までに読めた分を返す
function Get-OwnProcessOutput {
    $lines = @()
    foreach ($pair in @(@('標準エラー', $script:stderrTask), @('標準出力', $script:stdoutTask))) {
        $task = $pair[1]
        if ($null -eq $task) { continue }
        try {
            if ($task.Wait(3000)) {
                $text = $task.Result
                if (-not [string]::IsNullOrWhiteSpace($text)) {
                    $lines += "  exe の$($pair[0]):"
                    $lines += ($text.TrimEnd() -split "`r?`n" | ForEach-Object { "    $_" })
                }
            }
        }
        catch {
            # 読めなかった出力は添えない
        }
    }
    return $lines
}

# エラーを標準エラーに出し、自分が起動したプロセスを片付けて、終了コード 1 で終える
function Exit-WithError([string]$Message, [string[]]$Details = @()) {
    Stop-OwnProcess
    [Console]::Error.WriteLine("エラー: $Message")
    foreach ($line in $Details) { [Console]::Error.WriteLine($line) }
    foreach ($line in (Get-OwnProcessOutput)) { [Console]::Error.WriteLine($line) }
    exit 1
}

# 指定したプロセスの最上位ウィンドウを列挙し、閉じる要求を送るための Win32 の呼び出し
$nativeSource = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace LargeFolderFinderBuild
{
    public sealed class WindowInfo
    {
        public IntPtr Handle;
        public string ClassName;
        public string Title;
        public bool Visible;
    }

    public static class NativeWindows
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_CLOSE = 0x0010;

        // 指定したプロセスが持つ最上位ウィンドウの一覧を返す
        public static WindowInfo[] GetTopLevelWindows(int processId)
        {
            var result = new List<WindowInfo>();
            EnumWindows(delegate (IntPtr hWnd, IntPtr lParam)
            {
                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                if (pid == (uint)processId)
                {
                    result.Add(new WindowInfo
                    {
                        Handle = hWnd,
                        ClassName = GetClassNameOf(hWnd),
                        Title = GetTextOf(hWnd),
                        Visible = IsWindowVisible(hWnd)
                    });
                }
                return true;
            }, IntPtr.Zero);
            return result.ToArray();
        }

        // ダイアログの中の文言（子ウィンドウの Static の文字列）を返す
        public static string[] GetStaticTexts(IntPtr hWnd)
        {
            var texts = new List<string>();
            EnumChildWindows(hWnd, delegate (IntPtr child, IntPtr lParam)
            {
                if (GetClassNameOf(child) == "Static")
                {
                    string text = GetTextOf(child);
                    if (!string.IsNullOrEmpty(text)) texts.Add(text);
                }
                return true;
            }, IntPtr.Zero);
            return texts.ToArray();
        }

        // ウィンドウに閉じる要求を送る
        public static bool RequestClose(IntPtr hWnd)
        {
            return PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        private static string GetClassNameOf(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string GetTextOf(IntPtr hWnd)
        {
            int length = GetWindowTextLength(hWnd);
            var sb = new StringBuilder(Math.Max(length, 0) + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }
    }
}
'@

# 表示中のエラーのダイアログ（#32770）の説明を返す。無ければ空
function Get-ErrorDialogDetails([int]$ProcessId) {
    $details = @()
    $dialogs = @([LargeFolderFinderBuild.NativeWindows]::GetTopLevelWindows($ProcessId) | Where-Object { $_.Visible -and $_.ClassName -eq '#32770' })
    foreach ($dialog in $dialogs) {
        $details += "  ダイアログの題: $($dialog.Title)"
        foreach ($text in [LargeFolderFinderBuild.NativeWindows]::GetStaticTexts($dialog.Handle)) {
            $details += ($text -split "`r?`n" | ForEach-Object { "    $_" })
        }
    }
    return $details
}

try {
    # --- 引数の検証 ---
    if ([string]::IsNullOrWhiteSpace($ExePath)) {
        Exit-WithError '-ExePath に起動を確かめる exe のパスを指定してください。'
    }
    $timeout = 0
    if (-not [int]::TryParse($TimeoutSeconds, [ref]$timeout) -or $timeout -lt 1 -or $timeout -gt 600) {
        Exit-WithError "-TimeoutSeconds の値 '$TimeoutSeconds' は使えません。1〜600 の整数（秒）を指定してください。"
    }
    # 相対パスは現在の場所を基準に絶対パスへ直す
    $exeFullPath = [IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ExePath))
    if (-not (Test-Path -LiteralPath $exeFullPath -PathType Leaf)) {
        Exit-WithError "exe が見つかりません: $exeFullPath"
    }

    if (-not ('LargeFolderFinderBuild.NativeWindows' -as [type])) {
        Add-Type -TypeDefinition $nativeSource -Language CSharp
    }

    Write-Host "起動を確かめます: $exeFullPath（待つ時間: $timeout 秒）"
    Write-Host '注意: 起動したアプリはアプリデータに設定とログを書きます。手元で使うときは事前に退避してください。'

    # --- 起動 ---
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $exeFullPath
    $startInfo.WorkingDirectory = Split-Path -Parent $exeFullPath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    try {
        $script:ownProcess = [System.Diagnostics.Process]::Start($startInfo)
    }
    catch {
        $inner = $_.Exception
        while ($null -ne $inner.InnerException) { $inner = $inner.InnerException }
        Exit-WithError "exe を起動できません: $exeFullPath（$($inner.Message)）"
    }
    if ($null -eq $script:ownProcess) {
        Exit-WithError "exe を起動できません: $exeFullPath（プロセスが作られませんでした）"
    }
    # 出力を読み続けないと exe が書き込みで止まることがあるため、非同期で読んでおく
    $script:stdoutTask = $script:ownProcess.StandardOutput.ReadToEndAsync()
    $script:stderrTask = $script:ownProcess.StandardError.ReadToEndAsync()
    $processId = $script:ownProcess.Id
    Write-Host "起動しました（PID $processId）。ウィンドウが出るのを待ちます。"

    # --- ウィンドウが出るまで待つ ---
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $mainWindow = $null
    while ($null -eq $mainWindow) {
        if ($script:ownProcess.HasExited) {
            Exit-WithError "ウィンドウが出る前にプロセスが終了しました（終了コード $($script:ownProcess.ExitCode)、起動から $([math]::Round($stopwatch.Elapsed.TotalSeconds, 1)) 秒）。"
        }
        $dialogDetails = @(Get-ErrorDialogDetails $processId)
        if ($dialogDetails.Count -gt 0) {
            Exit-WithError "起動中にエラーのダイアログが出ました（起動から $([math]::Round($stopwatch.Elapsed.TotalSeconds, 1)) 秒）。" $dialogDetails
        }
        $mainWindow = [LargeFolderFinderBuild.NativeWindows]::GetTopLevelWindows($processId) |
            Where-Object { $_.Visible -and $_.ClassName.StartsWith('HwndWrapper', [StringComparison]::Ordinal) } |
            Select-Object -First 1
        if ($null -ne $mainWindow) { break }
        if ($stopwatch.Elapsed.TotalSeconds -ge $timeout) {
            Exit-WithError "時間内（$timeout 秒）にウィンドウが出ませんでした。"
        }
        Start-Sleep -Milliseconds $PollMilliseconds
    }
    $shownSeconds = [math]::Round($stopwatch.Elapsed.TotalSeconds, 1)
    Write-Host "ウィンドウが出ました（起動から $shownSeconds 秒、題: '$($mainWindow.Title)'、クラス: $($mainWindow.ClassName)）。"

    # --- 生きていることを確かめる ---
    Start-Sleep -Milliseconds $AliveCheckMilliseconds
    if ($script:ownProcess.HasExited) {
        Exit-WithError "ウィンドウが出た後、$($AliveCheckMilliseconds / 1000) 秒以内にプロセスが終了しました（終了コード $($script:ownProcess.ExitCode)）。"
    }
    $dialogDetails = @(Get-ErrorDialogDetails $processId)
    if ($dialogDetails.Count -gt 0) {
        Exit-WithError 'ウィンドウが出た後にエラーのダイアログが出ました。' $dialogDetails
    }
    Write-Host "$($AliveCheckMilliseconds / 1000) 秒後もプロセスは生きています。"

    # --- 閉じる要求を送り、終わるのを待つ ---
    Write-Host 'ウィンドウに閉じる要求を送ります。'
    if (-not [LargeFolderFinderBuild.NativeWindows]::RequestClose($mainWindow.Handle)) {
        [Console]::Error.WriteLine('警告: 閉じる要求を送れませんでした。')
    }
    if ($script:ownProcess.WaitForExit($timeout * 1000)) {
        $script:ownProcess.WaitForExit()
        Write-Host "プロセスは閉じる要求で終了しました（終了コード $($script:ownProcess.ExitCode)）。"
    }
    else {
        [Console]::Error.WriteLine("警告: 閉じる要求の後、時間内（$timeout 秒）に終了しませんでした。")
        Stop-OwnProcess
    }

    Write-Host "成功: exe が起動し、ウィンドウが出ることを確かめました: $exeFullPath"
    exit 0
}
catch {
    Exit-WithError "起動の確認の途中で失敗しました: $($_.Exception.Message)"
}
finally {
    # どこで抜けても、自分が起動したプロセスを残さない
    Stop-OwnProcess
}
