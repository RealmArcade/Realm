using System;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Ecs;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the tool accordion needs from the editor: its node tree, the shared accordion and button styling, the
/// tool-selection broadcast every card fires, the FontAwesome face the icon glyphs use, and the handful of
/// editor actions the object and coordinate cards invoke.
/// </summary>
public sealed record ToolAccordionHooks(
	Node Owner,
	Action<Button> StyleAccordionHeader,
	Action<Button, Control, string> SetupAccordion,
	Action<Button, string, Action?, int, string> SetupButton,
	Action<Button, string, Action?, int, string> SetupOptionButton,
	Action<EditorTool, Button?> SelectTool,
	Func<Font?> FontAwesomeFont,
	Action AddObjectRequested,
	Action DeleteSelectedObjectRequested,
	Action<string> ShowFeedback,
	Action RefreshCoordinateList,
	Action<string> OpenInstanceManager,
	Action OpenSpawnAnchors);

/// <summary>
/// The tool accordion in the editor's right panel: the per-module card grids (terrain, texture, pathing, objects,
/// clipboard) and the named-coordinate panel. It builds the card views and owns the resulting buttons, which the
/// editor still reads for tool highlighting and sidebar morphing; selection itself stays with
/// <see cref="ToolAccordionHooks.SelectTool"/>.
/// </summary>
public sealed class ToolAccordionPanel
{
	private const string BasePath = "RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion";
	private const string ContentPath = BasePath + "/ContentTool";

	public VBoxContainer Accordion { get; private set; } = null!;
	public Button Header { get; private set; } = null!;
	public VBoxContainer Content { get; private set; } = null!;
	public VBoxContainer TerrainPanel { get; private set; } = null!;
	public VBoxContainer DecoPanel { get; private set; } = null!;
	public VBoxContainer PathingPanel { get; private set; } = null!;
	public VBoxContainer CoordinatesPanel { get; private set; } = null!;
	public VBoxContainer ObjectsPanel { get; private set; } = null!;
	public VBoxContainer ClipboardPanel { get; private set; } = null!;
	public Button RaiseButton { get; private set; } = null!;
	public Button LowerButton { get; private set; } = null!;
	public Button HeightButton { get; private set; } = null!;
	public Button SmoothButton { get; private set; } = null!;
	public Button FlattenButton { get; private set; } = null!;
	public Button RampButton { get; private set; } = null!;
	public Button NoiseButton { get; private set; } = null!;
	public Button WaterButton { get; private set; } = null!;
	public Button TextureBrushButton { get; private set; } = null!;
	public Button FloodFillButton { get; private set; } = null!;
	public Button SelectAreaButton { get; private set; } = null!;
	public Button CutButton { get; private set; } = null!;
	public Button CopyButton { get; private set; } = null!;
	public Button PasteButton { get; private set; } = null!;
	public Button EraseAreaButton { get; private set; } = null!;
	public Button PathingBrushButton { get; private set; } = null!;
	public Button FloodFillPathingButton { get; private set; } = null!;
	public Button SelectMoveButton { get; private set; } = null!;
	public Button AddObjectButton { get; private set; } = null!;
	public Button DeleteObjectButton { get; private set; } = null!;
	public Button DrawCoordinateButton { get; private set; } = null!;
	public LineEdit CoordinateNameInput { get; private set; } = null!;
	public Button CommitCoordinateButton { get; private set; } = null!;
	public Button ViewCoordinatesButton { get; private set; } = null!;
	public Button OpenSpawnAnchorsButton { get; private set; } = null!;

	public void Build(ToolAccordionHooks hooks)
	{
		BuildAccordion(hooks);

		var terrainGrid = hooks.Owner.GetNode<GridContainer>(ContentPath + "/PanelTerrainVBox/TerrainGrid_GridContainer");
		var decoGrid = hooks.Owner.GetNode<GridContainer>(ContentPath + "/PanelDecoVBox/DecoGrid_GridContainer");
		var pathGrid = hooks.Owner.GetNode<GridContainer>(ContentPath + "/PanelPathingVBox/PathGrid_GridContainer");
		var objGrid = hooks.Owner.GetNode<GridContainer>(ContentPath + "/PanelObjectsVBox/ObjGrid_GridContainer");
		var clipGrid = hooks.Owner.GetNode<GridContainer>(ContentPath + "/PanelClipboard/ClipGrid_GridContainer");

		BuildTerrainToolCards(hooks, terrainGrid);

		TextureBrushButton = CreateToolCard(hooks, decoGrid, "BtnTextureBrush", UnicodeIcons.PAINT_BRUSH, "Paint", () => hooks.SelectTool(EditorTool.PaintTexture, TextureBrushButton), "Paint terrain texture (8)");

		FloodFillButton = CreateToolCard(hooks, decoGrid, "BtnFloodFill", UnicodeIcons.FILL_DRIP, "Flood Fill", () => hooks.SelectTool(EditorTool.FloodFill, FloodFillButton), "Flood fill terrain texture");

		BuildAreaAndPathingToolCards(hooks, clipGrid, pathGrid);

		BuildObjectToolCards(hooks, objGrid);

		BuildCoordinatePanel(hooks);

		CutButton = CreateToolCard(hooks, clipGrid, "BtnCut", UnicodeIcons.SCISSORS, "Cut", () => GameContext.EditorService.CutSelectedArea(), "Cut selected area to clipboard (Ctrl+X)");

		CopyButton = CreateToolCard(hooks, clipGrid, "BtnCopy", UnicodeIcons.COPY, "Copy", () => GameContext.EditorService.CopySelectedArea(), "Copy selected area to clipboard (Ctrl+C)");

		PasteButton = CreateToolCard(hooks, clipGrid, "BtnPaste", UnicodeIcons.PASTE, "Paste", () => hooks.SelectTool(EditorTool.PasteArea, PasteButton), "Paste clipboard contents onto terrain (Ctrl+V)");

		EraseAreaButton = CreateToolCard(hooks, clipGrid, "BtnEraseArea", UnicodeIcons.TRASH, "Erase Area", () => GameContext.EditorService.EraseSelectedArea(), "Erase heights, textures and objects within selection (Delete)");
	}

	private void BuildAccordion(ToolAccordionHooks hooks)
	{
		Accordion = hooks.Owner.GetNode<VBoxContainer>(BasePath);
		Header = hooks.Owner.GetNode<Button>(BasePath + "/BtnHeaderTool");
		Content = hooks.Owner.GetNode<VBoxContainer>(ContentPath);
		hooks.StyleAccordionHeader(Header);
		hooks.SetupAccordion(Header, Content, TranslationServer.Translate("Tool"));

		TerrainPanel = hooks.Owner.GetNode<VBoxContainer>(ContentPath + "/PanelTerrainVBox");
		DecoPanel = hooks.Owner.GetNode<VBoxContainer>(ContentPath + "/PanelDecoVBox");
		PathingPanel = hooks.Owner.GetNode<VBoxContainer>(ContentPath + "/PanelPathingVBox");
		CoordinatesPanel = hooks.Owner.GetNode<VBoxContainer>(ContentPath + "/PanelCoordinatesVBox");
		ObjectsPanel = hooks.Owner.GetNode<VBoxContainer>(ContentPath + "/PanelObjectsVBox");
		ClipboardPanel = hooks.Owner.GetNode<VBoxContainer>(ContentPath + "/PanelClipboard");
	}

	private void BuildTerrainToolCards(ToolAccordionHooks hooks, GridContainer terrainGrid)
	{
		RaiseButton = CreateToolCard(hooks, terrainGrid, "BtnRaise", UnicodeIcons.ARROW_UP, "Raise", () => hooks.SelectTool(EditorTool.Raise, RaiseButton), "Elevate terrain height (1)");

		LowerButton = CreateToolCard(hooks, terrainGrid, "BtnLower", UnicodeIcons.ARROW_DOWN, "Lower", () => hooks.SelectTool(EditorTool.Lower, LowerButton), "Lower terrain height (2)");

		HeightButton = CreateToolCard(hooks, terrainGrid, "BtnHeight", UnicodeIcons.ARROWS_V, "Height", () => hooks.SelectTool(EditorTool.Height, HeightButton), "Set terrain to exact height (3)");

		SmoothButton = CreateToolCard(hooks, terrainGrid, "BtnSmooth", UnicodeIcons.SMOOTH, "Smooth", () => hooks.SelectTool(EditorTool.Smooth, SmoothButton), "Smooth terrain height (4)");

		FlattenButton = CreateToolCard(hooks, terrainGrid, "BtnPlateau", UnicodeIcons.SQUARE, "Flatten", () => hooks.SelectTool(EditorTool.Plateau, FlattenButton), "Flatten terrain to cursor height on click (5)");

		RampButton = CreateToolCard(hooks, terrainGrid, "BtnRamp", UnicodeIcons.RAMP, "Ramp", () => hooks.SelectTool(EditorTool.Ramp, RampButton), "Create ramp between two points (6)");

		NoiseButton = CreateToolCard(hooks, terrainGrid, "BtnNoise", UnicodeIcons.NOISE, "Noise", () => hooks.SelectTool(EditorTool.Noise, NoiseButton), "Add random height variations/noise to terrain (7)");

		WaterButton = CreateToolCard(hooks, terrainGrid, "BtnWater", UnicodeIcons.WATER, "Water", () => hooks.SelectTool(EditorTool.Water, WaterButton), "Flood fill water mesh bounded by cliff walls (0)");
	}

	private void BuildAreaAndPathingToolCards(ToolAccordionHooks hooks, GridContainer clipGrid, GridContainer pathGrid)
	{
		SelectAreaButton = CreateToolCard(hooks, clipGrid, "BtnSelectArea", UnicodeIcons.EXPAND_ARROWS, "Select Area", () => hooks.SelectTool(EditorTool.SelectArea, SelectAreaButton), "Select rectangular area");

		PathingBrushButton = CreateToolCard(hooks, pathGrid, "BtnPathingBrush", UnicodeIcons.BRUSH_ALT, "Brush", () => hooks.SelectTool(EditorTool.PaintPathing, PathingBrushButton), "Paint pathing attributes onto the terrain map");

		FloodFillPathingButton = CreateToolCard(hooks, pathGrid, "BtnFloodFillPathing", UnicodeIcons.FILL_DRIP, "Flood Fill", () => hooks.SelectTool(EditorTool.FloodFillPathing, FloodFillPathingButton), "Flood fill pathing attributes onto the terrain map");
	}

	private void BuildObjectToolCards(ToolAccordionHooks hooks, GridContainer objGrid)
	{
		SelectMoveButton = CreateToolCard(hooks, objGrid, "BtnSelectMove", UnicodeIcons.MOVE, "Select/Move", () => hooks.SelectTool(EditorTool.SelectMove, SelectMoveButton), "Select and move units, props, or decals");

		AddObjectButton = CreateToolCard(hooks, objGrid, "BtnAddObject", UnicodeIcons.CUBE, "Add Object", () => hooks.AddObjectRequested(), "Place units, props, or decals");

		DeleteObjectButton = CreateToolCard(hooks, objGrid, "BtnDeleteObject", UnicodeIcons.TRASH, "Erase", () =>
		{
			if (GodotObject.IsInstanceValid(GameContext.Host?.SelectedEditorObject))
				hooks.DeleteSelectedObjectRequested();
			else
				hooks.SelectTool(EditorTool.DeleteObject, DeleteObjectButton);
		}, "Erase units, props, or decals");
	}

	private void BuildCoordinatePanel(ToolAccordionHooks hooks)
	{
		DrawCoordinateButton = hooks.Owner.GetNode<Button>(ContentPath + "/PanelCoordinatesVBox/BtnDrawCoordinate");
		hooks.SetupButton(DrawCoordinateButton, $"{UnicodeIcons.DRAW_POLYGON} DRAW COORD", () => hooks.SelectTool(EditorTool.DrawCoordinate, DrawCoordinateButton), 11, "Drag to define a named coordinate box exposed as C# variables");

		CoordinateNameInput = hooks.Owner.GetNode<LineEdit>(ContentPath + "/PanelCoordinatesVBox/CoordinateNameRow/TxtCoordinateName");
		CommitCoordinateButton = hooks.Owner.GetNode<Button>(ContentPath + "/PanelCoordinatesVBox/BtnCommitCoordinate");
		hooks.SetupButton(CommitCoordinateButton, "+ ADD", null, 11, "Create named coordinate");
		CommitCoordinateButton.Pressed += () =>
		{
			if (GameContext.EditorService.Tools.PendingCoordinateMinX == GameContext.EditorService.Tools.PendingCoordinateMaxX && GameContext.EditorService.Tools.PendingCoordinateMinZ == GameContext.EditorService.Tools.PendingCoordinateMaxZ)
			{
				hooks.ShowFeedback("Select a valid area first by dragging.");
				return;
			}

			string name = CoordinateNameInput?.Text ?? "";
			if (string.IsNullOrWhiteSpace(name))
			{
				hooks.ShowFeedback("Enter a coordinate name before creating.");
				return;
			}

			bool ok = GameContext.IEditorAPI?.CommitCoordinate(name, GameContext.EditorService.Tools.PendingCoordinateMinX, GameContext.EditorService.Tools.PendingCoordinateMinZ, GameContext.EditorService.Tools.PendingCoordinateMaxX, GameContext.EditorService.Tools.PendingCoordinateMaxZ) ?? false;
			if (ok)
			{
				hooks.RefreshCoordinateList();
				hooks.ShowFeedback($"Coordinate '{name}' created.");
				CoordinateNameInput?.Text = "";
			}
		};
		ViewCoordinatesButton = hooks.Owner.GetNode<Button>(ContentPath + "/PanelCoordinatesVBox/BtnViewCoordinates");
		hooks.SetupOptionButton(ViewCoordinatesButton, $"{UnicodeIcons.LIST} View Existing Coordinates", () => hooks.OpenInstanceManager("Coordinates"), 11, "Open instance manager to view and locate existing defined coordinates");

		Label spawnAnchorsTitle = hooks.Owner.GetNode<Label>(ContentPath + "/PanelCoordinatesVBox/LblSpawnAnchorTitle");
		spawnAnchorsTitle.Text = TranslationServer.Translate("SPAWN ANCHORS");

		Label spawnAnchorsInstructions = hooks.Owner.GetNode<Label>(ContentPath + "/PanelCoordinatesVBox/LblSpawnAnchorInstructions");
		spawnAnchorsInstructions.Text = TranslationServer.Translate("Seat each player in a coordinate. A match starts that player somewhere inside it.");

		OpenSpawnAnchorsButton = hooks.Owner.GetNode<Button>(ContentPath + "/PanelCoordinatesVBox/BtnOpenSpawnAnchors");
		hooks.SetupButton(OpenSpawnAnchorsButton, $"{UnicodeIcons.PIN} SPAWN ANCHORS", () => hooks.OpenSpawnAnchors(), 11, "Seat players in the coordinates they begin a match inside");
	}

	/// <summary>
	/// Builds one tool card: a flat icon button with its caption stacked underneath, added to the module grid.
	/// The button is returned so the editor can read it back for tool highlighting.
	/// </summary>
	public Button CreateToolCard(ToolAccordionHooks hooks, Control cardParent, string buttonName, string iconGlyph, string labelText, Action onClick, string tooltip = "")
	{
		var btn = new Button();
		btn.Name = buttonName;

		btn.Text = iconGlyph;
		btn.TooltipText = tooltip;
		btn.Flat = false;
		btn.CustomMinimumSize = new Vector2(52, 52);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		btn.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

		var font = hooks.FontAwesomeFont();
		if (font != null)
		{
			btn.AddThemeFontOverride("font", font);
		}

		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateEditorToolButtonPlate(
			new Color(0.16f, 0.15f, 0.13f, 0.90f), new Color(0.42f, 0.36f, 0.26f, 0.85f), 4));
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateEditorToolButtonPlate(
			new Color(0.28f, 0.24f, 0.18f, 0.96f), UIStyle.ColorGold, 4));
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateEditorToolButtonPlate(
			new Color(0.36f, 0.30f, 0.20f, 0.98f), UIStyle.ColorGold, -1));
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		btn.AddThemeFontSizeOverride("font_size", 20);
		btn.AddThemeColorOverride("font_color", new Color(0.95f, 0.90f, 0.82f));
		btn.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);

		if (onClick != null)
		{
			btn.Pressed += onClick;
		}

		var lbl = new Label();
		lbl.Name = "ToolCardLabel";
		lbl.Text = labelText;
		lbl.HorizontalAlignment = HorizontalAlignment.Center;
		lbl.AddThemeFontSizeOverride("font_size", 10);
		lbl.AddThemeColorOverride("font_color", new Color(0.88f, 0.84f, 0.78f));

		var card = new VBoxContainer();
		card.Name = "ToolCard_" + btn.Name;
		card.AddThemeConstantOverride("separation", 3);
		card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		card.Alignment = BoxContainer.AlignmentMode.Center;

		card.AddChild(btn);
		card.AddChild(lbl);
		cardParent.AddChild(card);

		return btn;
	}

	public void StyleContainers(Control? inspectorContent)
	{
		if (TerrainPanel != null)
		{
			MapEditorStyle.SubContainer(TerrainPanel, "Terrain Elevation");
		}

		if (DecoPanel != null)
		{
			MapEditorStyle.SubContainer(DecoPanel, "Texture Actions");
		}

		if (PathingPanel != null)
		{
			MapEditorStyle.SubContainer(PathingPanel, "Pathing Actions");
		}

		if (ObjectsPanel != null)
		{
			MapEditorStyle.SubContainer(ObjectsPanel, "Object Placement");
		}

		if (ClipboardPanel != null)
		{
			MapEditorStyle.SubContainer(ClipboardPanel, "Clipboard Actions");
		}

		var targetInspector = MapEditorStyle.ContentTarget(inspectorContent);
		if (targetInspector != null)
		{
			MapEditorStyle.SubContainer(targetInspector.GetNodeOrNull<Control>("InspBox_VBoxContainer"), "Transform Controls");
		}
	}
}
