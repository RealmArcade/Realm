import * as vscode from 'vscode';
import { Node } from 'jsonc-parser';
import { JsonSchema, SchemaDocument } from './schemaKeywords';
import { JsonSnapshotCache } from './jsonDocument';
import { MapSchemaRegistry } from './schemaRegistry';

interface MemberText {
    name: string;
    text: string;
}

interface RenderedNode {
    text: string;
    changed: boolean;
}

function desiredKeyOrder(schemaDocument: SchemaDocument, schema: JsonSchema | undefined, present: string[]): string[] {
    const declared = schemaDocument.propertyNames(schema);
    const ordered = declared.filter(name => present.includes(name));
    const unknown = present
        .filter(name => !declared.includes(name))
        .sort((left, right) => (left < right ? -1 : left > right ? 1 : 0));
    return [...ordered, ...unknown];
}

class SchemaFormatter {
    constructor(
        private readonly text: string,
        private readonly schemaDocument: SchemaDocument,
        private readonly positionAt: (offset: number) => vscode.Position
    ) {}

    formatRoot(node: Node): vscode.TextEdit[] {
        const edits: vscode.TextEdit[] = [];
        this.collect(node, this.schemaDocument.root, edits);
        return edits;
    }

    private collect(node: Node, schema: JsonSchema | undefined, edits: vscode.TextEdit[]): void {
        if (node.type === 'object') {
            const rendered = this.renderObject(node, schema);
            if (rendered.changed) {
                edits.push(new vscode.TextEdit(
                    new vscode.Range(this.positionAt(node.offset), this.positionAt(node.offset + node.length)),
                    rendered.text
                ));
                return;
            }
            for (const child of node.children ?? []) {
                const keyNode = child.children?.[0];
                const valueNode = child.children?.[1];
                if (!keyNode || !valueNode || typeof keyNode.value !== 'string') {
                    continue;
                }
                this.collect(valueNode, this.schemaDocument.property(schema, keyNode.value), edits);
            }
            return;
        }

        if (node.type === 'array') {
            const itemSchema = this.schemaDocument.items(schema);
            for (const child of node.children ?? []) {
                this.collect(child, itemSchema, edits);
            }
        }
    }

    private renderObject(node: Node, schema: JsonSchema | undefined): RenderedNode {
        const raw = this.raw(node);
        const members = node.children ?? [];
        if (members.length === 0) {
            return { text: raw, changed: false };
        }

        const rendered: MemberText[] = [];
        let memberChanged = false;
        for (const member of members) {
            const keyNode = member.children?.[0];
            const valueNode = member.children?.[1];
            if (!keyNode || typeof keyNode.value !== 'string') {
                return { text: raw, changed: false };
            }
            const keyText = this.raw(keyNode);
            if (!valueNode) {
                return { text: raw, changed: false };
            }
            const colonText = this.text.slice(keyNode.offset + keyNode.length, valueNode.offset);
            const value = this.renderValue(valueNode, this.schemaDocument.property(schema, keyNode.value));
            memberChanged = memberChanged || value.changed;
            rendered.push({ name: keyNode.value, text: `${keyText}${colonText}${value.text}` });
        }

        const presentNames = rendered.map(member => member.name);
        const order = desiredKeyOrder(this.schemaDocument, schema, presentNames);
        const orderChanged = order.some((name, index) => name !== presentNames[index]);
        if (!orderChanged && !memberChanged) {
            return { text: raw, changed: false };
        }

        const innerStart = node.offset + 1;
        const innerEnd = node.offset + node.length - 1;
        if (innerEnd < innerStart) {
            return { text: raw, changed: false };
        }

        const first = members[0];
        const last = members[members.length - 1];
        const leadingGap = this.text.slice(innerStart, first.offset);
        const closingGap = this.text.slice(last.offset + last.length, innerEnd);
        const body = order
            .map(name => rendered[presentNames.indexOf(name)].text)
            .join(`,${leadingGap}`);

        return { text: `{${leadingGap}${body}${closingGap}}`, changed: true };
    }

    private renderValue(node: Node, schema: JsonSchema | undefined): RenderedNode {
        if (node.type === 'object') {
            return this.renderObject(node, schema);
        }
        if (node.type !== 'array') {
            return { text: this.raw(node), changed: false };
        }

        const children = node.children ?? [];
        const itemSchema = this.schemaDocument.items(schema);
        let changed = false;
        const texts = children.map(child => {
            const rendered = this.renderValue(child, itemSchema);
            changed = changed || rendered.changed;
            return rendered.text;
        });
        if (!changed || children.length === 0) {
            return { text: this.raw(node), changed: false };
        }

        const innerStart = node.offset + 1;
        const innerEnd = node.offset + node.length - 1;
        if (innerEnd < innerStart) {
            return { text: this.raw(node), changed: false };
        }
        const first = children[0];
        const last = children[children.length - 1];
        const leadingGap = this.text.slice(innerStart, first.offset);
        const closingGap = this.text.slice(last.offset + last.length, innerEnd);
        return { text: `[${leadingGap}${texts.join(`,${leadingGap}`)}${closingGap}]`, changed: true };
    }

    private raw(node: Node): string {
        return this.text.slice(node.offset, node.offset + node.length);
    }
}

export class SchemaFormattingProvider implements vscode.DocumentFormattingEditProvider {
    constructor(
        private readonly snapshots: JsonSnapshotCache,
        private readonly registry: MapSchemaRegistry
    ) {}

    provideDocumentFormattingEdits(
        document: vscode.TextDocument,
        _options: vscode.FormattingOptions,
        token: vscode.CancellationToken
    ): vscode.TextEdit[] {
        if (token.isCancellationRequested) {
            return [];
        }
        try {
            return computeFormattingEdits(document, this.snapshots, this.registry);
        } catch {
            return [];
        }
    }
}

export function computeFormattingEdits(
    document: vscode.TextDocument,
    snapshots: JsonSnapshotCache,
    registry: MapSchemaRegistry
): vscode.TextEdit[] {
    const schemaDocument = registry.forDocument(document);
    if (!schemaDocument) {
        return [];
    }
    const snapshot = snapshots.get(document);
    if (!snapshot.tree || snapshot.errors.length > 0) {
        return [];
    }
    const formatter = new SchemaFormatter(snapshot.text, schemaDocument, offset => document.positionAt(offset));
    return formatter.formatRoot(snapshot.tree);
}
