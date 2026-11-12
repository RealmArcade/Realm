using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Ecs;
using Realm.EditorAPI;

namespace Realm.Client.UI.MapEditor;

/// <summary>
///     The editor's spawn anchor table: one row per seat, stating which player that row seats and which authored
///     coordinate the player begins a match inside, with a button that moves the camera to that coordinate. A row whose
///     coordinate is unassigned seats nobody, so the board states one mapping per seat and a match picks the point within
///     the mapped coordinate when it starts. Every change commits through <see cref="GameContext.IEditorAPI" />, so the
///     table, the undo history and the markers drawn on the terrain all describe the same anchors.
/// </summary>
[GlobalClass]
public partial class SpawnAnchorDialog : FloatingDialogBase
{
	private const float TopLeftCornerMargin = 20.0f;
	private const int RowCount = EcsWorld.MAX_PLAYERS;
	private static readonly string UnassignedCoordinateText = TranslationServer.Translate("(unassigned)");

	private sealed class AnchorRow
	{
		public OptionButton PlayerOption = null!;
		public OptionButton CoordinateOption = null!;
		public Button LocateButton = null!;
		public int Player;
	}

	private readonly List<AnchorRow> _rows = new();
	private readonly List<string> _coordinateNames = new();
	private bool _suppressCallbacks;

	public SpawnAnchorDialog() { }

	public SpawnAnchorDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Spawn Anchors"), new Vector2(700, 660))
	{
		BuildControls();
		SetFooterCloseOnly();
	}

	private void BuildControls()
	{
		var instructions = new Label();
		instructions.Name = "LblSpawnAnchorInstructions";
		instructions.Text = TranslationServer.Translate("Seat each player in a coordinate. A match starts that player inside the coordinate it maps, so the ground a seat owns is authored here. A row with no coordinate seats nobody, and a player with no row begins anywhere on the board.");
		instructions.AddThemeFontSizeOverride("font_size", 11);
		instructions.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		instructions.CustomMinimumSize = new Vector2(640, 0);
		instructions.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		BodyContainer.AddChild(instructions);

		var scroll = new ScrollContainer();
		scroll.Name = "ScrollSpawnAnchors";
		scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		scroll.CustomMinimumSize = new Vector2(0, 500);
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

		var grid = new GridContainer();
		grid.Name = "GridSpawnAnchors";
		grid.Columns = 3;
		grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		grid.AddThemeConstantOverride("h_separation", 8);
		grid.AddThemeConstantOverride("v_separation", 4);
		scroll.AddChild(grid);
		BodyContainer.AddChild(scroll);

		AddColumnTitle(grid, "LblSpawnAnchorPlayerHeader", TranslationServer.Translate("Player"), 170);
		AddColumnTitle(grid, "LblSpawnAnchorCoordinateHeader", TranslationServer.Translate("Coordinate"), 300);
		AddColumnTitle(grid, "LblSpawnAnchorLocateHeader", TranslationServer.Translate("Locate"), 60);

		for (int rowIndex = 0; rowIndex < RowCount; rowIndex++)
		{
			grid.AddChild(BuildPlayerOption(rowIndex));
			grid.AddChild(BuildCoordinateOption(rowIndex));
			grid.AddChild(BuildLocateButton(rowIndex));
		}
	}

	private static void AddColumnTitle(GridContainer grid, string nodeName, string text, float width)
	{
		var title = new Label();
		title.Name = nodeName;
		title.Text = text;
		title.AddThemeFontSizeOverride("font_size", 11);
		title.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		title.CustomMinimumSize = new Vector2(width, 0);
		grid.AddChild(title);
	}

	private OptionButton BuildPlayerOption(int rowIndex)
	{
		var playerOption = new OptionButton();
		playerOption.Name = $"OptSpawnAnchorPlayer_{rowIndex + 1}";
		playerOption.Set("icon_max_width", 0);
		playerOption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		playerOption.CustomMinimumSize = new Vector2(170, 28);
		playerOption.AddThemeFontSizeOverride("font_size", 10);

		for (int player = 0; player < PlayerColorConfig.Palette.Length; player++)
		{
			playerOption.AddItem(PlayerColorConfig.GetPaletteLabel(player), player);
		}

		playerOption.Selected = rowIndex % PlayerColorConfig.Palette.Length;
		playerOption.ItemSelected += index => OnPlayerSelected(rowIndex, (int)index);

		var row = new AnchorRow { PlayerOption = playerOption, Player = playerOption.Selected };
		_rows.Add(row);

		return playerOption;
	}

	private OptionButton BuildCoordinateOption(int rowIndex)
	{
		var coordinateOption = new OptionButton();
		coordinateOption.Name = $"OptSpawnAnchorCoordinate_{rowIndex + 1}";
		coordinateOption.Set("icon_max_width", 0);
		coordinateOption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		coordinateOption.CustomMinimumSize = new Vector2(300, 28);
		coordinateOption.AddThemeFontSizeOverride("font_size", 10);
		coordinateOption.AddItem(UnassignedCoordinateText, 0);
		coordinateOption.ItemSelected += _ => OnCoordinateSelected(rowIndex);

		_rows[rowIndex].CoordinateOption = coordinateOption;
		return coordinateOption;
	}

	private Button BuildLocateButton(int rowIndex)
	{
		var locateButton = new Button();
		locateButton.Name = $"BtnLocateSpawnAnchor_{rowIndex + 1}";
		locateButton.Set("icon_max_width", 16);
		locateButton.CustomMinimumSize = new Vector2(60, 28);
		locateButton.FocusMode = FocusModeEnum.None;
		locateButton.Icon = UIStyle.GetUnicodeIconTexture(UnicodeIcons.CROSSHAIRS_TARGET, 24);
		locateButton.TooltipText = TranslationServer.Translate("Move the camera to the centre of this row's coordinate");
		locateButton.Pressed += () => LocateCoordinate(rowIndex);

		_rows[rowIndex].LocateButton = locateButton;
		return locateButton;
	}

	private void OnPlayerSelected(int rowIndex, int player)
	{
		if (_suppressCallbacks) return;
		if (rowIndex < 0 || rowIndex >= _rows.Count) return;

		int previousPlayer = _rows[rowIndex].Player;
		_rows[rowIndex].Player = player;

		for (int otherRow = 0; otherRow < _rows.Count; otherRow++)
		{
			if (otherRow == rowIndex || _rows[otherRow].Player != player) continue;

			_rows[otherRow].Player = previousPlayer;
		}

		RefreshRows();
	}

	private void OnCoordinateSelected(int rowIndex)
	{
		if (_suppressCallbacks) return;
		if (rowIndex < 0 || rowIndex >= _rows.Count) return;

		AnchorRow row = _rows[rowIndex];
		string coordinateName = SelectedCoordinateName(row);
		EditorSpawnAnchor? existing = GameContext.EditorService?.FindSpawnAnchor(row.Player);

		if (coordinateName.Length == 0 && existing == null)
		{
			RefreshRows();
			return;
		}

		if (GameContext.IEditorAPI == null)
		{
			Hud?.ShowFeedback(TranslationServer.Translate("No map is open in the editor."));
			return;
		}

		EditorOperationResult result = coordinateName.Length == 0
			? GameContext.IEditorAPI.DeleteSpawnAnchor(row.Player)
			: GameContext.IEditorAPI.CommitSpawnAnchor(row.Player, coordinateName);

		if (!result.Success)
		{
			Hud?.ShowFeedback(result.Error ?? string.Empty);
		}

		RefreshRows();
	}

	private void LocateCoordinate(int rowIndex)
	{
		if (rowIndex < 0 || rowIndex >= _rows.Count) return;

		string coordinateName = SelectedCoordinateName(_rows[rowIndex]);
		if (coordinateName.Length == 0)
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Assign a coordinate to this row before locating it."));
			return;
		}

		GameContext.IEditorAPI?.SelectCoordinate(coordinateName);
	}

	private static string SelectedCoordinateName(AnchorRow row)
	{
		if (row.CoordinateOption.Selected <= 0) return string.Empty;

		return row.CoordinateOption.GetItemText(row.CoordinateOption.Selected);
	}

	/// <summary>
	///     Rewrites every row from the anchors the editor currently holds: the coordinate each row may choose from, the
	///     seat each row states, and whether that row has somewhere to locate. It runs after every change so the table
	///     always reads the same anchors the terrain markers were drawn from.
	/// </summary>
	public void RefreshRows()
	{
		if (_rows.Count == 0) return;

		_suppressCallbacks = true;
		try
		{
			RefreshCoordinateItems();

			List<EditorSpawnAnchor> anchors = GameContext.EditorService?.EditorSpawnAnchors ?? new List<EditorSpawnAnchor>();
			for (int rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
			{
				AnchorRow row = _rows[rowIndex];
				row.PlayerOption.Selected = row.Player;

				string coordinateName = string.Empty;
				for (int anchorIndex = 0; anchorIndex < anchors.Count; anchorIndex++)
				{
					if (anchors[anchorIndex].Player != row.Player) continue;

					coordinateName = anchors[anchorIndex].CoordinateName ?? string.Empty;
					break;
				}

				int coordinateIndex = _coordinateNames.IndexOf(coordinateName);
				row.CoordinateOption.Selected = coordinateIndex < 0 ? 0 : coordinateIndex + 1;
				row.LocateButton.Disabled = coordinateIndex < 0;
			}
		}
		finally
		{
			_suppressCallbacks = false;
		}
	}

	private void RefreshCoordinateItems()
	{
		var coordinateNames = new List<string>();
		var coordinates = GameContext.EditorService?.EditorCoordinates;
		if (coordinates != null)
		{
			for (int index = 0; index < coordinates.Count; index++)
			{
				coordinateNames.Add(coordinates[index].Name);
			}
		}

		coordinateNames.Sort(string.CompareOrdinal);
		bool unchanged = coordinateNames.Count == _coordinateNames.Count;
		for (int index = 0; unchanged && index < coordinateNames.Count; index++)
		{
			unchanged = string.Equals(coordinateNames[index], _coordinateNames[index], System.StringComparison.Ordinal);
		}

		if (unchanged) return;

		_coordinateNames.Clear();
		_coordinateNames.AddRange(coordinateNames);

		for (int rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
		{
			OptionButton coordinateOption = _rows[rowIndex].CoordinateOption;
			coordinateOption.Clear();
			coordinateOption.AddItem(UnassignedCoordinateText, 0);
			for (int index = 0; index < _coordinateNames.Count; index++)
			{
				coordinateOption.AddItem(_coordinateNames[index], index + 1);
			}
		}
	}

	public void RefreshIfOpen()
	{
		if (IsOpen) RefreshRows();
	}

	public override void OpenDialog()
	{
		base.OpenDialog();
		Position = new Vector2(TopLeftCornerMargin, TopLeftCornerMargin);
		RefreshRows();
	}
}
