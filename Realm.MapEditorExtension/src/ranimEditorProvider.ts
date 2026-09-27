import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import * as os from 'os';
import { sendGodotIpc } from './extension';

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
        const tempDir = path.join(os.tmpdir(), 'realm_extension_previews');
        if (!fs.existsSync(tempDir)) {
            fs.mkdirSync(tempDir, { recursive: true });
        }

        webviewPanel.webview.options = {
            enableScripts: true,
            localResourceRoots: [vscode.Uri.file(tempDir)]
        };

        const ranimPath = document.uri.fsPath;
        const fileNameHash = Buffer.from(ranimPath).toString('hex').substring(0, 12);
        const outputWebpPath = path.join(tempDir, `${fileNameHash}_${path.basename(ranimPath, '.ranim')}.webp`);

        webviewPanel.webview.html = this.getLoadingHtml();

        try {
            const response = await sendGodotIpc({
                action: 'renderRanim',
                inputPath: ranimPath,
                outputPath: outputWebpPath
            });

            if (!response || !response.success || !fs.existsSync(outputWebpPath)) {
                throw new Error(response?.error || 'Godot IPC failed to render .ranim animation.');
            }

            const imageUri = webviewPanel.webview.asWebviewUri(vscode.Uri.file(outputWebpPath));
            webviewPanel.webview.html = this.getPreviewHtml(webviewPanel.webview, imageUri, path.basename(ranimPath));
        } catch (error: any) {
            webviewPanel.webview.html = this.getErrorHtml(error?.message || 'Failed to render .ranim animation.');
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
        <div>Rendering skeletal animation preview (.webp)...</div>
    </div>
</body>
</html>`;
    }

    private getPreviewHtml(webview: vscode.Webview, imageUri: vscode.Uri, title: string): string {
        return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src ${webview.cspSource} data:; style-src 'unsafe-inline';">
    <style>
        body { display: flex; flex-direction: column; align-items: center; justify-content: center; min-height: 100vh; margin: 0; padding: 20px; background-color: var(--vscode-editor-background); color: var(--vscode-editor-foreground); font-family: var(--vscode-font-family); box-sizing: border-box; }
        .header { margin-bottom: 16px; font-weight: 600; font-size: 14px; color: var(--vscode-descriptionForeground); }
        .image-container { display: flex; align-items: center; justify-content: center; padding: 12px; border: 1px solid var(--vscode-widget-border, #454545); border-radius: 6px; background-color: rgba(0, 0, 0, 0.2); max-width: 90vw; max-height: 80vh; overflow: auto; }
        img { max-width: 100%; max-height: 75vh; object-fit: contain; }
    </style>
</head>
<body>
    <div class="header">${title} (Animated WebP Preview)</div>
    <div class="image-container">
        <img src="${imageUri}" alt="Animated WebP Preview" />
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
        <h3>Failed to load .ranim preview</h3>
        <p>${errorMessage}</p>
    </div>
</body>
</html>`;
    }
}
