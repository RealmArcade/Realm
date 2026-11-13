import * as vscode from 'vscode';
import { JsonSchema, SchemaDocument } from './schemaKeywords';
import { CursorLocation, JsonSnapshotCache, nodeRange, resolveCursorLocation } from './jsonDocument';
import { MapSchemaRegistry } from './schemaRegistry';
import { DocumentIndexCache, dictionaryKey, findRecord } from './documentIndex';
import { MapAssetIndex, mapFolderOfDocument } from './assetIndex';

function code(text: string): string {
    return `\`${text.replace(/`/g, "'")}\``;
}

export class SchemaHoverProvider implements vscode.HoverProvider {
    constructor(
        private readonly snapshots: JsonSnapshotCache,
        private readonly registry: MapSchemaRegistry,
        private readonly documentIndex: DocumentIndexCache,
        private readonly assets: MapAssetIndex,
        private readonly isEnabled: () => boolean
    ) {}

    provideHover(
        document: vscode.TextDocument,
        position: vscode.Position,
        token: vscode.CancellationToken
    ): vscode.Hover | undefined {
        if (!this.isEnabled() || token.isCancellationRequested) {
            return undefined;
        }
        const location = resolveCursorLocation(document, position, this.snapshots, this.registry);
        if (!location) {
            return undefined;
        }

        try {
            const contents = new vscode.MarkdownString();
            contents.isTrusted = false;
            const wroteSomething = location.onKey
                ? this.describeKey(location, contents)
                : this.describeValue(location, contents);
            if (!wroteSomething) {
                return undefined;
            }

            const node = location.onKey ? location.keyNode : location.valueNode;
            if (!node) {
                return undefined;
            }
            return new vscode.Hover(contents, nodeRange(document, node));
        } catch {
            return undefined;
        }
    }

    private describeKey(location: CursorLocation, contents: vscode.MarkdownString): boolean {
        const schemaDocument = location.schemaDocument;
        const schema = location.keySchema ?? location.valueSchema;
        if (!schema) {
            return false;
        }
        const name = location.keyName ?? '';
        const title = schemaDocument.title(schema);
        contents.appendMarkdown(`**${title ?? name}**`);
        const typeLabel = schemaDocument.typeLabel(schema);
        if (typeLabel) {
            contents.appendMarkdown(` · ${code(typeLabel)}`);
        }
        const unit = schemaDocument.unit(schema);
        if (unit) {
            contents.appendMarkdown(` · ${code(unit)}`);
        }
        contents.appendMarkdown('\n\n');
        const description = schemaDocument.description(schema);
        if (description) {
            contents.appendMarkdown(`${description}\n\n`);
        }
        this.appendDomain(schemaDocument, schema, contents);
        return true;
    }

    private describeValue(location: CursorLocation, contents: vscode.MarkdownString): boolean {
        const schemaDocument = location.schemaDocument;
        const schema = location.valueSchema;
        const node = location.valueNode;
        if (!schema || !node) {
            return false;
        }

        const name = location.keyName ?? schemaDocument.title(schema) ?? '';
        const value = node.value;
        const unit = schemaDocument.unit(schema);
        contents.appendMarkdown(`**${name}** · ${code(String(value))}${unit ? ` ${code(unit)}` : ''}\n\n`);

        let wroteSomething = true;
        const enumValues = schemaDocument.enumValues(schema);
        if (enumValues && typeof value === 'string') {
            const index = enumValues.indexOf(value);
            const enumDescription = index >= 0 ? schemaDocument.enumDescriptions(schema)[index] : undefined;
            if (enumDescription && enumDescription.length > 0) {
                contents.appendMarkdown(`${enumDescription}\n\n`);
            }
        }

        const labels = schemaDocument.labels(schema);
        if (labels && typeof value === 'number') {
            const match = labels.find(entry => entry.value === value);
            contents.appendMarkdown(match ? `${match.label}\n\n` : '');
        }

        const flags = schemaDocument.flags(schema);
        if (flags && typeof value === 'number') {
            const set = flags.filter(flag => flag.value !== 0 && (value & flag.value) === flag.value);
            const unnamed = value & ~flags.reduce((mask, flag) => mask | flag.value, 0);
            const entries = set.map(flag => `${flag.label} (${flag.value})`);
            if (unnamed !== 0) {
                entries.push(`unknown bits (${unnamed})`);
            }
            contents.appendMarkdown(entries.length > 0 ? `${entries.join(', ')}\n\n` : '');
        }

        for (const reference of schemaDocument.references(schema)) {
            wroteSomething = this.describeReference(location, reference.sheets, reference.dictionary, value, contents) || wroteSomething;
        }

        const asset = schemaDocument.asset(schema);
        if (asset && typeof value === 'string') {
            wroteSomething = this.describeAsset(location, asset.folders, value, contents) || wroteSomething;
        }

        return wroteSomething;
    }

    private describeReference(
        location: CursorLocation,
        sheets: string[] | undefined,
        dictionary: string | undefined,
        value: unknown,
        contents: vscode.MarkdownString
    ): boolean {
        if (typeof value !== 'string') {
            return false;
        }
        const index = this.documentIndex.get(location.document);
        if (!index) {
            return false;
        }

        if (sheets && sheets.length > 0) {
            const found: string[] = [];
            const searched: string[] = [];
            for (const sheetName of sheets) {
                const sheet = index.sheets.get(sheetName);
                if (!sheet) {
                    continue;
                }
                searched.push(sheetName);
                const record = findRecord(sheet, value);
                if (record) {
                    found.push(sheetName);
                }
            }
            if (found.length > 0) {
                contents.appendMarkdown(`Reference resolves to ${code(value)} in ${found.join(', ')}.\n\n`);
                return true;
            }
            if (searched.length > 0) {
                contents.appendMarkdown(`No record in ${searched.join(', ')} is named ${code(value)}.\n\n`);
                return true;
            }
            return false;
        }

        if (dictionary !== undefined) {
            const keys = index.dictionaries.get(dictionary);
            if (!keys) {
                return false;
            }
            const key = dictionaryKey(keys, value);
            contents.appendMarkdown(key
                ? `Names the ${code(key)} entry of ${code(dictionary)}.\n\n`
                : `No key of ${code(dictionary)} is named ${code(value)}.\n\n`);
            return true;
        }

        return false;
    }

    private describeAsset(
        location: CursorLocation,
        folders: string[],
        value: string,
        contents: vscode.MarkdownString
    ): boolean {
        const mapFolder = mapFolderOfDocument(location.document);
        if (!mapFolder) {
            return false;
        }
        const resolution = this.assets.resolve(mapFolder, folders, value);
        if (resolution.state === 'exists') {
            contents.appendMarkdown(`File exists: ${code(resolution.relativePath ?? value)}.\n\n`);
            return true;
        }
        if (resolution.state === 'missing') {
            contents.appendMarkdown(`No file named ${code(value)} under ${folders.map(code).join(', ')}.\n\n`);
            return true;
        }
        contents.appendMarkdown(`File existence unknown for ${code(value)}.\n\n`);
        return true;
    }

    private appendDomain(schemaDocument: SchemaDocument, schema: JsonSchema, contents: vscode.MarkdownString): void {
        const enumValues = schemaDocument.enumValues(schema);
        if (enumValues) {
            const descriptions = schemaDocument.enumDescriptions(schema);
            const lines = enumValues.map((value, index) => {
                const description = descriptions[index];
                return description && description.length > 0 ? `- ${code(value)} — ${description}` : `- ${code(value)}`;
            });
            contents.appendMarkdown(`Allowed values:\n${lines.join('\n')}\n\n`);
        }

        const labels = schemaDocument.labels(schema);
        if (labels) {
            contents.appendMarkdown(`Allowed values:\n${labels.map(label => `- ${code(String(label.value))} — ${label.label}`).join('\n')}\n\n`);
        }

        const flags = schemaDocument.flags(schema);
        if (flags) {
            contents.appendMarkdown(`Bits:\n${flags.map(flag => `- ${flag.value} — ${flag.label}`).join('\n')}\n\n`);
        }

        const minimum = schemaDocument.minimum(schema);
        const maximum = schemaDocument.maximum(schema);
        if (minimum !== undefined || maximum !== undefined) {
            contents.appendMarkdown(`Range: ${minimum ?? '−∞'} to ${maximum ?? '∞'}\n\n`);
        }

        for (const reference of schemaDocument.references(schema)) {
            if (reference.sheets) {
                contents.appendMarkdown(`Names a record from ${reference.sheets.join(', ')} in this document.\n\n`);
                continue;
            }
            if (reference.dictionary) {
                contents.appendMarkdown(`Names an entry of ${code(reference.dictionary)} in this document.\n\n`);
            }
        }

        const asset = schemaDocument.asset(schema);
        if (asset) {
            contents.appendMarkdown(`A file of kind ${asset.kinds.join(', ')} under ${asset.folders.map(code).join(', ')}.\n\n`);
        }
    }
}
