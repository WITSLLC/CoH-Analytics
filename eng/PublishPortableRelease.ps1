# Publishes the two explicitly identified portable CoH Analytics Windows x64 packages.
param(
    # Optional canonical override; defaults to CoHAnalyticsReleaseVersion in Directory.Build.props.
    [string]$ReleaseVersion,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
. (Join-Path $PSScriptRoot "ReleaseVersion.ps1")
$resolvedVersion = Resolve-CoHAnalyticsReleaseVersion `
    -ReleaseVersion $ReleaseVersion `
    -RepositoryRoot $repoRoot
$project = Join-Path $repoRoot "src/CoHAnalytics/CoHAnalytics.csproj"
$artifactDir = Join-Path $repoRoot "artifacts/release/$($resolvedVersion.Canonical)"

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
        -p:CoHAnalyticsReleaseVersion=$($resolvedVersion.Canonical) `
        -p:CoHAnalyticsDeploymentType=$DeploymentType `
        -o $outputDir
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $DeploymentType with exit code $LASTEXITCODE"
    }

    $zipName = "CoH-Analytics-$($resolvedVersion.Canonical)-win-x64-$DirectoryName.zip"
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
