<#
.SYNOPSIS
  Testet, veröffentlicht und verpackt SlicerSync.

.DESCRIPTION
  1. dotnet test
  2. dotnet publish → publish\SlicerSync.exe (eigenständige Einzeldatei, win-x64)
  3. Inno Setup → dist\SlicerSync-Setup-<Version>.exe
     Die aktuelle Git-for-Windows-Version (URL + SHA-256) wird bei GitHub abgefragt;
     ohne Internet gelten die Standardwerte aus installer\SlicerSync.iss.

.EXAMPLE
  .\build.ps1
  .\build.ps1 -SkipTests -SkipInstaller
#>
param(
    [string]$Version = "1.0.0",
    [switch]$SkipTests,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

function Find-Tool([string]$name, [string[]]$candidates) {
    $cmd = Get-Command $name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    throw "$name wurde nicht gefunden."
}

$dotnet = Find-Tool "dotnet" @("$env:ProgramFiles\dotnet\dotnet.exe")

if (-not $SkipTests) {
    Write-Host "== Tests ==" -ForegroundColor Cyan
    & $dotnet test tests\SlicerSync.Core.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw "Tests fehlgeschlagen." }
}

Write-Host "== Veröffentlichen ==" -ForegroundColor Cyan
if (Test-Path publish) { Remove-Item publish -Recurse -Force }
& $dotnet publish src\SlicerSync.App -c Release -o publish -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "Veröffentlichen fehlgeschlagen." }
$exe = Get-Item publish\SlicerSync.exe
Write-Host ("publish\SlicerSync.exe ({0:N1} MB)" -f ($exe.Length / 1MB))

if ($SkipInstaller) { return }

Write-Host "== Installer ==" -ForegroundColor Cyan
$iscc = Find-Tool "ISCC.exe" @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")

$defines = @("/DAppVersion=$Version")
try {
    $release = Invoke-RestMethod "https://api.github.com/repos/git-for-windows/git/releases/latest" -Headers @{ "User-Agent" = "slicersync-build" }
    $asset = $release.assets | Where-Object name -match '^Git-.*-64-bit\.exe$' | Select-Object -First 1
    if ($asset -and $asset.digest -match '^sha256:([0-9a-f]{64})$') {
        $gitVersion = $release.tag_name -replace '^v', '' -replace '\.windows\.1$', '' -replace '\.windows\.', '.'
        $defines += "/DGitVersion=$gitVersion", "/DGitUrl=$($asset.browser_download_url)", "/DGitSha256=$($Matches[1])"
        Write-Host "Git for Windows: $($asset.name)"
    }
} catch {
    Write-Warning "Git-Version konnte nicht abgefragt werden, verwende Standardwerte aus dem Skript. ($_)"
}

& $iscc @defines installer\SlicerSync.iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup fehlgeschlagen." }
Get-ChildItem dist\*.exe | ForEach-Object { Write-Host ("{0} ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB)) -ForegroundColor Green }
