[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-arm64")]
    [string] $Runtime = "win-x64",
    [string] $InnoSetupCompiler,
    [string] $SignToolPath,
    [string] $SigningCertificateThumbprint,
    [string] $TimestampServer = "http://timestamp.digicert.com",
    [switch] $AllowUntrustedSelfSignedPublisher,
    [string] $ExpectedPublisherCertificateSha256
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
    if ($AllowUntrustedSelfSignedPublisher -and
        $ExpectedPublisherCertificateSha256 -notmatch '^[0-9A-Fa-f]{64}$')
    {
        throw "ExpectedPublisherCertificateSha256 must be provided when allowing an untrusted self-signed publisher."
    }
}

function Assert-AuthenticodeSignature
{
    param([Parameter(Mandatory = $true)][string] $Path)

    $verifyOutput = @(& $SignToolPath verify /pa /all /tw /v $Path 2>&1)
    $verifyExitCode = $LASTEXITCODE
    $verifyOutput | ForEach-Object { Write-Host $_ }
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($null -eq $signature.SignerCertificate)
    {
        throw "Authenticode signature is missing from $Path."
    }
    if ($null -eq $signature.TimeStamperCertificate)
    {
        throw "Trusted timestamp is missing from $Path."
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedPublisherCertificateSha256))
    {
        $actualPublisherSha256 = $signature.SignerCertificate.GetCertHashString(
            [Security.Cryptography.HashAlgorithmName]::SHA256).ToLowerInvariant()
        if ($actualPublisherSha256 -ne $ExpectedPublisherCertificateSha256.ToLowerInvariant())
        {
            throw "Authenticode publisher certificate does not match the pinned SHA-256 for $Path."
        }
    }

    if ($verifyExitCode -eq 0)
    {
        if ($signature.Status -ne [Management.Automation.SignatureStatus]::Valid)
        {
            throw "PowerShell Authenticode verification disagrees with SignTool for ${Path}: $($signature.StatusMessage)"
        }
        return
    }

    if (-not $AllowUntrustedSelfSignedPublisher)
    {
        throw "Authenticode verification failed for $Path."
    }

    $combinedOutput = ($verifyOutput | Out-String)
    $onlyUntrustedRoot = $combinedOutput -match '(?is)terminated in a root\s+certificate which is not trusted' -or
        $signature.StatusMessage -match '(?i)0x800B0109|root certificate.*not trusted'
    $timestampReported = $combinedOutput -match '(?i)signature is timestamped|Timestamp Verified by|Timestamp\s+Authenticode'
    $allowedStatus = $signature.Status -in @(
        [Management.Automation.SignatureStatus]::NotTrusted,
        [Management.Automation.SignatureStatus]::UnknownError)
    if (-not $onlyUntrustedRoot -or -not $timestampReported -or -not $allowedStatus)
    {
        throw "Authenticode verification failed for a reason other than the pinned self-signed root for ${Path}: $($signature.StatusMessage)"
    }

    $timestampChain = [Security.Cryptography.X509Certificates.X509Chain]::new()
    try
    {
        $timestampChain.ChainPolicy.RevocationMode =
            [Security.Cryptography.X509Certificates.X509RevocationMode]::Online
        if (-not $timestampChain.Build($signature.TimeStamperCertificate))
        {
            $errors = ($timestampChain.ChainStatus | ForEach-Object StatusInformation) -join '; '
            throw "Timestamp certificate chain verification failed for ${Path}: $errors"
        }
    }
    finally
    {
        $timestampChain.Dispose()
    }

    Write-Host "Authenticode signature, pinned self-signed publisher and trusted timestamp verified: $Path"
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
        & $SignToolPath sign /sha1 $SigningCertificateThumbprint /fd SHA256 /t $TimestampServer /d "GameSave Manager" $executable
        if ($LASTEXITCODE -ne 0) { throw "Signing failed for $executable with exit code $LASTEXITCODE" }
        Assert-AuthenticodeSignature -Path $executable
    }

    $signCommand = '$q' + $SignToolPath + '$q sign /sha1 ' + $SigningCertificateThumbprint +
        ' /fd SHA256 /t ' + $TimestampServer + ' /d $qGameSave Manager$q $f'
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
    Assert-AuthenticodeSignature -Path $installerPath
}

$installerFile = Get-Item -LiteralPath $installerPath
$installerHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installerFile.FullName).Hash.ToLowerInvariant()
$checksumPath = Join-Path $installerFile.DirectoryName "SHA256SUMS.txt"
"$installerHash  $($installerFile.Name)" | Set-Content -LiteralPath $checksumPath -Encoding ascii
Write-Host "Installer: $($installerFile.FullName)"
Write-Host "SHA-256: $installerHash"
Write-Host "Checksum file: $checksumPath"
