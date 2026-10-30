using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Services;

namespace Realm.Client.UI;

[GlobalClass]
public partial class ColorPickerPopover : Control
{
	private const float PanelWidth = 316.0f;
	private const float SwatchSize = 34.0f;
	private const int SwatchColumns = 6;
	private const float SwatchSeparation = 6.0f;

	private ColorRect _dismissOverlay;
	private PanelContainer _panel;
	private GridContainer _swatchGrid;
	private Label _emptyLabel;
	private Action<int>? _onSelected;

	public override void _Ready()
	{
		EnsureBuilt();
	}

	private void EnsureBuilt()
	{
		if (_panel != null) return;

		Name = "ColorPickerPopover";
		Visible = false;
		MouseFilter = MouseFilterEnum.Stop;
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		_dismissOverlay = new ColorRect();
		_dismissOverlay.Name = "DismissOverlay_ColorRect";
		_dismissOverlay.Color = new Color(0.0f, 0.0f, 0.0f, 0.45f);
		_dismissOverlay.MouseFilter = MouseFilterEnum.Stop;
		_dismissOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_dismissOverlay.GuiInput += OnDismissOverlayInput;
		AddChild(_dismissOverlay);

		_panel = new PanelContainer();
		_panel.Name = "ColorPickerPanel_PanelContainer";
		_panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		AddChild(_panel);

		var vbox = new VBoxContainer();
		vbox.Name = "Content_VBoxContainer";
		vbox.AddThemeConstantOverride("separation", 10);
		_panel.AddChild(vbox);

		var titleLabel = new Label();
		titleLabel.Name = "Title_Label";
		titleLabel.Text = TranslationServer.Translate("CHOOSE YOUR COLOR");
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		titleLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		titleLabel.AddThemeFontSizeOverride("font_size", 16);
		vbox.AddChild(titleLabel);

		_swatchGrid = new GridContainer();
		_swatchGrid.Name = "SwatchGrid_GridContainer";
		_swatchGrid.Columns = SwatchColumns;
		_swatchGrid.AddThemeConstantOverride("h_separation", (int)SwatchSeparation);
		_swatchGrid.AddThemeConstantOverride("v_separation", (int)SwatchSeparation);
		vbox.AddChild(_swatchGrid);

		_emptyLabel = new Label();
		_emptyLabel.Name = "Empty_Label";
		_emptyLabel.Text = TranslationServer.Translate("Every color is already taken.");
		_emptyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_emptyLabel.CustomMinimumSize = new Vector2(PanelWidth - 48, 0);
		_emptyLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_emptyLabel.Visible = false;
		_emptyLabel.AddThemeFontSizeOverride("font_size", 13);
		_emptyLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
		vbox.AddChild(_emptyLabel);
	}

	public void ShowFor(Control anchor, IReadOnlyList<int> availablePaletteIndices, int selectedPaletteIndex, Action<int> onSelected)
	{
		EnsureBuilt();

		_onSelected = onSelected;
		Populate(availablePaletteIndices, selectedPaletteIndex);
		Visible = true;

		Node? parent = GetParent();
		if (parent != null)
		{
			parent.MoveChild(this, parent.GetChildCount() - 1);
		}

		PositionPanelNear(anchor);
	}

	public void Close()
	{
		Visible = false;
		_onSelected = null;
	}

	private void Populate(IReadOnlyList<int> availablePaletteIndices, int selectedPaletteIndex)
	{
		foreach (Node child in _swatchGrid.GetChildren())
		{
			_swatchGrid.RemoveChild(child);
			child.QueueFree();
		}

		_emptyLabel.Visible = availablePaletteIndices.Count == 0;
		_swatchGrid.Visible = availablePaletteIndices.Count > 0;

		for (int index = 0; index < availablePaletteIndices.Count; index++)
		{
			_swatchGrid.AddChild(CreateSwatch(availablePaletteIndices[index], availablePaletteIndices[index] == selectedPaletteIndex));
		}
	}

	private Button CreateSwatch(int paletteIndex, bool isSelected)
	{
		var swatch = new Button();
		swatch.Name = $"ColorSwatch{paletteIndex}_Button";
		swatch.CustomMinimumSize = new Vector2(SwatchSize, SwatchSize);
		swatch.FocusMode = FocusModeEnum.None;

		var style = CreateSwatchStyle(PlayerColorConfig.GetColor(paletteIndex), isSelected);
		swatch.AddThemeStyleboxOverride("normal", style);
		swatch.AddThemeStyleboxOverride("hover", CreateSwatchStyle(PlayerColorConfig.GetColor(paletteIndex), true));
		swatch.AddThemeStyleboxOverride("pressed", CreateSwatchStyle(PlayerColorConfig.GetColor(paletteIndex), true));
		swatch.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		swatch.Pressed += () =>
		{
			_onSelected?.Invoke(paletteIndex);
			Close();
		};
		swatch.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();

		return swatch;
	}

	private static StyleBoxFlat CreateSwatchStyle(Color color, bool highlighted)
	{
		var style = new StyleBoxFlat();
		style.BgColor = color;
		style.BorderColor = highlighted ? UIStyle.ColorGold : new Color(0.35f, 0.35f, 0.4f);
		style.SetBorderWidthAll(highlighted ? 3 : 1);
		style.SetCornerRadiusAll(4);
		return style;
	}

	private void PositionPanelNear(Control? anchor)
	{
		float rows = Mathf.Max(1.0f, Mathf.CeilToInt((float)_swatchGrid.GetChildCount() / SwatchColumns));
		float gridHeight = _swatchGrid.Visible ? rows * (SwatchSize + SwatchSeparation) : 24.0f;
		float panelHeight = gridHeight + 84.0f;
		Vector2 viewportSize = GetViewportRect().Size;

		Vector2 position = anchor != null
			? anchor.GlobalPosition + new Vector2(anchor.Size.X * 0.5f - PanelWidth * 0.5f, anchor.Size.Y + 10.0f)
			: new Vector2(viewportSize.X * 0.5f - PanelWidth * 0.5f, viewportSize.Y * 0.5f - panelHeight * 0.5f);

		position.X = Mathf.Clamp(position.X, 16.0f, Mathf.Max(16.0f, viewportSize.X - PanelWidth - 16.0f));
		position.Y = Mathf.Clamp(position.Y, 16.0f, Mathf.Max(16.0f, viewportSize.Y - panelHeight - 16.0f));

		_panel.Position = position;
		_panel.Size = new Vector2(PanelWidth, panelHeight);
	}

	private void OnDismissOverlayInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton mouseEvent) return;
		if (!mouseEvent.Pressed || mouseEvent.ButtonIndex != MouseButton.Left) return;

		Close();
		GetViewport().SetInputAsHandled();
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (!Visible) return;
		if (!@event.IsActionPressed("ui_cancel")) return;

		Close();
		GetViewport().SetInputAsHandled();
	}
}
