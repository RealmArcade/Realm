import * as vscode from 'vscode';
import { JsonSnapshotCache } from './jsonDocument';
import { MapSchemaRegistry } from './schemaRegistry';
import { SchemaDiagnostics } from './diagnostics';

export interface AuthoringServices {
    snapshots: JsonSnapshotCache;
    registry: MapSchemaRegistry;
}

export function createAuthoringServices(context: vscode.ExtensionContext): AuthoringServices {
    const registry = new MapSchemaRegistry(context.extensionUri, context.extension.packageJSON);
    const snapshots = new JsonSnapshotCache();
    context.subscriptions.push(registry, snapshots);
    return { snapshots, registry };
}

export function registerAuthoringFeatures(context: vscode.ExtensionContext, services: AuthoringServices): void {
    if (services.registry.documentFilters().length === 0) {
        return;
    }

    const diagnostics = new SchemaDiagnostics(services.snapshots, services.registry);
    context.subscriptions.push(...diagnostics.register());
}
