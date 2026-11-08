using System;
using Godot;
using Realm.Client.Services;
using static Godot.Control;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// The pathing-mask accordion: the five mask checkboxes with their coloured rows, the bordered mode row, and the
/// add/clear mode dropdown. It builds those controls from the container it is given and wires their changes into the
/// view model, so the editor only reads the checkboxes its mask gathering needs.
/// </summary>
public class MapEditorPathingPanel
{
	private Action<CheckBox> _styleCheckBoxRow = null!;

	public CheckBox ShallowWater { get; private set; } = null!;
	public CheckBox DeepWater { get; private set; } = null!;
	public CheckBox Flying { get; private set; } = null!;
	public CheckBox Ground { get; private set; } = null!;
	public CheckBox Buildable { get; private set; } = null!;
	public OptionButton Mode { get; private set; } = null!;

	/// <summary>
	/// Builds the accordion inside <paramref name="container"/>. The checkbox-row styling comes from the editor,
	/// which is where the shared checkbox styling lives, rather than being reimplemented here.
	/// </summary>
	public void Build(VBoxContainer container, Action<CheckBox> styleCheckBoxRow)
	{
		_styleCheckBoxRow = styleCheckBoxRow;

		var pathingContent = container.GetNode<VBoxContainer>("PathingContent");
		ShallowWater = pathingContent.GetNode<CheckBox>("ChkShallowWater");
		DeepWater = pathingContent.GetNode<CheckBox>("ChkDeepWater");
		Flying = pathingContent.GetNode<CheckBox>("ChkFlying");
		Ground = pathingContent.GetNode<CheckBox>("ChkGround");
		Buildable = pathingContent.GetNode<CheckBox>("ChkBuildable");

		SetupCheckBoxRow(pathingContent, ShallowWater, new Color(0.2f, 0.6f, 1.0f), "Shallow Water");
		SetupCheckBoxRow(pathingContent, DeepWater, new Color(0.0f, 0.15f, 0.7f), "Deep Water");
		SetupCheckBoxRow(pathingContent, Flying, new Color(0.85f, 0.85f, 0.0f), "Flying");
		SetupCheckBoxRow(pathingContent, Ground, new Color(0.2f, 0.85f, 0.2f), "Ground");
		SetupCheckBoxRow(pathingContent, Buildable, new Color(0.6f, 0.2f, 0.8f), "Buildable");
		Ground.ButtonPressed = true;

		WrapModeRow(pathingContent);

		Mode = pathingContent.GetNode<OptionButton>("ModeRowPanel/PathingModeRow/OptPathingMode");
		Mode.AddItem(TranslationServer.Translate("Add Pathing Attribute"), 0);
		Mode.AddItem(TranslationServer.Translate("Clear Pathing Attribute"), 1);
		Mode.Selected = 0;

		WireToggles();
	}

	private void WireToggles()
	{
		ShallowWater.Toggled += val =>
		{
			if (GameContext.MapEditorHud is { } hud) hud._viewModel.ShallowWater = val;
		};
		DeepWater.Toggled += val =>
		{
			if (GameContext.MapEditorHud is { } hud) hud._viewModel.DeepWater = val;
		};
		Flying.Toggled += val =>
		{
			if (GameContext.MapEditorHud is { } hud) hud._viewModel.Flying = val;
		};
		Ground.Toggled += val =>
		{
			if (GameContext.MapEditorHud is { } hud) hud._viewModel.Ground = val;
		};
		Buildable.Toggled += val =>
		{
			if (GameContext.MapEditorHud is { } hud) hud._viewModel.Buildable = val;
		};
		Mode.ItemSelected += idx =>
		{
			if (GameContext.MapEditorHud is { } hud) hud._viewModel.PathingModeIndex = (int)idx;
		};
	}

	/// <summary>
	/// Wraps the mode dropdown in a bordered panel so it reads as one row of the mask list. The row keeps its
	/// position in the list, which is why it is moved rather than appended.
	/// </summary>
	private static void WrapModeRow(VBoxContainer pathingContent)
	{
		var pathingModeRow = pathingContent.GetNodeOrNull<HBoxContainer>("PathingModeRow");
		if (pathingModeRow == null) return;

		var modePanel = new PanelContainer();
		modePanel.Name = "ModeRowPanel";
		modePanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		modePanel.AddThemeStyleboxOverride("panel",
			UIStyle.CreateCardPlate(new Color(0.12f, 0.13f, 0.16f, 0.82f), new Color(0.35f, 0.33f, 0.28f, 0.75f), 1, 4, 8, 8, 4, 4));

		int modeIdx = pathingModeRow.GetIndex();
		pathingContent.RemoveChild(pathingModeRow);
		modePanel.AddChild(pathingModeRow);
		pathingContent.AddChild(modePanel);
		pathingContent.MoveChild(modePanel, modeIdx);
	}

	/// <summary>
	/// Turns one mask checkbox into a full row: the checkbox, a colour chip that toggles it, and the bordered panel
	/// that holds both, all in the checkbox's original position in the list.
	/// </summary>
	private void SetupCheckBoxRow(VBoxContainer parent, CheckBox chk, Color color, string labelText)
	{
		if (chk == null || parent == null) return;

		chk.Text = TranslationServer.Translate(labelText);
		chk.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		UIStyle.ApplyCheckboxStyle(chk);
		_styleCheckBoxRow(chk);

		var rowPanel = new PanelContainer();
		rowPanel.Name = "RowPanel" + chk.Name;
		rowPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		rowPanel.AddThemeStyleboxOverride("panel",
			UIStyle.CreateCardPlate(new Color(0.12f, 0.13f, 0.16f, 0.82f), new Color(0.35f, 0.33f, 0.28f, 0.75f), 1, 4, 8, 8, 3, 3));

		var row = new HBoxContainer();
		row.Name = "Row" + chk.Name;
		row.AddThemeConstantOverride("separation", 8);
		row.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		int originalIndex = chk.GetIndex();
		parent.RemoveChild(chk);
		row.AddChild(chk);

		var colorBox = new Panel();
		colorBox.Name = "ColorBox" + chk.Name;
		colorBox.CustomMinimumSize = new Vector2(18, 18);
		colorBox.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		colorBox.TooltipText = TranslationServer.Translate(labelText);

		colorBox.AddThemeStyleboxOverride("panel",
			UIStyle.CreateButtonPlate(color, new Color(0.95f, 0.95f, 1.0f, 0.85f), 3));

		colorBox.GuiInput += (@event) =>
		{
			if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
			{
				chk.ButtonPressed = !chk.ButtonPressed;
			}
		};

		row.AddChild(colorBox);
		rowPanel.AddChild(row);
		parent.AddChild(rowPanel);
		parent.MoveChild(rowPanel, originalIndex);
	}

	public void Update(MapEditorHUDViewModel viewModel)
	{
		if (viewModel == null) return;
		UpdateCheckBox(ShallowWater, viewModel.ShallowWater);
		UpdateCheckBox(DeepWater, viewModel.DeepWater);
		UpdateCheckBox(Flying, viewModel.Flying);
		UpdateCheckBox(Ground, viewModel.Ground);
		UpdateCheckBox(Buildable, viewModel.Buildable);
		UpdatePathingMode(viewModel.PathingModeIndex);
	}

	private void UpdateCheckBox(CheckBox checkBox, bool value)
	{
		if (checkBox == null || checkBox.ButtonPressed == value) return;
		checkBox.ButtonPressed = value;
	}

	private void UpdatePathingMode(int modeIndex)
	{
		if (Mode == null || Mode.Selected == modeIndex || modeIndex >= Mode.ItemCount) return;
		Mode.Selected = modeIndex;
	}
}
