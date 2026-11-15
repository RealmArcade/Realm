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
		public void SwitchModuleView(EditorModule module)
		{
			_activeModule = module;
			UpdateModuleSwitchButtons();

			if (module != EditorModule.Coordinates)
			{
				GameContext.Host?.HideCoordinateSelectionOutline();
			}

			UpdatePanelVisibilityForModule(module);

			if (GameContext.Host != null)
			{
				switch (module)
				{
					case EditorModule.Terrain:
						TriggerToolSelection(EditorTool.Raise, _toolPanel.RaiseButton);
						break;
					case EditorModule.TextureDeco:
						TriggerToolSelection(EditorTool.PaintTexture, _toolPanel.TextureBrushButton);
						break;
					case EditorModule.Pathing:
						TriggerToolSelection(EditorTool.PaintPathing, _toolPanel.PathingBrushButton);
						break;
					case EditorModule.Objects:
						_entityPaletteController?.TriggerAddObjectMode();
						break;
					case EditorModule.Coordinates:
						TriggerToolSelection(EditorTool.DrawCoordinate, _toolPanel.DrawCoordinateButton);
						break;
					case EditorModule.Clipboard:
						TriggerToolSelection(EditorTool.SelectArea, _toolPanel.SelectAreaButton);
						break;
				}
			}
		}

		private void UpdateSidebarMorph(EditorTool tool)
		{
			if (_panelRight == null) return;

			bool isClumpActive = _placementPanel?.ClumpModeCheck != null && _placementPanel.ClumpModeCheck.ButtonPressed;
			bool isBrush = (tool == EditorTool.Raise ||
			                tool == EditorTool.Lower ||
			                tool == EditorTool.Height ||
			                tool == EditorTool.Smooth ||
			                tool == EditorTool.Plateau ||
			                tool == EditorTool.Ramp ||
			                tool == EditorTool.PaintTexture ||
			                tool == EditorTool.Noise ||
			                tool == EditorTool.PaintPathing ||
			                tool == EditorTool.FloodFillPathing ||
			                tool == EditorTool.PlacePropClump ||
			                ((tool == EditorTool.PlaceUnit || tool == EditorTool.PlaceProp || tool == EditorTool.PlaceDecal) && isClumpActive))
			               && tool != EditorTool.Water;

			if (_brushPanel?.Accordion != null)
			{
				_brushPanel.Accordion.Visible = isBrush;
				UpdateBrushSizeVisibility();
				UpdateBrushShapeVisibility();
				UpdateBrushStrengthVisibility();
				bool isTextureMode = tool == EditorTool.PaintTexture;
				if (_brushPanel?.BlockModeCheck != null)
				{
					_brushPanel.BlockModeCheck.Visible = (tool != EditorTool.PaintPathing &&
					                         tool != EditorTool.FloodFillPathing &&
					                         !isTextureMode &&
					                         tool != EditorTool.Smooth &&
					                         tool != EditorTool.Noise &&
					                         tool != EditorTool.Ramp &&
					                         tool != EditorTool.PlacePropClump &&
					                         tool != EditorTool.Height &&
					                         !isClumpActive);
				}

				UpdateBlockStepVisibility();
			}

			if (_waterPanel?.Accordion != null)
			{
				_waterPanel.Accordion.Visible = (tool == EditorTool.Water);
			}

			bool isBlockModeActive = (_brushPanel?.BlockModeCheck != null && _brushPanel.BlockModeCheck.Visible && _brushPanel.BlockModeCheck.ButtonPressed) || (GameContext.Host != null && GameContext.Host.EditorBlockMode);
			bool isPaintTool = tool == EditorTool.PaintTexture ||
			                   tool == EditorTool.FloodFill;
			bool isBlockHeightTool = (isBlockModeActive && (
				                         tool == EditorTool.Raise ||
				                         tool == EditorTool.Lower ||
				                         tool == EditorTool.Plateau)) ||
			                         tool == EditorTool.Height;

			bool isRampTool = tool == EditorTool.Ramp;

			bool texSettingsVisible = _containerTextureSettings != null && (isPaintTool || isBlockHeightTool || isRampTool);
			if (_containerTextureSettings != null) _containerTextureSettings.Visible = texSettingsVisible;

			bool isTexturePaintToolOnly = tool == EditorTool.PaintTexture;

			if (ChkApplyGroundTexture != null) ChkApplyGroundTexture.Visible = texSettingsVisible && isTexturePaintToolOnly;
			if (ChkApplyCliffTexture != null) ChkApplyCliffTexture.Visible = texSettingsVisible && isTexturePaintToolOnly;

			bool pathingSettingsVisible = _containerPathingSettings != null && (tool == EditorTool.PaintPathing || tool == EditorTool.FloodFillPathing);
			if (_containerPathingSettings != null) _containerPathingSettings.Visible = pathingSettingsVisible;

			bool eyedropperSettingsVisible = _containerEyedropperSettings != null && (tool == EditorTool.Eyedropper);
			if (_containerEyedropperSettings != null) _containerEyedropperSettings.Visible = eyedropperSettingsVisible;

			bool pasteSettingsVisible = _pastePanel?.Container != null && (tool == EditorTool.SelectArea || tool == EditorTool.PasteArea);
			if (_pastePanel?.Container != null) _pastePanel.Container.Visible = pasteSettingsVisible;

			bool isPlacement = (tool == EditorTool.PlaceUnit ||
			                    tool == EditorTool.PlaceProp ||
			                    tool == EditorTool.PlacePropClump ||
			                    tool == EditorTool.PlaceDecal ||
			                    tool == EditorTool.PlaceVfx ||
			                    tool == EditorTool.PlaceItem);

			bool categorySelectorVisible = _containerCategorySelector != null && isPlacement;
			if (_containerCategorySelector != null) _containerCategorySelector.Visible = categorySelectorVisible;

			bool anyToolSettingVisible = texSettingsVisible ||
			                             pathingSettingsVisible ||
			                             eyedropperSettingsVisible ||
			                             pasteSettingsVisible ||
			                             categorySelectorVisible;

			if (_accordionToolSettings != null)
			{
				_accordionToolSettings.Visible = anyToolSettingVisible;
			}

			bool hasPlacementConfig = isPlacement;
			if (_placementPanel?.Accordion != null)
			{
				_placementPanel.Accordion.Visible = hasPlacementConfig;
			}

			if (_placementPanel?.PlayerContainer != null)
			{
				bool showPlacementPlayer = tool == EditorTool.PlaceUnit;
				_placementPanel.PlayerContainer.Visible = showPlacementPlayer;
				if (showPlacementPlayer)
				{
					SyncPlacementPlayerSelection(GameContext.EditorService.Tools.PlaceUnitPlayerIndex);
				}
			}

			if (_placementPanel?.ClumpModeCheck != null)
			{
				_placementPanel.ClumpModeCheck.Visible = isPlacement;
				if (_spacingBox != null) _spacingBox.Visible = isPlacement && _placementPanel.ClumpModeCheck.ButtonPressed;
				if (_placementPanel?.DensityBox != null) _placementPanel.DensityBox.Visible = isPlacement && _placementPanel.ClumpModeCheck.ButtonPressed;
				if (_placementPanel?.ScaleVarBox != null) _placementPanel.ScaleVarBox.Visible = isPlacement && _placementPanel.ClumpModeCheck.ButtonPressed;
			}

			bool hasSelectedObject = GameContext.Host != null && GodotObject.IsInstanceValid(GameContext.Host.SelectedEditorObject);
			if (_accordionInspector != null)
			{
				_accordionInspector.Visible = hasSelectedObject && (tool == EditorTool.SelectMove);
			}

			if (_accordionContainer != null)
			{
				_accordionContainer.QueueSort();
			}
		}

		public override void _UnhandledInput(InputEvent @event)
		{
			if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
			{
				if (keyEvent.Keycode == Key.F1)
				{
					SwitchModuleView(EditorModule.Terrain);
					GetViewport().SetInputAsHandled();
				}
				else if (keyEvent.Keycode == Key.F2)
				{
					SwitchModuleView(EditorModule.TextureDeco);
					GetViewport().SetInputAsHandled();
				}
				else if (keyEvent.Keycode == Key.F3)
				{
					SwitchModuleView(EditorModule.Pathing);
					GetViewport().SetInputAsHandled();
				}
				else if (keyEvent.Keycode == Key.F4)
				{
					SwitchModuleView(EditorModule.Objects);
					GetViewport().SetInputAsHandled();
				}
				else if (keyEvent.Keycode == Key.F5)
				{
					SwitchModuleView(EditorModule.Coordinates);
					GetViewport().SetInputAsHandled();
				}
				else if (keyEvent.Keycode == Key.F6)
				{
					SwitchModuleView(EditorModule.Clipboard);
					GetViewport().SetInputAsHandled();
				}
				else if (keyEvent.Keycode == Key.F7)
				{
					if (GameContext.Host != null && GameContext.Host.GroundTerrain != null)
					{
						GameContext.Host.GroundTerrain.ToggleWireframeMode();
						bool isWireframe = GetViewport()?.DebugDraw == Viewport.DebugDrawEnum.Wireframe;
						UpdateWireframeOverlayExternal(isWireframe);
					}

					GetViewport().SetInputAsHandled();
				}
				else if (keyEvent.Keycode == Key.F8)
				{
					ToggleFreeCamera();
					GetViewport().SetInputAsHandled();
				}
				else if (keyEvent.Keycode == Key.F9)
				{
					ToggleShadows();
					GetViewport().SetInputAsHandled();
				}
			}
		}
	}
}
