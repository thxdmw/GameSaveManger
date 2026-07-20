[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [ValidateSet("win-x64", "win-arm64")]
    [string] $Runtime = "win-x64",
    [ValidateSet("SelfContained", "FrameworkDependent")]
    [string] $DeploymentMode = "SelfContained",
    [string] $OutputDirectory
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "release-common.ps1")
$releaseInfo = Get-GameSaveManagerReleaseInfo -RepositoryRoot $repositoryRoot
$project = Join-Path $repositoryRoot "src\GameSaveManager.App\GameSaveManager.App.csproj"
if ([string]::IsNullOrWhiteSpace($OutputDirectory))
{
    $folderName = if ($DeploymentMode -eq "SelfContained") { $Runtime } else { "$Runtime-framework-dependent" }
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\publish\$folderName"
}

$selfContained = if ($DeploymentMode -eq "SelfContained") { "true" } else { "false" }
Write-Host "Publishing GameSave Manager $($releaseInfo.Version) with $DeploymentMode to $OutputDirectory"
dotnet publish $project `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained $selfContained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $OutputDirectory

if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$assetSourceDirectory = Join-Path $repositoryRoot "src\GameSaveManager.Infrastructure\Discovery\Assets"
$assetOutputDirectory = Join-Path $OutputDirectory "Assets"
[IO.Directory]::CreateDirectory($assetOutputDirectory) | Out-Null
$requiredAssetNames = @("ludusavi-manifest.yaml", "NOTICE-Ludusavi-manifest.txt")
foreach ($requiredAssetName in $requiredAssetNames)
{
    $sourceAsset = Join-Path $assetSourceDirectory $requiredAssetName
    $publishedAsset = Join-Path $assetOutputDirectory $requiredAssetName
    if (-not (Test-Path -LiteralPath $sourceAsset))
    {
        throw "Required source asset is missing: $sourceAsset"
    }
    Copy-Item -LiteralPath $sourceAsset -Destination $publishedAsset -Force
    if (-not (Test-Path -LiteralPath $publishedAsset))
    {
        throw "Published client is incomplete; required asset is missing: $publishedAsset"
    }
}
Write-Host "Required Ludusavi assets verified."

$bootstrapperProject = Join-Path $repositoryRoot "src\GameSaveManager.UpdateBootstrapper\GameSaveManager.UpdateBootstrapper.csproj"
$bootstrapperOutput = Join-Path $repositoryRoot "artifacts\bootstrapper\$Runtime"
dotnet publish $bootstrapperProject `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=true `
    -p:TrimMode=partial `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $bootstrapperOutput
if ($LASTEXITCODE -ne 0)
{
    throw "Update bootstrapper publish failed with exit code $LASTEXITCODE"
}
$bootstrapperExecutable = Join-Path $bootstrapperOutput "GameSaveManager.UpdateBootstrapper.exe"
if (-not (Test-Path -LiteralPath $bootstrapperExecutable))
{
    throw "Update bootstrapper output is missing: $bootstrapperExecutable"
}
Copy-Item -LiteralPath $bootstrapperExecutable -Destination $OutputDirectory -Force
Write-Host "Update bootstrapper verified."
