# Builds the CoH Analytics Windows x64 MSI from a dedicated installer-stamped payload.
param(
    # Package identity used in artifact paths and filenames (e.g. 0.1.2-beta).
    [string]$PackageVersion = "0.1.2-beta",
    # Numeric MSI ProductVersion (Beta is display-only and must not appear here).
    [string]$InstallerVersion = "0.1.2",
    # Human-facing status string for ARP/shortcut text.
    [string]$UserFacingVersion = "0.1.2 Beta",
    [string]$Configuration = "Release",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
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
