export type JsonSchema = { [keyword: string]: any };

export type JsonPath = (string | number)[];

export interface NumericLabel {
    value: number;
    label: string;
}

export interface AssetDeclaration {
    kinds: string[];
    folders: string[];
}

export interface ReferenceDeclaration {
    sheets?: string[];
    dictionary?: string;
}

const referenceKeyword = 'x-realm-reference';
const assetKeyword = 'x-realm-asset';
const controlKeyword = 'x-realm-control';
const unitKeyword = 'x-realm-unit';
const flagsKeyword = 'x-realm-flags';
const enumLabelsKeyword = 'x-realm-enum-labels';

function isObject(value: unknown): value is JsonSchema {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function stringArray(value: unknown): string[] {
    if (!Array.isArray(value)) {
        return [];
    }
    return value.filter((entry): entry is string => typeof entry === 'string');
}

function numericLabels(value: unknown): NumericLabel[] | undefined {
    if (!Array.isArray(value)) {
        return undefined;
    }
    const labels: NumericLabel[] = [];
    for (const entry of value) {
        if (!isObject(entry) || typeof entry.value !== 'number' || typeof entry.label !== 'string') {
            continue;
        }
        labels.push({ value: entry.value, label: entry.label });
    }
    return labels.length > 0 ? labels : undefined;
}

export function readJsonPointer(root: unknown, pointer: string): unknown {
    if (pointer.length === 0) {
        return root;
    }
    if (!pointer.startsWith('/')) {
        return undefined;
    }
    let current: unknown = root;
    for (const rawSegment of pointer.slice(1).split('/')) {
        const segment = rawSegment.replace(/~1/g, '/').replace(/~0/g, '~');
        if (Array.isArray(current)) {
            const index = Number(segment);
            if (!Number.isInteger(index) || index < 0 || index >= current.length) {
                return undefined;
            }
            current = current[index];
            continue;
        }
        if (!isObject(current)) {
            return undefined;
        }
        current = current[segment];
    }
    return current;
}

export class SchemaDocument {
    constructor(readonly root: JsonSchema) {}

    resolve(node: JsonSchema | undefined): JsonSchema | undefined {
        let current = node;
        for (let depth = 0; current && depth < 32; depth++) {
            const reference = current.$ref;
            if (typeof reference !== 'string' || !reference.startsWith('#')) {
                return current;
            }
            const target = readJsonPointer(this.root, reference.slice(1));
            if (!isObject(target)) {
                return undefined;
            }
            current = target;
        }
        return current;
    }

    properties(node: JsonSchema | undefined): { [name: string]: JsonSchema } | undefined {
        const resolved = this.resolve(node);
        const properties = resolved?.properties;
        return isObject(properties) ? (properties as { [name: string]: JsonSchema }) : undefined;
    }

    propertyNames(node: JsonSchema | undefined): string[] {
        const properties = this.properties(node);
        return properties ? Object.keys(properties) : [];
    }

    property(node: JsonSchema | undefined, name: string): JsonSchema | undefined {
        const properties = this.properties(node);
        if (properties && Object.prototype.hasOwnProperty.call(properties, name)) {
            return this.resolve(properties[name]);
        }
        return this.entryValueSchema(node);
    }

    items(node: JsonSchema | undefined): JsonSchema | undefined {
        const resolved = this.resolve(node);
        if (!resolved) {
            return undefined;
        }
        if (Array.isArray(resolved.items)) {
            return undefined;
        }
        return this.resolve(resolved.items);
    }

    entryValueSchema(node: JsonSchema | undefined): JsonSchema | undefined {
        const resolved = this.resolve(node);
        if (!resolved) {
            return undefined;
        }
        const additional = resolved.additionalProperties;
        return isObject(additional) ? this.resolve(additional) : undefined;
    }

    isClosedObject(node: JsonSchema | undefined): boolean {
        const resolved = this.resolve(node);
        if (!resolved || resolved.additionalProperties !== false) {
            return false;
        }
        return this.properties(resolved) !== undefined;
    }

    types(node: JsonSchema | undefined): string[] {
        const resolved = this.resolve(node);
        const type = resolved?.type;
        if (typeof type === 'string') {
            return [type];
        }
        return stringArray(type);
    }

    typeLabel(node: JsonSchema | undefined): string | undefined {
        const types = this.types(node).filter(type => type !== 'null');
        return types.length > 0 ? types.join(' | ') : undefined;
    }

    title(node: JsonSchema | undefined): string | undefined {
        const value = this.resolve(node)?.title;
        return typeof value === 'string' ? value : undefined;
    }

    description(node: JsonSchema | undefined): string | undefined {
        const value = this.resolve(node)?.description;
        return typeof value === 'string' ? value : undefined;
    }

    enumValues(node: JsonSchema | undefined): string[] | undefined {
        const resolved = this.resolve(node);
        if (!resolved || !Array.isArray(resolved.enum)) {
            return undefined;
        }
        const values = resolved.enum.filter((entry: unknown): entry is string => typeof entry === 'string');
        return values.length > 0 ? values : undefined;
    }

    enumDescriptions(node: JsonSchema | undefined): string[] {
        const resolved = this.resolve(node);
        if (!resolved || !Array.isArray(resolved.enumDescriptions)) {
            return [];
        }
        return resolved.enumDescriptions.map((entry: unknown) => (typeof entry === 'string' ? entry : ''));
    }

    labels(node: JsonSchema | undefined, keyword: string = enumLabelsKeyword): NumericLabel[] | undefined {
        return numericLabels(this.resolve(node)?.[keyword]);
    }

    flags(node: JsonSchema | undefined): NumericLabel[] | undefined {
        return this.labels(node, flagsKeyword);
    }

    unit(node: JsonSchema | undefined): string | undefined {
        const value = this.resolve(node)?.[unitKeyword];
        return typeof value === 'string' ? value : undefined;
    }

    control(node: JsonSchema | undefined): string | undefined {
        const value = this.resolve(node)?.[controlKeyword];
        return typeof value === 'string' ? value : undefined;
    }

    asset(node: JsonSchema | undefined): AssetDeclaration | undefined {
        const declaration = this.resolve(node)?.[assetKeyword];
        if (!isObject(declaration)) {
            return undefined;
        }
        const kinds = stringArray(declaration.kinds);
        const folders = stringArray(declaration.folders);
        if (kinds.length === 0) {
            return undefined;
        }
        return { kinds, folders: folders.length > 0 ? folders : ['.'] };
    }

    references(node: JsonSchema | undefined): ReferenceDeclaration[] {
        const declaration = this.resolve(node)?.[referenceKeyword];
        if (!isObject(declaration)) {
            return [];
        }
        const candidates: JsonSchema[] = Array.isArray(declaration.anyOf)
            ? declaration.anyOf.filter((entry: unknown): entry is JsonSchema => isObject(entry))
            : [declaration];
        const references: ReferenceDeclaration[] = [];
        for (const candidate of candidates) {
            const sheets = stringArray(candidate.sheets);
            if (sheets.length > 0) {
                references.push({ sheets });
                continue;
            }
            if (typeof candidate.dictionary === 'string') {
                references.push({ dictionary: candidate.dictionary });
            }
        }
        return references;
    }

    referenceSheets(node: JsonSchema | undefined): string[] {
        const sheets: string[] = [];
        for (const reference of this.references(node)) {
            for (const sheet of reference.sheets ?? []) {
                if (!sheets.includes(sheet)) {
                    sheets.push(sheet);
                }
            }
        }
        return sheets;
    }

    minimum(node: JsonSchema | undefined): number | undefined {
        const value = this.resolve(node)?.minimum;
        return typeof value === 'number' ? value : undefined;
    }

    maximum(node: JsonSchema | undefined): number | undefined {
        const value = this.resolve(node)?.maximum;
        return typeof value === 'number' ? value : undefined;
    }

    sensitivity(node: JsonSchema | undefined): boolean {
        return this.resolve(node)?.['x-realm-sensitive'] === true;
    }

    schemaVersion(): number | undefined {
        const value = this.root['x-realm-schema-version'];
        return typeof value === 'number' ? value : undefined;
    }
}

export function numericBitLabels(value: number, flags: NumericLabel[]): string[] {
    const labels: string[] = [];
    for (const flag of flags) {
        if (flag.value !== 0 && (value & flag.value) === flag.value) {
            labels.push(flag.label);
        }
    }
    return labels;
}

export function describeEnumLabel(value: number, labels: NumericLabel[]): string | undefined {
    for (const label of labels) {
        if (label.value === value) {
            return label.label;
        }
    }
    return undefined;
}

export function fileNameFromAssetPath(assetPath: string): string {
    const normalized = assetPath.replace(/\\/g, '/');
    const lastSeparator = normalized.lastIndexOf('/');
    return lastSeparator >= 0 ? normalized.slice(lastSeparator + 1) : normalized;
}

export function assetKindOfPath(assetPath: string): string | undefined {
    const fileName = fileNameFromAssetPath(assetPath).toLowerCase();
    const lastDot = fileName.lastIndexOf('.');
    return lastDot > 0 ? fileName.slice(lastDot + 1) : undefined;
}
