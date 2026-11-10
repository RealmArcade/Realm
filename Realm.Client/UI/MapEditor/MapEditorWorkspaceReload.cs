using System;
using System.IO;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the reload needs from the editor: where its active directory is, whether the in-memory map has unsaved work,
/// the two refresh paths for metadata, version recording and the feedback line.
/// </summary>
public sealed record WorkspaceReloadHooks(
	Func<string> ActiveEditingDirectory,
	Func<bool> HasUnsavedChanges,
	Action ReloadMetadata,
	Action RecordExternalChangeVersion,
	Action<string> Feedback);

/// <summary>
/// Picking up edits made outside the editor. The workspace watcher reports that files changed, and this decides when it
/// is safe to act on that - the editor defers while a drag, a modal, a sync or a pending autosave could be writing the
/// same files.
/// </summary>
public sealed class MapEditorWorkspaceReload
{
	private readonly WorkspaceReloadHooks _hooks;

	public MapEditorWorkspaceReload(WorkspaceReloadHooks hooks) => _hooks = hooks;

	public void ApplyPending()
	{
		if (!EditorWorkspaceWatcher.HasPendingExternalChanges) return;
		if (GameContext.Host == null) return;
		if (GameContext.EditorService.IsSyncing || GameContext.EditorService.IsPaused) return;
		if (FloatingDialogBase.HasAnyDialogOpen) return;
		if (Input.IsMouseButtonPressed(MouseButton.Left) || Input.IsMouseButtonPressed(MouseButton.Right)) return;
		if (GameContext.MapAutosaveService.HasPendingWrite) return;

		string editingDirectory = _hooks.ActiveEditingDirectory();
		if (string.IsNullOrEmpty(editingDirectory) || !File.Exists(Path.Combine(editingDirectory, "metadata.json"))) return;
		if (!EditorWorkspaceWatcher.TryTakePendingExternalChanges(out bool metadataChanged, out bool terrainChanged)) return;

		if (metadataChanged)
		{
			_hooks.ReloadMetadata();
			_hooks.Feedback(TranslationServer.Translate("metadata.json changed outside the editor — reloaded."));
		}

		if (!terrainChanged) return;

		_hooks.RecordExternalChangeVersion();

		// An unsaved map is never replaced from disk: the version history keeps the other copy, and the user decides.
		if (_hooks.HasUnsavedChanges())
		{
			_hooks.Feedback(TranslationServer.Translate("The terrain files changed outside the editor - your map was left as it is. The other version is in MAP VERSIONS."));
			return;
		}

		ReloadTerrain(editingDirectory);
	}

	/// <summary>
	/// Loads the terrain the watcher reported. The syncing flag is what tells the watcher to ignore the writes this load
	/// causes, so it is cleared in a finally even when the load throws.
	/// </summary>
	private void ReloadTerrain(string editingDirectory)
	{
		if (GameContext.Host is not { } host) return;

		string terrainPath = Path.Combine(editingDirectory, "terrain.json");
		if (!File.Exists(terrainPath)) return;

		GameContext.EditorService.IsSyncing = true;
		try
		{
			if (host.LoadMapFromFile(terrainPath, ensureGlbOptimized: false))
			{
				host.EditorHasUnsavedChanges = false;
				_hooks.Feedback(TranslationServer.Translate("The terrain changed outside the editor — reloaded."));
			}
			else
			{
				_hooks.Feedback(TranslationServer.Translate("The changed terrain files could not be read, so the map in this window was left untouched."));
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapEditorHUD] Reloading terrain from disk failed: {ex.Message}");
		}
		finally
		{
			GameContext.EditorService.IsSyncing = false;
		}
	}
}
