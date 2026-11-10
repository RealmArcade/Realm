using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Ecs.Services;
using Realm.EditorAPI;
using Realm.Shared.Audio;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Environment = System.Environment;
using FileAccess = Godot.FileAccess;
using HttpClient = System.Net.Http.HttpClient;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client;

[SupportedOSPlatform("windows")]
public partial class VSCodeManager
{
	private Process? _vscodeProcess;
	private Control _containerControl = null!;
	private bool _isInitialized;
	private bool _isVisible;

	public bool IsVisible
	{
		get
		{
			if (_vscodeProcess == null)
			{
				return false;
			}
			try
			{
				return !_vscodeProcess.HasExited;
			}
			catch
			{
				return false;
			}
		}
	}

	private void SaveCurrentLocaleToFile()
	{
		try
		{
			string dir = ProjectSettings.GlobalizePath("user://");
			if (!Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			string code = GameSettings.Language.ToLocaleCode();
			File.WriteAllText(Path.Combine(dir, "realm-locale.txt"), code);

			var activeDict = LocalizationManager.GetDictionary(code);
			File.WriteAllText(Path.Combine(dir, "realm-locale-dict.json"), JsonSerializer.Serialize(activeDict));

			var enDict = LocalizationManager.GetDictionary("en");
			File.WriteAllText(Path.Combine(dir, "realm-locale-en.json"), JsonSerializer.Serialize(enDict));
		}
		catch
		{
		}
	}

	private void OnLanguageChanged(GameLanguage newLanguage)
	{
		SaveCurrentLocaleToFile();
	}

	public void Initialize(Control containerControl)
	{
		if (_isInitialized)
		{
			_containerControl = containerControl;
			return;
		}

		LocalizationManager.LanguageChanged -= OnLanguageChanged;
		LocalizationManager.LanguageChanged += OnLanguageChanged;
		SaveCurrentLocaleToFile();

		_containerControl = containerControl;

		StartIpcHttpListener();

		_isInitialized = true;
	}

	private void EnsureWorkspaceMapApiDll(string projectRoot)
	{
		try
		{
			string mapFolderRaw = GetMapFolderToOpen(projectRoot);
			if (!string.IsNullOrEmpty(mapFolderRaw) && Directory.Exists(mapFolderRaw))
			{
				string libDir = Path.Combine(mapFolderRaw, "lib");
				if (!File.Exists(Path.Combine(libDir, "Realm.MapAPI.dll")))
				{
					GameLog.LogToConsole("Realm.MapAPI.dll missing in workspace. Running EnsureMapProjectFiles...");
					GameHost.EnsureMapProjectFiles(mapFolderRaw);
				}
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"Failed to ensure Realm.MapAPI.dll in workspace: {ex.Message}");
		}
	}

	private int _ipcHttpPort = 8092;

	private async void StartIpcHttpListener()
	{
		try
		{
			var listener = new HttpListener();
			try
			{
				listener.Prefixes.Add($"http://127.0.0.1:{_ipcHttpPort}/api/");
				listener.Start();
			}
			catch
			{
				_ipcHttpPort = 8093;
				listener = new HttpListener();
				listener.Prefixes.Add($"http://127.0.0.1:{_ipcHttpPort}/api/");
				listener.Start();
			}

			GameLog.LogToConsole($"[VSCodeManager] HTTP IPC bridge listening on http://127.0.0.1:{_ipcHttpPort}/api/");

			while (true)
			{
				var ctx = await listener.GetContextAsync();
				_ = Task.Run(() => { HandleIpcHttpRequest(ctx); });
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[VSCodeManager] HTTP IPC bridge error: {ex.Message}");
		}
	}

	private readonly ConcurrentQueue<string> _pendingExtensionCommands = new ConcurrentQueue<string>();

	public void QueueWorkspaceMetadataReload()
	{
		_pendingExtensionCommands.Enqueue("reloadWorkspaceMetadata");
	}

	public async Task SaveAllOpenFilesAsync()
	{
		_pendingExtensionCommands.Enqueue("saveAll");
		await Task.Delay(500);
	}

}
