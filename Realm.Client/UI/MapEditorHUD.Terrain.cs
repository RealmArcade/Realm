using System.IO;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;

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
			ResetFolderLocations();
			GameContext.EditorService.ResetToBlankMapDomain();
			string terrainPath = Path.Combine(GameContext.EditorService._tempWorkspacePath, "terrain.json");
			string metadataPath = Path.Combine(GameContext.EditorService._tempWorkspacePath, "metadata.json");
			GameContext.EditorService.LastTerrainSyncTime = GameContext.EditorService.GetMaxTerrainWriteTime(terrainPath);
			GameContext.EditorService.LastMetadataSyncTime = GameContext.EditorService.GetLastWriteTimeSafe(metadataPath);
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