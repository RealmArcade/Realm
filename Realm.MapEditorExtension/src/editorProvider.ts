import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import { sendGodotIpc } from './extension';

export class RealmMapEditorProvider implements vscode.CustomTextEditorProvider {
    public static register(context: vscode.ExtensionContext): vscode.Disposable {
        const provider = new RealmMapEditorProvider(context);
        return vscode.window.registerCustomEditorProvider(RealmMapEditorProvider.viewType, provider);
    }

    private static readonly viewType = 'realm.mapEditor';

    constructor(
        private readonly context: vscode.ExtensionContext
    ) {}

    public async resolveCustomTextEditor(
        document: vscode.TextDocument,
        webviewPanel: vscode.WebviewPanel,
        _token: vscode.CancellationToken
    ): Promise<void> {
        webviewPanel.webview.options = {
            enableScripts: true
        };
        webviewPanel.webview.html = this.getHtmlForWebview(webviewPanel.webview);

        const changeDocumentSubscription = vscode.workspace.onDidChangeTextDocument(e => {
            if (e.document.uri.toString() === document.uri.toString()) {
                webviewPanel.webview.postMessage({
                    type: 'update',
                    text: this.getMergedDocumentText(document)
                });
            }
        });

        const manifestWatcher = vscode.workspace.createFileSystemWatcher(
            new vscode.RelativePattern(path.dirname(document.uri.fsPath), 'manifest.json')
        );
        const onManifestChange = () => {
            webviewPanel.webview.postMessage({
                type: 'update',
                text: this.getMergedDocumentText(document)
            });
        };
        manifestWatcher.onDidChange(onManifestChange);
        manifestWatcher.onDidCreate(onManifestChange);
        manifestWatcher.onDidDelete(onManifestChange);

        webviewPanel.onDidDispose(() => {
            changeDocumentSubscription.dispose();
            manifestWatcher.dispose();
        });

        webviewPanel.webview.onDidReceiveMessage(async e => {
            switch (e.type) {
                case 'ready':
                    webviewPanel.webview.postMessage({
                        type: 'update',
                        text: this.getMergedDocumentText(document)
                    });
                    break;
                case 'change':
                    const applied = await this.updateTextDocument(document, e.text);
                    if (!applied) {
                        console.warn('WorkspaceEdit was not applied, resyncing webview');
                        webviewPanel.webview.postMessage({
                            type: 'update',
                            text: this.getMergedDocumentText(document)
                        });
                    }
                    break;
                case 'browseFile':
                    await this.handleBrowseFile(webviewPanel.webview, e.fieldId, e.fieldClass, e.fieldIndex, e.fileTypes, e.assetType);
                    break;
                case 'resolvePath':
                    const absPath = this.resolveGodotPath(e.path, document.uri);
                    const webviewUri = absPath ? webviewPanel.webview.asWebviewUri(vscode.Uri.file(absPath)).toString() : '';
                    webviewPanel.webview.postMessage({
                        type: 'resolvePathResult',
                        requestId: e.requestId,
                        uri: webviewUri
                    });
                    break;
                case 'pruneDomain':
                    await this.handlePruneDomain(webviewPanel.webview, e.domain, document);
                    break;
            }
        });
    }

    private async handlePruneDomain(webview: vscode.Webview, domain: string, document: vscode.TextDocument) {
        try {
            let metadata: any;
            try {
                metadata = JSON.parse(document.getText());
            } catch (err) {
                vscode.window.showErrorMessage(`Cannot prune ${domain}: metadata.json is invalid JSON.`);
                return;
            }

            const targetDir = path.dirname(document.uri.fsPath);
            const terrainPath = path.join(targetDir, 'terrain.json');
            if (!fs.existsSync(terrainPath)) {
                vscode.window.showWarningMessage(`Cannot prune ${domain}: terrain.json not found in map folder.`);
                return;
            }

            let terrainData: any;
            try {
                terrainData = JSON.parse(fs.readFileSync(terrainPath, 'utf8'));
            } catch (err: any) {
                vscode.window.showErrorMessage(`Cannot prune ${domain}: failed to parse terrain.json: ${err.message}`);
                return;
            }

            const placedIds = new Set<string>();

            function addIdentifier(val: any) {
                if (!val || typeof val !== 'string') return;
                const trimmed = val.trim().toLowerCase();
                if (!trimmed) return;
                placedIds.add(trimmed);
                const normalized = trimmed.replace(/\\/g, '/');
                placedIds.add(normalized);
                const clean = normalized.replace(/^(res:\/\/|user:\/\/|assets\/)/i, '');
                placedIds.add(clean);
                const baseName = path.basename(normalized);
                placedIds.add(baseName);
                const withoutExt = baseName.replace(/\.[^/.]+$/, '');
                if (withoutExt) placedIds.add(withoutExt);
            }

            function scanCsScripts(dir: string) {
                if (!fs.existsSync(dir)) return;
                try {
                    const entries = fs.readdirSync(dir, { withFileTypes: true });
                    for (const entry of entries) {
                        if (entry.isDirectory()) {
                            if (['bin', 'obj', '.godot', '.git', 'lib', 'node_modules'].includes(entry.name.toLowerCase())) {
                                continue;
                            }
                            scanCsScripts(path.join(dir, entry.name));
                        } else if (entry.isFile() && entry.name.toLowerCase().endsWith('.cs')) {
                            try {
                                const content = fs.readFileSync(path.join(dir, entry.name), 'utf8');
                                const strRegex = /"([^"\\]*(?:\\.[^"\\]*)*)"/g;
                                let match;
                                while ((match = strRegex.exec(content)) !== null) {
                                    const val = match[1];
                                    if (val && val.length < 200) {
                                        addIdentifier(val);
                                    }
                                }
                            } catch {
                            }
                        }
                    }
                } catch {
                }
            }

            // 1. Gather all placed IDs from terrain.json (Units, Props, Decals)
            if (Array.isArray(terrainData.Units)) {
                terrainData.Units.forEach((u: any) => {
                    if (u) {
                        addIdentifier(u.UnitId);
                        addIdentifier(u.Name);
                        addIdentifier(u.ModelPath);
                    }
                });
            }
            if (Array.isArray(terrainData.Props)) {
                terrainData.Props.forEach((p: any) => {
                    if (p) {
                        addIdentifier(p.PropId);
                        addIdentifier(p.Name);
                        addIdentifier(p.ModelPath);
                    }
                });
            }
            if (Array.isArray(terrainData.Decals)) {
                terrainData.Decals.forEach((d: any) => {
                    if (d) {
                        addIdentifier(d.DecalId);
                        addIdentifier(d.Name);
                    }
                });
            }

            // 2. Gather all string literals from C# scripts in the map workspace
            scanCsScripts(targetDir);

            // 3. Helper to test if an entity or item matches any placed identifier
            function isEntityReferenced(item: any): boolean {
                if (!item || typeof item !== 'object') return false;
                const candidates = [
                    item.ObjectID,
                    item.UnitId,
                    item.PropId,
                    item.WeaponId,
                    item.AbilityId,
                    item.UpgradeId,
                    item.ItemId,
                    item.Name,
                    item.ModelPath,
                    item.DropModelPath,
                    item.PortraitModelPath,
                    item.MissileModelPath,
                    item.ProjectileModelPath,
                    item.ProjectileModel,
                    item.EffectModel
                ];
                for (const c of candidates) {
                    if (c && typeof c === 'string') {
                        const trimmed = c.trim().toLowerCase();
                        if (placedIds.has(trimmed)) return true;
                        const normalized = trimmed.replace(/\\/g, '/');
                        if (placedIds.has(normalized)) return true;
                        const clean = normalized.replace(/^(res:\/\/|user:\/\/|assets\/)/i, '');
                        if (placedIds.has(clean)) return true;
                        const baseName = path.basename(normalized);
                        if (placedIds.has(baseName)) return true;
                        const withoutExt = baseName.replace(/\.[^/.]+$/, '');
                        if (withoutExt && placedIds.has(withoutExt)) return true;
                    }
                }
                return false;
            }

            // 4. Transitive expansion: add build options, weapons, abilities, upgrades, items of referenced entities
            let expanded = true;
            let loopCount = 0;
            while (expanded && loopCount < 50) {
                expanded = false;
                loopCount++;
                const prevSize = placedIds.size;

                const allEntities = [
                    ...(metadata.CustomUnits || []),
                    ...(metadata.CustomBuildings || []),
                    ...(metadata.CustomResources || []),
                    ...(metadata.CustomProps || [])
                ];

                for (const entity of allEntities) {
                    if (isEntityReferenced(entity)) {
                        addIdentifier(entity.ObjectID || entity.UnitId);
                        addIdentifier(entity.Name);
                        addIdentifier(entity.ModelPath);

                        if (Array.isArray(entity.BuildOptions)) {
                            entity.BuildOptions.forEach((opt: string) => addIdentifier(opt));
                        }
                        if (Array.isArray(entity.Weapons)) {
                            entity.Weapons.forEach((w: string) => addIdentifier(w));
                        }
                        if (Array.isArray(entity.Abilities)) {
                            entity.Abilities.forEach((a: string) => addIdentifier(a));
                        }
                        if (Array.isArray(entity.Upgrades)) {
                            entity.Upgrades.forEach((u: string) => addIdentifier(u));
                        }
                        if (Array.isArray(entity.StartingItems)) {
                            entity.StartingItems.forEach((i: string) => addIdentifier(i));
                        }
                        if (Array.isArray(entity.Items)) {
                            entity.Items.forEach((i: string) => addIdentifier(i));
                        }
                    }
                }

                if (Array.isArray(metadata.CustomAbilities)) {
                    for (const abi of metadata.CustomAbilities) {
                        if (isEntityReferenced(abi)) {
                            addIdentifier(abi.ObjectID || abi.AbilityId);
                            addIdentifier(abi.Name);
                            if (abi.SummonedUnitId) addIdentifier(abi.SummonedUnitId);
                            if (Array.isArray(abi.GrantedWeapons)) abi.GrantedWeapons.forEach((w: string) => addIdentifier(w));
                        }
                    }
                }

                if (Array.isArray(metadata.CustomUpgrades)) {
                    for (const up of metadata.CustomUpgrades) {
                        if (isEntityReferenced(up)) {
                            addIdentifier(up.ObjectID || up.UpgradeId);
                            addIdentifier(up.Name);
                            if (Array.isArray(up.GrantedWeapons)) up.GrantedWeapons.forEach((w: string) => addIdentifier(w));
                            if (Array.isArray(up.AffectedUnitIds)) up.AffectedUnitIds.forEach((uid: string) => addIdentifier(uid));
                        }
                    }
                }

                if (Array.isArray(metadata.CustomItems)) {
                    for (const itm of metadata.CustomItems) {
                        if (isEntityReferenced(itm)) {
                            addIdentifier(itm.ObjectID || itm.ItemId);
                            addIdentifier(itm.Name);
                            if (Array.isArray(itm.Abilities)) itm.Abilities.forEach((a: string) => addIdentifier(a));
                            if (Array.isArray(itm.GrantedWeapons)) itm.GrantedWeapons.forEach((w: string) => addIdentifier(w));
                        }
                    }
                }

                if (placedIds.size > prevSize) {
                    expanded = true;
                }
            }

            // 5. Filter target domain
            let initialCount = 0;
            let finalCount = 0;

            if (domain === 'units') {
                initialCount = (metadata.CustomUnits || []).length;
                metadata.CustomUnits = (metadata.CustomUnits || []).filter((u: any) => isEntityReferenced(u));
                finalCount = metadata.CustomUnits.length;
            } else if (domain === 'buildings') {
                initialCount = (metadata.CustomBuildings || []).length;
                metadata.CustomBuildings = (metadata.CustomBuildings || []).filter((b: any) => isEntityReferenced(b));
                finalCount = metadata.CustomBuildings.length;
            } else if (domain === 'resources') {
                initialCount = (metadata.CustomResources || []).length;
                metadata.CustomResources = (metadata.CustomResources || []).filter((r: any) => isEntityReferenced(r));
                finalCount = metadata.CustomResources.length;
            } else if (domain === 'props') {
                initialCount = (metadata.CustomProps || []).length;
                metadata.CustomProps = (metadata.CustomProps || []).filter((p: any) => isEntityReferenced(p));
                finalCount = metadata.CustomProps.length;
            } else if (domain === 'weapons') {
                initialCount = (metadata.CustomWeapons || []).length;
                metadata.CustomWeapons = (metadata.CustomWeapons || []).filter((w: any) => isEntityReferenced(w));
                finalCount = metadata.CustomWeapons.length;
            } else if (domain === 'abilities') {
                initialCount = (metadata.CustomAbilities || []).length;
                metadata.CustomAbilities = (metadata.CustomAbilities || []).filter((a: any) => isEntityReferenced(a));
                finalCount = metadata.CustomAbilities.length;
            } else if (domain === 'upgrades') {
                initialCount = (metadata.CustomUpgrades || []).length;
                metadata.CustomUpgrades = (metadata.CustomUpgrades || []).filter((u: any) => isEntityReferenced(u));
                finalCount = metadata.CustomUpgrades.length;
            } else if (domain === 'items') {
                initialCount = (metadata.CustomItems || []).length;
                metadata.CustomItems = (metadata.CustomItems || []).filter((i: any) => isEntityReferenced(i));
                finalCount = metadata.CustomItems.length;
            }

            const removedCount = initialCount - finalCount;
            await this.saveMetadataViaGodotIpc(document, metadata, webview);

            if (removedCount > 0) {
                vscode.window.showInformationMessage(`Pruned ${removedCount} unplaced item(s) from ${domain}.`);
            } else {
                vscode.window.showInformationMessage(`No unplaced items found in ${domain}. All items are placed or referenced on terrain.`);
            }
        } catch (err: any) {
            vscode.window.showErrorMessage(`Failed to prune ${domain}: ${err.message}`);
        }
    }

    private async handleBrowseFile(
        webview: vscode.Webview,
        fieldId: string | null,
        fieldClass: string | null,
        fieldIndex: number | null,
        fileTypes?: string[],
        assetType?: string
    ) {
        webview.postMessage({
            type: 'browseFileFallback',
            fieldId,
            fieldClass,
            fieldIndex,
            assetType,
            accept: fileTypes ? fileTypes.map(ext => '.' + ext.replace(/^\./, '')).join(',') : '*'
        });
    }

    private resolveGodotPath(godotPath: string, documentUri: vscode.Uri): string | null {
        if (!godotPath) {
            return null;
        }
        
        let cleanPath = godotPath.trim();
        if (cleanPath.startsWith('res://')) {
            cleanPath = cleanPath.substring(6);
        }

        const candidateSubDirs = [
            '',
            path.join('Assets', 'models', 'units'),
            path.join('Assets', 'models', 'buildings'),
            path.join('Assets', 'models', 'resources'),
            path.join('Assets', 'models', 'props'),
            path.join('Assets', 'decals'),
            path.join('Assets', 'icons'),
            path.join('Assets', 'textures'),
            path.join('Assets', 'skyboxes'),
            path.join('Assets', 'vfx'),
            path.join('Assets', 'audio', 'sfx')
        ];

        const docDir = path.dirname(documentUri.fsPath);
        const searchRoots: string[] = [];

        let currentDir = docDir;
        while (true) {
            searchRoots.push(currentDir);
            const projectFile = path.join(currentDir, 'project.godot');
            if (fs.existsSync(projectFile)) {
                break;
            }
            const parent = path.dirname(currentDir);
            if (parent === currentDir) {
                break;
            }
            currentDir = parent;
        }

        const workspaceFolders = vscode.workspace.workspaceFolders;
        if (workspaceFolders) {
            for (const folder of workspaceFolders) {
                searchRoots.push(folder.uri.fsPath);
                searchRoots.push(path.join(folder.uri.fsPath, 'Realm.Godot'));
            }
        }

        for (const root of searchRoots) {
            for (const subDir of candidateSubDirs) {
                const fullCandidate = subDir ? path.join(root, subDir, cleanPath) : path.join(root, cleanPath);
                if (fs.existsSync(fullCandidate)) {
                    return fullCandidate;
                }
            }
        }
        
        return null;
    }

    private async updateTextDocument(document: vscode.TextDocument, text: string): Promise<boolean> {
        const edit = new vscode.WorkspaceEdit();
        const fullRange = new vscode.Range(
            document.positionAt(0),
            document.positionAt(document.getText().length)
        );
        edit.replace(
            document.uri,
            fullRange,
            text
        );
        try {
            const applied = await vscode.workspace.applyEdit(edit);
            if (!applied) {
                console.error('updateTextDocument: applyEdit returned false');
            }
            return applied;
        } catch (err) {
            console.error('updateTextDocument error:', err);
            return false;
        }
    }

    private async saveMetadataViaGodotIpc(document: vscode.TextDocument, metadata: any, webview?: vscode.Webview): Promise<void> {
        const rawJson = typeof metadata === 'string' ? metadata : JSON.stringify(metadata);
        try {
            const response = await sendGodotIpc({
                action: 'formatAndSaveJson',
                filePath: document.uri.fsPath,
                content: rawJson
            });

            if (response && response.success && typeof response.formattedContent === 'string') {
                await this.updateTextDocument(document, response.formattedContent);
                if (webview) {
                    webview.postMessage({
                        type: 'update',
                        text: response.formattedContent
                    });
                }
                return;
            }
        } catch (err) {
            console.error('[RealmExtension] saveMetadataViaGodotIpc failed, falling back:', err);
        }

        const fallbackText = typeof metadata === 'string' ? metadata : JSON.stringify(metadata, null, 2);
        await this.updateTextDocument(document, fallbackText);
        await document.save();
        this.notifyGodotReloadMetadata();
        if (webview) {
            webview.postMessage({
                type: 'update',
                text: fallbackText
            });
        }
    }

    private notifyGodotReloadMetadata(): void {
        sendGodotIpc({ action: 'reloadMetadata' }).catch(() => {});
    }

    private getHtmlForWebview(webview: vscode.Webview): string {
        let detectedLocale = 'en';
        let activeDict: Record<string, string> = {};
        let enDict: Record<string, string> = {};

        try {
            const appData = process.env.APPDATA || path.join(require('os').homedir(), 'AppData', 'Roaming');
            const userDir = path.join(appData, 'Godot', 'app_userdata', 'Realm');
            const locFile = path.join(userDir, 'realm-locale.txt');
            if (fs.existsSync(locFile)) {
                detectedLocale = fs.readFileSync(locFile, 'utf8').trim() || 'en';
            }
            const dictFile = path.join(userDir, 'realm-locale-dict.json');
            if (fs.existsSync(dictFile)) {
                activeDict = JSON.parse(fs.readFileSync(dictFile, 'utf8'));
            }
            const enFile = path.join(userDir, 'realm-locale-en.json');
            if (fs.existsSync(enFile)) {
                enDict = JSON.parse(fs.readFileSync(enFile, 'utf8'));
            }
        } catch {
        }

        if (Object.keys(activeDict).length === 0 || Object.keys(enDict).length === 0) {
            const searchDirs: string[] = [
                path.join(this.context.extensionPath, '..', 'Realm.Godot', 'locale'),
                path.join(this.context.extensionPath, 'locale'),
                path.join(process.cwd(), 'Realm.Godot', 'locale'),
            ];
            if (vscode.workspace.workspaceFolders) {
                for (const wf of vscode.workspace.workspaceFolders) {
                    searchDirs.push(path.join(wf.uri.fsPath, 'Realm.Godot', 'locale'));
                    searchDirs.push(path.join(wf.uri.fsPath, '..', 'Realm.Godot', 'locale'));
                }
            }

            for (const dir of searchDirs) {
                if (fs.existsSync(dir)) {
                    try {
                        const enFile = path.join(dir, 'en.json');
                        if (fs.existsSync(enFile) && Object.keys(enDict).length === 0) {
                            enDict = JSON.parse(fs.readFileSync(enFile, 'utf8'));
                        }
                        const activeFile = path.join(dir, `${detectedLocale}.json`);
                        if (fs.existsSync(activeFile) && Object.keys(activeDict).length === 0) {
                            activeDict = JSON.parse(fs.readFileSync(activeFile, 'utf8'));
                        }
                    } catch {}
                }
                if (Object.keys(enDict).length > 0 && Object.keys(activeDict).length > 0) {
                    break;
                }
            }
        }

        const nonce = this.getNonce();

        let scriptContent = '';
        let styleContent = '';
        try {
            scriptContent = fs.readFileSync(path.join(this.context.extensionPath, 'media', 'editor.js'), 'utf8');
        } catch {}
        try {
            styleContent = fs.readFileSync(path.join(this.context.extensionPath, 'media', 'editor.css'), 'utf8');
        } catch {}

        return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; connect-src http://127.0.0.1:* http://localhost:*; img-src ${webview.cspSource} data: https:; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <style>${styleContent}</style>
    <title>Realm Map Editor</title>
</head>
<body>
    <div class="app-container">
        <div class="global-header">
            <div class="app-title-group">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="color: var(--accent);"><path d="M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5"/></svg>
                <h1>Realm Map Editor</h1>
            </div>
            <div class="global-tabs">
                <button type="button" class="tab-btn active" data-domain="units">👥 Units</button>
                <button type="button" class="tab-btn" data-domain="buildings">🏢 Buildings</button>
                <button type="button" class="tab-btn" data-domain="resources">🪵 Resources</button>
                <button type="button" class="tab-btn" data-domain="props">📦 Props</button>
                <button type="button" class="tab-btn" data-domain="weapons">⚔️ Weapons</button>
                <button type="button" class="tab-btn" data-domain="abilities">🪄 Abilities</button>
                <button type="button" class="tab-btn" data-domain="upgrades">🛡️ Upgrades</button>
                <button type="button" class="tab-btn" data-domain="items">📦 Items</button>
                <button type="button" class="tab-btn" data-domain="properties">⚙️ Settings</button>
            </div>
            <div class="header-right-actions">
                <div id="save-status" class="save-status saved" title="Auto-saved to file">● Saved</div>
                <button type="button" id="toggle-lock-btn" class="btn secondary-btn small-btn" title="Lock Editor (Read-Only Mode)">🔓 Lock</button>
                <button type="button" id="toggle-buttons-btn" class="btn secondary-btn small-btn" title="Toggle Add/Delete Controls">➕ Edit Ops</button>
                <button type="button" id="toggle-debug-btn" class="btn secondary-btn small-btn" title="Toggle Debug JSON View">🐞 Debug</button>
                <span style="font-size: 11px; color: var(--text-muted); opacity: 0.8; margin-left: 4px;" title="Press Ctrl+Shift+I inside editor to open Chromium DevTools console for debugging">💡 Ctrl+Shift+I DevTools</span>
            </div>
        </div>
        <div class="editor-body">
            <div class="sidebar">
                <div class="sidebar-subheader" style="padding-top: 16px;">
                    <h2>Units List</h2>
                    <div class="add-buttons-group" style="display: flex; gap: 4px;">
                        <button id="add-unit-btn" class="btn primary-btn" style="padding: 4px 8px;" title="Add Unit">+ Add</button>
                        <button type="button" id="prune-entities-btn" class="btn secondary-btn" style="padding: 4px 8px;" title="Prune items never placed on terrain.json">✂️ Prune Unused</button>
                    </div>
                </div>
                <div class="search-container">
                    <input type="text" id="search-input" placeholder="Search units..." />
                </div>
                <div id="unit-list" class="unit-list"></div>
            </div>
            <div class="main-content">
                <div id="empty-state" class="empty-state">
                    <div class="empty-state-content">
                        <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                            <path d="M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5" />
                        </svg>
                        <h3>Select a unit to edit</h3>
                        <p>Or configure Map Properties in the global tabs.</p>
                    </div>
                </div>
            <div id="editor-form" class="editor-form hidden">
                <div class="form-header">
                    <div style="display: flex; justify-content: space-between; align-items: center; width: 100%;">
                        <div>
                            <div class="breadcrumb" id="editor-breadcrumb">Units</div>
                            <h2 id="editor-title">Edit Entity</h2>
                            <span id="editor-subtitle" class="subtitle">ID</span>
                        </div>
                        <div class="header-actions" style="display: flex; gap: 6px;">
                            <button type="button" id="edit-animations-btn" class="btn secondary-btn" title="Open Unit Animation Studio in Godot">🎬 Edit Animations</button>
                            <button type="button" id="copy-unit-btn" class="btn secondary-btn" title="Copy entity to clipboard">✂️ Copy</button>
                            <button type="button" id="paste-unit-btn" class="btn secondary-btn" title="Paste entity from clipboard">📋 Paste</button>
                            <button type="button" id="duplicate-unit-btn" class="btn secondary-btn">📋 Duplicate</button>
                            <button type="button" id="delete-unit-btn" class="btn secondary-btn" title="Delete entity">🗑️ Delete</button>
                        </div>
                    </div>
                </div>
                <div class="form-scroll-container">
                    <div class="form-section">
                        <h3>General Information</h3>
                        <div class="form-group">
                            <label for="field-ObjectID-slug">ObjectID</label>
                            <div style="display: flex; align-items: center; gap: 4px;">
                                <span id="field-object-type-prefix" style="color: var(--vscode-descriptionForeground, #888888); font-family: var(--vscode-editor-font-family, monospace); font-size: 13px; font-weight: bold;">unit/</span>
                                <input type="text" id="field-ObjectID-slug" style="flex: 1;" required />
                            </div>
                        </div>
                        <div class="form-group">
                            <label for="field-Name">Name</label>
                            <input type="text" id="field-Name" required />
                        </div>
                        <div class="form-group">
                            <label for="field-Description">Description</label>
                            <textarea id="field-Description" rows="3" required></textarea>
                        </div>
                        <div class="form-group">
                            <label>Model Asset (rmesh)</label>
                            <div class="input-with-browse" style="display: flex; gap: 6px; width: 100%; align-items: center;">
                                <span id="field-ModelPath" class="readonly-model-label" style="flex: 1; min-height: 28px; padding: 4px 8px; background: var(--vscode-input-background, #1e1e1e); border: 1px solid var(--vscode-input-border, #3c3c3c); border-radius: 2px; color: var(--vscode-input-foreground, #cccccc); display: flex; align-items: center; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; user-select: text; font-family: var(--vscode-editor-font-family, monospace); font-size: 12px;">(None)</span>
                                <button type="button" class="btn edit-model-btn" data-field="ModelPath" title="Edit Model Asset in Godot">✏️</button>
                            </div>
                        </div>
                        <div class="form-group">
                            <label>Portrait Model Path (Optional)</label>
                            <div class="input-with-browse" style="display: flex; gap: 6px; width: 100%; align-items: center;">
                                <span id="field-PortraitModelPath" class="readonly-model-label" style="flex: 1; min-height: 28px; padding: 4px 8px; background: var(--vscode-input-background, #1e1e1e); border: 1px solid var(--vscode-input-border, #3c3c3c); border-radius: 2px; color: var(--vscode-input-foreground, #cccccc); display: flex; align-items: center; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; user-select: text; font-family: var(--vscode-editor-font-family, monospace); font-size: 12px;">(None)</span>
                                <button type="button" class="btn edit-model-btn" data-field="PortraitModelPath" title="Edit Portrait Model in Godot">✏️</button>
                            </div>
                        </div>
                        <div class="form-group checkbox-group">
                            <input type="checkbox" id="field-IsHero" />
                            <label for="field-IsHero">Is Hero</label>
                        </div>
                    </div>

                    <div id="section-unit-animations" class="form-section">
                        <h3>Unit Animations</h3>
                        <div style="display: flex; align-items: center; justify-content: space-between; background: var(--vscode-input-background, #1e1e1e); border: 1px solid var(--vscode-input-border, #3c3c3c); border-radius: 4px; padding: 10px 14px;">
                            <div>
                                <span style="font-weight: 600; font-size: 13px;">Rigged Animations (.ranim)</span>
                                <p style="margin: 3px 0 0 0; font-size: 12px; opacity: 0.75;">Live preview and configure Idle, Walk, Attack, Death, and Spell casting animations in Godot.</p>
                            </div>
                            <button type="button" id="edit-animations-body-btn" class="btn secondary-btn" style="white-space: nowrap;" title="Open Unit Animation Studio in Godot">🎬 Edit Animations</button>
                        </div>
                    </div>

                    <div id="section-resource-node-config" class="form-section">
                        <h3>Resource Deposit Settings</h3>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-MaxCapacity">Max Resource Capacity</label>
                                <input type="number" id="field-MaxCapacity" min="0" step="any" placeholder="2000" />
                            </div>
                            <div class="form-group">
                                <label for="field-HarvestRate">Harvest Yield / Cycle</label>
                                <input type="number" id="field-HarvestRate" min="0" step="any" placeholder="10" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-GrowthRate">Regen / Growth Rate (Units/sec)</label>
                                <input type="number" id="field-GrowthRate" min="0" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-MaxWorkers">Max Simultaneous Harvesters</label>
                                <input type="number" id="field-MaxWorkers" min="1" step="1" placeholder="5" />
                            </div>
                        </div>
                    </div>

                    <div id="section-unit-stats" class="form-section">
                        <h3>Attributes & Stats</h3>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-Vitality">Vitality (VIT)</label>
                                <input type="number" id="field-Vitality" min="0" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-Might">Might (MIG)</label>
                                <input type="number" id="field-Might" min="0" step="any" placeholder="0.0" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-Agility">Agility (AGI)</label>
                                <input type="number" id="field-Agility" min="0" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-Finesse">Finesse (FIN)</label>
                                <input type="number" id="field-Finesse" min="0" step="any" placeholder="0.0" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-Focus">Focus (FOC)</label>
                                <input type="number" id="field-Focus" min="0" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-Willpower">Willpower (WIL)</label>
                                <input type="number" id="field-Willpower" min="0" step="any" placeholder="0.0" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-MaxHp">Max HP</label>
                                <input type="number" id="field-MaxHp" min="0" step="any" required />
                            </div>
                            <div class="form-group">
                                <label for="field-Damage">Damage</label>
                                <input type="number" id="field-Damage" min="0" step="any" required />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-Range">Range</label>
                                <input type="number" id="field-Range" min="0" step="any" required />
                            </div>
                            <div class="form-group">
                                <label for="field-Armor">Armor</label>
                                <input type="number" id="field-Armor" min="0" step="any" required />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-Speed">Speed</label>
                                <input type="number" id="field-Speed" min="0" step="any" required />
                            </div>
                            <div class="form-group">
                                <label for="field-AttackCooldown">Attack Cooldown</label>
                                <input type="number" id="field-AttackCooldown" min="0" step="any" required />
                            </div>
                        </div>
                        <div class="form-group">
                            <label for="field-ScanRadius">Scan Radius</label>
                            <input type="number" id="field-ScanRadius" min="0" step="any" required />
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-HpRegen">HP Regen / sec</label>
                                <input type="number" id="field-HpRegen" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-HpRegenCombatDelay">HP Regen Combat Delay (sec)</label>
                                <input type="number" id="field-HpRegenCombatDelay" min="0" step="any" placeholder="0.0" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-MaxMana">Max Mana</label>
                                <input type="number" id="field-MaxMana" min="0" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-ManaRegen">Mana Regen / sec</label>
                                <input type="number" id="field-ManaRegen" step="any" placeholder="0.0" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-RatedArmor">Rated Armor (EHP Scaling)</label>
                                <input type="number" id="field-RatedArmor" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-ArmorType">Armor Type Tag</label>
                                <input type="text" id="field-ArmorType" placeholder="unarmored" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-FlatArmorPenetration">Flat Armor Pen</label>
                                <input type="number" id="field-FlatArmorPenetration" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-PercentArmorPenetration">Percent Armor Pen (0.0 - 1.0)</label>
                                <input type="number" id="field-PercentArmorPenetration" min="0" max="1" step="any" placeholder="0.0" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-DamageVariance">Damage Variance (±)</label>
                                <input type="number" id="field-DamageVariance" min="0" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-DamageType">Damage Type Tag</label>
                                <input type="text" id="field-DamageType" placeholder="normal" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-CritChance">Crit Chance (0.0 - 1.0)</label>
                                <input type="number" id="field-CritChance" min="0" max="1" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-CritMultiplier">Crit Multiplier</label>
                                <input type="number" id="field-CritMultiplier" min="0" step="any" placeholder="1.0" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-SplashType">Splash Falloff Type</label>
                                <select id="field-SplashType">
                                    <option value="None">None</option>
                                    <option value="RadialStep">RadialStep</option>
                                    <option value="RadialLinear">RadialLinear</option>
                                </select>
                            </div>
                            <div class="form-group checkbox-group">
                                <label for="field-FriendlyFire">
                                    <input type="checkbox" id="field-FriendlyFire" />
                                    Friendly Fire Splash
                                </label>
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-SplashInnerRadius">Splash Inner Radius</label>
                                <input type="number" id="field-SplashInnerRadius" min="0" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-SplashInnerRatio">Splash Inner Ratio</label>
                                <input type="number" id="field-SplashInnerRatio" min="0" step="any" placeholder="1.0" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-SplashMediumRadius">Splash Medium Radius</label>
                                <input type="number" id="field-SplashMediumRadius" min="0" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-SplashMediumRatio">Splash Medium Ratio</label>
                                <input type="number" id="field-SplashMediumRatio" min="0" step="any" placeholder="0.5" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-SplashOuterRadius">Splash Outer Radius</label>
                                <input type="number" id="field-SplashOuterRadius" min="0" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-SplashOuterRatio">Splash Outer Ratio</label>
                                <input type="number" id="field-SplashOuterRatio" min="0" step="any" placeholder="0.25" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-PushPriority">Push Priority</label>
                                <input type="number" id="field-PushPriority" step="1" placeholder="0" />
                            </div>
                            <div class="form-group">
                                <label for="field-MovementType">Movement Type</label>
                                <input type="text" id="field-MovementType" placeholder="Ground" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-SightRange">Sight Range</label>
                                <input type="number" id="field-SightRange" min="0" step="any" placeholder="15.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-AcquisitionRange">Acquisition Range</label>
                                <input type="number" id="field-AcquisitionRange" min="0" step="any" placeholder="15.0" />
                            </div>
                        </div>
                    </div>

                    <div id="section-unit-costs" class="form-section">
                        <h3>Resource Costs & Production</h3>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-CostGold">Gold Cost</label>
                                <input type="number" id="field-CostGold" min="0" step="any" required />
                            </div>
                            <div class="form-group">
                                <label for="field-CostWood">Wood Cost</label>
                                <input type="number" id="field-CostWood" min="0" step="any" required />
                            </div>
                            <div class="form-group">
                                <label for="field-CostStone">Stone Cost</label>
                                <input type="number" id="field-CostStone" min="0" step="any" required />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-PopCost">Population Cost</label>
                                <input type="number" id="field-PopCost" min="0" step="1" required />
                            </div>
                            <div class="form-group">
                                <label for="field-ProductionTime">Production Time</label>
                                <input type="number" id="field-ProductionTime" min="0" step="any" required />
                            </div>
                        </div>
                    </div>

                    <div id="section-unit-combat" class="form-section">
                        <h3>Combat Types & Rewards</h3>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-AttackType">Attack Type</label>
                                <select id="field-AttackType" required>
                                    <option value="melee">Melee</option>
                                    <option value="ranged">Ranged</option>
                                    <option value="none">None</option>
                                </select>
                            </div>
                            <div class="form-group">
                                <label for="field-ArmorType">Armor Type</label>
                                <select id="field-ArmorType" required>
                                    <option value="light">Light</option>
                                    <option value="heavy">Heavy</option>
                                    <option value="building">Building</option>
                                </select>
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-GoldBounty">Gold Bounty</label>
                                <input type="number" id="field-GoldBounty" min="0" step="any" required />
                            </div>
                            <div class="form-group">
                                <label for="field-XpBounty">XP Bounty (Optional)</label>
                                <input type="number" id="field-XpBounty" min="0" step="any" />
                            </div>
                        </div>
                    </div>

                    <div id="section-unit-capabilities" class="form-section">
                        <h3>Lists & Capabilities</h3>
                        <div class="form-group">
                            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px;">
                                <label style="margin-bottom: 0;">Build Options (Optional)</label>
                                <div style="display: flex; gap: 4px;">
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="BuildOptions" title="Copy Build Options block">📋 Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="BuildOptions" title="Paste Build Options block">📥 Paste</button>
                                </div>
                            </div>
                            <div id="build-options-container" class="tag-list-container">
                                <div class="tags" id="build-options-tags"></div>
                                <div class="tag-input-row">
                                    <input type="text" id="build-option-input" list="suggest-units" placeholder="Add build option..." />
                                    <button type="button" id="add-build-option-btn" class="btn secondary-btn">+</button>
                                </div>
                            </div>
                        </div>
                        <div class="form-group">
                            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px;">
                                <label style="margin-bottom: 0;">Abilities (Optional)</label>
                                <div style="display: flex; gap: 4px;">
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="Abilities" title="Copy Abilities block">📋 Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="Abilities" title="Paste Abilities block">📥 Paste</button>
                                </div>
                            </div>
                            <div id="abilities-container" class="tag-list-container">
                                <div class="tags" id="abilities-tags"></div>
                                <div class="tag-input-row">
                                    <input type="text" id="ability-input" list="suggest-abilities" placeholder="Add ability..." />
                                    <button type="button" id="add-ability-btn" class="btn secondary-btn">+</button>
                                </div>
                            </div>
                        </div>
                        <div class="form-group">
                            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px;">
                                <label style="margin-bottom: 0;">Weapons (Optional)</label>
                                <div style="display: flex; gap: 4px;">
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="Weapons" title="Copy Weapons block">📋 Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="Weapons" title="Paste Weapons block">📥 Paste</button>
                                </div>
                            </div>
                            <div id="weapons-container" class="tag-list-container">
                                <div class="tags" id="weapons-tags"></div>
                                <div class="tag-input-row">
                                    <input type="text" id="weapon-input" list="suggest-weapons" placeholder="Add custom weapon ID..." />
                                    <button type="button" id="add-weapon-btn" class="btn secondary-btn">+</button>
                                </div>
                            </div>
                        </div>
                        <div class="form-group">
                            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px;">
                                <label style="margin-bottom: 0;">Starting Items (Optional)</label>
                                <div style="display: flex; gap: 4px;">
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="StartingItems" title="Copy Starting Items block">📋 Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="StartingItems" title="Paste Starting Items block">📥 Paste</button>
                                </div>
                            </div>
                            <div id="items-container" class="tag-list-container">
                                <div class="tags" id="items-tags"></div>
                                <div class="tag-input-row">
                                    <input type="text" id="item-input" list="suggest-items" placeholder="Add custom item ID..." />
                                    <button type="button" id="add-item-btn" class="btn secondary-btn">+</button>
                                </div>
                            </div>
                        </div>
                        <div class="form-group">
                            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px;">
                                <label style="margin-bottom: 0;">Tech Upgrades (Optional)</label>
                                <div style="display: flex; gap: 4px;">
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="Upgrades" title="Copy Tech Upgrades block">📋 Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="Upgrades" title="Paste Tech Upgrades block">📥 Paste</button>
                                </div>
                            </div>
                            <div id="upgrades-container" class="tag-list-container">
                                <div class="tags" id="upgrades-tags"></div>
                                <div class="tag-input-row">
                                    <input type="text" id="upgrade-input" list="suggest-upgrades" placeholder="Add custom upgrade ID..." />
                                    <button type="button" id="add-upgrade-btn" class="btn secondary-btn">+</button>
                                </div>
                            </div>
                        </div>
                        <div class="form-group">
                            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px;">
                                <label style="margin-bottom: 0;">Status Effects / Buffs (Optional)</label>
                                <div style="display: flex; gap: 4px;">
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="StatusEffects" title="Copy Status Effects block">📋 Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="StatusEffects" title="Paste Status Effects block">📥 Paste</button>
                                </div>
                            </div>
                            <div id="statuseffects-container" class="tag-list-container">
                                <div class="tags" id="statuseffects-tags"></div>
                                <div class="tag-input-row">
                                    <input type="text" id="statuseffect-input" placeholder="Add passive buff ID..." />
                                    <button type="button" id="add-statuseffect-btn" class="btn secondary-btn">+</button>
                                </div>
                            </div>
                        </div>
                        <div class="form-group">
                            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px;">
                                <label style="margin-bottom: 0;">Audio Sound Events (Optional)</label>
                                <div style="display: flex; gap: 4px;">
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="SoundEvents" title="Copy Sound Events block">📋 Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="SoundEvents" title="Paste Sound Events block">📥 Paste</button>
                                </div>
                            </div>
                            <div id="soundevents-container" class="tag-list-container">
                                <div class="tags" id="soundevents-tags"></div>
                                <div class="tag-input-row">
                                    <input type="text" id="soundevent-input" placeholder="Add audio event..." />
                                    <button type="button" id="add-soundevent-btn" class="btn secondary-btn">+</button>
                                </div>
                            </div>
                        </div>
                    </div>

                    <div id="section-pathing-flags" class="form-section">
                        <h3>Placement & Pathing Flags</h3>
                        <div class="form-group">
                            <div id="field-PathingType-flags" style="display: flex; flex-wrap: wrap; gap: 10px; margin-top: 4px;">
                                <label style="display: flex; align-items: center; gap: 4px; font-weight: normal;"><input type="checkbox" class="pathing-flag-cb" value="1" /> Shallow Water (1)</label>
                                <label style="display: flex; align-items: center; gap: 4px; font-weight: normal;"><input type="checkbox" class="pathing-flag-cb" value="2" /> Deep Water (2)</label>
                                <label style="display: flex; align-items: center; gap: 4px; font-weight: normal;"><input type="checkbox" class="pathing-flag-cb" value="4" /> Flying (4)</label>
                                <label style="display: flex; align-items: center; gap: 4px; font-weight: normal;"><input type="checkbox" class="pathing-flag-cb" value="8" /> Ground (8)</label>
                                <label style="display: flex; align-items: center; gap: 4px; font-weight: normal;"><input type="checkbox" class="pathing-flag-cb" value="32" /> Buildable (32)</label>
                            </div>
                            <input type="hidden" id="field-PathingType" value="8" />
                        </div>
                    </div>
                </div>
            </div>
            <div id="map-properties-form" class="editor-form hidden">
                <div class="form-header">
                    <div>
                        <div class="breadcrumb" id="map-properties-breadcrumb">Map > Properties</div>
                        <h2>Map Properties</h2>
                        <span class="subtitle">General configuration metadata</span>
                    </div>
                </div>
                <div class="form-scroll-container">
                    <div class="form-section">
                        <h3>General Information</h3>
                        <div class="form-group">
                            <label for="prop-MapName">Map Name</label>
                            <input type="text" id="prop-MapName" />
                        </div>
                        <div class="form-group">
                            <label for="prop-MapDescription">Description</label>
                            <textarea id="prop-MapDescription" rows="3"></textarea>
                        </div>
                        <div class="form-group">
                            <label for="prop-SuggestedPlayers">Suggested Players</label>
                            <input type="text" id="prop-SuggestedPlayers" placeholder="e.g. 2-4 Players" />
                        </div>
                    </div>
                    <div class="form-section">
                        <h3>Visuals & Assets</h3>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="prop-MinimapImage">Minimap Image</label>
                                <div class="input-with-browse">
                                    <input type="text" id="prop-MinimapImage" placeholder="res://Assets/...png" />
                                    <button type="button" class="btn browse-btn" data-input-id="prop-MinimapImage" data-file-types="png,jpg,jpeg,svg,tga,dds" title="Browse files">📁</button>
                                    <button type="button" class="btn clear-btn" data-input-id="prop-MinimapImage" title="Clear path">❌</button>
                                </div>
                            </div>
                            <div class="form-group">
                                <label for="prop-ShroudType">Shroud Style</label>
                                <select id="prop-ShroudType">
                                    <option value="visible">Always Visible</option>
                                    <option value="VisionShroud">VisionShroud (Grey Mask)</option>
                                    <option value="ExplorationShroud">ExplorationShroud (Black Unexplored)</option>
                                </select>
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="prop-TerrainBaseHeight">Terrain Base Height</label>
                                <input type="number" id="prop-TerrainBaseHeight" min="0" step="any" />
                            </div>
                            <div class="form-group">
                                <label for="prop-ShadowIntensity">Shadow Intensity</label>
                                <input type="number" id="prop-ShadowIntensity" min="0" step="any" />
                            </div>
                        </div>
                    </div>
                    <div class="form-section">
                        <h3>Dimensions & Limits</h3>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="prop-MapWidth">Map Width</label>
                                <input type="number" id="prop-MapWidth" min="0" step="1" />
                            </div>
                            <div class="form-group">
                                <label for="prop-MapHeight">Map Height</label>
                                <input type="number" id="prop-MapHeight" min="0" step="1" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="prop-PlayableWidth">Playable Width</label>
                                <input type="number" id="prop-PlayableWidth" min="0" step="1" />
                            </div>
                            <div class="form-group">
                                <label for="prop-PlayableHeight">Playable Height</label>
                                <input type="number" id="prop-PlayableHeight" min="0" step="1" />
                            </div>
                        </div>
                    </div>
                    <div class="form-section">
                        <h3>Loading Screen</h3>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="prop-LoadingImage">Loading Image</label>
                                <div class="input-with-browse">
                                    <input type="text" id="prop-LoadingImage" placeholder="res://Assets/...png" />
                                    <button type="button" class="btn browse-btn" data-input-id="prop-LoadingImage" data-file-types="png,jpg,jpeg,svg,tga,dds" title="Browse files">📁</button>
                                    <button type="button" class="btn clear-btn" data-input-id="prop-LoadingImage" title="Clear path">❌</button>
                                </div>
                            </div>
                            <div class="form-group">
                                <label for="prop-LoadingMusic">Loading Music</label>
                                <div class="input-with-browse">
                                    <input type="text" id="prop-LoadingMusic" placeholder="res://Assets/...ogg" />
                                    <button type="button" class="btn browse-btn" data-input-id="prop-LoadingMusic" data-file-types="ogg,wav,mp3" title="Browse files">📁</button>
                                    <button type="button" class="btn clear-btn" data-input-id="prop-LoadingMusic" title="Clear path">❌</button>
                                </div>
                            </div>
                        </div>
                        <div class="form-group">
                            <label for="prop-LoadingTitle">Loading Title</label>
                            <input type="text" id="prop-LoadingTitle" />
                        </div>
                        <div class="form-group">
                            <label for="prop-LoadingSubtitle">Loading Subtitle</label>
                            <input type="text" id="prop-LoadingSubtitle" />
                        </div>
                        <div class="form-group">
                            <label for="prop-LoadingBodyText">Loading Description / Lore</label>
                            <textarea id="prop-LoadingBodyText" rows="3"></textarea>
                        </div>
                    </div>
                    <div class="form-section">
                        <h3>Lobby Instructions</h3>
                        <div class="form-group">
                            <label for="prop-HowToPlayObjective">Lobby Objective</label>
                            <input type="text" id="prop-HowToPlayObjective" placeholder="e.g. Destroy the enemy town center" />
                        </div>
                        <div class="form-group">
                            <label>Lobby Instructions List (Optional)</label>
                            <div id="instructions-container" class="tag-list-container">
                                <div class="tags" id="instructions-tags"></div>
                                <div class="tag-input-row">
                                    <input type="text" id="instruction-input" placeholder="Add lobby instruction..." />
                                    <button type="button" id="add-instruction-btn" class="btn secondary-btn">+</button>
                                </div>
                            </div>
                        </div>
                    </div>
                    <div class="form-section">
                        <h3>Player Slots</h3>
                        <div id="player-slots-container" class="list-editor-container">
                            <div id="player-slots-list"></div>
                            <button type="button" id="add-player-slot-btn" class="btn secondary-btn">+ Add Player Slot</button>
                        </div>
                    </div>
                    <div class="form-section">
                        <h3>Teams & Alliances</h3>
                        <div id="teams-container" class="list-editor-container">
                            <div id="teams-list"></div>
                            <button type="button" id="add-team-btn" class="btn secondary-btn">+ Add Team</button>
                        </div>
                    </div>
                    <div class="form-section">
                        <h3>Changelog</h3>
                        <div id="changelog-container" class="list-editor-container">
                            <div id="changelog-list"></div>
                            <button type="button" id="add-changelog-btn" class="btn secondary-btn">+ Add Changelog Entry</button>
                        </div>
                    </div>
                </div>
            </div>
            <div id="custom-weapons-form" class="editor-form hidden">
                <div class="form-header">
                    <div>
                        <div class="breadcrumb">Map > Custom Weapons</div>
                        <h2>Custom Weapons</h2>
                        <span class="subtitle">Combat attacks &amp; weapon definitions</span>
                    </div>
                </div>
                <div class="form-scroll-container">
                    <div class="form-section">
                        <div id="weapons-list-container" class="list-editor-container">
                            <div id="custom-weapons-list"></div>
                            <div class="add-buttons-row" style="display: flex; gap: 8px;">
                                <button type="button" id="add-custom-weapon-btn" class="btn secondary-btn">+ Add Custom Weapon</button>
                                <button type="button" id="paste-custom-weapon-btn" class="btn secondary-btn" title="Paste Weapon from Clipboard">📋 Paste Weapon</button>
                                <button type="button" id="prune-weapons-btn" class="btn secondary-btn" title="Prune weapons never used by placed units on terrain.json">✂️ Prune Unused</button>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
            <div id="custom-abilities-form" class="editor-form hidden">
                <div class="form-header">
                    <div>
                        <div class="breadcrumb">Map > Custom Abilities</div>
                        <h2>Custom Abilities</h2>
                        <span class="subtitle">Spells and passives catalog</span>
                    </div>
                </div>
                <div class="form-scroll-container">
                    <div class="form-section">
                        <div id="abilities-list-container" class="list-editor-container">
                            <div id="custom-abilities-list"></div>
                            <div class="add-buttons-row" style="display: flex; gap: 8px;">
                                <button type="button" id="add-custom-ability-btn" class="btn secondary-btn">+ Add Custom Ability</button>
                                <button type="button" id="paste-custom-ability-btn" class="btn secondary-btn" title="Paste Ability from Clipboard">📋 Paste Ability</button>
                                <button type="button" id="prune-abilities-btn" class="btn secondary-btn" title="Prune abilities never used by placed units on terrain.json">✂️ Prune Unused</button>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
            <div id="custom-upgrades-form" class="editor-form hidden">
                <div class="form-header">
                    <div>
                        <div class="breadcrumb">Map > Custom Upgrades</div>
                        <h2>Custom Upgrades</h2>
                        <span class="subtitle">Researchable tech upgrades</span>
                    </div>
                </div>
                <div class="form-scroll-container">
                    <div class="form-section">
                        <div id="upgrades-list-container" class="list-editor-container">
                            <div id="custom-upgrades-list"></div>
                            <div class="add-buttons-row" style="display: flex; gap: 8px;">
                                <button type="button" id="add-custom-upgrade-btn" class="btn secondary-btn">+ Add Custom Upgrade</button>
                                <button type="button" id="paste-custom-upgrade-btn" class="btn secondary-btn" title="Paste Upgrade from Clipboard">📋 Paste Upgrade</button>
                                <button type="button" id="prune-upgrades-btn" class="btn secondary-btn" title="Prune upgrades never used by placed units on terrain.json">✂️ Prune Unused</button>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
            <div id="custom-items-form" class="editor-form hidden">
                <div class="form-header">
                    <div>
                        <div class="breadcrumb">Map > Custom Items</div>
                        <h2>Custom Items</h2>
                        <span class="subtitle">Inventory item specifications</span>
                    </div>
                </div>
                <div class="form-scroll-container">
                    <div class="form-section">
                        <div id="items-list-container" class="list-editor-container">
                            <div id="custom-items-list"></div>
                            <div class="add-buttons-row" style="display: flex; gap: 8px;">
                                <button type="button" id="add-custom-item-btn" class="btn secondary-btn">+ Add Custom Item</button>
                                <button type="button" id="paste-custom-item-btn" class="btn secondary-btn" title="Paste Item from Clipboard">📋 Paste Item</button>
                                <button type="button" id="prune-items-btn" class="btn secondary-btn" title="Prune items never used by placed units on terrain.json">✂️ Prune Unused</button>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
            
            <div id="debug-json-container" class="debug-json-container collapsed hidden">
                <div class="debug-json-header">
                    <h3>Debug Data JSON (Read-only)</h3>
                    <div class="debug-json-actions">
                        <button type="button" id="copy-json-btn" class="btn secondary-btn small-btn">Copy JSON</button>
                        <button type="button" id="expand-json-btn" class="btn secondary-btn small-btn">Expand</button>
                    </div>
                </div>
                <div class="debug-json-body">
                    <pre><code id="debug-json-pre"></code></pre>
                </div>
            </div>
        </div>
        </div>
    </div>
    <script nonce="${nonce}">
        window.REALM_LOCALE = "${detectedLocale}";
        window.REALM_I18N = ${JSON.stringify(activeDict)};
        window.REALM_EN_FALLBACK = ${JSON.stringify(enDict)};
    </script>
    <script nonce="${nonce}">${scriptContent}</script>
</body>
</html>`;
    }

    private getMergedDocumentText(document: vscode.TextDocument): string {
        const text = document.getText();
        try {
            const parsed = JSON.parse(text);
            if (!parsed.Assets || Object.keys(parsed.Assets).length === 0) {
                const docDir = path.dirname(document.uri.fsPath);
                const manifestPath = path.join(docDir, 'manifest.json');
                if (fs.existsSync(manifestPath)) {
                    const manifestData = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
                    if (manifestData && manifestData.Assets) {
                        parsed.Assets = manifestData.Assets;
                        return JSON.stringify(parsed);
                    }
                }
            }
        } catch {
        }
        return text;
    }

    private getNonce(): string {
        let text = '';
        const possible = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
        for (let i = 0; i < 32; i++) {
            text += possible.charAt(Math.floor(Math.random() * possible.length));
        }
        return text;
    }
}
