# NOTE: Can't use official VSCode due to license. VSCodium is MIT version of VSCode. They're nearly identical git repos but microsoft version has minor customizations.
param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$godotDir = $PSScriptRoot
$rootDir = Split-Path -Parent $godotDir
$appDataDir = Join-Path $env:APPDATA "Godot\app_userdata\Realm"
$embedDir = Join-Path $appDataDir "vscode"
$userDataDir = Join-Path $embedDir "user-data-dir"
$extsDir = Join-Path $userDataDir "extensions"
$editorDir = Join-Path $embedDir "editor"
$binDir = Join-Path $editorDir "bin"
$versionFile = Join-Path $embedDir "installed_vscode_version.json"
$completedMarkerPath = Join-Path $embedDir "install_completed.marker"

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

Write-Host "Target editor installation directory: $appDataDir"
New-Item -ItemType Directory -Force -Path $binDir | Out-Null
New-Item -ItemType Directory -Force -Path $userDataDir | Out-Null
New-Item -ItemType Directory -Force -Path $extsDir | Out-Null
New-Item -ItemType Directory -Force -Path $editorDir | Out-Null

function Get-VSCodeRoots {
    param(
        [string]$EditorDir,
        [string]$EmbedDir
    )

    $roots = [System.Collections.Generic.List[string]]::new()
    if ($EditorDir -and (Test-Path $EditorDir)) {
        $appDir = Join-Path $EditorDir "resources\app"
        if (Test-Path $appDir) { $roots.Add($appDir) } else { $roots.Add($EditorDir) }
    }
    if ($EmbedDir -and (Test-Path $EmbedDir)) {
        $serveWeb = Join-Path $EmbedDir "cli-data-dir\serve-web"
        if (Test-Path $serveWeb) {
            $commitDirs = Get-ChildItem -Path $serveWeb -Directory -ErrorAction SilentlyContinue
            foreach ($d in $commitDirs) {
                $roots.Add($d.FullName)
            }
        }
    }
    if ($env:VSCODE_CLI_DATA_DIR -and (Test-Path $env:VSCODE_CLI_DATA_DIR)) {
        $serveWeb = Join-Path $env:VSCODE_CLI_DATA_DIR "serve-web"
        if (Test-Path $serveWeb) {
            $commitDirs = Get-ChildItem -Path $serveWeb -Directory -ErrorAction SilentlyContinue
            foreach ($d in $commitDirs) {
                $roots.Add($d.FullName)
            }
        }
    }
    return $roots | Select-Object -Unique
}

function Patch-ProductJson {
    param([string]$ProductJsonPath)

    if (-not (Test-Path $ProductJsonPath)) { return }

    try {
        $pjContent = [System.IO.File]::ReadAllText($ProductJsonPath, [System.Text.Encoding]::UTF8)
        $pj = $pjContent | ConvertFrom-Json
        $changed = $false

        if ($pj.win32AppUserModelId -ne "Realm.GameClient") {
            $pj.win32AppUserModelId = "Realm.GameClient"
            $changed = $true
        }

        if (-not $pj.configurationDefaults) {
            $pj | Add-Member -MemberType NoteProperty -Name "configurationDefaults" -Value ([PSCustomObject]@{})
            $changed = $true
        }
        if ($pj.configurationDefaults."security.workspace.trust.enabled" -ne $false) {
            $pj.configurationDefaults | Add-Member -MemberType NoteProperty -Name "security.workspace.trust.enabled" -Value $false -Force
            $changed = $true
        }
        if ($pj.configurationDefaults."security.workspace.trust.startupPrompt" -ne "never") {
            $pj.configurationDefaults | Add-Member -MemberType NoteProperty -Name "security.workspace.trust.startupPrompt" -Value "never" -Force
            $changed = $true
        }
        if ($pj.configurationDefaults."security.workspace.trust.banner" -ne "never") {
            $pj.configurationDefaults | Add-Member -MemberType NoteProperty -Name "security.workspace.trust.banner" -Value "never" -Force
            $changed = $true
        }
        if ($pj.configurationDefaults."security.workspace.trust.emptyWindow" -ne $false) {
            $pj.configurationDefaults | Add-Member -MemberType NoteProperty -Name "security.workspace.trust.emptyWindow" -Value $false -Force
            $changed = $true
        }

        if (-not $pj.extensionKind) {
            $pj | Add-Member -MemberType NoteProperty -Name "extensionKind" -Value ([PSCustomObject]@{})
            $changed = $true
        }

        if ($pj.extensionKind.PSObject.Properties["google.google-antigravity"]) {
            $pj.extensionKind.PSObject.Properties.Remove("google.google-antigravity")
            $changed = $true
        }

        $workspaceExts = @("muhammad-sammy.csharp", "patcx.vscode-nuget-gallery", "speige.realm-map-editor", "vadimcn.vscode-lldb", "haoyangzeng.opencui")
        foreach ($ext in $workspaceExts) {
            if (-not $pj.extensionKind.$ext) {
                $pj.extensionKind | Add-Member -MemberType NoteProperty -Name $ext -Value @("workspace") -Force
                $changed = $true
            }
        }

        if ($changed) {
            $updatedPj = $pj | ConvertTo-Json -Depth 20
            [System.IO.File]::WriteAllText($ProductJsonPath, $updatedPj, [System.Text.UTF8Encoding]::new($false))
            Write-Host "Updated product.json at $ProductJsonPath"
        }
    } catch {
        Write-Warning "Failed to patch product.json at $ProductJsonPath : $_"
    }
}

$oldExtDest = Join-Path $extsDir "realm-map-editor"
if (Test-Path $oldExtDest) {
    try {
        Remove-Item -Path $oldExtDest -Recurse -Force
    } catch {
        Write-Warning "Could not remove the previous Realm Map Editor extension at $oldExtDest : $($_.Exception.Message)"
    }
}

$extensionsJsonCheck = Join-Path $extsDir "extensions.json"
if (Test-Path $extensionsJsonCheck) {
    try {
        $fi = Get-Item $extensionsJsonCheck -ErrorAction SilentlyContinue
        if ($fi -and $fi.Length -gt 10MB) {
            Write-Host "Removing excessively large extensions.json ($($fi.Length) bytes)..."
            Remove-Item -Path $extensionsJsonCheck -Force -ErrorAction SilentlyContinue
        } else {
            $rawJson = [System.IO.File]::ReadAllText($extensionsJsonCheck, [System.Text.Encoding]::UTF8).Trim()
            if (-not $rawJson.StartsWith("[")) {
                Write-Host "Removing malformed non-array extensions.json..."
                Remove-Item -Path $extensionsJsonCheck -Force -ErrorAction SilentlyContinue
            }
        }
    } catch {
        Remove-Item -Path $extensionsJsonCheck -Force -ErrorAction SilentlyContinue
    }
}

$wasiVersion = "34"
$wasiSdkBaseDir = Join-Path $appDataDir "wasi_sdk"
$wasiTargetDir = Join-Path $wasiSdkBaseDir "wasi-sdk-$wasiVersion"
$wasiClang = Join-Path $wasiTargetDir "bin\clang.exe"

$shouldInstallWasi = $Force -or (-not (Test-Path $wasiClang)) -or ((Get-Item $wasiClang).Length -eq 0)

if ($shouldInstallWasi) {
    $wasiTar = Join-Path $wasiSdkBaseDir "wasi-sdk-$wasiVersion.tar.gz"
    try {
        Write-Host "Downloading WASI SDK $wasiVersion..."
        New-Item -ItemType Directory -Force -Path $wasiTargetDir | Out-Null
        $wasiUrl = "https://github.com/WebAssembly/wasi-sdk/releases/download/wasi-sdk-$wasiVersion/wasi-sdk-$wasiVersion.0-x86_64-windows.tar.gz"
        curl.exe -L $wasiUrl -o $wasiTar

        if (-not (Test-Path $wasiTar) -or (Get-Item $wasiTar).Length -eq 0) {
            throw "The WASI SDK archive could not be downloaded from $wasiUrl"
        }

        Write-Host "Extracting WASI SDK $wasiVersion..."
        tar.exe -xf $wasiTar -C $wasiTargetDir --strip-components=1
        Write-Host "WASI SDK $wasiVersion installed to $wasiTargetDir successfully."
    } catch {
        Write-Warning "WASI SDK $wasiVersion could not be installed: $($_.Exception.Message). The editor still opens; map compilation stays unavailable until it is installed."
    } finally {
        if (Test-Path $wasiTar) {
            Remove-Item -Path $wasiTar -Force -ErrorAction SilentlyContinue
        }
    }
} else {
    Write-Host "WASI SDK verified at $wasiTargetDir"
}

$codiumCliPath = Join-Path $binDir "codium.cmd"
$codiumEditorExe = Join-Path $editorDir "VSCodium.exe"

$criticalFileMissing = (-not (Test-Path $codiumCliPath)) -or (-not (Test-Path $codiumEditorExe))

$remoteStableName = ""
$remoteStableSha = ""
$remoteCliUrl = ""
$remoteDesktopUrl = ""
$shaQueryFailed = $false

Write-Host "Checking for VSCodium open-source releases..."
try {
    $releaseResponse = Invoke-RestMethod -Uri "https://api.github.com/repos/VSCodium/vscodium/releases/latest" -UserAgent "Mozilla/5.0" -TimeoutSec 10 -ErrorAction Stop
    $remoteStableName = $releaseResponse.tag_name
    $remoteStableSha = $releaseResponse.target_commitish

    $desktopAsset = $releaseResponse.assets | Where-Object { $_.name -like 'VSCodium-win32-x64-*.zip' } | Select-Object -First 1
    $cliAsset = $releaseResponse.assets | Where-Object { $_.name -like 'vscodium-cli-win32-x64-*.tar.gz' } | Select-Object -First 1

    if ($desktopAsset) {
        $remoteDesktopUrl = $desktopAsset.browser_download_url
    }
    if ($cliAsset) {
        $remoteCliUrl = $cliAsset.browser_download_url
    }
} catch {
    Write-Host "Failed to check VSCodium GitHub release API ($($_.Exception.Message))."
    $shaQueryFailed = $true
}

$shouldInstallVSCode = $false

if ($Force) {
    Write-Host "Force re-install requested. Re-installing VSCodium..."
    $shouldInstallVSCode = $true
} elseif ($criticalFileMissing) {
    Write-Host "Auto-repair detected: Critical VSCodium files missing or corrupt. Installing VSCodium..."
    $shouldInstallVSCode = $true
} elseif ($shaQueryFailed) {
    Write-Host "Skipping auto-detection because VSCodium API check was unreachable and critical files exist."
    $shouldInstallVSCode = $false
} else {
    $installedVersionData = $null
    if (Test-Path $versionFile) {
        try {
            $installedVersionData = [System.IO.File]::ReadAllText($versionFile, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        } catch {}
    }

    if ($installedVersionData -and $installedVersionData.name -and $installedVersionData.version) {
        $installedName = $installedVersionData.name
        $installedSha = $installedVersionData.version

        if ($installedName -ne $remoteStableName -or $installedSha -ne $remoteStableSha) {
            Write-Host "Newer VSCodium version detected (installed: $installedName, remote: $remoteStableName). Updating..."
            $shouldInstallVSCode = $true
        } else {
            Write-Host "VSCodium ($installedName) is up to date."
        }
    } else {
        $targetExe = $codiumEditorExe
        $exeVersion = if (Test-Path $targetExe) { (Get-Item $targetExe).VersionInfo.ProductVersion } else { $null }
        if ($exeVersion) {
            Write-Host "VSCodium verified at $editorDir"
        }
    }
}

if ($shouldInstallVSCode) {
    Get-Process | Where-Object { 
        try { $_.Path -and $_.Path.StartsWith($embedDir) } catch { $false } 
    } | Stop-Process -Force -ErrorAction SilentlyContinue

    $fallbackVersion = if ($remoteStableName) { $remoteStableName } else { "1.135.06055" }

    $cliArchive = $null
    try {
        Write-Host "Downloading VSCodium CLI..."
        $cliArchive = Join-Path $embedDir "vscodium-cli.tar.gz"
        $cliDownloadUrl = if ($remoteCliUrl) { $remoteCliUrl } else { "https://github.com/VSCodium/vscodium/releases/download/$fallbackVersion/vscodium-cli-win32-x64-$fallbackVersion.tar.gz" }
        curl.exe -L $cliDownloadUrl -o $cliArchive

        if (-not (Test-Path $cliArchive) -or (Get-Item $cliArchive).Length -eq 0) {
            throw "The VSCodium CLI archive could not be downloaded from $cliDownloadUrl"
        }

        Write-Host "Extracting VSCodium CLI..."
        tar.exe -xf $cliArchive -C $binDir
        Write-Host "VSCodium CLI installed successfully."
    } catch {
        Write-Warning "VSCodium CLI could not be installed: $($_.Exception.Message). Extensions stay uninstalled until it is available."
    } finally {
        if ($cliArchive -and (Test-Path $cliArchive)) {
            Remove-Item -Path $cliArchive -Force -ErrorAction SilentlyContinue
        }
    }

    $desktopZip = $null
    try {
        Write-Host "Downloading VSCodium Desktop..."
        $desktopZip = Join-Path $embedDir "vscodium-desktop.zip"
        $desktopDownloadUrl = if ($remoteDesktopUrl) { $remoteDesktopUrl } else { "https://github.com/VSCodium/vscodium/releases/download/$fallbackVersion/VSCodium-win32-x64-$fallbackVersion.zip" }
        curl.exe -L $desktopDownloadUrl -o $desktopZip

        if (-not (Test-Path $desktopZip) -or (Get-Item $desktopZip).Length -eq 0) {
            throw "The VSCodium desktop archive could not be downloaded from $desktopDownloadUrl"
        }

        Write-Host "Extracting VSCodium Desktop..."
        Expand-Archive -Path $desktopZip -DestinationPath $editorDir -Force
        Write-Host "VSCodium Desktop installed successfully."
    } catch {
        Write-Warning "VSCodium Desktop could not be installed: $($_.Exception.Message)"
    } finally {
        if ($desktopZip -and (Test-Path $desktopZip)) {
            Remove-Item -Path $desktopZip -Force -ErrorAction SilentlyContinue
        }
    }

    if (-not (Test-Path $codiumEditorExe)) {
        Write-Warning "VSCodium is not present at $codiumEditorExe. The embedded editor cannot be opened until it is installed."
    }


    if ($remoteStableName -and $remoteStableSha) {
        $meta = @{
            name = $remoteStableName
            version = $remoteStableSha
            installed_utc = (Get-Date).ToUniversalTime().ToString("o")
        }
        $metaText = $meta | ConvertTo-Json
        [System.IO.File]::WriteAllText($versionFile, $metaText, $utf8NoBom)
    } else {
        $targetExe = $codiumEditorExe
        $prodVer = if (Test-Path $targetExe) { (Get-Item $targetExe).VersionInfo.ProductVersion } else { "stable" }
        $meta = @{
            name = if ($prodVer) { $prodVer } else { "stable" }
            version = "unknown"
            installed_utc = (Get-Date).ToUniversalTime().ToString("o")
        }
        $metaText = $meta | ConvertTo-Json
        [System.IO.File]::WriteAllText($versionFile, $metaText, $utf8NoBom)
    }
}

$activeCliPath = $codiumCliPath

$extSrc = Join-Path $godotDir "vscode_extensions_dist\speige.realm-map-editor"
if (-not (Test-Path $extSrc)) {
    $altSrc = Join-Path $godotDir "..\Realm.MapEditorExtension"
    if (Test-Path $altSrc) {
        $extSrc = $altSrc
    }
}
if (-not (Test-Path $extSrc)) {
    Write-Warning "The Realm Map Editor extension files were not found. The editor still opens, without map editing support."
}

$extVersion = "0.0.1"
$extPkgJson = Join-Path $extSrc "package.json"
if (Test-Path $extPkgJson) {
    try {
        $pkgObj = [System.IO.File]::ReadAllText($extPkgJson, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        if ($pkgObj.version) {
            $extVersion = $pkgObj.version
        }
    } catch {}
}

$extDest = Join-Path $extsDir "speige.realm-map-editor-$extVersion"
New-Item -ItemType Directory -Force -Path $extDest | Out-Null

$shouldInstallExt = $Force -or (-not (Test-Path (Join-Path $extDest "package.json")))
if ($shouldInstallExt -and $extSrc -and (Test-Path $extSrc)) {
    try {
        foreach ($item in @("package.json", "metadata.schema.json", "terrain.schema.json", "manifest.schema.json", "dist", "media")) {
            $srcItem = Join-Path $extSrc $item
            if (Test-Path $srcItem) {
                $destItem = Join-Path $extDest $item
                if (Test-Path $srcItem -PathType Container) {
                    New-Item -ItemType Directory -Force -Path $destItem | Out-Null
                    Copy-Item -Path (Join-Path $srcItem "*") -Destination $destItem -Recurse -Force
                } else {
                    Copy-Item -Path $srcItem -Destination $destItem -Force
                }
            }
        }
    } catch {
        Write-Warning "The Realm Map Editor extension could not be copied to $extDest : $($_.Exception.Message)"
    }
} else {
    Write-Host "Realm Map Editor extension ($extVersion) verified at $extDest"
}

$requiredExtensions = @(
    "muhammad-sammy.csharp",
    "OHZIInteractiveStudio.ohzi-vscode-glb-viewer",
    "Gruntfuggly.todo-tree",
	# "mechatroner.rainbow-json",
    "patcx.vscode-nuget-gallery",
    "AykutSarac.jsoncrack-vscode",
    "vadimcn.vscode-lldb",
    "haoyangzeng.opencui"
	# "akondratiuk1-dev.texture-viewer"
)

foreach ($extId in $requiredExtensions) {
    $extMatch = Get-ChildItem -Path $extsDir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "$extId*" -or $_.Name -like "*$extId*" }
    if ((-not $extMatch) -or $Force) {
        if (-not (Test-Path $activeCliPath)) {
            Write-Warning "Skipping extension $extId because the VSCodium CLI is missing at $activeCliPath. The editor still opens without it."
            continue
        }

        Write-Host "Installing extension $extId from Open VSX..."
        try {
            & $activeCliPath --extensions-dir $extsDir --user-data-dir $userDataDir --install-extension $extId --force
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "Extension $extId could not be installed (exit code $LASTEXITCODE). The editor still opens without it."
            }
        } catch {
            Write-Warning "Extension $extId could not be installed: $($_.Exception.Message). The editor still opens without it."
        }
    } else {
        Write-Host "Extension $extId already installed."
    }
}

$antigravityDirs = Get-ChildItem -Path $extsDir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "google.google-antigravity*" }
foreach ($antigravityDir in $antigravityDirs) {
    Remove-Item -LiteralPath $antigravityDir.FullName -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Removed the deprecated antigravity extension from $($antigravityDir.FullName)"
}

$extensionsJsonPath = Join-Path $extsDir "extensions.json"
if (Test-Path -LiteralPath $extensionsJsonPath) {
    try {
        $cachedExtensions = [System.IO.File]::ReadAllText($extensionsJsonPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        $cachedExtensionsArray = @($cachedExtensions)
        $filteredExtensions = @($cachedExtensionsArray | Where-Object { $_.identifier.id -ne "google.google-antigravity" })
        if ($filteredExtensions.Count -ne $cachedExtensionsArray.Count) {
            $serializedExtensions = ConvertTo-Json -InputObject $filteredExtensions -Depth 20 -Compress
            [System.IO.File]::WriteAllText($extensionsJsonPath, $serializedExtensions, [System.Text.UTF8Encoding]::new($false))
            Write-Host "Removed the deprecated antigravity extension from extensions.json"
        }
    } catch {
        Write-Warning "Failed to remove the deprecated antigravity extension from extensions.json: $($_.Exception.Message)"
    }
}

$ohziPatchFile = Join-Path $PSScriptRoot "Templates\ohzi_extension_patch.js"
if (-not (Test-Path $ohziPatchFile)) {
    $ohziPatchFile = Join-Path $rootDir "Realm.Client\Templates\ohzi_extension_patch.js"
}
if (Test-Path $ohziPatchFile) {
    $ohziDirs = Get-ChildItem -Path $extsDir -Directory -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -like "*ohzi-vscode-glb-viewer*" -or $_.Name -like "*ohzi*"
    }
    foreach ($ohziDir in $ohziDirs) {
        $extJsPath = Join-Path $ohziDir.FullName "extension.js"
        $needsPatch = -not (Test-Path $extJsPath)
        if (-not $needsPatch -and (Test-Path $extJsPath)) {
            $extJsContent = [System.IO.File]::ReadAllText($extJsPath, [System.Text.Encoding]::UTF8)
            if (-not $extJsContent.Contains("REALM_PATCHED_OHZI_BASE64")) {
                $needsPatch = $true
            }
        }
        if ($needsPatch) {
            try {
                Copy-Item -Path $ohziPatchFile -Destination $extJsPath -Force
                Write-Host "Patched OHZI GLB viewer extension in $($ohziDir.FullName)"
            } catch {
                Write-Warning "Could not patch the OHZI GLB viewer extension in $($ohziDir.FullName): $($_.Exception.Message)"
            }
        }
    }
}

$opencodeCliDir = Join-Path $appDataDir "opencode_cli"
$opencodeCliExe = Join-Path $opencodeCliDir "bin\opencode.exe"

function Test-OpencodeExecutable {
    param([string]$CandidatePath)

    if (-not $CandidatePath) { return $false }
    if (-not (Test-Path -LiteralPath $CandidatePath -PathType Leaf)) { return $false }
    return ((Get-Item -LiteralPath $CandidatePath).Length -gt 0)
}

function Find-OpencodeExecutable {
    $candidates = @(
        $opencodeCliExe,
        (Join-Path $env:USERPROFILE ".opencode\bin\opencode.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\@opencodedesktop\resources\opencode-cli.exe"),
        (Join-Path $env:APPDATA "npm\opencode.cmd")
    )

    foreach ($candidate in $candidates) {
        if (Test-OpencodeExecutable $candidate) { return $candidate }
    }

    $onPath = Get-Command "opencode" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($onPath) { return $onPath.Source }

    return $null
}

function Install-OpencodeCli {
    try {
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/sst/opencode/releases/latest" -UserAgent "Mozilla/5.0" -TimeoutSec 15 -ErrorAction Stop
    } catch {
        Write-Warning "Failed to query opencode releases: $($_.Exception.Message)"
        return $false
    }

    $asset = $release.assets | Where-Object { $_.name -eq "opencode-windows-x64.zip" } | Select-Object -First 1
    if (-not $asset) {
        Write-Warning "opencode-windows-x64.zip not found in release $($release.tag_name)."
        return $false
    }

    $archivePath = Join-Path $opencodeCliDir "opencode-windows-x64.zip"
    $extractDir = Join-Path $opencodeCliDir "extract"
    New-Item -ItemType Directory -Force -Path ([System.IO.Path]::GetDirectoryName($opencodeCliExe)) | Out-Null

    try {
        curl.exe -sS -L $asset.browser_download_url -o $archivePath
        if (-not (Test-Path -LiteralPath $archivePath) -or (Get-Item -LiteralPath $archivePath).Length -eq 0) {
            Write-Warning "opencode CLI download failed (release $($release.tag_name))."
            return $false
        }

        if (Test-Path -LiteralPath $extractDir) {
            Remove-Item -LiteralPath $extractDir -Recurse -Force
        }
        Expand-Archive -LiteralPath $archivePath -DestinationPath $extractDir -Force

        $extractedExe = Get-ChildItem -LiteralPath $extractDir -Filter "opencode.exe" -Recurse -File | Select-Object -First 1
        if (-not $extractedExe) {
            Write-Warning "opencode.exe not found inside the release archive."
            return $false
        }

        Copy-Item -LiteralPath $extractedExe.FullName -Destination $opencodeCliExe -Force
        Write-Host "opencode CLI ($($release.tag_name)) installed to $opencodeCliExe"
        return $true
    } catch {
        Write-Warning "Failed to install opencode CLI: $($_.Exception.Message)"
        return $false
    } finally {
        if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue }
        if (Test-Path -LiteralPath $extractDir) { Remove-Item -LiteralPath $extractDir -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

$resolvedOpencodeCli = $null
if (Test-OpencodeExecutable $opencodeCliExe) {
    $resolvedOpencodeCli = $opencodeCliExe
} else {
    Write-Host "Managed opencode CLI not found. Downloading opencode CLI..."
    if (Install-OpencodeCli) {
        $resolvedOpencodeCli = $opencodeCliExe
    } else {
        $resolvedOpencodeCli = Find-OpencodeExecutable
        if ($resolvedOpencodeCli) {
            Write-Warning "Falling back to the locally installed opencode CLI at $resolvedOpencodeCli. Compatibility depends on its version."
        }
    }
}

if ($resolvedOpencodeCli) {
    Write-Host "opencode CLI resolved at $resolvedOpencodeCli"
} else {
    Write-Warning "opencode CLI is unavailable. The OpenCode Panel extension cannot start its backend until opencode is installed or 'opencui.binaryPath' points to a valid binary."
}

$opencuiDirs = Get-ChildItem -Path $extsDir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "haoyangzeng.opencui*" }
foreach ($opencuiDir in $opencuiDirs) {
    $opencuiPackagePath = Join-Path $opencuiDir.FullName "package.json"
    if (-not (Test-Path -LiteralPath $opencuiPackagePath)) { continue }
    try {
        $opencuiPackage = [System.IO.File]::ReadAllText($opencuiPackagePath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        $activationEvents = @($opencuiPackage.activationEvents | Where-Object { $_ })
        if ($activationEvents -notcontains "onStartupFinished") {
            $activationEvents += "onStartupFinished"
            $opencuiPackage | Add-Member -MemberType NoteProperty -Name "activationEvents" -Value $activationEvents -Force
            $opencuiJson = $opencuiPackage | ConvertTo-Json -Depth 100
            [System.IO.File]::WriteAllText($opencuiPackagePath, $opencuiJson, [System.Text.UTF8Encoding]::new($false))
            Write-Host "Enabled startup activation for the OpenCode Panel extension in $($opencuiDir.FullName)"
        }
    } catch {
        Write-Warning "Failed to enable startup activation for the OpenCode Panel extension: $($_.Exception.Message)"
    }
}

try {
    $userDir = Join-Path $userDataDir "User"
    New-Item -ItemType Directory -Force -Path $userDir | Out-Null
    $userSettingsPath = Join-Path $userDir "settings.json"
    $settingsObj = [ordered]@{
        "security.workspace.trust.enabled" = $false
        "security.workspace.trust.startupPrompt" = "never"
        "security.workspace.trust.banner" = "never"
        "security.workspace.trust.emptyWindow" = $false
        "security.workspace.trust.untrustedFiles" = "open"
    }
    if ($resolvedOpencodeCli) {
        $settingsObj["opencui.binaryPath"] = $resolvedOpencodeCli
    }
    if (Test-Path $userSettingsPath) {
        try {
            $existing = [System.IO.File]::ReadAllText($userSettingsPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
            foreach ($prop in $existing.PSObject.Properties) {
                if (-not $settingsObj.Contains($prop.Name)) {
                    $settingsObj[$prop.Name] = $prop.Value
                }
            }
        } catch {}
    }
    $settingsJson = $settingsObj | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($userSettingsPath, $settingsJson, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Ensured user settings at $userSettingsPath"
} catch {
    Write-Warning "Could not write the editor user settings: $($_.Exception.Message)"
}

$cliDataDir = Join-Path $embedDir "cli-data-dir"
New-Item -ItemType Directory -Force -Path $cliDataDir | Out-Null
try {
    & $activeCliPath --cli-data-dir $cliDataDir serve-web --help | Out-Null
} catch {}

$roots = Get-VSCodeRoots -EditorDir $editorDir -EmbedDir $embedDir
foreach ($root in $roots) {
    $productJsonFiles = Get-ChildItem -Recurse -Filter "product.json" $root -ErrorAction SilentlyContinue
    foreach ($pj in $productJsonFiles) {
        Patch-ProductJson -ProductJsonPath $pj.FullName
    }
}

try {
    Get-Date -Format "o" | Out-File -FilePath $completedMarkerPath -Encoding utf8
} catch {
    Write-Warning "Could not write the editor install marker at $completedMarkerPath : $($_.Exception.Message)"
}

Write-Host "VS Code Embedded and Extension setup completed. Anything that could not be installed is listed in the warnings above."

