[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [ValidateSet("win-x64", "win-arm64")]
    [string] $Runtime = "win-x64",
    [string] $OutputDirectory
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot "src\GameSaveManager.App\GameSaveManager.App.csproj"
if ([string]::IsNullOrWhiteSpace($OutputDirectory))
{
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\publish\$Runtime"
}

Write-Host "发布 GameSave Manager V2 到 $OutputDirectory"
dotnet publish $project `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $OutputDirectory

if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish 失败，退出码：$LASTEXITCODE"
}