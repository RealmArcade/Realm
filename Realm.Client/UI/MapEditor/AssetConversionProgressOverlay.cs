using System;
using Godot;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// The blocking overlay shown while an external asset converts in the background. Conversion runs off the main
/// thread, so the bar cannot report real progress: it creeps forward toward a ceiling and never moves backwards,
/// which is enough to show the editor is working rather than frozen.
/// </summary>
[GlobalClass]
public partial class AssetConversionProgressOverlay : Control
{
	private const double CreepStartPercent = 4.0;
	private const double CreepCeilingPercent = 90.0;
	private const double CreepCatchUpRate = 1.2;
	private const double CreepFloorPercentPerSecond = 0.8;

	private ProgressBar _progressBar = null!;
	private Label _statusLabel = null!;

	public void Initialize(string title, string status)
	{
		Name = "AssetConversionProgressOverlay";
		SetAnchorsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		ZIndex = 1100;

		var dimmer = new ColorRect();
		dimmer.Name = "Dimmer_ColorRect";
		dimmer.Color = new Color(0, 0, 0, 0.72f);
		dimmer.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(dimmer);

		var center = UIStyle.CreateCenteredOverlay();
		dimmer.AddChild(center);

		var panel = new PanelContainer();
		panel.Name = "ConversionPanel_PanelContainer";
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.CustomMinimumSize = new Vector2(480, 0);
		center.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.Name = "Content_VBoxContainer";
		vbox.AddThemeConstantOverride("separation", 12);
		panel.AddChild(vbox);

		var titleLabel = new Label();
		titleLabel.Name = "Title_Label";
		titleLabel.Text = title;
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		titleLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		titleLabel.AddThemeFontSizeOverride("font_size", 16);
		vbox.AddChild(titleLabel);

		_statusLabel = new Label();
		_statusLabel.Name = "Status_Label";
		_statusLabel.Text = status;
		_statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_statusLabel.CustomMinimumSize = new Vector2(420, 0);
		_statusLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_statusLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		_statusLabel.AddThemeFontSizeOverride("font_size", 12);
		vbox.AddChild(_statusLabel);

		_progressBar = UIStyle.CreateProgressBar(420f, 18f);
		UIStyle.ApplyProgressBar(_progressBar);
		_progressBar.Value = CreepStartPercent;
		vbox.AddChild(_progressBar);
	}

	public void SetStatus(string status)
	{
		if (!GodotObject.IsInstanceValid(_statusLabel)) return;
		_statusLabel.Text = status;
	}

	public override void _Process(double delta)
	{
		if (!GodotObject.IsInstanceValid(_progressBar)) return;
		if (_progressBar.Value >= CreepCeilingPercent) return;

		double remaining = CreepCeilingPercent - _progressBar.Value;
		double creep = remaining * Math.Min(1.0, delta * CreepCatchUpRate) + delta * CreepFloorPercentPerSecond;
		_progressBar.Value = Math.Min(CreepCeilingPercent, _progressBar.Value + creep);
	}
}
