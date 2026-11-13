import * as vscode from 'vscode';
import { JsonSchema, SchemaDocument } from './schemaKeywords';
import { JsonSnapshot, JsonSnapshotCache } from './jsonDocument';
import { MapSchemaRegistry } from './schemaRegistry';

export interface SheetRecord {
    identity: string;
    value: { [key: string]: any };
}

export interface SheetIndex {
    name: string;
    identityKey: string;
    records: SheetRecord[];
}

export interface DocumentIndex {
    sheets: Map<string, SheetIndex>;
    dictionaries: Map<string, string[]>;
}

function isPlainObject(value: unknown): value is { [key: string]: any } {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function candidateIdentityKeys(
    records: { [key: string]: any }[],
    schemaDocument: SchemaDocument,
    recordSchema: JsonSchema | undefined
): string[] {
    const ordered = schemaDocument.propertyNames(recordSchema);
    for (const record of records) {
        for (const key of Object.keys(record)) {
            if (!ordered.includes(key)) {
                ordered.push(key);
            }
        }
    }
    return ordered;
}

function detectIdentityKey(
    records: { [key: string]: any }[],
    schemaDocument: SchemaDocument,
    recordSchema: JsonSchema | undefined
): string | undefined {
    if (records.length === 0) {
        return undefined;
    }

    let uniformFallback: string | undefined;
    for (const key of candidateIdentityKeys(records, schemaDocument, recordSchema)) {
        const values: string[] = [];
        let uniform = true;
        for (const record of records) {
            const value = record[key];
            if (typeof value !== 'string' || value.length === 0) {
                uniform = false;
                break;
            }
            values.push(value);
        }
        if (!uniform) {
            continue;
        }
        const distinct = new Set(values.map(value => value.toLowerCase()));
        if (distinct.size === values.length) {
            return key;
        }
        if (uniformFallback === undefined) {
            uniformFallback = key;
        }
    }
    return uniformFallback;
}

function indexArray(
    name: string,
    entries: unknown[],
    schemaDocument: SchemaDocument,
    schema: JsonSchema | undefined,
    index: DocumentIndex
): void {
    if (entries.length === 0) {
        return;
    }
    const records = entries.filter(isPlainObject);
    if (records.length !== entries.length) {
        return;
    }
    const recordSchema = schemaDocument.items(schema);
    const identityKey = detectIdentityKey(records, schemaDocument, recordSchema);
    if (!identityKey || index.sheets.has(name)) {
        return;
    }
    const sheetRecords: SheetRecord[] = [];
    for (const record of records) {
        const identity = record[identityKey];
        if (typeof identity === 'string') {
            sheetRecords.push({ identity, value: record });
        }
    }
    if (sheetRecords.length > 0) {
        index.sheets.set(name, { name, identityKey, records: sheetRecords });
    }
}

function walk(
    value: unknown,
    schema: JsonSchema | undefined,
    propertyName: string | undefined,
    schemaDocument: SchemaDocument,
    index: DocumentIndex
): void {
    if (Array.isArray(value)) {
        if (propertyName !== undefined) {
            indexArray(propertyName, value, schemaDocument, schema, index);
        }
        const itemSchema = schemaDocument.items(schema);
        for (const entry of value) {
            walk(entry, itemSchema, undefined, schemaDocument, index);
        }
        return;
    }

    if (!isPlainObject(value)) {
        return;
    }

    const entrySchema = schemaDocument.entryValueSchema(schema);
    if (entrySchema && propertyName !== undefined && !index.dictionaries.has(propertyName)) {
        index.dictionaries.set(propertyName, Object.keys(value));
    }

    for (const key of Object.keys(value)) {
        walk(value[key], schemaDocument.property(schema, key), key, schemaDocument, index);
    }
}

export class DocumentIndexCache {
    private readonly entries = new Map<string, { version: number; index: DocumentIndex }>();

    constructor(
        private readonly snapshots: JsonSnapshotCache,
        private readonly registry: MapSchemaRegistry
    ) {}

    get(document: vscode.TextDocument): DocumentIndex | undefined {
        const snapshot: JsonSnapshot = this.snapshots.get(document);
        const key = snapshot.uri;
        const cached = this.entries.get(key);
        if (cached && cached.version === snapshot.version) {
            return cached.index;
        }
        const schemaDocument = this.registry.forDocument(document);
        if (!schemaDocument) {
            return undefined;
        }
        const index: DocumentIndex = { sheets: new Map(), dictionaries: new Map() };
        walk(snapshot.value, schemaDocument.root, undefined, schemaDocument, index);
        this.entries.set(key, { version: snapshot.version, index });
        if (this.entries.size > 12) {
            for (const entryKey of this.entries.keys()) {
                if (entryKey !== key) {
                    this.entries.delete(entryKey);
                    break;
                }
            }
        }
        return index;
    }

    dispose(): void {
        this.entries.clear();
    }
}

export function findRecord(sheet: SheetIndex, identity: string): SheetRecord | undefined {
    const lowered = identity.toLowerCase();
    for (const record of sheet.records) {
        if (record.identity.toLowerCase() === lowered) {
            return record;
        }
    }
    return undefined;
}

export function dictionaryKey(dictionary: string[], candidate: string): string | undefined {
    const lowered = candidate.toLowerCase();
    for (const key of dictionary) {
        if (key.toLowerCase() === lowered) {
            return key;
        }
    }
    return undefined;
}
