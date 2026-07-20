[CmdletBinding()]
param(
    [string] $PrivateKeyPath,
    [string] $PublicKeyPath
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PrivateKeyPath))
{
    $userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    $PrivateKeyPath = Join-Path $userProfile ".gamesavemanager-secrets\update-manifest-private-key.pk8"
}
if ([string]::IsNullOrWhiteSpace($PublicKeyPath))
{
    $PublicKeyPath = Join-Path $repositoryRoot "src\GameSaveManager.Infrastructure\Updates\Assets\update-signing-public-key.pem"
}

$PrivateKeyPath = [IO.Path]::GetFullPath($PrivateKeyPath)
$PublicKeyPath = [IO.Path]::GetFullPath($PublicKeyPath)
foreach ($path in @($PrivateKeyPath, $PublicKeyPath))
{
    if (Test-Path -LiteralPath $path)
    {
        throw "Refusing to overwrite an existing signing key: $path"
    }
    $directory = Split-Path -Parent $path
    [IO.Directory]::CreateDirectory($directory) | Out-Null
}

$releaseTool = Join-Path $repositoryRoot "tools\GameSaveManager.ReleaseTool\GameSaveManager.ReleaseTool.csproj"
dotnet run --project $releaseTool --configuration Release -- `
    generate-key `
    --private $PrivateKeyPath `
    --public $PublicKeyPath
if ($LASTEXITCODE -ne 0)
{
    throw "Update signing key generation failed with exit code $LASTEXITCODE"
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
$acl = [Security.AccessControl.FileSecurity]::new()
$acl.SetOwner($identity)
$acl.SetAccessRuleProtection($true, $false)
$rule = [Security.AccessControl.FileSystemAccessRule]::new(
    $identity,
    [Security.AccessControl.FileSystemRights]::FullControl,
    [Security.AccessControl.AccessControlType]::Allow)
$acl.AddAccessRule($rule)
Set-Acl -LiteralPath $PrivateKeyPath -AclObject $acl

Write-Host "Private update-manifest key created outside the repository: $PrivateKeyPath"
Write-Host "Public verification key created: $PublicKeyPath"
Write-Host "Back up the private key securely, then store its Base64 content in the UPDATE_MANIFEST_SIGNING_KEY_BASE64 GitHub secret."
