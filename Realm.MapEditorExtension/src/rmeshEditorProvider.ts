import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import * as os from 'os';
import * as crypto from 'crypto';
import { sendGodotIpc } from './extension';

function getTempGlbPath(rmeshFsPath: string): string {
    const tempDir = path.join(os.tmpdir(), 'realm_extension_previews');
    if (!fs.existsSync(tempDir)) {
        fs.mkdirSync(tempDir, { recursive: true });
    }
    const hash = crypto.createHash('md5').update(rmeshFsPath).digest('hex').substring(0, 12);
    return path.join(tempDir, `${hash}_${path.basename(rmeshFsPath, '.rmesh')}.glb`);
}

async function ensureGlbExtracted(rmeshFsPath: string): Promise<{ glbPath: string; metadata: any } | null> {
    const glbPath = getTempGlbPath(rmeshFsPath);
    const response = await sendGodotIpc({
        action: 'convertRmesh',
        inputPath: rmeshFsPath,
        outputPath: glbPath
    });
    if (!response || !response.success) {
        return null;
    }
    return { glbPath, metadata: response.metadata || {} };
}

export class RmeshGlbFileSystemProvider implements vscode.FileSystemProvider {
    public static readonly scheme = 'rmesh-git';

    private _emitter = new vscode.EventEmitter<vscode.FileChangeEvent[]>();
    readonly onDidChangeFile: vscode.Event<vscode.FileChangeEvent[]> = this._emitter.event;

    public static createVirtualUri(rmeshFsPath: string): vscode.Uri {
        const baseName = path.basename(rmeshFsPath, path.extname(rmeshFsPath));
        return vscode.Uri.from({
            scheme: RmeshGlbFileSystemProvider.scheme,
            authority: 'memory',
            path: `/${encodeURIComponent(rmeshFsPath)}/${baseName}.glb`
        });
    }

    public static getRmeshPathFromUri(uri: vscode.Uri): string | null {
        const parts = uri.path.split('/').filter(p => p.length > 0);
        if (parts.length > 0) {
            try {
                return decodeURIComponent(parts[0]);
            } catch {
                return null;
            }
        }
        return null;
    }

    watch(_uri: vscode.Uri, _options: { readonly recursive: boolean; readonly excludes: readonly string[] }): vscode.Disposable {
        return new vscode.Disposable(() => {});
    }

    stat(uri: vscode.Uri): vscode.FileStat {
        const rmeshPath = RmeshGlbFileSystemProvider.getRmeshPathFromUri(uri);
        if (!rmeshPath || !fs.existsSync(rmeshPath)) {
            throw vscode.FileSystemError.FileNotFound(uri);
        }
        const glbPath = getTempGlbPath(rmeshPath);
        if (!fs.existsSync(glbPath)) {
            throw vscode.FileSystemError.FileNotFound(uri);
        }
        try {
            const rmeshStats = fs.statSync(rmeshPath);
            const glbStats = fs.statSync(glbPath);
            return {
                type: vscode.FileType.File,
                ctime: rmeshStats.ctimeMs,
                mtime: rmeshStats.mtimeMs,
                size: glbStats.size
            };
        } catch (err: any) {
            if (err instanceof vscode.FileSystemError) throw err;
            throw vscode.FileSystemError.Unavailable(err?.message || 'Error reading rmesh file');
        }
    }

    readDirectory(_uri: vscode.Uri): [string, vscode.FileType][] {
        return [];
    }

    createDirectory(_uri: vscode.Uri): void {
        throw vscode.FileSystemError.NoPermissions();
    }

    readFile(uri: vscode.Uri): Uint8Array {
        const rmeshPath = RmeshGlbFileSystemProvider.getRmeshPathFromUri(uri);
        if (!rmeshPath || !fs.existsSync(rmeshPath)) {
            throw vscode.FileSystemError.FileNotFound(uri);
        }
        const glbPath = getTempGlbPath(rmeshPath);
        if (!fs.existsSync(glbPath)) {
            throw vscode.FileSystemError.FileNotFound(uri);
        }
        try {
            const glbBuffer = fs.readFileSync(glbPath);
            return new Uint8Array(glbBuffer.buffer, glbBuffer.byteOffset, glbBuffer.byteLength);
        } catch (err: any) {
            if (err instanceof vscode.FileSystemError) throw err;
            throw vscode.FileSystemError.Unavailable(err?.message || 'Error reading decompressed GLB payload');
        }
    }

    writeFile(_uri: vscode.Uri, _content: Uint8Array, _options: { readonly create: boolean; readonly overwrite: boolean }): void {
        throw vscode.FileSystemError.NoPermissions();
    }

    delete(_uri: vscode.Uri, _options: { readonly recursive: boolean }): void {
        throw vscode.FileSystemError.NoPermissions();
    }

    rename(_oldUri: vscode.Uri, _newUri: vscode.Uri, _options: { readonly overwrite: boolean }): void {
        throw vscode.FileSystemError.NoPermissions();
    }
}

export async function openRmeshInGlbViewer(rmeshPath: string): Promise<void> {
    try {
        const result = await ensureGlbExtracted(rmeshPath);
        if (!result) {
            vscode.window.showErrorMessage('Failed to extract GLB from RMESH: Godot IPC returned an error. Make sure Godot is running with the Realm project open.');
            return;
        }
        const virtualUri = RmeshGlbFileSystemProvider.createVirtualUri(rmeshPath);
        await vscode.commands.executeCommand('vscode.openWith', virtualUri, 'glbViewer.customEditor', { preview: false });
    } catch (err: any) {
        vscode.window.showErrorMessage(`Failed to open 3D GLB Viewer: ${err?.message || err}. Make sure OHZIInteractiveStudio.ohzi-vscode-glb-viewer is installed.`);
    }
}

export class RealmRmeshViewerProvider implements vscode.CustomReadonlyEditorProvider {
    public static readonly viewType = 'realm.rmeshViewer';

    public static register(context: vscode.ExtensionContext): vscode.Disposable {
        const provider = new RealmRmeshViewerProvider(context);
        return vscode.window.registerCustomEditorProvider(RealmRmeshViewerProvider.viewType, provider, {
            supportsMultipleEditorsPerDocument: false
        });
    }

    constructor(
        private readonly context: vscode.ExtensionContext
    ) {}

    public async openCustomDocument(
        uri: vscode.Uri,
        _openContext: vscode.CustomDocumentOpenContext,
        _token: vscode.CancellationToken
    ): Promise<vscode.CustomDocument> {
        return {
            uri,
            dispose: () => {}
        };
    }

    public async resolveCustomEditor(
        document: vscode.CustomDocument,
        webviewPanel: vscode.WebviewPanel,
        _token: vscode.CancellationToken
    ): Promise<void> {
        webviewPanel.webview.options = {
            enableScripts: true
        };

        const rmeshPath = document.uri.fsPath;

        webviewPanel.webview.onDidReceiveMessage(async message => {
            if (message.command === 'open3d') {
                await openRmeshInGlbViewer(rmeshPath);
            } else if (message.command === 'exportGlb') {
                const confirmed = await vscode.window.showWarningMessage(
                    'The Realm Asset Agreement states that files cannot be used outside the Realm UGC platform unless you are the original author of the asset. Do you understand?',
                    { modal: true },
                    'Yes, Export GLB'
                );

                if (confirmed === 'Yes, Export GLB') {
                    try {
                        const defaultUri = vscode.Uri.file(path.join(path.dirname(rmeshPath), `${path.basename(rmeshPath, path.extname(rmeshPath))}.glb`));
                        const targetUri = await vscode.window.showSaveDialog({
                            defaultUri,
                            filters: { 'GLTF Binary Model': ['glb'] },
                            saveLabel: 'Export GLB'
                        });

                        if (targetUri) {
                            const result = await ensureGlbExtracted(rmeshPath);
                            if (result && fs.existsSync(result.glbPath)) {
                                fs.copyFileSync(result.glbPath, targetUri.fsPath);
                                vscode.window.showInformationMessage(`Successfully exported GLB to: ${targetUri.fsPath}`);
                            } else {
                                vscode.window.showErrorMessage('Failed to extract GLB payload from RMESH file. Make sure Godot is running.');
                            }
                        }
                    } catch (err: any) {
                        vscode.window.showErrorMessage(`Export failed: ${err?.message || err}`);
                    }
                }
            }
        });

        try {
            const stats = fs.statSync(rmeshPath);
            const ipcResult = await ensureGlbExtracted(rmeshPath);
            const glbSize = ipcResult && fs.existsSync(ipcResult.glbPath) ? fs.statSync(ipcResult.glbPath).size : 0;
            const metadata = ipcResult?.metadata || {};

            webviewPanel.webview.html = this.getPreviewHtml(
                path.basename(rmeshPath),
                stats.size,
                metadata,
                glbSize
            );
        } catch (error: any) {
            webviewPanel.webview.html = this.getErrorHtml(error?.message || 'Failed to load RMESH file.');
        }
    }

    private getPreviewHtml(fileName: string, fileSize: number, metadata: any, glbSize: number): string {
        const nonce = crypto.randomBytes(16).toString('base64');

        const formatSize = (bytes: number) => {
            if (bytes >= 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(2) + ' MB';
            if (bytes >= 1024) return (bytes / 1024).toFixed(1) + ' KB';
            return bytes + ' B';
        };

        const author = metadata?.author || 'Unknown';
        const blake3 = metadata?.blake3 || 'None';
        const assetType = metadata?.asset_type || 'None';
        const teamColor = metadata?.chroma_key ? '✅ Enabled' : '❌ Disabled';
        const prefName = metadata?.preferred_file_name || fileName;
        const createdUtc = metadata?.created_utc || 'Unknown';
        const license = metadata?.license || 'Realm UGC License';

        return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
    <style>
        body {
            display: flex;
            flex-direction: column;
            align-items: center;
            justify-content: center;
            min-height: 100vh;
            margin: 0;
            padding: 24px;
            background-color: var(--vscode-editor-background);
            color: var(--vscode-editor-foreground);
            font-family: var(--vscode-font-family);
            box-sizing: border-box;
        }
        .card {
            background-color: var(--vscode-sideBar-background, rgba(255, 255, 255, 0.05));
            border: 1px solid var(--vscode-widget-border, rgba(255, 255, 255, 0.1));
            border-radius: 8px;
            padding: 24px;
            width: 100%;
            max-width: 560px;
            box-shadow: 0 4px 16px rgba(0, 0, 0, 0.2);
        }
        .header {
            display: flex;
            align-items: center;
            gap: 12px;
            margin-bottom: 20px;
            border-bottom: 1px solid var(--vscode-widget-border, rgba(255, 255, 255, 0.1));
            padding-bottom: 16px;
        }
        .icon {
            font-size: 32px;
        }
        .title-group h1 {
            margin: 0 0 4px 0;
            font-size: 18px;
            color: var(--vscode-editor-foreground);
        }
        .title-group .subtitle {
            margin: 0;
            font-size: 12px;
            color: var(--vscode-descriptionForeground);
        }
        .meta-grid {
            display: grid;
            grid-template-columns: 140px 1fr;
            gap: 10px 16px;
            font-size: 13px;
            margin-bottom: 24px;
        }
        .meta-label {
            color: var(--vscode-descriptionForeground);
            font-weight: 500;
        }
        .meta-value {
            word-break: break-all;
            font-family: var(--vscode-editor-font-family, monospace);
        }
        .badge {
            display: inline-block;
            padding: 2px 8px;
            border-radius: 4px;
            font-size: 11px;
            font-weight: 600;
            background-color: var(--vscode-badge-background, #388bfd33);
            color: var(--vscode-badge-foreground, #58a6ff);
        }
        .actions {
            display: flex;
            justify-content: flex-end;
            gap: 12px;
        }
        .btn {
            border: none;
            padding: 8px 16px;
            border-radius: 4px;
            font-size: 13px;
            font-weight: 500;
            cursor: pointer;
            transition: background-color 0.15s ease, transform 0.05s ease;
            display: inline-flex;
            align-items: center;
            gap: 6px;
        }
        .btn:active {
            transform: scale(0.98);
        }
        .btn-primary {
            background-color: var(--vscode-button-background, #0e639c);
            color: var(--vscode-button-foreground, #ffffff);
            font-weight: 600;
        }
        .btn-primary:hover {
            background-color: var(--vscode-button-hoverBackground, #1177bb);
        }
        .btn-secondary {
            background-color: var(--vscode-button-secondaryBackground, #3a3d41);
            color: var(--vscode-button-secondaryForeground, #ffffff);
        }
        .btn-secondary:hover {
            background-color: var(--vscode-button-secondaryHoverBackground, #45494e);
        }
    </style>
</head>
<body>
    <div class="card">
        <div class="header">
            <div class="icon">📦</div>
            <div class="title-group">
                <h1>${fileName}</h1>
                <p class="subtitle">Realm 3D Mesh Container (.rmesh)</p>
            </div>
        </div>

        <div class="meta-grid">
            <div class="meta-label">Asset Type:</div>
            <div class="meta-value"><span class="badge">${assetType}</span></div>

            <div class="meta-label">Author Tag:</div>
            <div class="meta-value"><strong>${author}</strong></div>

            <div class="meta-label">Team Color:</div>
            <div class="meta-value">${teamColor}</div>

            <div class="meta-label">Total Size:</div>
            <div class="meta-value">${formatSize(fileSize)} (GLB: ${formatSize(glbSize)})</div>

            <div class="meta-label">Preferred Name:</div>
            <div class="meta-value">${prefName}</div>

            <div class="meta-label">BLAKE3 Hash:</div>
            <div class="meta-value"><small>${blake3}</small></div>

            <div class="meta-label">License:</div>
            <div class="meta-value">${license}</div>

            <div class="meta-label">Created UTC:</div>
            <div class="meta-value"><small>${createdUtc}</small></div>
        </div>

        <div class="actions">
            <button id="btn-open3d" class="btn btn-primary">🎮 Open in 3D Viewer (OHZI)</button>
            <button id="btn-export-glb" class="btn btn-secondary">📤 Export to GLB...</button>
        </div>
    </div>

    <script nonce="${nonce}">
        const vscode = acquireVsCodeApi();
        document.getElementById('btn-open3d').addEventListener('click', function() {
            vscode.postMessage({ command: 'open3d' });
        });
        document.getElementById('btn-export-glb').addEventListener('click', function() {
            vscode.postMessage({ command: 'exportGlb' });
        });
    </script>
</body>
</html>`;
    }

    private getErrorHtml(errorMessage: string): string {
        return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <style>
        body { display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; background-color: var(--vscode-editor-background); color: var(--vscode-errorForeground, #f48771); font-family: var(--vscode-font-family); }
        .error-box { padding: 16px; border: 1px solid var(--vscode-inputValidation-errorBorder, #be1100); border-radius: 4px; background-color: var(--vscode-inputValidation-errorBackground, rgba(255, 0, 0, 0.1)); max-width: 80%; }
    </style>
</head>
<body>
    <div class="error-box">
        <strong>Error Loading .rmesh:</strong><br/>
        ${errorMessage}
    </div>
</body>
</html>`;
    }
}
