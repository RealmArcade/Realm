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

    $schemaSrc = Join-Path $sourceExtensionDir "map_schema.json"
    if (-not (Test-Path $schemaSrc)) {
        $schemaSrc = Join-Path $fallbackSourceExtensionDir "map_schema.json"
    }
    if (Test-Path $schemaSrc) {
        Copy-Item -Path $schemaSrc -Destination (Join-Path $targetExtensionPath "map_schema.json") -Force
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
    $extArray = @()
    if (Test-Path $extensionsJsonPath) {
        try {
            $extFi = Get-Item $extensionsJsonPath -ErrorAction SilentlyContinue
            if ($extFi -and $extFi.Length -lt 10MB) {
                $extJsonText = [System.IO.File]::ReadAllText($extensionsJsonPath, [System.Text.Encoding]::UTF8)
                $extList = $extJsonText | ConvertFrom-Json
                if ($extList) {
                    $extArray = @($extList)
                }
            } else {
                Remove-Item -Path $extensionsJsonPath -Force -ErrorAction SilentlyContinue
            }
        } catch {
            Write-Warning "Failed to parse extensions.json: $_. Resetting..."
            Remove-Item -Path $extensionsJsonPath -Force -ErrorAction SilentlyContinue
            $extArray = @()
        }
    }

    try {
        $targetId = "speige.realm-map-editor"
        $targetRelativeLocation = $targetExtensionDirName
        $normalizedTargetPath = "/" + ([System.IO.Path]::GetFullPath($targetExtensionPath).Replace("\", "/"))
        $found = $false

        foreach ($item in $extArray) {
            if ($item.identifier -and $item.identifier.id -eq $targetId) {
                $item.version = $extVersion
                $item.relativeLocation = $targetRelativeLocation
                if ($item.location) {
                    $item.location.path = $normalizedTargetPath
                }
                $found = $true
                break
            }
        }

        if (-not $found) {
            $newEntry = [PSCustomObject]@{
                identifier = [PSCustomObject]@{ id = $targetId }
                version = $extVersion
                location = [PSCustomObject]@{
                    '$mid' = 1
                    path = $normalizedTargetPath
                    scheme = "file"
                }
                relativeLocation = $targetRelativeLocation
                metadata = [PSCustomObject]@{
                    installedTimestamp = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
                    source = "local"
                    isApplicationScoped = $false
                    isMachineScoped = $false
                }
            }
            $extArray += $newEntry
        }

        $updatedJson = $extArray | ConvertTo-Json -Depth 10 -Compress
        [System.IO.File]::WriteAllText($extensionsJsonPath, $updatedJson, $utf8NoBom)
        Write-Host "Updated extensions.json in $extensionsDir"
    } catch {
        Write-Warning "Failed to update extensions.json: $_"
    }

    $ohziPatchSrcCandidates = @(
        (Join-Path $rootDir "Realm.MapEditorExtension\patches\ohzi-vscode-glb-viewer\extension.js"),
        (Join-Path $rootDir "Realm.Godot\vscode_extensions_dist\patches\ohzi-vscode-glb-viewer\extension.js")
    )
    $ohziPatchFile = $null
    foreach ($cand in $ohziPatchSrcCandidates) {
        if (Test-Path $cand) {
            $ohziPatchFile = $cand
            break
        }
    }
    if ($ohziPatchFile) {
        $ohziDirs = Get-ChildItem -Path $extensionsDir -Directory -ErrorAction SilentlyContinue | Where-Object {
            $_.Name -like "*ohzi-vscode-glb-viewer*"
        }
        foreach ($ohziDir in $ohziDirs) {
            $extJsPath = Join-Path $ohziDir.FullName "extension.js"
            $needsPatch = -not (Test-Path $extJsPath)
            if (-not $needsPatch -and (Test-Path $extJsPath)) {
                $extJsContent = [System.IO.File]::ReadAllText($extJsPath, [System.Text.Encoding]::UTF8)
                if (-not $extJsContent.Contains("loadModelFromBase64")) {
                    $needsPatch = $true
                }
            }
            if ($needsPatch) {
                Copy-Item -Path $ohziPatchFile -Destination $extJsPath -Force
                Write-Host "Patched OHZI GLB viewer extension in $($ohziDir.FullName)"
            }
        }
    }
}

Write-Host "Visual Studio debug extension sync completed successfully."
