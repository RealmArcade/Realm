import * as vscode from 'vscode';
import { Node, ParseError, findNodeAtOffset, getNodePath, parse, parseTree } from 'jsonc-parser';
import { JsonPath, JsonSchema, SchemaDocument } from './schemaKeywords';
import { MapSchemaRegistry } from './schemaRegistry';

export interface JsonSnapshot {
    uri: string;
    version: number;
    text: string;
    lineEnding: string;
    tree: Node | undefined;
    value: unknown;
    errors: ParseError[];
}

export const parseOptions = { allowTrailingComma: true, disallowComments: false, allowEmptyContent: true };

const maximumCachedDocuments = 12;

export class JsonSnapshotCache {
    private readonly snapshots = new Map<string, JsonSnapshot>();

    get(document: vscode.TextDocument): JsonSnapshot {
        const key = document.uri.toString();
        const cached = this.snapshots.get(key);
        if (cached && cached.version === document.version) {
            return cached;
        }
        const text = document.getText();
        const errors: ParseError[] = [];
        const tree = parseTree(text, errors, parseOptions);
        const snapshot: JsonSnapshot = {
            uri: key,
            version: document.version,
            text,
            lineEnding: text.includes('\r\n') ? '\r\n' : '\n',
            tree,
            value: parse(text, undefined, parseOptions),
            errors
        };
        this.snapshots.set(key, snapshot);
        this.prune(key);
        return snapshot;
    }

    forget(uri: vscode.Uri): void {
        this.snapshots.delete(uri.toString());
    }

    dispose(): void {
        this.snapshots.clear();
    }

    private prune(keep: string): void {
        if (this.snapshots.size <= maximumCachedDocuments) {
            return;
        }
        for (const key of this.snapshots.keys()) {
            if (key !== keep) {
                this.snapshots.delete(key);
                if (this.snapshots.size <= maximumCachedDocuments) {
                    return;
                }
            }
        }
    }
}

export interface CursorLocation {
    document: vscode.TextDocument;
    snapshot: JsonSnapshot;
    schemaDocument: SchemaDocument;
    jsonPath: JsonPath;
    valueNode: Node | undefined;
    valueSchema: JsonSchema | undefined;
    containerNode: Node | undefined;
    containerSchema: JsonSchema | undefined;
    containerPath: JsonPath;
    keyName: string | undefined;
    keyNode: Node | undefined;
    keySchema: JsonSchema | undefined;
    onKey: boolean;
    offset: number;
}

export function schemaAtPath(schemaDocument: SchemaDocument, jsonPath: JsonPath): JsonSchema | undefined {
    let current: JsonSchema | undefined = schemaDocument.root;
    for (const segment of jsonPath) {
        if (!current) {
            return undefined;
        }
        current = typeof segment === 'number' ? schemaDocument.items(current) : schemaDocument.property(current, segment);
    }
    return current;
}

export function nodeRange(document: vscode.TextDocument, node: Node): vscode.Range {
    return new vscode.Range(document.positionAt(node.offset), document.positionAt(node.offset + node.length));
}

export function resolveCursorLocation(
    document: vscode.TextDocument,
    position: vscode.Position,
    cache: JsonSnapshotCache,
    registry: MapSchemaRegistry
): CursorLocation | undefined {
    const schemaDocument = registry.forDocument(document);
    if (!schemaDocument) {
        return undefined;
    }
    const snapshot = cache.get(document);
    const tree = snapshot.tree;
    if (!tree) {
        return undefined;
    }

    const offset = document.offsetAt(position);
    const deepest = findNodeAtOffset(tree, offset, true) ?? tree;

    let keyNode: Node | undefined;
    let valueNode: Node | undefined;
    let containerNode: Node | undefined;
    let onKey = false;

    if (deepest.type === 'property') {
        keyNode = deepest.children?.[0];
        valueNode = deepest.children?.[1];
        containerNode = deepest.parent;
        onKey = keyNode !== undefined && offset <= keyNode.offset + keyNode.length;
    } else if (deepest.parent && deepest.parent.type === 'property') {
        keyNode = deepest.parent.children?.[0];
        valueNode = deepest.parent.children?.[1];
        containerNode = deepest.parent.parent;
        onKey = keyNode !== undefined && deepest === keyNode && offset <= keyNode.offset + keyNode.length;
    } else if (deepest.type === 'object' || deepest.type === 'array') {
        containerNode = deepest;
        valueNode = deepest;
    } else {
        containerNode = deepest.parent;
        valueNode = deepest;
    }

    if (!containerNode || (containerNode.type !== 'object' && containerNode.type !== 'array')) {
        containerNode = undefined;
    }

    const containerPath: JsonPath = containerNode ? getNodePath(containerNode) : [];
    const containerSchema = schemaAtPath(schemaDocument, containerPath);
    const keyName = keyNode && typeof keyNode.value === 'string' ? keyNode.value : undefined;

    let jsonPath: JsonPath = containerPath;
    let keySchema: JsonSchema | undefined;
    let valueSchema: JsonSchema | undefined;

    if (keyName !== undefined) {
        jsonPath = [...containerPath, keyName];
        keySchema = schemaDocument.property(containerSchema, keyName);
        valueSchema = keySchema;
    } else if (containerNode) {
        jsonPath = containerPath;
        valueSchema = containerSchema;
    }

    return {
        document,
        snapshot,
        schemaDocument,
        jsonPath,
        valueNode,
        valueSchema,
        containerNode,
        containerSchema,
        containerPath,
        keyName,
        keyNode,
        keySchema,
        onKey,
        offset
    };
}

export function objectPropertyNames(node: Node | undefined): string[] {
    if (!node || node.type !== 'object' || !node.children) {
        return [];
    }
    const names: string[] = [];
    for (const child of node.children) {
        const key = child.children?.[0];
        if (key && typeof key.value === 'string') {
            names.push(key.value);
        }
    }
    return names;
}

export function propertyNodeByName(node: Node | undefined, name: string): Node | undefined {
    if (!node || node.type !== 'object' || !node.children) {
        return undefined;
    }
    for (const child of node.children) {
        const key = child.children?.[0];
        if (key && key.value === name) {
            return child;
        }
    }
    return undefined;
}

export function pathLabel(jsonPath: JsonPath): string {
    let label = '';
    for (const segment of jsonPath) {
        if (typeof segment === 'number') {
            label += `[${segment}]`;
            continue;
        }
        label += label.length === 0 ? segment : `.${segment}`;
    }
    return label;
}
