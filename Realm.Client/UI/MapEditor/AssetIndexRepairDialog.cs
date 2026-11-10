using System;
using Godot;
using Realm.Client.Services;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// The blocking overlay shown while the asset index is rebuilt from the CAS manifests. It owns the dimmer,
/// the card and the progress controls, and the two states the rebuild ends in, so the editor only has to
/// start the work and dismiss it.
/// </summary>
public partial class AssetIndexRepairDialog : Control
{
	public ProgressBar Progress { get; private set; } = null!;
	public Label StatusLabel { get; private set; } = null!;

	public AssetIndexRepairDialog() { }

	/// <summary>
	/// Builds the overlay. Call before adding it to the tree; the controls exist immediately.
	/// </summary>
	public void Initialize()
	{
		Name = "AssetIndexRepairOverlay";
		SetAnchorsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		ZIndex = 1100;

		var dimmer = new ColorRect();
		dimmer.Name = "Dimmer_ColorRect";
		dimmer.Color = new Color(0, 0, 0, 0.75f);
		dimmer.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(dimmer);

		var center = UIStyle.CreateCenteredOverlay();
		dimmer.AddChild(center);

		var panel = new PanelContainer();
		panel.Name = "Panel_PanelContainer";
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.CustomMinimumSize = new Vector2(520, 220);
		center.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.Name = "Vbox_VBoxContainer";
		vbox.AddThemeConstantOverride("separation", 12);
		panel.AddChild(vbox);

		var titleLabel = new Label();
		titleLabel.Name = "TitleLabel_Label";
		UIStyle.ApplyTitle(titleLabel, "🗄️ " + TranslationServer.Translate("REPAIRING ASSET INDEX"), 20);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(titleLabel);

		var descLabel = new Label();
		descLabel.Name = "DescLabel_Label";
		descLabel.Text = TranslationServer.Translate("Rebuilding asset index database from CAS manifests...");
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		descLabel.AddThemeFontSizeOverride("font_size", 13);
		vbox.AddChild(descLabel);

		Progress = UIStyle.CreateProgressBar(460f, 22f);
		vbox.AddChild(Progress);

		StatusLabel = new Label();
		StatusLabel.Name = "StatusLabel_Label";
		StatusLabel.Text = TranslationServer.Translate("Initializing asset database rebuild...");
		StatusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		StatusLabel.AddThemeFontSizeOverride("font_size", 13);
		StatusLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		vbox.AddChild(StatusLabel);
	}

	/// <summary>
	/// Reports rebuild progress from whichever thread the rebuild runs on, hopping back to the main thread
	/// before touching either control.
	/// </summary>
	public Progress<AssetIndexProgressUpdate> CreateProgressReporter()
	{
		return new Progress<AssetIndexProgressUpdate>(update =>
		{
			Callable.From(() =>
			{
				if (GodotObject.IsInstanceValid(Progress))
				{
					Progress.Value = Mathf.Clamp(update.ProgressPercentage * 100.0, 0, 100);
				}

				if (GodotObject.IsInstanceValid(StatusLabel))
				{
					StatusLabel.Text = update.Message;
				}
			}).CallDeferred();
		});
	}

	/// <summary>Shows the finished state the overlay stays on briefly before it is dismissed.</summary>
	public void ShowCompleted()
	{
		if (GodotObject.IsInstanceValid(Progress))
		{
			Progress.Value = 100;
		}

		if (GodotObject.IsInstanceValid(StatusLabel))
		{
			StatusLabel.Text = TranslationServer.Translate("Asset index repair complete!");
			StatusLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.9f, 0.3f));
		}
	}
}
