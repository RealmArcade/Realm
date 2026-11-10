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
		private const string LeftColumnPath = "LeftSlidePanel/LeftScroll/LeftVBox";
		private const string RightColumnPath = "RightSlidePanel/RightScroll/AccordionContainer";
		private const string LeftScrollerPath = "LeftSlidePanel/LeftScroll";
		private const string RightScrollerPath = "RightSlidePanel/RightScroll";
		private const float LeftPanelDesignWidth = 325f;
		private const float RightPanelDesignWidth = 300f;
		private const float WidthChangeTolerance = 0.5f;

		private float _leftPanelWidth { get; set; } = LeftPanelDesignWidth;
		private float _rightPanelWidth { get; set; } = RightPanelDesignWidth;
		private bool _sidebarWidthsHooked { get; set; }

		private void ApplyThemeStyles()
		{
			ApplyPanelStyles();
			ApplyAccordionStyles();
			ApplyContentBoxStyles();
			ApplyScrollContainerStyles();
			ApplyActionButtonsStyles();
			ApplyCheckBoxStyles();
			ApplySubContainerStyles();
			ApplyTopBarStyles();
			ApplyStatusLabelStyles();
		}

		/// <summary>
		/// The sidebar's cards, in the order they appear. Every card pass walks this one list, so a new card is styled by
		/// all of them instead of being added to each pass by hand. Only the file and viewport cards cap their content;
		/// the tool cards keep their content at its natural height, because a card that clips its own content crops the
		/// sidebar even when the column has room for it.
		/// </summary>
		private MapEditorCard[] Cards() => new[]
		{
			new MapEditorCard(_accordionFile, _filePanel.Content, 300f),
			new MapEditorCard(_viewportPanel.Accordion, _viewportPanel.Content, 0f, false),
			new MapEditorCard(_toolPanel.Accordion, _toolPanel.Content, 320f, true, false),
			new MapEditorCard(_brushPanel?.Accordion, _brushPanel?.Content, 300f, true, false),
			new MapEditorCard(_waterPanel?.Accordion, _waterPanel?.Content, 200f, true, false),
			new MapEditorCard(_accordionToolSettings, _contentToolSettings, 320f, true, false),
			new MapEditorCard(_placementPanel.Accordion, _placementPanel.Content, 320f, true, false),
			new MapEditorCard(_accordionInspector, _contentInspector, 300f, true, false),
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
			foreach (var card in Cards()) MapEditorCardTheme.SetupScroll(card.Content, card.ScrollMaxHeight, card.AllowExpand, card.Scrollable);
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
			HookSidebarWidthChanges();
			FitSidebarWidths();
		}

		/// <summary>
		/// The columns are sized to the cards, not the cards to the columns: whenever the widest card in a column changes
		/// - a card is expanded, shows more rows, or is rebuilt with different texture names - the column reports a new
		/// minimum and the panel is re-fitted, so no card is ever drawn past the panel's edge.
		/// </summary>
		private void HookSidebarWidthChanges()
		{
			if (_sidebarWidthsHooked) return;
			_sidebarWidthsHooked = true;

			var leftColumn = GetNodeOrNull<Control>(LeftColumnPath);
			if (leftColumn != null) leftColumn.MinimumSizeChanged += FitSidebarWidths;

			var rightColumn = GetNodeOrNull<Control>(RightColumnPath);
			if (rightColumn != null) rightColumn.MinimumSizeChanged += FitSidebarWidths;

			GetViewport()?.SizeChanged += FitSidebarWidths;
		}

		/// <summary>
		/// Widens each sidebar panel to the width its column needs and remembers it, so the expand/collapse tweens slide
		/// the panel by its real width. A measurement that has not changed is not applied, which keeps the
		/// <see cref="Control.MinimumSizeChanged"/> that triggered it from re-entering.
		/// </summary>
		private void FitSidebarWidths()
		{
			float viewportWidth = GetViewportRect().Size.X;

			float leftWidth = MapEditorChromeTheme.MeasureColumnWidth(
				GetNodeOrNull<Control>(LeftColumnPath), GetNodeOrNull<ScrollContainer>(LeftScrollerPath),
				LeftPanelDesignWidth, viewportWidth);

			float rightWidth = MapEditorChromeTheme.MeasureColumnWidth(
				GetNodeOrNull<Control>(RightColumnPath), GetNodeOrNull<ScrollContainer>(RightScrollerPath),
				RightPanelDesignWidth, viewportWidth);

			if (Mathf.Abs(leftWidth - _leftPanelWidth) > WidthChangeTolerance)
			{
				_leftPanelWidth = leftWidth;
				MapEditorChromeTheme.ApplyColumnWidth(_panelLeft, anchoredRight: false, _leftPanelExpanded, leftWidth);
			}

			if (Mathf.Abs(rightWidth - _rightPanelWidth) > WidthChangeTolerance)
			{
				_rightPanelWidth = rightWidth;
				MapEditorChromeTheme.ApplyColumnWidth(_panelRight, anchoredRight: true, _rightPanelExpanded, rightWidth);
			}
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
