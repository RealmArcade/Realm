import './form.css';
import {
    ChildrenPage,
    ContainerNode,
    FlagOption,
    FormNode,
    FormProjection,
    FormSide,
    LeafNode,
    PathSegments,
    SelectOption,
    WebviewToHostMessage,
    pathKey
} from '../formTypes';

interface VsCodeApi {
    postMessage(message: unknown): void;
    getState(): unknown;
    setState(state: unknown): void;
}

declare function acquireVsCodeApi(): VsCodeApi;

const api = acquireVsCodeApi();
const pageSize = 40;

interface PagedNodes {
    nodes: FormNode[];
    total: number;
}

class FormView {
    private root: HTMLElement;
    private projection: FormProjection | undefined;
    private reference: FormProjection | undefined;
    private referenceName: string | undefined;
    private readonly pages = new Map<string, PagedNodes>();
    private readonly expanded = new Set<string>();
    private readonly missingShown = new Set<string>();
    private readonly dirtyPaths = new Map<string, unknown>();
    private gesture: string | undefined;
    private gesturePath: string | undefined;
    private deferredProjection: FormProjection | undefined;
    private deferredChildren: { side: FormSide; path: PathSegments; page: ChildrenPage } | undefined;

    constructor(root: HTMLElement) {
        this.root = root;
        const state = api.getState() as { expanded?: string[] } | undefined;
        for (const key of state?.expanded ?? []) {
            this.expanded.add(key);
        }
    }

    public setProjection(projection: FormProjection): void {
        if (!this.projection && projection.expanded) {
            for (const key of projection.expanded) {
                this.expanded.add(key);
            }
        }
        if (this.projection && projection.version === this.projection.version && sameSections(this.projection, projection)) {
            this.projection = projection;
            this.applyStateBanners();
            return;
        }
        if (this.isInteracting()) {
            this.deferredProjection = projection;
            return;
        }
        const versionChanged = !this.projection || this.projection.version !== projection.version;
        this.projection = projection;
        if (versionChanged) {
            this.pages.clear();
        }
        this.render();
        this.requestOpenContainers();
    }

    public setReference(projection: FormProjection | undefined, name?: string): void {
        this.reference = projection;
        this.referenceName = name;
        this.pages.forEach((_value, key) => {
            if (key.startsWith('reference:')) {
                this.pages.delete(key);
            }
        });
        this.render();
        this.requestOpenContainers();
    }

    public setChildren(side: FormSide, requestPath: PathSegments, page: ChildrenPage): void {
        if (this.isInteracting()) {
            this.deferredChildren = { side, path: requestPath, page };
            return;
        }
        const key = pageKey(side, requestPath);
        const existing = this.pages.get(key);
        if (existing && page.offset > 0) {
            existing.nodes = [...existing.nodes, ...page.nodes];
            existing.total = page.total;
        } else {
            this.pages.set(key, { nodes: page.nodes, total: page.total });
        }
        this.render();
    }

    public showError(message: string): void {
        this.showBanner(message, 'error');
    }

    public showInfo(message: string): void {
        this.showBanner(message, 'info');
    }

    private showBanner(message: string, kind: 'error' | 'info'): void {
        const banner = this.root.querySelector<HTMLElement>('.banner.transient');
        if (banner) {
            banner.remove();
        }
        const element = document.createElement('div');
        element.className = `banner ${kind} transient`;
        element.textContent = message;
        const host = this.root.querySelector<HTMLElement>('.header');
        if (host) {
            host.after(element);
        } else {
            this.root.prepend(element);
        }
        setTimeout(() => element.remove(), 8000);
    }

    private isInteracting(): boolean {
        if (this.gesture !== undefined) {
            return true;
        }
        return this.gesturePath !== undefined;
    }

    private applyDeferred(): void {
        const children = this.deferredChildren;
        this.deferredChildren = undefined;
        if (children) {
            this.setChildren(children.side, children.path, children.page);
        }
        const projection = this.deferredProjection;
        this.deferredProjection = undefined;
        if (projection) {
            this.setProjection(projection);
        }
    }

    private applyStateBanners(): void {
        const existing = this.root.querySelector<HTMLElement>('.banner.state');
        const projection = this.projection;
        if (existing) {
            existing.remove();
        }
        if (!projection || projection.state === 'ok') {
            return;
        }
        const element = document.createElement('div');
        element.className = 'banner state warning';
        element.textContent = projection.stateMessage ?? 'The document does not parse, so the form is read-only.';
        const host = this.root.querySelector<HTMLElement>('.header');
        if (host) {
            host.after(element);
        } else {
            this.root.prepend(element);
        }
    }

    private render(): void {
        const activeField = document.activeElement instanceof HTMLInputElement || document.activeElement instanceof HTMLTextAreaElement || document.activeElement instanceof HTMLSelectElement
            ? document.activeElement.dataset.path
            : undefined;
        const scrollTop = this.root.scrollTop;
        this.root.textContent = '';

        this.root.appendChild(this.renderToolbar());
        this.applyStateBanners();

        const projection = this.projection;
        if (!projection) {
            const loading = document.createElement('div');
            loading.className = 'loading';
            loading.textContent = 'Loading the metadata form...';
            this.root.appendChild(loading);
            return;
        }

        const panes = document.createElement('div');
        panes.className = this.reference ? 'panes compare' : 'panes';

        const edited = document.createElement('div');
        edited.className = 'pane edited';
        edited.appendChild(this.renderColumnHeader(projection.title, projection.subtitle));
        const sections = document.createElement('div');
        sections.className = 'sections';
        for (const section of projection.sections) {
            sections.appendChild(this.renderNode(section, 'edited'));
        }
        edited.appendChild(sections);
        panes.appendChild(edited);

        if (this.reference) {
            const reference = document.createElement('div');
            reference.className = 'pane reference';
            reference.appendChild(this.renderColumnHeader(this.referenceName ?? 'Reference', 'Read-only reference'));
            const referenceSections = document.createElement('div');
            referenceSections.className = 'sections';
            for (const section of this.reference.sections) {
                referenceSections.appendChild(this.renderNode(section, 'reference'));
            }
            reference.appendChild(referenceSections);
            panes.appendChild(reference);
        }

        this.root.appendChild(panes);
        this.root.scrollTop = scrollTop;

        if (activeField) {
            const restored = this.root.querySelector<HTMLElement>(`[data-path="${cssEscape(activeField)}"]`);
            restored?.focus();
        }
    }

    private renderToolbar(): HTMLElement {
        const header = document.createElement('div');
        header.className = 'header';

        const title = document.createElement('div');
        title.className = 'title';
        title.textContent = 'Metadata Form';
        header.appendChild(title);

        const actions = document.createElement('div');
        actions.className = 'actions';

        actions.appendChild(this.button('Reveal in text', () => {
            api.postMessage({ type: 'reveal', path: [] } satisfies WebviewToHostMessage);
        }, 'Show this document beside the form in the text editor'));

        actions.appendChild(this.button('Open as text', () => {
            api.postMessage({ type: 'openText' } satisfies WebviewToHostMessage);
        }, 'Open the document in the text editor instead of the form'));

        actions.appendChild(this.button(this.reference ? 'Stop comparing' : 'Compare...', () => {
            api.postMessage(this.reference
                ? ({ type: 'closeCompare' } satisfies WebviewToHostMessage)
                : ({ type: 'compare' } satisfies WebviewToHostMessage));
        }, 'Show another metadata.json beside this one, read-only'));

        header.appendChild(actions);
        return header;
    }

    private renderColumnHeader(title: string, subtitle: string): HTMLElement {
        const header = document.createElement('div');
        header.className = 'column-header';
        const heading = document.createElement('span');
        heading.className = 'column-title';
        heading.textContent = title;
        const sub = document.createElement('span');
        sub.className = 'column-subtitle';
        sub.textContent = subtitle;
        header.appendChild(heading);
        header.appendChild(sub);
        return header;
    }

    private button(label: string, onClick: () => void, title?: string): HTMLButtonElement {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'tool';
        button.textContent = label;
        if (title) {
            button.title = title;
        }
        button.addEventListener('click', event => {
            event.preventDefault();
            onClick();
        });
        return button;
    }

    private renderNode(node: FormNode, side: FormSide): HTMLElement {
        return node.kind === 'leaf' ? this.renderLeaf(node, side) : this.renderContainer(node, side);
    }

    private renderContainer(node: ContainerNode, side: FormSide): HTMLElement {
        const key = pageKey(side, node.path);
        const open = this.expanded.has(key);
        const wrapper = document.createElement('div');
        wrapper.className = `container ${node.kind}`;

        const header = document.createElement('div');
        header.className = 'container-header';

        const twisty = document.createElement('button');
        twisty.type = 'button';
        twisty.className = 'twisty';
        twisty.textContent = open ? '\u25BE' : '\u25B8';
        twisty.setAttribute('aria-expanded', open ? 'true' : 'false');
        twisty.addEventListener('click', event => {
            event.preventDefault();
            this.toggle(key);
        });
        header.appendChild(twisty);

        const label = document.createElement('button');
        label.type = 'button';
        label.className = 'container-label';
        label.textContent = node.label;
        label.title = node.description ?? node.label;
        label.addEventListener('click', event => {
            event.preventDefault();
            this.toggle(key);
        });
        header.appendChild(label);

        if (node.identity && node.identity !== node.label) {
            const identity = document.createElement('span');
            identity.className = 'identity';
            identity.textContent = node.identity;
            header.appendChild(identity);
        }

        const count = document.createElement('span');
        count.className = 'count';
        count.textContent = String(node.childCount);
        header.appendChild(count);

        if (side === 'edited' && node.kind === 'list') {
            header.appendChild(this.button('+ Add', () => {
                api.postMessage({ type: 'addItem', path: node.path } satisfies WebviewToHostMessage);
            }, 'Append a new record'));
        }

        if (side === 'edited' && node.kind === 'group' && node.dictionary) {
            header.appendChild(this.button('+ Entry', () => {
                const keyName = window.prompt('New key');
                if (keyName && keyName.trim().length > 0) {
                    api.postMessage({ type: 'addEntry', path: node.path, keyName: keyName.trim() } satisfies WebviewToHostMessage);
                }
            }, 'Add a dictionary entry'));
        }

        if (side === 'edited' && node.kind === 'entry') {
            header.appendChild(this.button('Rename', () => {
                const keyName = window.prompt('Rename key', node.key ?? '');
                if (keyName && keyName.trim().length > 0) {
                    api.postMessage({ type: 'renameEntry', path: node.path, keyName: keyName.trim() } satisfies WebviewToHostMessage);
                }
            }, 'Rename this dictionary key'));
            header.appendChild(this.button('Remove', () => {
                api.postMessage({ type: 'removeEntry', path: node.path } satisfies WebviewToHostMessage);
            }, 'Remove this dictionary entry'));
        }

        if (side === 'edited' && node.kind === 'record') {
            header.appendChild(this.button('Remove', () => {
                api.postMessage({ type: 'removeItem', path: node.path } satisfies WebviewToHostMessage);
            }, 'Remove this record'));
        }

        wrapper.appendChild(header);

        if (node.description) {
            const description = document.createElement('p');
            description.className = 'description';
            description.textContent = node.description;
            wrapper.appendChild(description);
        }

        if (open) {
            wrapper.appendChild(this.renderChildren(node, side));
        }

        return wrapper;
    }

    private renderChildren(node: ContainerNode, side: FormSide): HTMLElement {
        const body = document.createElement('div');
        body.className = 'children';
        const key = pageKey(side, node.path);
        const page = this.pages.get(key);

        if (!page) {
            api.postMessage({ type: 'expand', side, path: node.path, offset: 0, limit: pageSize } satisfies WebviewToHostMessage);
            const loading = document.createElement('div');
            loading.className = 'loading small';
            loading.textContent = 'Loading...';
            body.appendChild(loading);
            return body;
        }

        const missing: FormNode[] = [];
        const present: FormNode[] = [];
        for (const child of page.nodes) {
            if (child.kind === 'leaf' && !child.present) {
                missing.push(child);
            } else {
                present.push(child);
            }
        }

        let firstMissingLabel = '';
        if (missing.length > 0) {
            const missingKey = `${key}:missing`;
            const shown = this.missingShown.has(missingKey);
            firstMissingLabel = `${missing.length} field${missing.length === 1 ? '' : 's'} the schema declares but the document omits`;
            const toggle = this.button(shown ? `Hide ${firstMissingLabel}` : `Show ${firstMissingLabel}`, () => {
                if (shown) {
                    this.missingShown.delete(missingKey);
                } else {
                    this.missingShown.add(missingKey);
                }
                this.render();
            });
            toggle.classList.add('missing-toggle');
            body.appendChild(toggle);
            if (shown) {
                for (const child of missing) {
                    body.appendChild(this.renderNode(child, side));
                }
            }
        }

        for (const child of present) {
            body.appendChild(this.renderNode(child, side));
        }

        if (page.nodes.length < page.total) {
            body.appendChild(this.button(`Show ${Math.min(pageSize, page.total - page.nodes.length)} more of ${page.total}`, () => {
                api.postMessage({ type: 'expand', side, path: node.path, offset: page.nodes.length, limit: pageSize } satisfies WebviewToHostMessage);
            }));
        }

        return body;
    }

    private toggle(key: string): void {
        if (this.expanded.has(key)) {
            this.expanded.delete(key);
        } else {
            this.expanded.add(key);
        }
        api.setState({ expanded: [...this.expanded] });
        api.postMessage({ type: 'layout', expanded: [...this.expanded] } satisfies WebviewToHostMessage);
        api.postMessage({ type: 'flush' } satisfies WebviewToHostMessage);
        this.render();
        this.requestOpenContainers();
    }

    private requestOpenContainers(): void {
        const walk = (nodes: FormNode[], side: FormSide) => {
            for (const node of nodes) {
                if (node.kind === 'leaf') {
                    continue;
                }
                const key = pageKey(side, node.path);
                if (!this.expanded.has(key)) {
                    continue;
                }
                if (!this.pages.has(key)) {
                    api.postMessage({ type: 'expand', side, path: node.path, offset: 0, limit: pageSize } satisfies WebviewToHostMessage);
                }
                const page = this.pages.get(key);
                if (page) {
                    walk(page.nodes, side);
                }
            }
        };
        walk(this.projection?.sections ?? [], 'edited');
        walk(this.reference?.sections ?? [], 'reference');
    }

    private renderLeaf(node: LeafNode, side: FormSide): HTMLElement {
        const row = document.createElement('div');
        row.className = 'row';
        if (!node.present) {
            row.classList.add('absent');
        }
        if (node.readOnly) {
            row.classList.add('readonly');
        }

        const label = document.createElement('label');
        label.className = 'row-label';
        label.textContent = node.label;
        label.title = node.description ? `${node.label}\n\n${node.description}` : node.label;
        row.appendChild(label);

        const control = document.createElement('div');
        control.className = 'row-control';
        control.appendChild(this.renderControl(node, side));
        row.appendChild(control);

        if (node.unit) {
            const unit = document.createElement('span');
            unit.className = 'unit';
            unit.textContent = node.unit;
            row.appendChild(unit);
        }

        if (side === 'reference' && !node.readOnly && node.present && this.projection?.state === 'ok') {
            const copy = this.button('Copy \u2192', () => {
                api.postMessage({
                    type: 'set',
                    path: node.path,
                    value: node.value,
                    gesture: 'copy',
                    phase: 'end'
                } satisfies WebviewToHostMessage);
                this.showInfo(`Copied ${node.label} to the edited document.`);
            }, 'Write this value into the edited document');
            copy.classList.add('copy');
            row.appendChild(copy);
        }

        if (side === 'edited' && node.widget === 'readonly') {
            const reveal = this.button('Reveal', () => {
                api.postMessage({ type: 'reveal', path: node.path } satisfies WebviewToHostMessage);
            });
            reveal.classList.add('copy');
            row.appendChild(reveal);
        }

        return row;
    }

    private renderControl(node: LeafNode, side: FormSide): HTMLElement {
        const readOnly = node.readOnly || side === 'reference' || this.projection?.state === 'unparsable';
        const path = node.path;

        switch (node.widget) {
            case 'checkbox': {
                const input = document.createElement('input');
                input.type = 'checkbox';
                input.checked = node.value === true;
                input.disabled = readOnly;
                input.dataset.path = pathKey(path);
                input.addEventListener('change', () => this.commit(path, input.checked, 'toggle', 'end'));
                return input;
            }
            case 'select': {
                const select = document.createElement('select');
                select.disabled = readOnly;
                select.dataset.path = pathKey(path);
                const options = node.options ?? [];
                if (node.nullable) {
                    const emptyOption = document.createElement('option');
                    emptyOption.value = '';
                    emptyOption.textContent = '(none)';
                    select.appendChild(emptyOption);
                }
                for (const option of options) {
                    const element = document.createElement('option');
                    element.value = String(option.value);
                    element.textContent = option.label;
                    if (option.description) {
                        element.title = option.description;
                    }
                    select.appendChild(element);
                }
                select.value = node.value === undefined || node.value === null ? '' : String(node.value);
                select.addEventListener('change', () => {
                    const chosen = options.find(option => String(option.value) === select.value);
                    const value = chosen ? chosen.value : (select.value === '' && node.present ? null : undefined);
                    if (value !== undefined) {
                        this.commit(path, value, 'select', 'end');
                    }
                });
                return select;
            }
            case 'flags': {
                const group = document.createElement('div');
                group.className = 'flags';
                const current = typeof node.value === 'number' ? node.value : 0;
                const declaredMask = (node.flags ?? []).reduce((mask, flag) => mask | flag.bit, 0);
                for (const flag of node.flags ?? []) {
                    const wrapper = document.createElement('label');
                    wrapper.className = 'flag';
                    const input = document.createElement('input');
                    input.type = 'checkbox';
                    input.checked = (current & flag.bit) === flag.bit;
                    input.disabled = readOnly;
                    input.addEventListener('change', () => {
                        const base = (typeof node.value === 'number' ? node.value : 0) & ~declaredMask;
                        const selected = (node.flags ?? []).reduce((value, candidate) => {
                            const box = group.querySelector<HTMLInputElement>(`input[data-bit="${candidate.bit}"]`);
                            return box && box.checked ? value | candidate.bit : value;
                        }, 0);
                        this.commit(path, base | selected, `flag:${flag.bit}`, 'end');
                    });
                    input.dataset.bit = String(flag.bit);
                    wrapper.appendChild(input);
                    const text = document.createElement('span');
                    text.textContent = flag.label;
                    wrapper.appendChild(text);
                    group.appendChild(wrapper);
                }
                const unknownBits = current & ~declaredMask;
                if (unknownBits !== 0) {
                    const note = document.createElement('span');
                    note.className = 'unknown-bits';
                    note.textContent = `+${unknownBits}`;
                    note.title = 'Bits this schema does not name are preserved in the stored number.';
                    group.appendChild(note);
                }
                return group;
            }
            case 'slider': {
                const wrapper = document.createElement('div');
                wrapper.className = 'slider';
                const input = document.createElement('input');
                input.type = 'range';
                const minimum = node.minimum ?? 0;
                const maximum = node.maximum ?? 1;
                input.min = String(minimum);
                input.max = String(maximum);
                input.step = String(node.step ?? (maximum - minimum) / 100);
                input.value = String(typeof node.value === 'number' ? node.value : minimum);
                input.disabled = readOnly;
                input.dataset.path = pathKey(path);
                const readout = document.createElement('span');
                readout.className = 'readout';
                readout.textContent = input.value;
                input.addEventListener('input', () => {
                    readout.textContent = input.value;
                    this.commit(path, Number(input.value), `slider:${pathKey(path)}`, 'change');
                });
                input.addEventListener('change', () => {
                    this.commit(path, Number(input.value), `slider:${pathKey(path)}`, 'end');
                });
                wrapper.appendChild(input);
                wrapper.appendChild(readout);
                return wrapper;
            }
            case 'tags': {
                const input = document.createElement('input');
                input.type = 'text';
                input.value = Array.isArray(node.value) ? node.value.join(', ') : String(node.value ?? '');
                input.disabled = readOnly;
                input.dataset.path = pathKey(path);
                input.placeholder = 'comma separated';
                input.addEventListener('change', () => {
                    const entries = input.value.split(',').map(entry => entry.trim()).filter(entry => entry.length > 0);
                    this.commit(path, entries, 'tags', 'end');
                });
                return input;
            }
            case 'number': {
                const input = document.createElement('input');
                input.type = 'number';
                input.value = node.value === undefined || node.value === null ? '' : String(node.value);
                if (node.minimum !== undefined) {
                    input.min = String(node.minimum);
                }
                if (node.maximum !== undefined) {
                    input.max = String(node.maximum);
                }
                input.step = 'any';
                input.disabled = readOnly;
                input.dataset.path = pathKey(path);
                input.addEventListener('change', () => this.commit(path, input.value, 'number', 'end'));
                return input;
            }
            case 'color': {
                const wrapper = document.createElement('div');
                wrapper.className = 'color';
                const input = document.createElement('input');
                input.type = 'text';
                input.className = 'monospace';
                input.value = String(node.value ?? '');
                input.disabled = readOnly;
                input.dataset.path = pathKey(path);
                input.addEventListener('change', () => this.commit(path, input.value, 'color', 'end'));

                const picker = document.createElement('input');
                picker.type = 'color';
                const literal = String(node.value ?? '');
                const match = /^#([0-9a-fA-F]{6})/.exec(literal);
                picker.value = match ? `#${match[1].toLowerCase()}` : '#ffffff';
                picker.disabled = readOnly;
                picker.title = 'Pick a colour; an alpha suffix in the literal is kept';
                picker.addEventListener('input', () => {
                    const alpha = /^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})$/.exec(literal);
                    const value = `${picker.value}${alpha ? alpha[1] : ''}`;
                    input.value = value;
                    this.commit(path, value, `color:${pathKey(path)}`, 'change');
                });
                picker.addEventListener('change', () => {
                    this.commit(path, input.value, `color:${pathKey(path)}`, 'end');
                });

                wrapper.appendChild(input);
                wrapper.appendChild(picker);
                return wrapper;
            }
            case 'asset':
            case 'reference': {
                const wrapper = document.createElement('div');
                wrapper.className = 'suggested';
                const input = document.createElement('input');
                input.type = 'text';
                input.value = String(node.value ?? '');
                input.disabled = readOnly;
                input.dataset.path = pathKey(path);
                input.spellcheck = false;

                const listId = `suggestions-${hash(pathKey(path))}`;
                const list = document.createElement('datalist');
                list.id = listId;
                for (const suggestion of node.suggestions ?? []) {
                    const option = document.createElement('option');
                    option.value = suggestion;
                    list.appendChild(option);
                }
                input.setAttribute('list', listId);
                input.addEventListener('change', () => this.commit(path, input.value, 'suggestion', 'end'));
                wrapper.appendChild(input);
                wrapper.appendChild(list);

                if (node.assetState) {
                    const state = document.createElement('span');
                    state.className = `asset-state ${node.assetState}`;
                    state.textContent = node.assetState === 'exists' ? 'found' : node.assetState === 'missing' ? 'missing' : 'unknown';
                    wrapper.appendChild(state);
                }
                return wrapper;
            }
            case 'textarea': {
                const input = document.createElement('textarea');
                input.value = String(node.value ?? '');
                input.disabled = readOnly;
                input.rows = 3;
                input.dataset.path = pathKey(path);
                if (node.monospace) {
                    input.classList.add('monospace');
                }
                input.addEventListener('change', () => this.commit(path, input.value, 'textarea', 'end'));
                return input;
            }
            default: {
                const input = document.createElement('input');
                input.type = 'text';
                input.value = node.value === undefined || node.value === null ? '' : String(node.value);
                input.disabled = readOnly;
                input.dataset.path = pathKey(path);
                if (node.monospace) {
                    input.classList.add('monospace');
                }
                input.addEventListener('change', () => this.commit(path, input.value, 'text', 'end'));
                return input;
            }
        }
    }

    private commit(path: PathSegments, value: unknown, gesture: string, phase: 'change' | 'end'): void {
        api.postMessage({ type: 'set', path, value, gesture, phase } satisfies WebviewToHostMessage);
        if (phase === 'change') {
            this.gesture = gesture;
            this.gesturePath = pathKey(path);
            return;
        }
        if (this.gesture === gesture) {
            this.gesture = undefined;
            this.gesturePath = undefined;
            this.applyDeferred();
        }
    }
}

function pageKey(side: FormSide, path: PathSegments): string {
    return `${side}:${pathKey(path)}`;
}

function sameSections(left: FormProjection, right: FormProjection): boolean {
    return left.state === right.state && JSON.stringify(left.sections) === JSON.stringify(right.sections);
}

function hash(value: string): string {
    let result = 0;
    for (let index = 0; index < value.length; index++) {
        result = (result * 31 + value.charCodeAt(index)) | 0;
    }
    return Math.abs(result).toString(36);
}

function cssEscape(value: string): string {
    return value.replace(/["\\]/g, character => `\\${character}`);
}

const app = document.getElementById('app');
const view = new FormView(app as HTMLElement);

window.addEventListener('message', event => {
    const message = event.data as { type?: string; [key: string]: unknown };
    if (!message || typeof message.type !== 'string') {
        return;
    }
    switch (message.type) {
        case 'project':
            view.setProjection(message.projection as FormProjection);
            return;
        case 'children':
            view.setChildren(
                message.side as FormSide,
                message.path as PathSegments,
                message.page as ChildrenPage
            );
            return;
        case 'reference':
            view.setReference(message.projection as FormProjection | undefined, message.name as string | undefined);
            return;
        case 'error':
            view.showError(String(message.message ?? 'The form could not apply that change.'));
            return;
        case 'info':
            view.showInfo(String(message.message ?? ''));
            return;
        default:
            return;
    }
});

api.postMessage({ type: 'ready' } satisfies WebviewToHostMessage);
