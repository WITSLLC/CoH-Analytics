# Publishes the two explicitly identified portable CoH Analytics Windows x64 packages.
param(
    [string]$PackageVersion = "0.1.2-beta",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $repoRoot "src/CoHAnalytics/CoHAnalytics.csproj"
$artifactDir = Join-Path $repoRoot "artifacts/release/$PackageVersion"
$numericVersion = $PackageVersion -replace '-beta$', ''

if ($numericVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "PackageVersion must be MAJOR.MINOR.PATCH or MAJOR.MINOR.PATCH-beta."
}

function Publish-PortablePackage {
    param(
        [string]$DirectoryName,
        [string]$DeploymentType,
        [bool]$SelfContained
    )

    $outputDir = Join-Path $artifactDir $DirectoryName
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
    dotnet publish $project `
        -c $Configuration `
        -r win-x64 `
        --self-contained $SelfContained `
        -p:Version=$PackageVersion `
        -p:InformationalVersion=$PackageVersion `
        -p:AssemblyVersion="$numericVersion.0" `
        -p:FileVersion="$numericVersion.0" `
        -p:CoHAnalyticsDeploymentType=$DeploymentType `
        -o $outputDir
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $DeploymentType with exit code $LASTEXITCODE"
    }

    $zipName = "CoH-Analytics-$PackageVersion-win-x64-$DirectoryName.zip"
    $zipPath = Join-Path $artifactDir $zipName
    Compress-Archive -Path (Join-Path $outputDir '*') -DestinationPath $zipPath -Force
    $hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $zipName`n" | Set-Content -Path "$zipPath.sha256" -Encoding ascii -NoNewline
    Write-Host "$DeploymentType package: $zipPath"
}

Publish-PortablePackage `
    -DirectoryName "self-contained" `
    -DeploymentType "PortableSelfContained" `
    -SelfContained $true

Publish-PortablePackage `
    -DirectoryName "framework-dependent" `
    -DeploymentType "PortableFrameworkDependent" `
    -SelfContained $false
