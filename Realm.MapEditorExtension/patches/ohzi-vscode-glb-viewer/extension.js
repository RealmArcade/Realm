const vscode = require('vscode');
const fs = require('fs');
const path = require('path');
const { exec } = require('child_process');

function getHTML(panel)
{
  const publicPath = path.join(__dirname, 'public');
  const webviewPath = path.join(publicPath, 'webview');
  const assetsPath = path.join(webviewPath, 'assets');
  const fontsPath = path.join(webviewPath, 'fonts');
  const htmlPath = path.join(webviewPath, 'index.html');
  let html = fs.readFileSync(htmlPath, 'utf8');

  html = html.replace(/<link\s+rel="modulepreload"[^>]*>/gi, '');

  let inlinedCss = '';
  if (fs.existsSync(assetsPath))
  {
    const cssFiles = fs.readdirSync(assetsPath).filter(f => f.endsWith('.css'));
    for (const cssFile of cssFiles)
    {
      let css = fs.readFileSync(path.join(assetsPath, cssFile), 'utf8');
      css = css.replace(/url\((["']?)(\/webview\/fonts\/[^"')]+)\1\)/g, (match, quote, fontRelPath) =>
      {
        const fontRelClean = fontRelPath.replace(/^\/webview\/fonts\//, '');
        const fontFullPath = path.join(fontsPath, fontRelClean);
        if (fs.existsSync(fontFullPath))
        {
          const fontB64 = fs.readFileSync(fontFullPath).toString('base64');
          return `url("data:font/woff2;base64,${fontB64}")`;
        }
        return match;
      });
      inlinedCss += css + '\n';
    }
  }

  html = html.replace(/<link\s+rel="stylesheet"[^>]*>/gi, `<style>${inlinedCss}</style>`);

  let threeDataUri = '';
  let threeFileName = '';
  if (fs.existsSync(assetsPath))
  {
    const threeFiles = fs.readdirSync(assetsPath).filter(f => f.startsWith('three-') && f.endsWith('.js'));
    if (threeFiles.length > 0)
    {
      threeFileName = threeFiles[0];
      const threeCode = fs.readFileSync(path.join(assetsPath, threeFileName), 'utf8');
      threeDataUri = `data:text/javascript;base64,${Buffer.from(threeCode).toString('base64')}`;
    }
  }

  let indexDataUri = '';
  if (fs.existsSync(assetsPath))
  {
    const indexJsFiles = fs.readdirSync(assetsPath).filter(f => f.startsWith('index-') && f.endsWith('.js'));
    if (indexJsFiles.length > 0)
    {
      let indexCode = fs.readFileSync(path.join(assetsPath, indexJsFiles[0]), 'utf8');
      if (threeFileName && threeDataUri)
      {
        indexCode = indexCode.replace(new RegExp(`(["'])\\./${threeFileName.replace('.', '\\.')}\\1`, 'g'), `"${threeDataUri}"`);
      }
      indexDataUri = `data:text/javascript;base64,${Buffer.from(indexCode).toString('base64')}`;
    }
  }

  html = html.replace(/<script\s+type="module"[^>]*src="[^"]+"[^>]*><\/script>/gi, `<script type="module" crossorigin src="${indexDataUri}"></script>`);

  html = html.replace(
    '<!-- content-security-policy-replaced-on-extension-js-->',
    `<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data: blob: https:; script-src 'unsafe-inline' 'unsafe-eval' 'wasm-unsafe-eval' data: blob:; style-src 'unsafe-inline' data:; font-src data:; connect-src data: blob: https:; worker-src blob: data:;">`
  );

  return html;
}

class GLBDocument
{
  constructor(uri)
  {
    this.uri = uri;
  }

  dispose()
  {
  }
}

function getWebViewPath(webviewPanel)
{
  return '';
}

function checkFileExtensionDefaults(context)
{
  const gltfPromptedKey = 'gltfEditorPromptShown';
  const alreadyPrompted = context.globalState.get(gltfPromptedKey);

  const disposable = vscode.workspace.onDidOpenTextDocument((document) =>
  {
    if (!alreadyPrompted && document.uri.fsPath.endsWith('.gltf'))
    {
      vscode.window.showInformationMessage(
        'Would you like to use the GLTF Visual Viewer for .gltf files?',
        'Yes', 'No'
      ).then(selection =>
      {
        if (selection === 'Yes')
        {
          const config = vscode.workspace.getConfiguration('workbench');
          const associations = config.get('editorAssociations') || {};
          associations['*.gltf'] = 'glbViewer.customEditor';
          config.update('editorAssociations', associations, vscode.ConfigurationTarget.Global).then(async() =>
          {
            const activeEditor = vscode.window.activeTextEditor;
            if (activeEditor && activeEditor.document.uri.fsPath.endsWith('.gltf'))
            {
              const uri = activeEditor.document.uri;
              await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
              await vscode.commands.executeCommand('vscode.openWith', uri, 'glbViewer.customEditor');
            }
          });

          context.globalState.update(gltfPromptedKey, true);
        }
        else
        {
          context.globalState.update(gltfPromptedKey, true);
        }
      });
    }
  });

  context.subscriptions.push(disposable);
}

async function sendModelAsBase64(panel, modelUri)
{
  try
  {
    const data = await vscode.workspace.fs.readFile(modelUri);

    const dataBase64 = Buffer.from(data).toString('base64');
    const fileSize = data.byteLength;

    const extName = path.extname(modelUri.path || modelUri.fsPath || '') || '.glb';
    const cleanExt = extName.replace(/^\./, '');

    panel.webview.postMessage({
      type: 'loadModelFromBase64',
      data: dataBase64,
      extension: cleanExt || 'glb',
      fileSize: fileSize
    });
  }
  catch (err)
  {
    vscode.window.showErrorMessage(`Failed to read GLB: ${err}`);
  }
}

function getDefaultBlenderPath()
{
  if (process.platform === 'darwin')
  {
    const macPath = '/Applications/Blender.app/Contents/MacOS/Blender';
    if (fs.existsSync(macPath)) return macPath;
    return 'blender';
  }

  if (process.platform === 'win32')
  {
    const possiblePaths = [];

    for (let i = 5; i > 2; i--)
    {
      for (let j = 6; j > -1; j--)
      {
        possiblePaths.push(`C:\\Program Files\\Blender Foundation\\Blender ${i}.${j}\\blender.exe`);
      }
    }

    possiblePaths.push('C:\\Program Files\\Blender Foundation\\Blender\\blender.exe');

    for (const p of possiblePaths)
    {
      if (fs.existsSync(p)) return p;
    }
    return 'blender';
  }

  return 'blender';
}

function getBlenderPath()
{
  const config = vscode.workspace.getConfiguration('glbViewer');
  const customPath = config.get('blenderPath')?.trim();
  return customPath && customPath.length > 0 ? customPath : getDefaultBlenderPath();
}

function activate(context)
{
  const provider =
  {
    async openCustomDocument(uri, openContext, token)
    {
      return new GLBDocument(uri);
    },

    async resolveCustomEditor(document, webviewPanel, _token)
    {
      webviewPanel.webview.options = {
        enableScripts: true,
        enableFindWidget: true,
        retainContextWhenHidden: true
      };

      webviewPanel.webview.html = getHTML(webviewPanel);

      webviewPanel.webview.onDidReceiveMessage(message =>
      {
        if (message.type === 'ready')
        {
          webviewPanel.webview.postMessage({
            type: 'updateConfig',
            config: vscode.workspace.getConfiguration('glbViewer')
          });

          webviewPanel.webview.postMessage({
            type: 'setWebViewPath',
            webview_path: getWebViewPath(webviewPanel)
          });

          webviewPanel.webview.postMessage({
            type: 'showOpenOnBlenderButton'
          });

          sendModelAsBase64(webviewPanel, document.uri);
        }
        if (message.type === 'openJson')
        {
          const jsonContent = JSON.stringify(message.payload, null, 2);

          vscode.workspace.openTextDocument({
            content: jsonContent,
            language: 'json'
          }).then(doc =>
          {
            vscode.window.showTextDocument(doc, vscode.ViewColumn.Active, true);
          });
        }

        if (message.type === 'openAsText')
        {
          vscode.commands.executeCommand('vscode.openWith', document.uri, 'default');
        }

        if (message.type === 'openInBlender')
        {
          const filePath = document.uri.fsPath;
          const blenderPath = getBlenderPath();
          const command = `"${blenderPath}" --python-expr "import bpy; bpy.ops.import_scene.gltf(filepath='${filePath.replace(/\\/g, '\\\\')}')"`;

          exec(command, (error, stdout, stderr) =>
          {
            if (error)
            {
              vscode.window.showErrorMessage(`Error launching Blender. Make sure to set the executable path in settings. \n\n${error.message}`);
              return;
            }
          });
        }
      });

      vscode.workspace.onDidChangeConfiguration((event) =>
      {
        if (event.affectsConfiguration('glbViewer.relevant3dObjectKeys'))
        {
          webviewPanel.webview.postMessage({
            type: 'updateConfig',
            config: vscode.workspace.getConfiguration('glbViewer')
          });
        }
        if (event.affectsConfiguration('glbViewer.prettifyPropertyLabels'))
        {
          webviewPanel.webview.postMessage({
            type: 'updateConfig',
            config: vscode.workspace.getConfiguration('glbViewer')
          });
        }
      });
    }
  };

  const openAsTextCommand = vscode.commands.registerCommand('glbViewer.openAsText', async(uri) =>
  {
    if (!uri && vscode.window.activeTextEditor)
    {
      uri = vscode.window.activeTextEditor.document.uri;
    }
    if (!uri) return;

    await vscode.commands.executeCommand('vscode.openWith', uri, 'default');
  });

  context.subscriptions.push(openAsTextCommand);

  context.subscriptions.push(
    vscode.window.registerCustomEditorProvider(
      'glbViewer.customEditor',
      provider,
      {
        webviewOptions: {
          retainContextWhenHidden: true
        }
      }
    )
  );

  checkFileExtensionDefaults(context);
}

function deactivate()
{}

module.exports = {
  activate,
  deactivate
};
