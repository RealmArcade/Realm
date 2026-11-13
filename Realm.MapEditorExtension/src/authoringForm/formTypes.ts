export type PathSegments = (string | number)[];

export type LeafWidget =
    | 'text'
    | 'textarea'
    | 'number'
    | 'slider'
    | 'checkbox'
    | 'select'
    | 'flags'
    | 'asset'
    | 'reference'
    | 'color'
    | 'tags'
    | 'readonly';

export interface SelectOption {
    value: string | number;
    label: string;
    description?: string;
}

export interface FlagOption {
    bit: number;
    label: string;
}

export type AssetState = 'exists' | 'missing' | 'unknown';

export interface LeafNode {
    kind: 'leaf';
    path: PathSegments;
    key?: string;
    label: string;
    description?: string;
    widget: LeafWidget;
    unit?: string;
    minimum?: number;
    maximum?: number;
    step?: number;
    options?: SelectOption[];
    flags?: FlagOption[];
    suggestions?: string[];
    assetState?: AssetState;
    readOnly?: boolean;
    monospace?: boolean;
    multiline?: boolean;
    value: unknown;
    present: boolean;
}

export interface ContainerNode {
    kind: 'section' | 'group' | 'list' | 'record' | 'entry';
    path: PathSegments;
    key?: string;
    label: string;
    description?: string;
    childCount: number;
    identity?: string;
    dictionary?: boolean;
    missingCount?: number;
}

export type FormNode = LeafNode | ContainerNode;

export interface ChildrenPage {
    nodes: FormNode[];
    offset: number;
    total: number;
}

export interface FormProjection {
    version: number;
    state: 'ok' | 'unparsable';
    title: string;
    subtitle: string;
    sections: FormNode[];
    collapsed: string[];
    writable: boolean;
    stateMessage?: string;
}

export type FormSide = 'edited' | 'reference';

export type WebviewToHostMessage =
    | { type: 'ready' }
    | { type: 'expand'; side: FormSide; path: PathSegments; offset: number; limit: number }
    | { type: 'set'; path: PathSegments; value: unknown; gesture: string; phase: 'change' | 'end' }
    | { type: 'flush' }
    | { type: 'layout'; collapsed: string[] }
    | { type: 'compare' }
    | { type: 'closeCompare' }
    | { type: 'reveal'; path: PathSegments }
    | { type: 'openText' }
    | { type: 'addEntry'; path: PathSegments; keyName: string }
    | { type: 'renameEntry'; path: PathSegments; keyName: string }
    | { type: 'removeEntry'; path: PathSegments }
    | { type: 'addItem'; path: PathSegments }
    | { type: 'removeItem'; path: PathSegments };

export type HostToWebviewMessage =
    | { type: 'project'; projection: FormProjection }
    | { type: 'children'; side: FormSide; path: PathSegments; page: ChildrenPage }
    | { type: 'reference'; projection: FormProjection | undefined; name?: string }
    | { type: 'error'; message: string }
    | { type: 'info'; message: string }
    | { type: 'revealed' };

export function pathKey(path: PathSegments): string {
    return JSON.stringify(path);
}
