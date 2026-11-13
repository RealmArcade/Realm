import * as vscode from 'vscode';
import { Node } from 'jsonc-parser';
import { AssetDeclaration } from './schemaKeywords';
import { assetKindOfPath } from './schemaKeywords';
import { CursorLocation, JsonSnapshotCache, nodeRange, objectPropertyNames, resolveCursorLocation } from './jsonDocument';
import { MapSchemaRegistry } from './schemaRegistry';
import { DocumentIndexCache } from './documentIndex';
import { MapAssetIndex, mapFolderOfDocument } from './assetIndex';

const maximumAssetSuggestions = 1000;

function escapeForStringContent(value: string): string {
    return value.replace(/\\/g, '\\\\').replace(/"/g, '\\"').replace(/\r?\n/g, '\\n');
}

function normalizedPath(value: string): string {
    return value.replace(/\\/g, '/');
}

export class SchemaCompletionProvider implements vscode.CompletionItemProvider {
    constructor(
        private readonly snapshots: JsonSnapshotCache,
        private readonly registry: MapSchemaRegistry,
        private readonly documentIndex: DocumentIndexCache,
        private readonly assets: MapAssetIndex
    ) {}

    provideCompletionItems(
        document: vscode.TextDocument,
        position: vscode.Position,
        token: vscode.CancellationToken
    ): vscode.CompletionList | undefined {
        const location = resolveCursorLocation(document, position, this.snapshots, this.registry);
        if (!location || token.isCancellationRequested) {
            return undefined;
        }

        try {
            const atKey = location.onKey
                || (location.containerNode?.type === 'object' && location.valueNode === location.containerNode);
            const items = atKey
                ? this.keyCompletions(document, location)
                : this.valueCompletions(document, location);
            if (items.length === 0) {
                return undefined;
            }
            return new vscode.CompletionList(items, items.length >= maximumAssetSuggestions);
        } catch {
            return undefined;
        }
    }

    private keyCompletions(document: vscode.TextDocument, location: CursorLocation): vscode.CompletionItem[] {
        const schemaDocument = location.schemaDocument;
        const containerSchema = location.containerSchema;
        if (!schemaDocument.isClosedObject(containerSchema)) {
            return [];
        }
        const properties = schemaDocument.properties(containerSchema);
        if (!properties) {
            return [];
        }

        const present = new Set(objectPropertyNames(location.containerNode));
        if (location.keyName !== undefined) {
            present.delete(location.keyName);
        }
        const cursor = document.positionAt(location.offset);
        const replacementRange = location.keyNode ? nodeRange(document, location.keyNode) : new vscode.Range(cursor, cursor);

        const items: vscode.CompletionItem[] = [];
        Object.keys(properties).forEach((name, index) => {
            if (present.has(name)) {
                return;
            }
            const propertySchema = schemaDocument.property(containerSchema, name);
            const item = new vscode.CompletionItem(name, vscode.CompletionItemKind.Property);
            item.range = replacementRange;
            item.insertText = `${JSON.stringify(name)}: `;
            item.filterText = name;
            item.sortText = index.toString().padStart(4, '0');
            const title = schemaDocument.title(propertySchema);
            const typeLabel = schemaDocument.typeLabel(propertySchema);
            item.detail = [title && title !== name ? title : undefined, typeLabel].filter(Boolean).join(' · ');
            const description = schemaDocument.description(propertySchema);
            if (description) {
                item.documentation = new vscode.MarkdownString(description);
            }
            items.push(item);
        });
        return items;
    }

    private valueCompletions(document: vscode.TextDocument, location: CursorLocation): vscode.CompletionItem[] {
        const schemaDocument = location.schemaDocument;
        const valueSchema = location.valueSchema;
        if (!valueSchema) {
            return [];
        }

        const items: vscode.CompletionItem[] = [];
        const description = schemaDocument.description(valueSchema);

        for (const reference of schemaDocument.references(valueSchema)) {
            items.push(...this.referenceItems(location, reference.sheets, reference.dictionary));
        }

        const enumValues = schemaDocument.enumValues(valueSchema);
        if (enumValues) {
            const enumDescriptions = schemaDocument.enumDescriptions(valueSchema);
            enumValues.forEach((value, index) => {
                const item = this.textItem(location, value);
                item.kind = vscode.CompletionItemKind.EnumMember;
                item.sortText = `1${index.toString().padStart(4, '0')}`;
                const enumDescription = enumDescriptions[index];
                if (enumDescription && enumDescription.length > 0) {
                    item.detail = enumDescription;
                    item.documentation = new vscode.MarkdownString(enumDescription);
                } else if (description) {
                    item.documentation = new vscode.MarkdownString(description);
                }
                items.push(item);
            });
        }

        const unit = schemaDocument.unit(valueSchema);
        const labels = schemaDocument.labels(valueSchema);
        if (labels) {
            for (const label of labels) {
                const item = this.numberItem(document, location, label.value);
                item.label = label.label;
                item.filterText = `${label.label} ${label.value}`;
                item.detail = unit ? `${label.value} ${unit}` : `${label.value}`;
                item.sortText = label.value.toString().padStart(8, '0');
                if (description) {
                    item.documentation = new vscode.MarkdownString(description);
                }
                items.push(item);
            }
        }

        const flags = schemaDocument.flags(valueSchema);
        if (flags && !labels) {
            for (const flag of flags) {
                const item = this.numberItem(document, location, flag.value);
                item.label = flag.label;
                item.filterText = `${flag.label} ${flag.value}`;
                item.detail = `${flag.value}`;
                items.push(item);
            }
        }

        const asset = schemaDocument.asset(valueSchema);
        if (asset) {
            items.push(...this.assetItems(document, location, asset));
        }

        return items;
    }

    private referenceItems(
        location: CursorLocation,
        sheets: string[] | undefined,
        dictionary: string | undefined
    ): vscode.CompletionItem[] {
        const index = this.documentIndex.get(location.document);
        if (!index) {
            return [];
        }

        const items: vscode.CompletionItem[] = [];
        for (const sheetName of sheets ?? []) {
            const sheet = index.sheets.get(sheetName);
            if (!sheet) {
                continue;
            }
            for (const record of sheet.records) {
                const item = new vscode.CompletionItem(record.identity, vscode.CompletionItemKind.Reference);
                item.range = this.stringContentRange(location);
                item.insertText = escapeForStringContent(record.identity);
                item.filterText = record.identity;
                item.detail = `${sheetName} · ${sheet.identityKey}`;
                items.push(item);
            }
        }

        if (dictionary !== undefined) {
            for (const key of index.dictionaries.get(dictionary) ?? []) {
                const item = new vscode.CompletionItem(key, vscode.CompletionItemKind.Value);
                item.range = this.stringContentRange(location);
                item.insertText = escapeForStringContent(key);
                item.filterText = key;
                item.detail = dictionary;
                items.push(item);
            }
        }

        return items;
    }

    private assetItems(document: vscode.TextDocument, location: CursorLocation, asset: AssetDeclaration): vscode.CompletionItem[] {
        const mapFolder = mapFolderOfDocument(document);
        if (!mapFolder) {
            return [];
        }

        const typed = typeof location.valueNode?.value === 'string' ? normalizedPath(location.valueNode.value) : '';
        const lastSeparator = typed.lastIndexOf('/');
        const typedFolder = lastSeparator >= 0 ? typed.slice(0, lastSeparator + 1) : '';

        const offerable = new Set<string>();
        for (const folder of asset.folders) {
            const normalizedFolder = normalizedPath(folder).replace(/^\.\/?/, '').replace(/\/+$/, '');
            if (normalizedFolder.length > 0) {
                offerable.add(`${normalizedFolder}/`);
            }
            for (const subFolder of this.assets.foldersUnderFolder(mapFolder, folder)) {
                offerable.add(subFolder);
            }
            for (const file of this.assets.filesUnderFolder(mapFolder, folder, asset.kinds)) {
                offerable.add(file);
                const lastSlash = file.lastIndexOf('/');
                const parent = lastSlash >= 0 ? file.slice(0, lastSlash) : '';
                if (parent.toLowerCase() === normalizedFolder.toLowerCase()) {
                    offerable.add(file.slice(lastSlash + 1));
                }
            }
        }

        const items: vscode.CompletionItem[] = [];
        for (const candidate of [...offerable].sort((left, right) => left.localeCompare(right))) {
            if (!candidate.toLowerCase().startsWith(typedFolder.toLowerCase())) {
                continue;
            }
            const remainder = candidate.slice(typedFolder.length);
            if (remainder.length === 0) {
                continue;
            }
            const isFolder = candidate.endsWith('/');
            if (!isFolder && remainder.includes('/')) {
                continue;
            }
            const name = remainder;
            const item = new vscode.CompletionItem(name, isFolder ? vscode.CompletionItemKind.Folder : vscode.CompletionItemKind.File);
            item.range = this.stringContentRange(location);
            item.insertText = escapeForStringContent(candidate);
            item.filterText = name;
            item.sortText = `${isFolder ? '0' : '1'}${name}`;
            const kind = assetKindOfPath(candidate);
            item.detail = kind ? `${kind} · ${candidate}` : candidate;
            items.push(item);
            if (items.length >= maximumAssetSuggestions) {
                break;
            }
        }

        const description = location.schemaDocument.description(location.valueSchema);
        if (description) {
            for (const item of items) {
                item.documentation = new vscode.MarkdownString(description);
            }
        }
        return items;
    }

    private stringContentRange(location: CursorLocation): vscode.Range {
        const document = location.document;
        const node: Node | undefined = location.valueNode;
        if (node && node.type === 'string') {
            const start = document.positionAt(node.offset + 1);
            const end = document.positionAt(node.offset + node.length - 1);
            if (end.isAfterOrEqual(start)) {
                return new vscode.Range(start, end);
            }
        }
        if (node) {
            return nodeRange(document, node);
        }
        const cursor = document.positionAt(location.offset);
        return new vscode.Range(cursor, cursor);
    }

    private textItem(location: CursorLocation, value: string): vscode.CompletionItem {
        const item = new vscode.CompletionItem(value, vscode.CompletionItemKind.Value);
        item.range = this.stringContentRange(location);
        item.insertText = escapeForStringContent(value);
        item.filterText = value;
        return item;
    }

    private numberItem(document: vscode.TextDocument, location: CursorLocation, value: number): vscode.CompletionItem {
        const item = new vscode.CompletionItem(String(value), vscode.CompletionItemKind.Value);
        const node: Node | undefined = location.valueNode;
        item.range = node && node.type === 'number'
            ? nodeRange(document, node)
            : new vscode.Range(document.positionAt(location.offset), document.positionAt(location.offset));
        item.insertText = String(value);
        item.filterText = String(value);
        return item;
    }
}
