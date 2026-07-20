[CmdletBinding()]
param(
    [string] $Subject = "CN=GameSave Manager Self-Signed Publisher",
    [ValidateRange(1, 10)]
    [int] $ValidYears = 5,
    [string] $PrivateDirectory,
    [string] $PublicCertificatePath,
    [string] $MetadataPath
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PrivateDirectory))
{
    $profile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    $PrivateDirectory = Join-Path $profile ".gamesavemanager-secrets"
}
if ([string]::IsNullOrWhiteSpace($PublicCertificatePath))
{
    $PublicCertificatePath = Join-Path $repositoryRoot "certificates\GameSaveManager-Publisher.cer"
}
if ([string]::IsNullOrWhiteSpace($MetadataPath))
{
    $MetadataPath = Join-Path $repositoryRoot "certificates\GameSaveManager-Publisher.json"
}

$PrivateDirectory = [IO.Path]::GetFullPath($PrivateDirectory)
$PublicCertificatePath = [IO.Path]::GetFullPath($PublicCertificatePath)
$MetadataPath = [IO.Path]::GetFullPath($MetadataPath)
$privatePfxPath = Join-Path $PrivateDirectory "GameSaveManager-Publisher.pfx"
$protectedPasswordPath = Join-Path $PrivateDirectory "GameSaveManager-Publisher.password.dpapi"
foreach ($path in @($privatePfxPath, $protectedPasswordPath, $PublicCertificatePath, $MetadataPath))
{
    if (Test-Path -LiteralPath $path) { throw "Refusing to overwrite existing certificate material: $path" }
}
[IO.Directory]::CreateDirectory($PrivateDirectory) | Out-Null
[IO.Directory]::CreateDirectory((Split-Path -Parent $PublicCertificatePath)) | Out-Null

$passwordBytes = [byte[]]::new(48)
$random = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $random.GetBytes($passwordBytes) }
finally { $random.Dispose() }
$plainPassword = [Convert]::ToBase64String($passwordBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
$securePassword = ConvertTo-SecureString $plainPassword -AsPlainText -Force
$certificate = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -FriendlyName "GameSave Manager Self-Signed Code Signing" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -KeyAlgorithm RSA `
    -KeyLength 3072 `
    -HashAlgorithm SHA256 `
    -KeyExportPolicy Exportable `
    -KeyUsage DigitalSignature `
    -NotAfter (Get-Date).AddYears($ValidYears)

try
{
    Export-PfxCertificate -Cert $certificate -FilePath $privatePfxPath -Password $securePassword | Out-Null
    Export-Certificate -Cert $certificate -FilePath $PublicCertificatePath -Type CERT | Out-Null
    $securePassword | ConvertFrom-SecureString | Set-Content -LiteralPath $protectedPasswordPath -Encoding ascii

    $publisherSha256 = $certificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256).ToLowerInvariant()
    $certificateFileSha256 = (Get-FileHash -LiteralPath $PublicCertificatePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [ordered]@{
        schemaVersion = 1
        subject = $certificate.Subject
        friendlyName = $certificate.FriendlyName
        sha1Thumbprint = $certificate.Thumbprint.ToLowerInvariant()
        publisherCertificateSha256 = $publisherSha256
        certificateFileSha256 = $certificateFileSha256
        notBeforeUtc = $certificate.NotBefore.ToUniversalTime().ToString("O")
        notAfterUtc = $certificate.NotAfter.ToUniversalTime().ToString("O")
        keyAlgorithm = "RSA"
        keySize = 3072
        signatureHashAlgorithm = "SHA256"
        codeSigningEku = "1.3.6.1.5.5.7.3.3"
        trustScope = "CurrentUser"
        trustModel = "self-signed"
    } | ConvertTo-Json | Set-Content -LiteralPath $MetadataPath -Encoding utf8

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
    foreach ($privatePath in @($privatePfxPath, $protectedPasswordPath))
    {
        $acl = [Security.AccessControl.FileSecurity]::new()
        $acl.SetOwner($identity)
        $acl.SetAccessRuleProtection($true, $false)
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            $identity,
            [Security.AccessControl.FileSystemRights]::FullControl,
            [Security.AccessControl.AccessControlType]::Allow))
        Set-Acl -LiteralPath $privatePath -AclObject $acl
    }

    Write-Host "Public certificate: $PublicCertificatePath"
    Write-Host "Publisher certificate SHA-256: $publisherSha256"
    Write-Host "Private PFX stored outside repository: $privatePfxPath"
    Write-Host "PFX password protected for the current Windows user: $protectedPasswordPath"
}
finally
{
    $plainPassword = $null
    [Array]::Clear($passwordBytes, 0, $passwordBytes.Length)
}
