[CmdletBinding()]
param(
    [string]$PfxPath = "$env:USERPROFILE\.gamesavemanager-secrets\GameSaveManager-Publisher.pfx",
    [string]$ProtectedPasswordPath = "$env:USERPROFILE\.gamesavemanager-secrets\GameSaveManager-Publisher.password.dpapi",
    [string]$ManifestPrivateKeyPath = "$env:USERPROFILE\.gamesavemanager-secrets\update-manifest-private-key.pk8",
    [string]$PublicCertificatePath = (Join-Path $PSScriptRoot '..\certificates\GameSaveManager-Publisher.cer')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Set-GitHubSecretFromStandardInput {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$Value
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'gh'
    $startInfo.Arguments = "secret set $Name"
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        $process.StandardInput.Write($Value)
        $process.StandardInput.Close()
        $output = $process.StandardOutput.ReadToEnd()
        $errorOutput = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "写入 GitHub Secret $Name 失败：$errorOutput$output"
        }
    }
    finally {
        $process.Dispose()
    }
}

foreach ($path in @($PfxPath, $ProtectedPasswordPath, $ManifestPrivateKeyPath, $PublicCertificatePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "缺少发布密钥文件：$path"
    }
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw '未找到 GitHub CLI。请先安装 gh 并运行 gh auth login。'
}

& gh auth status *> $null
if ($LASTEXITCODE -ne 0) {
    throw 'GitHub CLI 尚未登录。请先运行 gh auth login。'
}

$protectedPassword = (Get-Content -LiteralPath $ProtectedPasswordPath -Raw -Encoding UTF8).Trim()
$securePassword = ConvertTo-SecureString $protectedPassword
$credential = [System.Net.NetworkCredential]::new('', $securePassword)
$plainPassword = $credential.Password

try {
    $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
    $pfxCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($PfxPath, $plainPassword, $flags)
    $publicCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($PublicCertificatePath)
    try {
        $pfxSha256 = $pfxCertificate.GetCertHashString(
            [Security.Cryptography.HashAlgorithmName]::SHA256).ToUpperInvariant()
        $publicSha256 = $publicCertificate.GetCertHashString(
            [Security.Cryptography.HashAlgorithmName]::SHA256).ToUpperInvariant()
        if ($pfxSha256 -ne $publicSha256) {
            throw 'PFX 中的证书与仓库公开证书不一致，拒绝写入 GitHub Secrets。'
        }

        Write-Host "证书校验通过：$publicSha256"
        Write-Host '正在安全写入 GitHub Actions Secrets；不会显示密码或私钥内容。'

        Set-GitHubSecretFromStandardInput -Name 'WINDOWS_SIGNING_PFX_BASE64' -Value ([Convert]::ToBase64String([IO.File]::ReadAllBytes($PfxPath)))
        Set-GitHubSecretFromStandardInput -Name 'WINDOWS_SIGNING_PFX_PASSWORD' -Value $plainPassword
        Set-GitHubSecretFromStandardInput -Name 'UPDATE_MANIFEST_SIGNING_KEY_BASE64' -Value ([Convert]::ToBase64String([IO.File]::ReadAllBytes($ManifestPrivateKeyPath)))

        Write-Host '三个发布 Secret 已更新。可以运行 release 工作流或创建版本标签。'
    }
    finally {
        $pfxCertificate.Dispose()
        $publicCertificate.Dispose()
    }
}
finally {
    $plainPassword = $null
    $protectedPassword = $null
    $credential = $null
    $securePassword = $null
    [GC]::Collect()
}
