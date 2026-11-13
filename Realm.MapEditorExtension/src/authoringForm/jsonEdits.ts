import * as vscode from 'vscode';
import { Node, findNodeAtLocation, modify } from 'jsonc-parser';
import { JsonSchema, SchemaDocument } from '../authoring/schemaKeywords';
import { JsonSnapshot } from '../authoring/jsonDocument';
import { PathSegments } from './formTypes';

export interface EditDocument {
    document: vscode.TextDocument;
    snapshot: JsonSnapshot;
}

export function indentUnitOf(document: vscode.TextDocument): string {
    const lines = document.getText().split('\n', 80);
    for (const line of lines) {
        const match = /^([ \t]+)\S/.exec(line);
        if (match) {
            return match[1];
        }
    }
    return '  ';
}

function formattingOptionsOf(document: vscode.TextDocument): { insertSpaces: boolean; tabSize: number; eol: string } {
    const unit = indentUnitOf(document);
    return {
        insertSpaces: !unit.startsWith('\t'),
        tabSize: unit.startsWith('\t') ? 1 : Math.max(1, unit.length),
        eol: document.eol === vscode.EndOfLine.CRLF ? '\r\n' : '\n'
    };
}

function lineIndentation(document: vscode.TextDocument, offset: number): string {
    const line = document.lineAt(document.positionAt(offset));
    const match = /^[ \t]*/.exec(line.text);
    return match ? match[0] : '';
}

export function renderJson(value: unknown, baseIndent: string, indentUnit: string, eol: string): string {
    const compact = JSON.stringify(value);
    if (compact === undefined) {
        return 'null';
    }
    if (typeof value !== 'object' || value === null) {
        return compact;
    }
    const text = JSON.stringify(value, null, indentUnit);
    if (text.indexOf('\n') < 0) {
        return text;
    }
    return text.split('\n').join(eol + baseIndent);
}

export function rangeOf(document: vscode.TextDocument, node: Node): vscode.Range {
    return new vscode.Range(document.positionAt(node.offset), document.positionAt(node.offset + node.length));
}

export function schemaForPath(schemaDocument: SchemaDocument, path: PathSegments): JsonSchema | undefined {
    let current: JsonSchema | undefined = schemaDocument.root;
    for (const segment of path) {
        if (!current) {
            return undefined;
        }
        current = typeof segment === 'number' ? schemaDocument.items(current) : schemaDocument.property(current, segment);
    }
    return current;
}

export function defaultValueFor(schemaDocument: SchemaDocument, schema: JsonSchema | undefined): unknown {
    const enumValues = schemaDocument.enumValues(schema);
    if (enumValues && enumValues.length > 0) {
        return enumValues[0];
    }
    const labels = schemaDocument.labels(schema);
    if (labels && labels.length > 0) {
        return labels[0].value;
    }
    const types = schemaDocument.types(schema);
    if (types.includes('string')) {
        return '';
    }
    if (types.includes('integer') || types.includes('number')) {
        const minimum = schemaDocument.minimum(schema);
        return minimum !== undefined && minimum > 0 ? minimum : 0;
    }
    if (types.includes('boolean')) {
        return false;
    }
    if (types.includes('array')) {
        return [];
    }
    if (types.includes('object')) {
        return {};
    }
    return null;
}

export function valueAt(snapshot: JsonSnapshot, path: PathSegments): unknown {
    const tree = snapshot.tree;
    if (!tree) {
        return undefined;
    }
    const node = path.length === 0 ? tree : findNodeAtLocation(tree, path);
    return node ? nodeValue(node) : undefined;
}

function nodeValue(node: Node): unknown {
    if (node.type === 'object') {
        const result: { [key: string]: unknown } = {};
        for (const child of node.children ?? []) {
            const keyNode = child.children?.[0];
            const valueNode = child.children?.[1];
            if (keyNode && typeof keyNode.value === 'string' && valueNode) {
                result[keyNode.value] = nodeValue(valueNode);
            }
        }
        return result;
    }
    if (node.type === 'array') {
        return (node.children ?? []).map(nodeValue);
    }
    return node.value;
}

function editsFromParser(document: vscode.TextDocument, text: string, path: PathSegments, value: unknown): vscode.TextEdit[] | undefined {
    const edits = modify(text, path, value, { formattingOptions: formattingOptionsOf(document) });
    if (edits.length === 0) {
        return undefined;
    }
    return edits.map(edit => new vscode.TextEdit(
        new vscode.Range(
            document.positionAt(edit.offset),
            document.positionAt(edit.offset + edit.length)
        ),
        edit.content
    ));
}

export function setValueEdits(document: vscode.TextDocument, snapshot: JsonSnapshot, schemaDocument: SchemaDocument, path: PathSegments, value: unknown): vscode.TextEdit[] | undefined {
    const tree = snapshot.tree;
    if (!tree) {
        return undefined;
    }

    const node = findNodeAtLocation(tree, path);
    if (node) {
        const rendered = renderJson(value, lineIndentation(document, node.offset), indentUnitOf(document), formattingOptionsOf(document).eol);
        return [vscode.TextEdit.replace(rangeOf(document, node), rendered)];
    }

    const parentPath = path.slice(0, -1);
    const last = path[path.length - 1];
    if (typeof last !== 'string') {
        return undefined;
    }
    const parentNode = parentPath.length === 0 ? tree : findNodeAtLocation(tree, parentPath);
    if (!parentNode || parentNode.type !== 'object') {
        return editsFromParser(document, snapshot.text, path, value);
    }
    return insertPropertyInSchemaOrder(document, snapshot, schemaDocument, parentNode, parentPath, last, value);
}

function insertPropertyInSchemaOrder(
    document: vscode.TextDocument,
    snapshot: JsonSnapshot,
    schemaDocument: SchemaDocument,
    objectNode: Node,
    objectPath: PathSegments,
    keyName: string,
    value: unknown
): vscode.TextEdit[] | undefined {
    const indentUnit = indentUnitOf(document);
    const eol = formattingOptionsOf(document).eol;
    const members = objectNode.children ?? [];
    const declared = schemaDocument.propertyNames(schemaForPath(schemaDocument, objectPath));
    const orderedKeys = [...declared, ...members
        .map(member => member.children?.[0]?.value)
        .filter((name): name is string => typeof name === 'string' && !declared.includes(name))
        .sort((left, right) => (left < right ? -1 : left > right ? 1 : 0))];
    const desiredIndex = orderedKeys.indexOf(keyName);

    const memberIndent = members.length > 0
        ? lineIndentation(document, members[0].offset)
        : lineIndentation(document, objectNode.offset) + indentUnit;
    const rendered = `${JSON.stringify(keyName)}: ${renderJson(value, memberIndent, indentUnit, eol)}`;

    if (members.length === 0) {
        const closingIndent = lineIndentation(document, objectNode.offset);
        const body = `{${eol}${memberIndent}${rendered}${eol}${closingIndent}}`;
        return [vscode.TextEdit.replace(rangeOf(document, objectNode), body)];
    }

    for (const member of members) {
        const keyNode = member.children?.[0];
        if (!keyNode || typeof keyNode.value !== 'string') {
            continue;
        }
        const memberIndex = orderedKeys.indexOf(keyNode.value);
        const insertBefore = desiredIndex < 0
            ? memberIndex < 0 && keyNode.value > keyName
            : memberIndex < 0 || memberIndex > desiredIndex;
        if (!insertBefore) {
            continue;
        }
        const targetIndent = lineIndentation(document, member.offset);
        const at = new vscode.Position(document.positionAt(member.offset).line, targetIndent.length);
        return [vscode.TextEdit.insert(at, `${rendered},${eol}${targetIndent}`)];
    }

    const last = members[members.length - 1];
    return [vscode.TextEdit.insert(document.positionAt(last.offset + last.length), `,${eol}${memberIndent}${rendered}`)];
}

export function removeAtEdits(document: vscode.TextDocument, snapshot: JsonSnapshot, path: PathSegments): vscode.TextEdit[] | undefined {
    if (!snapshot.tree) {
        return undefined;
    }
    return editsFromParser(document, snapshot.text, path, undefined);
}

export function appendItemEdits(document: vscode.TextDocument, snapshot: JsonSnapshot, arrayPath: PathSegments, value: unknown): vscode.TextEdit[] | undefined {
    const arrayNode = snapshot.tree ? findNodeAtLocation(snapshot.tree, arrayPath) : undefined;
    if (!arrayNode || arrayNode.type !== 'array') {
        return undefined;
    }
    const length = (arrayNode.children ?? []).length;
    return editsFromParser(document, snapshot.text, [...arrayPath, length], value);
}

export function insertEntryEdits(document: vscode.TextDocument, snapshot: JsonSnapshot, objectPath: PathSegments, keyName: string, value: unknown): vscode.TextEdit[] | undefined {
    if (!snapshot.tree) {
        return undefined;
    }
    return editsFromParser(document, snapshot.text, [...objectPath, keyName], value);
}

export function renameKeyEdits(document: vscode.TextDocument, snapshot: JsonSnapshot, path: PathSegments, newKey: string): vscode.TextEdit[] | undefined {
    const tree = snapshot.tree;
    if (!tree) {
        return undefined;
    }
    const node = findNodeAtLocation(tree, path);
    const propertyNode = node?.parent;
    const keyNode = propertyNode?.children?.[0];
    if (!node || !propertyNode || !keyNode || typeof keyNode.value !== 'string') {
        return undefined;
    }
    if (keyNode.value === newKey) {
        return undefined;
    }
    const siblings = propertyNode.parent?.children ?? [];
    for (const sibling of siblings) {
        if (sibling === propertyNode) {
            continue;
        }
        const siblingKey = sibling.children?.[0];
        if (siblingKey && siblingKey.value === newKey) {
            return undefined;
        }
    }
    return [vscode.TextEdit.replace(rangeOf(document, keyNode), JSON.stringify(newKey))];
}
