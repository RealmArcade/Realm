$ErrorActionPreference = "Stop"

$rootDir = Split-Path -Parent $PSScriptRoot
$versionJsonPath = Join-Path $rootDir "version.json"

if (-not (Test-Path $versionJsonPath)) {
    throw "version.json not found at $versionJsonPath"
}

$versionData = [System.IO.File]::ReadAllText($versionJsonPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
$version = $versionData.version
$extVersion = if ($versionData.extensionVersion) { $versionData.extensionVersion } else { $version.Split('-')[0] }

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$sourceExtensionDir = Join-Path $rootDir "Realm.MapEditorExtension"
$fallbackSourceExtensionDir = Join-Path $rootDir "Realm.Godot\vscode_extensions_dist\speige.realm-map-editor"

$candidateExtensionDirs = [System.Collections.Generic.List[string]]::new()

if ($env:APPDATA) {
    $candidateExtensionDirs.Add((Join-Path $env:APPDATA "Godot\app_userdata\Realm\vscode\user-data-dir\extensions"))
}

$specialAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
if ($specialAppData) {
    $candidateExtensionDirs.Add((Join-Path $specialAppData "Godot\app_userdata\Realm\vscode\user-data-dir\extensions"))
}

$specialLocalAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
if ($specialLocalAppData) {
    $candidateExtensionDirs.Add((Join-Path $specialLocalAppData "godot\app_userdata\Realm\vscode\user-data-dir\extensions"))
}

$specialUserProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
if ($specialUserProfile) {
    $candidateExtensionDirs.Add((Join-Path $specialUserProfile ".local\share\godot\app_userdata\Realm\vscode\user-data-dir\extensions"))
    $candidateExtensionDirs.Add((Join-Path $specialUserProfile "Library\Application Support\Godot\app_userdata\Realm\vscode\user-data-dir\extensions"))
}

$uniqueExtensionDirs = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($dir in $candidateExtensionDirs) {
    if (-not [string]::IsNullOrWhiteSpace($dir)) {
        [void]$uniqueExtensionDirs.Add([System.IO.Path]::GetFullPath($dir))
    }
}

foreach ($extensionsDir in $uniqueExtensionDirs) {
    $userDataDir = Split-Path -Parent $extensionsDir
    $vscodeDir = Split-Path -Parent $userDataDir

    if (-not (Test-Path $extensionsDir) -and -not (Test-Path $userDataDir) -and -not (Test-Path $vscodeDir)) {
        continue
    }

    if (-not (Test-Path $extensionsDir)) {
        New-Item -ItemType Directory -Force -Path $extensionsDir | Out-Null
    }

    $targetExtensionDirName = "speige.realm-map-editor-$extVersion"
    $targetExtensionPath = Join-Path $extensionsDir $targetExtensionDirName

    $existingEditorDirs = Get-ChildItem -Path $extensionsDir -Directory -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -like "speige.realm-map-editor*" -or $_.Name -like "realm-map-editor*"
    }

    foreach ($existingDir in $existingEditorDirs) {
        if ($existingDir.Name -ne $targetExtensionDirName) {
            Remove-Item -Path $existingDir.FullName -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "Removed obsolete extension directory: $($existingDir.FullName)"
        }
    }

    if (-not (Test-Path $targetExtensionPath)) {
        New-Item -ItemType Directory -Force -Path $targetExtensionPath | Out-Null
    }

    $packageJsonSrc = Join-Path $sourceExtensionDir "package.json"
    if (-not (Test-Path $packageJsonSrc)) {
        $packageJsonSrc = Join-Path $fallbackSourceExtensionDir "package.json"
    }
    if (Test-Path $packageJsonSrc) {
        Copy-Item -Path $packageJsonSrc -Destination (Join-Path $targetExtensionPath "package.json") -Force
    }

    foreach ($schemaFile in @("metadata.schema.json", "terrain.schema.json", "manifest.schema.json")) {
        $schemaSrc = Join-Path $sourceExtensionDir $schemaFile
        if (-not (Test-Path $schemaSrc)) {
            $schemaSrc = Join-Path $fallbackSourceExtensionDir $schemaFile
        }
        if (Test-Path $schemaSrc) {
            Copy-Item -Path $schemaSrc -Destination (Join-Path $targetExtensionPath $schemaFile) -Force
        }
    }

    $distSrc = Join-Path $sourceExtensionDir "dist"
    if (-not (Test-Path $distSrc)) {
        $distSrc = Join-Path $fallbackSourceExtensionDir "dist"
    }
    if (Test-Path $distSrc) {
        $targetDist = Join-Path $targetExtensionPath "dist"
        if (-not (Test-Path $targetDist)) {
            New-Item -ItemType Directory -Force -Path $targetDist | Out-Null
        }
        Copy-Item -Path (Join-Path $distSrc "*") -Destination $targetDist -Recurse -Force
    }

    $mediaSrc = Join-Path $sourceExtensionDir "media"
    if (-not (Test-Path $mediaSrc)) {
        $mediaSrc = Join-Path $fallbackSourceExtensionDir "media"
    }
    if (Test-Path $mediaSrc) {
        $targetMedia = Join-Path $targetExtensionPath "media"
        if (-not (Test-Path $targetMedia)) {
            New-Item -ItemType Directory -Force -Path $targetMedia | Out-Null
        }
        Copy-Item -Path (Join-Path $mediaSrc "*") -Destination $targetMedia -Recurse -Force
    }

    Write-Host "Updated extension in $targetExtensionPath"

    $obsoletePath = Join-Path $extensionsDir ".obsolete"
    if (Test-Path $obsoletePath) {
        try {
            $obsoleteFi = Get-Item $obsoletePath -ErrorAction SilentlyContinue
            if ($obsoleteFi -and $obsoleteFi.Length -lt 10MB) {
                $obsoleteText = [System.IO.File]::ReadAllText($obsoletePath, [System.Text.Encoding]::UTF8)
                $obsoleteObj = $obsoleteText | ConvertFrom-Json
                $cleanedObsolete = @{}
                $hasObsoleteKey = $false
                foreach ($prop in $obsoleteObj.psobject.properties) {
                    if ($prop.Name -like "speige.realm-map-editor*" -or $prop.Name -like "realm-map-editor*") {
                        $hasObsoleteKey = $true
                    } else {
                        $cleanedObsolete[$prop.Name] = $prop.Value
                    }
                }
                if ($hasObsoleteKey) {
                    $obsoleteJsonText = $cleanedObsolete | ConvertTo-Json -Compress
                    [System.IO.File]::WriteAllText($obsoletePath, $obsoleteJsonText, $utf8NoBom)
                    Write-Host "Cleaned obsolete extension references from $obsoletePath"
                }
            } else {
                Remove-Item -Path $obsoletePath -Force -ErrorAction SilentlyContinue
            }
        } catch {
            Remove-Item -Path $obsoletePath -Force -ErrorAction SilentlyContinue
        }
    }

    $extensionsJsonPath = Join-Path $extensionsDir "extensions.json"
    if (Test-Path $extensionsJsonPath) {
        try {
            $rawContent = [System.IO.File]::ReadAllText($extensionsJsonPath, [System.Text.Encoding]::UTF8).Trim()
            if (-not $rawContent.StartsWith("[")) {
                Remove-Item -Path $extensionsJsonPath -Force -ErrorAction SilentlyContinue
                Write-Host "Removed malformed non-array extensions.json from $extensionsDir"
            }
        } catch {
            Remove-Item -Path $extensionsJsonPath -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host "Visual Studio debug extension sync completed successfully."
