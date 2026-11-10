using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Godot.Collections;
using NSec.Cryptography;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Services.MapHistory;
using Realm.Client.Services.ModelOptimization;
using Realm.Client.Services.Publishing;
using Realm.Client.UI.MapEditor;
using Realm.Client.VFX;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.EditorAPI;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Environment = System.Environment;
using HttpClient = System.Net.Http.HttpClient;
using Key = Godot.Key;
using MirrorMode = Realm.Ecs.Components.Core.MirrorMode;
using TerrainCell = Realm.Ecs.Components.Terrain.TerrainCell;
using WaterType = Realm.Ecs.Components.Terrain.WaterType;

using static Godot.Control;
using Realm.Client.Utils;

namespace Realm.Client.UI
{
	public partial class MapEditorHUD
	{
		public bool IsPathingAddMode()
		{
			if (_optPathingMode == null) return true;
			return _optPathingMode.Selected == 0;
		}

		public bool IsWaterRemoveAction()
		{
			return GameContext.EditorService.Tools.IsWaterRemove;
		}

		public string GetEyedropperMode()
		{
			if (_optEyedropperMode == null) return "all";
			return _optEyedropperMode.Selected switch
			{
				1 => "3d",
				2 => "decal",
				3 => "terrain",
				4 => "height",
				_ => "all"
			};
		}

		public void UpdateGridSnapExternal(bool snap)
		{
			if (_placementPanel?.SnapButton != null)
			{
				_placementPanel.SnapButton.Text = snap ? TranslationServer.Translate($"{UnicodeIcons.TABLE} SNAP TO GRID: ON") : TranslationServer.Translate($"{UnicodeIcons.TABLE} SNAP TO GRID: OFF");
			}
		}

		public void OpenCoordinateNamingPanel(int minX, int minZ, int maxX, int maxZ)
		{
			GameContext.EditorService.SetPendingCoordinatesDomain(minX, minZ, maxX, maxZ);
			if (_toolPanel.CommitCoordinateButton != null) _toolPanel.CommitCoordinateButton.Visible = true;
			if (_toolPanel.CoordinateNameInput != null) _toolPanel.CoordinateNameInput.GrabFocus();
		}

		public void UpdateMeasureToolExternal(bool active)
		{
			if (_viewportPanel.MeasureButton != null)
			{
				_viewportPanel.MeasureButton.Text = UnicodeIcons.RULER;
				_viewportPanel.MeasureButton.TooltipText = TranslationServer.Translate($"Tape Measure Tool: {(active ? "ACTIVE" : "INACTIVE")} (U)");
				_viewportPanel.MeasureButton.Modulate = active ? new Color(1.3f, 1.15f, 0.7f) : new Color(1f, 1f, 1f);
			}
		}

		private void SetupEnvLightingDropdown(OptionButton opt)
		{
			opt.Clear();
			opt.AddItem(TranslationServer.Translate($"{UnicodeIcons.SUN} Day"), 0);
			opt.AddItem(TranslationServer.Translate($"{UnicodeIcons.CLOUD_SUN} Dusk"), 1);
			opt.AddItem(TranslationServer.Translate($"{UnicodeIcons.MOON} Night"), 2);
			opt.AddItem(TranslationServer.Translate($"{UnicodeIcons.HORIZON_SUN} Dawn"), 3);
			StyleOptionButtonPopup(opt);
		}

		public void UpdateEnvLightingSelection(int timeOfDayIndex)
		{
			if (_viewportPanel.LightingOption != null)
			{
				int idx = Mathf.Clamp(timeOfDayIndex, 0, 3);
				_viewportPanel.LightingOption.Select(idx);
			}
		}



		public void ToggleVSCodeEditor()
		{
			if (!OperatingSystem.IsWindows())
			{
				return;
			}

			if (GameContext.VSCode.IsVisible)
			{
				GameContext.VSCode.Focus();
				return;
			}

			if (!GameContext.VSCode.IsInstalled())
			{
				if (!GameContext.VSCode.IsInstalling) GameContext.VSCode.StartInstallIfNeeded();
				CodeEditorCard.ShowInstallingCard();
				return;
			}

			GenerateVSCodeFilesExternal();
			GameContext.VSCode.SetVisible(true);
			Callable.From(CodeEditorCard.ReportDependencyState).CallDeferred();
		}

		private CodeEditorAvailability? _codeEditorCard;

		private CodeEditorAvailability CodeEditorCard => _codeEditorCard ??= new CodeEditorAvailability(
			new CodeEditorHooks(this, ToggleVSCodeEditor, (button, text, onClick) => SetupButton(button, text, onClick)));

		public void UndoAction()
		{
			EditorHistoryManager.Undo();
			ShowFeedback("Undo Action performed");
		}

		public void RedoAction()
		{
			EditorHistoryManager.Redo();
			ShowFeedback("Redo Action performed");
		}

		public void DeleteSelectedObjectAction()
		{
			DeleteSelectedObject();
		}

		private void DeleteSelectedObject()
		{
			EditorOperationResult result = GameContext.IEditorAPI.DeleteSelectedObject();
			if (!string.IsNullOrEmpty(result.Error))
			{
				ShowFeedback(result.Error);
				return;
			}

			if (result.Success)
			{
				_instanceManagerDialog?.RefreshIfOpen();
			}
		}

		public void RotateSelectedObjectAction(float angleDelta)
		{
			RotateSelectedObject(angleDelta);
		}

		public void ScaleSelectedObjectAction(float scaleMultiplier)
		{
			if (scaleMultiplier < 0f)
			{
				ResetScaleSelectedObject();
			}
			else
			{
				ScaleSelectedObject(scaleMultiplier - 1.0f);
			}
		}

		public void SetSpawnPlayer(int playerIndex)
		{
			if (GameContext.Host == null) return;

			GameContext.EditorService.SetPlaceUnitPlayerIndexDomain(playerIndex);
			SyncPlacementPlayerSelection(GameContext.EditorService.Tools.PlaceUnitPlayerIndex);
		}

		private void SyncPlacementPlayerSelection(int playerIndex)
		{
			if (_placementPanel?.PlayerOption == null) return;

			_placementPanel.PlayerOption.Selected = Mathf.Clamp(playerIndex, 0, PlayerColorConfig.Palette.Length - 1);
		}

		public void SelectCategoryItemExternal(string category, string filename)
		{
			_entityPaletteController?.SelectCategoryItemExternal(category, filename);
		}

		public void SelectPickedDecalView(string decalId)
		{
			_entityPaletteController?.SelectCategoryItemExternal("Decals", decalId);
		}

		public EditorPaintSurfaceMode GetPaintSurfaceMode()
		{
			bool applyGround = ChkApplyGroundTexture == null || ChkApplyGroundTexture.ButtonPressed;
			bool applyCliff = ChkApplyCliffTexture == null || ChkApplyCliffTexture.ButtonPressed;

			if (applyGround && applyCliff) return EditorPaintSurfaceMode.GroundAndCliff;
			if (applyGround) return EditorPaintSurfaceMode.GroundOnly;
			return EditorPaintSurfaceMode.CliffOnly;
		}

		public void CyclePaintSurfaceMode()
		{
			EditorPaintSurfaceMode nextMode = GetPaintSurfaceMode() switch
			{
				EditorPaintSurfaceMode.GroundOnly => EditorPaintSurfaceMode.GroundAndCliff,
				EditorPaintSurfaceMode.GroundAndCliff => EditorPaintSurfaceMode.CliffOnly,
				_ => EditorPaintSurfaceMode.GroundOnly
			};

			ApplyPaintSurfaceMode(nextMode);
			ShowFeedback(string.Format(TranslationServer.Translate("Painting {0}"), TranslatePaintSurfaceMode(nextMode)));
		}

		private void ApplyPaintSurfaceMode(EditorPaintSurfaceMode mode)
		{
			if (ChkApplyGroundTexture == null || ChkApplyCliffTexture == null) return;

			ChkApplyGroundTexture.SetPressedNoSignal(mode != EditorPaintSurfaceMode.CliffOnly);
			ChkApplyCliffTexture.SetPressedNoSignal(mode != EditorPaintSurfaceMode.GroundOnly);
			UpdateTextureLabels();
		}

		private static string TranslatePaintSurfaceMode(EditorPaintSurfaceMode mode)
		{
			return mode switch
			{
				EditorPaintSurfaceMode.GroundOnly => TranslationServer.Translate("ground only"),
				EditorPaintSurfaceMode.CliffOnly => TranslationServer.Translate("cliff only"),
				_ => TranslationServer.Translate("ground and cliff")
			};
		}

		public void UpdateBrushShapeExternal(bool isSquare)
		{
			if (GameContext.EditorService.Placement.TrySetBrushShape(isSquare))
			{
				if (_brushPanel?.ShapeButton != null)
				{
					_brushPanel.ShapeButton.Text = isSquare ? TranslationServer.Translate($"{UnicodeIcons.SQUARE} BRUSH: SQUARE") : TranslationServer.Translate($"{UnicodeIcons.CIRCLE} BRUSH: CIRCLE");
				}

				if (_pastePanel?.ClipboardBrushShape != null)
				{
					_pastePanel.ClipboardBrushShape.Text = isSquare ? TranslationServer.Translate($"{UnicodeIcons.SQUARE} BRUSH: SQUARE") : TranslationServer.Translate($"{UnicodeIcons.CIRCLE} BRUSH: CIRCLE");
				}

				if (GameContext.Host != null && GameContext.Host.GroundTerrain != null && GameContext.EditorService != null && GameContext.EditorService.Interaction.SelectionStart != null && GameContext.EditorService.Interaction.SelectionEnd != null)
				{
					var (minX, minZ, maxX, maxZ) = GameContext.EditorService.Interaction.GetSelectionBounds();
					GameContext.Host.RebuildSelectionHighlightMeshExternal(minX, minZ, maxX, maxZ);
				}
			}
		}

		public void UpdateBrushStrengthExternal(float strength)
		{
			if (_brushPanel?.StrengthSlider != null)
			{
				_brushPanel.StrengthSlider.Value = strength;
			}

			if (_brushPanel?.StrengthValue != null)
			{
				_brushPanel.StrengthValue.Text = strength.ToString("F1");
			}
		}

		public void UpdateBlockModeExternal(bool enabled)
		{
			if (_brushPanel?.BlockModeCheck != null)
			{
				_brushPanel.BlockModeCheck.ButtonPressed = enabled;
			}
		}

		public void SelectToolFromHotkey(EditorTool tool)
		{
			Button? targetBtn = null;
			switch (tool)
			{
				case EditorTool.Raise: targetBtn = _toolPanel.RaiseButton; break;
				case EditorTool.Lower: targetBtn = _toolPanel.LowerButton; break;
				case EditorTool.Height: targetBtn = _toolPanel.HeightButton; break;
				case EditorTool.Smooth: targetBtn = _toolPanel.SmoothButton; break;
				case EditorTool.Plateau: targetBtn = _toolPanel.FlattenButton; break;
				case EditorTool.Ramp: targetBtn = _toolPanel.RampButton; break;
				case EditorTool.Noise: targetBtn = _toolPanel.NoiseButton; break;
				case EditorTool.Water: targetBtn = _toolPanel.WaterButton; break;
				case EditorTool.PaintTexture: targetBtn = _toolPanel.TextureBrushButton; break;
				case EditorTool.FloodFill: targetBtn = _toolPanel.FloodFillButton; break;
				case EditorTool.PaintPathing: targetBtn = _toolPanel.PathingBrushButton; break;
				case EditorTool.FloodFillPathing: targetBtn = _toolPanel.FloodFillPathingButton; break;
				case EditorTool.DrawCoordinate: targetBtn = _toolPanel.DrawCoordinateButton; break;
				case EditorTool.SelectArea: targetBtn = _toolPanel.SelectAreaButton; break;
				case EditorTool.PasteArea: targetBtn = _toolPanel.PasteButton; break;
				case EditorTool.PlaceUnit:
				case EditorTool.PlaceProp:
				case EditorTool.PlaceDecal:
					targetBtn = _toolPanel.AddObjectButton;
					break;
				case EditorTool.DeleteObject: targetBtn = _toolPanel.DeleteObjectButton; break;
				case EditorTool.SelectMove: targetBtn = _toolPanel.SelectMoveButton; break;
				case EditorTool.Eyedropper: targetBtn = _topToolbar.EyedropperButton; break;
				case EditorTool.Measure: targetBtn = _viewportPanel.MeasureButton; break;
			}

			if (targetBtn != null)
			{
				TriggerToolSelection(tool, targetBtn, GameContext.Host?.ActivePlaceId ?? "");
			}
		}

		private Button? GetButtonForTool(EditorTool tool, string placeId)
		{
			return tool switch
			{
				EditorTool.Raise => _toolPanel.RaiseButton,
				EditorTool.Lower => _toolPanel.LowerButton,
				EditorTool.Height => _toolPanel.HeightButton,
				EditorTool.Smooth => _toolPanel.SmoothButton,
				EditorTool.Plateau => _toolPanel.FlattenButton,
				EditorTool.Ramp => _toolPanel.RampButton,
				EditorTool.Noise => _toolPanel.NoiseButton,
				EditorTool.Water => _toolPanel.WaterButton,
				EditorTool.PaintPathing => _toolPanel.PathingBrushButton,
				EditorTool.FloodFillPathing => _toolPanel.FloodFillPathingButton,
				EditorTool.DrawCoordinate => _toolPanel.DrawCoordinateButton,
				EditorTool.SelectArea => _toolPanel.SelectAreaButton,
				EditorTool.SelectMove => _toolPanel.SelectMoveButton,
				EditorTool.DeleteObject => _toolPanel.DeleteObjectButton,
				EditorTool.PaintTexture => GetTextureSwatchButton(placeId),
				EditorTool.FloodFill => _toolPanel.FloodFillButton,
				EditorTool.Eyedropper => _topToolbar.EyedropperButton,
				_ => null
			};
		}

		private Button GetTextureSwatchButton(string placeId)
		{
			for (int i = 0; i < GameContext.EditorService.Swatches.Paths.Count; i++)
			{
				if (GameContext.EditorService.Swatches.Paths[i] != placeId) continue;

				var swatch = Swatches.GetButton(i);
				if (swatch != null) return swatch;
			}

			return _toolPanel.TextureBrushButton;
		}
	}
}
