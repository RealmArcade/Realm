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
		private void SetupMirrorDropdown(OptionButton opt, string tooltip = "")
			=> SymmetryDropdowns.Populate(opt, GameContext.Host?.EditorSymmetryFolds ?? 4, SetMirrorMode,
				GetFontAwesomeFont(), tooltip);

		private MapEditorSymmetry? _symmetry;

		/// <summary>
		/// The symmetry setters live in MapEditorSymmetry; the editor supplies the refreshes they trigger, the viewport
		/// accordion they may open, and its status bar.
		/// </summary>
		private MapEditorSymmetry Symmetry => _symmetry ??= new MapEditorSymmetry(new SymmetryHooks(
			UpdateMirrorDropdowns, UpdateSymmetrySubControlsVisibility, UpdatePolarRadialStepButtonText,
			UpdatePolarRingSpacingButtonText,
			() => ExpandAccordion(_viewportPanel.Header, _viewportPanel.Content, "Viewport & Navigation"),
			ShowFeedback));

		private void SetMirrorMode(MirrorMode mode) => Symmetry.SetMirrorMode(mode);

		public void UpdateMirrorDropdowns()
		{
			if (GameContext.Host == null) return;
			int folds = GameContext.Host.EditorSymmetryFolds;
			var mode = GameContext.Host.EditorMirrorMode;

			UpdateSingleMirrorDropdown(_brushPanel.MirrorOption, mode, folds);
			UpdateSingleMirrorDropdown(_placementPanel.MirrorOption, mode, folds);
			UpdateSingleMirrorDropdown(_pastePanel?.ClipboardMirrorMode, mode, folds);

			UpdateSymmetrySubControlsVisibility();
		}

		private void UpdateSingleMirrorDropdown(OptionButton opt, MirrorMode mode, int folds)
			=> SymmetryDropdowns.Refresh(opt, mode, folds);

		private void SetSymmetrySpokes(int spokes) => Symmetry.SetSymmetrySpokes(spokes);

		public void UpdateSymmetrySpokesDropdowns()
		{
			UpdatePolarRadialStepButtonText();
		}

		private void SetupPolarRingSpacingDropdown(OptionButton opt, string tooltip = "")
		{
			if (opt == null) return;
			opt.Clear();
			opt.Set("icon_max_width", 0);
			opt.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			opt.ClipText = true;

			for (int i = 0; i < EditorService.PolarRingSpacingOptions.Length; i++)
			{
				float spacing = EditorService.PolarRingSpacingOptions[i];
				opt.AddItem($"{UnicodeIcons.CIRCLE} " + string.Format(TranslationServer.Translate("RINGS: {0:F0}T"), spacing), (int)spacing);
			}

			if (!string.IsNullOrEmpty(tooltip))
			{
				opt.TooltipText = tooltip;
			}

			StyleOptionButtonPopup(opt);

			opt.ItemSelected += (long idx) =>
			{
				float spacing = (float)opt.GetItemId((int)idx);
				SetPolarRingSpacing(spacing);
			};
		}

		private void SetPolarRingSpacing(float spacing) => Symmetry.SetPolarRingSpacing(spacing);

		private void SetPolarSpokesView(int spokes) => Symmetry.SetPolarSpokesView(spokes);

		private void ResetPivotToMapCenterView()
		{
			if (GameContext.Host == null || GameContext.Host.GroundTerrain == null) return;
			GameContext.EditorService.ResetPivotToMapCenterDomain();
			ShowFeedback(TranslationServer.Translate("Symmetry & Polar Pivot reset to Map Center"));
		}
	}
}
