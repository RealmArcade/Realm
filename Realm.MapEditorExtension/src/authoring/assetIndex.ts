import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';

export interface AssetFileIndex {
    files: string[];
    folders: string[];
    filesByLowerPath: Map<string, string>;
}

export type AssetResolutionState = 'exists' | 'missing' | 'unknown';

export interface AssetResolution {
    state: AssetResolutionState;
    relativePath: string | undefined;
}

const skippedDirectoryNames = new Set(['node_modules', 'bin', 'obj', 'lib']);
const maximumWalkDepth = 8;
const maximumIndexedFiles = 20000;

function toPosixPath(value: string): string {
    return value.replace(/\\/g, '/');
}

function walkFolder(mapFolder: string): AssetFileIndex {
    const files: string[] = [];
    const folders: string[] = [];
    const filesByLowerPath = new Map<string, string>();

    const visit = (relativeFolder: string, depth: number) => {
        if (depth > maximumWalkDepth || files.length >= maximumIndexedFiles) {
            return;
        }
        const absoluteFolder = relativeFolder.length === 0 ? mapFolder : path.join(mapFolder, relativeFolder);
        let entries: fs.Dirent[];
        try {
            entries = fs.readdirSync(absoluteFolder, { withFileTypes: true });
        } catch {
            return;
        }
        for (const entry of entries) {
            if (entry.name.startsWith('.') || skippedDirectoryNames.has(entry.name)) {
                continue;
            }
            const relativePath = relativeFolder.length === 0 ? entry.name : `${relativeFolder}/${entry.name}`;
            if (entry.isDirectory()) {
                folders.push(`${relativePath}/`);
                visit(relativePath, depth + 1);
                continue;
            }
            if (!entry.isFile()) {
                continue;
            }
            files.push(relativePath);
            filesByLowerPath.set(relativePath.toLowerCase(), relativePath);
        }
    };

    visit('', 0);
    return { files, folders, filesByLowerPath };
}

export class MapAssetIndex implements vscode.Disposable {
    private readonly indexes = new Map<string, AssetFileIndex | undefined>();
    private readonly watchers: vscode.Disposable[] = [];

    constructor() {
        for (const folder of vscode.workspace.workspaceFolders ?? []) {
            this.watchFolder(folder);
        }
        this.watchers.push(vscode.workspace.onDidChangeWorkspaceFolders(event => {
            for (const removed of event.removed) {
                this.indexes.delete(removed.uri.fsPath);
            }
            for (const added of event.added) {
                this.watchFolder(added);
            }
        }));
    }

    index(mapFolder: string): AssetFileIndex {
        if (this.indexes.has(mapFolder)) {
            return this.indexes.get(mapFolder) ?? { files: [], folders: [], filesByLowerPath: new Map() };
        }
        const index = walkFolder(mapFolder);
        this.indexes.set(mapFolder, index);
        return index;
    }

    filesUnderFolder(mapFolder: string, relativeFolder: string, kinds: string[]): string[] {
        const index = this.index(mapFolder);
        const prefix = relativeFolder === '.' || relativeFolder.length === 0 ? '' : `${toPosixPath(relativeFolder).replace(/\/+$/, '')}/`;
        const allowedKinds = new Set(kinds.map(kind => kind.toLowerCase()));
        const matches: string[] = [];
        for (const file of index.files) {
            if (prefix.length > 0 && !file.toLowerCase().startsWith(prefix.toLowerCase())) {
                continue;
            }
            const dot = file.lastIndexOf('.');
            const kind = dot > 0 ? file.slice(dot + 1).toLowerCase() : '';
            if (!allowedKinds.has(kind)) {
                continue;
            }
            matches.push(file);
        }
        return matches;
    }

    foldersUnderFolder(mapFolder: string, relativeFolder: string): string[] {
        const index = this.index(mapFolder);
        const prefix = relativeFolder === '.' || relativeFolder.length === 0 ? '' : `${toPosixPath(relativeFolder).replace(/\/+$/, '')}/`;
        return index.folders.filter(folder => (prefix.length === 0 ? true : folder.toLowerCase().startsWith(prefix.toLowerCase())));
    }

    resolve(mapFolder: string, declaredFolders: string[], value: string): AssetResolution {
        const normalized = toPosixPath(value.trim());
        if (normalized.length === 0) {
            return { state: 'missing', relativePath: undefined };
        }
        if (normalized.startsWith('res://')) {
            return { state: 'unknown', relativePath: undefined };
        }

        const index = this.index(mapFolder);
        const candidates: string[] = [];
        if (normalized.includes('/')) {
            candidates.push(normalized.replace(/^\.\//, ''));
        } else {
            for (const folder of declaredFolders) {
                candidates.push(folder === '.' || folder.length === 0 ? normalized : `${toPosixPath(folder).replace(/\/+$/, '')}/${normalized}`);
            }
        }

        for (const candidate of candidates) {
            const found = index.filesByLowerPath.get(candidate.toLowerCase());
            if (found) {
                return { state: 'exists', relativePath: found };
            }
        }

        const searchedEverything = normalized.includes('/') || declaredFolders.some(folder => {
            if (folder === '.' || folder.length === 0) {
                return true;
            }
            try {
                return fs.statSync(path.join(mapFolder, folder)).isDirectory();
            } catch {
                return false;
            }
        });
        return { state: searchedEverything ? 'missing' : 'unknown', relativePath: undefined };
    }

    dispose(): void {
        for (const watcher of this.watchers) {
            watcher.dispose();
        }
        this.watchers.length = 0;
        this.indexes.clear();
    }

    private watchFolder(folder: vscode.WorkspaceFolder): void {
        const watcher = vscode.workspace.createFileSystemWatcher(
            new vscode.RelativePattern(folder, '**/*.{rmesh,rtex,raud,ranim,png,webp}')
        );
        const invalidate = () => this.indexes.delete(folder.uri.fsPath);
        this.watchers.push(watcher, watcher.onDidCreate(invalidate), watcher.onDidDelete(invalidate), watcher.onDidChange(invalidate));
    }
}

export function mapFolderOfDocument(document: vscode.TextDocument): string | undefined {
    if (document.uri.scheme !== 'file') {
        return undefined;
    }
    return path.dirname(document.uri.fsPath);
}
