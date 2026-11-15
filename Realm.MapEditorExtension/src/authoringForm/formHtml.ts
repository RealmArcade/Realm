import * as vscode from 'vscode';

export function buildFormHtml(webview: vscode.Webview, extensionUri: vscode.Uri): string {
    const bundleRoot = vscode.Uri.joinPath(extensionUri, 'dist');
    const scriptUri = webview.asWebviewUri(vscode.Uri.joinPath(bundleRoot, 'form.js'));
    const styleUri = webview.asWebviewUri(vscode.Uri.joinPath(bundleRoot, 'form.css'));
    const nonce = createNonce();

    return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src ${webview.cspSource} data:; style-src ${webview.cspSource}; script-src 'nonce-${nonce}';">
<link rel="stylesheet" href="${styleUri}">
<title>Metadata Form</title>
</head>
<body>
<div id="app" class="app">
  <div class="loading">Loading the metadata form...</div>
</div>
<script nonce="${nonce}" src="${scriptUri}"></script>
</body>
</html>`;
}

function createNonce(): string {
    const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
    let nonce = '';
    for (let index = 0; index < 32; index++) {
        nonce += alphabet.charAt(Math.floor(Math.random() * alphabet.length));
    }
    return nonce;
}
