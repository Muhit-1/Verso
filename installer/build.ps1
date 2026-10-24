<#
  Builds the installer:   pwsh installer/build.ps1 [-Version 0.1.0]

  1. publishes Verso (self-contained, so the user needs no .NET installed; ReadyToRun for a fast start)
  2. compiles installer/Verso.iss with Inno Setup (https://jrsoftware.org/isinfo.php, `choco install innosetup`)

  Output: artifacts/installer/Verso-Setup-<version>.exe
#>
param(
    [string]$Version = "0.1.0",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root "artifacts/publish/Verso"

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

dotnet publish (Join-Path $root "src/Verso.App") `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:Version=$Version -p:DebugType=none `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$iscc = (Get-Command iscc -ErrorAction SilentlyContinue)?.Source
if (-not $iscc) {
    $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
}
if (-not (Test-Path $iscc)) { throw "Inno Setup was not found. Install it with: choco install innosetup" }

& $iscc "/DAppVersion=$Version" "/DPublishDir=$publish" (Join-Path $root "installer/Verso.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }

Write-Host "Installer: $(Join-Path $root "artifacts/installer/Verso-Setup-$Version.exe")"
