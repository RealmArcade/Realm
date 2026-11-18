using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Steamworks;
using NSec.Cryptography;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Services.Publishing;
using Realm.Client.Services.Steam;
using Realm.Client.Utils;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.Steam;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Key = NSec.Cryptography.Key;
using Environment = System.Environment;

namespace Realm.Client.UI.MapEditor;

/// <summary>Asks the editor for the authorship key a publish is signed with; false when none is selected.</summary>
public delegate bool AuthorshipKeyProvider(out Key? key);

/// <summary>
/// What the publish flow needs from the editor: the node it may parent its dialog to (and take process-frame waits
/// from), the status surfaces it reports through, and the editor-owned steps it cannot perform itself - sign-in,
/// authorship, asset validation, metadata naming and signing, the WASM compile and the thumbnail.
/// </summary>
public sealed record PublishHooks(
	Node Owner,
	Action<string> Feedback,
	Action<string> AppendLog,
	Func<bool> HasWasmErrors,
	Action<bool> SetWasmErrors,
	Action<string, Color> SetWasmConsoleStatus,
	Func<bool> EnsureSignedIn,
	AuthorshipKeyProvider TryGetAuthorshipKey,
	Func<string, bool> CheckScriptOptimized,
	Func<string, bool> CheckAssetSize,
	Func<string> MapName,
	Func<string> MapVersion,
	Func<string, string, string, string, Task> SignMetadata,
	Func<string> AuthorName,
	Func<string, Task> CompileScript,
	Func<string, Task>? SaveMinimapThumbnail,
	Action InvalidateMetadataCache,
	Action<string, string, int, int, double, bool> ShowGreenlight);

/// <summary>
/// The publish pipeline: validate the workspace, compile the map script, package and sign the assets, then hand the
/// package to Steam Workshop, which is the publication. It runs on the editor's node so the process-frame waits and the
/// dialog child behave as they did inside the HUD, and reports every outcome through <see cref="PublishProgressView"/>.
/// </summary>
public sealed class MapPublishController
{
	private readonly PublishHooks _hooks;

	public MapPublishController(PublishHooks hooks)
	{
		_hooks = hooks;
	}

	public async void RunAsync(bool fullExport = false)
	{
		if (GameContext.Host == null || GameContext.EditorService.IsSyncing) return;

		if (!_hooks.EnsureSignedIn()) return;

		if (!_hooks.TryGetAuthorshipKey(out NSec.Cryptography.Key? publishAuthorshipKey) || publishAuthorshipKey == null)
		{
			_hooks.Feedback(TranslationServer.Translate("Select an authorship key before publishing."));
			return;
		}

		GameContext.EditorService.IsSyncing = true;
		if (GameContext.EditorService != null)
		{
			GameContext.EditorService.IsPaused = true;
		}

		var activeEditor = GameContext.EditorService;
		string workspace = activeEditor != null && !string.IsNullOrEmpty(activeEditor._editingDirectory)
			? activeEditor._editingDirectory
			: MapWorkspaceService.GetActiveWorkspacePath();

		var (assetsValid, missingAssets) = MapAssetHelper.ValidateWorkspaceAssets(workspace);
		if (!assetsValid)
		{
			_hooks.Feedback(string.Format(TranslationServer.Translate("Publish failed: Map is missing required asset files:\n{0}"), string.Join(", ", missingAssets.Take(4)) + (missingAssets.Count > 4 ? "..." : "")));
			_hooks.AppendLog($"[ERROR] Publish failed. Missing required asset files:\n  {string.Join("\n  ", missingAssets)}");
			if (GameContext.EditorService != null)
			{
				GameContext.EditorService.IsSyncing = false;
				GameContext.EditorService.IsPaused = false;
			}
			return;
		}

		string mapTitle = _hooks.MapName();
		if (string.IsNullOrWhiteSpace(mapTitle) || MapNaming.IsUntitledMapName(mapTitle)) mapTitle = "UntitledMap";

		string mapVersion = _hooks.MapVersion();
		if (string.IsNullOrWhiteSpace(mapVersion)) mapVersion = "1.0.0";

		string metaJsonPath = Path.Combine(workspace, "metadata.json");
		string activeConfigPath = metaJsonPath;
		string tempTerrainPath = Path.Combine(workspace, "terrain.json");

		var publishDialog = new MapPublishDialog();
		publishDialog.Initialize(mapTitle, mapVersion);
		_hooks.Owner.AddChild(publishDialog);

		Control popup = publishDialog;
		var progressBar = publishDialog.Progress;
		var statusLabel = publishDialog.StatusLabel;
		var closeBtn = publishDialog.CloseButton;
		var view = new PublishProgressView(progressBar, statusLabel, closeBtn);

		closeBtn.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			if (GodotObject.IsInstanceValid(popup))
			{
				popup.QueueFree();
			}
		};

		_hooks.SetWasmErrors(false);
		Action<string> logHandler = line => _hooks.AppendLog(line);
		GameLog.OnLine += logHandler;

		try
		{
			await SavePublishWorkspaceStateAsync(workspace, tempTerrainPath, view);

			view.Stage(15, TranslationServer.Translate("Verifying map assets..."));
			await _hooks.Owner.ToSignal(_hooks.Owner.GetTree(), SceneTree.SignalName.ProcessFrame);

			(assetsValid, missingAssets) = MapAssetHelper.ValidateWorkspaceAssets(workspace);
			if (!assetsValid)
			{
				view.Fail(TranslationServer.Translate("Missing required asset files. Publish aborted."));
				_hooks.Feedback(string.Format(TranslationServer.Translate("Publish failed: Map is missing required asset files:\n{0}"), string.Join(", ", missingAssets.Take(4)) + (missingAssets.Count > 4 ? "..." : "")));
				_hooks.AppendLog($"[ERROR] Publish aborted. Missing required asset files:\n  {string.Join("\n  ", missingAssets)}");
				return;
			}

			view.Stage(20, TranslationServer.Translate("Compiling WASM map script..."));
			await _hooks.Owner.ToSignal(_hooks.Owner.GetTree(), SceneTree.SignalName.ProcessFrame);

			await _hooks.CompileScript(workspace);

			if (_hooks.HasWasmErrors())
			{
				view.Fail(TranslationServer.Translate("WASM compilation failed. Publish aborted."));
				_hooks.Feedback(TranslationServer.Translate("WASM compilation failed. Publish aborted."));
				return;
			}

			var csprojFiles = Directory.GetFiles(workspace, "*.csproj", SearchOption.TopDirectoryOnly);
			if (csprojFiles.Length > 0 || File.Exists(Path.Combine(workspace, "MapScript.cs")))
			{
				string? wasmPath = MapStorageService.FindWasmPath(workspace);

				if (string.IsNullOrEmpty(wasmPath) || !File.Exists(wasmPath))
				{
					_hooks.SetWasmErrors(true);
					view.Fail(TranslationServer.Translate("Compiled WASM binary missing. Publish aborted."));
					_hooks.Feedback(TranslationServer.Translate("Compiled WASM binary missing. Publish aborted."));
					return;
				}
			}

			if (!_hooks.CheckScriptOptimized(workspace))
			{
				view.Fail(TranslationServer.Translate("The compiled map script still carries debug information. Publish aborted."));
				return;
			}

			if (!_hooks.CheckAssetSize(workspace))
			{
				view.Fail(TranslationServer.Translate("Asset exceeds 15 MB size limit. Publish aborted."));
				return;
			}

			GameContext.MapStorageService.DeduplicateWorkspaceBuildArtifacts(workspace);

			await OptimizePublishAssetsAsync(workspace, view);

			view.Stage(60, TranslationServer.Translate("Generating map thumbnail..."));
			await _hooks.Owner.ToSignal(_hooks.Owner.GetTree(), SceneTree.SignalName.ProcessFrame);

			if (_hooks.SaveMinimapThumbnail != null)
			{
				await _hooks.SaveMinimapThumbnail(workspace);
			}

			view.Stage(65, TranslationServer.Translate("Generating manifest & indexing asset hashes..."));
			await _hooks.Owner.ToSignal(_hooks.Owner.GetTree(), SceneTree.SignalName.ProcessFrame);

			var authorshipKey = publishAuthorshipKey;
			string currentUsername = _hooks.AuthorName();
			var publish = await PublishPackageBuilder.BuildAsync(
				workspace, activeConfigPath, mapTitle, mapVersion, currentUsername, authorshipKey,
				isTestVersion: false,
				() => _hooks.SignMetadata(workspace, mapTitle, mapVersion, currentUsername));

			view.Stage(70, TranslationServer.Translate("Preparing the Workshop upload..."));
			await _hooks.Owner.ToSignal(_hooks.Owner.GetTree(), SceneTree.SignalName.ProcessFrame);

			string stagingRoot = ProjectSettings.GlobalizePath("user://workshop_publish");
			string stagingFolder = Path.Combine(stagingRoot, SanitizeFolderName(mapTitle));
			MapArchiveHelper.StageCanonicalCopy(workspace, stagingFolder);

			var identity = new WorkshopMapMetadata
			{
				MapName = mapTitle,
				Version = mapVersion,
				GameBuildNumber = RealmVersion.GameBuildNumber,
				ContentHash = publish.ManifestJsonContent != null ? MapManifest.LoadFromJson(publish.ManifestJsonContent).ComputeManifestBlake3() : string.Empty,
				AuthorSteamId = GameContext.Steam.SteamId
			};

			WorkshopMapEntry? existingItem = await SteamWorkshopService.FindByMapNameAsync(mapTitle, CancellationToken.None);
			ulong existingItemId = existingItem?.PublishedFileId ?? 0;

			string? thumbnailPath = PrepareWorkshopPreview(workspace, stagingRoot);
			var publishResult = await SteamWorkshopService.PublishAsync(
				stagingFolder,
				thumbnailPath,
				mapTitle,
				string.Format(TranslationServer.Translate("A Realm map, published from the map editor. Version {0}."), mapVersion),
				identity,
				ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic,
				existingItemId,
				string.Format(TranslationServer.Translate("Realm map editor published version {0}."), mapVersion),
				progress =>
				{
					view.Stage(70 + progress.Fraction * 28, progress.Stage);
				},
				CancellationToken.None);

			if (publishResult.Success)
			{
				view.Stage(100, TranslationServer.Translate("Map published to the Steam Workshop."));
				view.Succeed(TranslationServer.Translate("Map compiled & published to the Steam Workshop!"));
				await _hooks.Owner.ToSignal(_hooks.Owner.GetTree().CreateTimer(0.6f), SceneTreeTimer.SignalName.Timeout);

				if (GodotObject.IsInstanceValid(popup))
				{
					popup.QueueFree();
				}

				_hooks.Feedback(TranslationServer.Translate("Map compiled & published to the Steam Workshop!"));
				GameContext.UserInterface?.ShowConfirmationDialog(
					string.Format(TranslationServer.Translate("Map compiled & published to the Steam Workshop as item {0}."), publishResult.PublishedFileId),
					() => { },
					confirmText: TranslationServer.Translate("OK"),
					showCancel: false
				);
			}
			else
			{
				string errorText = string.Format(TranslationServer.Translate("Publishing to the Workshop failed: {0}"), publishResult.FailureReason);
				view.Fail(errorText);
				_hooks.Feedback(errorText);
			}
		}
		catch (Exception ex)
		{
			_hooks.SetWasmConsoleStatus($"❌ Publish Failed: {ex.Message}", new Color(1.0f, 0.3f, 0.3f));
			if (view.IsAlive)
			{
				view.Fail(string.Format(TranslationServer.Translate("Publish error: {0}"), ex.Message));
			}

			_hooks.Feedback(string.Format(TranslationServer.Translate("Publish error: {0}"), ex.Message));
		}
		finally
		{
			GameLog.OnLine -= logHandler;
			var syncingEditor = GameContext.EditorService;
			if (syncingEditor != null)
			{
				syncingEditor.IsPaused = false;
				syncingEditor.LastMetadataSyncTime = syncingEditor.GetLastWriteTimeSafe(activeConfigPath);
				syncingEditor.LastTerrainSyncTime = syncingEditor.GetMaxTerrainWriteTime(tempTerrainPath);
				syncingEditor.IsSyncing = false;
			}
		}
	}

	private async Task SavePublishWorkspaceStateAsync(string workspace, string terrainPath, PublishProgressView view)
	{
		view.Stage(10, TranslationServer.Translate("Saving map state & workspace..."));
		await _hooks.Owner.ToSignal(_hooks.Owner.GetTree(), SceneTree.SignalName.ProcessFrame);

		WorkspaceMetadataBootstrapper.EnsureLicenseFile(workspace);
		var host = GameContext.Host ?? throw new InvalidOperationException("Publishing requires an editor host.");
		host.SaveMapToFile(terrainPath, performReload: false);
		host.EditorHasUnsavedChanges = false;
		_hooks.InvalidateMetadataCache();

		if (OperatingSystem.IsWindows())
		{
			await GameContext.VSCode.SaveAllOpenFilesAsync();
		}
	}

	private async Task OptimizePublishAssetsAsync(string workspace, PublishProgressView view)
	{
		view.Stage(35, TranslationServer.Translate("Optimizing 3D assets..."));
		await _hooks.Owner.ToSignal(_hooks.Owner.GetTree(), SceneTree.SignalName.ProcessFrame);

		await MapWorkspaceService.EnsureGlbAssetsOptimizedCooperativeAsync(workspace, async (current, total, fileName) =>
		{
			float fraction = total > 0 ? (float)current / total : 1.0f;
			view.Stage(35 + fraction * 15, string.Format(TranslationServer.Translate("Optimizing 3D model {0}/{1}: {2}..."), current, total, fileName));
			await _hooks.Owner.ToSignal(_hooks.Owner.GetTree(), SceneTree.SignalName.ProcessFrame);
		});

		view.Stage(50, TranslationServer.Translate("Converting textures..."));
		await _hooks.Owner.ToSignal(_hooks.Owner.GetTree(), SceneTree.SignalName.ProcessFrame);

		await MapWorkspaceService.EnsurePngAssetsConvertedCooperativeAsync(workspace, async (current, total, fileName) =>
		{
			float fraction = total > 0 ? (float)current / total : 1.0f;
			view.Stage(50 + fraction * 15, string.Format(TranslationServer.Translate("Converting texture {0}/{1}: {2}..."), current, total, fileName));
			await _hooks.Owner.ToSignal(_hooks.Owner.GetTree(), SceneTree.SignalName.ProcessFrame);
		});
	}

	/// <summary>
	/// Turns the map's own thumbnail into the image Steam accepts as an item preview, because Steam reads PNG and JPEG
	/// and the editor writes WebP. The preview sits beside the uploaded folder rather than inside it, so the map the
	/// player downloads is not carrying the storefront's copy of its own picture.
	/// </summary>
	/// <param name="workspace">The map folder holding the thumbnail.</param>
	/// <param name="stagingRoot">Where the upload is being staged.</param>
	/// <returns>The preview image's path, or null when the map has no thumbnail.</returns>
	private static string? PrepareWorkshopPreview(string workspace, string stagingRoot)
	{
		string thumbnailPath = Path.Combine(workspace, MapEditorMinimap.ThumbnailFileName);
		if (!File.Exists(thumbnailPath)) return null;

		try
		{
			using var bitmap = SkiaSharp.SKBitmap.Decode(thumbnailPath);
			if (bitmap == null) return null;

			Directory.CreateDirectory(stagingRoot);
			string previewPath = Path.Combine(stagingRoot, "workshop_preview.png");
			using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
			using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
			using var stream = File.Create(previewPath);
			data.SaveTo(stream);
			return previewPath;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapPublishController] The Workshop preview image could not be prepared: {exception.Message}");
			return null;
		}
	}

	/// <summary>
	/// Reduces a map's title to a folder name, so two maps whose titles differ only in characters a filesystem refuses
	/// do not stage into the same place.
	/// </summary>
	/// <param name="title">The map's title.</param>
	/// <returns>A name that is safe as one folder.</returns>
	private static string SanitizeFolderName(string title)
	{
		string cleaned = string.Join("_", title.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
		return string.IsNullOrEmpty(cleaned) ? "map" : cleaned;
	}
}
