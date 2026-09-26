import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import * as os from 'os';
import { sendGodotIpc } from './extension';

export class RealmRtexViewerProvider implements vscode.CustomReadonlyEditorProvider {
    public static readonly viewType = 'realm.rtexViewer';

    public static register(context: vscode.ExtensionContext): vscode.Disposable {
        const provider = new RealmRtexViewerProvider(context);
        return vscode.window.registerCustomEditorProvider(RealmRtexViewerProvider.viewType, provider, {
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
        const tempDir = path.join(os.tmpdir(), 'realm_extension_previews');
        if (!fs.existsSync(tempDir)) {
            fs.mkdirSync(tempDir, { recursive: true });
        }

        webviewPanel.webview.options = { enableScripts: false };

        const rtexPath = document.uri.fsPath;
        const fileNameHash = Buffer.from(rtexPath).toString('hex').substring(0, 12);
        const outputPngPath = path.join(tempDir, `${fileNameHash}_${path.basename(rtexPath, '.rtex')}.png`);

        webviewPanel.webview.html = this.getLoadingHtml();

        try {
            const response = await sendGodotIpc({
                action: 'convertRtex',
                inputPath: rtexPath,
                outputPath: outputPngPath
            });

            if (!response || !response.success || !fs.existsSync(outputPngPath)) {
                throw new Error(response?.error || 'Godot IPC failed to convert RTEX preview.');
            }

            const pngBytes = fs.readFileSync(outputPngPath);
            const base64DataUri = `data:image/png;base64,${pngBytes.toString('base64')}`;
            webviewPanel.webview.html = this.getPreviewHtml(base64DataUri, path.basename(rtexPath), response.metadata);
        } catch (error: any) {
            webviewPanel.webview.html = this.getErrorHtml(error?.message || 'Failed to load RTEX preview.');
        }
    }

    private getLoadingHtml(): string {
        return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <style>
        body { display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; background-color: var(--vscode-editor-background); color: var(--vscode-editor-foreground); font-family: var(--vscode-font-family); }
        .spinner { border: 4px solid rgba(255, 255, 255, 0.1); width: 36px; height: 36px; border-radius: 50%; border-left-color: var(--vscode-progressBar-background, #0e639c); animation: spin 1s linear infinite; margin-bottom: 12px; }
        @keyframes spin { 0% { transform: rotate(0deg); } 100% { transform: rotate(360deg); } }
        .container { display: flex; flex-direction: column; align-items: center; }
    </style>
</head>
<body>
    <div class="container">
        <div class="spinner"></div>
        <div>Loading RTEX texture preview...</div>
    </div>
</body>
</html>`;
    }

    private getPreviewHtml(base64DataUri: string, title: string, metadata?: any): string {
        const typeInfo = metadata?.asset_type ? `<div class="meta-item"><strong>Type:</strong> ${metadata.asset_type}</div>` : '';
        const tagsInfo = metadata?.tags && Array.isArray(metadata.tags) && metadata.tags.length > 0 ? `<div class="meta-item"><strong>Tags:</strong> ${metadata.tags.slice(0, 10).join(', ')}${metadata.tags.length > 10 ? '...' : ''}</div>` : '';

        return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; style-src 'unsafe-inline';">
    <style>
        body { display: flex; flex-direction: column; align-items: center; justify-content: center; min-height: 100vh; margin: 0; padding: 20px; background-color: var(--vscode-editor-background); color: var(--vscode-editor-foreground); font-family: var(--vscode-font-family); box-sizing: border-box; }
        .header { margin-bottom: 12px; font-weight: 600; font-size: 15px; color: var(--vscode-descriptionForeground); }
        .meta-container { margin-bottom: 14px; font-size: 12px; color: var(--vscode-descriptionForeground); display: flex; gap: 16px; flex-wrap: wrap; justify-content: center; }
        .meta-item strong { color: var(--vscode-editor-foreground); }
        .image-container { display: flex; align-items: center; justify-content: center; padding: 12px; border: 1px solid var(--vscode-widget-border, #454545); border-radius: 6px; background-color: rgba(0, 0, 0, 0.2); max-width: 90vw; max-height: 75vh; overflow: auto; }
        img { max-width: 100%; max-height: 70vh; object-fit: contain; image-rendering: pixelated; }
    </style>
</head>
<body>
    <div class="header">${title}</div>
    ${typeInfo || tagsInfo ? `<div class="meta-container">${typeInfo}${tagsInfo}</div>` : ''}
    <div class="image-container">
        <img src="${base64DataUri}" alt="RTEX Preview" />
    </div>
</body>
</html>`;
    }

    private getErrorHtml(errorMessage: string): string {
        return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <style>
        body { display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; background-color: var(--vscode-editor-background); color: var(--vscode-errorForeground, #f48771); font-family: var(--vscode-font-family); padding: 20px; text-align: center; }
        .error-box { border: 1px solid var(--vscode-inputValidation-errorBorder, #be1100); background-color: var(--vscode-inputValidation-errorBackground, #5a1d1d); padding: 16px 24px; border-radius: 6px; max-width: 600px; }
    </style>
</head>
<body>
    <div class="error-box">
        <h3>Failed to load RTEX preview</h3>
        <p>${errorMessage}</p>
    </div>
</body>
</html>`;
    }
}
