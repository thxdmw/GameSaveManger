[CmdletBinding()]
param(
    [string] $Version = "0.1.0",
    [ValidateSet("win-x64", "win-arm64")]
    [string] $Runtime = "win-x64",
    [string] $InnoSetupCompiler
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot "publish-windows.ps1") -Runtime $Runtime -Version $Version
if ($LASTEXITCODE -ne 0)
{
    throw "Client publish failed; installer generation was stopped."
}

if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler))
{
    $programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    $programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
    $candidates = @(
        (Join-Path $programFilesX86 "Inno Setup 6\ISCC.exe"),
        (Join-Path $programFiles "Inno Setup 6\ISCC.exe")
    )
    $InnoSetupCompiler = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler) -or -not (Test-Path $InnoSetupCompiler))
{
    throw "Inno Setup 6 ISCC.exe was not found. Install Inno Setup or provide -InnoSetupCompiler."
}

$publishDirectory = Join-Path $repositoryRoot "artifacts\publish\$Runtime"
$installerDefinition = Join-Path $repositoryRoot "installer\GameSaveManager.iss"
& $InnoSetupCompiler "/DMyAppVersion=$Version" "/DPublishDir=$publishDirectory" $installerDefinition
if ($LASTEXITCODE -ne 0)
{
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
}

$installerPath = Join-Path $repositoryRoot "artifacts\installer\GameSaveManager-Setup-$Version.exe"
if (-not (Test-Path -LiteralPath $installerPath))
{
    throw "Installer was not generated at the expected path: $installerPath"
}

$installerFile = Get-Item -LiteralPath $installerPath
$installerHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installerFile.FullName).Hash.ToLowerInvariant()
$checksumPath = Join-Path $installerFile.DirectoryName "SHA256SUMS.txt"
"$installerHash  $($installerFile.Name)" | Set-Content -LiteralPath $checksumPath -Encoding ascii
Write-Host "Installer: $($installerFile.FullName)"
Write-Host "SHA-256: $installerHash"
Write-Host "Checksum file: $checksumPath"
