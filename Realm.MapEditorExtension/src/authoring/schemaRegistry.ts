import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { JsonSchema, SchemaDocument } from './schemaKeywords';

interface ValidationContribution {
    globs: string[];
    patterns: RegExp[];
    schemaPath: string;
}

function escapeForRegExp(text: string): string {
    return text.replace(/[.+^${}()|[\]\\]/g, '\\$&');
}

export function globToRegExp(glob: string): RegExp {
    const normalized = glob.replace(/\\/g, '/');
    let pattern = '';
    for (let index = 0; index < normalized.length; index++) {
        const character = normalized[index];
        if (character === '*') {
            if (normalized[index + 1] === '*') {
                pattern += '.*';
                index++;
                if (normalized[index + 1] === '/') {
                    pattern += '/?';
                    index++;
                }
                continue;
            }
            pattern += '[^/]*';
            continue;
        }
        if (character === '?') {
            pattern += '[^/]';
            continue;
        }
        pattern += escapeForRegExp(character);
    }
    return new RegExp(`^${pattern}$`, 'i');
}

export class MapSchemaRegistry implements vscode.Disposable {
    private readonly contributions: ValidationContribution[] = [];
    private readonly schemas = new Map<string, SchemaDocument | undefined>();
    private readonly disposables: vscode.Disposable[] = [];

    constructor(extensionUri: vscode.Uri, packageJson: unknown) {
        const contributes = (packageJson as { contributes?: { jsonValidation?: unknown } } | undefined)?.contributes;
        const entries = contributes?.jsonValidation;
        if (Array.isArray(entries)) {
            for (const entry of entries) {
                if (typeof entry !== 'object' || entry === null) {
                    continue;
                }
                const contribution = entry as { fileMatch?: unknown; url?: unknown };
                if (typeof contribution.url !== 'string') {
                    continue;
                }
                const globs = (Array.isArray(contribution.fileMatch) ? contribution.fileMatch : [contribution.fileMatch])
                    .filter((match): match is string => typeof match === 'string');
                if (globs.length === 0) {
                    continue;
                }
                const patterns = globs.map(globToRegExp);
                const schemaPath = contribution.url.startsWith('./') || contribution.url.startsWith('../')
                    ? path.resolve(extensionUri.fsPath, contribution.url)
                    : contribution.url;
                this.contributions.push({ globs, patterns, schemaPath });
            }
        }

        this.watchSchemas();
    }

    forDocument(document: vscode.TextDocument): SchemaDocument | undefined {
        const normalizedPath = document.uri.fsPath.replace(/\\/g, '/');
        for (const contribution of this.contributions) {
            if (!contribution.patterns.some(pattern => pattern.test(normalizedPath))) {
                continue;
            }
            return this.load(contribution.schemaPath);
        }
        return undefined;
    }

    documentFilters(): vscode.DocumentFilter[] {
        const filters: vscode.DocumentFilter[] = [];
        for (const contribution of this.contributions) {
            for (const glob of contribution.globs) {
                filters.push({ scheme: 'file', pattern: glob });
            }
        }
        return filters;
    }

    forSchemaPath(schemaPath: string): SchemaDocument | undefined {
        return this.load(schemaPath);
    }

    dispose(): void {
        for (const disposable of this.disposables) {
            disposable.dispose();
        }
        this.disposables.length = 0;
    }

    private watchSchemas(): void {
        for (const contribution of this.contributions) {
            if (!contribution.schemaPath.startsWith('/') && !/^[a-zA-Z]:[\\/]/.test(contribution.schemaPath)) {
                continue;
            }
            const watcher = vscode.workspace.createFileSystemWatcher(
                new vscode.RelativePattern(vscode.Uri.file(path.dirname(contribution.schemaPath)), path.basename(contribution.schemaPath))
            );
            const invalidate = () => this.schemas.delete(contribution.schemaPath);
            this.disposables.push(watcher, watcher.onDidChange(invalidate), watcher.onDidCreate(invalidate), watcher.onDidDelete(invalidate));
        }
    }

    private load(schemaPath: string): SchemaDocument | undefined {
        if (this.schemas.has(schemaPath)) {
            return this.schemas.get(schemaPath);
        }
        let parsed: SchemaDocument | undefined;
        try {
            const text = fs.readFileSync(schemaPath, 'utf8').replace(/^\uFEFF/, '');
            const root = JSON.parse(text) as JsonSchema;
            parsed = new SchemaDocument(root);
        } catch {
            parsed = undefined;
        }
        this.schemas.set(schemaPath, parsed);
        return parsed;
    }
}
