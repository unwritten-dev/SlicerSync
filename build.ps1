<#
.SYNOPSIS
  Tests, publishes and packages SlicerSync.

.DESCRIPTION
  1. dotnet test
  2. dotnet publish → publish\SlicerSync.exe (self-contained single file, win-x64)
  3. Inno Setup → dist\SlicerSync-Setup-<version>.exe
     The current Git for Windows version (URL + SHA-256) is queried from GitHub;
     without internet access the defaults in installer\SlicerSync.iss are used.

.EXAMPLE
  .\build.ps1
  .\build.ps1 -Version 1.1.0
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
    throw "$name was not found."
}

$dotnet = Find-Tool "dotnet" @("$env:ProgramFiles\dotnet\dotnet.exe")

if (-not $SkipTests) {
    Write-Host "== Tests ==" -ForegroundColor Cyan
    & $dotnet test tests\SlicerSync.Core.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}

Write-Host "== Publish ==" -ForegroundColor Cyan
if (Test-Path publish) { Remove-Item publish -Recurse -Force }
& $dotnet publish src\SlicerSync.App -c Release -o publish -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
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
    $headers = @{ "User-Agent" = "slicersync-build" }
    if ($env:GITHUB_TOKEN) { $headers["Authorization"] = "Bearer $env:GITHUB_TOKEN" }
    $release = Invoke-RestMethod "https://api.github.com/repos/git-for-windows/git/releases/latest" -Headers $headers
    $asset = $release.assets | Where-Object name -match '^Git-.*-64-bit\.exe$' | Select-Object -First 1
    if ($asset -and $asset.digest -match '^sha256:([0-9a-f]{64})$') {
        $gitVersion = $release.tag_name -replace '^v', '' -replace '\.windows\.1$', '' -replace '\.windows\.', '.'
        $defines += "/DGitVersion=$gitVersion", "/DGitUrl=$($asset.browser_download_url)", "/DGitSha256=$($Matches[1])"
        Write-Host "Git for Windows: $($asset.name)"
    }
} catch {
    Write-Warning "Could not query the Git version, using the defaults from the script. ($_)"
}

& $iscc @defines installer\SlicerSync.iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed." }
Get-ChildItem dist\*.exe | ForEach-Object { Write-Host ("{0} ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB)) -ForegroundColor Green }
