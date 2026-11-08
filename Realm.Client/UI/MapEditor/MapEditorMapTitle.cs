using System;
using Godot;
using Environment = System.Environment;
using Realm.Client.Services;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// The map's display name in the editor's top bar: resolving it from the workspace (manifest first, then metadata, then
/// a running host, then the placeholder) and rendering the header, which marks unsaved changes.
/// </summary>
internal static class MapEditorMapTitle
{
	/// <summary>
	/// The cache is two seconds long because the header refreshes on a timer and re-reading the manifest each tick would
	/// hit the disk for a name that cannot have changed without the saved-state flag moving too.
	/// </summary>
	private const long CacheLifetimeMs = 2000;

	public static string Resolve(bool forceReload)
	{
		long now = Environment.TickCount64;
		var identity = GameContext.EditorService.MapIdentity;

		if (!forceReload
			&& !string.IsNullOrEmpty(GameContext.EditorService.CachedMapName)
			&& (now - GameContext.EditorService.LastMapNameCacheTicks < CacheLifetimeMs))
		{
			return GameContext.EditorService.CachedMapName;
		}

		try
		{
			string workspacePath = !string.IsNullOrEmpty(GameContext.EditorService._editingDirectory)
				? GameContext.EditorService._editingDirectory
				: MapWorkspaceService.GetActiveWorkspacePath();

			if (identity.TryGetNameFromManifest(workspacePath, out string manifestMapName))
			{
				return identity.UpdateCachedName(manifestMapName, now);
			}

			if (identity.TryGetNameFromMetadata(workspacePath, out string metadataMapName))
			{
				return identity.UpdateCachedName(metadataMapName, now);
			}

			if (identity.TryGetNameFromActiveHost(out string activeMapName))
			{
				return identity.UpdateCachedName(activeMapName, now);
			}

			return identity.UpdateCachedName(TranslationServer.Translate("Unnamed Map"), now);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapEditorHUD] Error getting map name: {ex.Message}");
			return identity.UpdateCachedName(TranslationServer.Translate("Unnamed Map"), now);
		}
	}

	/// <summary>
	/// Both the text and the tooltip are written only when they change, because this runs on the header timer and every
	/// assignment makes Godot re-layout the label.
	/// </summary>
	public static void UpdateHeader(Label? mapNameLabel)
	{
		if (mapNameLabel == null) return;

		string mapName = Resolve(false);
		string displayMapName = MapNaming.IsUntitledMapName(mapName)
			? TranslationServer.Translate("Untitled Map")
			: mapName;
		bool hasUnsaved = GameContext.Host?.EditorHasUnsavedChanges ?? false;

		string displayText = hasUnsaved ? $"🗺️ {displayMapName} *" : $"🗺️ {displayMapName}";
		string tooltipText = hasUnsaved
			? $"{displayMapName} * ({TranslationServer.Translate("Unsaved changes — Press Ctrl+S to save")})"
			: $"{displayMapName} ({TranslationServer.Translate("All changes saved")})";

		if (mapNameLabel.Text != displayText)
		{
			mapNameLabel.Text = displayText;
		}

		if (mapNameLabel.TooltipText != tooltipText)
		{
			mapNameLabel.TooltipText = tooltipText;
		}
	}
}
