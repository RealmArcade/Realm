# NOTE: Can't use official VSCode due to license. VSCodium is MIT version of VSCode. They're nearly identical git repos but microsoft version has minor customizations.
param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$godotDir = $PSScriptRoot
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

$oldExtDest = Join-Path $extsDir "realm-map-editor"
if (Test-Path $oldExtDest) {
    Remove-Item -Path $oldExtDest -Recurse -Force
}

$extensionsJsonCheck = Join-Path $extsDir "extensions.json"
if (Test-Path $extensionsJsonCheck) {
    try {
        $fi = Get-Item $extensionsJsonCheck -ErrorAction SilentlyContinue
        if ($fi -and $fi.Length -gt 10MB) {
            Write-Host "Removing excessively large extensions.json ($($fi.Length) bytes)..."
            Remove-Item -Path $extensionsJsonCheck -Force -ErrorAction SilentlyContinue
        }
    } catch {}
}

$wasiVersion = "34"
$wasiSdkBaseDir = Join-Path $appDataDir "wasi_sdk"
$wasiTargetDir = Join-Path $wasiSdkBaseDir "wasi-sdk-$wasiVersion"
$wasiClang = Join-Path $wasiTargetDir "bin\clang.exe"

$shouldInstallWasi = $Force -or (-not (Test-Path $wasiClang)) -or ((Get-Item $wasiClang).Length -eq 0)

if ($shouldInstallWasi) {
    Write-Host "Downloading WASI SDK $wasiVersion..."
    New-Item -ItemType Directory -Force -Path $wasiTargetDir | Out-Null
    $wasiTar = Join-Path $wasiSdkBaseDir "wasi-sdk-$wasiVersion.tar.gz"
    $wasiUrl = "https://github.com/WebAssembly/wasi-sdk/releases/download/wasi-sdk-$wasiVersion/wasi-sdk-$wasiVersion.0-x86_64-windows.tar.gz"
    curl.exe -L $wasiUrl -o $wasiTar

    Write-Host "Extracting WASI SDK $wasiVersion..."
    tar.exe -xf $wasiTar -C $wasiTargetDir --strip-components=1
    if (Test-Path $wasiTar) {
        Remove-Item -Path $wasiTar -Force
    }
    Write-Host "WASI SDK $wasiVersion installed to $wasiTargetDir successfully."
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

    Write-Host "Downloading VSCodium CLI..."
    $cliArchive = Join-Path $embedDir "vscodium-cli.tar.gz"
    $cliDownloadUrl = if ($remoteCliUrl) { $remoteCliUrl } else { "https://github.com/VSCodium/vscodium/releases/download/$fallbackVersion/vscodium-cli-win32-x64-$fallbackVersion.tar.gz" }
    curl.exe -L $cliDownloadUrl -o $cliArchive

    Write-Host "Extracting VSCodium CLI..."
    tar.exe -xf $cliArchive -C $binDir
    if (Test-Path $cliArchive) {
        Remove-Item -Path $cliArchive -Force
    }
    Write-Host "VSCodium CLI installed successfully."

    Write-Host "Downloading VSCodium Desktop..."
    $desktopZip = Join-Path $embedDir "vscodium-desktop.zip"
    $desktopDownloadUrl = if ($remoteDesktopUrl) { $remoteDesktopUrl } else { "https://github.com/VSCodium/vscodium/releases/download/$fallbackVersion/VSCodium-win32-x64-$fallbackVersion.zip" }
    curl.exe -L $desktopDownloadUrl -o $desktopZip

    Write-Host "Extracting VSCodium Desktop..."
    Expand-Archive -Path $desktopZip -DestinationPath $editorDir -Force
    if (Test-Path $desktopZip) {
        Remove-Item -Path $desktopZip -Force
    }
    Write-Host "VSCodium Desktop installed successfully."

    $productJsonFiles = Get-ChildItem -Recurse -Filter "product.json" $editorDir
    foreach ($pj in $productJsonFiles) {
        $content = [System.IO.File]::ReadAllText($pj.FullName, [System.Text.Encoding]::UTF8)
        if ($content -match 'vscode-cdn\.net') {
            Write-Host "Patching webview CDN endpoint in $($pj.FullName)..."
            $content = $content -replace '"webviewContentExternalBaseUrlTemplate":\s*"https://\{\{uuid\}\}\.vscode-cdn\.net/\{\{quality\}\}/\{\{commit\}\}/out/vs/workbench/contrib/webview/browser/pre/"', '"webviewContentExternalBaseUrlTemplate": "{{commit}}/out/vs/workbench/contrib/webview/browser/pre/"'
            [System.IO.File]::WriteAllText($pj.FullName, $content, $utf8NoBom)
        }
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
    Write-Host "Installing Realm Map Editor extension version $extVersion to $extDest..."
    if (Test-Path (Join-Path $extSrc "package.json")) {
        Copy-Item -Path (Join-Path $extSrc "package.json") -Destination (Join-Path $extDest "package.json") -Force
    }
    if (Test-Path (Join-Path $extSrc "map_schema.json")) {
        Copy-Item -Path (Join-Path $extSrc "map_schema.json") -Destination (Join-Path $extDest "map_schema.json") -Force
    }
    if (Test-Path (Join-Path $extSrc "dist")) {
        $destDist = Join-Path $extDest "dist"
        New-Item -ItemType Directory -Force -Path $destDist | Out-Null
        Copy-Item -Path (Join-Path $extSrc "dist\*") -Destination $destDist -Recurse -Force
    }
    if (Test-Path (Join-Path $extSrc "media")) {
        $destMedia = Join-Path $extDest "media"
        New-Item -ItemType Directory -Force -Path $destMedia | Out-Null
        Copy-Item -Path (Join-Path $extSrc "media\*") -Destination $destMedia -Recurse -Force
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
    # "akondratiuk1-dev.texture-viewer",
    "Google.google-antigravity"
)

foreach ($extId in $requiredExtensions) {
    $extMatch = Get-ChildItem -Path $extsDir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "$extId*" -or $_.Name -like "*$extId*" }
    if ((-not $extMatch) -or $Force) {
        Write-Host "Installing extension $extId from Open VSX..."
        & $activeCliPath --extensions-dir $extsDir --user-data-dir $userDataDir --install-extension $extId
    } else {
        Write-Host "Extension $extId already installed."
    }
}

function Patch-OhziExtension {
    param(
        [string]$TargetExtensionsDir,
        [switch]$ForcePatch
    )

    $patchSrcCandidates = @(
        (Join-Path $godotDir "vscode_extensions_dist\patches\ohzi-vscode-glb-viewer\extension.js"),
        (Join-Path $godotDir "..\Realm.MapEditorExtension\patches\ohzi-vscode-glb-viewer\extension.js"),
        (Join-Path $PSScriptRoot "..\Realm.MapEditorExtension\patches\ohzi-vscode-glb-viewer\extension.js")
    )

    $patchFile = $null
    foreach ($candidate in $patchSrcCandidates) {
        if (Test-Path $candidate) {
            $patchFile = $candidate
            break
        }
    }

    if (-not $patchFile) {
        Write-Warning "OHZI patch file not found in candidates."
        return
    }

    $ohziDirs = Get-ChildItem -Path $TargetExtensionsDir -Directory -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -like "*ohzi-vscode-glb-viewer*"
    }

    foreach ($ohziDir in $ohziDirs) {
        $extJsPath = Join-Path $ohziDir.FullName "extension.js"
        $needsPatch = $ForcePatch -or (-not (Test-Path $extJsPath))
        if (-not $needsPatch -and (Test-Path $extJsPath)) {
            $content = [System.IO.File]::ReadAllText($extJsPath, [System.Text.Encoding]::UTF8)
            if (-not $content.Contains("REALM_PATCHED_OHZI_BASE64") -and (-not $content.Contains("threeDataUri") -or $content.Contains("loadModelFromUri"))) {
                $needsPatch = $true
            }
        }

        if ($needsPatch) {
            Copy-Item -Path $patchFile -Destination $extJsPath -Force
            Write-Host "Patched OHZI GLB viewer extension in $($ohziDir.FullName)"
        } else {
            Write-Host "OHZI GLB viewer extension in $($ohziDir.FullName) already patched."
        }
    }
}

Patch-OhziExtension -TargetExtensionsDir $extsDir -ForcePatch:$Force

Get-Date -Format "o" | Out-File -FilePath $completedMarkerPath -Encoding utf8
Write-Host "VS Code Embedded and Extension setup completed successfully!"
