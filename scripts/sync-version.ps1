param(
    [string]$VersionNumber = "",
    [switch]$Publish
)

$ErrorActionPreference = "Stop"

$rootDir = Split-Path -Parent $PSScriptRoot
$versionJsonPath = Join-Path $rootDir "version.json"

if (-not (Test-Path $versionJsonPath)) {
    throw "version.json not found at $versionJsonPath"
}

$versionData = [System.IO.File]::ReadAllText($versionJsonPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json

function Get-SemVerTriple([string]$ver) {
    $clean = ($ver -replace '^v', '' -split '[-_]')[0]
    $parts = @($clean.Split('.'))
    while ($parts.Count -lt 3) { $parts += "0" }
    return "$($parts[0]).$($parts[1]).$($parts[2])"
}

if ($VersionNumber -and $VersionNumber.Trim() -ne "") {
    $rawInput = $VersionNumber.Trim()
    $extVersion = Get-SemVerTriple $rawInput
    $fileVersion = "$extVersion.0"
    
    $productVersion = $rawInput -replace '^v', ''
    if ($rawInput -match '^v?\d+(?:\.\d+)*[-_](.+)$') {
        $prerelease = $Matches[1]
        $infoVersion = "$($extVersion)_$($prerelease)"
        $version = "$extVersion-$($prerelease.Replace('_', '-'))"
    } else {
        $infoVersion = $productVersion
        $version = $extVersion
    }
    
    $versionData.version = $version
    $versionData.fileVersion = $fileVersion
    $versionData.productVersion = $productVersion
    $versionData.informationalVersion = $infoVersion
    $versionData.extensionVersion = $extVersion
    
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $updatedJsonText = $versionData | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($versionJsonPath, $updatedJsonText, $utf8NoBom)
    Write-Host "Updated version.json with VersionNumber: $VersionNumber"
} else {
    $rawVersion = if ($versionData.version) { $versionData.version } else { "0.0.1" }
    $extVersion = if ($versionData.extensionVersion) { $versionData.extensionVersion } else { Get-SemVerTriple $rawVersion }
    $fileVersion = if ($versionData.fileVersion) { $versionData.fileVersion } else { "$extVersion.0" }
    $productVersion = if ($versionData.productVersion) { $versionData.productVersion } else { $rawVersion }
    $infoVersion = if ($versionData.informationalVersion) { $versionData.informationalVersion } else { $productVersion.Replace('-', '_') }
    
    if ($rawVersion -match '^v?\d+(?:\.\d+)*[-_](.+)$') {
        $prerelease = $Matches[1]
        $version = "$extVersion-$($prerelease.Replace('_', '-'))"
    } else {
        $version = $extVersion
    }
}

Write-Host "Syncing version $version (Ext: $extVersion, File: $fileVersion, Product: $productVersion, Info: $infoVersion)..."

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$propsPath = Join-Path $rootDir "Directory.Build.props"
$propsContent = @"
<Project>
  <PropertyGroup>
    <Version>$version</Version>
    <FileVersion>$fileVersion</FileVersion>
    <InformationalVersion>$infoVersion</InformationalVersion>
  </PropertyGroup>
</Project>
"@
[System.IO.File]::WriteAllText($propsPath, $propsContent, $utf8NoBom)
Write-Host "Updated Directory.Build.props"

$extPkgPath = Join-Path $rootDir "Realm.MapEditorExtension\package.json"
if (Test-Path $extPkgPath) {
    $pkg = [System.IO.File]::ReadAllText($extPkgPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $pkg.version = $extVersion
    $pkgText = $pkg | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($extPkgPath, $pkgText, $utf8NoBom)
    Write-Host "Updated $extPkgPath"
}

$distPkgPath = Join-Path $rootDir "Realm.Client\vscode_extensions_dist\speige.realm-map-editor\package.json"
if (Test-Path $distPkgPath) {
    $distPkg = [System.IO.File]::ReadAllText($distPkgPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $distPkg.version = $extVersion
    $distPkgText = $distPkg | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($distPkgPath, $distPkgText, $utf8NoBom)
    Write-Host "Updated $distPkgPath"
}

$exportCfgPath = Join-Path $rootDir "Realm.Client\export_presets.cfg"
if (Test-Path $exportCfgPath) {
    $cfgContent = [System.IO.File]::ReadAllText($exportCfgPath, [System.Text.Encoding]::UTF8)
    $cfgContent = $cfgContent -replace 'application/file_version=".*?"', "application/file_version=`"$fileVersion`""
    $cfgContent = $cfgContent -replace 'application/product_version=".*?"', "application/product_version=`"$productVersion`""
    [System.IO.File]::WriteAllText($exportCfgPath, $cfgContent, $utf8NoBom)
    Write-Host "Updated $exportCfgPath"
}

if ($env:GITHUB_ENV) {
    Add-Content -Path $env:GITHUB_ENV -Value "VERSION_NUMBER=$infoVersion"
    Add-Content -Path $env:GITHUB_ENV -Value "RAW_VERSION=$version"
}
if ($env:GITHUB_OUTPUT) {
    Add-Content -Path $env:GITHUB_OUTPUT -Value "version_number=$infoVersion"
    Add-Content -Path $env:GITHUB_OUTPUT -Value "raw_version=$version"
}

# A plain-text stamp beside the artefacts, so the build somebody is holding can be named from a screenshot instead of from
# the properties of an executable. Nothing in Realm reads it: the game takes its version from the assembly it was compiled
# into and its Steam application from the client that launched it, so this file is only ever evidence for a person.
function Get-GameBuildNumber {
    $versionPath = Join-Path $rootDir "Realm.Shared\RealmVersion.cs"
    if (-not (Test-Path $versionPath)) { return "(unknown)" }

    $match = [regex]::Match([System.IO.File]::ReadAllText($versionPath, [System.Text.Encoding]::UTF8), 'GameBuildNumber\s*=\s*"(?<value>[^"]+)"')
    if ($match.Success) { return $match.Groups["value"].Value }
    return "(unknown)"
}

function Get-SteamApplicationIds {
    $idsPath = Join-Path $rootDir "Realm.Client\Services\Steam\SteamApplicationIds.cs"
    if (-not (Test-Path $idsPath)) { return "(unknown)" }

    $idsText = [System.IO.File]::ReadAllText($idsPath, [System.Text.Encoding]::UTF8)
    $full = [regex]::Match($idsText, 'FullGameAppId\s*=\s*(?<value>\d+)')
    $demo = [regex]::Match($idsText, 'DemoAppId\s*=\s*(?<value>\d+)')
    if (-not $full.Success -and -not $demo.Success) { return "(unknown)" }

    $fullId = if ($full.Success) { $full.Groups["value"].Value } else { "unknown" }
    $demoId = if ($demo.Success) { $demo.Groups["value"].Value } else { "unknown" }
    return "$fullId (full), $demoId (demo)"
}

$buildInput = if ($VersionNumber -and $VersionNumber.Trim() -ne "") { $VersionNumber.Trim() } else { "(none: the version.json in this checkout)" }
$runNumber = if ($env:GITHUB_RUN_NUMBER) { $env:GITHUB_RUN_NUMBER } else { "(local build)" }
$runUrl = if ($env:GITHUB_SERVER_URL -and $env:GITHUB_REPOSITORY -and $env:GITHUB_RUN_ID) {
    "$($env:GITHUB_SERVER_URL)/$($env:GITHUB_REPOSITORY)/actions/runs/$($env:GITHUB_RUN_ID)"
} else {
    "(local build)"
}
$commit = if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { "(unknown)" }
$ref = if ($env:GITHUB_REF) { $env:GITHUB_REF } else { "(unknown)" }
$actor = if ($env:GITHUB_ACTOR) { $env:GITHUB_ACTOR } else { "(local build)" }
$publishBuild = if ($Publish) { "yes" } else { "no" }
$builtAt = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", [System.Globalization.CultureInfo]::InvariantCulture)

$stampLines = @(
    "Realm build stamp",
    "=================",
    "Written when this build applied its version, so a bug report can name the build it came from.",
    "Realm never reads this file: the game takes its version and its Steam application from the assembly it was",
    "compiled into, and from the Steam client that launched it.",
    "",
    "Version            : $version",
    "Build input        : $buildInput",
    "File version       : $fileVersion",
    "Product version    : $productVersion",
    "Informational      : $infoVersion",
    "Game build number  : $(Get-GameBuildNumber)",
    "Steam app ids      : $(Get-SteamApplicationIds)",
    "Publish build      : $publishBuild",
    "Built (UTC)        : $builtAt",
    "Workflow run       : $runNumber",
    "Commit             : $commit",
    "Ref                : $ref",
    "Actor              : $actor",
    "Run                : $runUrl"
)

$stampPath = Join-Path $rootDir "version.txt"
[System.IO.File]::WriteAllText($stampPath, ($stampLines -join "`r`n") + "`r`n", $utf8NoBom)
Write-Host "Wrote $stampPath"

Write-Host "Version sync completed successfully."
