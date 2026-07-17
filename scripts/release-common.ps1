function Get-GameSaveManagerReleaseInfo
{
    [CmdletBinding()]
    param(
        [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
    )

    $versionFile = Join-Path $RepositoryRoot "Directory.Build.props"
    if (-not (Test-Path -LiteralPath $versionFile))
    {
        throw "Version source was not found: $versionFile"
    }

    [xml] $versionDocument = Get-Content -LiteralPath $versionFile -Raw -Encoding utf8
    $properties = @($versionDocument.Project.PropertyGroup) |
        Where-Object { $_.GameSaveManagerVersionPrefix } |
        Select-Object -First 1
    if ($null -eq $properties)
    {
        throw "Directory.Build.props does not define GameSaveManagerVersionPrefix."
    }

    $versionPrefix = [string] $properties.GameSaveManagerVersionPrefix
    $versionSuffix = [string] $properties.GameSaveManagerVersionSuffix
    $releaseChannel = [string] $properties.GameSaveManagerReleaseChannel
    if ($versionPrefix -notmatch '^\d+\.\d+\.\d+$')
    {
        throw "GameSaveManagerVersionPrefix must use major.minor.patch: $versionPrefix"
    }
    if (-not [string]::IsNullOrWhiteSpace($versionSuffix) -and
        $versionSuffix -notmatch '^[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*$')
    {
        throw "GameSaveManagerVersionSuffix is not a valid SemVer suffix: $versionSuffix"
    }
    if ([string]::IsNullOrWhiteSpace($releaseChannel))
    {
        throw "GameSaveManagerReleaseChannel cannot be empty."
    }

    $version = if ([string]::IsNullOrWhiteSpace($versionSuffix))
    {
        $versionPrefix
    }
    else
    {
        "$versionPrefix-$versionSuffix"
    }

    [pscustomobject]@{
        Version = $version
        VersionPrefix = $versionPrefix
        VersionSuffix = $versionSuffix
        FileVersion = "$versionPrefix.0"
        ReleaseChannel = $releaseChannel
        Tag = "v$version"
        VersionFile = $versionFile
    }
}
