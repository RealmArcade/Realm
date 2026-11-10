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
		private void StyleAccordionHeader(Button btn) => MapEditorStyle.AccordionHeader(btn, GetFontAwesomeFont());

		public FontVariation? GetFontAwesomeFont()
		{
			if (_faFontVariation == null)
			{
				try
				{
					var faFont = GD.Load<FontFile>("res://Assets/UI/fa-solid-900.ttf");
					if (faFont != null)
					{
						_faFontVariation = new FontVariation();
						_faFontVariation.Fallbacks = new Array<Font> { faFont };
					}
				}
				catch
				{
					_faFontVariation = null;
				}
			}

			return _faFontVariation;
		}

		private void StyleIconButton(Button btn, string iconText, string tooltipText) => MapEditorStyle.IconButton(btn, iconText, tooltipText, GetFontAwesomeFont());


		private void RestructurePanelLayouts()
		{
			StyleFileAccordionOperations();

			StyleViewportNavigationBar();

			StyleToolPanelContainers();

			StyleBrushAccordionControls();

			StyleTexturePaletteRows();

			if (_containerPathingSettings != null)
			{
				StyleSubContainer(_containerPathingSettings, "Pathing Properties");
			}

			StylePasteAndPlacementSettings();

			RestyleOptionButtonContents();
		}








		private void StylePasteAndPlacementSettings()
		{
			_pastePanel.StyleOptions();

			_placementPanel.StyleControls();

			if (_containerEyedropperSettings != null)
			{
				MapEditorStyle.SubContainer(_containerEyedropperSettings, "Eyedropper Settings");
			}
		}

		private void RestyleOptionButtonContents()
		{
			Control[] allOptionContents = new Control[]
			{
				_filePanel.Content, _viewportPanel.Content, _toolPanel.Content,
				_brushPanel.Content, _contentToolSettings, _placementPanel.Content, _contentInspector, _contentLightingTuning
			};
			foreach (var content in allOptionContents)
			{
				MapEditorStyle.OptionButtonsInContainer(content, GetFontAwesomeFont());
			}
		}


		private void StyleBrushAccordionControls() => _brushPanel.StyleControls();


		private void StyleToolPanelContainers() => _toolPanel.StyleContainers(_contentInspector);


		private void StyleFileAccordionOperations() => _filePanel.StyleOperations();


		private void StyleViewportNavigationBar()
		{
			var polarRow = _viewportPanel.StyleNavigationBar(GetFontAwesomeFont(), _cameraPanel.ToggleButton);
			if (polarRow != null) _rowPolarConfig = polarRow;
			if (_rowPolarConfig == null) return;

			bool isSymmetryActive = GameContext.Host != null &&
			                        (GameContext.Host.EditorMirrorMode != MirrorMode.None ||
			                         GameContext.EditorService.Tools.GridMode == GridOverlayMode.Polar ||
			                         GameContext.EditorService.Tools.GridMode == GridOverlayMode.Both);
			_rowPolarConfig.Visible = isSymmetryActive;
		}


		private void StyleTexturePaletteRows()
		{
			if (_containerTextureSettings == null) return;

			MapEditorStyle.SubContainer(_containerTextureSettings, "Texture Palette & Settings");

			if (RowGroundTexture != null)
			{
				MapEditorStyle.RowBackground(RowGroundTexture,
					new Color(0.12f, 0.16f, 0.20f, 0.85f), new Color(0.20f, 0.65f, 0.95f, 0.85f));
			}

			if (RowCliffTexture != null)
			{
				MapEditorStyle.RowBackground(RowCliffTexture,
					new Color(0.18f, 0.14f, 0.10f, 0.85f), new Color(0.95f, 0.55f, 0.15f, 0.85f));
			}

			if (_lblTerrainTexture != null)
			{
				_lblTerrainTexture.AddThemeFontSizeOverride("font_size", 11);
				_lblTerrainTexture.AddThemeColorOverride("font_color", new Color(0.40f, 0.80f, 1.0f));
			}

			if (_lblCliffTexture != null)
			{
				_lblCliffTexture.AddThemeFontSizeOverride("font_size", 11);
				_lblCliffTexture.AddThemeColorOverride("font_color", new Color(1.0f, 0.70f, 0.30f));
			}
		}
	}
}
