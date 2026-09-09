function Get-CoHAnalyticsDefaultReleaseVersion {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    $propsPath = Join-Path $RepositoryRoot "Directory.Build.props"
    [xml]$props = Get-Content -LiteralPath $propsPath -Raw
    $value = [string]$props.Project.PropertyGroup.CoHAnalyticsReleaseVersion
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "CoHAnalyticsReleaseVersion is missing from '$propsPath'."
    }

    return $value.Trim()
}

function Resolve-CoHAnalyticsReleaseVersion {
    param(
        [string]$ReleaseVersion,
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    $canonical = if ([string]::IsNullOrWhiteSpace($ReleaseVersion)) {
        Get-CoHAnalyticsDefaultReleaseVersion -RepositoryRoot $RepositoryRoot
    }
    else {
        $ReleaseVersion
    }

    $match = [regex]::Match(
        $canonical,
        '^(?<Major>0|[1-9]\d*)\.(?<Minor>0|[1-9]\d*)\.(?<Patch>0|[1-9]\d*)(?<Beta>-beta)?$')
    if (-not $match.Success) {
        throw "ReleaseVersion must be MAJOR.MINOR.PATCH or MAJOR.MINOR.PATCH-beta. Received '$canonical'."
    }

    $major = [uint64]$match.Groups['Major'].Value
    $minor = [uint64]$match.Groups['Minor'].Value
    $patch = [uint64]$match.Groups['Patch'].Value

    if ($major -gt 255) {
        throw "ReleaseVersion major version must be between 0 and 255. Received '$canonical'."
    }

    if ($minor -gt 255) {
        throw "ReleaseVersion minor version must be between 0 and 255. Received '$canonical'."
    }

    if ($patch -gt 65535) {
        throw "ReleaseVersion patch/build version must be between 0 and 65535. Received '$canonical'."
    }

    $numeric = "$major.$minor.$patch"
    $isBeta = $match.Groups['Beta'].Success
    return [pscustomobject]@{
        Canonical = $canonical
        Numeric = $numeric
        Display = if ($isBeta) { "$numeric Beta" } else { $numeric }
        Assembly = "$numeric.0"
        IsBeta = $isBeta
    }
}
