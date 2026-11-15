import * as path from 'path';
import * as vscode from 'vscode';
import { JsonSnapshotCache, nodeRange, resolveCursorLocation } from './jsonDocument';
import { MapSchemaRegistry } from './schemaRegistry';
import { ColourSwatchStore, formatColourLiteral, paletteChoices, parseColourLiteral } from './colourSwatch';

interface ColourQuickPickItem extends vscode.QuickPickItem {
    literal?: string;
    custom?: boolean;
}

async function pickColourLiteral(current: string, swatches: ColourSwatchStore): Promise<string | undefined> {
    const parsed = parseColourLiteral(current);
    const items: ColourQuickPickItem[] = paletteChoices(parsed).map(literal => ({
        label: literal,
        literal,
        iconPath: swatches.uriFor(literal),
        description: literal === current ? 'current value' : undefined
    }));
    items.push({ label: '$(edit) Enter a colour value…', custom: true, alwaysShow: true });

    const picked = await vscode.window.showQuickPick(items, {
        title: 'Pick a colour',
        placeHolder: `Current value ${current}`,
        matchOnDescription: true
    });
    if (!picked) {
        return undefined;
    }
    if (picked.literal) {
        return picked.literal;
    }
    if (!picked.custom) {
        return undefined;
    }

    const typed = await vscode.window.showInputBox({
        title: 'Colour value',
        value: current,
        prompt: 'A hex literal such as #4AD4E6 or #4AD4E655 with alpha.',
        validateInput: candidate => (parseColourLiteral(candidate) ? undefined : 'Expected a hex colour literal such as #4AD4E6 or #4AD4E655.')
    });
    if (!typed) {
        return undefined;
    }
    const colour = parseColourLiteral(typed);
    return colour ? formatColourLiteral(colour, colour.hasAlpha) : undefined;
}

export function registerAuthoringCommands(
    snapshots: JsonSnapshotCache,
    registry: MapSchemaRegistry,
    swatches: ColourSwatchStore
): vscode.Disposable[] {
    return [
        vscode.commands.registerCommand('realm.metadata.openAssetPreview', async (
            documentUri: string,
            viewType: string,
            relativePath: string
        ) => {
            const mapFolder = path.dirname(vscode.Uri.parse(documentUri).fsPath);
            const assetUri = vscode.Uri.file(path.join(mapFolder, relativePath));
            await vscode.commands.executeCommand('vscode.openWith', assetUri, viewType);
        }),

        vscode.commands.registerCommand('realm.metadata.explainMissingAsset', async (
            documentUri: string,
            assetPath: string,
            folders: string[]
        ) => {
            const mapFolder = path.dirname(vscode.Uri.parse(documentUri).fsPath);
            await vscode.window.showWarningMessage(
                `No file named "${assetPath}" is under ${folders.join(', ')} in ${mapFolder}.`
            );
        }),

        vscode.commands.registerCommand('realm.metadata.pickColor', async (documentUri: string, offset: number) => {
            const uri = vscode.Uri.parse(documentUri);
            const document = await vscode.workspace.openTextDocument(uri);
            const location = resolveCursorLocation(document, document.positionAt(offset), snapshots, registry);
            if (!location || !location.valueNode || typeof location.valueNode.value !== 'string') {
                return;
            }
            if (location.schemaDocument.control(location.valueSchema) !== 'color') {
                return;
            }

            const current = location.valueNode.value;
            const chosen = await pickColourLiteral(current, swatches);
            if (!chosen || chosen === current) {
                return;
            }

            const fresh = resolveCursorLocation(document, document.positionAt(offset), snapshots, registry);
            if (!fresh || !fresh.valueNode || typeof fresh.valueNode.value !== 'string') {
                return;
            }
            const edit = new vscode.WorkspaceEdit();
            edit.replace(uri, nodeRange(document, fresh.valueNode), JSON.stringify(chosen));
            await vscode.workspace.applyEdit(edit);
        })
    ];
}
