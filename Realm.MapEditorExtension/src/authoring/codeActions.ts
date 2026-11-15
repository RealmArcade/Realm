import * as vscode from 'vscode';
import { Node } from 'jsonc-parser';
import { JsonSchema, SchemaDocument } from './schemaKeywords';
import { JsonSnapshotCache, objectPropertyNames, resolveCursorLocation } from './jsonDocument';
import { MapSchemaRegistry } from './schemaRegistry';

export function defaultValueLiteral(schemaDocument: SchemaDocument, schema: JsonSchema | undefined): string {
    const enumValues = schemaDocument.enumValues(schema);
    if (enumValues && enumValues.length > 0) {
        return JSON.stringify(enumValues[0]);
    }

    const labels = schemaDocument.labels(schema);
    if (labels && labels.length > 0) {
        return String(labels[0].value);
    }

    const types = schemaDocument.types(schema);
    if (types.includes('string')) {
        return '""';
    }
    if (types.includes('integer') || types.includes('number')) {
        const minimum = schemaDocument.minimum(schema);
        return String(minimum !== undefined && minimum > 0 ? minimum : 0);
    }
    if (types.includes('boolean')) {
        return 'false';
    }
    if (types.includes('array')) {
        return '[]';
    }
    if (types.includes('object')) {
        return '{}';
    }
    return 'null';
}

function leadingWhitespace(text: string): string {
    const match = /^[ \t]*/.exec(text);
    return match ? match[0] : '';
}

function indentUnitOf(document: vscode.TextDocument): string {
    const editor = vscode.window.visibleTextEditors.find(candidate => candidate.document === document);
    if (editor) {
        return editor.options.insertSpaces === false ? '\t' : ' '.repeat(typeof editor.options.tabSize === 'number' ? editor.options.tabSize : 2);
    }
    const lines = document.getText().split('\n', 40);
    for (const line of lines) {
        const whitespace = leadingWhitespace(line);
        if (whitespace.length > 0 && line.trim().length > 0) {
            return whitespace;
        }
    }
    return '  ';
}

function memberIndentOf(document: vscode.TextDocument, objectNode: Node, indentUnit: string): { memberIndent: string; closingIndent: string } {
    const openingLine = document.lineAt(document.positionAt(objectNode.offset));
    const closingIndent = leadingWhitespace(openingLine.text);
    const firstMember = objectNode.children?.[0];
    if (firstMember) {
        const memberLine = document.lineAt(document.positionAt(firstMember.offset));
        if (memberLine.lineNumber > openingLine.lineNumber) {
            return { memberIndent: leadingWhitespace(memberLine.text), closingIndent };
        }
    }
    return { memberIndent: closingIndent + indentUnit, closingIndent };
}

export class MissingFieldsCodeActionProvider implements vscode.CodeActionProvider {
    public static readonly providedCodeActionKinds = [vscode.CodeActionKind.RefactorRewrite];

    constructor(
        private readonly snapshots: JsonSnapshotCache,
        private readonly registry: MapSchemaRegistry
    ) {}

    provideCodeActions(
        document: vscode.TextDocument,
        range: vscode.Range | vscode.Selection,
        _context: vscode.CodeActionContext,
        token: vscode.CancellationToken
    ): vscode.CodeAction[] {
        if (token.isCancellationRequested) {
            return [];
        }
        try {
            return this.build(document, range.start);
        } catch {
            return [];
        }
    }

    private build(document: vscode.TextDocument, position: vscode.Position): vscode.CodeAction[] {
        const location = resolveCursorLocation(document, position, this.snapshots, this.registry);
        if (!location) {
            return [];
        }
        const schemaDocument = location.schemaDocument;

        let objectNode: Node | undefined;
        let objectSchema: JsonSchema | undefined;
        if (location.valueNode && location.valueNode.type === 'object') {
            objectNode = location.valueNode;
            objectSchema = location.valueSchema;
        } else if (location.containerNode && location.containerNode.type === 'object') {
            objectNode = location.containerNode;
            objectSchema = location.containerSchema;
        }
        if (!objectNode || !schemaDocument.isClosedObject(objectSchema)) {
            return [];
        }

        const properties = schemaDocument.properties(objectSchema);
        if (!properties) {
            return [];
        }

        const present = new Set(objectPropertyNames(objectNode));
        const missing = Object.keys(properties).filter(name => !present.has(name));
        if (missing.length === 0) {
            return [];
        }

        const indentUnit = indentUnitOf(document);
        const { memberIndent, closingIndent } = memberIndentOf(document, objectNode, indentUnit);
        const members = missing.map(name => `${JSON.stringify(name)}: ${defaultValueLiteral(schemaDocument, schemaDocument.property(objectSchema, name))}`);
        const hasMembers = (objectNode.children?.length ?? 0) > 0;

        const edit = new vscode.WorkspaceEdit();
        if (hasMembers) {
            const insertionPoint = document.positionAt(objectNode.offset + objectNode.length - 1);
            const separator = document.eol === vscode.EndOfLine.CRLF ? '\r\n' : '\n';
            const text = members.map(member => `,${separator}${memberIndent}${member}`).join('');
            edit.insert(document.uri, insertionPoint, text);
        } else {
            const separator = document.eol === vscode.EndOfLine.CRLF ? '\r\n' : '\n';
            const text = `{${separator}${members.map(member => `${memberIndent}${member}`).join(`,${separator}`)}${separator}${closingIndent}}`;
            edit.replace(
                document.uri,
                new vscode.Range(document.positionAt(objectNode.offset), document.positionAt(objectNode.offset + objectNode.length)),
                text
            );
        }

        const action = new vscode.CodeAction(
            `Add ${missing.length} missing ${missing.length === 1 ? 'field' : 'fields'}: ${missing.join(', ')}`,
            vscode.CodeActionKind.RefactorRewrite
        );
        action.edit = edit;
        action.diagnostics = [];
        action.isPreferred = false;
        return [action];
    }
}
