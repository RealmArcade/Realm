import * as fs from 'fs';
import * as vscode from 'vscode';

export interface ParsedColourLiteral {
    red: number;
    green: number;
    blue: number;
    alpha: number;
    hasAlpha: boolean;
}

const colourLiteralPattern = /^#([0-9a-fA-F]{3,8})$/;

export function parseColourLiteral(literal: string): ParsedColourLiteral | undefined {
    const match = colourLiteralPattern.exec(literal.trim());
    if (!match) {
        return undefined;
    }
    let digits = match[1];
    if (digits.length === 3 || digits.length === 4) {
        digits = digits.split('').map(digit => `${digit}${digit}`).join('');
    }
    if (digits.length !== 6 && digits.length !== 8) {
        return undefined;
    }
    const hasAlpha = digits.length === 8;
    return {
        red: parseInt(digits.slice(0, 2), 16),
        green: parseInt(digits.slice(2, 4), 16),
        blue: parseInt(digits.slice(4, 6), 16),
        alpha: hasAlpha ? parseInt(digits.slice(6, 8), 16) : 255,
        hasAlpha
    };
}

export function formatColourLiteral(colour: ParsedColourLiteral, withAlpha: boolean): string {
    const hex = (value: number) => value.toString(16).padStart(2, '0').toUpperCase();
    const base = `#${hex(colour.red)}${hex(colour.green)}${hex(colour.blue)}`;
    return withAlpha ? `${base}${hex(colour.alpha)}` : base;
}

const paletteLiterals = [
    '#FFFFFF', '#C8C8C8', '#909090', '#585858', '#303030', '#000000',
    '#FF4D4D', '#FF8A3D', '#FFC93D', '#D7F53D', '#7DE33D', '#3DE38A',
    '#3DE3D7', '#3DA7E3', '#3D6BE3', '#7A3DE3', '#C23DE3', '#E33DA7',
    '#FFD9D9', '#FFE6D9', '#FFF6D9', '#EEFFD9', '#D9FFEA', '#D9FFFB',
    '#D9EEFF', '#D9E2FF', '#E6D9FF', '#F7D9FF', '#FFD9F1', '#8C1F1F'
];

export class ColourSwatchStore implements vscode.Disposable {
    private readonly swatches = new Map<string, vscode.Uri>();
    private readonly folder: vscode.Uri;

    constructor(storageUri: vscode.Uri) {
        this.folder = vscode.Uri.joinPath(storageUri, 'swatches');
    }

    uriFor(literal: string): vscode.Uri | undefined {
        const colour = parseColourLiteral(literal);
        if (!colour) {
            return undefined;
        }
        const key = formatColourLiteral(colour, colour.hasAlpha).toLowerCase();
        const cached = this.swatches.get(key);
        if (cached) {
            return cached;
        }

        const uri = vscode.Uri.joinPath(this.folder, `${key.replace('#', '')}.svg`);
        try {
            fs.mkdirSync(this.folder.fsPath, { recursive: true });
            if (!fs.existsSync(uri.fsPath)) {
                fs.writeFileSync(uri.fsPath, swatchSvg(colour), 'utf8');
            }
        } catch {
            return undefined;
        }
        this.swatches.set(key, uri);
        return uri;
    }

    dispose(): void {
        this.swatches.clear();
    }
}

function swatchSvg(colour: ParsedColourLiteral): string {
    const opacity = (colour.alpha / 255).toFixed(3);
    return [
        '<svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 14 14">',
        '<rect x="1.5" y="1.5" width="11" height="11" rx="2" ',
        `fill="rgb(${colour.red},${colour.green},${colour.blue})" fill-opacity="${opacity}" `,
        'stroke="#00000066" stroke-width="1"/>',
        '</svg>'
    ].join('');
}

export function paletteChoices(current: ParsedColourLiteral | undefined): string[] {
    const withAlpha = current?.hasAlpha ?? false;
    return paletteLiterals.map(literal => {
        const colour = parseColourLiteral(literal);
        if (!colour) {
            return literal;
        }
        return formatColourLiteral({ ...colour, alpha: current?.alpha ?? 255 }, withAlpha);
    });
}
