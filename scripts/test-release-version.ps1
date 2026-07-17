[CmdletBinding()]
param(
    [string] $Tag = $env:GITHUB_REF_NAME
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "release-common.ps1")

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$releaseInfo = Get-GameSaveManagerReleaseInfo -RepositoryRoot $repositoryRoot
if ([string]::IsNullOrWhiteSpace($Tag))
{
    throw "Provide -Tag v$($releaseInfo.Version), or run from a tag workflow."
}
if ($Tag -cne $releaseInfo.Tag)
{
    throw "Tag '$Tag' does not match the version source '$($releaseInfo.Tag)'."
}

$releaseNotes = Join-Path $repositoryRoot "docs\release-notes-$($releaseInfo.Version).md"
if (-not (Test-Path -LiteralPath $releaseNotes))
{
    throw "Release notes are missing: $releaseNotes"
}

Write-Host "Version source, tag and release notes match: $Tag"
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT))
{
    "version=$($releaseInfo.Version)" | Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8
    "tag=$($releaseInfo.Tag)" | Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8
    "release_notes=$releaseNotes" | Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8
}
