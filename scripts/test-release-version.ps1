[CmdletBinding()]
param(
    [string] $Tag = $env:GITHUB_REF_NAME,
    [string] $ReleaseNotesOutputPath
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

$releaseDocument = Join-Path $repositoryRoot "docs\release.md"
if (-not (Test-Path -LiteralPath $releaseDocument))
{
    throw "Release document is missing: $releaseDocument"
}

$releaseLines = Get-Content -LiteralPath $releaseDocument -Encoding utf8
$escapedVersion = [Regex]::Escape($releaseInfo.Version)
$headingPattern = "^###\s+${escapedVersion}(?:[^0-9A-Za-z.-].*)?$"
$sectionStart = -1
for ($index = 0; $index -lt $releaseLines.Count; $index++)
{
    if ($releaseLines[$index] -match $headingPattern)
    {
        $sectionStart = $index
        break
    }
}
if ($sectionStart -lt 0)
{
    throw "Release history is missing version $($releaseInfo.Version): $releaseDocument"
}

$sectionEnd = $releaseLines.Count
for ($index = $sectionStart + 1; $index -lt $releaseLines.Count; $index++)
{
    if ($releaseLines[$index] -match '^###\s+')
    {
        $sectionEnd = $index
        break
    }
}
$sectionBody = ($releaseLines[($sectionStart + 1)..($sectionEnd - 1)] -join "`n").Trim()

if ([string]::IsNullOrWhiteSpace($ReleaseNotesOutputPath))
{
    $ReleaseNotesOutputPath = Join-Path $repositoryRoot "artifacts\release-notes-$($releaseInfo.Version).md"
}
elseif (-not [System.IO.Path]::IsPathRooted($ReleaseNotesOutputPath))
{
    $ReleaseNotesOutputPath = Join-Path $repositoryRoot $ReleaseNotesOutputPath
}

$releaseNotesDirectory = Split-Path -Parent $ReleaseNotesOutputPath
New-Item -ItemType Directory -Path $releaseNotesDirectory -Force | Out-Null
$releaseNotes = "# GameSave Manager $($releaseInfo.Version)`n`n$sectionBody`n"
[System.IO.File]::WriteAllText($ReleaseNotesOutputPath, $releaseNotes, [System.Text.UTF8Encoding]::new($false))

Write-Host "Version source, tag and release history match: $Tag"
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT))
{
    "version=$($releaseInfo.Version)" | Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8
    "tag=$($releaseInfo.Tag)" | Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8
    "release_notes=$ReleaseNotesOutputPath" | Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8
}
