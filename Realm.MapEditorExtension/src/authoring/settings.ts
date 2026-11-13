import * as vscode from 'vscode';

export const authoringConfigurationSection = 'realm.mapAuthoring';

export interface AuthoringSettings {
    hoverEnabled: boolean;
    decorationsEnabled: boolean;
    codeLensEnabled: boolean;
}

export function readAuthoringSettings(): AuthoringSettings {
    const configuration = vscode.workspace.getConfiguration(authoringConfigurationSection);
    return {
        hoverEnabled: configuration.get<boolean>('hover', true),
        decorationsEnabled: configuration.get<boolean>('decorations', true),
        codeLensEnabled: configuration.get<boolean>('codeLens', true)
    };
}

export function onAuthoringSettingsChanged(listener: (settings: AuthoringSettings) => void): vscode.Disposable {
    return vscode.workspace.onDidChangeConfiguration(event => {
        if (event.affectsConfiguration(authoringConfigurationSection)) {
            listener(readAuthoringSettings());
        }
    });
}
