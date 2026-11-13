import * as vscode from 'vscode';
import { JsonSnapshotCache } from './jsonDocument';
import { MapSchemaRegistry } from './schemaRegistry';
import { DocumentIndexCache } from './documentIndex';
import { MapAssetIndex } from './assetIndex';
import { SchemaCompletionProvider } from './completionProvider';
import { SchemaHoverProvider } from './hoverProvider';
import { SchemaDiagnostics } from './diagnostics';
import { readAuthoringSettings } from './settings';

export interface AuthoringServices {
    snapshots: JsonSnapshotCache;
    registry: MapSchemaRegistry;
    documentIndex: DocumentIndexCache;
    assets: MapAssetIndex;
}

export function createAuthoringServices(context: vscode.ExtensionContext): AuthoringServices {
    const registry = new MapSchemaRegistry(context.extensionUri, context.extension.packageJSON);
    const snapshots = new JsonSnapshotCache();
    const documentIndex = new DocumentIndexCache(snapshots, registry);
    const assets = new MapAssetIndex();
    context.subscriptions.push(registry, snapshots, documentIndex, assets);
    return { snapshots, registry, documentIndex, assets };
}

export function registerAuthoringFeatures(context: vscode.ExtensionContext, services: AuthoringServices): void {
    const filters = services.registry.documentFilters();
    if (filters.length === 0) {
        return;
    }

    const diagnostics = new SchemaDiagnostics(services.snapshots, services.registry);

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
        )
    );

    context.subscriptions.push(...diagnostics.register());
}
