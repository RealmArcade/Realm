using System;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Services.MapHistory;

namespace Realm.Client.UI
{
	public partial class MapEditorHUD
	{
		public int GetSelectedPathingMask()
		{
			int mask = 0;
			mask = ApplyPathingMask(mask, _chkGround, EditableTerrain.PATHING_GROUND);
			mask = ApplyPathingMask(mask, _chkFlying, EditableTerrain.PATHING_FLYING);
			mask = ApplyPathingMask(mask, _chkShallowWater, EditableTerrain.PATHING_SHALLOW_WATER);
			mask = ApplyPathingMask(mask, _chkDeepWater, EditableTerrain.PATHING_DEEP_WATER);
			mask = ApplyPathingMask(mask, _chkBuildable, EditableTerrain.PATHING_BUILDABLE);
			return mask;
		}

		private int ApplyPathingMask(int currentMask, CheckBox checkBox, int pathingFlag)
		{
			if (checkBox == null) return currentMask;
			if (!checkBox.ButtonPressed) return currentMask;
			return currentMask | pathingFlag;
		}

		public float GetSelectedWaterHeight()
		{
			if (_sldWaterHeight == null) return 0.9f;
			return (float)_sldWaterHeight.Value;
		}

		public void SelectPaintSwatchByIndexView(int index)
		{
			if (index >= 0 && index < _swatchButtons.Count)
			{
				if (ChkApplyCliffTexture != null && ChkApplyCliffTexture.ButtonPressed && (ChkApplyGroundTexture == null || !ChkApplyGroundTexture.ButtonPressed))
				{
					SelectCliffTexture(index);
				}
				else
				{
					SelectTerrainTexture(index, _swatchButtons[index]);
				}
			}
		}

		public void ResetToBlankMapView()
		{
			_ = CreateBlankMapAsync();
		}

		private async Task CreateBlankMapAsync()
		{
			string untitledDirectory = MapWorkspaceService.GetUntitledMapDirectory();
			KeepUntitledMapInHistory(untitledDirectory);

			GameContext.EditorService.IsSyncing = true;
			try
			{
				if (!MapWorkspaceService.RebuildUntitledMapFolder(untitledDirectory, out string errorMessage))
				{
					ShowFeedback(string.Format(TranslationServer.Translate("Could not create a new map folder: {0}"), errorMessage));
					return;
				}

				string? previouslyOpenedFolder = GameSettings.LastOpenedFolder;
				ResetFolderLocations();

				if (!await LoadMapFolderAsync(untitledDirectory))
				{
					string reason = !string.IsNullOrEmpty(_lastMapLoadFailure) ? _lastMapLoadFailure : TranslationServer.Translate("Unknown error.");
					ShowFeedback(string.Format(TranslationServer.Translate("The new map folder was created at {0} but could not be opened: {1}"), untitledDirectory, reason));
					return;
				}

				if (!string.IsNullOrEmpty(previouslyOpenedFolder))
				{
					GameSettings.LastOpenedFolder = previouslyOpenedFolder;
					GameSettings.Save();
				}

				ShowFeedback(TranslationServer.Translate("Started a new untitled map. Give it a name when you save it somewhere of your own."));
			}
			finally
			{
				GameContext.EditorService.IsSyncing = false;
			}
		}

		private static void KeepUntitledMapInHistory(string untitledDirectory)
		{
			if (GameContext.Host == null) return;
			if (!File.Exists(Path.Combine(untitledDirectory, "metadata.json"))) return;

			if (IsSameDirectory(GameContext.EditorService._editingDirectory, untitledDirectory))
			{
				GameContext.Host.SaveMapToFile(Path.Combine(untitledDirectory, "terrain.json"), performReload: false);
				GameContext.Host.EditorHasUnsavedChanges = false;
			}
			else
			{
				GameContext.MapAutosaveService.FlushPendingWrites(TimeSpan.FromSeconds(3));
			}

			GameContext.MapHistoryService.CaptureVersionSynchronously(untitledDirectory, MapVersionKind.BeforeNewMap, TranslationServer.Translate("Before a new map"), untitledDirectory);
		}

		public void ViewLoadMapProperties()
		{
			GameContext.EditorService.LoadMapProperties();
			RefreshWaterSwatches();
			_mapSettingsDialog?.LoadMapProperties();
		}

		private void SelectTerrainTexture(int index, Button swatch)
		{
			if (GameContext.Host != null)
			{
				if (ChkApplyCliffTexture != null && ChkApplyCliffTexture.ButtonPressed && (ChkApplyGroundTexture == null || !ChkApplyGroundTexture.ButtonPressed))
				{
					SelectCliffTexture(index);
					return;
				}

				GameContext.EditorService.EditorPaintTextureIndex = index;
				HighlightSwatch(swatch);

				if (!IsSwatchCompatibleTool(GameContext.Host.ActiveEditorTool))
				{
					TriggerToolSelection(EditorTool.PaintTexture, _btnTextureBrush);
				}

				UpdateTextureLabels();

				string name = TranslationServer.Translate(GameContext.EditorService.SwatchDisplayNames[index]);
				ShowFeedback(string.Format(TranslationServer.Translate("Selected Terrain: {0}"), name));
			}
		}

		private void ApplyLiveLightingTuning()
		{
			if (GameContext.Terrain is EditableTerrain editableTerrain)
			{
				GameContext.EnvironmentService.ApplyLiveLightingTuning(editableTerrain);
			}
		}
	}
}