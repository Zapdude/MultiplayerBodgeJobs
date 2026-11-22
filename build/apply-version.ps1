# Fold version.json into About/About.xml and print the derived release values.
# The tag is derived here and nowhere else: v{version}{releaseTagSuffix}.
# Bump version in version.json to mint a new tag; until then every build refreshes
# the same release.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$meta = Get-Content (Join-Path $root 'version.json') -Raw | ConvertFrom-Json

$version = [string]$meta.version
$tag = "v$version$([string]$meta.releaseTagSuffix)"
$prerelease = [bool]$meta.prerelease

$hash = $null
if ($args -contains '--hash') {
    $hash = (& git -C $root rev-parse --short HEAD 2>$null)
    if ($LASTEXITCODE -ne 0) { $hash = $null }
}

$modVersion = if ($hash) { "$version ($hash)" } else { $version }
$aboutPath = Join-Path $root 'About/About.xml'
$text = [System.IO.File]::ReadAllText($aboutPath)
$new = ([regex]'<modVersion>.*?</modVersion>').Replace($text, "<modVersion>$modVersion</modVersion>", 1)
if ($new -eq $text) { throw "apply-version: no <modVersion> in $aboutPath" }
[System.IO.File]::WriteAllText($aboutPath, $new, [System.Text.UTF8Encoding]::new($false))

Write-Output "version=$version"
Write-Output "releaseTag=$tag"
Write-Output "prerelease=$($prerelease.ToString().ToLower())"
