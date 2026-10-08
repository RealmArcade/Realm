import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import { REALM_ASSET_AGREEMENT_WARNING } from './constants';
import { formatFileSize, getErrorHtml } from './viewerUtils';

export class RealmRaudViewerProvider implements vscode.CustomReadonlyEditorProvider {
    public static readonly viewType = 'realm.raudViewer';

    public static register(context: vscode.ExtensionContext): vscode.Disposable {
        const provider = new RealmRaudViewerProvider(context);
        return vscode.window.registerCustomEditorProvider(RealmRaudViewerProvider.viewType, provider, {
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

        const raudPath = document.uri.fsPath;

        webviewPanel.webview.onDidReceiveMessage(async message => {
            if (message.command === 'exportOgg') {
                const confirmed = await vscode.window.showWarningMessage(
                    REALM_ASSET_AGREEMENT_WARNING,
                    { modal: true },
                    'Yes, Export OGG'
                );

                if (confirmed === 'Yes, Export OGG') {
                    try {
                        const defaultUri = vscode.Uri.file(path.join(path.dirname(raudPath), `${path.basename(raudPath, '.raud')}.ogg`));
                        const targetUri = await vscode.window.showSaveDialog({
                            defaultUri,
                            filters: { 'Ogg Vorbis Audio': ['ogg'] },
                            saveLabel: 'Export OGG'
                        });

                        if (targetUri) {
                            const buffer = fs.readFileSync(raudPath);
                            const parsed = this.parseRaud(buffer);
                            if (parsed && parsed.tracks.length > 0) {
                                fs.writeFileSync(targetUri.fsPath, parsed.tracks[0]);
                                vscode.window.showInformationMessage(`Successfully exported OGG to: ${targetUri.fsPath}`);
                            } else {
                                vscode.window.showErrorMessage('Failed to extract audio track from RAUD file.');
                            }
                        }
                    } catch (err: any) {
                        vscode.window.showErrorMessage(`Export failed: ${err?.message || err}`);
                    }
                }
            }
        });

        try {
            const fileBuffer = fs.readFileSync(raudPath);
            const parsed = this.parseRaud(fileBuffer);
            const stats = fs.statSync(raudPath);

            const track0Base64 = parsed && parsed.tracks.length > 0
                ? parsed.tracks[0].toString('base64')
                : null;

            webviewPanel.webview.html = this.getPreviewHtml(
                webviewPanel.webview,
                path.basename(raudPath),
                stats.size,
                parsed?.metadata || {},
                parsed?.tracks.length || 0,
                track0Base64
            );
        } catch (error: any) {
            webviewPanel.webview.html = getErrorHtml('Error Loading .raud', error?.message || 'Failed to load RAUD file.');
        }
    }

    private parseRaud(buffer: Buffer): { metadata: any; tracks: Buffer[] } | null {
        if (buffer.length < 16) return null;
        const magic = buffer.toString('ascii', 0, 4);
        if (magic !== 'RAUD') return null;

        const version = buffer.readUInt32LE(4);
        const metaLen = buffer.readUInt32LE(8);
        if (buffer.length < 12 + metaLen + 4) return null;

        let metadata: any = {};
        if (metaLen > 0) {
            try {
                const metaJson = buffer.toString('utf8', 12, 12 + metaLen);
                metadata = JSON.parse(metaJson);
            } catch {}
        }

        const trackCount = buffer.readUInt32LE(12 + metaLen);
        const tracks: Buffer[] = [];
        let offset = 16 + metaLen;

        for (let i = 0; i < trackCount; i++) {
            if (offset + 4 > buffer.length) break;
            const trackLen = buffer.readUInt32LE(offset);
            offset += 4;
            if (offset + trackLen > buffer.length) break;
            tracks.push(buffer.subarray(offset, offset + trackLen));
            offset += trackLen;
        }

        return { metadata, tracks };
    }

    private getPreviewHtml(webview: vscode.Webview, fileName: string, fileSize: number, metadata: any, trackCount: number, audioBase64: string | null): string {

        const author = metadata?.author || 'Unknown';
        const blake3 = metadata?.blake3 || 'None';
        const assetType = metadata?.asset_type || 'SoundEffect';
        const prefName = metadata?.preferred_file_name || fileName;
        const createdUtc = metadata?.created_utc || 'Unknown';
        const license = metadata?.license || 'Realm UGC License';

        const audioPlayerHtml = audioBase64
            ? `<div class="audio-player-box">
                <audio controls src="data:audio/ogg;base64,${audioBase64}" style="width: 100%;"></audio>
               </div>`
            : `<div class="audio-player-box" style="color: var(--vscode-descriptionForeground);">No audio track data available for playback</div>`;

        return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; media-src data:; style-src 'unsafe-inline'; script-src 'unsafe-inline';">
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
        .audio-player-box {
            margin-bottom: 20px;
            padding: 12px;
            background: rgba(0, 0, 0, 0.2);
            border-radius: 6px;
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
            background-color: var(--vscode-button-background, #0e639c);
            color: var(--vscode-button-foreground, #ffffff);
            border: none;
            padding: 8px 16px;
            border-radius: 4px;
            font-size: 13px;
            font-weight: 500;
            cursor: pointer;
            transition: background-color 0.15s ease;
        }
        .btn:hover {
            background-color: var(--vscode-button-hoverBackground, #1177bb);
        }
    </style>
</head>
<body>
    <div class="card">
        <div class="header">
            <div class="icon">🎵</div>
            <div class="title-group">
                <h1>${fileName}</h1>
                <p class="subtitle">Realm Audio Container (.raud)</p>
            </div>
        </div>

        ${audioPlayerHtml}

        <div class="meta-grid">
            <div class="meta-label">Asset Type:</div>
            <div class="meta-value"><span class="badge">${assetType}</span></div>

            <div class="meta-label">Author Tag:</div>
            <div class="meta-value"><strong>${author}</strong></div>

            <div class="meta-label">Track Count:</div>
            <div class="meta-value">${trackCount} track(s)</div>

            <div class="meta-label">File Size:</div>
            <div class="meta-value">${formatFileSize(fileSize)}</div>

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
            <button class="btn" onclick="exportOgg()">📤 Export to OGG...</button>
        </div>
    </div>

    <script>
        const vscode = acquireVsCodeApi();
        function exportOgg() {
            vscode.postMessage({ command: 'exportOgg' });
        }
    </script>
</body>
</html>`;
    }


}
