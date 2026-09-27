import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import * as http from 'http';
import { RealmMapEditorProvider } from './editorProvider';
import { RealmRtexViewerProvider } from './rtexEditorProvider';
import { RealmRanimViewerProvider } from './ranimEditorProvider';
import { RealmRmeshViewerProvider, RmeshGlbFileSystemProvider, openRmeshInGlbViewer } from './rmeshEditorProvider';
import { RealmRaudViewerProvider } from './raudEditorProvider';
import { MIXAMO_EXPORT_LICENSING_WARNING } from './constants';

export function activate(context: vscode.ExtensionContext) {
    context.subscriptions.push(RealmMapEditorProvider.register(context));
    context.subscriptions.push(RealmRtexViewerProvider.register(context));
    context.subscriptions.push(RealmRanimViewerProvider.register(context));
    context.subscriptions.push(RealmRmeshViewerProvider.register(context));
    context.subscriptions.push(RealmRaudViewerProvider.register(context));

    context.subscriptions.push(
        vscode.workspace.registerFileSystemProvider(
            RmeshGlbFileSystemProvider.scheme,
            new RmeshGlbFileSystemProvider(),
            { isCaseSensitive: true, isReadonly: true }
        )
    );

    context.subscriptions.push(
        vscode.commands.registerCommand('realm.openRmesh3D', async (uri?: vscode.Uri) => {
            let targetPath = uri?.fsPath;
            if (!targetPath && vscode.window.activeTextEditor) {
                targetPath = vscode.window.activeTextEditor.document.uri.fsPath;
            }
            if (targetPath && targetPath.endsWith('.rmesh')) {
                await openRmeshInGlbViewer(targetPath);
            } else {
                vscode.window.showWarningMessage('Please select a .rmesh file to view in 3D.');
            }
        })
    );

    context.subscriptions.push(
        vscode.commands.registerCommand('realm.exportRanim', async (uri?: vscode.Uri) => {
            vscode.window.showWarningMessage(MIXAMO_EXPORT_LICENSING_WARNING);
        })
    );

    context.subscriptions.push(
        vscode.workspace.onWillSaveTextDocument(event => {
            const fileName = path.basename(event.document.fileName).toLowerCase();
            if (fileName === 'metadata.json' || fileName === 'manifest.json' || fileName === 'terrain.json') {
                event.waitUntil((async () => {
                    try {
                        const content = event.document.getText();
                        const response = await sendGodotIpc({
                            action: 'formatAndSaveJson',
                            filePath: event.document.uri.fsPath,
                            content: content
                        });

                        if (response && response.success && typeof response.formattedContent === 'string') {
                            const fullRange = new vscode.Range(
                                event.document.positionAt(0),
                                event.document.positionAt(content.length)
                            );
                            return [vscode.TextEdit.replace(fullRange, response.formattedContent)];
                        }
                    } catch (err) {
                        console.error('[RealmExtension] onWillSaveTextDocument IPC error:', err);
                    }
                    return [];
                })());
            }
        })
    );

    openStartupFiles(context);
    startGodotIpcListener(context);
}

export function sendGodotIpc(payload: any): Promise<any> {
    return new Promise((resolve, reject) => {
        const ports = [8092, 8093];
        const postData = JSON.stringify(payload);

        const attemptNext = (portIndex: number) => {
            if (portIndex >= ports.length) {
                return reject(new Error('Could not connect to Godot IPC bridge on ports 8092 or 8093'));
            }
            const port = ports[portIndex];
            const req = http.request({
                hostname: '127.0.0.1',
                port: port,
                path: '/api/',
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Content-Length': Buffer.byteLength(postData)
                }
            }, (res) => {
                let body = '';
                res.on('data', chunk => body += chunk);
                res.on('end', () => {
                    try {
                        resolve(JSON.parse(body));
                    } catch {
                        resolve({ success: res.statusCode === 200, raw: body });
                    }
                });
            });

            req.on('error', () => {
                attemptNext(portIndex + 1);
            });

            req.setTimeout(3000, () => {
                try { req.destroy(); } catch {}
                attemptNext(portIndex + 1);
            });

            req.write(postData);
            req.end();
        };

        attemptNext(0);
    });
}

function startGodotIpcListener(context: vscode.ExtensionContext): void {
    const ports = [8092, 8093];
    const pollInterval = setInterval(async () => {
        for (const port of ports) {
            try {
                const data = await httpGetJson(`http://127.0.0.1:${port}/api/poll`);
                if (data && Array.isArray(data.commands)) {
                    for (const cmd of data.commands) {
                        if (cmd === 'saveAll') {
                            await handleSaveAllCommand();
                        }
                    }
                }
            } catch {
            }
        }
    }, 300);

    context.subscriptions.push({
        dispose: () => clearInterval(pollInterval)
    });
}

async function handleSaveAllCommand(): Promise<void> {
    try {
        for (const doc of vscode.workspace.textDocuments) {
            if (doc.isDirty) {
                await doc.save();
            }
        }
        await vscode.commands.executeCommand('workbench.action.files.saveAll');
    } catch (err) {
        console.error('[RealmExtension] Error executing saveAll:', err);
    }
}

function httpGetJson(urlStr: string): Promise<any> {
    return new Promise((resolve, reject) => {
        const req = http.get(urlStr, (res) => {
            if (res.statusCode !== 200) {
                return reject(new Error(`Status ${res.statusCode}`));
            }
            let body = '';
            res.on('data', chunk => body += chunk);
            res.on('end', () => {
                try {
                    resolve(JSON.parse(body));
                } catch (e) {
                    reject(e);
                }
            });
        });
        req.on('error', reject);
        req.setTimeout(800, () => {
            req.destroy();
            reject(new Error('Timeout'));
        });
    });
}

async function openStartupFiles(context: vscode.ExtensionContext): Promise<void> {
    await delay(2000);

    const folders = vscode.workspace.workspaceFolders;
    if (!folders || folders.length === 0) { return; }

    const workspaceDir = folders[0].uri.fsPath;
    const scriptPath = path.join(workspaceDir, 'MapScript.cs');
    const metadataPath = path.join(workspaceDir, 'metadata.json');

    if (!fs.existsSync(scriptPath) || !fs.existsSync(metadataPath)) { return; }

    try {
        const metadataUri = vscode.Uri.file(metadataPath);
        await vscode.commands.executeCommand('vscode.open', metadataUri, { preview: false, preserveFocus: true });

        const scriptUri = vscode.Uri.file(scriptPath);
        const scriptDoc = await vscode.workspace.openTextDocument(scriptUri);
        await vscode.window.showTextDocument(scriptDoc, { preview: false, preserveFocus: false });
        await vscode.commands.executeCommand('workbench.action.revertFile');
    } catch (err) {
    }
}

function delay(ms: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, ms));
}

export function deactivate() {}
