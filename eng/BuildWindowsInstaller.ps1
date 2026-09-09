# Builds the CoH Analytics Windows x64 MSI from a dedicated installer-stamped payload.
param(
    # Package identity used in artifact paths and filenames (e.g. 0.1.3-beta).
    [string]$PackageVersion = "0.1.3-beta",
    # Numeric MSI ProductVersion (Beta is display-only and must not appear here).
    [string]$InstallerVersion = "0.1.3",
    # Human-facing status string for ARP/shortcut text.
    [string]$UserFacingVersion = "0.1.3 Beta",
    [string]$Configuration = "Release",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

function Get-CanonicalNumericVersion {
    param(
        [Parameter(Mandatory)]
        [string]$Value,
        [Parameter(Mandatory)]
        [string]$ParameterName
    )

    $match = [regex]::Match($Value, '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')
    if (-not $match.Success) {
        throw "$ParameterName must be an exact three-part numeric version (MAJOR.MINOR.PATCH). Received '$Value'."
    }

    $major = [uint64]$match.Groups[1].Value
    $minor = [uint64]$match.Groups[2].Value
    $patch = [uint64]$match.Groups[3].Value

    if ($major -gt 255) {
        throw "$ParameterName major version must be between 0 and 255. Received '$Value'."
    }

    if ($minor -gt 255) {
        throw "$ParameterName minor version must be between 0 and 255. Received '$Value'."
    }

    if ($patch -gt 65535) {
        throw "$ParameterName patch/build version must be between 0 and 65535. Received '$Value'."
    }

    return "$major.$minor.$patch"
}

$packageMatch = [regex]::Match(
    $PackageVersion,
    '^(?<Numeric>(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*))(?<Beta>-beta)?$')
if (-not $packageMatch.Success) {
    throw "PackageVersion must be MAJOR.MINOR.PATCH or MAJOR.MINOR.PATCH-beta. Received '$PackageVersion'."
}

$numericPackageVersion = Get-CanonicalNumericVersion `
    -Value $packageMatch.Groups['Numeric'].Value `
    -ParameterName "PackageVersion"
$canonicalInstallerVersion = Get-CanonicalNumericVersion `
    -Value $InstallerVersion `
    -ParameterName "InstallerVersion"

if ($canonicalInstallerVersion -ne $numericPackageVersion) {
    throw "InstallerVersion '$InstallerVersion' must match the numeric PackageVersion '$numericPackageVersion'."
}

$expectedDisplaySuffix = if ($packageMatch.Groups['Beta'].Success) { ' Beta' } else { '' }
$escapedNumericVersion = [regex]::Escape($numericPackageVersion)
$displayPattern = "^(?:Version )?$escapedNumericVersion$([regex]::Escape($expectedDisplaySuffix))$"
if (-not [regex]::IsMatch($UserFacingVersion, $displayPattern)) {
    $expectedDisplayVersion = "$numericPackageVersion$expectedDisplaySuffix"
    throw "UserFacingVersion '$UserFacingVersion' must represent PackageVersion '$PackageVersion' (for example, '$expectedDisplayVersion' or 'Version $expectedDisplayVersion')."
}

# Public MSI artifacts are immutable: every different public package must advance this
# three-part ProductVersion because WiX generates a new ProductCode for every build.
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

$payloadDir = Join-Path $repoRoot "artifacts/release/$PackageVersion/installer-payload"
$artifactDir = Join-Path $repoRoot "artifacts/release/$PackageVersion"
$installerProj = Join-Path $repoRoot "installer/CoHAnalytics.Installer/CoHAnalytics.Installer.wixproj"

if (-not $SkipPublish) {
    Write-Host "Publishing Windows Installer win-x64 payload to $payloadDir"
    New-Item -ItemType Directory -Force -Path $payloadDir | Out-Null
    dotnet publish (Join-Path $repoRoot "src/CoHAnalytics/CoHAnalytics.csproj") `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:Version=$PackageVersion `
        -p:InformationalVersion=$PackageVersion `
        -p:AssemblyVersion="$InstallerVersion.0" `
        -p:FileVersion="$InstallerVersion.0" `
        -p:CoHAnalyticsDeploymentType=WindowsInstaller `
        -o $payloadDir
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
}

if (-not (Test-Path (Join-Path $payloadDir "CoHAnalytics.exe"))) {
    throw "Missing Windows Installer payload at $payloadDir"
}

Write-Host "Building WiX installer (PackageVersion=$PackageVersion, InstallerVersion=$InstallerVersion)"
dotnet build $installerProj `
    -c $Configuration `
    -p:PackageVersion=$PackageVersion `
    -p:InstallerVersion=$InstallerVersion `
    -p:UserFacingVersion=$UserFacingVersion
if ($LASTEXITCODE -ne 0) {
    throw "WiX build failed with exit code $LASTEXITCODE"
}

$msiLeaf = "CoH-Analytics-$PackageVersion-win-x64.msi"
$builtMsi = Join-Path $repoRoot "installer/CoHAnalytics.Installer/bin/$Configuration/$msiLeaf"
if (-not (Test-Path $builtMsi)) {
    # Fallback: locate MSI under installer bin
    $builtMsi = Get-ChildItem (Join-Path $repoRoot "installer/CoHAnalytics.Installer/bin") -Recurse -Filter $msiLeaf |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not $builtMsi -or -not (Test-Path $builtMsi)) {
    throw "Built MSI not found"
}

New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
$destMsi = Join-Path $artifactDir $msiLeaf
Copy-Item -Force $builtMsi $destMsi

$hash = (Get-FileHash $destMsi -Algorithm SHA256).Hash.ToLowerInvariant()
$shaPath = "$destMsi.sha256"
"$hash  $(Split-Path $destMsi -Leaf)`n" | Set-Content -Path $shaPath -Encoding ascii -NoNewline

Write-Host "Installer: $destMsi"
Write-Host "SHA256: $hash"
