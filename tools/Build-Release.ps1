[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$xivmarkRoot = Split-Path $PSScriptRoot -Parent
Push-Location $xivmarkRoot
try {
    dotnet build XivMark.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    dotnet run --project XivMark.Tests/XivMark.Tests.csproj -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed.' }

    $xivmarkManifest = Get-Content XivMark/bin/Release/XivMark.json -Raw | ConvertFrom-Json
    $xivmarkVersion = $xivmarkManifest.AssemblyVersion
    if ($xivmarkManifest.InternalName -ne 'XivMark' -or $xivmarkManifest.DalamudApiLevel -ne 15) {
        throw 'Unexpected plugin identity or API level.'
    }
    if ($xivmarkManifest.RepoUrl -ne 'https://github.com/kuchris/xivmark' -or
        $xivmarkManifest.IconUrl -ne "https://raw.githubusercontent.com/kuchris/xivmark/$xivmarkVersion/images/icon.png") {
        throw 'Manifest repository/icon URLs do not match this release.'
    }

    Add-Type -AssemblyName System.Drawing
    $xivmarkImage = [Drawing.Image]::FromFile((Join-Path $xivmarkRoot 'images/icon.png'))
    try {
        if ($xivmarkImage.Width -ne 512 -or $xivmarkImage.Height -ne 512) { throw 'Icon must be 512 x 512.' }
    } finally { $xivmarkImage.Dispose() }

    New-Item -ItemType Directory -Path artifacts -Force | Out-Null
    $xivmarkArchive = Join-Path $xivmarkRoot "artifacts/XivMark-$xivmarkVersion.zip"
    Copy-Item XivMark/bin/Release/XivMark/latest.zip $xivmarkArchive
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $xivmarkZip = [IO.Compression.ZipFile]::OpenRead($xivmarkArchive)
    try {
        foreach ($xivmarkFile in @('XivMark.dll', 'XivMark.json', 'icon.png')) {
            $xivmarkEntry = $xivmarkZip.GetEntry($xivmarkFile)
            if ($null -eq $xivmarkEntry) { throw "Missing ZIP entry: $xivmarkFile" }
            $xivmarkStream = $xivmarkEntry.Open()
            try { $xivmarkPackedHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($xivmarkStream)) }
            finally { $xivmarkStream.Dispose() }
            if ($xivmarkPackedHash -ne (Get-FileHash "XivMark/bin/Release/$xivmarkFile").Hash) {
                throw "ZIP does not contain the current build: $xivmarkFile"
            }
        }
    } finally { $xivmarkZip.Dispose() }

    $xivmarkHash = (Get-FileHash $xivmarkArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$xivmarkHash  $(Split-Path $xivmarkArchive -Leaf)" | Set-Content artifacts/SHA256SUMS.txt -Encoding utf8NoBOM
    Write-Output "Verified $xivmarkArchive"
} finally { Pop-Location }
