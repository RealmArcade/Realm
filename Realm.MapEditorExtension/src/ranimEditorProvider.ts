import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import { sendGodotIpc } from './extension';
import { MIXAMO_EXPORT_LICENSING_WARNING } from './constants';
import { getPreviewTempPath, getLoadingHtml, getErrorHtml } from './viewerUtils';

export class RealmRanimViewerProvider implements vscode.CustomReadonlyEditorProvider {
    public static readonly viewType = 'realm.ranimViewer';

    public static register(context: vscode.ExtensionContext): vscode.Disposable {
        const provider = new RealmRanimViewerProvider(context);
        return vscode.window.registerCustomEditorProvider(RealmRanimViewerProvider.viewType, provider, {
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
        webviewPanel.webview.options = { enableScripts: true };

        const ranimPath = document.uri.fsPath;
        const outputWebpPath = getPreviewTempPath(ranimPath, '.webp');

        webviewPanel.webview.onDidReceiveMessage(async message => {
            if (message.command === 'exportRanim') {
                vscode.window.showWarningMessage(MIXAMO_EXPORT_LICENSING_WARNING);
            }
        });

        webviewPanel.webview.html = getLoadingHtml('Rendering skeletal animation preview (.webp)...');

        try {
            const response = await sendGodotIpc({
                action: 'renderRanim',
                inputPath: ranimPath,
                outputPath: outputWebpPath
            });

            if (!response || !response.success || !fs.existsSync(outputWebpPath)) {
                throw new Error(response?.error || 'Godot IPC failed to render .ranim animation.');
            }

            const webpBytes = fs.readFileSync(outputWebpPath);
            const base64DataUri = `data:image/webp;base64,${webpBytes.toString('base64')}`;
            webviewPanel.webview.html = this.getPreviewHtml(base64DataUri, path.basename(ranimPath));
        } catch (error: any) {
            webviewPanel.webview.html = getErrorHtml('Failed to load .ranim preview', error?.message || 'Failed to render .ranim animation.');
        }
    }

    private getPreviewHtml(base64DataUri: string, title: string): string {
        return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; style-src 'unsafe-inline'; script-src 'unsafe-inline';">
    <style>
        body { display: flex; flex-direction: column; align-items: center; justify-content: center; min-height: 100vh; margin: 0; padding: 20px; background-color: var(--vscode-editor-background); color: var(--vscode-editor-foreground); font-family: var(--vscode-font-family); box-sizing: border-box; }
        .header { margin-bottom: 16px; font-weight: 600; font-size: 14px; color: var(--vscode-descriptionForeground); }
        .image-container { display: flex; align-items: center; justify-content: center; padding: 12px; border: 1px solid var(--vscode-widget-border, #454545); border-radius: 6px; background-color: rgba(0, 0, 0, 0.2); max-width: 90vw; max-height: 75vh; overflow: auto; }
        img { max-width: 100%; max-height: 70vh; object-fit: contain; }
        .actions { display: flex; justify-content: center; margin-top: 16px; }
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
    <div class="header">${title} (Animated WebP Preview)</div>
    <div class="image-container">
        <img src="${base64DataUri}" alt="Animated WebP Preview" />
    </div>
    <div class="actions">
        <button class="btn" onclick="exportRanim()">📤 Export Animation...</button>
    </div>
    <script>
        const vscode = acquireVsCodeApi();
        function exportRanim() {
            vscode.postMessage({ command: 'exportRanim' });
        }
    </script>
</body>
</html>`;
    }
}
