import * as path from 'path';
import * as vscode from 'vscode';
import { Node, findNodeAtLocation } from 'jsonc-parser';
import { JsonSnapshotCache } from '../authoring/jsonDocument';
import { MapSchemaRegistry } from '../authoring/schemaRegistry';
import { DocumentIndexCache } from '../authoring/documentIndex';
import { MapAssetIndex, mapFolderOfDocument } from '../authoring/assetIndex';
import { JsonSchema, SchemaDocument } from '../authoring/schemaKeywords';
import { ProjectionContext, buildChildren, buildSections, projectionState, schemaAt } from './projection';
import {
    appendItemEdits,
    defaultValueFor,
    insertEntryEdits,
    removeAtEdits,
    renameKeyEdits,
    schemaForPath,
    setValueEdits,
    valueAt
} from './jsonEdits';
import { ChildrenPage, FormNode, FormProjection, FormSide, PathSegments, WebviewToHostMessage, pathKey } from './formTypes';
import { buildFormHtml } from './formHtml';

export interface FormServices {
    snapshots: JsonSnapshotCache;
    registry: MapSchemaRegistry;
    documentIndex: DocumentIndexCache;
    assets: MapAssetIndex;
}

const pageSize = 40;
const coalesceMilliseconds = 220;
const layoutStatePrefix = 'realm.metadataForm.collapsed:';

interface ReferenceDocument {
    name: string;
    document: vscode.TextDocument;
}

interface PendingCommit {
    path: PathSegments;
    value: unknown;
    timer: ReturnType<typeof setTimeout>;
}

interface Session {
    document: vscode.TextDocument;
    panel: vscode.WebviewPanel;
    collapsed: string[];
    sections: FormNode[];
    state: 'ok' | 'unparsable';
    reference: ReferenceDocument | undefined;
    pending: PendingCommit | undefined;
    layoutTimer: ReturnType<typeof setTimeout> | undefined;
    disposed: boolean;
    subscriptions: vscode.Disposable[];
}

export class MetadataFormProvider implements vscode.CustomTextEditorProvider, vscode.Disposable {
    public static readonly viewType = 'realm.metadataForm';

    private readonly sessions = new Map<string, Session>();

    constructor(
        private readonly context: vscode.ExtensionContext,
        private readonly services: FormServices
    ) {}

    public static register(context: vscode.ExtensionContext, services: FormServices): vscode.Disposable {
        const provider = new MetadataFormProvider(context, services);
        return vscode.Disposable.from(
            provider,
            vscode.window.registerCustomEditorProvider(MetadataFormProvider.viewType, provider, {
                webviewOptions: { retainContextWhenHidden: true },
                supportsMultipleEditorsPerDocument: false
            }),
            vscode.commands.registerCommand('realm.openMetadataForm', async (argument?: unknown) => {
                const uri = await resolveFormTarget(context, services, argument);
                if (!uri) {
                    await vscode.window.showInformationMessage('Open a metadata.json file to edit it as a form.');
                    return;
                }
                await vscode.commands.executeCommand('vscode.openWith', uri, MetadataFormProvider.viewType);
            })
        );
    }

    public dispose(): void {
        for (const session of [...this.sessions.values()]) {
            this.flush(session);
            session.disposed = true;
        }
        this.sessions.clear();
    }

    public resolveCustomTextEditor(
        document: vscode.TextDocument,
        panel: vscode.WebviewPanel,
        _token: vscode.CancellationToken
    ): void {
        const schemaFileName = this.services.registry.schemaFileNameFor(document);
        if (schemaFileName !== 'metadata.schema.json') {
            panel.webview.html = renderUnsupportedDocument(panel.webview, document);
            return;
        }

        const key = document.uri.toString();
        const session: Session = {
            document,
            panel,
            collapsed: this.readCollapsed(document),
            sections: [],
            state: 'ok',
            reference: undefined,
            pending: undefined,
            layoutTimer: undefined,
            disposed: false,
            subscriptions: []
        };
        this.sessions.set(key, session);

        panel.webview.options = {
            enableScripts: true,
            localResourceRoots: [vscode.Uri.joinPath(this.context.extensionUri, 'dist')]
        };
        panel.webview.html = buildFormHtml(panel.webview, this.context.extensionUri);

        const documentKey = document.uri.toString();
        session.subscriptions.push(
            panel.webview.onDidReceiveMessage(message => this.handleMessage(session, message as WebviewToHostMessage)),
            vscode.workspace.onDidChangeTextDocument(event => {
                if (event.document.uri.toString() === documentKey) {
                    this.postProjection(session);
                }
            }),
            panel.onDidChangeViewState(state => {
                if (!state.webviewPanel.visible) {
                    this.flush(session);
                }
            }),
            panel.onDidDispose(() => this.close(session))
        );
    }

    private close(session: Session): void {
        this.flush(session);
        session.disposed = true;
        for (const subscription of session.subscriptions) {
            subscription.dispose();
        }
        session.subscriptions.length = 0;
        if (session.layoutTimer) {
            clearTimeout(session.layoutTimer);
        }
        this.sessions.delete(session.document.uri.toString());
    }

    private post(session: Session, message: unknown): void {
        if (session.disposed) {
            return;
        }
        void session.panel.webview.postMessage(message);
    }

    private handleMessage(session: Session, message: WebviewToHostMessage): void {
        if (!message || typeof message.type !== 'string') {
            return;
        }
        try {
            switch (message.type) {
                case 'ready':
                    this.postProjection(session);
                    if (session.reference) {
                        this.postReference(session);
                    }
                    return;
                case 'expand':
                    this.postChildren(session, message.side ?? 'edited', message.path ?? [], message.offset ?? 0, message.limit ?? pageSize);
                    return;
                case 'set':
                    this.queue(session, message.path, message.value, message.phase === 'end');
                    return;
                case 'flush':
                    this.flush(session);
                    return;
                case 'layout':
                    session.collapsed = Array.isArray(message.collapsed) ? message.collapsed.filter(entry => typeof entry === 'string') : [];
                    this.saveCollapsed(session);
                    return;
                case 'compare':
                    void this.pickReference(session);
                    return;
                case 'closeCompare':
                    session.reference = undefined;
                    this.post(session, { type: 'reference', projection: undefined });
                    return;
                case 'reveal':
                    void this.reveal(session, message.path ?? []);
                    return;
                case 'openText':
                    this.flush(session);
                    void vscode.commands.executeCommand('vscode.openWith', session.document.uri, 'default');
                    return;
                case 'addEntry':
                    void this.structural(session, message.path ?? [], 'addEntry', message.keyName ?? '');
                    return;
                case 'renameEntry':
                    void this.structural(session, message.path ?? [], 'renameEntry', message.keyName ?? '');
                    return;
                case 'removeEntry':
                    void this.structural(session, message.path ?? [], 'removeEntry');
                    return;
                case 'addItem':
                    void this.structural(session, message.path ?? [], 'addItem');
                    return;
                case 'removeItem':
                    void this.structural(session, message.path ?? [], 'removeItem');
                    return;
                default:
                    return;
            }
        } catch (error) {
            this.post(session, { type: 'error', message: describeError(error) });
        }
    }

    private contextFor(document: vscode.TextDocument): ProjectionContext | undefined {
        const schemaDocument = this.services.registry.forDocument(document);
        if (!schemaDocument) {
            return undefined;
        }
        const snapshot = this.services.snapshots.get(document);
        return {
            uri: document.uri,
            version: document.version,
            snapshot,
            schemaDocument,
            index: this.services.documentIndex.get(document),
            mapFolder: mapFolderOfDocument(document),
            assets: this.services.assets
        };
    }

    private referenceContext(session: Session): ProjectionContext | undefined {
        if (!session.reference) {
            return undefined;
        }
        const referenceDocument = session.reference.document;
        const editedSchema: SchemaDocument | undefined = this.services.registry.forDocument(session.document);
        if (!editedSchema) {
            return undefined;
        }
        const snapshot = this.services.snapshots.get(referenceDocument);
        return {
            uri: referenceDocument.uri,
            version: referenceDocument.version,
            snapshot,
            schemaDocument: editedSchema,
            index: this.services.documentIndex.get(referenceDocument),
            mapFolder: mapFolderOfDocument(referenceDocument),
            assets: this.services.assets
        };
    }

    private postProjection(session: Session): void {
        const context = this.contextFor(session.document);
        if (!context) {
            this.post(session, { type: 'error', message: 'No schema is associated with this document, so the form cannot render it.' });
            return;
        }

        const state = projectionState(context);
        session.state = state.state;
        if (state.state === 'ok') {
            session.sections = buildSections(context);
        }

        const projection: FormProjection = {
            version: context.version,
            state: state.state,
            title: vscode.workspace.asRelativePath(session.document.uri, false),
            subtitle: `Document version ${context.version}`,
            sections: session.sections,
            collapsed: session.collapsed,
            writable: state.state === 'ok',
            stateMessage: state.message
        };
        this.post(session, { type: 'project', projection });
    }

    private postChildren(session: Session, side: FormSide, requestPath: PathSegments, offset: number, limit: number): void {
        const context = side === 'reference' ? this.referenceContext(session) : this.contextFor(session.document);
        if (!context) {
            this.post(session, { type: 'children', side, path: requestPath, page: { nodes: [], offset: 0, total: 0 } });
            return;
        }
        const page: ChildrenPage = buildChildren(context, requestPath, offset, limit > 0 ? limit : pageSize);
        this.post(session, { type: 'children', side, path: requestPath, page });
    }

    private postReference(session: Session): void {
        const context = this.referenceContext(session);
        if (!context || !session.reference) {
            this.post(session, { type: 'reference', projection: undefined });
            return;
        }
        const state = projectionState(context);
        const projection: FormProjection = {
            version: context.version,
            state: state.state,
            title: session.reference.name,
            subtitle: 'Read-only reference',
            sections: state.state === 'ok' ? buildSections(context) : [],
            collapsed: [],
            writable: false,
            stateMessage: state.message
        };
        this.post(session, { type: 'reference', projection, name: session.reference.name });
    }

    private queue(session: Session, requestPath: PathSegments, value: unknown, endOfGesture: boolean): void {
        if (session.pending && pathKey(session.pending.path) !== pathKey(requestPath)) {
            this.flush(session);
        }
        if (session.pending) {
            clearTimeout(session.pending.timer);
            session.pending = undefined;
        }
        if (endOfGesture) {
            void this.commit(session, requestPath, value);
            return;
        }
        session.pending = {
            path: requestPath,
            value,
            timer: setTimeout(() => this.flush(session), coalesceMilliseconds)
        };
    }

    private flush(session: Session): void {
        const pending = session.pending;
        if (!pending) {
            return;
        }
        session.pending = undefined;
        clearTimeout(pending.timer);
        void this.commit(session, pending.path, pending.value);
    }

    private async commit(session: Session, requestPath: PathSegments, rawValue: unknown): Promise<void> {
        const context = this.contextFor(session.document);
        if (!context) {
            return;
        }
        if (session.state !== 'ok' || projectionState(context).state !== 'ok') {
            this.post(session, { type: 'info', message: 'The document does not parse, so nothing was written. Fix the text and the form resumes writing.' });
            return;
        }

        const schema = schemaAt(context, requestPath);
        const value = coerceValue(context.schemaDocument, schema, rawValue);
        if (value === undefined) {
            this.post(session, { type: 'error', message: 'That value cannot be stored in this field.' });
            this.postProjection(session);
            return;
        }

        const current = valueAt(context.snapshot, requestPath);
        if (sameValue(current, value)) {
            return;
        }

        const edits = setValueEdits(session.document, context.snapshot, context.schemaDocument, requestPath, value);
        if (!edits) {
            this.post(session, { type: 'error', message: 'That edit could not be expressed in the document.' });
            this.postProjection(session);
            return;
        }
        await this.apply(session, edits);
    }

    private async structural(session: Session, requestPath: PathSegments, operation: 'addEntry' | 'renameEntry' | 'removeEntry' | 'addItem' | 'removeItem', keyName?: string): Promise<void> {
        this.flush(session);
        const context = this.contextFor(session.document);
        if (!context) {
            return;
        }
        if (projectionState(context).state !== 'ok') {
            this.post(session, { type: 'info', message: 'The document does not parse, so nothing was written.' });
            return;
        }

        const schemaDocument = context.schemaDocument;
        const targetSchema = schemaAt(context, requestPath);
        let edits: vscode.TextEdit[] | undefined;

        switch (operation) {
            case 'addEntry': {
                if (typeof keyName !== 'string' || keyName.length === 0) {
                    return;
                }
                const entryValue = defaultValueFor(schemaDocument, schemaDocument.entryValueSchema(targetSchema));
                edits = insertEntryEdits(session.document, context.snapshot, requestPath, keyName, entryValue);
                break;
            }
            case 'renameEntry': {
                const newKey = typeof keyName === 'string' ? keyName.trim() : '';
                if (newKey.length === 0) {
                    return;
                }
                edits = renameKeyEdits(session.document, context.snapshot, requestPath, newKey);
                break;
            }
            case 'removeEntry':
                edits = removeAtEdits(session.document, context.snapshot, requestPath);
                break;
            case 'addItem': {
                const itemValue = defaultValueFor(schemaDocument, schemaDocument.items(targetSchema));
                edits = appendItemEdits(session.document, context.snapshot, requestPath, itemValue);
                break;
            }
            case 'removeItem':
                edits = removeAtEdits(session.document, context.snapshot, requestPath);
                break;
            default:
                return;
        }

        if (!edits) {
            this.post(session, { type: 'error', message: 'That change could not be expressed in the document. Check for a duplicate key.' });
            this.postProjection(session);
            return;
        }
        await this.apply(session, edits);
    }

    private async apply(session: Session, edits: vscode.TextEdit[]): Promise<void> {
        const edit = new vscode.WorkspaceEdit();
        for (const textEdit of edits) {
            edit.replace(session.document.uri, textEdit.range, textEdit.newText);
        }
        let applied = false;
        try {
            applied = await vscode.workspace.applyEdit(edit);
        } catch (error) {
            applied = false;
            this.post(session, { type: 'error', message: describeError(error) });
        }
        if (!applied) {
            this.post(session, { type: 'error', message: 'The edit was rejected by the editor. The form was re-read from the document.' });
            this.postProjection(session);
        }
    }

    private async reveal(session: Session, requestPath: PathSegments): Promise<void> {
        this.flush(session);
        const snapshot = this.services.snapshots.get(session.document);
        const target = requestPath.length === 0 ? undefined : findNode(snapshot, requestPath);
        const editor = await vscode.window.showTextDocument(session.document.uri, {
            viewColumn: vscode.ViewColumn.Beside,
            preview: false,
            preserveFocus: false
        });
        if (target) {
            const range = new vscode.Range(
                session.document.positionAt(target.offset),
                session.document.positionAt(target.offset + target.length)
            );
            editor.selection = new vscode.Selection(range.start, range.end);
            editor.revealRange(range, vscode.TextEditorRevealType.InCenterIfOutsideViewport);
        }
        this.post(session, { type: 'revealed' });
    }

    private async pickReference(session: Session): Promise<void> {
        this.flush(session);
        const mapFolder = mapFolderOfDocument(session.document);
        const picked = await vscode.window.showOpenDialog({
            canSelectFiles: true,
            canSelectFolders: false,
            canSelectMany: false,
            openLabel: 'Compare with this metadata.json',
            defaultUri: mapFolder ? vscode.Uri.file(path.join(mapFolder, 'metadata.json')) : undefined,
            filters: { 'Metadata': ['json'] }
        });
        if (!picked || picked.length === 0) {
            return;
        }
        const referenceDocument = await vscode.workspace.openTextDocument(picked[0]);
        session.reference = { name: path.basename(picked[0].fsPath), document: referenceDocument };
        this.postReference(session);
    }

    private readCollapsed(document: vscode.TextDocument): string[] {
        const value = this.context.workspaceState.get<unknown>(layoutStatePrefix + document.uri.toString());
        return Array.isArray(value) ? value.filter((entry): entry is string => typeof entry === 'string') : [];
    }

    private saveCollapsed(session: Session): void {
        if (session.layoutTimer) {
            clearTimeout(session.layoutTimer);
        }
        session.layoutTimer = setTimeout(() => {
            session.layoutTimer = undefined;
            void this.context.workspaceState.update(layoutStatePrefix + session.document.uri.toString(), session.collapsed);
        }, 200);
    }
}

function findNode(snapshot: { tree: Node | undefined }, requestPath: PathSegments): Node | undefined {
    if (!snapshot.tree) {
        return undefined;
    }
    return findNodeAtLocation(snapshot.tree, requestPath);
}

function coerceValue(schemaDocument: SchemaDocument, schema: JsonSchema | undefined, raw: unknown): unknown {
    if (raw === null) {
        return null;
    }
    const types = schemaDocument.types(schema);
    const allowsNull = types.includes('null');

    if (Array.isArray(raw)) {
        return raw.map(entry => (typeof entry === 'string' ? entry : String(entry)));
    }

    if (types.includes('boolean') && !types.includes('string')) {
        return Boolean(raw);
    }

    if (types.includes('integer') || types.includes('number')) {
        const text = typeof raw === 'number' ? String(raw) : String(raw).trim();
        if (text.length === 0) {
            return allowsNull ? null : undefined;
        }
        const parsed = Number(text);
        if (!Number.isFinite(parsed)) {
            return undefined;
        }
        return types.includes('integer') && !types.includes('number') ? Math.round(parsed) : parsed;
    }

    if (typeof raw === 'string') {
        if (raw.length === 0 && allowsNull && !types.includes('string')) {
            return null;
        }
        return raw;
    }

    return raw;
}

function sameValue(left: unknown, right: unknown): boolean {
    return JSON.stringify(left) === JSON.stringify(right);
}

function describeError(error: unknown): string {
    return error instanceof Error ? error.message : String(error);
}

function renderUnsupportedDocument(webview: vscode.Webview, document: vscode.TextDocument): string {
    const nonce = 'metadataform';
    void nonce;
    return `<!DOCTYPE html><html><head><meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src ${webview.cspSource};">
</head><body><p>The metadata form edits <code>metadata.json</code> only, so ${escapeHtml(path.basename(document.uri.fsPath))} is not shown here.</p>
<p>Open it as text instead.</p></body></html>`;
}

function escapeHtml(value: string): string {
    return value.replace(/[&<>"']/g, character => {
        switch (character) {
            case '&': return '&amp;';
            case '<': return '&lt;';
            case '>': return '&gt;';
            case '"': return '&quot;';
            default: return '&#39;';
        }
    });
}

async function resolveFormTarget(context: vscode.ExtensionContext, services: FormServices, argument?: unknown): Promise<vscode.Uri | undefined> {
    if (argument instanceof vscode.Uri) {
        return argument;
    }
    const active = vscode.window.activeTextEditor?.document;
    if (active && services.registry.schemaFileNameFor(active) === 'metadata.schema.json') {
        return active.uri;
    }

    const picked = await vscode.window.showOpenDialog({
        canSelectFiles: true,
        canSelectFolders: false,
        canSelectMany: false,
        openLabel: 'Open as form',
        filters: { 'Metadata': ['json'] }
    });
    if (!picked || picked.length === 0) {
        return undefined;
    }
    return picked[0];
}
