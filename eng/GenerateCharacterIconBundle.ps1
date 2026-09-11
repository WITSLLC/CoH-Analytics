# Regenerates the embedded character-icons.cohicons bundle from PNG masters.
# Master images are optional private development inputs and are not part of the
# public repository. Pass -SourceDirectory explicitly when regenerating.
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDirectory,

    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\CoHAnalytics\Assets\Bundles\character-icons.cohicons')
)

$ErrorActionPreference = 'Stop'

$icons = @(
    @{ Id = 'default-male-01'; Source = 'male1.png' },
    @{ Id = 'default-male-02'; Source = 'male2.png' },
    @{ Id = 'default-male-03'; Source = 'male3.png' },
    @{ Id = 'default-male-04'; Source = 'male4.png' },
    @{ Id = 'default-male-05'; Source = 'male5.png' },
    @{ Id = 'default-male-06'; Source = 'male6.png' },
    @{ Id = 'default-male-07'; Source = 'male7.png' },
    @{ Id = 'default-male-08'; Source = 'male8.png' },
    @{ Id = 'default-male-09'; Source = 'male9.png' },
    @{ Id = 'default-male-10'; Source = 'male10.png' },
    @{ Id = 'default-female-01'; Source = 'female1.png' },
    @{ Id = 'default-female-02'; Source = 'female2.png' },
    @{ Id = 'default-female-03'; Source = 'female3.png' },
    @{ Id = 'default-female-04'; Source = 'female4.png' },
    @{ Id = 'default-female-05'; Source = 'female5.png' },
    @{ Id = 'default-female-06'; Source = 'female6.png' },
    @{ Id = 'default-female-07'; Source = 'female7.png' },
    @{ Id = 'default-female-08'; Source = 'female8.png' },
    @{ Id = 'default-female-09'; Source = 'female9.png' },
    @{ Id = 'default-female-10'; Source = 'female10.png' }
)

$expectedSourceNames = @($icons | ForEach-Object { $_.Source })
$allowedExtraSourceNames = @('gallery_silhouette.png')
$actualSourceNames = @(
    Get-ChildItem -LiteralPath $SourceDirectory -File -Filter '*.png' |
        Select-Object -ExpandProperty Name
)

foreach ($sourceName in $expectedSourceNames) {
    $sourcePath = Join-Path $SourceDirectory $sourceName
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        throw "Character icon source directory is missing required file '$sourceName'."
    }
}

$unexpectedSourceNames = @(
    $actualSourceNames |
        Where-Object {
            $_ -notin $expectedSourceNames -and $_ -notin $allowedExtraSourceNames
        } |
        Sort-Object
)
if ($unexpectedSourceNames.Count -gt 0) {
    throw ("Character icon source directory contains unexpected PNG files: {0}." -f ($unexpectedSourceNames -join ', '))
}

$outputDirectory = Split-Path -Parent $OutputPath
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

$temporaryPath = "$OutputPath.tmp"
if (Test-Path -LiteralPath $temporaryPath) {
    Remove-Item -LiteralPath $temporaryPath -Force
}

Add-Type -AssemblyName System.IO.Compression

$manifestIcons = @(
    $icons | ForEach-Object {
        [ordered]@{
            id = $_.Id
            entry = "icons/$($_.Id).png"
        }
    }
)
$manifest = [ordered]@{
    schemaVersion = 1
    icons = $manifestIcons
}
$manifestJson = $manifest | ConvertTo-Json -Depth 4 -Compress
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$fixedTimestamp = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)

$fileStream = [System.IO.FileStream]::new(
    $temporaryPath,
    [System.IO.FileMode]::CreateNew,
    [System.IO.FileAccess]::Write,
    [System.IO.FileShare]::None)
try {
    $archive = [System.IO.Compression.ZipArchive]::new(
        $fileStream,
        [System.IO.Compression.ZipArchiveMode]::Create,
        $true,
        $utf8NoBom)
    try {
        $manifestEntry = $archive.CreateEntry(
            'manifest.json',
            [System.IO.Compression.CompressionLevel]::Optimal)
        $manifestEntry.LastWriteTime = $fixedTimestamp
        $manifestStream = $manifestEntry.Open()
        try {
            $manifestBytes = $utf8NoBom.GetBytes($manifestJson)
            $manifestStream.Write($manifestBytes, 0, $manifestBytes.Length)
        }
        finally {
            $manifestStream.Dispose()
        }

        foreach ($icon in $icons) {
            $sourcePath = Join-Path $SourceDirectory $icon.Source
            $entry = $archive.CreateEntry(
                "icons/$($icon.Id).png",
                [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $fixedTimestamp
            $entryStream = $entry.Open()
            try {
                $sourceStream = [System.IO.File]::OpenRead($sourcePath)
                try {
                    $sourceStream.CopyTo($entryStream)
                }
                finally {
                    $sourceStream.Dispose()
                }
            }
            finally {
                $entryStream.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    $fileStream.Dispose()
}

if (Test-Path -LiteralPath $OutputPath) {
    Remove-Item -LiteralPath $OutputPath -Force
}

[System.IO.File]::Move($temporaryPath, $OutputPath)
$hash = Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256
Write-Output "Generated $OutputPath"
Write-Output "SHA256 $($hash.Hash)"
