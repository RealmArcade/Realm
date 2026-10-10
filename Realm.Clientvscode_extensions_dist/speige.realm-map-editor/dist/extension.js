"use strict";
var __create = Object.create;
var __defProp = Object.defineProperty;
var __getOwnPropDesc = Object.getOwnPropertyDescriptor;
var __getOwnPropNames = Object.getOwnPropertyNames;
var __getProtoOf = Object.getPrototypeOf;
var __hasOwnProp = Object.prototype.hasOwnProperty;
var __export = (target, all) => {
  for (var name in all)
    __defProp(target, name, { get: all[name], enumerable: true });
};
var __copyProps = (to, from, except, desc) => {
  if (from && typeof from === "object" || typeof from === "function") {
    for (let key of __getOwnPropNames(from))
      if (!__hasOwnProp.call(to, key) && key !== except)
        __defProp(to, key, { get: () => from[key], enumerable: !(desc = __getOwnPropDesc(from, key)) || desc.enumerable });
  }
  return to;
};
var __toESM = (mod, isNodeMode, target) => (target = mod != null ? __create(__getProtoOf(mod)) : {}, __copyProps(
  // If the importer is in node compatibility mode or this is not an ESM
  // file that has been converted to a CommonJS file using a Babel-
  // compatible transform (i.e. "__esModule" has not been set), then set
  // "default" to the CommonJS "module.exports" for node compatibility.
  isNodeMode || !mod || !mod.__esModule ? __defProp(target, "default", { value: mod, enumerable: true }) : target,
  mod
));
var __toCommonJS = (mod) => __copyProps(__defProp({}, "__esModule", { value: true }), mod);

// src/extension.ts
var extension_exports = {};
__export(extension_exports, {
  activate: () => activate,
  deactivate: () => deactivate,
  sendGodotIpc: () => sendGodotIpc
});
module.exports = __toCommonJS(extension_exports);
var vscode6 = __toESM(require("vscode"));
var path7 = __toESM(require("path"));
var fs7 = __toESM(require("fs"));
var http = __toESM(require("http"));

// src/editorProvider.ts
var vscode = __toESM(require("vscode"));
var path = __toESM(require("path"));
var fs = __toESM(require("fs"));
var RealmMapEditorProvider = class _RealmMapEditorProvider {
  constructor(context) {
    this.context = context;
  }
  static register(context) {
    const provider = new _RealmMapEditorProvider(context);
    return vscode.window.registerCustomEditorProvider(_RealmMapEditorProvider.viewType, provider);
  }
  static {
    this.viewType = "realm.mapEditor";
  }
  async resolveCustomTextEditor(document, webviewPanel, _token) {
    webviewPanel.webview.options = {
      enableScripts: true
    };
    webviewPanel.webview.html = this.getHtmlForWebview(webviewPanel.webview);
    const changeDocumentSubscription = vscode.workspace.onDidChangeTextDocument((e) => {
      if (e.document.uri.toString() === document.uri.toString()) {
        webviewPanel.webview.postMessage({
          type: "update",
          text: this.getMergedDocumentText(document)
        });
      }
    });
    const manifestWatcher = vscode.workspace.createFileSystemWatcher(
      new vscode.RelativePattern(path.dirname(document.uri.fsPath), "manifest.json")
    );
    const onManifestChange = () => {
      webviewPanel.webview.postMessage({
        type: "update",
        text: this.getMergedDocumentText(document)
      });
    };
    manifestWatcher.onDidChange(onManifestChange);
    manifestWatcher.onDidCreate(onManifestChange);
    manifestWatcher.onDidDelete(onManifestChange);
    webviewPanel.onDidDispose(() => {
      changeDocumentSubscription.dispose();
      manifestWatcher.dispose();
    });
    webviewPanel.webview.onDidReceiveMessage(async (e) => {
      switch (e.type) {
        case "ready":
          webviewPanel.webview.postMessage({
            type: "update",
            text: this.getMergedDocumentText(document)
          });
          break;
        case "change":
          const applied = await this.updateTextDocument(document, e.text);
          if (!applied) {
            console.warn("WorkspaceEdit was not applied, resyncing webview");
            webviewPanel.webview.postMessage({
              type: "update",
              text: this.getMergedDocumentText(document)
            });
          }
          break;
        case "browseFile":
          await this.handleBrowseFile(webviewPanel.webview, e.fieldId, e.fieldClass, e.fieldIndex, e.fileTypes, e.assetType);
          break;
        case "resolvePath":
          const absPath = this.resolveGodotPath(e.path, document.uri);
          const webviewUri = absPath ? webviewPanel.webview.asWebviewUri(vscode.Uri.file(absPath)).toString() : "";
          webviewPanel.webview.postMessage({
            type: "resolvePathResult",
            requestId: e.requestId,
            uri: webviewUri
          });
          break;
        case "pruneDomain":
          await this.handlePruneDomain(webviewPanel.webview, e.domain, document);
          break;
      }
    });
  }
  async handlePruneDomain(webview, domain, document) {
    try {
      let addIdentifier2 = function(val) {
        if (!val || typeof val !== "string")
          return;
        const trimmed = val.trim().toLowerCase();
        if (!trimmed)
          return;
        placedIds.add(trimmed);
        const normalized = trimmed.replace(/\\/g, "/");
        placedIds.add(normalized);
        const clean = normalized.replace(/^(res:\/\/|user:\/\/|assets\/)/i, "");
        placedIds.add(clean);
        const baseName = path.basename(normalized);
        placedIds.add(baseName);
        const withoutExt = baseName.replace(/\.[^/.]+$/, "");
        if (withoutExt)
          placedIds.add(withoutExt);
      }, scanCsScripts2 = function(dir) {
        if (!fs.existsSync(dir))
          return;
        try {
          const entries = fs.readdirSync(dir, { withFileTypes: true });
          for (const entry of entries) {
            if (entry.isDirectory()) {
              if (["bin", "obj", ".godot", ".git", "lib", "node_modules"].includes(entry.name.toLowerCase())) {
                continue;
              }
              scanCsScripts2(path.join(dir, entry.name));
            } else if (entry.isFile() && entry.name.toLowerCase().endsWith(".cs")) {
              try {
                const content = fs.readFileSync(path.join(dir, entry.name), "utf8");
                const strRegex = /"([^"\\]*(?:\\.[^"\\]*)*)"/g;
                let match;
                while ((match = strRegex.exec(content)) !== null) {
                  const val = match[1];
                  if (val && val.length < 200) {
                    addIdentifier2(val);
                  }
                }
              } catch {
              }
            }
          }
        } catch {
        }
      }, isEntityReferenced2 = function(item) {
        if (!item || typeof item !== "object")
          return false;
        const candidates = [
          item.TemplateID,
          item.TemplateId,
          item.UnitId,
          item.PropId,
          item.DecalId,
          item.WeaponId,
          item.AbilityId,
          item.UpgradeId,
          item.ItemId,
          item.Name,
          item.ModelPath,
          item.DropModelPath,
          item.PortraitModelPath,
          item.MissileModelPath,
          item.ProjectileModelPath,
          item.ProjectileModel,
          item.EffectModel
        ];
        for (const c of candidates) {
          if (c && typeof c === "string") {
            const trimmed = c.trim().toLowerCase();
            if (placedIds.has(trimmed))
              return true;
            const normalized = trimmed.replace(/\\/g, "/");
            if (placedIds.has(normalized))
              return true;
            const clean = normalized.replace(/^(res:\/\/|user:\/\/|assets\/)/i, "");
            if (placedIds.has(clean))
              return true;
            const baseName = path.basename(normalized);
            if (placedIds.has(baseName))
              return true;
            const withoutExt = baseName.replace(/\.[^/.]+$/, "");
            if (withoutExt && placedIds.has(withoutExt))
              return true;
          }
        }
        return false;
      };
      var addIdentifier = addIdentifier2, scanCsScripts = scanCsScripts2, isEntityReferenced = isEntityReferenced2;
      let metadata;
      try {
        metadata = JSON.parse(document.getText());
      } catch (err) {
        vscode.window.showErrorMessage(`Cannot prune ${domain}: metadata.json is invalid JSON.`);
        return;
      }
      const targetDir = path.dirname(document.uri.fsPath);
      const terrainPath = path.join(targetDir, "terrain.json");
      if (!fs.existsSync(terrainPath)) {
        vscode.window.showWarningMessage(`Cannot prune ${domain}: terrain.json not found in map folder.`);
        return;
      }
      let terrainData;
      try {
        terrainData = JSON.parse(fs.readFileSync(terrainPath, "utf8"));
      } catch (err) {
        vscode.window.showErrorMessage(`Cannot prune ${domain}: failed to parse terrain.json: ${err.message}`);
        return;
      }
      const placedIds = /* @__PURE__ */ new Set();
      if (Array.isArray(terrainData.Units)) {
        terrainData.Units.forEach((u) => {
          if (u) {
            addIdentifier2(u.TemplateId);
            addIdentifier2(u.TemplateID);
            addIdentifier2(u.UnitId);
            addIdentifier2(u.Name);
            addIdentifier2(u.ModelPath);
          }
        });
      }
      if (Array.isArray(terrainData.Props)) {
        terrainData.Props.forEach((p) => {
          if (p) {
            addIdentifier2(p.TemplateId);
            addIdentifier2(p.TemplateID);
            addIdentifier2(p.PropId);
            addIdentifier2(p.Name);
            addIdentifier2(p.ModelPath);
          }
        });
      }
      if (Array.isArray(terrainData.Decals)) {
        terrainData.Decals.forEach((d) => {
          if (d) {
            addIdentifier2(d.TemplateId);
            addIdentifier2(d.TemplateID);
            addIdentifier2(d.DecalId);
            addIdentifier2(d.Name);
          }
        });
      }
      scanCsScripts2(targetDir);
      let expanded = true;
      let loopCount = 0;
      while (expanded && loopCount < 50) {
        expanded = false;
        loopCount++;
        const prevSize = placedIds.size;
        if (!metadata.Templates) {
          metadata.Templates = {};
        }
        const templates2 = metadata.Templates;
        const allEntities = [
          ...templates2.Units || [],
          ...templates2.Buildings || [],
          ...templates2.Resources || [],
          ...templates2.Props || []
        ];
        for (const entity of allEntities) {
          if (isEntityReferenced2(entity)) {
            addIdentifier2(entity.TemplateID || entity.UnitId);
            addIdentifier2(entity.Name);
            addIdentifier2(entity.ModelPath);
            if (Array.isArray(entity.BuildOptions)) {
              entity.BuildOptions.forEach((opt) => addIdentifier2(opt));
            }
            if (Array.isArray(entity.Weapons)) {
              entity.Weapons.forEach((w) => addIdentifier2(w));
            }
            if (Array.isArray(entity.Abilities)) {
              entity.Abilities.forEach((a) => addIdentifier2(a));
            }
            if (Array.isArray(entity.Upgrades)) {
              entity.Upgrades.forEach((u) => addIdentifier2(u));
            }
            if (Array.isArray(entity.StartingItems)) {
              entity.StartingItems.forEach((i) => addIdentifier2(i));
            }
            if (Array.isArray(entity.Items)) {
              entity.Items.forEach((i) => addIdentifier2(i));
            }
          }
        }
        if (Array.isArray(templates2.Abilities)) {
          for (const abi of templates2.Abilities) {
            if (isEntityReferenced2(abi)) {
              addIdentifier2(abi.TemplateID || abi.AbilityId);
              addIdentifier2(abi.Name);
              if (abi.SummonedUnitId)
                addIdentifier2(abi.SummonedUnitId);
              if (Array.isArray(abi.GrantedWeapons))
                abi.GrantedWeapons.forEach((w) => addIdentifier2(w));
            }
          }
        }
        if (Array.isArray(templates2.Upgrades)) {
          for (const up of templates2.Upgrades) {
            if (isEntityReferenced2(up)) {
              addIdentifier2(up.TemplateID || up.UpgradeId);
              addIdentifier2(up.Name);
              if (Array.isArray(up.GrantedWeapons))
                up.GrantedWeapons.forEach((w) => addIdentifier2(w));
              if (Array.isArray(up.AffectedUnitIds))
                up.AffectedUnitIds.forEach((uid) => addIdentifier2(uid));
            }
          }
        }
        if (Array.isArray(templates2.Items)) {
          for (const itm of templates2.Items) {
            if (isEntityReferenced2(itm)) {
              addIdentifier2(itm.TemplateID || itm.ItemId);
              addIdentifier2(itm.Name);
              if (Array.isArray(itm.Abilities))
                itm.Abilities.forEach((a) => addIdentifier2(a));
              if (Array.isArray(itm.GrantedWeapons))
                itm.GrantedWeapons.forEach((w) => addIdentifier2(w));
            }
          }
        }
        if (placedIds.size > prevSize) {
          expanded = true;
        }
      }
      if (!metadata.Templates) {
        metadata.Templates = {};
      }
      const templates = metadata.Templates;
      let initialCount = 0;
      let finalCount = 0;
      if (domain === "units") {
        initialCount = (templates.Units || []).length;
        templates.Units = (templates.Units || []).filter((u) => isEntityReferenced2(u));
        finalCount = templates.Units.length;
      } else if (domain === "buildings") {
        initialCount = (templates.Buildings || []).length;
        templates.Buildings = (templates.Buildings || []).filter((b) => isEntityReferenced2(b));
        finalCount = templates.Buildings.length;
      } else if (domain === "resources") {
        initialCount = (templates.Resources || []).length;
        templates.Resources = (templates.Resources || []).filter((r) => isEntityReferenced2(r));
        finalCount = templates.Resources.length;
      } else if (domain === "props") {
        initialCount = (templates.Props || []).length;
        templates.Props = (templates.Props || []).filter((p) => isEntityReferenced2(p));
        finalCount = templates.Props.length;
      } else if (domain === "weapons") {
        initialCount = (templates.Weapons || []).length;
        templates.Weapons = (templates.Weapons || []).filter((w) => isEntityReferenced2(w));
        finalCount = templates.Weapons.length;
      } else if (domain === "abilities") {
        initialCount = (templates.Abilities || []).length;
        templates.Abilities = (templates.Abilities || []).filter((a) => isEntityReferenced2(a));
        finalCount = templates.Abilities.length;
      } else if (domain === "upgrades") {
        initialCount = (templates.Upgrades || []).length;
        templates.Upgrades = (templates.Upgrades || []).filter((u) => isEntityReferenced2(u));
        finalCount = templates.Upgrades.length;
      } else if (domain === "items") {
        initialCount = (templates.Items || []).length;
        templates.Items = (templates.Items || []).filter((i) => isEntityReferenced2(i));
        finalCount = templates.Items.length;
      }
      const removedCount = initialCount - finalCount;
      await this.saveMetadataViaGodotIpc(document, metadata, webview);
      if (removedCount > 0) {
        vscode.window.showInformationMessage(`Pruned ${removedCount} unplaced item(s) from ${domain}.`);
      } else {
        vscode.window.showInformationMessage(`No unplaced items found in ${domain}. All items are placed or referenced on terrain.`);
      }
    } catch (err) {
      vscode.window.showErrorMessage(`Failed to prune ${domain}: ${err.message}`);
    }
  }
  async handleBrowseFile(webview, fieldId, fieldClass, fieldIndex, fileTypes, assetType) {
    webview.postMessage({
      type: "browseFileFallback",
      fieldId,
      fieldClass,
      fieldIndex,
      assetType,
      accept: fileTypes ? fileTypes.map((ext) => "." + ext.replace(/^\./, "")).join(",") : "*"
    });
  }
  resolveGodotPath(godotPath, documentUri) {
    if (!godotPath) {
      return null;
    }
    let cleanPath = godotPath.trim();
    if (cleanPath.startsWith("res://")) {
      cleanPath = cleanPath.substring(6);
    }
    const candidateSubDirs = [
      "",
      path.join("Assets", "models", "units"),
      path.join("Assets", "models", "buildings"),
      path.join("Assets", "models", "resources"),
      path.join("Assets", "models", "props"),
      path.join("Assets", "decals"),
      path.join("Assets", "icons"),
      path.join("Assets", "textures"),
      path.join("Assets", "skyboxes"),
      path.join("Assets", "vfx"),
      path.join("Assets", "audio", "sfx")
    ];
    const docDir = path.dirname(documentUri.fsPath);
    const searchRoots = [];
    let currentDir = docDir;
    while (true) {
      searchRoots.push(currentDir);
      const projectFile = path.join(currentDir, "project.godot");
      if (fs.existsSync(projectFile)) {
        break;
      }
      const parent = path.dirname(currentDir);
      if (parent === currentDir) {
        break;
      }
      currentDir = parent;
    }
    const workspaceFolders = vscode.workspace.workspaceFolders;
    if (workspaceFolders) {
      for (const folder of workspaceFolders) {
        searchRoots.push(folder.uri.fsPath);
        searchRoots.push(path.join(folder.uri.fsPath, "Realm.Client"));
      }
    }
    for (const root of searchRoots) {
      for (const subDir of candidateSubDirs) {
        const fullCandidate = subDir ? path.join(root, subDir, cleanPath) : path.join(root, cleanPath);
        if (fs.existsSync(fullCandidate)) {
          return fullCandidate;
        }
      }
    }
    return null;
  }
  async updateTextDocument(document, text) {
    const edit = new vscode.WorkspaceEdit();
    const fullRange = new vscode.Range(
      document.positionAt(0),
      document.positionAt(document.getText().length)
    );
    edit.replace(
      document.uri,
      fullRange,
      text
    );
    try {
      const applied = await vscode.workspace.applyEdit(edit);
      if (!applied) {
        console.error("updateTextDocument: applyEdit returned false");
      }
      return applied;
    } catch (err) {
      console.error("updateTextDocument error:", err);
      return false;
    }
  }
  async saveMetadataViaGodotIpc(document, metadata, webview) {
    const rawJson = typeof metadata === "string" ? metadata : JSON.stringify(metadata);
    try {
      const response = await sendGodotIpc({
        action: "formatAndSaveJson",
        filePath: document.uri.fsPath,
        content: rawJson
      });
      if (response && response.success && typeof response.formattedContent === "string") {
        await this.updateTextDocument(document, response.formattedContent);
        if (webview) {
          webview.postMessage({
            type: "update",
            text: response.formattedContent
          });
        }
        return;
      }
    } catch (err) {
      console.error("[RealmExtension] saveMetadataViaGodotIpc failed, falling back:", err);
    }
    const fallbackText = typeof metadata === "string" ? metadata : JSON.stringify(metadata, null, 2);
    await this.updateTextDocument(document, fallbackText);
    await document.save();
    this.notifyGodotReloadMetadata();
    if (webview) {
      webview.postMessage({
        type: "update",
        text: fallbackText
      });
    }
  }
  notifyGodotReloadMetadata() {
    sendGodotIpc({ action: "reloadMetadata" }).catch(() => {
    });
  }
  getHtmlForWebview(webview) {
    let detectedLocale = "en";
    let activeDict = {};
    let enDict = {};
    try {
      const appData = process.env.APPDATA || path.join(require("os").homedir(), "AppData", "Roaming");
      const userDir = path.join(appData, "Godot", "app_userdata", "Realm");
      const locFile = path.join(userDir, "realm-locale.txt");
      if (fs.existsSync(locFile)) {
        detectedLocale = fs.readFileSync(locFile, "utf8").trim() || "en";
      }
      const dictFile = path.join(userDir, "realm-locale-dict.json");
      if (fs.existsSync(dictFile)) {
        activeDict = JSON.parse(fs.readFileSync(dictFile, "utf8"));
      }
      const enFile = path.join(userDir, "realm-locale-en.json");
      if (fs.existsSync(enFile)) {
        enDict = JSON.parse(fs.readFileSync(enFile, "utf8"));
      }
    } catch {
    }
    if (Object.keys(activeDict).length === 0 || Object.keys(enDict).length === 0) {
      const searchDirs = [
        path.join(this.context.extensionPath, "..", "Realm.Client", "locale"),
        path.join(this.context.extensionPath, "locale"),
        path.join(process.cwd(), "Realm.Client", "locale")
      ];
      if (vscode.workspace.workspaceFolders) {
        for (const wf of vscode.workspace.workspaceFolders) {
          searchDirs.push(path.join(wf.uri.fsPath, "Realm.Client", "locale"));
          searchDirs.push(path.join(wf.uri.fsPath, "..", "Realm.Client", "locale"));
        }
      }
      for (const dir of searchDirs) {
        if (fs.existsSync(dir)) {
          try {
            const enFile = path.join(dir, "en.json");
            if (fs.existsSync(enFile) && Object.keys(enDict).length === 0) {
              enDict = JSON.parse(fs.readFileSync(enFile, "utf8"));
            }
            const activeFile = path.join(dir, `${detectedLocale}.json`);
            if (fs.existsSync(activeFile) && Object.keys(activeDict).length === 0) {
              activeDict = JSON.parse(fs.readFileSync(activeFile, "utf8"));
            }
          } catch {
          }
        }
        if (Object.keys(enDict).length > 0 && Object.keys(activeDict).length > 0) {
          break;
        }
      }
    }
    const nonce = this.getNonce();
    let scriptContent = "";
    let styleContent = "";
    try {
      scriptContent = fs.readFileSync(path.join(this.context.extensionPath, "media", "editor.js"), "utf8");
    } catch {
    }
    try {
      styleContent = fs.readFileSync(path.join(this.context.extensionPath, "media", "editor.css"), "utf8");
    } catch {
    }
    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; connect-src http://127.0.0.1:* http://localhost:*; img-src ${webview.cspSource} data: https:; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <style>${styleContent}</style>
    <title>Realm Map Editor</title>
</head>
<body>
    <div class="app-container">
        <div class="global-header">
            <div class="app-title-group">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="color: var(--accent);"><path d="M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5"/></svg>
                <h1>Realm Map Editor</h1>
            </div>
            <div class="global-tabs">
                <button type="button" class="tab-btn active" data-domain="units">\u{1F465} Units</button>
                <button type="button" class="tab-btn" data-domain="buildings">\u{1F3E2} Buildings</button>
                <button type="button" class="tab-btn" data-domain="resources">\u{1FAB5} Resources</button>
                <button type="button" class="tab-btn" data-domain="props">\u{1F4E6} Props</button>
                <button type="button" class="tab-btn" data-domain="weapons">\u2694\uFE0F Weapons</button>
                <button type="button" class="tab-btn" data-domain="abilities">\u{1FA84} Abilities</button>
                <button type="button" class="tab-btn" data-domain="upgrades">\u{1F6E1}\uFE0F Upgrades</button>
                <button type="button" class="tab-btn" data-domain="items">\u{1F4E6} Items</button>
                <button type="button" class="tab-btn" data-domain="properties">\u2699\uFE0F Settings</button>
            </div>
            <div class="header-right-actions">
                <div id="save-status" class="save-status saved" title="Auto-saved to file">\u25CF Saved</div>
                <button type="button" id="toggle-lock-btn" class="btn secondary-btn small-btn" title="Lock Editor (Read-Only Mode)">\u{1F513} Lock</button>
                <button type="button" id="toggle-buttons-btn" class="btn secondary-btn small-btn" title="Toggle Add/Delete Controls">\u2795 Edit Ops</button>
                <button type="button" id="toggle-debug-btn" class="btn secondary-btn small-btn" title="Toggle Debug JSON View">\u{1F41E} Debug</button>
                <span style="font-size: 11px; color: var(--text-muted); opacity: 0.8; margin-left: 4px;" title="Press Ctrl+Shift+I inside editor to open Chromium DevTools console for debugging">\u{1F4A1} Ctrl+Shift+I DevTools</span>
            </div>
        </div>
        <div class="editor-body">
            <div class="sidebar">
                <div class="sidebar-subheader" style="padding-top: 16px;">
                    <h2>Units List</h2>
                    <div class="add-buttons-group" style="display: flex; gap: 4px;">
                        <button id="add-unit-btn" class="btn primary-btn" style="padding: 4px 8px;" title="Add Unit">+ Add</button>
                        <button type="button" id="prune-entities-btn" class="btn secondary-btn" style="padding: 4px 8px;" title="Prune items never placed on terrain.json">\u2702\uFE0F Prune Unused</button>
                    </div>
                </div>
                <div class="search-container">
                    <input type="text" id="search-input" placeholder="Search units..." />
                </div>
                <div id="unit-list" class="unit-list"></div>
            </div>
            <div class="main-content">
                <div id="empty-state" class="empty-state">
                    <div class="empty-state-content">
                        <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                            <path d="M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5" />
                        </svg>
                        <h3>Select a unit to edit</h3>
                        <p>Or configure Map Properties in the global tabs.</p>
                    </div>
                </div>
            <div id="editor-form" class="editor-form hidden">
                <div class="form-header">
                    <div style="display: flex; justify-content: space-between; align-items: center; width: 100%;">
                        <div>
                            <div class="breadcrumb" id="editor-breadcrumb">Units</div>
                            <h2 id="editor-title">Edit Entity</h2>
                            <span id="editor-subtitle" class="subtitle">ID</span>
                        </div>
                        <div class="header-actions" style="display: flex; gap: 6px;">
                            <button type="button" id="edit-animations-btn" class="btn secondary-btn" title="Open Unit Animation Studio in Godot">\u{1F3AC} Edit Animations</button>
                            <button type="button" id="copy-unit-btn" class="btn secondary-btn" title="Copy entity to clipboard">\u2702\uFE0F Copy</button>
                            <button type="button" id="paste-unit-btn" class="btn secondary-btn" title="Paste entity from clipboard">\u{1F4CB} Paste</button>
                            <button type="button" id="duplicate-unit-btn" class="btn secondary-btn">\u{1F4CB} Duplicate</button>
                            <button type="button" id="delete-unit-btn" class="btn secondary-btn" title="Delete entity">\u{1F5D1}\uFE0F Delete</button>
                        </div>
                    </div>
                </div>
                <div class="form-scroll-container">
                    <div class="form-section">
                        <h3>General Information</h3>
                        <div class="form-group">
                            <label for="field-TemplateID-slug">TemplateID</label>
                            <div style="display: flex; align-items: center; gap: 4px;">
                                <span id="field-template-type-prefix" style="color: var(--vscode-descriptionForeground, #888888); font-family: var(--vscode-editor-font-family, monospace); font-size: 13px; font-weight: bold;">unit/</span>
                                <input type="text" id="field-TemplateID-slug" style="flex: 1;" required />
                            </div>
                        </div>
                        <div class="form-group">
                            <label for="field-Name">Name</label>
                            <input type="text" id="field-Name" required />
                        </div>
                        <div class="form-group">
                            <label for="field-Description">Description</label>
                            <textarea id="field-Description" rows="3" required></textarea>
                        </div>
                        <div class="form-group">
                            <label>Model Asset (rmesh)</label>
                            <div class="input-with-browse" style="display: flex; gap: 6px; width: 100%; align-items: center;">
                                <span id="field-ModelPath" class="readonly-model-label" style="flex: 1; min-height: 28px; padding: 4px 8px; background: var(--vscode-input-background, #1e1e1e); border: 1px solid var(--vscode-input-border, #3c3c3c); border-radius: 2px; color: var(--vscode-input-foreground, #cccccc); display: flex; align-items: center; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; user-select: text; font-family: var(--vscode-editor-font-family, monospace); font-size: 12px;">(None)</span>
                                <button type="button" class="btn edit-model-btn" data-field="ModelPath" title="Edit Model Asset in Godot">\u270F\uFE0F</button>
                            </div>
                        </div>
                        <div class="form-group">
                            <label>Portrait Model Path (Optional)</label>
                            <div class="input-with-browse" style="display: flex; gap: 6px; width: 100%; align-items: center;">
                                <span id="field-PortraitModelPath" class="readonly-model-label" style="flex: 1; min-height: 28px; padding: 4px 8px; background: var(--vscode-input-background, #1e1e1e); border: 1px solid var(--vscode-input-border, #3c3c3c); border-radius: 2px; color: var(--vscode-input-foreground, #cccccc); display: flex; align-items: center; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; user-select: text; font-family: var(--vscode-editor-font-family, monospace); font-size: 12px;">(None)</span>
                                <button type="button" class="btn edit-model-btn" data-field="PortraitModelPath" title="Edit Portrait Model in Godot">\u270F\uFE0F</button>
                            </div>
                        </div>
                        <div class="form-group checkbox-group">
                            <input type="checkbox" id="field-IsHero" />
                            <label for="field-IsHero">Is Hero</label>
                        </div>
                    </div>

                    <div id="section-unit-animations" class="form-section">
                        <h3>Unit Animations</h3>
                        <div style="display: flex; align-items: center; justify-content: space-between; background: var(--vscode-input-background, #1e1e1e); border: 1px solid var(--vscode-input-border, #3c3c3c); border-radius: 4px; padding: 10px 14px;">
                            <div>
                                <span style="font-weight: 600; font-size: 13px;">Rigged Animations (.ranim)</span>
                                <p style="margin: 3px 0 0 0; font-size: 12px; opacity: 0.75;">Live preview and configure Idle, Walk, Attack, Death, and Spell casting animations in Godot.</p>
                            </div>
                            <button type="button" id="edit-animations-body-btn" class="btn secondary-btn" style="white-space: nowrap;" title="Open Unit Animation Studio in Godot">\u{1F3AC} Edit Animations</button>
                        </div>
                    </div>

                    <div id="section-resource-node-config" class="form-section">
                        <h3>Resource Deposit Settings</h3>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-MaxCapacity">Max Resource Capacity</label>
                                <input type="number" id="field-MaxCapacity" min="0" step="any" placeholder="2000" />
                            </div>
                            <div class="form-group">
                                <label for="field-HarvestRate">Harvest Yield / Cycle</label>
                                <input type="number" id="field-HarvestRate" min="0" step="any" placeholder="10" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-GrowthRate">Regen / Growth Rate (Units/sec)</label>
                                <input type="number" id="field-GrowthRate" min="0" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-MaxWorkers">Max Simultaneous Harvesters</label>
                                <input type="number" id="field-MaxWorkers" min="1" step="1" placeholder="5" />
                            </div>
                        </div>
                    </div>

                    <div id="section-unit-stats" class="form-section">
                        <h3>Attributes & Stats</h3>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-Strength">Strength (STR)</label>
                                <input type="number" id="field-Strength" min="0" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-Agility">Agility (AGI)</label>
                                <input type="number" id="field-Agility" min="0" step="any" placeholder="0.0" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-Vitality">Vitality (VIT)</label>
                                <input type="number" id="field-Vitality" min="0" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-Intelligence">Intelligence (INT)</label>
                                <input type="number" id="field-Intelligence" min="0" step="any" placeholder="0.0" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-Wisdom">Wisdom (WIS)</label>
                                <input type="number" id="field-Wisdom" min="0" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-Fortune">Fortune (FORT)</label>
                                <input type="number" id="field-Fortune" min="0" step="any" placeholder="0.0" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-MaxHp">Max HP</label>
                                <input type="number" id="field-MaxHp" min="0" step="any" required />
                            </div>
                            <div class="form-group">
                                <label for="field-Damage">Damage</label>
                                <input type="number" id="field-Damage" min="0" step="any" required />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-Range">Range</label>
                                <input type="number" id="field-Range" min="0" step="any" required />
                            </div>
                            <div class="form-group">
                                <label for="field-Armor">Armor</label>
                                <input type="number" id="field-Armor" min="0" step="any" required />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-Speed">Speed</label>
                                <input type="number" id="field-Speed" min="0" step="any" required />
                            </div>
                            <div class="form-group">
                                <label for="field-AttackCooldown">Attack Cooldown</label>
                                <input type="number" id="field-AttackCooldown" min="0" step="any" required />
                            </div>
                        </div>
                        <div class="form-group">
                            <label for="field-ScanRadius">Scan Radius</label>
                            <input type="number" id="field-ScanRadius" min="0" step="any" required />
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-HpRegen">HP Regen / sec</label>
                                <input type="number" id="field-HpRegen" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-HpRegenCombatDelay">HP Regen Combat Delay (sec)</label>
                                <input type="number" id="field-HpRegenCombatDelay" min="0" step="any" placeholder="0.0" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-MaxMana">Max Mana</label>
                                <input type="number" id="field-MaxMana" min="0" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-ManaRegen">Mana Regen / sec</label>
                                <input type="number" id="field-ManaRegen" step="any" placeholder="0.0" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-RatedArmor">Rated Armor (EHP Scaling)</label>
                                <input type="number" id="field-RatedArmor" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-ArmorType">Armor Type Tag</label>
                                <input type="text" id="field-ArmorType" placeholder="unarmored" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-FlatArmorPenetration">Flat Armor Pen</label>
                                <input type="number" id="field-FlatArmorPenetration" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-PercentArmorPenetration">Percent Armor Pen (0.0 - 1.0)</label>
                                <input type="number" id="field-PercentArmorPenetration" min="0" max="1" step="any" placeholder="0.0" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-DamageVariance">Damage Variance (\xB1)</label>
                                <input type="number" id="field-DamageVariance" min="0" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-DamageType">Damage Type Tag</label>
                                <input type="text" id="field-DamageType" placeholder="normal" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-CritChance">Crit Chance (0.0 - 1.0)</label>
                                <input type="number" id="field-CritChance" min="0" max="1" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-CritMultiplier">Crit Multiplier</label>
                                <input type="number" id="field-CritMultiplier" min="0" step="any" placeholder="1.0" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-SplashType">Splash Falloff Type</label>
                                <select id="field-SplashType">
                                    <option value="None">None</option>
                                    <option value="RadialStep">RadialStep</option>
                                    <option value="RadialLinear">RadialLinear</option>
                                </select>
                            </div>
                            <div class="form-group checkbox-group">
                                <label for="field-FriendlyFire">
                                    <input type="checkbox" id="field-FriendlyFire" />
                                    Friendly Fire Splash
                                </label>
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-SplashInnerRadius">Splash Inner Radius</label>
                                <input type="number" id="field-SplashInnerRadius" min="0" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-SplashInnerRatio">Splash Inner Ratio</label>
                                <input type="number" id="field-SplashInnerRatio" min="0" step="any" placeholder="1.0" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-SplashMediumRadius">Splash Medium Radius</label>
                                <input type="number" id="field-SplashMediumRadius" min="0" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-SplashMediumRatio">Splash Medium Ratio</label>
                                <input type="number" id="field-SplashMediumRatio" min="0" step="any" placeholder="0.5" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-SplashOuterRadius">Splash Outer Radius</label>
                                <input type="number" id="field-SplashOuterRadius" min="0" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-SplashOuterRatio">Splash Outer Ratio</label>
                                <input type="number" id="field-SplashOuterRatio" min="0" step="any" placeholder="0.25" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-PushPriority">Push Priority</label>
                                <input type="number" id="field-PushPriority" step="1" placeholder="0" />
                            </div>
                            <div class="form-group">
                                <label for="field-MovementType">Movement Type</label>
                                <input type="text" id="field-MovementType" placeholder="Ground" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-SightRange">Sight Range</label>
                                <input type="number" id="field-SightRange" min="0" step="any" placeholder="15.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-AcquisitionRange">Acquisition Range</label>
                                <input type="number" id="field-AcquisitionRange" min="0" step="any" placeholder="15.0" />
                            </div>
                        </div>
                    </div>

                    <div id="section-unit-costs" class="form-section">
                        <h3>Resource Costs & Production</h3>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-CostGold">Gold Cost</label>
                                <input type="number" id="field-CostGold" min="0" step="any" required />
                            </div>
                            <div class="form-group">
                                <label for="field-CostWood">Wood Cost</label>
                                <input type="number" id="field-CostWood" min="0" step="any" required />
                            </div>
                            <div class="form-group">
                                <label for="field-CostStone">Stone Cost</label>
                                <input type="number" id="field-CostStone" min="0" step="any" required />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-PopCost">Population Cost</label>
                                <input type="number" id="field-PopCost" min="0" step="1" required />
                            </div>
                            <div class="form-group">
                                <label for="field-ProductionTime">Production Time</label>
                                <input type="number" id="field-ProductionTime" min="0" step="any" required />
                            </div>
                        </div>
                    </div>

                    <div id="section-unit-combat" class="form-section">
                        <h3>Combat Types & Rewards</h3>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-AttackType">Attack Type</label>
                                <select id="field-AttackType" required>
                                    <option value="melee">Melee</option>
                                    <option value="ranged">Ranged</option>
                                    <option value="none">None</option>
                                </select>
                            </div>
                            <div class="form-group">
                                <label for="field-ArmorType">Armor Type</label>
                                <select id="field-ArmorType" required>
                                    <option value="light">Light</option>
                                    <option value="heavy">Heavy</option>
                                    <option value="building">Building</option>
                                </select>
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-GoldBounty">Gold Bounty</label>
                                <input type="number" id="field-GoldBounty" min="0" step="any" required />
                            </div>
                            <div class="form-group">
                                <label for="field-XpBounty">XP Bounty (Optional)</label>
                                <input type="number" id="field-XpBounty" min="0" step="any" />
                            </div>
                        </div>
                    </div>

                    <div id="section-unit-capabilities" class="form-section">
                        <h3>Lists & Capabilities</h3>
                        <div class="form-group">
                            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px;">
                                <label style="margin-bottom: 0;">Build Options (Optional)</label>
                                <div style="display: flex; gap: 4px;">
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="BuildOptions" title="Copy Build Options block">\u{1F4CB} Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="BuildOptions" title="Paste Build Options block">\u{1F4E5} Paste</button>
                                </div>
                            </div>
                            <div id="build-options-container" class="tag-list-container">
                                <div class="tags" id="build-options-tags"></div>
                                <div class="tag-input-row">
                                    <input type="text" id="build-option-input" list="suggest-units" placeholder="Add build option..." />
                                    <button type="button" id="add-build-option-btn" class="btn secondary-btn">+</button>
                                </div>
                            </div>
                        </div>
                        <div class="form-group">
                            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px;">
                                <label style="margin-bottom: 0;">Abilities (Optional)</label>
                                <div style="display: flex; gap: 4px;">
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="Abilities" title="Copy Abilities block">\u{1F4CB} Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="Abilities" title="Paste Abilities block">\u{1F4E5} Paste</button>
                                </div>
                            </div>
                            <div id="abilities-container" class="tag-list-container">
                                <div class="tags" id="abilities-tags"></div>
                                <div class="tag-input-row">
                                    <input type="text" id="ability-input" list="suggest-abilities" placeholder="Add ability..." />
                                    <button type="button" id="add-ability-btn" class="btn secondary-btn">+</button>
                                </div>
                            </div>
                        </div>
                        <div class="form-group">
                            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px;">
                                <label style="margin-bottom: 0;">Weapons (Optional)</label>
                                <div style="display: flex; gap: 4px;">
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="Weapons" title="Copy Weapons block">\u{1F4CB} Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="Weapons" title="Paste Weapons block">\u{1F4E5} Paste</button>
                                </div>
                            </div>
                            <div id="weapons-container" class="tag-list-container">
                                <div class="tags" id="weapons-tags"></div>
                                <div class="tag-input-row">
                                    <input type="text" id="weapon-input" list="suggest-weapons" placeholder="Add custom weapon ID..." />
                                    <button type="button" id="add-weapon-btn" class="btn secondary-btn">+</button>
                                </div>
                            </div>
                        </div>
                        <div class="form-group">
                            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px;">
                                <label style="margin-bottom: 0;">Starting Items (Optional)</label>
                                <div style="display: flex; gap: 4px;">
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="StartingItems" title="Copy Starting Items block">\u{1F4CB} Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="StartingItems" title="Paste Starting Items block">\u{1F4E5} Paste</button>
                                </div>
                            </div>
                            <div id="items-container" class="tag-list-container">
                                <div class="tags" id="items-tags"></div>
                                <div class="tag-input-row">
                                    <input type="text" id="item-input" list="suggest-items" placeholder="Add custom item ID..." />
                                    <button type="button" id="add-item-btn" class="btn secondary-btn">+</button>
                                </div>
                            </div>
                        </div>
                        <div class="form-group">
                            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px;">
                                <label style="margin-bottom: 0;">Tech Upgrades (Optional)</label>
                                <div style="display: flex; gap: 4px;">
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="Upgrades" title="Copy Tech Upgrades block">\u{1F4CB} Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="Upgrades" title="Paste Tech Upgrades block">\u{1F4E5} Paste</button>
                                </div>
                            </div>
                            <div id="upgrades-container" class="tag-list-container">
                                <div class="tags" id="upgrades-tags"></div>
                                <div class="tag-input-row">
                                    <input type="text" id="upgrade-input" list="suggest-upgrades" placeholder="Add custom upgrade ID..." />
                                    <button type="button" id="add-upgrade-btn" class="btn secondary-btn">+</button>
                                </div>
                            </div>
                        </div>
                        <div class="form-group">
                            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px;">
                                <label style="margin-bottom: 0;">Status Effects / Buffs (Optional)</label>
                                <div style="display: flex; gap: 4px;">
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="StatusEffects" title="Copy Status Effects block">\u{1F4CB} Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="StatusEffects" title="Paste Status Effects block">\u{1F4E5} Paste</button>
                                </div>
                            </div>
                            <div id="statuseffects-container" class="tag-list-container">
                                <div class="tags" id="statuseffects-tags"></div>
                                <div class="tag-input-row">
                                    <input type="text" id="statuseffect-input" placeholder="Add passive buff ID..." />
                                    <button type="button" id="add-statuseffect-btn" class="btn secondary-btn">+</button>
                                </div>
                            </div>
                        </div>
                        <div class="form-group">
                            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px;">
                                <label style="margin-bottom: 0;">Audio Sound Events (Optional)</label>
                                <div style="display: flex; gap: 4px;">
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="SoundEvents" title="Copy Sound Events block">\u{1F4CB} Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="SoundEvents" title="Paste Sound Events block">\u{1F4E5} Paste</button>
                                </div>
                            </div>
                            <div id="soundevents-container" class="tag-list-container">
                                <div class="tags" id="soundevents-tags"></div>
                                <div class="tag-input-row">
                                    <input type="text" id="soundevent-input" placeholder="Add audio event..." />
                                    <button type="button" id="add-soundevent-btn" class="btn secondary-btn">+</button>
                                </div>
                            </div>
                        </div>
                    </div>

                    <div id="section-pathing-flags" class="form-section">
                        <h3>Placement & Pathing Flags</h3>
                        <div class="form-group">
                            <div id="field-PathingType-flags" style="display: flex; flex-wrap: wrap; gap: 10px; margin-top: 4px;">
                                <label style="display: flex; align-items: center; gap: 4px; font-weight: normal;"><input type="checkbox" class="pathing-flag-cb" value="1" /> Shallow Water (1)</label>
                                <label style="display: flex; align-items: center; gap: 4px; font-weight: normal;"><input type="checkbox" class="pathing-flag-cb" value="2" /> Deep Water (2)</label>
                                <label style="display: flex; align-items: center; gap: 4px; font-weight: normal;"><input type="checkbox" class="pathing-flag-cb" value="4" /> Flying (4)</label>
                                <label style="display: flex; align-items: center; gap: 4px; font-weight: normal;"><input type="checkbox" class="pathing-flag-cb" value="8" /> Ground (8)</label>
                                <label style="display: flex; align-items: center; gap: 4px; font-weight: normal;"><input type="checkbox" class="pathing-flag-cb" value="32" /> Buildable (32)</label>
                            </div>
                            <input type="hidden" id="field-PathingType" value="8" />
                        </div>
                    </div>
                </div>
            </div>
            <div id="map-properties-form" class="editor-form hidden">
                <div class="form-header">
                    <div>
                        <div class="breadcrumb" id="map-properties-breadcrumb">Map > Properties</div>
                        <h2>Map Properties</h2>
                        <span class="subtitle">General configuration metadata</span>
                    </div>
                </div>
                <div class="form-scroll-container">
                    <div class="form-section">
                        <h3>General Information</h3>
                        <div class="form-group">
                            <label for="prop-MapName">Map Name</label>
                            <input type="text" id="prop-MapName" />
                        </div>
                        <div class="form-group">
                            <label for="prop-MapDescription">Description</label>
                            <textarea id="prop-MapDescription" rows="3"></textarea>
                        </div>
                        <div class="form-group">
                            <label for="prop-SuggestedPlayers">Suggested Players</label>
                            <input type="text" id="prop-SuggestedPlayers" placeholder="e.g. 2-4 Players" />
                        </div>
                    </div>
                    <div class="form-section">
                        <h3>Visuals & Assets</h3>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="prop-MinimapImage">Minimap Image</label>
                                <div class="input-with-browse">
                                    <input type="text" id="prop-MinimapImage" placeholder="res://Assets/...png" />
                                    <button type="button" class="btn browse-btn" data-input-id="prop-MinimapImage" data-file-types="png,jpg,jpeg,svg,tga,dds" title="Browse files">\u{1F4C1}</button>
                                    <button type="button" class="btn clear-btn" data-input-id="prop-MinimapImage" title="Clear path">\u274C</button>
                                </div>
                            </div>
                            <div class="form-group">
                                <label for="prop-ShroudType">Shroud Style</label>
                                <select id="prop-ShroudType">
                                    <option value="visible">Always Visible</option>
                                    <option value="VisionShroud">VisionShroud (Grey Mask)</option>
                                    <option value="ExplorationShroud">ExplorationShroud (Black Unexplored)</option>
                                </select>
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="prop-TerrainBaseHeight">Terrain Base Height</label>
                                <input type="number" id="prop-TerrainBaseHeight" min="0" step="any" />
                            </div>
                            <div class="form-group">
                                <label for="prop-ShadowIntensity">Shadow Intensity</label>
                                <input type="number" id="prop-ShadowIntensity" min="0" step="any" />
                            </div>
                        </div>
                    </div>
                    <div class="form-section">
                        <h3>Dimensions & Limits</h3>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="prop-MapWidth">Map Width</label>
                                <input type="number" id="prop-MapWidth" min="0" step="1" />
                            </div>
                            <div class="form-group">
                                <label for="prop-MapHeight">Map Height</label>
                                <input type="number" id="prop-MapHeight" min="0" step="1" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="prop-PlayableWidth">Playable Width</label>
                                <input type="number" id="prop-PlayableWidth" min="0" step="1" />
                            </div>
                            <div class="form-group">
                                <label for="prop-PlayableHeight">Playable Height</label>
                                <input type="number" id="prop-PlayableHeight" min="0" step="1" />
                            </div>
                        </div>
                    </div>
                    <div class="form-section">
                        <h3>Loading Screen</h3>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="prop-LoadingImage">Loading Image</label>
                                <div class="input-with-browse">
                                    <input type="text" id="prop-LoadingImage" placeholder="res://Assets/...png" />
                                    <button type="button" class="btn browse-btn" data-input-id="prop-LoadingImage" data-file-types="png,jpg,jpeg,svg,tga,dds" title="Browse files">\u{1F4C1}</button>
                                    <button type="button" class="btn clear-btn" data-input-id="prop-LoadingImage" title="Clear path">\u274C</button>
                                </div>
                            </div>
                            <div class="form-group">
                                <label for="prop-LoadingMusic">Loading Music</label>
                                <div class="input-with-browse">
                                    <input type="text" id="prop-LoadingMusic" placeholder="res://Assets/...ogg" />
                                    <button type="button" class="btn browse-btn" data-input-id="prop-LoadingMusic" data-file-types="ogg,wav,mp3" title="Browse files">\u{1F4C1}</button>
                                    <button type="button" class="btn clear-btn" data-input-id="prop-LoadingMusic" title="Clear path">\u274C</button>
                                </div>
                            </div>
                        </div>
                        <div class="form-group">
                            <label for="prop-LoadingTitle">Loading Title</label>
                            <input type="text" id="prop-LoadingTitle" />
                        </div>
                        <div class="form-group">
                            <label for="prop-LoadingSubtitle">Loading Subtitle</label>
                            <input type="text" id="prop-LoadingSubtitle" />
                        </div>
                        <div class="form-group">
                            <label for="prop-LoadingBodyText">Loading Description / Lore</label>
                            <textarea id="prop-LoadingBodyText" rows="3"></textarea>
                        </div>
                    </div>
                    <div class="form-section">
                        <h3>Lobby Instructions</h3>
                        <div class="form-group">
                            <label for="prop-HowToPlayObjective">Lobby Objective</label>
                            <input type="text" id="prop-HowToPlayObjective" placeholder="e.g. Destroy the enemy town center" />
                        </div>
                        <div class="form-group">
                            <label>Lobby Instructions List (Optional)</label>
                            <div id="instructions-container" class="tag-list-container">
                                <div class="tags" id="instructions-tags"></div>
                                <div class="tag-input-row">
                                    <input type="text" id="instruction-input" placeholder="Add lobby instruction..." />
                                    <button type="button" id="add-instruction-btn" class="btn secondary-btn">+</button>
                                </div>
                            </div>
                        </div>
                    </div>
                    <div class="form-section">
                        <h3>Player Slots</h3>
                        <div id="player-slots-container" class="list-editor-container">
                            <div id="player-slots-list"></div>
                            <button type="button" id="add-player-slot-btn" class="btn secondary-btn">+ Add Player Slot</button>
                        </div>
                    </div>
                    <div class="form-section">
                        <h3>Teams & Alliances</h3>
                        <div id="teams-container" class="list-editor-container">
                            <div id="teams-list"></div>
                            <button type="button" id="add-team-btn" class="btn secondary-btn">+ Add Team</button>
                        </div>
                    </div>
                    <div class="form-section">
                        <h3>Changelog</h3>
                        <div id="changelog-container" class="list-editor-container">
                            <div id="changelog-list"></div>
                            <button type="button" id="add-changelog-btn" class="btn secondary-btn">+ Add Changelog Entry</button>
                        </div>
                    </div>
                </div>
            </div>
            <div id="custom-weapons-form" class="editor-form hidden">
                <div class="form-header">
                    <div>
                        <div class="breadcrumb">Map > Custom Weapons</div>
                        <h2>Custom Weapons</h2>
                        <span class="subtitle">Combat attacks &amp; weapon definitions</span>
                    </div>
                </div>
                <div class="form-scroll-container">
                    <div class="form-section">
                        <div id="weapons-list-container" class="list-editor-container">
                            <div id="custom-weapons-list"></div>
                            <div class="add-buttons-row" style="display: flex; gap: 8px;">
                                <button type="button" id="add-custom-weapon-btn" class="btn secondary-btn">+ Add Custom Weapon</button>
                                <button type="button" id="paste-custom-weapon-btn" class="btn secondary-btn" title="Paste Weapon from Clipboard">\u{1F4CB} Paste Weapon</button>
                                <button type="button" id="prune-weapons-btn" class="btn secondary-btn" title="Prune weapons never used by placed units on terrain.json">\u2702\uFE0F Prune Unused</button>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
            <div id="custom-abilities-form" class="editor-form hidden">
                <div class="form-header">
                    <div>
                        <div class="breadcrumb">Map > Custom Abilities</div>
                        <h2>Custom Abilities</h2>
                        <span class="subtitle">Spells and passives catalog</span>
                    </div>
                </div>
                <div class="form-scroll-container">
                    <div class="form-section">
                        <div id="abilities-list-container" class="list-editor-container">
                            <div id="custom-abilities-list"></div>
                            <div class="add-buttons-row" style="display: flex; gap: 8px;">
                                <button type="button" id="add-custom-ability-btn" class="btn secondary-btn">+ Add Custom Ability</button>
                                <button type="button" id="paste-custom-ability-btn" class="btn secondary-btn" title="Paste Ability from Clipboard">\u{1F4CB} Paste Ability</button>
                                <button type="button" id="prune-abilities-btn" class="btn secondary-btn" title="Prune abilities never used by placed units on terrain.json">\u2702\uFE0F Prune Unused</button>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
            <div id="custom-upgrades-form" class="editor-form hidden">
                <div class="form-header">
                    <div>
                        <div class="breadcrumb">Map > Custom Upgrades</div>
                        <h2>Custom Upgrades</h2>
                        <span class="subtitle">Researchable tech upgrades</span>
                    </div>
                </div>
                <div class="form-scroll-container">
                    <div class="form-section">
                        <div id="upgrades-list-container" class="list-editor-container">
                            <div id="custom-upgrades-list"></div>
                            <div class="add-buttons-row" style="display: flex; gap: 8px;">
                                <button type="button" id="add-custom-upgrade-btn" class="btn secondary-btn">+ Add Custom Upgrade</button>
                                <button type="button" id="paste-custom-upgrade-btn" class="btn secondary-btn" title="Paste Upgrade from Clipboard">\u{1F4CB} Paste Upgrade</button>
                                <button type="button" id="prune-upgrades-btn" class="btn secondary-btn" title="Prune upgrades never used by placed units on terrain.json">\u2702\uFE0F Prune Unused</button>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
            <div id="custom-items-form" class="editor-form hidden">
                <div class="form-header">
                    <div>
                        <div class="breadcrumb">Map > Custom Items</div>
                        <h2>Custom Items</h2>
                        <span class="subtitle">Inventory item specifications</span>
                    </div>
                </div>
                <div class="form-scroll-container">
                    <div class="form-section">
                        <div id="items-list-container" class="list-editor-container">
                            <div id="custom-items-list"></div>
                            <div class="add-buttons-row" style="display: flex; gap: 8px;">
                                <button type="button" id="add-custom-item-btn" class="btn secondary-btn">+ Add Custom Item</button>
                                <button type="button" id="paste-custom-item-btn" class="btn secondary-btn" title="Paste Item from Clipboard">\u{1F4CB} Paste Item</button>
                                <button type="button" id="prune-items-btn" class="btn secondary-btn" title="Prune items never used by placed units on terrain.json">\u2702\uFE0F Prune Unused</button>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
            
            <div id="debug-json-container" class="debug-json-container collapsed hidden">
                <div class="debug-json-header">
                    <h3>Debug Data JSON (Read-only)</h3>
                    <div class="debug-json-actions">
                        <button type="button" id="copy-json-btn" class="btn secondary-btn small-btn">Copy JSON</button>
                        <button type="button" id="expand-json-btn" class="btn secondary-btn small-btn">Expand</button>
                    </div>
                </div>
                <div class="debug-json-body">
                    <pre><code id="debug-json-pre"></code></pre>
                </div>
            </div>
        </div>
        </div>
    </div>
    <script nonce="${nonce}">
        window.REALM_LOCALE = "${detectedLocale}";
        window.REALM_I18N = ${JSON.stringify(activeDict)};
        window.REALM_EN_FALLBACK = ${JSON.stringify(enDict)};
    </script>
    <script nonce="${nonce}">${scriptContent}</script>
</body>
</html>`;
  }
  getMergedDocumentText(document) {
    const text = document.getText();
    try {
      const parsed = JSON.parse(text);
      if (!parsed.Assets || Object.keys(parsed.Assets).length === 0) {
        const docDir = path.dirname(document.uri.fsPath);
        const manifestPath = path.join(docDir, "manifest.json");
        if (fs.existsSync(manifestPath)) {
          const manifestData = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
          if (manifestData && manifestData.Assets) {
            parsed.Assets = manifestData.Assets;
            return JSON.stringify(parsed);
          }
        }
      }
    } catch {
    }
    return text;
  }
  getNonce() {
    let text = "";
    const possible = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    for (let i = 0; i < 32; i++) {
      text += possible.charAt(Math.floor(Math.random() * possible.length));
    }
    return text;
  }
};

// src/rtexEditorProvider.ts
var vscode2 = __toESM(require("vscode"));
var path3 = __toESM(require("path"));
var fs3 = __toESM(require("fs"));

// src/constants.ts
var REALM_ASSET_AGREEMENT_WARNING = "The Realm Platform UGC Agreement states that files cannot be used outside the Realm Platform unless you are the original author of the asset. Do you understand?";
var MIXAMO_EXPORT_LICENSING_WARNING = "Export not available due to licensing. Download animations from https://www.mixamo.com";

// src/viewerUtils.ts
var path2 = __toESM(require("path"));
var fs2 = __toESM(require("fs"));
var os = __toESM(require("os"));
var crypto = __toESM(require("crypto"));
function getPreviewTempDir() {
  const tempDir = path2.join(os.tmpdir(), "realm_extension_previews");
  if (!fs2.existsSync(tempDir)) {
    fs2.mkdirSync(tempDir, { recursive: true });
  }
  return tempDir;
}
function getPreviewTempPath(sourceFsPath, newExt) {
  const tempDir = getPreviewTempDir();
  const hash = crypto.createHash("md5").update(sourceFsPath).digest("hex").substring(0, 12);
  const baseName = path2.basename(sourceFsPath, path2.extname(sourceFsPath));
  const ext = newExt.startsWith(".") ? newExt : `.${newExt}`;
  return path2.join(tempDir, `${hash}_${baseName}${ext}`);
}
function formatFileSize(bytes) {
  if (bytes >= 1024 * 1024)
    return (bytes / (1024 * 1024)).toFixed(2) + " MB";
  if (bytes >= 1024)
    return (bytes / 1024).toFixed(1) + " KB";
  return bytes + " B";
}
function getLoadingHtml(loadingText) {
  return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <style>
        body { display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; background-color: var(--vscode-editor-background); color: var(--vscode-editor-foreground); font-family: var(--vscode-font-family); }
        .spinner { border: 4px solid rgba(255, 255, 255, 0.1); width: 36px; height: 36px; border-radius: 50%; border-left-color: var(--vscode-progressBar-background, #0e639c); animation: spin 1s linear infinite; margin-bottom: 12px; }
        @keyframes spin { 0% { transform: rotate(0deg); } 100% { transform: rotate(360deg); } }
        .container { display: flex; flex-direction: column; align-items: center; }
    </style>
</head>
<body>
    <div class="container">
        <div class="spinner"></div>
        <div>${loadingText}</div>
    </div>
</body>
</html>`;
}
function getErrorHtml(title, errorMessage) {
  return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <style>
        body { display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; background-color: var(--vscode-editor-background); color: var(--vscode-errorForeground, #f48771); font-family: var(--vscode-font-family); padding: 20px; text-align: center; }
        .error-box { border: 1px solid var(--vscode-inputValidation-errorBorder, #be1100); background-color: var(--vscode-inputValidation-errorBackground, #5a1d1d); padding: 16px 24px; border-radius: 6px; max-width: 600px; }
    </style>
</head>
<body>
    <div class="error-box">
        <h3>${title}</h3>
        <p>${errorMessage}</p>
    </div>
</body>
</html>`;
}

// src/rtexEditorProvider.ts
var RealmRtexViewerProvider = class _RealmRtexViewerProvider {
  constructor(context) {
    this.context = context;
  }
  static {
    this.viewType = "realm.rtexViewer";
  }
  static register(context) {
    const provider = new _RealmRtexViewerProvider(context);
    return vscode2.window.registerCustomEditorProvider(_RealmRtexViewerProvider.viewType, provider, {
      supportsMultipleEditorsPerDocument: false
    });
  }
  async openCustomDocument(uri, _openContext, _token) {
    return {
      uri,
      dispose: () => {
      }
    };
  }
  async resolveCustomEditor(document, webviewPanel, _token) {
    webviewPanel.webview.options = { enableScripts: true };
    const rtexPath = document.uri.fsPath;
    const outputPngPath = getPreviewTempPath(rtexPath, ".png");
    webviewPanel.webview.onDidReceiveMessage(async (message) => {
      if (message.command === "exportPng") {
        const confirmed = await vscode2.window.showWarningMessage(
          REALM_ASSET_AGREEMENT_WARNING,
          { modal: true },
          "Yes, Export PNG"
        );
        if (confirmed === "Yes, Export PNG") {
          try {
            const defaultUri = vscode2.Uri.file(path3.join(path3.dirname(rtexPath), `${path3.basename(rtexPath, ".rtex")}.png`));
            const targetUri = await vscode2.window.showSaveDialog({
              defaultUri,
              filters: { "PNG Image": ["png"] },
              saveLabel: "Export PNG"
            });
            if (targetUri) {
              const response = await sendGodotIpc({
                action: "convertRtex",
                inputPath: rtexPath,
                outputPath: targetUri.fsPath
              });
              if (response && response.success && fs3.existsSync(targetUri.fsPath)) {
                vscode2.window.showInformationMessage(`Successfully exported PNG to: ${targetUri.fsPath}`);
              } else if (fs3.existsSync(outputPngPath)) {
                fs3.copyFileSync(outputPngPath, targetUri.fsPath);
                vscode2.window.showInformationMessage(`Successfully exported PNG to: ${targetUri.fsPath}`);
              } else {
                vscode2.window.showErrorMessage("Failed to extract PNG from RTEX file. Make sure Godot is running.");
              }
            }
          } catch (err) {
            vscode2.window.showErrorMessage(`Export failed: ${err?.message || err}`);
          }
        }
      }
    });
    webviewPanel.webview.html = getLoadingHtml("Loading RTEX texture preview...");
    try {
      const response = await sendGodotIpc({
        action: "convertRtex",
        inputPath: rtexPath,
        outputPath: outputPngPath
      });
      if (!response || !response.success || !fs3.existsSync(outputPngPath)) {
        throw new Error(response?.error || "Godot IPC failed to convert RTEX preview.");
      }
      const pngBytes = fs3.readFileSync(outputPngPath);
      const base64DataUri = `data:image/png;base64,${pngBytes.toString("base64")}`;
      webviewPanel.webview.html = this.getPreviewHtml(base64DataUri, path3.basename(rtexPath), response.metadata);
    } catch (error) {
      webviewPanel.webview.html = getErrorHtml("Failed to load RTEX preview", error?.message || "Failed to load RTEX preview.");
    }
  }
  getPreviewHtml(base64DataUri, title, metadata) {
    const typeInfo = metadata?.asset_type ? `<div class="meta-item"><strong>Type:</strong> ${metadata.asset_type}</div>` : "";
    const tagsInfo = metadata?.tags && Array.isArray(metadata.tags) && metadata.tags.length > 0 ? `<div class="meta-item"><strong>Tags:</strong> ${metadata.tags.slice(0, 10).join(", ")}${metadata.tags.length > 10 ? "..." : ""}</div>` : "";
    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; style-src 'unsafe-inline'; script-src 'unsafe-inline';">
    <style>
        body { display: flex; flex-direction: column; align-items: center; justify-content: center; min-height: 100vh; margin: 0; padding: 20px; background-color: var(--vscode-editor-background); color: var(--vscode-editor-foreground); font-family: var(--vscode-font-family); box-sizing: border-box; }
        .header { margin-bottom: 12px; font-weight: 600; font-size: 15px; color: var(--vscode-descriptionForeground); }
        .meta-container { margin-bottom: 14px; font-size: 12px; color: var(--vscode-descriptionForeground); display: flex; gap: 16px; flex-wrap: wrap; justify-content: center; }
        .meta-item strong { color: var(--vscode-editor-foreground); }
        .image-container { display: flex; align-items: center; justify-content: center; padding: 12px; border: 1px solid var(--vscode-widget-border, #454545); border-radius: 6px; background-color: rgba(0, 0, 0, 0.2); max-width: 90vw; max-height: 70vh; overflow: auto; }
        img { max-width: 100%; max-height: 65vh; object-fit: contain; image-rendering: pixelated; }
        .actions { display: flex; justify-content: center; margin-top: 16px; }
        .btn {
            background-color: var(--vscode-button-background, #0e639c);
            color: var(--vscode-button-foreground, #ffffff);
            border: none;
            padding: 8px 16px;
            border-radius: 4px;
            font-size: 13px;
            font-weight: 500;
            cursor: pointer;
            transition: background-color 0.15s ease;
        }
        .btn:hover {
            background-color: var(--vscode-button-hoverBackground, #1177bb);
        }
    </style>
</head>
<body>
    <div class="header">${title}</div>
    ${typeInfo || tagsInfo ? `<div class="meta-container">${typeInfo}${tagsInfo}</div>` : ""}
    <div class="image-container">
        <img src="${base64DataUri}" alt="RTEX Preview" />
    </div>
    <div class="actions">
        <button class="btn" onclick="exportPng()">\u{1F4E4} Export to PNG...</button>
    </div>
    <script>
        const vscode = acquireVsCodeApi();
        function exportPng() {
            vscode.postMessage({ command: 'exportPng' });
        }
    </script>
</body>
</html>`;
  }
};

// src/ranimEditorProvider.ts
var vscode3 = __toESM(require("vscode"));
var path4 = __toESM(require("path"));
var fs4 = __toESM(require("fs"));
var RealmRanimViewerProvider = class _RealmRanimViewerProvider {
  constructor(context) {
    this.context = context;
  }
  static {
    this.viewType = "realm.ranimViewer";
  }
  static register(context) {
    const provider = new _RealmRanimViewerProvider(context);
    return vscode3.window.registerCustomEditorProvider(_RealmRanimViewerProvider.viewType, provider, {
      supportsMultipleEditorsPerDocument: false
    });
  }
  async openCustomDocument(uri, _openContext, _token) {
    return {
      uri,
      dispose: () => {
      }
    };
  }
  async resolveCustomEditor(document, webviewPanel, _token) {
    webviewPanel.webview.options = { enableScripts: true };
    const ranimPath = document.uri.fsPath;
    const outputWebpPath = getPreviewTempPath(ranimPath, ".webp");
    webviewPanel.webview.onDidReceiveMessage(async (message) => {
      if (message.command === "exportRanim") {
        vscode3.window.showWarningMessage(MIXAMO_EXPORT_LICENSING_WARNING);
      }
    });
    webviewPanel.webview.html = getLoadingHtml("Rendering skeletal animation preview (.webp)...");
    try {
      const response = await sendGodotIpc({
        action: "renderRanim",
        inputPath: ranimPath,
        outputPath: outputWebpPath
      });
      if (!response || !response.success || !fs4.existsSync(outputWebpPath)) {
        throw new Error(response?.error || "Godot IPC failed to render .ranim animation.");
      }
      const webpBytes = fs4.readFileSync(outputWebpPath);
      const base64DataUri = `data:image/webp;base64,${webpBytes.toString("base64")}`;
      webviewPanel.webview.html = this.getPreviewHtml(base64DataUri, path4.basename(ranimPath));
    } catch (error) {
      webviewPanel.webview.html = getErrorHtml("Failed to load .ranim preview", error?.message || "Failed to render .ranim animation.");
    }
  }
  getPreviewHtml(base64DataUri, title) {
    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; style-src 'unsafe-inline'; script-src 'unsafe-inline';">
    <style>
        body { display: flex; flex-direction: column; align-items: center; justify-content: center; min-height: 100vh; margin: 0; padding: 20px; background-color: var(--vscode-editor-background); color: var(--vscode-editor-foreground); font-family: var(--vscode-font-family); box-sizing: border-box; }
        .header { margin-bottom: 16px; font-weight: 600; font-size: 14px; color: var(--vscode-descriptionForeground); }
        .image-container { display: flex; align-items: center; justify-content: center; padding: 12px; border: 1px solid var(--vscode-widget-border, #454545); border-radius: 6px; background-color: rgba(0, 0, 0, 0.2); max-width: 90vw; max-height: 75vh; overflow: auto; }
        img { max-width: 100%; max-height: 70vh; object-fit: contain; }
        .actions { display: flex; justify-content: center; margin-top: 16px; }
        .btn {
            background-color: var(--vscode-button-background, #0e639c);
            color: var(--vscode-button-foreground, #ffffff);
            border: none;
            padding: 8px 16px;
            border-radius: 4px;
            font-size: 13px;
            font-weight: 500;
            cursor: pointer;
            transition: background-color 0.15s ease;
        }
        .btn:hover {
            background-color: var(--vscode-button-hoverBackground, #1177bb);
        }
    </style>
</head>
<body>
    <div class="header">${title} (Animated WebP Preview)</div>
    <div class="image-container">
        <img src="${base64DataUri}" alt="Animated WebP Preview" />
    </div>
    <div class="actions">
        <button class="btn" onclick="exportRanim()">\u{1F4E4} Export Animation...</button>
    </div>
    <script>
        const vscode = acquireVsCodeApi();
        function exportRanim() {
            vscode.postMessage({ command: 'exportRanim' });
        }
    </script>
</body>
</html>`;
  }
};

// src/rmeshEditorProvider.ts
var vscode4 = __toESM(require("vscode"));
var path5 = __toESM(require("path"));
var fs5 = __toESM(require("fs"));
var crypto2 = __toESM(require("crypto"));
function getTempGlbPath(rmeshFsPath) {
  return getPreviewTempPath(rmeshFsPath, ".glb");
}
async function ensureGlbExtracted(rmeshFsPath) {
  const glbPath = getTempGlbPath(rmeshFsPath);
  const response = await sendGodotIpc({
    action: "convertRmesh",
    inputPath: rmeshFsPath,
    outputPath: glbPath
  });
  if (!response || !response.success) {
    return null;
  }
  return { glbPath, metadata: response.metadata || {} };
}
var RmeshGlbFileSystemProvider = class _RmeshGlbFileSystemProvider {
  constructor() {
    this._emitter = new vscode4.EventEmitter();
    this.onDidChangeFile = this._emitter.event;
  }
  static {
    this.scheme = "rmesh-git";
  }
  static createVirtualUri(rmeshFsPath) {
    const baseName = path5.basename(rmeshFsPath, path5.extname(rmeshFsPath));
    return vscode4.Uri.from({
      scheme: _RmeshGlbFileSystemProvider.scheme,
      authority: "memory",
      path: `/${encodeURIComponent(rmeshFsPath)}/${baseName}.glb`
    });
  }
  static getRmeshPathFromUri(uri) {
    const parts = uri.path.split("/").filter((p) => p.length > 0);
    if (parts.length > 0) {
      try {
        return decodeURIComponent(parts[0]);
      } catch {
        return null;
      }
    }
    return null;
  }
  watch(_uri, _options) {
    return new vscode4.Disposable(() => {
    });
  }
  stat(uri) {
    const rmeshPath = _RmeshGlbFileSystemProvider.getRmeshPathFromUri(uri);
    if (!rmeshPath || !fs5.existsSync(rmeshPath)) {
      throw vscode4.FileSystemError.FileNotFound(uri);
    }
    const glbPath = getTempGlbPath(rmeshPath);
    if (!fs5.existsSync(glbPath)) {
      throw vscode4.FileSystemError.FileNotFound(uri);
    }
    try {
      const rmeshStats = fs5.statSync(rmeshPath);
      const glbStats = fs5.statSync(glbPath);
      return {
        type: vscode4.FileType.File,
        ctime: rmeshStats.ctimeMs,
        mtime: rmeshStats.mtimeMs,
        size: glbStats.size
      };
    } catch (err) {
      if (err instanceof vscode4.FileSystemError)
        throw err;
      throw vscode4.FileSystemError.Unavailable(err?.message || "Error reading rmesh file");
    }
  }
  readDirectory(_uri) {
    return [];
  }
  createDirectory(_uri) {
    throw vscode4.FileSystemError.NoPermissions();
  }
  readFile(uri) {
    const rmeshPath = _RmeshGlbFileSystemProvider.getRmeshPathFromUri(uri);
    if (!rmeshPath || !fs5.existsSync(rmeshPath)) {
      throw vscode4.FileSystemError.FileNotFound(uri);
    }
    const glbPath = getTempGlbPath(rmeshPath);
    if (!fs5.existsSync(glbPath)) {
      throw vscode4.FileSystemError.FileNotFound(uri);
    }
    try {
      const glbBuffer = fs5.readFileSync(glbPath);
      return new Uint8Array(glbBuffer.buffer, glbBuffer.byteOffset, glbBuffer.byteLength);
    } catch (err) {
      if (err instanceof vscode4.FileSystemError)
        throw err;
      throw vscode4.FileSystemError.Unavailable(err?.message || "Error reading decompressed GLB payload");
    }
  }
  writeFile(_uri, _content, _options) {
    throw vscode4.FileSystemError.NoPermissions();
  }
  delete(_uri, _options) {
    throw vscode4.FileSystemError.NoPermissions();
  }
  rename(_oldUri, _newUri, _options) {
    throw vscode4.FileSystemError.NoPermissions();
  }
};
async function openRmeshInGlbViewer(rmeshPath) {
  try {
    const result = await ensureGlbExtracted(rmeshPath);
    if (!result) {
      vscode4.window.showErrorMessage("Failed to extract GLB from RMESH: Godot IPC returned an error. Make sure Godot is running with the Realm project open.");
      return;
    }
    const virtualUri = RmeshGlbFileSystemProvider.createVirtualUri(rmeshPath);
    await vscode4.commands.executeCommand("vscode.openWith", virtualUri, "glbViewer.customEditor", { preview: false });
  } catch (err) {
    vscode4.window.showErrorMessage(`Failed to open 3D GLB Viewer: ${err?.message || err}. Make sure OHZIInteractiveStudio.ohzi-vscode-glb-viewer is installed.`);
  }
}
var RealmRmeshViewerProvider = class _RealmRmeshViewerProvider {
  constructor(context) {
    this.context = context;
  }
  static {
    this.viewType = "realm.rmeshViewer";
  }
  static register(context) {
    const provider = new _RealmRmeshViewerProvider(context);
    return vscode4.window.registerCustomEditorProvider(_RealmRmeshViewerProvider.viewType, provider, {
      supportsMultipleEditorsPerDocument: false
    });
  }
  async openCustomDocument(uri, _openContext, _token) {
    return {
      uri,
      dispose: () => {
      }
    };
  }
  async resolveCustomEditor(document, webviewPanel, _token) {
    webviewPanel.webview.options = {
      enableScripts: true
    };
    const rmeshPath = document.uri.fsPath;
    webviewPanel.webview.onDidReceiveMessage(async (message) => {
      if (message.command === "open3d") {
        await openRmeshInGlbViewer(rmeshPath);
      } else if (message.command === "exportGlb") {
        const confirmed = await vscode4.window.showWarningMessage(
          REALM_ASSET_AGREEMENT_WARNING,
          { modal: true },
          "Yes, Export GLB"
        );
        if (confirmed === "Yes, Export GLB") {
          try {
            const defaultUri = vscode4.Uri.file(path5.join(path5.dirname(rmeshPath), `${path5.basename(rmeshPath, path5.extname(rmeshPath))}.glb`));
            const targetUri = await vscode4.window.showSaveDialog({
              defaultUri,
              filters: { "GLTF Binary Model": ["glb"] },
              saveLabel: "Export GLB"
            });
            if (targetUri) {
              const result = await ensureGlbExtracted(rmeshPath);
              if (result && fs5.existsSync(result.glbPath)) {
                fs5.copyFileSync(result.glbPath, targetUri.fsPath);
                vscode4.window.showInformationMessage(`Successfully exported GLB to: ${targetUri.fsPath}`);
              } else {
                vscode4.window.showErrorMessage("Failed to extract GLB payload from RMESH file. Make sure Godot is running.");
              }
            }
          } catch (err) {
            vscode4.window.showErrorMessage(`Export failed: ${err?.message || err}`);
          }
        }
      }
    });
    try {
      const stats = fs5.statSync(rmeshPath);
      const ipcResult = await ensureGlbExtracted(rmeshPath);
      const glbSize = ipcResult && fs5.existsSync(ipcResult.glbPath) ? fs5.statSync(ipcResult.glbPath).size : 0;
      const metadata = ipcResult?.metadata || {};
      webviewPanel.webview.html = this.getPreviewHtml(
        path5.basename(rmeshPath),
        stats.size,
        metadata,
        glbSize
      );
    } catch (error) {
      webviewPanel.webview.html = getErrorHtml("Error Loading .rmesh", error?.message || "Failed to load RMESH file.");
    }
  }
  getPreviewHtml(fileName, fileSize, metadata, glbSize) {
    const nonce = crypto2.randomBytes(16).toString("base64");
    const author = metadata?.author || "Unknown";
    const blake3 = metadata?.blake3 || "None";
    const assetType = metadata?.asset_type || "None";
    const teamColor = metadata?.chroma_key ? "\u2705 Enabled" : "\u274C Disabled";
    const prefName = metadata?.preferred_file_name || fileName;
    const createdUtc = metadata?.created_utc || "Unknown";
    const license = metadata?.license || "Realm UGC License";
    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
    <style>
        body {
            display: flex;
            flex-direction: column;
            align-items: center;
            justify-content: center;
            min-height: 100vh;
            margin: 0;
            padding: 24px;
            background-color: var(--vscode-editor-background);
            color: var(--vscode-editor-foreground);
            font-family: var(--vscode-font-family);
            box-sizing: border-box;
        }
        .card {
            background-color: var(--vscode-sideBar-background, rgba(255, 255, 255, 0.05));
            border: 1px solid var(--vscode-widget-border, rgba(255, 255, 255, 0.1));
            border-radius: 8px;
            padding: 24px;
            width: 100%;
            max-width: 560px;
            box-shadow: 0 4px 16px rgba(0, 0, 0, 0.2);
        }
        .header {
            display: flex;
            align-items: center;
            gap: 12px;
            margin-bottom: 20px;
            border-bottom: 1px solid var(--vscode-widget-border, rgba(255, 255, 255, 0.1));
            padding-bottom: 16px;
        }
        .icon {
            font-size: 32px;
        }
        .title-group h1 {
            margin: 0 0 4px 0;
            font-size: 18px;
            color: var(--vscode-editor-foreground);
        }
        .title-group .subtitle {
            margin: 0;
            font-size: 12px;
            color: var(--vscode-descriptionForeground);
        }
        .meta-grid {
            display: grid;
            grid-template-columns: 140px 1fr;
            gap: 10px 16px;
            font-size: 13px;
            margin-bottom: 24px;
        }
        .meta-label {
            color: var(--vscode-descriptionForeground);
            font-weight: 500;
        }
        .meta-value {
            word-break: break-all;
            font-family: var(--vscode-editor-font-family, monospace);
        }
        .badge {
            display: inline-block;
            padding: 2px 8px;
            border-radius: 4px;
            font-size: 11px;
            font-weight: 600;
            background-color: var(--vscode-badge-background, #388bfd33);
            color: var(--vscode-badge-foreground, #58a6ff);
        }
        .actions {
            display: flex;
            justify-content: flex-end;
            gap: 12px;
        }
        .btn {
            border: none;
            padding: 8px 16px;
            border-radius: 4px;
            font-size: 13px;
            font-weight: 500;
            cursor: pointer;
            transition: background-color 0.15s ease, transform 0.05s ease;
            display: inline-flex;
            align-items: center;
            gap: 6px;
        }
        .btn:active {
            transform: scale(0.98);
        }
        .btn-primary {
            background-color: var(--vscode-button-background, #0e639c);
            color: var(--vscode-button-foreground, #ffffff);
            font-weight: 600;
        }
        .btn-primary:hover {
            background-color: var(--vscode-button-hoverBackground, #1177bb);
        }
        .btn-secondary {
            background-color: var(--vscode-button-secondaryBackground, #3a3d41);
            color: var(--vscode-button-secondaryForeground, #ffffff);
        }
        .btn-secondary:hover {
            background-color: var(--vscode-button-secondaryHoverBackground, #45494e);
        }
    </style>
</head>
<body>
    <div class="card">
        <div class="header">
            <div class="icon">\u{1F4E6}</div>
            <div class="title-group">
                <h1>${fileName}</h1>
                <p class="subtitle">Realm 3D Mesh Container (.rmesh)</p>
            </div>
        </div>

        <div class="meta-grid">
            <div class="meta-label">Asset Type:</div>
            <div class="meta-value"><span class="badge">${assetType}</span></div>

            <div class="meta-label">Author Tag:</div>
            <div class="meta-value"><strong>${author}</strong></div>

            <div class="meta-label">Team Color:</div>
            <div class="meta-value">${teamColor}</div>

            <div class="meta-label">Total Size:</div>
            <div class="meta-value">${formatFileSize(fileSize)} (GLB: ${formatFileSize(glbSize)})</div>

            <div class="meta-label">Preferred Name:</div>
            <div class="meta-value">${prefName}</div>

            <div class="meta-label">BLAKE3 Hash:</div>
            <div class="meta-value"><small>${blake3}</small></div>

            <div class="meta-label">License:</div>
            <div class="meta-value">${license}</div>

            <div class="meta-label">Created UTC:</div>
            <div class="meta-value"><small>${createdUtc}</small></div>
        </div>

        <div class="actions">
            <button id="btn-open3d" class="btn btn-primary">\u{1F3AE} Open in 3D Viewer (OHZI)</button>
            <button id="btn-export-glb" class="btn btn-secondary">\u{1F4E4} Export to GLB...</button>
        </div>
    </div>

    <script nonce="${nonce}">
        const vscode = acquireVsCodeApi();
        document.getElementById('btn-open3d').addEventListener('click', function() {
            vscode.postMessage({ command: 'open3d' });
        });
        document.getElementById('btn-export-glb').addEventListener('click', function() {
            vscode.postMessage({ command: 'exportGlb' });
        });
    </script>
</body>
</html>`;
  }
};

// src/raudEditorProvider.ts
var vscode5 = __toESM(require("vscode"));
var path6 = __toESM(require("path"));
var fs6 = __toESM(require("fs"));
var RealmRaudViewerProvider = class _RealmRaudViewerProvider {
  constructor(context) {
    this.context = context;
  }
  static {
    this.viewType = "realm.raudViewer";
  }
  static register(context) {
    const provider = new _RealmRaudViewerProvider(context);
    return vscode5.window.registerCustomEditorProvider(_RealmRaudViewerProvider.viewType, provider, {
      supportsMultipleEditorsPerDocument: false
    });
  }
  async openCustomDocument(uri, _openContext, _token) {
    return {
      uri,
      dispose: () => {
      }
    };
  }
  async resolveCustomEditor(document, webviewPanel, _token) {
    webviewPanel.webview.options = {
      enableScripts: true
    };
    const raudPath = document.uri.fsPath;
    webviewPanel.webview.onDidReceiveMessage(async (message) => {
      if (message.command === "exportOgg") {
        const confirmed = await vscode5.window.showWarningMessage(
          REALM_ASSET_AGREEMENT_WARNING,
          { modal: true },
          "Yes, Export OGG"
        );
        if (confirmed === "Yes, Export OGG") {
          try {
            const defaultUri = vscode5.Uri.file(path6.join(path6.dirname(raudPath), `${path6.basename(raudPath, ".raud")}.ogg`));
            const targetUri = await vscode5.window.showSaveDialog({
              defaultUri,
              filters: { "Ogg Vorbis Audio": ["ogg"] },
              saveLabel: "Export OGG"
            });
            if (targetUri) {
              const buffer = fs6.readFileSync(raudPath);
              const parsed = this.parseRaud(buffer);
              if (parsed && parsed.tracks.length > 0) {
                fs6.writeFileSync(targetUri.fsPath, parsed.tracks[0]);
                vscode5.window.showInformationMessage(`Successfully exported OGG to: ${targetUri.fsPath}`);
              } else {
                vscode5.window.showErrorMessage("Failed to extract audio track from RAUD file.");
              }
            }
          } catch (err) {
            vscode5.window.showErrorMessage(`Export failed: ${err?.message || err}`);
          }
        }
      }
    });
    try {
      const fileBuffer = fs6.readFileSync(raudPath);
      const parsed = this.parseRaud(fileBuffer);
      const stats = fs6.statSync(raudPath);
      const track0Base64 = parsed && parsed.tracks.length > 0 ? parsed.tracks[0].toString("base64") : null;
      webviewPanel.webview.html = this.getPreviewHtml(
        webviewPanel.webview,
        path6.basename(raudPath),
        stats.size,
        parsed?.metadata || {},
        parsed?.tracks.length || 0,
        track0Base64
      );
    } catch (error) {
      webviewPanel.webview.html = getErrorHtml("Error Loading .raud", error?.message || "Failed to load RAUD file.");
    }
  }
  parseRaud(buffer) {
    if (buffer.length < 16)
      return null;
    const magic = buffer.toString("ascii", 0, 4);
    if (magic !== "RAUD")
      return null;
    const version = buffer.readUInt32LE(4);
    const metaLen = buffer.readUInt32LE(8);
    if (buffer.length < 12 + metaLen + 4)
      return null;
    let metadata = {};
    if (metaLen > 0) {
      try {
        const metaJson = buffer.toString("utf8", 12, 12 + metaLen);
        metadata = JSON.parse(metaJson);
      } catch {
      }
    }
    const trackCount = buffer.readUInt32LE(12 + metaLen);
    const tracks = [];
    let offset = 16 + metaLen;
    for (let i = 0; i < trackCount; i++) {
      if (offset + 4 > buffer.length)
        break;
      const trackLen = buffer.readUInt32LE(offset);
      offset += 4;
      if (offset + trackLen > buffer.length)
        break;
      tracks.push(buffer.subarray(offset, offset + trackLen));
      offset += trackLen;
    }
    return { metadata, tracks };
  }
  getPreviewHtml(webview, fileName, fileSize, metadata, trackCount, audioBase64) {
    const author = metadata?.author || "Unknown";
    const blake3 = metadata?.blake3 || "None";
    const assetType = metadata?.asset_type || "SoundEffect";
    const prefName = metadata?.preferred_file_name || fileName;
    const createdUtc = metadata?.created_utc || "Unknown";
    const license = metadata?.license || "Realm UGC License";
    const audioPlayerHtml = audioBase64 ? `<div class="audio-player-box">
                <audio controls src="data:audio/ogg;base64,${audioBase64}" style="width: 100%;"></audio>
               </div>` : `<div class="audio-player-box" style="color: var(--vscode-descriptionForeground);">No audio track data available for playback</div>`;
    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; media-src data:; style-src 'unsafe-inline'; script-src 'unsafe-inline';">
    <style>
        body {
            display: flex;
            flex-direction: column;
            align-items: center;
            justify-content: center;
            min-height: 100vh;
            margin: 0;
            padding: 24px;
            background-color: var(--vscode-editor-background);
            color: var(--vscode-editor-foreground);
            font-family: var(--vscode-font-family);
            box-sizing: border-box;
        }
        .card {
            background-color: var(--vscode-sideBar-background, rgba(255, 255, 255, 0.05));
            border: 1px solid var(--vscode-widget-border, rgba(255, 255, 255, 0.1));
            border-radius: 8px;
            padding: 24px;
            width: 100%;
            max-width: 560px;
            box-shadow: 0 4px 16px rgba(0, 0, 0, 0.2);
        }
        .header {
            display: flex;
            align-items: center;
            gap: 12px;
            margin-bottom: 20px;
            border-bottom: 1px solid var(--vscode-widget-border, rgba(255, 255, 255, 0.1));
            padding-bottom: 16px;
        }
        .icon {
            font-size: 32px;
        }
        .title-group h1 {
            margin: 0 0 4px 0;
            font-size: 18px;
            color: var(--vscode-editor-foreground);
        }
        .title-group .subtitle {
            margin: 0;
            font-size: 12px;
            color: var(--vscode-descriptionForeground);
        }
        .audio-player-box {
            margin-bottom: 20px;
            padding: 12px;
            background: rgba(0, 0, 0, 0.2);
            border-radius: 6px;
        }
        .meta-grid {
            display: grid;
            grid-template-columns: 140px 1fr;
            gap: 10px 16px;
            font-size: 13px;
            margin-bottom: 24px;
        }
        .meta-label {
            color: var(--vscode-descriptionForeground);
            font-weight: 500;
        }
        .meta-value {
            word-break: break-all;
            font-family: var(--vscode-editor-font-family, monospace);
        }
        .badge {
            display: inline-block;
            padding: 2px 8px;
            border-radius: 4px;
            font-size: 11px;
            font-weight: 600;
            background-color: var(--vscode-badge-background, #388bfd33);
            color: var(--vscode-badge-foreground, #58a6ff);
        }
        .actions {
            display: flex;
            justify-content: flex-end;
            gap: 12px;
        }
        .btn {
            background-color: var(--vscode-button-background, #0e639c);
            color: var(--vscode-button-foreground, #ffffff);
            border: none;
            padding: 8px 16px;
            border-radius: 4px;
            font-size: 13px;
            font-weight: 500;
            cursor: pointer;
            transition: background-color 0.15s ease;
        }
        .btn:hover {
            background-color: var(--vscode-button-hoverBackground, #1177bb);
        }
    </style>
</head>
<body>
    <div class="card">
        <div class="header">
            <div class="icon">\u{1F3B5}</div>
            <div class="title-group">
                <h1>${fileName}</h1>
                <p class="subtitle">Realm Audio Container (.raud)</p>
            </div>
        </div>

        ${audioPlayerHtml}

        <div class="meta-grid">
            <div class="meta-label">Asset Type:</div>
            <div class="meta-value"><span class="badge">${assetType}</span></div>

            <div class="meta-label">Author Tag:</div>
            <div class="meta-value"><strong>${author}</strong></div>

            <div class="meta-label">Track Count:</div>
            <div class="meta-value">${trackCount} track(s)</div>

            <div class="meta-label">File Size:</div>
            <div class="meta-value">${formatFileSize(fileSize)}</div>

            <div class="meta-label">Preferred Name:</div>
            <div class="meta-value">${prefName}</div>

            <div class="meta-label">BLAKE3 Hash:</div>
            <div class="meta-value"><small>${blake3}</small></div>

            <div class="meta-label">License:</div>
            <div class="meta-value">${license}</div>

            <div class="meta-label">Created UTC:</div>
            <div class="meta-value"><small>${createdUtc}</small></div>
        </div>

        <div class="actions">
            <button class="btn" onclick="exportOgg()">\u{1F4E4} Export to OGG...</button>
        </div>
    </div>

    <script>
        const vscode = acquireVsCodeApi();
        function exportOgg() {
            vscode.postMessage({ command: 'exportOgg' });
        }
    </script>
</body>
</html>`;
  }
};

// src/extension.ts
function activate(context) {
  context.subscriptions.push(RealmMapEditorProvider.register(context));
  context.subscriptions.push(RealmRtexViewerProvider.register(context));
  context.subscriptions.push(RealmRanimViewerProvider.register(context));
  context.subscriptions.push(RealmRmeshViewerProvider.register(context));
  context.subscriptions.push(RealmRaudViewerProvider.register(context));
  context.subscriptions.push(
    vscode6.workspace.registerFileSystemProvider(
      RmeshGlbFileSystemProvider.scheme,
      new RmeshGlbFileSystemProvider(),
      { isCaseSensitive: true, isReadonly: true }
    )
  );
  context.subscriptions.push(
    vscode6.commands.registerCommand("realm.openRmesh3D", async (uri) => {
      let targetPath = uri?.fsPath;
      if (!targetPath && vscode6.window.activeTextEditor) {
        targetPath = vscode6.window.activeTextEditor.document.uri.fsPath;
      }
      if (targetPath && targetPath.endsWith(".rmesh")) {
        await openRmeshInGlbViewer(targetPath);
      } else {
        vscode6.window.showWarningMessage("Please select a .rmesh file to view in 3D.");
      }
    })
  );
  context.subscriptions.push(
    vscode6.commands.registerCommand("realm.exportRanim", async (uri) => {
      vscode6.window.showWarningMessage(MIXAMO_EXPORT_LICENSING_WARNING);
    })
  );
  context.subscriptions.push(
    vscode6.workspace.onWillSaveTextDocument((event) => {
      const fileName = path7.basename(event.document.fileName).toLowerCase();
      if (fileName === "metadata.json" || fileName === "manifest.json" || fileName === "terrain.json") {
        event.waitUntil((async () => {
          try {
            const content = event.document.getText();
            const response = await sendGodotIpc({
              action: "formatAndSaveJson",
              filePath: event.document.uri.fsPath,
              content
            });
            if (response && response.success && typeof response.formattedContent === "string") {
              const fullRange = new vscode6.Range(
                event.document.positionAt(0),
                event.document.positionAt(content.length)
              );
              return [vscode6.TextEdit.replace(fullRange, response.formattedContent)];
            }
          } catch (err) {
            console.error("[RealmExtension] onWillSaveTextDocument IPC error:", err);
          }
          return [];
        })());
      }
    })
  );
  openStartupFiles(context);
  startGodotIpcListener(context);
}
function sendGodotIpc(payload) {
  return new Promise((resolve, reject) => {
    const ports = [8092, 8093];
    const postData = JSON.stringify(payload);
    const attemptNext = (portIndex) => {
      if (portIndex >= ports.length) {
        return reject(new Error("Could not connect to Godot IPC bridge on ports 8092 or 8093"));
      }
      const port = ports[portIndex];
      const req = http.request({
        hostname: "127.0.0.1",
        port,
        path: "/api/",
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Content-Length": Buffer.byteLength(postData)
        }
      }, (res) => {
        let body = "";
        res.on("data", (chunk) => body += chunk);
        res.on("end", () => {
          try {
            resolve(JSON.parse(body));
          } catch {
            resolve({ success: res.statusCode === 200, raw: body });
          }
        });
      });
      req.on("error", () => {
        attemptNext(portIndex + 1);
      });
      req.setTimeout(3e3, () => {
        try {
          req.destroy();
        } catch {
        }
        attemptNext(portIndex + 1);
      });
      req.write(postData);
      req.end();
    };
    attemptNext(0);
  });
}
function startGodotIpcListener(context) {
  const ports = [8092, 8093];
  const pollInterval = setInterval(async () => {
    for (const port of ports) {
      try {
        const data = await httpGetJson(`http://127.0.0.1:${port}/api/poll`);
        if (data && Array.isArray(data.commands)) {
          for (const cmd of data.commands) {
            if (cmd === "saveAll") {
              await handleSaveAllCommand();
            }
          }
        }
      } catch {
      }
    }
  }, 300);
  context.subscriptions.push({
    dispose: () => clearInterval(pollInterval)
  });
}
async function handleSaveAllCommand() {
  try {
    for (const doc of vscode6.workspace.textDocuments) {
      if (doc.isDirty) {
        await doc.save();
      }
    }
    await vscode6.commands.executeCommand("workbench.action.files.saveAll");
  } catch (err) {
    console.error("[RealmExtension] Error executing saveAll:", err);
  }
}
function httpGetJson(urlStr) {
  return new Promise((resolve, reject) => {
    const req = http.get(urlStr, (res) => {
      if (res.statusCode !== 200) {
        return reject(new Error(`Status ${res.statusCode}`));
      }
      let body = "";
      res.on("data", (chunk) => body += chunk);
      res.on("end", () => {
        try {
          resolve(JSON.parse(body));
        } catch (e) {
          reject(e);
        }
      });
    });
    req.on("error", reject);
    req.setTimeout(800, () => {
      req.destroy();
      reject(new Error("Timeout"));
    });
  });
}
async function openStartupFiles(context) {
  await delay(2e3);
  const folders = vscode6.workspace.workspaceFolders;
  if (!folders || folders.length === 0) {
    return;
  }
  const workspaceDir = folders[0].uri.fsPath;
  const scriptPath = path7.join(workspaceDir, "MapScript.cs");
  const metadataPath = path7.join(workspaceDir, "metadata.json");
  if (!fs7.existsSync(scriptPath) || !fs7.existsSync(metadataPath)) {
    return;
  }
  try {
    const metadataUri = vscode6.Uri.file(metadataPath);
    await vscode6.commands.executeCommand("vscode.open", metadataUri, { preview: false, preserveFocus: true });
    const scriptUri = vscode6.Uri.file(scriptPath);
    const scriptDoc = await vscode6.workspace.openTextDocument(scriptUri);
    await vscode6.window.showTextDocument(scriptDoc, { preview: false, preserveFocus: false });
    await vscode6.commands.executeCommand("workbench.action.revertFile");
  } catch (err) {
  }
}
function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
function deactivate() {
}
// Annotate the CommonJS export names for ESM import in node:
0 && (module.exports = {
  activate,
  deactivate,
  sendGodotIpc
});
