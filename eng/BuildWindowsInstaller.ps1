# Builds the CoH Analytics Windows x64 MSI from the validated self-contained publish payload.
param(
    [string]$Version = "0.1-beta.1",
    [string]$Configuration = "Release",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

$payloadDir = Join-Path $repoRoot "artifacts/release/$Version/self-contained"
$artifactDir = Join-Path $repoRoot "artifacts/release/$Version"
$installerProj = Join-Path $repoRoot "installer/CoHAnalytics.Installer/CoHAnalytics.Installer.wixproj"

if (-not $SkipPublish) {
    Write-Host "Publishing self-contained win-x64 payload to $payloadDir"
    New-Item -ItemType Directory -Force -Path $payloadDir | Out-Null
    dotnet publish (Join-Path $repoRoot "src/CoHAnalytics/CoHAnalytics.csproj") `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -o $payloadDir
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
}

if (-not (Test-Path (Join-Path $payloadDir "CoHAnalytics.exe"))) {
    throw "Missing self-contained payload at $payloadDir"
}

Write-Host "Building WiX installer"
dotnet build $installerProj -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "WiX build failed with exit code $LASTEXITCODE"
}

$builtMsi = Join-Path $repoRoot "installer/CoHAnalytics.Installer/bin/$Configuration/CoH-Analytics-$Version-win-x64.msi"
if (-not (Test-Path $builtMsi)) {
    # Fallback: locate MSI under installer bin
    $builtMsi = Get-ChildItem (Join-Path $repoRoot "installer/CoHAnalytics.Installer/bin") -Recurse -Filter "CoH-Analytics-$Version-win-x64.msi" |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not $builtMsi -or -not (Test-Path $builtMsi)) {
    throw "Built MSI not found"
}

New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
$destMsi = Join-Path $artifactDir "CoH-Analytics-$Version-win-x64.msi"
Copy-Item -Force $builtMsi $destMsi

$hash = (Get-FileHash $destMsi -Algorithm SHA256).Hash.ToLowerInvariant()
$shaPath = "$destMsi.sha256"
"$hash  $(Split-Path $destMsi -Leaf)`n" | Set-Content -Path $shaPath -Encoding ascii -NoNewline

Write-Host "Installer: $destMsi"
Write-Host "SHA256: $hash"
