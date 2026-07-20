[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-arm64")]
    [string] $Runtime = "win-x64",
    [string] $InnoSetupCompiler,
    [string] $SignToolPath,
    [string] $SigningCertificateThumbprint,
    [string] $TimestampServer = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "release-common.ps1")
$releaseInfo = Get-GameSaveManagerReleaseInfo -RepositoryRoot $repositoryRoot
$Version = $releaseInfo.Version
$signingEnabled = -not [string]::IsNullOrWhiteSpace($SignToolPath) -and
    -not [string]::IsNullOrWhiteSpace($SigningCertificateThumbprint)
if ((-not [string]::IsNullOrWhiteSpace($SignToolPath)) -xor
    (-not [string]::IsNullOrWhiteSpace($SigningCertificateThumbprint)))
{
    throw "SignToolPath and SigningCertificateThumbprint must be provided together."
}
if ($signingEnabled)
{
    if (-not (Test-Path -LiteralPath $SignToolPath)) { throw "SignTool was not found: $SignToolPath" }
    $SigningCertificateThumbprint = $SigningCertificateThumbprint.Replace(" ", "")
    if ($SigningCertificateThumbprint -notmatch '^[0-9A-Fa-f]{40}$')
    {
        throw "SigningCertificateThumbprint must be a 40-character SHA-1 certificate thumbprint used only for certificate selection."
    }
    if (-not [Uri]::IsWellFormedUriString($TimestampServer, [UriKind]::Absolute))
    {
        throw "TimestampServer must be an absolute URL."
    }
}
& (Join-Path $PSScriptRoot "publish-windows.ps1") -Runtime $Runtime
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
$mainExecutable = Join-Path $publishDirectory "GameSaveManager.exe"
$bootstrapperExecutable = Join-Path $publishDirectory "GameSaveManager.UpdateBootstrapper.exe"
$compilerArguments = @("/DMyAppVersion=$Version", "/DPublishDir=$publishDirectory")
if ($signingEnabled)
{
    foreach ($executable in @($mainExecutable, $bootstrapperExecutable))
    {
        & $SignToolPath sign /sha1 $SigningCertificateThumbprint /fd SHA256 /tr $TimestampServer /td SHA256 /d "GameSave Manager" $executable
        if ($LASTEXITCODE -ne 0) { throw "Signing failed for $executable with exit code $LASTEXITCODE" }
        & $SignToolPath verify /pa /all /tw $executable
        if ($LASTEXITCODE -ne 0) { throw "Authenticode verification failed for $executable." }
    }

    $signCommand = '$q' + $SignToolPath + '$q sign /sha1 ' + $SigningCertificateThumbprint +
        ' /fd SHA256 /tr ' + $TimestampServer + ' /td SHA256 /d $qGameSave Manager$q $f'
    $compilerArguments += "/DSignToolName=gamesavemanager"
    $compilerArguments += "/Sgamesavemanager=$signCommand"
}
$compilerArguments += $installerDefinition
& $InnoSetupCompiler $compilerArguments
if ($LASTEXITCODE -ne 0)
{
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
}

$installerPath = Join-Path $repositoryRoot "artifacts\installer\GameSaveManager-Setup-$Version.exe"
if (-not (Test-Path -LiteralPath $installerPath))
{
    throw "Installer was not generated at the expected path: $installerPath"
}

if ($signingEnabled)
{
    & $SignToolPath verify /pa /all /tw $installerPath
    if ($LASTEXITCODE -ne 0) { throw "Authenticode verification failed for the installer." }
}

$installerFile = Get-Item -LiteralPath $installerPath
$installerHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installerFile.FullName).Hash.ToLowerInvariant()
$checksumPath = Join-Path $installerFile.DirectoryName "SHA256SUMS.txt"
"$installerHash  $($installerFile.Name)" | Set-Content -LiteralPath $checksumPath -Encoding ascii
Write-Host "Installer: $($installerFile.FullName)"
Write-Host "SHA-256: $installerHash"
Write-Host "Checksum file: $checksumPath"
