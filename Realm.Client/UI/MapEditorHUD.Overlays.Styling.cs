// The theme pass for the editor chrome: cards, accordions, content boxes, scroll containers, action and
// tool buttons, checkboxes, sub-containers, the top bar and the status labels, in that order.

using System;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Godot.Collections;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Services.MapHistory;
using Realm.Client.UI.MapEditor;
using Realm.Ecs.Services;
using Realm.EditorAPI;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.Services;
using Environment = System.Environment;
using Key = NSec.Cryptography.Key;
using MirrorMode = Realm.Ecs.Components.Core.MirrorMode;
using WaterType = Realm.Ecs.Components.Terrain.WaterType;
using static Godot.Control;
using Realm.Client.Utils;


namespace Realm.Client.UI
{
	public partial class MapEditorHUD
	{
		private void ApplyThemeStyles()
		{
			ApplyPanelStyles();
			ApplyAccordionStyles();
			ApplyContentBoxStyles();
			ApplyScrollContainerStyles();
			ApplyActionButtonsStyles();
			ApplyToolButtonsStyles();
			ApplyInspectorButtonsStyles();
			ApplyCheckBoxStyles();
			ApplySubContainerStyles();
			ApplyTopBarStyles();
			ApplyStatusLabelStyles();
		}

		/// <summary>
		/// The sidebar's cards, in the order they appear. Every card pass walks this one list, so a new card is styled by
		/// all of them instead of being added to each pass by hand.
		/// </summary>
		private MapEditorCard[] Cards() => new[]
		{
			new MapEditorCard(_accordionFile, _filePanel.Content, 300f),
			new MapEditorCard(_viewportPanel.Accordion, _viewportPanel.Content, 0f, false),
			new MapEditorCard(_toolPanel.Accordion, _toolPanel.Content, 320f),
			new MapEditorCard(_brushPanel?.Accordion, _brushPanel?.Content, 300f),
			new MapEditorCard(_waterPanel?.Accordion, _waterPanel?.Content, 200f),
			new MapEditorCard(_accordionToolSettings, _contentToolSettings, 320f),
			new MapEditorCard(_placementPanel.Accordion, _placementPanel.Content, 320f),
			new MapEditorCard(_accordionInspector, _contentInspector, 300f),
		};

		private void ApplyAccordionStyles()
		{
			foreach (var card in Cards()) MapEditorCardTheme.StyleAccordion(card.Accordion);
		}

		private void ApplyContentBoxStyles()
		{
			foreach (var card in Cards()) MapEditorCardTheme.StyleContent(card.Content);
		}

		private void ApplyScrollContainerStyles()
		{
			foreach (var card in Cards()) MapEditorCardTheme.SetupScroll(card.Content, card.ScrollMaxHeight, card.AllowExpand);
		}

		/// <summary>
		/// The two row buttons in the editor's own chrome. The file card's buttons take the same look from
		/// <see cref="FileOperationsPanel.StyleOperations"/>, which runs with the other panel passes.
		/// </summary>
		private void ApplyActionButtonsStyles()
		{
			if (_topToolbar.TestMapButton != null) StyleRowButton(_topToolbar.TestMapButton);
			StyleRowButton(_btnRandomGen);
		}

		private void ApplyToolButtonsStyles() => _toolPanel.StyleButtons();
		private void ApplyInspectorButtonsStyles() => _inspectorView.StyleButtons();
		/// <summary>
		/// The checkboxes the editor itself owns: the terrain flags on the pathing card. Every other checkbox in the
		/// sidebar belongs to a panel that styles its own rows.
		/// </summary>
		private void ApplyCheckBoxStyles()
		{
			StyleCheckBoxRow(_chkShallowWater);
			StyleCheckBoxRow(_chkDeepWater);
			StyleCheckBoxRow(_chkFlying);
			StyleCheckBoxRow(_chkGround);
			StyleCheckBoxRow(_chkBuildable);
		}

		/// <summary>
		/// The plates the editor itself owns, on the tool-settings card. The brush, placement and paste panels plate their
		/// own sections in their own style passes.
		/// </summary>
		private void ApplySubContainerStyles()
		{
			StyleSubContainer(_containerTextureSettings, "Texture Paint Palette");
			StyleSubContainer(_containerPathingSettings, "Pathing Masks");
			StyleSubContainer(_containerPlacementSettings, "Placement Controls");
			StyleSubContainer(_containerEyedropperSettings, "Eyedropper Sample Filter");
			StyleSubContainer(_containerCategorySelector, "Entity Categories");
		}

		private void ApplyPanelStyles()
		{
			MapEditorChromeTheme.SidebarColumns(this, _panelLeft, _panelRight);
		}

		private void ApplyTopBarStyles()
		{
			MapEditorChromeTheme.TopBar(this, _topBar, _viewportPanel.MeasurementPanel);
		}

		private void ApplyStatusLabelStyles()
		{
			MapEditorChromeTheme.StatusLabels(_topToolbar.StatusLabel, _viewportPanel.MeasurementLabel,
				_topToolbar.FeedbackLabel, GetFontAwesomeFont());
		}
	}
}
