import * as vscode from 'vscode';
import { Node } from 'jsonc-parser';
import { JsonSchema, SchemaDocument, assetKindOfPath } from './schemaKeywords';
import { JsonSnapshot, JsonSnapshotCache } from './jsonDocument';
import { MapSchemaRegistry } from './schemaRegistry';
import { MapAssetIndex, mapFolderOfDocument } from './assetIndex';
import { parseColourLiteral } from './colourSwatch';
import { AuthoringSettings } from './settings';
import { RealmRmeshViewerProvider } from '../rmeshEditorProvider';
import { RealmRtexViewerProvider } from '../rtexEditorProvider';
import { RealmRaudViewerProvider } from '../raudEditorProvider';
import { RealmRanimViewerProvider } from '../ranimEditorProvider';

interface ViewerKind {
    title: string;
    viewType: string;
}

const viewerKinds: { [kind: string]: ViewerKind } = {
    rmesh: { title: 'Preview mesh', viewType: RealmRmeshViewerProvider.viewType },
    rtex: { title: 'View texture', viewType: RealmRtexViewerProvider.viewType },
    raud: { title: 'Play audio', viewType: RealmRaudViewerProvider.viewType },
    ranim: { title: 'Play animation', viewType: RealmRanimViewerProvider.viewType }
};

interface LensDescriptor {
    offset: number;
    length: number;
    title: string;
    command: string;
    arguments: unknown[];
    tooltip: string;
}

const refreshDelayMilliseconds = 250;

export class AuthoringCodeLensProvider implements vscode.CodeLensProvider, vscode.Disposable {
    private readonly changeEmitter = new vscode.EventEmitter<void>();
    private readonly cache = new Map<string, { version: number; lenses: LensDescriptor[] }>();
    private readonly timers = new Map<string, ReturnType<typeof setTimeout>>();
    private settings: AuthoringSettings;

    readonly onDidChangeCodeLenses = this.changeEmitter.event;

    constructor(
        private readonly snapshots: JsonSnapshotCache,
        private readonly registry: MapSchemaRegistry,
        private readonly assets: MapAssetIndex,
        settings: AuthoringSettings
    ) {
        this.settings = settings;
    }

    register(): vscode.Disposable[] {
        return [
            vscode.workspace.onDidChangeTextDocument(event => this.scheduleRefresh(event.document)),
            vscode.workspace.onDidCloseTextDocument(document => {
                this.cache.delete(document.uri.toString());
                this.timers.delete(document.uri.toString());
            })
        ];
    }

    updateSettings(settings: AuthoringSettings): void {
        this.settings = settings;
        this.cache.clear();
        this.changeEmitter.fire();
    }

    dispose(): void {
        for (const timer of this.timers.values()) {
            clearTimeout(timer);
        }
        this.timers.clear();
        this.cache.clear();
        this.changeEmitter.dispose();
    }

    provideCodeLenses(document: vscode.TextDocument, token: vscode.CancellationToken): vscode.CodeLens[] {
        if (!this.settings.codeLensEnabled || token.isCancellationRequested) {
            return [];
        }
        try {
            const descriptors = this.lensesFor(document);
            return descriptors.map(descriptor => new vscode.CodeLens(
                new vscode.Range(document.positionAt(descriptor.offset), document.positionAt(descriptor.offset + descriptor.length)),
                {
                    title: descriptor.title,
                    command: descriptor.command,
                    arguments: descriptor.arguments,
                    tooltip: descriptor.tooltip
                }
            ));
        } catch {
            return [];
        }
    }

    private scheduleRefresh(document: vscode.TextDocument): void {
        if (!this.registry.forDocument(document)) {
            return;
        }
        const key = document.uri.toString();
        const timer = this.timers.get(key);
        if (timer) {
            clearTimeout(timer);
        }
        this.timers.set(key, setTimeout(() => {
            this.timers.delete(key);
            this.cache.delete(key);
            this.changeEmitter.fire();
        }, refreshDelayMilliseconds));
    }

    private lensesFor(document: vscode.TextDocument): LensDescriptor[] {
        const key = document.uri.toString();
        const cached = this.cache.get(key);
        if (cached && cached.version === document.version) {
            return cached.lenses;
        }
        const lenses = this.computeLenses(document);
        this.cache.set(key, { version: document.version, lenses });
        return lenses;
    }

    private computeLenses(document: vscode.TextDocument): LensDescriptor[] {
        const schemaDocument = this.registry.forDocument(document);
        if (!schemaDocument) {
            return [];
        }
        const snapshot: JsonSnapshot = this.snapshots.get(document);
        if (!snapshot.tree) {
            return [];
        }
        const mapFolder = mapFolderOfDocument(document);
        const descriptors: LensDescriptor[] = [];
        this.visit(document, schemaDocument, snapshot.tree, schemaDocument.root, mapFolder, descriptors);
        return descriptors;
    }

    private visit(
        document: vscode.TextDocument,
        schemaDocument: SchemaDocument,
        node: Node,
        schema: JsonSchema | undefined,
        mapFolder: string | undefined,
        descriptors: LensDescriptor[]
    ): void {
        if (node.type === 'object' && node.children) {
            for (const child of node.children) {
                const keyNode = child.children?.[0];
                const valueNode = child.children?.[1];
                if (!valueNode || !keyNode || typeof keyNode.value !== 'string') {
                    continue;
                }
                this.visit(document, schemaDocument, valueNode, schemaDocument.property(schema, keyNode.value), mapFolder, descriptors);
            }
            return;
        }

        if (node.type === 'array' && node.children) {
            const itemSchema = schemaDocument.items(schema);
            for (const child of node.children) {
                this.visit(document, schemaDocument, child, itemSchema, mapFolder, descriptors);
            }
            return;
        }

        if (node.type !== 'string' || typeof node.value !== 'string') {
            return;
        }

        const asset = schemaDocument.asset(schema);
        if (asset) {
            const kind = assetKindOfPath(node.value);
            const viewer = kind ? viewerKinds[kind] : undefined;
            if (viewer && mapFolder && kind && asset.kinds.includes(kind)) {
                const resolution = this.assets.resolve(mapFolder, asset.folders, node.value);
                if (resolution.state === 'exists') {
                    descriptors.push({
                        offset: node.offset,
                        length: node.length,
                        title: viewer.title,
                        command: 'realm.metadata.openAssetPreview',
                        arguments: [document.uri.toString(), viewer.viewType, resolution.relativePath ?? node.value],
                        tooltip: `${node.value} (${resolution.relativePath ?? node.value})`
                    });
                } else if (resolution.state === 'missing') {
                    descriptors.push({
                        offset: node.offset,
                        length: node.length,
                        title: `Missing file: ${node.value}`,
                        command: 'realm.metadata.explainMissingAsset',
                        arguments: [document.uri.toString(), node.value, asset.folders],
                        tooltip: 'The file this value names is not under the declared folders.'
                    });
                }
            }
        }

        if (schemaDocument.control(schema) === 'color' && parseColourLiteral(node.value)) {
            descriptors.push({
                offset: node.offset,
                length: node.length,
                title: 'Pick colour…',
                command: 'realm.metadata.pickColor',
                arguments: [document.uri.toString(), node.offset],
                tooltip: 'Open a colour picker and write the chosen literal back into this value.'
            });
        }
    }
}
