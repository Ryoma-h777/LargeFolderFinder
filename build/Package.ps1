<#
.SYNOPSIS
    発行した Large Folder Finder の2つの形態から、配布用の zip を2つ作る。

.DESCRIPTION
    自己完結版と軽量版（フレームワーク依存版）の発行フォルダから、次の構成だけを zip にする。
    - LargeFolderFinder.exe
    - Config.txt
    - Resources/Languages/*.yaml
    - Resources/Readme/*.txt
    - Resources/License/*（フォルダの直下のファイル）
    pdb など上記以外のファイル・下位フォルダは入れない（入れなかったファイルは一覧で表示する）。

    zip は System.IO.Compression で直接作る。Windows PowerShell 5.1 の Compress-Archive
    （Microsoft.PowerShell.Archive 1.0.1.0）は zip 内のパスの区切りに \ を書くことがあるため使わない。
    zip 内のパスの区切りは常に /。名前のエンコーディングは指定せず既定のままにする。既定では、
    ASCII だけの名前はそのまま書き、ASCII 以外の文字を含む名前は UTF-8 で書いて UTF-8 の印
    （汎用フラグのビット 11）を付ける。UTF-8 を明示して渡すと .NET Framework は印を付けずに
    UTF-8 で書き、展開する側で文字化けするため、明示しない。

    次の検査を zip を作る前（発行フォルダ）と作った後（zip を読み直して）の両方で行い、
    満たさなければ終了コード 1 で終わる。失敗したときは、このスクリプトが作った zip を消す。
    - 必須のファイル（exe、Config.txt、言語ファイル13本、ライセンス2本
      LICENSE.txt・ThirdPartyNotices.txt）がそろっていること
    - 2つの中身のファイル一覧（パスと大きさ）が、exe の大きさを除いて一致すること
    - zip の中身が、上記の構成に当てはまるファイルだけであること（区切りが / であることを含む）

    Windows PowerShell 5.1 でも動く書き方にしている。成功したときは終了コード 0 で終わる。

.PARAMETER SelfContainedDir
    自己完結版の発行フォルダ。省略時はリポジトリ直下の artifacts/publish/SelfContained。

.PARAMETER FrameworkDependentDir
    軽量版の発行フォルダ。省略時はリポジトリ直下の artifacts/publish/FrameworkDependent。

.PARAMETER OutputDir
    zip の出力先のフォルダ。省略時はリポジトリ直下の artifacts/package。
    artifacts/package 配下では、同じ名前の zip があれば作り直す。
    それ以外の場所では、同じ名前の zip があれば何も消さずに失敗にする（利用者のファイルを消さないため）。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File build/Package.ps1

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File build/Package.ps1 -SelfContainedDir artifacts/publish/SelfContained -FrameworkDependentDir artifacts/publish/FrameworkDependent -OutputDir artifacts/package
#>
param(
    [string]$SelfContainedDir,
    [string]$FrameworkDependentDir,
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

# 実行ファイルの名前
$ExeName = 'LargeFolderFinder.exe'
# 出力する zip の名前（自己完結版が既定の配布物）
$SelfContainedZipName = 'LargeFolderFinder.zip'
$FrameworkDependentZipName = 'LargeFolderFinder-FrameworkDependent.zip'
# 必須の言語ファイルの本数（LocalizationCheck の「言語 13」と同じ数）
$RequiredLanguageCount = 13
# 必須のライセンスのファイル
$RequiredLicenseFiles = @('Resources/License/LICENSE.txt', 'Resources/License/ThirdPartyNotices.txt')

# このスクリプトが作った zip（失敗したときに消す）
$script:createdZips = @()

# このスクリプトが作った zip を消す
function Remove-CreatedZips {
    foreach ($zip in $script:createdZips) {
        try {
            if (Test-Path -LiteralPath $zip) {
                Remove-Item -LiteralPath $zip -Force
                Write-Host "作りかけの zip を消しました: $zip"
            }
        }
        catch {
            [Console]::Error.WriteLine("警告: 作りかけの zip を消せませんでした: $zip（$($_.Exception.Message)）")
        }
    }
    $script:createdZips = @()
}

# エラーを標準エラーに出して、作った zip を消し、終了コード 1 で終える
function Exit-WithError([string]$Message) {
    Remove-CreatedZips
    [Console]::Error.WriteLine("エラー: $Message")
    exit 1
}

# 相対パスを現在の場所を基準に絶対パスへ直す（末尾の \ は取る）
function Resolve-FullPath([string]$Path) {
    return [IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)).TrimEnd('\')
}

# zip 内のパス（区切りは /）が、配布物に入れる構成に当てはまるかを返す
function Test-PackagedPath([string]$RelativePath) {
    if ($RelativePath -ceq $ExeName) { return $true }
    if ($RelativePath -ceq 'Config.txt') { return $true }
    if ($RelativePath -match '^Resources/Languages/[^/]+\.yaml$') { return $true }
    if ($RelativePath -match '^Resources/Readme/[^/]+\.txt$') { return $true }
    if ($RelativePath -match '^Resources/License/[^/]+$') { return $true }
    return $false
}

# 一覧の項目（Path と Length を持つ）の必須のファイルを確かめ、足りないものの説明を返す
function Get-MissingRequired([object[]]$Items) {
    $paths = @($Items | ForEach-Object { $_.Path })
    $missing = @()
    foreach ($required in @($ExeName, 'Config.txt') + $RequiredLicenseFiles) {
        if ($paths -cnotcontains $required) { $missing += $required }
    }
    $languageCount = @($paths | Where-Object { $_ -match '^Resources/Languages/[^/]+\.yaml$' }).Count
    if ($languageCount -ne $RequiredLanguageCount) {
        $missing += "言語ファイル（Resources/Languages/*.yaml）が $languageCount 本です（$RequiredLanguageCount 本が必要）"
    }
    return $missing
}

# 2つの一覧を exe の大きさを除いて比べ、違いの説明を返す
function Compare-Listings([object[]]$SelfContained, [object[]]$FrameworkDependent) {
    $differences = @()
    $scMap = @{}
    foreach ($item in $SelfContained) { $scMap[$item.Path] = $item.Length }
    $fdMap = @{}
    foreach ($item in $FrameworkDependent) { $fdMap[$item.Path] = $item.Length }
    foreach ($path in @(@($scMap.Keys) + @($fdMap.Keys) | Sort-Object -Unique)) {
        if (-not $fdMap.ContainsKey($path)) {
            $differences += "自己完結版にだけある: $path"
        }
        elseif (-not $scMap.ContainsKey($path)) {
            $differences += "軽量版にだけある: $path"
        }
        elseif ($path -cne $ExeName -and $scMap[$path] -ne $fdMap[$path]) {
            $differences += "大きさが違う: $path（自己完結版 $($scMap[$path]) バイト、軽量版 $($fdMap[$path]) バイト）"
        }
    }
    return $differences
}

# 必須のファイルと、2つの一覧の一致を検査する。満たさなければ失敗で終える
function Assert-Listings([object[]]$SelfContained, [object[]]$FrameworkDependent, [string]$Stage) {
    $problems = @()
    foreach ($pair in @(@('自己完結版', $SelfContained), @('軽量版', $FrameworkDependent))) {
        foreach ($missing in @(Get-MissingRequired $pair[1])) {
            $problems += "$($pair[0])に必須のファイルがありません: $missing"
        }
    }
    $differences = @(Compare-Listings $SelfContained $FrameworkDependent)
    if ($differences.Count -gt 0) {
        $problems += '2つの中身の一覧が一致しません（exe の大きさを除く）:'
        $problems += @($differences | ForEach-Object { "  $_" })
    }
    if ($problems.Count -gt 0) {
        Exit-WithError ("$($Stage)の検査に失敗しました。`n" + ($problems -join "`n"))
    }
}

# 発行フォルダから、zip に入れるファイルの一覧を作る（入れないファイルは表示する）
function Get-PublishListing([string]$Dir, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Dir -PathType Container)) {
        Exit-WithError "$($Label)の発行フォルダが見つかりません: $Dir（先に build/Publish.ps1 で発行してください）"
    }
    $items = @()
    $excluded = @()
    foreach ($file in @(Get-ChildItem -LiteralPath $Dir -Recurse -File -Force | Sort-Object FullName)) {
        $relative = $file.FullName.Substring($Dir.Length + 1).Replace('\', '/')
        if (Test-PackagedPath $relative) {
            $items += [pscustomobject]@{ Path = $relative; Length = $file.Length; FullName = $file.FullName }
        }
        else {
            $excluded += $relative
        }
    }
    if ($excluded.Count -gt 0) {
        Write-Host "  $($Label)の発行フォルダのうち zip に入れないファイル: $($excluded -join ', ')"
    }
    return , @($items | Sort-Object Path)
}

# 一覧のファイルを zip にする（zip 内の区切りは /。名前のエンコーディングは既定のまま）
function New-PackageZip([object[]]$Items, [string]$ZipPath) {
    $script:createdZips += $ZipPath
    $archive = [IO.Compression.ZipFile]::Open($ZipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($item in $Items) {
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $item.FullName, $item.Path, [IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally {
        $archive.Dispose()
    }
}

# zip を読み直して中身の一覧を作る。構成に当てはまらない項目があれば失敗で終える
function Get-ZipListing([string]$ZipPath, [string]$Label) {
    $items = @()
    $unexpected = @()
    $archive = [IO.Compression.ZipFile]::Open($ZipPath, [IO.Compression.ZipArchiveMode]::Read)
    try {
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName.Contains('\') -or -not (Test-PackagedPath $entry.FullName)) {
                $unexpected += $entry.FullName
            }
            $items += [pscustomobject]@{ Path = $entry.FullName; Length = $entry.Length }
        }
    }
    finally {
        $archive.Dispose()
    }
    if ($unexpected.Count -gt 0) {
        Exit-WithError "$($Label)の zip に配布物の構成に当てはまらない項目があります: $($unexpected -join ', ')"
    }
    return , @($items | Sort-Object Path)
}

try {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    # --- パスの決定 ---
    $repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $publishRoot = Join-Path $repoRoot 'artifacts\publish'
    $packageRoot = Join-Path $repoRoot 'artifacts\package'
    if ([string]::IsNullOrWhiteSpace($SelfContainedDir)) { $SelfContainedDir = Join-Path $publishRoot 'SelfContained' }
    if ([string]::IsNullOrWhiteSpace($FrameworkDependentDir)) { $FrameworkDependentDir = Join-Path $publishRoot 'FrameworkDependent' }
    if ([string]::IsNullOrWhiteSpace($OutputDir)) { $OutputDir = $packageRoot }
    $SelfContainedDir = Resolve-FullPath $SelfContainedDir
    $FrameworkDependentDir = Resolve-FullPath $FrameworkDependentDir
    $OutputDir = Resolve-FullPath $OutputDir

    # --- 発行フォルダの一覧と検査（zip を作る前） ---
    Write-Host "発行フォルダを調べます。"
    Write-Host "  自己完結版: $SelfContainedDir"
    Write-Host "  軽量版    : $FrameworkDependentDir"
    $scListing = Get-PublishListing $SelfContainedDir '自己完結版'
    $fdListing = Get-PublishListing $FrameworkDependentDir '軽量版'
    Assert-Listings $scListing $fdListing '発行フォルダ'

    # --- 出力先の準備 ---
    if (Test-Path -LiteralPath $OutputDir) {
        if (-not (Test-Path -LiteralPath $OutputDir -PathType Container)) {
            Exit-WithError "出力先がフォルダではありません: $OutputDir"
        }
    }
    else {
        New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    }
    $isPackageRoot = ($OutputDir -ieq $packageRoot) -or $OutputDir.StartsWith($packageRoot + '\', [StringComparison]::OrdinalIgnoreCase)
    $scZip = Join-Path $OutputDir $SelfContainedZipName
    $fdZip = Join-Path $OutputDir $FrameworkDependentZipName
    foreach ($zip in @($scZip, $fdZip)) {
        if (Test-Path -LiteralPath $zip) {
            if (-not $isPackageRoot) {
                Exit-WithError "出力先に同じ名前のファイルが既にあります: $zip（artifacts/package の外のファイルは消さないので、別の出力先を指定するか、そのファイルを移してください）"
            }
        }
    }
    foreach ($zip in @($scZip, $fdZip)) {
        if (Test-Path -LiteralPath $zip) {
            # 前回の zip が残っていると成功と取り違えるので、消して作り直す
            Write-Host "前回の zip を消します: $zip"
            Remove-Item -LiteralPath $zip -Force
        }
    }

    # --- zip の作成 ---
    Write-Host "zip を作ります: $scZip"
    New-PackageZip $scListing $scZip
    Write-Host "zip を作ります: $fdZip"
    New-PackageZip $fdListing $fdZip

    # --- zip を読み直して検査（zip を作った後） ---
    $scZipListing = Get-ZipListing $scZip '自己完結版'
    $fdZipListing = Get-ZipListing $fdZip '軽量版'
    Assert-Listings $scZipListing $fdZipListing 'zip'
    # zip の中身が発行フォルダから選んだ一覧と同じであることを確かめる
    foreach ($pair in @(@('自己完結版', $scListing, $scZipListing), @('軽量版', $fdListing, $fdZipListing))) {
        $expected = @($pair[1] | ForEach-Object { "$($_.Path)|$($_.Length)" }) -join "`n"
        $actual = @($pair[2] | ForEach-Object { "$($_.Path)|$($_.Length)" }) -join "`n"
        if ($expected -cne $actual) {
            Exit-WithError "$($pair[0]) の zip の中身が発行フォルダから選んだファイルと一致しません: $(if ($pair[0] -eq '自己完結版') { $scZip } else { $fdZip })"
        }
    }

    # --- 結果の表示 ---
    Write-Host ''
    Write-Host '配布用の zip を作り、検査に通りました。'
    foreach ($pair in @(@('自己完結版（既定）', $scZip, $scZipListing), @('軽量版', $fdZip, $fdZipListing))) {
        Write-Host ('  {0}: {1}（{2:N0} バイト、{3} ファイル）' -f $pair[0], $pair[1], (Get-Item -LiteralPath $pair[1]).Length, $pair[2].Count)
    }
    Write-Host '  中身（大きさは自己完結版 / 軽量版）:'
    $fdMap = @{}
    foreach ($item in $fdZipListing) { $fdMap[$item.Path] = $item.Length }
    foreach ($item in $scZipListing) {
        if ($item.Length -eq $fdMap[$item.Path]) {
            Write-Host ('    {0,15:N0} バイト  {1}' -f $item.Length, $item.Path)
        }
        else {
            Write-Host ('    {0,15:N0} / {1:N0} バイト  {2}' -f $item.Length, $fdMap[$item.Path], $item.Path)
        }
    }
    exit 0
}
catch {
    Exit-WithError "梱包の途中で失敗しました: $($_.Exception.Message)"
}
