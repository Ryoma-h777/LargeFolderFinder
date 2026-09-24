<#
.SYNOPSIS
    走査の速さを測り、そのまま貼り付けられる形で結果を出す。

.DESCRIPTION
    計測の道具（Tools/ScanBench）を決まった回数・決まった条件で走らせ、
    環境の情報と結果をまとめて出す。出力には利用者固有のパスを含めない
    （対象は -Label で付けた説明で表す）ため、そのまま貼って共有できる。

    使い方の例（PowerShell で、このリポジトリのフォルダから実行）:

      # 1) ふつうの計測（いまの版）
      powershell -NoProfile -File build/Measure-Scan.ps1 -Path D:\ -Label "D ドライブ全体"

      # 2) 変更の前の版と比べる（前の版の控えを使う）
      powershell -NoProfile -File build/Measure-Scan.ps1 -Path D:\ -Label "D ドライブ全体" -Before

      # 3) 同時に読む数を振って、いちばん速い値を探す（NAS のとき）
      powershell -NoProfile -File build/Measure-Scan.ps1 -Path \\nas\share -Label "NAS の共有" -Sweep

    結果は artifacts/measure/ にも保存される（このフォルダは Git 管理外）。

.PARAMETER Path
    測る対象のフォルダ（ドライブ全体なら D:\ のように指定）。出力には出さない。

.PARAMETER Label
    対象の説明。出力にはこれだけが出る（例: "D ドライブ全体"、"NAS の共有"）。

.PARAMETER Runs
    1組あたりの走査の回数（既定 4。1回目は「初回」、2回目以降は「温まった」として扱う）。

.PARAMETER Sweep
    同時に読む数（1・2・4・8・16・32）を振って測る。どの値がいちばん速いか探すときに使う。

.PARAMETER Before
    変更の前の版の計測の道具（artifacts/scan-performance/bench-before/）で測る。
    比べるときは、同じ対象に対して -Before あり/なしを続けて実行する。

.PARAMETER Sequential
    1つずつ順に読む（並列にしない）方式で測る。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$Label,
    [int]$Runs = 4,
    [switch]$Sweep,
    [switch]$Before,
    [switch]$Sequential
)

$ErrorActionPreference = 'Stop'

# 計測の道具は UTF-8 で出すので、受け取る側の文字コードも合わせる（合わせないと日本語が化ける）
$previousOutputEncoding = [Console]::OutputEncoding
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$repoRoot = Split-Path -Parent $PSScriptRoot

# 計測の道具の場所を決める
if ($Before) {
    $benchExe = Join-Path $repoRoot 'artifacts\scan-performance\bench-before\ScanBench.exe'
    $versionLabel = '変更の前の版'
} else {
    $benchExe = Join-Path $repoRoot 'Tools\ScanBench\bin\Release\net10.0-windows\ScanBench.exe'
    $versionLabel = 'いまの版'
}

if (-not (Test-Path $benchExe)) {
    Write-Host ''
    Write-Host '計測の道具が見つかりません。' -ForegroundColor Yellow
    Write-Host "  探した場所: $benchExe"
    if ($Before) {
        Write-Host '  変更の前の版の控えが無い場合は、-Before を付けずに実行してください。'
    } else {
        Write-Host '  先に次を実行してください: dotnet build LargeFolderFinder.sln -c Release'
    }
    exit 2
}

if (-not (Test-Path -LiteralPath $Path)) {
    Write-Host "測る対象が見つかりません（指定したパスを確かめてください）。" -ForegroundColor Yellow
    exit 2
}

# 起動中のアプリがあると結果が乱れるので知らせる（止めはしない）
$running = Get-Process -Name 'LargeFolderFinder' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host 'Large Folder Finder が起動しています。閉じてから測ると結果が安定します。' -ForegroundColor Yellow
    Write-Host ''
}

# 環境の情報（利用者名やパスは含めない）
$cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1)
$os = (Get-CimInstance Win32_OperatingSystem)
$memGb = [math]::Round(((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB), 0)

# 対象の種類（ローカル/ネットワーク、ファイルシステム）
$kind = 'ローカル'
$fileSystem = '不明'
if ($Path.StartsWith('\\')) {
    $kind = 'ネットワーク（UNC）'
} else {
    $driveRoot = [System.IO.Path]::GetPathRoot((Resolve-Path -LiteralPath $Path).Path)
    try {
        $drive = New-Object System.IO.DriveInfo($driveRoot)
        $fileSystem = $drive.DriveFormat
        if ($drive.DriveType -eq 'Network') { $kind = 'ネットワーク（割り当てたドライブ）' }
        if ($drive.DriveType -eq 'Removable') { $kind = 'リムーバブル' }
    } catch {
        # 意図して無視: ドライブの情報が取れないだけで、計測は続けられる
    }
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if ($isAdmin) { $adminLabel = 'はい' } else { $adminLabel = 'いいえ' }

# 出力先
$outDir = Join-Path $repoRoot 'artifacts\measure'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outFile = Join-Path $outDir "measure-$stamp.txt"

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('===== ここから下をそのままコピーして貼ってください =====')
$lines.Add('')
$lines.Add("対象: $Label")
$lines.Add("対象の種類: $kind / ファイルシステム: $fileSystem")
$lines.Add("計測した版: $versionLabel")
$lines.Add("管理者で実行: $adminLabel")
$lines.Add("CPU: $($cpu.Name) / 論理プロセッサ $($cpu.NumberOfLogicalProcessors) / メモリ ${memGb}GB")
$lines.Add("OS: $($os.Caption) $($os.Version)")
$lines.Add("測った日時: $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
$lines.Add('')

function Invoke-Bench {
    param([string[]]$ExtraArgs, [string]$Note)

    $benchArgs = @($Path, '--runs', $Runs, '--label', $Label)
    if ($Sequential) { $benchArgs += '--sequential' }
    $benchArgs += $ExtraArgs

    Write-Host "測っています: $Note ..." -ForegroundColor Cyan
    $output = & $benchExe @benchArgs 2>&1
    $exit = $LASTEXITCODE

    $script:lines.Add("--- $Note ---")
    foreach ($line in $output) { $script:lines.Add([string]$line) }
    if ($exit -ne 0) { $script:lines.Add("（計測の道具が終了コード $exit で終わりました）") }
    $script:lines.Add('')
}

if ($Sweep) {
    if ($Before) {
        Write-Host '変更の前の版では同時に読む数を指定できません。-Sweep を外して実行してください。' -ForegroundColor Yellow
        exit 2
    }
    foreach ($threads in 1, 2, 4, 8, 16, 32) {
        Invoke-Bench -ExtraArgs @('--threads', $threads, '--no-digest') -Note "同時に読む数 $threads"
    }
} else {
    if ($Sequential) { $note = '1つずつ順に読む' } else { $note = '既定の設定' }
    Invoke-Bench -ExtraArgs @() -Note $note
}

$lines.Add('===== ここまで =====')

$text = $lines -join [Environment]::NewLine
Set-Content -Path $outFile -Value $text -Encoding utf8

Write-Host ''
Write-Host $text
Write-Host ''
Write-Host "この内容は次のファイルにも保存しました: artifacts\measure\measure-$stamp.txt" -ForegroundColor Green

[Console]::OutputEncoding = $previousOutputEncoding
exit 0
