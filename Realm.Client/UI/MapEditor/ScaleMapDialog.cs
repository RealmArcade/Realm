using System;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;
using static Godot.Control;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the scale dialog needs from the editor: the node it parents itself to, and the two button stylings the editor
/// uses everywhere else. The members keep those names because the localization generator only extracts keys from call
/// sites that name a registered translating wrapper.
/// </summary>
public sealed record ScaleDialogHooks(
	Node Owner,
	Action<Button, string, Action, int, string> SetupButton,
	Action<Button, string, Action, int, string> SetupOptionButton);

/// <summary>
/// The scale-map modal: the width/depth steppers, the live target labels, and the apply/cancel pair. It owns the
/// target depth the steppers adjust and the silhouette preview the host draws, so the editor only opens and closes it.
/// </summary>
public sealed class ScaleMapDialog
{
	private readonly ScaleDialogHooks _hooks;

	private PanelContainer? _panel;
	private Label? _widthLabel;
	private Label? _heightLabel;
	private int _targetDepth;

	public ScaleMapDialog(ScaleDialogHooks hooks)
	{
		_hooks = hooks;
	}

	public PanelContainer? Panel => _panel;

	/// <summary>
	/// Sets the targets the dialog edits. The width lives on the editor service because the map settings dialog and the
	/// silhouette both read it there; the depth is this dialog's own because nothing else adjusts it.
	/// </summary>
	public void SetTargets(int width, int depth)
	{
		GameContext.EditorService.Tools.ScaleDialogTargetWidth = width;
		_targetDepth = depth;
	}

	public void Open()
	{
		if (_panel != null) return;

		GameContext.Host?.ShowScaleMapSilhouette(GameContext.EditorService.Tools.ScaleDialogTargetWidth, _targetDepth);

		_panel = new PanelContainer();
		_panel.Name = "ScaleMapDialog";
		_panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel());
		_panel.SetAnchorsPreset(LayoutPreset.Center);
		_panel.CustomMinimumSize = new Vector2(320, 0);
		_panel.GrowHorizontal = GrowDirection.Both;
		_panel.GrowVertical = GrowDirection.Both;
		_panel.ZIndex = 1000;
		_hooks.Owner.AddChild(_panel);
		_panel.MoveToFront();

		var margin = new MarginContainer();
		margin.Name = "Margin_MarginContainer";
		margin.AddThemeConstantOverride("margin_left", 16);
		margin.AddThemeConstantOverride("margin_right", 16);
		margin.AddThemeConstantOverride("margin_top", 14);
		margin.AddThemeConstantOverride("margin_bottom", 14);
		_panel.AddChild(margin);

		var innerVBox = new VBoxContainer();
		innerVBox.Name = "InnerVBox_VBoxContainer";
		innerVBox.AddThemeConstantOverride("separation", 12);
		margin.AddChild(innerVBox);

		var titleLabel = new Label();
		titleLabel.Name = "TitleLabel_Label";
		titleLabel.Text = TranslationServer.Translate("⚖ Scale Map");
		titleLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		titleLabel.AddThemeFontSizeOverride("font_size", 14);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		innerVBox.AddChild(titleLabel);

		var hintLabel = new Label();
		hintLabel.Name = "HintLabel_Label";
		hintLabel.Text = TranslationServer.Translate("Sets the new size. All terrain and entity\npositions will be scaled proportionally.");
		hintLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.75f));
		hintLabel.AddThemeFontSizeOverride("font_size", 10);
		hintLabel.HorizontalAlignment = HorizontalAlignment.Center;
		innerVBox.AddChild(hintLabel);

		innerVBox.AddChild(BuildSizeGrid());
		RefreshLabels();

		var separator = new HSeparator();
		separator.Name = "Separator_HSeparator";
		innerVBox.AddChild(separator);

		innerVBox.AddChild(BuildButtonRow());
	}

	public void Close()
	{
		if (_panel != null && GodotObject.IsInstanceValid(_panel))
		{
			_panel.QueueFree();
			_panel = null;
		}

		_widthLabel = null;
		_heightLabel = null;
	}

	public void RefreshLabels()
	{
		if (_widthLabel != null)
			_widthLabel.Text = $"W: {GameContext.EditorService.Tools.ScaleDialogTargetWidth}";
		if (_heightLabel != null)
			_heightLabel.Text = $"H: {_targetDepth}";
	}

	private void DecreaseWidth()
	{
		if (GameContext.EditorService.DecreaseScaleDialogWidth())
		{
			RefreshLabels();
			GameContext.Host?.ShowScaleMapSilhouette(GameContext.EditorService.Tools.ScaleDialogTargetWidth, _targetDepth);
		}
	}

	private void IncreaseWidth()
	{
		if (GameContext.EditorService.IncreaseScaleDialogWidth())
		{
			RefreshLabels();
			GameContext.Host?.ShowScaleMapSilhouette(GameContext.EditorService.Tools.ScaleDialogTargetWidth, _targetDepth);
		}
	}

	private void DecreaseDepth()
	{
		int newDepth = GameContext.EditorService.DecreaseScaleDialogDepth(_targetDepth);
		if (newDepth != _targetDepth)
		{
			_targetDepth = newDepth;
			RefreshLabels();
			GameContext.Host?.ShowScaleMapSilhouette(GameContext.EditorService.Tools.ScaleDialogTargetWidth, _targetDepth);
		}
	}

	private void IncreaseDepth()
	{
		int newDepth = GameContext.EditorService.IncreaseScaleDialogDepth(_targetDepth);
		if (newDepth != _targetDepth)
		{
			_targetDepth = newDepth;
			RefreshLabels();
			GameContext.Host?.ShowScaleMapSilhouette(GameContext.EditorService.Tools.ScaleDialogTargetWidth, _targetDepth);
		}
	}

	/// <summary>Width row above depth row: a live label on the left and the two steppers on the right.</summary>
	private GridContainer BuildSizeGrid()
	{
		var sizeGrid = new GridContainer();
		sizeGrid.Name = "SizeGrid_GridContainer";
		sizeGrid.Columns = 3;
		sizeGrid.AddThemeConstantOverride("h_separation", 8);
		sizeGrid.AddThemeConstantOverride("v_separation", 6);

		_widthLabel = new Label();
		_widthLabel.Name = "LblScalePreviewWidth_Label";
		_widthLabel.AddThemeFontSizeOverride("font_size", 11);
		_widthLabel.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_widthLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		sizeGrid.AddChild(_widthLabel);

		var btnW_Dec = new Button();
		btnW_Dec.Name = "BtnW_Dec_Button";
		btnW_Dec.Set("icon_max_width", 0);
		_hooks.SetupOptionButton(btnW_Dec, UnicodeIcons.MINUS, DecreaseWidth, 10, "Decrease target width");
		sizeGrid.AddChild(btnW_Dec);

		var btnW_Inc = new Button();
		btnW_Inc.Name = "BtnW_Inc_Button";
		btnW_Inc.Set("icon_max_width", 0);
		_hooks.SetupOptionButton(btnW_Inc, UnicodeIcons.PLUS, IncreaseWidth, 10, "Increase target width");
		sizeGrid.AddChild(btnW_Inc);

		_heightLabel = new Label();
		_heightLabel.Name = "LblScalePreviewHeight_Label";
		_heightLabel.AddThemeFontSizeOverride("font_size", 11);
		_heightLabel.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_heightLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		sizeGrid.AddChild(_heightLabel);

		var btnH_Dec = new Button();
		btnH_Dec.Name = "BtnH_Dec_Button";
		btnH_Dec.Set("icon_max_width", 0);
		_hooks.SetupOptionButton(btnH_Dec, UnicodeIcons.MINUS, DecreaseDepth, 10, "Decrease target depth");
		sizeGrid.AddChild(btnH_Dec);

		var btnH_Inc = new Button();
		btnH_Inc.Name = "BtnH_Inc_Button";
		btnH_Inc.Set("icon_max_width", 0);
		_hooks.SetupOptionButton(btnH_Inc, UnicodeIcons.PLUS, IncreaseDepth, 10, "Increase target depth");
		sizeGrid.AddChild(btnH_Inc);
		return sizeGrid;
	}

	private HBoxContainer BuildButtonRow()
	{
		var buttonRow = new HBoxContainer();
		buttonRow.Name = "ButtonRow_HBoxContainer";
		buttonRow.AddThemeConstantOverride("separation", 8);

		var btnCancel = UIStyle.CreateBareButton("BtnCancel_Button");
		btnCancel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_hooks.SetupButton(btnCancel, "✖ CANCEL", () =>
		{
			GameContext.Host?.HideScaleMapSilhouette();
			Close();
		}, 11, "Cancel and discard the scale operation");
		buttonRow.AddChild(btnCancel);

		var btnApply = new Button();
		btnApply.Name = "BtnApply_Button";
		btnApply.Set("icon_max_width", 0);
		btnApply.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_hooks.SetupButton(btnApply, "✔ APPLY", () =>
		{
			GameContext.Host?.HideScaleMapSilhouette();
			GameContext.Host?.ScaleMapExternal(GameContext.EditorService.Tools.ScaleDialogTargetWidth, _targetDepth);
			Close();
		}, 11, "Apply scale and stretch the entire map");
		buttonRow.AddChild(btnApply);
		return buttonRow;
	}
}
