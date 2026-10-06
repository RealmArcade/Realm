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

        $cdnTemplate = "/static/out/vs/workbench/contrib/webview/browser/pre/"
        if ($pj.webviewContentExternalBaseUrlTemplate -ne $cdnTemplate) {
            $pj.webviewContentExternalBaseUrlTemplate = $cdnTemplate
            $changed = $true
        }

        if (-not $pj.extensionKind) {
            $pj | Add-Member -MemberType NoteProperty -Name "extensionKind" -Value ([PSCustomObject]@{})
            $changed = $true
        }

        $workspaceExts = @("google.google-antigravity", "muhammad-sammy.csharp", "patcx.vscode-nuget-gallery", "speige.realm-map-editor")
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

function Patch-WebviewHostAndStreams {
    param([string]$RootDir)

    if (-not (Test-Path $RootDir)) { return }

    $outDirs = [System.Collections.Generic.List[string]]::new()
    $candidateOut = Join-Path $RootDir "out"
    if (Test-Path $candidateOut) { $outDirs.Add($candidateOut) }

    foreach ($outDir in ($outDirs | Select-Object -Unique)) {
        $jsPath = Join-Path $outDir "vs\code\browser\workbench\workbench.js"
        if (Test-Path $jsPath) {
            try {
                $content = [System.IO.File]::ReadAllText($jsPath, [System.Text.Encoding]::UTF8)
                $changed = $false

                $wbTargetEndpoint = 'get webviewExternalEndpoint(){const i=this.options.webviewEndpoint||this.productService.webviewContentExternalBaseUrlTemplate||"https://{{uuid}}.vscode-cdn.net/{{quality}}/{{commit}}/out/vs/workbench/contrib/webview/browser/pre/",e=this.payload?.get("webviewExternalEndpointCommit");return i.replace("{{commit}}",e??this.productService.commit??"ef65ac1ba57f57f2a3961bfe94aa20481caca4c6").replace("{{quality}}",(e?"insider":this.productService.quality)??"insider")}'
                $wbReplEndpoint = 'get webviewExternalEndpoint(){return (window.location.origin + "/static/out/vs/workbench/contrib/webview/browser/pre/");}'
                if ($content.Contains($wbTargetEndpoint)) {
                    $content = $content.Replace($wbTargetEndpoint, $wbReplEndpoint)
                    $changed = $true
                }

                if ($content.Contains("t.port1.postMessage(e,[e])")) {
                    $content = $content.Replace("try{const e=new ReadableStream,t=new MessageChannel;return t.port1.postMessage(e,[e]),t.port1.close(),t.port2.close(),!0}catch{return!1}", "try{return!1}catch{return!1}")
                    $changed = $true
                }

                if ($changed) {
                    [System.IO.File]::WriteAllText($jsPath, $content, [System.Text.UTF8Encoding]::new($false))
                    Write-Host "Patched transferable streams support in $jsPath"
                }
            } catch {}
        }

        $htmlPath = Join-Path $outDir "vs\workbench\contrib\webview\browser\pre\index.html"
        if (Test-Path $htmlPath) {
            try {
                $content = [System.IO.File]::ReadAllText($htmlPath, [System.Text.Encoding]::UTF8)
                $changed = $false

                $hostCheckTarget = "if (hostname === parentOriginHash || hostname.startsWith(parentOriginHash + '.')) {"
                $hostCheckRepl = "if (hostname === '127.0.0.1' || hostname === 'localhost' || hostname === parentOriginHash || hostname.startsWith(parentOriginHash + '.')) {"
                if ($content.Contains($hostCheckTarget)) {
                    $content = $content.Replace($hostCheckTarget, $hostCheckRepl)
                    $changed = $true
                }

                if ($content -match "script-src\s+[^;]+;" -and (-not $content.Contains("script-src 'self' 'unsafe-inline';"))) {
                    $content = [System.Text.RegularExpressions.Regex]::Replace($content, "script-src\s+[^;]+;", "script-src 'self' 'unsafe-inline';")
                    $changed = $true
                }

                $cspTarget = "frame-src 'self';"
                $cspRepl = "frame-src 'self' http://127.0.0.1:* http://localhost:* vscode-webview:;"
                if ($content.Contains($cspTarget)) {
                    $content = $content.Replace($cspTarget, $cspRepl)
                    $changed = $true
                }

                $pattern = "hostMessaging\.onMessage\('did-load-resource'[\s\S]*?assertIsDefined\(navigator\.serviceWorker\.controller\)\.postMessage\(\{ channel: 'did-load-resource',\s*data \}[^\n\r;]*\);(?:\s*\}\s*catch[^\}]*\})?\s*\}\);"
                if ($content -match $pattern) {
                    $replacement = "hostMessaging.onMessage('did-load-resource', (_event, data) => {`n`t`t`tif (data && data.stream) { try { data.stream.cancel().catch(() => {}); } catch {} delete data.stream; }`n`t`t`ttry { if (navigator.serviceWorker && navigator.serviceWorker.controller) { navigator.serviceWorker.controller.postMessage({ channel: 'did-load-resource', data }); } } catch (e) { console.warn('SW did-load-resource postMessage failed:', e); }`n`t`t});"
                    $content = [System.Text.RegularExpressions.Regex]::Replace($content, $pattern, $replacement)
                    $changed = $true
                }


                if ($changed) {
                    [System.IO.File]::WriteAllText($htmlPath, $content, [System.Text.UTF8Encoding]::new($false))
                    Write-Host "Patched service worker postMessage error guard and CSP in $htmlPath"
                }
            } catch {}
        }

    }
}

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
    foreach ($item in @("package.json", "map_schema.json", "dist", "media")) {
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
} else {
    Write-Host "Realm Map Editor extension ($extVersion) verified at $extDest"
}

$requiredExtensions = @(
    "google.google-antigravity",
    "muhammad-sammy.csharp",
    "OHZIInteractiveStudio.ohzi-vscode-glb-viewer",
    "Gruntfuggly.todo-tree",
	# "mechatroner.rainbow-json",
    "patcx.vscode-nuget-gallery",
    "AykutSarac.jsoncrack-vscode"
	# "akondratiuk1-dev.texture-viewer"
)

foreach ($extId in $requiredExtensions) {
    $extMatch = Get-ChildItem -Path $extsDir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "$extId*" -or $_.Name -like "*$extId*" }
    if ((-not $extMatch) -or $Force) {
        Write-Host "Installing extension $extId from Open VSX..."
        & $activeCliPath --extensions-dir $extsDir --user-data-dir $userDataDir --install-extension $extId --force
    } else {
        Write-Host "Extension $extId already installed."
    }
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
    Patch-WebviewHostAndStreams -RootDir $root
}

Get-Date -Format "o" | Out-File -FilePath $completedMarkerPath -Encoding utf8
Write-Host "VS Code Embedded and Extension setup completed successfully!"

