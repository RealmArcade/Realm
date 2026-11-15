import * as vscode from 'vscode';
import { JsonSnapshotCache } from './jsonDocument';
import { MapSchemaRegistry } from './schemaRegistry';
import { DocumentIndexCache } from './documentIndex';
import { MapAssetIndex } from './assetIndex';
import { ColourSwatchStore } from './colourSwatch';
import { AuthoringSettings, onAuthoringSettingsChanged, readAuthoringSettings } from './settings';
import { SchemaCompletionProvider } from './completionProvider';
import { SchemaHoverProvider } from './hoverProvider';
import { SchemaDiagnostics } from './diagnostics';
import { MissingFieldsCodeActionProvider } from './codeActions';
import { AuthoringCodeLensProvider } from './codeLensProvider';
import { SchemaFormattingProvider } from './formatting';
import { AuthoringDecorations } from './decorations';
import { registerAuthoringCommands } from './commands';
import { MetadataFormProvider } from '../authoringForm/formProvider';

export interface AuthoringServices {
    snapshots: JsonSnapshotCache;
    registry: MapSchemaRegistry;
    documentIndex: DocumentIndexCache;
    assets: MapAssetIndex;
    swatches: ColourSwatchStore;
}

export function createAuthoringServices(context: vscode.ExtensionContext): AuthoringServices {
    const registry = new MapSchemaRegistry(context.extensionUri, context.extension.packageJSON);
    const snapshots = new JsonSnapshotCache();
    const documentIndex = new DocumentIndexCache(snapshots, registry);
    const assets = new MapAssetIndex();
    const swatches = new ColourSwatchStore(context.globalStorageUri);
    context.subscriptions.push(registry, snapshots, documentIndex, assets, swatches);
    return { snapshots, registry, documentIndex, assets, swatches };
}

export function registerAuthoringFeatures(context: vscode.ExtensionContext, services: AuthoringServices): void {
    const filters = services.registry.documentFilters();
    if (filters.length === 0) {
        return;
    }

    const initialSettings = readAuthoringSettings();
    const diagnostics = new SchemaDiagnostics(services.snapshots, services.registry);
    const lenses = new AuthoringCodeLensProvider(
        services.snapshots,
        services.registry,
        services.assets,
        initialSettings
    );
    const decorations = new AuthoringDecorations(
        services.snapshots,
        services.registry,
        services.swatches,
        context.extensionUri,
        initialSettings
    );

    context.subscriptions.push(
        vscode.languages.registerCompletionItemProvider(
            filters,
            new SchemaCompletionProvider(services.snapshots, services.registry, services.documentIndex, services.assets),
            '"',
            '/'
        ),
        vscode.languages.registerHoverProvider(
            filters,
            new SchemaHoverProvider(
                services.snapshots,
                services.registry,
                services.documentIndex,
                services.assets,
                () => readAuthoringSettings().hoverEnabled
            )
        ),
        vscode.languages.registerCodeActionsProvider(
            filters,
            new MissingFieldsCodeActionProvider(services.snapshots, services.registry),
            { providedCodeActionKinds: MissingFieldsCodeActionProvider.providedCodeActionKinds }
        ),
        vscode.languages.registerCodeLensProvider(filters, lenses),
        vscode.languages.registerDocumentFormattingEditProvider(
            filters,
            new SchemaFormattingProvider(services.snapshots, services.registry)
        )
    );

    context.subscriptions.push(...diagnostics.register());
    context.subscriptions.push(lenses, ...lenses.register());
    context.subscriptions.push(decorations, ...decorations.register());
    context.subscriptions.push(...registerAuthoringCommands(services.snapshots, services.registry, services.swatches));

    context.subscriptions.push(MetadataFormProvider.register(context, {
        snapshots: services.snapshots,
        registry: services.registry,
        documentIndex: services.documentIndex,
        assets: services.assets
    }));

    context.subscriptions.push(onAuthoringSettingsChanged((settings: AuthoringSettings) => {
        lenses.updateSettings(settings);
        decorations.updateSettings(settings);
    }));
}
