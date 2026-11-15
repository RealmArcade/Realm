import * as vscode from 'vscode';
import { Node } from 'jsonc-parser';
import { JsonSchema, assetKindOfPath } from './schemaKeywords';
import { JsonSnapshot, JsonSnapshotCache, nodeRange } from './jsonDocument';
import { MapSchemaRegistry } from './schemaRegistry';
import { AuthoringSettings } from './settings';
import { ColourSwatchStore, formatColourLiteral, parseColourLiteral } from './colourSwatch';

const glyphKinds = new Set(['rmesh', 'rtex', 'raud', 'ranim']);
const refreshDelayMilliseconds = 120;

interface EditorDecorations {
    types: Set<vscode.TextEditorDecorationType>;
}

export class AuthoringDecorations implements vscode.Disposable {
    private readonly colourTypes = new Map<string, vscode.TextEditorDecorationType>();
    private readonly glyphTypes = new Map<string, vscode.TextEditorDecorationType>();
    private readonly applied = new Map<vscode.TextEditor, EditorDecorations>();
    private readonly disposables: vscode.Disposable[] = [];
    private timer: ReturnType<typeof setTimeout> | undefined;
    private settings: AuthoringSettings;

    constructor(
        private readonly snapshots: JsonSnapshotCache,
        private readonly registry: MapSchemaRegistry,
        private readonly swatches: ColourSwatchStore,
        private readonly extensionUri: vscode.Uri,
        settings: AuthoringSettings
    ) {
        this.settings = settings;
    }

    register(): vscode.Disposable[] {
        this.disposables.push(
            vscode.window.onDidChangeActiveTextEditor(() => {
                this.schedule();
            }),
            vscode.window.onDidChangeVisibleTextEditors(editors => {
                for (const editor of editors) {
                    this.apply(editor);
                }
            }),
            vscode.window.onDidChangeTextEditorVisibleRanges(event => this.apply(event.textEditor)),
            vscode.workspace.onDidChangeTextDocument(event => {
                for (const editor of vscode.window.visibleTextEditors) {
                    if (editor.document === event.document) {
                        this.schedule();
                        return;
                    }
                }
            }),
            vscode.workspace.onDidCloseTextDocument(document => {
                for (const editor of [...this.applied.keys()]) {
                    if (editor.document === document) {
                        this.clear(editor);
                        this.applied.delete(editor);
                    }
                }
            })
        );
        this.schedule();
        return this.disposables;
    }

    updateSettings(settings: AuthoringSettings): void {
        this.settings = settings;
        this.schedule();
    }

    dispose(): void {
        if (this.timer) {
            clearTimeout(this.timer);
            this.timer = undefined;
        }
        for (const editor of [...this.applied.keys()]) {
            this.clear(editor);
        }
        this.applied.clear();
        const registered = [...this.disposables];
        this.disposables.length = 0;
        for (const type of this.colourTypes.values()) {
            type.dispose();
        }
        this.colourTypes.clear();
        for (const type of this.glyphTypes.values()) {
            type.dispose();
        }
        this.glyphTypes.clear();
        for (const disposable of registered) {
            disposable.dispose();
        }
    }

    private schedule(): void {
        if (this.timer) {
            clearTimeout(this.timer);
        }
        this.timer = setTimeout(() => {
            this.timer = undefined;
            for (const editor of vscode.window.visibleTextEditors) {
                this.apply(editor);
            }
        }, refreshDelayMilliseconds);
    }

    private apply(editor: vscode.TextEditor): void {
        if (!this.settings.decorationsEnabled || !this.registry.forDocument(editor.document)) {
            this.clear(editor);
            return;
        }

        try {
            const decorationByType = new Map<vscode.TextEditorDecorationType, vscode.Range[]>();
            this.collect(editor, decorationByType);

            const previous = this.applied.get(editor)?.types ?? new Set<vscode.TextEditorDecorationType>();
            for (const type of previous) {
                if (!decorationByType.has(type)) {
                    editor.setDecorations(type, []);
                }
            }
            for (const [type, ranges] of decorationByType) {
                editor.setDecorations(type, ranges);
            }
            this.applied.set(editor, { types: new Set(decorationByType.keys()) });
        } catch {
            this.clear(editor);
        }
    }

    private clear(editor: vscode.TextEditor): void {
        const previous = this.applied.get(editor);
        if (!previous) {
            return;
        }
        for (const type of previous.types) {
            editor.setDecorations(type, []);
        }
        this.applied.delete(editor);
    }

    private collect(editor: vscode.TextEditor, decorationByType: Map<vscode.TextEditorDecorationType, vscode.Range[]>): void {
        const schemaDocument = this.registry.forDocument(editor.document);
        if (!schemaDocument) {
            return;
        }
        const snapshot: JsonSnapshot = this.snapshots.get(editor.document);
        const tree = snapshot.tree;
        if (!tree) {
            return;
        }

        const visible = editor.visibleRanges;
        const intersects = (node: Node): boolean => {
            const start = node.offset;
            const end = node.offset + node.length;
            for (const range of visible) {
                const rangeStart = editor.document.offsetAt(range.start);
                const rangeEnd = editor.document.offsetAt(range.end);
                if (start <= rangeEnd && end >= rangeStart) {
                    return true;
                }
            }
            return false;
        };

        const add = (type: vscode.TextEditorDecorationType, node: Node) => {
            const ranges = decorationByType.get(type) ?? [];
            ranges.push(nodeRange(editor.document, node));
            decorationByType.set(type, ranges);
        };

        const visit = (node: Node, schema: JsonSchema | undefined) => {
            if (!intersects(node)) {
                return;
            }
            if (node.type === 'object' && node.children) {
                for (const child of node.children) {
                    const keyNode = child.children?.[0];
                    const valueNode = child.children?.[1];
                    if (!valueNode || !keyNode || typeof keyNode.value !== 'string') {
                        continue;
                    }
                    visit(valueNode, schemaDocument.property(schema, keyNode.value));
                }
                return;
            }
            if (node.type === 'array' && node.children) {
                const itemSchema = schemaDocument.items(schema);
                for (const child of node.children) {
                    visit(child, itemSchema);
                }
                return;
            }
            if (node.type !== 'string' || typeof node.value !== 'string') {
                return;
            }

            if (schemaDocument.control(schema) === 'color') {
                const type = this.colourTypeFor(node.value);
                if (type) {
                    add(type, node);
                }
                return;
            }

            const asset = schemaDocument.asset(schema);
            if (asset) {
                const kind = assetKindOfPath(node.value);
                if (!kind || !asset.kinds.includes(kind) || !glyphKinds.has(kind)) {
                    return;
                }
                const type = this.glyphTypeFor(kind);
                if (type) {
                    add(type, node);
                }
            }
        };

        visit(tree, schemaDocument.root);
    }

    private colourTypeFor(literal: string): vscode.TextEditorDecorationType | undefined {
        const parsed = parseColourLiteral(literal);
        if (!parsed) {
            return undefined;
        }
        const key = formatColourLiteral(parsed, parsed.hasAlpha).toLowerCase();
        const cached = this.colourTypes.get(key);
        if (cached) {
            return cached;
        }
        const swatch = this.swatches.uriFor(literal);
        if (!swatch) {
            return undefined;
        }
        const type = vscode.window.createTextEditorDecorationType({
            rangeBehavior: vscode.DecorationRangeBehavior.ClosedClosed,
            after: {
                contentIconPath: swatch,
                margin: '0 0 0 0.4em',
                textDecoration: 'none; cursor: default;'
            }
        });
        this.colourTypes.set(key, type);
        return type;
    }

    private glyphTypeFor(kind: string): vscode.TextEditorDecorationType | undefined {
        const cached = this.glyphTypes.get(kind);
        if (cached) {
            return cached;
        }
        const iconUri = vscode.Uri.joinPath(this.extensionUri, 'media', 'icons', `${kind}.svg`);
        const type = vscode.window.createTextEditorDecorationType({
            rangeBehavior: vscode.DecorationRangeBehavior.ClosedClosed,
            after: {
                contentIconPath: iconUri,
                margin: '0 0 0 0.4em',
                textDecoration: 'none; cursor: default;'
            }
        });
        this.glyphTypes.set(kind, type);
        return type;
    }
}
