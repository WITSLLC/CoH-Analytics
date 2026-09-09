# Builds the CoH Analytics Windows x64 MSI from a dedicated installer-stamped payload.
param(
    # Optional canonical override; defaults to CoHAnalyticsReleaseVersion in Directory.Build.props.
    [string]$ReleaseVersion,
    [string]$Configuration = "Release",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
. (Join-Path $PSScriptRoot "ReleaseVersion.ps1")
$resolvedVersion = Resolve-CoHAnalyticsReleaseVersion `
    -ReleaseVersion $ReleaseVersion `
    -RepositoryRoot $repoRoot

# Public MSI artifacts are immutable: every different public package must advance the
# canonical release version because WiX generates a new ProductCode for every build.
Set-Location $repoRoot

$payloadDir = Join-Path $repoRoot "artifacts/release/$($resolvedVersion.Canonical)/installer-payload"
$artifactDir = Join-Path $repoRoot "artifacts/release/$($resolvedVersion.Canonical)"
$installerProj = Join-Path $repoRoot "installer/CoHAnalytics.Installer/CoHAnalytics.Installer.wixproj"

if (-not $SkipPublish) {
    Write-Host "Publishing Windows Installer win-x64 payload to $payloadDir"
    New-Item -ItemType Directory -Force -Path $payloadDir | Out-Null
    dotnet publish (Join-Path $repoRoot "src/CoHAnalytics/CoHAnalytics.csproj") `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:CoHAnalyticsReleaseVersion=$($resolvedVersion.Canonical) `
        -p:CoHAnalyticsDeploymentType=WindowsInstaller `
        -o $payloadDir
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
}

if (-not (Test-Path (Join-Path $payloadDir "CoHAnalytics.exe"))) {
    throw "Missing Windows Installer payload at $payloadDir"
}

Write-Host "Building WiX installer (ReleaseVersion=$($resolvedVersion.Canonical), InstallerVersion=$($resolvedVersion.Numeric))"
dotnet build $installerProj `
    -c $Configuration `
    -p:CoHAnalyticsReleaseVersion=$($resolvedVersion.Canonical)
if ($LASTEXITCODE -ne 0) {
    throw "WiX build failed with exit code $LASTEXITCODE"
}

$msiLeaf = "CoH-Analytics-$($resolvedVersion.Canonical)-win-x64.msi"
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
