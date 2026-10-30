using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Utils;

namespace Realm.Client.UI;

[GlobalClass]
public partial class AttributionPopover : Control
{
	private const float PanelWidth = 440.0f;
	private const float PanelBaseHeight = 168.0f;
	private const float RowHeight = 30.0f;
	private const float PanelMaximumHeight = 420.0f;

	private ColorRect _dismissOverlay;
	private PanelContainer _panel;
	private Label _titleLabel;
	private Label _subtitleLabel;
	private ScrollContainer _sourceScroll;
	private VBoxContainer _sourceList;
	private Label _emptyLabel;
	private Button _closeButton;
	private bool _built;

	public override void _Ready()
	{
		EnsureBuilt();
	}

	private void EnsureBuilt()
	{
		if (_built) return;
		_built = true;

		Name = "AttributionPopover";
		Visible = false;
		MouseFilter = MouseFilterEnum.Stop;
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		_dismissOverlay = new ColorRect();
		_dismissOverlay.Name = "DismissOverlay_ColorRect";
		_dismissOverlay.Color = new Color(0.0f, 0.0f, 0.0f, 0.55f);
		_dismissOverlay.MouseFilter = MouseFilterEnum.Stop;
		_dismissOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_dismissOverlay.GuiInput += OnDismissOverlayInput;
		AddChild(_dismissOverlay);

		_panel = new PanelContainer();
		_panel.Name = "AttributionPanel_PanelContainer";
		_panel.AddThemeStyleboxOverride("panel", CreatePanelStyle());
		AddChild(_panel);

		var vbox = new VBoxContainer();
		vbox.Name = "Content_VBoxContainer";
		vbox.AddThemeConstantOverride("separation", 8);
		_panel.AddChild(vbox);

		_titleLabel = new Label();
		_titleLabel.Name = "Title_Label";
		_titleLabel.Text = LocalizationManager.Translate("ATTRIBUTION");
		_titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_titleLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_titleLabel.AddThemeFontSizeOverride("font_size", 16);
		vbox.AddChild(_titleLabel);

		_subtitleLabel = new Label();
		_subtitleLabel.Name = "Subtitle_Label";
		_subtitleLabel.Text = LocalizationManager.Translate("This map contains content imported from:");
		_subtitleLabel.AutowrapMode = TextServer.AutowrapMode.Word;
		_subtitleLabel.CustomMinimumSize = new Vector2(PanelWidth - 32, 0);
		_subtitleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_subtitleLabel.AddThemeColorOverride("font_color", new Color(0.78f, 0.8f, 0.86f));
		_subtitleLabel.AddThemeFontSizeOverride("font_size", 11);
		vbox.AddChild(_subtitleLabel);

		_sourceScroll = new ScrollContainer();
		_sourceScroll.Name = "SourceScroll_ScrollContainer";
		_sourceScroll.CustomMinimumSize = new Vector2(0, 80);
		_sourceScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		_sourceScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		vbox.AddChild(_sourceScroll);

		_sourceList = new VBoxContainer();
		_sourceList.Name = "SourceList_VBoxContainer";
		_sourceList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_sourceList.AddThemeConstantOverride("separation", 4);
		_sourceScroll.AddChild(_sourceList);

		_emptyLabel = new Label();
		_emptyLabel.Name = "EmptyLabel_Label";
		_emptyLabel.Text = LocalizationManager.Translate("No imported content has been attributed to this map.");
		_emptyLabel.AutowrapMode = TextServer.AutowrapMode.Word;
		_emptyLabel.CustomMinimumSize = new Vector2(PanelWidth - 32, 0);
		_emptyLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_emptyLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.72f, 0.78f));
		_emptyLabel.AddThemeFontSizeOverride("font_size", 11);
		_emptyLabel.Visible = false;
		vbox.AddChild(_emptyLabel);

		_closeButton = new Button();
		_closeButton.Name = "Close_Button";
		_closeButton.Text = LocalizationManager.Translate("CLOSE");
		_closeButton.Set("icon_max_width", 0);
		_closeButton.CustomMinimumSize = new Vector2(120, 28);
		_closeButton.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		_closeButton.FocusMode = FocusModeEnum.None;
		_closeButton.AddThemeFontSizeOverride("font_size", 11);
		_closeButton.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_closeButton.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_closeButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_closeButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		_closeButton.Pressed += Close;
		vbox.AddChild(_closeButton);
	}

	public void ShowFor(Control anchor, IReadOnlyList<string> sourceMapNames)
	{
		EnsureBuilt();
		Populate(sourceMapNames);
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
	}

	private void Populate(IReadOnlyList<string> sourceMapNames)
	{
		foreach (Node child in _sourceList.GetChildren())
		{
			_sourceList.RemoveChild(child);
			child.QueueFree();
		}

		var distinctSourceMaps = MapAttributionLookup.Clean(sourceMapNames);
		_emptyLabel.Visible = distinctSourceMaps.Count == 0;
		_sourceScroll.Visible = distinctSourceMaps.Count > 0;

		foreach (string sourceMapName in distinctSourceMaps)
		{
			_sourceList.AddChild(CreateSourceRow(sourceMapName));
		}
	}

	private static PanelContainer CreateSourceRow(string sourceMapName)
	{
		var row = new PanelContainer();
		row.Name = "SourceRow_PanelContainer";
		row.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		var rowStyle = new StyleBoxFlat();
		rowStyle.BgColor = new Color(0.04f, 0.05f, 0.07f, 0.75f);
		rowStyle.BorderColor = new Color(0.45f, 0.38f, 0.25f, 0.35f);
		rowStyle.SetBorderWidthAll(1);
		rowStyle.SetCornerRadiusAll(4);
		rowStyle.ContentMarginLeft = 10;
		rowStyle.ContentMarginRight = 10;
		rowStyle.ContentMarginTop = 5;
		rowStyle.ContentMarginBottom = 5;
		row.AddThemeStyleboxOverride("panel", rowStyle);

		var label = new Label();
		label.Name = "SourceMapName_Label";
		label.Text = sourceMapName;
		label.AutowrapMode = TextServer.AutowrapMode.Word;
		label.CustomMinimumSize = new Vector2(PanelWidth - 52, 0);
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		label.AddThemeColorOverride("font_color", new Color(0.92f, 0.94f, 0.98f));
		label.AddThemeFontSizeOverride("font_size", 12);
		row.AddChild(label);

		return row;
	}

	private void PositionPanelNear(Control anchor)
	{
		float itemCount = Mathf.Max(1, _sourceList.GetChildCount());
		float panelHeight = Mathf.Clamp(PanelBaseHeight + itemCount * RowHeight, PanelBaseHeight + RowHeight, PanelMaximumHeight);
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

	private static StyleBoxFlat CreatePanelStyle()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.04f, 0.05f, 0.08f, 0.98f);
		style.BorderColor = UIStyle.ColorGoldDull;
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(8);
		style.ShadowColor = new Color(0.0f, 0.0f, 0.0f, 0.6f);
		style.ShadowSize = 12;
		style.ContentMarginLeft = 16;
		style.ContentMarginRight = 16;
		style.ContentMarginTop = 14;
		style.ContentMarginBottom = 14;
		return style;
	}
}
