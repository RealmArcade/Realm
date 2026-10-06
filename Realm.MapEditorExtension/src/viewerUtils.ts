import * as path from 'path';
import * as fs from 'fs';
import * as os from 'os';
import * as crypto from 'crypto';

export function getPreviewTempDir(): string {
    const tempDir = path.join(os.tmpdir(), 'realm_extension_previews');
    if (!fs.existsSync(tempDir)) {
        fs.mkdirSync(tempDir, { recursive: true });
    }
    return tempDir;
}

export function getPreviewTempPath(sourceFsPath: string, newExt: string): string {
    const tempDir = getPreviewTempDir();
    const hash = crypto.createHash('md5').update(sourceFsPath).digest('hex').substring(0, 12);
    const baseName = path.basename(sourceFsPath, path.extname(sourceFsPath));
    const ext = newExt.startsWith('.') ? newExt : `.${newExt}`;
    return path.join(tempDir, `${hash}_${baseName}${ext}`);
}

export function formatFileSize(bytes: number): string {
    if (bytes >= 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(2) + ' MB';
    if (bytes >= 1024) return (bytes / 1024).toFixed(1) + ' KB';
    return bytes + ' B';
}

export function getLoadingHtml(loadingText: string): string {
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
        <div>${loadingText}</div>
    </div>
</body>
</html>`;
}

export function getErrorHtml(title: string, errorMessage: string): string {
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
        <h3>${title}</h3>
        <p>${errorMessage}</p>
    </div>
</body>
</html>`;
}
