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
$project = Join-Path $repositoryRoot "src\GameSaveManager.App\GameSaveManager.App.csproj"
if ([string]::IsNullOrWhiteSpace($OutputDirectory))
{
    $folderName = if ($DeploymentMode -eq "SelfContained") { $Runtime } else { "$Runtime-framework-dependent" }
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\publish\$folderName"
}

$selfContained = if ($DeploymentMode -eq "SelfContained") { "true" } else { "false" }
Write-Host "Publishing GameSave Manager V2 with $DeploymentMode to $OutputDirectory"
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