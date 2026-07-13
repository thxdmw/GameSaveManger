[CmdletBinding()]
param(
    [string] $Version = "0.1.0",
    [ValidateSet("win-x64", "win-arm64")]
    [string] $Runtime = "win-x64",
    [string] $InnoSetupCompiler
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot "publish-windows.ps1") -Runtime $Runtime
if ($LASTEXITCODE -ne 0)
{
    throw "客户端发布失败，无法生成安装包。"
}

if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler))
{
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    $InnoSetupCompiler = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler) -or -not (Test-Path $InnoSetupCompiler))
{
    throw "未找到 Inno Setup 6 的 ISCC.exe。请安装 Inno Setup，或通过 -InnoSetupCompiler 指定路径。"
}

$publishDirectory = Join-Path $repositoryRoot "artifacts\publish\$Runtime"
$script = Join-Path $repositoryRoot "installer\GameSaveManager.iss"
& $InnoSetupCompiler "/DMyAppVersion=$Version" "/DPublishDir=$publishDirectory" $script
if ($LASTEXITCODE -ne 0)
{
    throw "Inno Setup 编译失败，退出码：$LASTEXITCODE"
}