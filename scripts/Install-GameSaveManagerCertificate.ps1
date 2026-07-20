#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $CertificatePath = (Join-Path $PSScriptRoot "GameSaveManager-Publisher.cer"),
    [string] $InstallerPath,
    [switch] $Force
)

$ErrorActionPreference = "Stop"
$expectedSubject = "CN=GameSave Manager Self-Signed Publisher"
$expectedPublisherSha256 = "14768bc7d3cf2b1eb5bbe3228adc8a3d35a1f923cb806b64147d7cfd3bca8e35"
$codeSigningOid = "1.3.6.1.5.5.7.3.3"
$CertificatePath = [IO.Path]::GetFullPath($CertificatePath)
if (-not (Test-Path -LiteralPath $CertificatePath))
{
    throw "找不到发布者证书：$CertificatePath"
}

$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($CertificatePath)
try
{
    $actualPublisherSha256 = $certificate.GetCertHashString(
        [Security.Cryptography.HashAlgorithmName]::SHA256).ToLowerInvariant()
    if ($actualPublisherSha256 -cne $expectedPublisherSha256)
    {
        throw "证书 SHA-256 指纹不匹配，已拒绝安装。实际值：$actualPublisherSha256"
    }
    if ($certificate.Subject -cne $expectedSubject) { throw "证书发布者名称不匹配。" }
    if ($certificate.HasPrivateKey) { throw "公开证书不应包含私钥。" }
    if ([DateTime]::Now -lt $certificate.NotBefore -or [DateTime]::Now -gt $certificate.NotAfter)
    {
        throw "发布者证书当前不在有效期内。"
    }
    $ekuSource = $certificate.Extensions | Where-Object { $_.Oid.Value -eq "2.5.29.37" } | Select-Object -First 1
    if ($null -eq $ekuSource) { throw "证书缺少代码签名增强用途。" }
    $eku = [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new(
        $ekuSource,
        $ekuSource.Critical)
    if (-not ($eku.EnhancedKeyUsages.Value -contains $codeSigningOid))
    {
        throw "证书不允许用于代码签名。"
    }
    $rsa = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($certificate)
    try
    {
        if ($null -eq $rsa -or $rsa.KeySize -lt 3072) { throw "证书 RSA 公钥强度不足。" }
    }
    finally
    {
        if ($null -ne $rsa) { $rsa.Dispose() }
    }

    $resolvedInstallerPath = $null
    if (-not [string]::IsNullOrWhiteSpace($InstallerPath))
    {
        $resolvedInstallerPath = [IO.Path]::GetFullPath($InstallerPath)
        if (-not (Test-Path -LiteralPath $resolvedInstallerPath -PathType Leaf))
        {
            throw "找不到安装包：$resolvedInstallerPath"
        }
        $untrustedSignature = Get-AuthenticodeSignature -LiteralPath $resolvedInstallerPath
        if ($null -eq $untrustedSignature.SignerCertificate)
        {
            throw "安装包不包含 Authenticode 签名。"
        }
        $untrustedInstallerPublisher = $untrustedSignature.SignerCertificate.GetCertHashString(
            [Security.Cryptography.HashAlgorithmName]::SHA256).ToLowerInvariant()
        if ($untrustedInstallerPublisher -ne $expectedPublisherSha256)
        {
            throw "安装包不是由预期的 GameSave Manager 证书签署。"
        }
    }

    Write-Host "即将信任 GameSave Manager 自签发布者证书。" -ForegroundColor Yellow
    Write-Host "发布者：$($certificate.Subject)"
    Write-Host "SHA-256：$actualPublisherSha256"
    Write-Host "有效期：$($certificate.NotBefore.ToString('yyyy-MM-dd')) 至 $($certificate.NotAfter.ToString('yyyy-MM-dd'))"
    Write-Warning "此操作会让当前 Windows 用户信任由该证书签署的程序。只应使用官方 GitHub Release 中的证书。"
    if (-not $Force)
    {
        $confirmation = Read-Host "确认指纹无误后输入 TRUST"
        if ($confirmation -cne "TRUST") { throw "用户取消了证书安装。" }
    }

    $addedStores = [Collections.Generic.List[string]]::new()
    try
    {
        foreach ($store in @("Cert:\CurrentUser\Root", "Cert:\CurrentUser\TrustedPublisher"))
        {
            $alreadyInstalled = Get-ChildItem $store | Where-Object {
                $_.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256).ToLowerInvariant() -eq $expectedPublisherSha256
            } | Select-Object -First 1
            if ($null -eq $alreadyInstalled)
            {
                Import-Certificate -FilePath $CertificatePath -CertStoreLocation $store | Out-Null
                $addedStores.Add($store)
            }
        }

        if ($null -ne $resolvedInstallerPath)
        {
            $signature = Get-AuthenticodeSignature -LiteralPath $resolvedInstallerPath
            if ($signature.Status -ne "Valid" -or $null -eq $signature.SignerCertificate)
            {
                throw "安装包 Authenticode 验证失败：$($signature.StatusMessage)"
            }
            $installerPublisher = $signature.SignerCertificate.GetCertHashString(
                [Security.Cryptography.HashAlgorithmName]::SHA256).ToLowerInvariant()
            if ($installerPublisher -ne $expectedPublisherSha256)
            {
                throw "安装包不是由预期的 GameSave Manager 证书签署。"
            }
            Write-Host "安装包签名验证通过：$resolvedInstallerPath" -ForegroundColor Green
        }

        Write-Host "GameSave Manager 发布者证书已安装到当前用户证书库。" -ForegroundColor Green
    }
    catch
    {
        foreach ($store in $addedStores)
        {
            foreach ($installedCertificate in @(Get-ChildItem $store | Where-Object {
                $_.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256).ToLowerInvariant() -eq $expectedPublisherSha256
            }))
            {
                Remove-Item -LiteralPath $installedCertificate.PSPath -Force -ErrorAction SilentlyContinue
            }
        }
        throw
    }
}
finally
{
    $certificate.Dispose()
}
