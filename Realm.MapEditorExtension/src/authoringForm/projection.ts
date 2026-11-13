import * as vscode from 'vscode';
import { SchemaDocument, JsonSchema } from '../authoring/schemaKeywords';
import { JsonSnapshot, JsonSnapshotCache, pathLabel } from '../authoring/jsonDocument';
import { DocumentIndex, DocumentIndexCache, detectIdentityKey, isPlainObject } from '../authoring/documentIndex';
import { MapAssetIndex, mapFolderOfDocument } from '../authoring/assetIndex';
import { MapSchemaRegistry } from '../authoring/schemaRegistry';
import { ChildrenPage, ContainerNode, FormNode, LeafNode, PathSegments, SelectOption } from './formTypes';

const maximumSuggestions = 300;
const defaultPageSize = 40;

export interface ProjectionContext {
    uri: vscode.Uri;
    version: number;
    snapshot: JsonSnapshot;
    schemaDocument: SchemaDocument;
    index: DocumentIndex | undefined;
    mapFolder: string | undefined;
    assets: MapAssetIndex;
}

export function valueAtPath(root: unknown, path: PathSegments): unknown {
    let current: unknown = root;
    for (const segment of path) {
        if (Array.isArray(current)) {
            const index = typeof segment === 'number' ? segment : Number(segment);
            current = Number.isInteger(index) ? current[index] : undefined;
            continue;
        }
        if (!isPlainObject(current)) {
            return undefined;
        }
        current = current[segment as string];
    }
    return current;
}

export function schemaAt(context: ProjectionContext, path: PathSegments): JsonSchema | undefined {
    let current: JsonSchema | undefined = context.schemaDocument.root;
    for (const segment of path) {
        if (!current) {
            return undefined;
        }
        current = typeof segment === 'number'
            ? context.schemaDocument.items(current)
            : context.schemaDocument.property(current, segment);
    }
    return current;
}

export function createProjectionContext(
    document: vscode.TextDocument,
    snapshots: JsonSnapshotCache,
    registry: MapSchemaRegistry,
    documentIndex: DocumentIndexCache,
    assets: MapAssetIndex
): ProjectionContext | undefined {
    const schemaDocument = registry.forDocument(document);
    if (!schemaDocument) {
        return undefined;
    }
    const snapshot = snapshots.get(document);
    return {
        uri: document.uri,
        version: document.version,
        snapshot,
        schemaDocument,
        index: documentIndex.get(document),
        mapFolder: mapFolderOfDocument(document),
        assets
    };
}

function titleOf(context: ProjectionContext, schema: JsonSchema | undefined, key: string | undefined): string {
    const title = context.schemaDocument.title(schema);
    if (title) {
        return title;
    }
    return key && key.length > 0 ? key : pathLabel([]);
}

function stringsOfSuggestions(values: string[]): string[] {
    return values.length > maximumSuggestions ? values.slice(0, maximumSuggestions) : values;
}

function referenceSuggestions(context: ProjectionContext, schema: JsonSchema | undefined): string[] {
    const suggestions: string[] = [];
    for (const reference of context.schemaDocument.references(schema)) {
        if (reference.sheets) {
            for (const sheetName of reference.sheets) {
                const sheet = context.index?.sheets.get(sheetName);
                for (const record of sheet?.records ?? []) {
                    if (!suggestions.includes(record.identity)) {
                        suggestions.push(record.identity);
                    }
                }
            }
            continue;
        }
        if (reference.dictionary !== undefined) {
            for (const key of context.index?.dictionaries.get(reference.dictionary) ?? []) {
                if (!suggestions.includes(key)) {
                    suggestions.push(key);
                }
            }
        }
    }
    return stringsOfSuggestions(suggestions);
}

function assetSuggestions(context: ProjectionContext, schema: JsonSchema | undefined): string[] {
    const asset = context.schemaDocument.asset(schema);
    if (!asset || !context.mapFolder) {
        return [];
    }
    const suggestions: string[] = [];
    for (const folder of asset.folders) {
        const normalizedFolder = folder.replace(/\\/g, '/').replace(/^\.\/?/, '').replace(/\/+$/, '');
        if (normalizedFolder.length > 0) {
            suggestions.push(`${normalizedFolder}/`);
        }
        for (const subFolder of context.assets.foldersUnderFolder(context.mapFolder, folder)) {
            suggestions.push(subFolder);
        }
        for (const file of context.assets.filesUnderFolder(context.mapFolder, folder, asset.kinds)) {
            suggestions.push(file);
            const lastSlash = file.lastIndexOf('/');
            if (lastSlash >= 0) {
                suggestions.push(file.slice(lastSlash + 1));
            }
        }
    }
    const unique = [...new Set(suggestions)].sort((left, right) => left.localeCompare(right));
    return stringsOfSuggestions(unique);
}

function enumOptions(context: ProjectionContext, schema: JsonSchema | undefined): SelectOption[] | undefined {
    const values = context.schemaDocument.enumValues(schema);
    if (!values) {
        return undefined;
    }
    const descriptions = context.schemaDocument.enumDescriptions(schema);
    return values.map((value, index) => ({
        value,
        label: value,
        description: descriptions[index] && descriptions[index].length > 0 ? descriptions[index] : undefined
    }));
}

function numericLabelOptions(context: ProjectionContext, schema: JsonSchema | undefined): SelectOption[] | undefined {
    const labels = context.schemaDocument.labels(schema);
    if (!labels) {
        return undefined;
    }
    return labels.map(label => ({ value: label.value, label: label.label }));
}

function sliderStep(minimum: number | undefined, maximum: number | undefined, integer: boolean): number | undefined {
    if (integer) {
        return 1;
    }
    if (minimum === undefined || maximum === undefined || maximum <= minimum) {
        return undefined;
    }
    const raw = (maximum - minimum) / 100;
    const rounded = Number(raw.toPrecision(2));
    return rounded > 0 ? rounded : undefined;
}

export function describeLeaf(
    context: ProjectionContext,
    path: PathSegments,
    key: string | undefined,
    schema: JsonSchema | undefined,
    semanticSchema: JsonSchema | undefined,
    value: unknown,
    present: boolean
): LeafNode {
    const schemaDocument = context.schemaDocument;
    const widgetSource = semanticSchema ?? schema;
    const types = schemaDocument.types(schema);
    const unit = schemaDocument.unit(widgetSource) ?? schemaDocument.unit(schema);
    const derived = schemaDocument.derivedSource(schema) !== undefined;
    const sensitive = schemaDocument.sensitivity(schema);
    const control = schemaDocument.control(widgetSource) ?? schemaDocument.control(schema);

    const base: LeafNode = {
        kind: 'leaf',
        path,
        key,
        label: titleOf(context, schema, key),
        description: schemaDocument.description(schema) ?? schemaDocument.description(widgetSource),
        widget: 'readonly',
        unit,
        value: value === undefined && !present ? undefined : value,
        present,
        nullable: types.includes('null'),
        readOnly: derived,
        monospace: sensitive
    };

    if (derived) {
        return base;
    }

    const asset = schemaDocument.asset(widgetSource) ?? schemaDocument.asset(schema);
    if (asset) {
        const assetState = typeof value === 'string' && context.mapFolder
            ? context.assets.resolve(context.mapFolder, asset.folders, value).state
            : undefined;
        return {
            ...base,
            widget: 'asset',
            suggestions: assetSuggestions(context, widgetSource),
            assetState
        };
    }

    const references = schemaDocument.references(widgetSource);
    if (references.length > 0) {
        return { ...base, widget: 'reference', suggestions: referenceSuggestions(context, widgetSource) };
    }

    const options = enumOptions(context, schema) ?? enumOptions(context, widgetSource);
    if (options) {
        return { ...base, widget: 'select', options, monospace: false };
    }

    const labels = numericLabelOptions(context, schema) ?? numericLabelOptions(context, widgetSource);
    if (labels) {
        return { ...base, widget: 'select', options: labels, monospace: false };
    }

    const flags = schemaDocument.flags(widgetSource) ?? schemaDocument.flags(schema);
    if (flags) {
        return { ...base, widget: 'flags', flags: flags.map(flag => ({ bit: flag.value, label: flag.label })) };
    }

    if (control === 'color') {
        return { ...base, widget: 'color', monospace: true };
    }

    if (control === 'tags') {
        return { ...base, widget: 'tags', monospace: false };
    }

    if (control === 'textarea' || control === 'json') {
        return { ...base, widget: 'textarea', multiline: true, monospace: control === 'json' };
    }

    const minimum = schemaDocument.minimum(schema);
    const maximum = schemaDocument.maximum(schema);

    if (control === 'slider' && minimum !== undefined && maximum !== undefined) {
        return {
            ...base,
            widget: 'slider',
            minimum,
            maximum,
            step: sliderStep(minimum, maximum, types.includes('integer'))
        };
    }

    if (types.includes('boolean')) {
        return { ...base, widget: 'checkbox', monospace: false };
    }

    if (types.includes('integer') || types.includes('number')) {
        if (minimum !== undefined && maximum !== undefined) {
            return {
                ...base,
                widget: 'slider',
                minimum,
                maximum,
                step: sliderStep(minimum, maximum, types.includes('integer'))
            };
        }
        return { ...base, widget: 'number', minimum, maximum, monospace: true };
    }

    if (types.includes('string')) {
        return { ...base, widget: 'text' };
    }

    if (types.includes('array')) {
        return { ...base, widget: 'tags' };
    }

    return schemaDocument.resolve(schema) ? { ...base, widget: 'text', monospace: false } : base;
}

function isContainerValue(value: unknown): boolean {
    return Array.isArray(value) || isPlainObject(value);
}

function orderPropertyNames(context: ProjectionContext, schema: JsonSchema | undefined, present: string[]): { ordered: string[]; missing: string[] } {
    const declared = context.schemaDocument.propertyNames(schema);
    const ordered = declared.filter(name => present.includes(name));
    const unknown = present.filter(name => !declared.includes(name)).sort((left, right) => (left < right ? -1 : left > right ? 1 : 0));
    const missing = context.schemaDocument.isClosedObject(schema)
        ? declared.filter(name => !present.includes(name))
        : [];
    return { ordered: [...ordered, ...unknown], missing };
}

function nodeForProperty(
    context: ProjectionContext,
    parentPath: PathSegments,
    key: string,
    schema: JsonSchema | undefined,
    value: unknown,
    present: boolean
): FormNode {
    const path = [...parentPath, key];
    if (!present) {
        return describeLeaf(context, path, key, schema, undefined, undefined, false);
    }

    if (Array.isArray(value)) {
        const itemSchema = context.schemaDocument.items(schema);
        const recordIdentity = arrayIdentity(context, value, schema);
        return {
            kind: 'list',
            path,
            key,
            label: titleOf(context, schema, key),
            description: context.schemaDocument.description(schema),
            childCount: value.length,
            identity: recordIdentity?.key,
            dictionary: isScalarList(context, value, itemSchema)
        };
    }

    if (isPlainObject(value)) {
        const isDictionary = context.schemaDocument.entryValueSchema(schema) !== undefined
            && context.schemaDocument.properties(schema) === undefined;
        return {
            kind: 'group',
            path,
            key,
            label: titleOf(context, schema, key),
            description: context.schemaDocument.description(schema),
            childCount: Object.keys(value).length,
            dictionary: isDictionary
        };
    }

    return describeLeaf(context, path, key, schema, undefined, value, true);
}

function isScalarList(context: ProjectionContext, entries: unknown[], itemSchema: JsonSchema | undefined): boolean {
    if (entries.length === 0) {
        return context.schemaDocument.types(itemSchema).includes('string')
            || context.schemaDocument.types(itemSchema).includes('number');
    }
    return entries.every(entry => !isContainerValue(entry));
}

function arrayIdentity(
    context: ProjectionContext,
    entries: unknown[],
    schema: JsonSchema | undefined
): { key: string; identityOf: (entry: unknown) => string | undefined } | undefined {
    const records = entries.filter(isPlainObject);
    if (records.length === 0 || records.length !== entries.length) {
        return undefined;
    }
    const itemSchema = context.schemaDocument.items(schema);
    const key = detectIdentityKey(records, context.schemaDocument, itemSchema);
    if (!key) {
        return undefined;
    }
    return {
        key,
        identityOf: entry => (isPlainObject(entry) && typeof entry[key] === 'string' ? entry[key] as string : undefined)
    };
}

export function buildSections(context: ProjectionContext): FormNode[] {
    const root = context.snapshot.value;
    if (!isPlainObject(root)) {
        return [];
    }
    const present = Object.keys(root);
    const { ordered, missing } = orderPropertyNames(context, context.schemaDocument.root, present);
    const nodes: FormNode[] = [];

    for (const key of ordered) {
        const schema = context.schemaDocument.property(context.schemaDocument.root, key);
        const node = nodeForProperty(context, [], key, schema, root[key], true);
        nodes.push(node.kind === 'leaf' ? node : { ...node, kind: 'section' });
    }

    for (const key of missing) {
        const schema = context.schemaDocument.property(context.schemaDocument.root, key);
        nodes.push(describeLeaf(context, [key], key, schema, undefined, undefined, false));
    }

    return nodes;
}

export function buildChildren(
    context: ProjectionContext,
    path: PathSegments,
    offset: number,
    limit: number
): ChildrenPage {
    const pageLimit = limit > 0 ? limit : defaultPageSize;
    const schema = schemaAt(context, path);
    const value = valueAtPath(context.snapshot.value, path);

    if (Array.isArray(value)) {
        const itemSchema = context.schemaDocument.items(schema);
        const identity = arrayIdentity(context, value, schema);
        const start = Math.max(0, offset);
        const end = Math.min(value.length, start + pageLimit);
        const nodes: FormNode[] = [];

        for (let index = start; index < end; index++) {
            const entry = value[index];
            const entryPath = [...path, index];
            const label = identity?.identityOf(entry);
            if (isContainerValue(entry)) {
                nodes.push({
                    kind: 'record',
                    path: entryPath,
                    label: label ?? `[${index}]`,
                    identity: label,
                    childCount: Array.isArray(entry) ? entry.length : Object.keys(entry as object).length,
                    dictionary: false
                });
                continue;
            }
            nodes.push(describeLeaf(context, entryPath, undefined, itemSchema, schema, entry, true));
        }

        return { nodes, offset: start, total: value.length };
    }

    if (isPlainObject(value)) {
        const isDictionary = context.schemaDocument.entryValueSchema(schema) !== undefined
            && context.schemaDocument.properties(schema) === undefined;

        if (isDictionary) {
            const keys = Object.keys(value).sort((left, right) => (left < right ? -1 : left > right ? 1 : 0));
            const start = Math.max(0, offset);
            const end = Math.min(keys.length, start + pageLimit);
            const entrySchema = context.schemaDocument.entryValueSchema(schema);
            const nodes: FormNode[] = [];
            for (let index = start; index < end; index++) {
                const key = keys[index];
                const entry = value[key];
                const entryPath = [...path, key];
                if (isContainerValue(entry)) {
                    nodes.push({
                        kind: 'entry',
                        path: entryPath,
                        key,
                        label: key,
                        childCount: Array.isArray(entry) ? entry.length : Object.keys(entry as object).length,
                        dictionary: false
                    });
                    continue;
                }
                nodes.push(describeLeaf(context, entryPath, key, entrySchema, undefined, entry, true));
            }
            return { nodes, offset: start, total: keys.length };
        }

        const present = Object.keys(value);
        const { ordered, missing } = orderPropertyNames(context, schema, present);
        const all: FormNode[] = [];
        for (const key of ordered) {
            all.push(nodeForProperty(context, path, key, context.schemaDocument.property(schema, key), value[key], true));
        }
        for (const key of missing) {
            all.push(describeLeaf(context, [...path, key], key, context.schemaDocument.property(schema, key), undefined, undefined, false));
        }

        const start = Math.max(0, offset);
        return { nodes: all.slice(start, start + pageLimit), offset: start, total: all.length };
    }

    return { nodes: [], offset: 0, total: 0 };
}

export function projectionState(context: ProjectionContext): { state: 'ok' | 'unparsable'; message?: string } {
    if (context.snapshot.errors.length > 0) {
        const first = context.snapshot.errors[0];
        return {
            state: 'unparsable',
            message: `The document does not parse at offset ${first.offset}, so the form is read-only until it does.`
        };
    }
    if (!isPlainObject(context.snapshot.value)) {
        return { state: 'unparsable', message: 'The document is not a JSON object yet, so the form is read-only.' };
    }
    return { state: 'ok' };
}
