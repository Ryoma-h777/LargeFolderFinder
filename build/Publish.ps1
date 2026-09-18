<#
.SYNOPSIS
    Large Folder Finder を2つの形態のどちらかで発行する。

.DESCRIPTION
    LargeFolderFinder.csproj を win-x64 向けの単一ファイルとして発行する。
    - SelfContained      : ランタイムを同梱する自己完結版（既定の配布物）
    - FrameworkDependent : .NET 10 Desktop Runtime の導入を前提とする軽量版
    フレームワーク依存版は --no-self-contained で指定する（--self-contained false は
    SDK の不具合 dotnet/sdk#51888 で自己完結になることがあるため使わない）。
    単一ファイルの圧縮は使わない（zip が縮まず、起動が遅くなるだけのため）。
    Windows PowerShell 5.1 でも動く書き方にしている。失敗したときは 0 以外の終了コードで終わる。

.PARAMETER Form
    発行の形態。SelfContained または FrameworkDependent（必須）。

.PARAMETER OutputDir
    発行先のフォルダ。省略時はリポジトリ直下の artifacts/publish/<Form>。
    artifacts/publish 配下のフォルダは発行の前に中身を消して作り直す。
    それ以外の場所は、存在しないか空のフォルダだけを受け付ける（利用者のファイルを消さないため）。

.PARAMETER Configuration
    ビルドの構成。Release（既定）または Debug。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File build/Publish.ps1 -Form SelfContained

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File build/Publish.ps1 -Form FrameworkDependent -OutputDir artifacts/publish/fd
#>
param(
    [string]$Form,
    [string]$OutputDir,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

# エラーを標準エラーに出して、終了コード 1 で終える
function Exit-WithError([string]$Message) {
    [Console]::Error.WriteLine("エラー: $Message")
    exit 1
}

try {
    # --- 引数の検証 ---
    $forms = @('SelfContained', 'FrameworkDependent')
    if ([string]::IsNullOrWhiteSpace($Form)) {
        Exit-WithError "-Form を指定してください（$($forms -join ' / ')）。"
    }
    # 大文字小文字の違いは許し、正規の綴りにそろえる
    $matchedForm = $forms | Where-Object { $_ -ieq $Form } | Select-Object -First 1
    if (-not $matchedForm) {
        Exit-WithError "-Form の値 '$Form' は使えません。$($forms -join ' / ') のどちらかを指定してください。"
    }
    $Form = $matchedForm

    $configurations = @('Release', 'Debug')
    $matchedConfiguration = $configurations | Where-Object { $_ -ieq $Configuration } | Select-Object -First 1
    if (-not $matchedConfiguration) {
        Exit-WithError "-Configuration の値 '$Configuration' は使えません。$($configurations -join ' / ') のどちらかを指定してください。"
    }
    $Configuration = $matchedConfiguration

    # --- パスの決定 ---
    $repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $projectPath = Join-Path $repoRoot 'LargeFolderFinder.csproj'
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        Exit-WithError "プロジェクトファイルが見つかりません: $projectPath"
    }
    $publishRoot = Join-Path $repoRoot 'artifacts\publish'

    if ([string]::IsNullOrWhiteSpace($OutputDir)) {
        $OutputDir = Join-Path $publishRoot $Form
    }
    # 相対パスは現在の場所を基準に絶対パスへ直す
    $OutputDir = [IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDir)).TrimEnd('\')

    # --- 発行先の準備 ---
    $isUnderPublishRoot = $OutputDir.StartsWith($publishRoot + '\', [StringComparison]::OrdinalIgnoreCase)
    if (Test-Path -LiteralPath $OutputDir) {
        if (-not (Test-Path -LiteralPath $OutputDir -PathType Container)) {
            Exit-WithError "発行先がフォルダではありません: $OutputDir"
        }
        if ($isUnderPublishRoot) {
            # 前回の発行物が残っていると成功と取り違えるので、消して作り直す
            Write-Host "前回の発行物を消します: $OutputDir"
            Remove-Item -LiteralPath $OutputDir -Recurse -Force
        }
        elseif (@(Get-ChildItem -LiteralPath $OutputDir -Force).Count -gt 0) {
            Exit-WithError "発行先のフォルダが空ではありません: $OutputDir（artifacts/publish の外のフォルダは消さないので、空のフォルダか存在しないフォルダを指定してください）"
        }
    }

    # --- dotnet publish の実行 ---
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Exit-WithError 'dotnet コマンドが見つかりません。.NET SDK（global.json の版）を導入してください。'
    }

    $publishArgs = @('publish', $projectPath, '-c', $Configuration, '-r', 'win-x64')
    if ($Form -eq 'SelfContained') {
        $publishArgs += @('--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true')
    }
    else {
        # --self-contained false は使わない（dotnet/sdk#51888）
        $publishArgs += @('--no-self-contained', '-p:PublishSingleFile=true')
    }
    $publishArgs += @('-o', $OutputDir)

    Write-Host "発行します（形態: $Form、構成: $Configuration）: $OutputDir"
    Write-Host "dotnet $($publishArgs -join ' ')"
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        Exit-WithError "dotnet publish が失敗しました（終了コード $LASTEXITCODE）。"
    }

    # --- 発行物の確認と一覧 ---
    $exePath = Join-Path $OutputDir 'LargeFolderFinder.exe'
    if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
        Exit-WithError "dotnet publish は成功を返しましたが、実行ファイルがありません: $exePath"
    }

    $versionInfo = (Get-Item -LiteralPath $exePath).VersionInfo
    Write-Host ''
    Write-Host "発行しました（形態: $Form）: $OutputDir"
    Write-Host "  実行ファイルの版（ProductVersion）: $($versionInfo.ProductVersion)"
    Write-Host '  発行物:'
    Get-ChildItem -LiteralPath $OutputDir -Recurse -File | Sort-Object FullName | ForEach-Object {
        $relative = $_.FullName.Substring($OutputDir.Length + 1)
        Write-Host ('    {0,15:N0} バイト  {1}' -f $_.Length, $relative)
    }
    exit 0
}
catch {
    Exit-WithError "発行の途中で失敗しました: $($_.Exception.Message)"
}
