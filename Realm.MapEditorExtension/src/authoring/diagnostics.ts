import * as vscode from 'vscode';
import { Node, getNodePath } from 'jsonc-parser';
import { JsonSchema, SchemaDocument } from './schemaKeywords';
import { JsonSnapshot, JsonSnapshotCache, nodeRange, pathLabel } from './jsonDocument';
import { MapSchemaRegistry } from './schemaRegistry';

const maximumReportedProblems = 500;
const refreshDelayMilliseconds = 180;

export class SchemaDiagnostics implements vscode.Disposable {
    private readonly collection: vscode.DiagnosticCollection;
    private readonly pending = new Map<string, ReturnType<typeof setTimeout>>();

    constructor(
        private readonly snapshots: JsonSnapshotCache,
        private readonly registry: MapSchemaRegistry
    ) {
        this.collection = vscode.languages.createDiagnosticCollection('realm-map-authoring');
    }

    register(): vscode.Disposable[] {
        return [
            this.collection,
            vscode.workspace.onDidOpenTextDocument(document => this.refreshSoon(document)),
            vscode.workspace.onDidChangeTextDocument(event => this.refreshSoon(event.document)),
            vscode.workspace.onDidCloseTextDocument(document => this.forget(document.uri)),
            { dispose: () => this.dispose() }
        ];
    }

    dispose(): void {
        for (const timer of this.pending.values()) {
            clearTimeout(timer);
        }
        this.pending.clear();
        this.collection.dispose();
    }

    private forget(uri: vscode.Uri): void {
        const key = uri.toString();
        const timer = this.pending.get(key);
        if (timer) {
            clearTimeout(timer);
            this.pending.delete(key);
        }
        this.collection.delete(uri);
    }

    private refreshSoon(document: vscode.TextDocument): void {
        if (!this.registry.forDocument(document)) {
            return;
        }
        const key = document.uri.toString();
        const timer = this.pending.get(key);
        if (timer) {
            clearTimeout(timer);
        }
        this.pending.set(key, setTimeout(() => {
            this.pending.delete(key);
            this.refresh(document);
        }, refreshDelayMilliseconds));
    }

    private refresh(document: vscode.TextDocument): void {
        if (document.isClosed) {
            return;
        }
        let diagnostics: vscode.Diagnostic[] = [];
        try {
            diagnostics = this.collect(document);
        } catch {
            diagnostics = [];
        }
        this.collection.set(document.uri, diagnostics);
    }

    private collect(document: vscode.TextDocument): vscode.Diagnostic[] {
        const schemaDocument = this.registry.forDocument(document);
        if (!schemaDocument) {
            return [];
        }
        const snapshot: JsonSnapshot = this.snapshots.get(document);
        if (!snapshot.tree) {
            return [];
        }
        const diagnostics: vscode.Diagnostic[] = [];
        this.visit(document, snapshot, schemaDocument, snapshot.tree, schemaDocument.root, diagnostics);
        return diagnostics;
    }

    private visit(
        document: vscode.TextDocument,
        snapshot: JsonSnapshot,
        schemaDocument: SchemaDocument,
        node: Node,
        schema: JsonSchema | undefined,
        diagnostics: vscode.Diagnostic[]
    ): void {
        if (diagnostics.length >= maximumReportedProblems) {
            return;
        }

        if (node.type === 'number' && typeof node.value === 'number') {
            this.reportRange(document, schemaDocument, node, schema, diagnostics);
            return;
        }

        if (node.type === 'object' && node.children) {
            const properties = schemaDocument.properties(schema);
            const closed = schemaDocument.isClosedObject(schema);
            const containerLabel = pathLabel(getNodePath(node));
            for (const child of node.children) {
                const keyNode = child.children?.[0];
                const valueNode = child.children?.[1];
                if (!keyNode || typeof keyNode.value !== 'string') {
                    continue;
                }
                const name = keyNode.value;
                if (closed && properties && !Object.prototype.hasOwnProperty.call(properties, name)) {
                    const diagnostic = new vscode.Diagnostic(
                        nodeRange(document, keyNode),
                        `Unknown property "${name}" in ${containerLabel.length === 0 ? 'the map document' : containerLabel}.`,
                        vscode.DiagnosticSeverity.Error
                    );
                    diagnostic.source = 'realm';
                    diagnostic.code = 'unknown-property';
                    diagnostics.push(diagnostic);
                }
                if (valueNode) {
                    this.visit(document, snapshot, schemaDocument, valueNode, schemaDocument.property(schema, name), diagnostics);
                }
            }
            return;
        }

        if (node.type === 'array' && node.children) {
            const itemSchema = schemaDocument.items(schema);
            for (const child of node.children) {
                this.visit(document, snapshot, schemaDocument, child, itemSchema, diagnostics);
            }
        }
    }

    private reportRange(
        document: vscode.TextDocument,
        schemaDocument: SchemaDocument,
        node: Node,
        schema: JsonSchema | undefined,
        diagnostics: vscode.Diagnostic[]
    ): void {
        const value = node.value as number;
        const minimum = schemaDocument.minimum(schema);
        const maximum = schemaDocument.maximum(schema);
        if (minimum !== undefined && value < minimum) {
            const diagnostic = new vscode.Diagnostic(
                nodeRange(document, node),
                `Value ${value} is below the minimum of ${minimum}.`,
                vscode.DiagnosticSeverity.Warning
            );
            diagnostic.source = 'realm';
            diagnostic.code = 'below-minimum';
            diagnostics.push(diagnostic);
            return;
        }
        if (maximum !== undefined && value > maximum) {
            const diagnostic = new vscode.Diagnostic(
                nodeRange(document, node),
                `Value ${value} is above the maximum of ${maximum}.`,
                vscode.DiagnosticSeverity.Warning
            );
            diagnostic.source = 'realm';
            diagnostic.code = 'above-maximum';
            diagnostics.push(diagnostic);
        }
    }
}
