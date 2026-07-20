#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $CertificatePath = (Join-Path $PSScriptRoot "GameSaveManager-Publisher.cer"),
    [switch] $Force
)

$ErrorActionPreference = "Stop"
$expectedSubject = "CN=GameSave Manager Self-Signed Publisher"
$expectedPublisherSha256 = "14768bc7d3cf2b1eb5bbe3228adc8a3d35a1f923cb806b64147d7cfd3bca8e35"
$CertificatePath = [IO.Path]::GetFullPath($CertificatePath)
if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf))
{
    throw "找不到发布者证书：$CertificatePath"
}

$publicCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($CertificatePath)
try
{
    $actualSha256 = $publicCertificate.GetCertHashString(
        [Security.Cryptography.HashAlgorithmName]::SHA256).ToLowerInvariant()
    if ($actualSha256 -cne $expectedPublisherSha256 -or $publicCertificate.Subject -cne $expectedSubject)
    {
        throw "公开证书与 GameSave Manager 固定发布者不一致，拒绝执行移除。"
    }
}
finally
{
    $publicCertificate.Dispose()
}

Write-Warning "移除后，Windows 和 GameSave Manager 将不再信任使用该自签证书签署的新版本。"
Write-Host "待移除证书 SHA-256：$expectedPublisherSha256"
if (-not $Force)
{
    $confirmation = Read-Host "确认移除后输入 REMOVE"
    if ($confirmation -cne "REMOVE") { throw "用户取消了证书移除。" }
}

$removed = 0
foreach ($store in @("Cert:\CurrentUser\Root", "Cert:\CurrentUser\TrustedPublisher"))
{
    foreach ($certificate in @(Get-ChildItem $store | Where-Object {
        $_.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256).ToLowerInvariant() -eq $expectedPublisherSha256
    }))
    {
        Remove-Item -LiteralPath $certificate.PSPath -Force
        $removed++
    }
}
Write-Host "已移除 $removed 个 GameSave Manager 当前用户证书库条目。" -ForegroundColor Green
