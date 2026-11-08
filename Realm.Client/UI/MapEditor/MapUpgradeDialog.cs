using System.IO;
using Godot;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// The build-mismatch modal: the map folder and the two builds it compares, the progress bar and
/// status line the migration reports through, and the upgrade/cancel pair. It owns every node it
/// builds, so the editor only reads the controls it needs instead of constructing the layout inline.
/// </summary>
public partial class MapUpgradeDialog : Control
{
	public ProgressBar Progress { get; private set; } = null!;
	public Label StatusLabel { get; private set; } = null!;
	public Button UpgradeButton { get; private set; } = null!;
	public Button CancelButton { get; private set; } = null!;

	/// <summary>
	/// Builds the dialog for one upgrade attempt. Call before adding it to the tree; the nodes exist
	/// immediately, which is what lets the upgrade flow read the controls straight after construction.
	/// The progress bar and status line start hidden — they appear once the upgrade is confirmed.
	/// </summary>
	public void Initialize(string selectedFolder, string? currentVersion, string targetVersion)
	{
		Name = "MapUpgradeDialog";
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		ZIndex = 1100;

		var body = BuildFrame();

		BuildHeader(body, selectedFolder, currentVersion, targetVersion);

		(Progress, StatusLabel) = BuildProgressSection(body);

		body.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });

		(UpgradeButton, CancelButton) = BuildButtonRow(body);
	}

	private VBoxContainer BuildFrame()
	{
		var panel = new Panel();
		panel.Name = "Popup_Panel";
		panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		AddChild(panel);

		var cardPanel = new Panel();
		cardPanel.Name = "CardPanel_Panel";
		cardPanel.CustomMinimumSize = new Vector2(540, 290);
		cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.AddChild(cardPanel);

		var body = new VBoxContainer();
		body.Name = "Vbox_VBoxContainer";
		body.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		body.CustomMinimumSize = new Vector2(500, 260);
		body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		body.SizeFlagsVertical = SizeFlags.ExpandFill;
		cardPanel.AddChild(body);
		return body;
	}

	private static void BuildHeader(VBoxContainer body, string selectedFolder, string? currentVersion, string targetVersion)
	{
		body.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15) });

		var titleLabel = new Label();
		titleLabel.Name = "TitleLabel_Label";
		UIStyle.ApplyTitle(titleLabel, TranslationServer.Translate("MAP BUILD MISMATCH"), 20);
		titleLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.4f, 0.3f));
		body.AddChild(titleLabel);

		body.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

		var descLabel = new Label();
		descLabel.Name = "DescLabel_Label";
		descLabel.Text = $"{string.Format(TranslationServer.Translate("Map: {0}"), Path.GetFileName(selectedFolder))}\n"
			+ $"{string.Format(TranslationServer.Translate("Map Build: {0} | Editor Build: {1}"), currentVersion, targetVersion)}\n\n"
			+ TranslationServer.Translate("This map format needs to be upgraded before it can be opened. Would you like to upgrade now?");
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.AddThemeFontSizeOverride("font_size", 13);
		descLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		body.AddChild(descLabel);
	}

	private static (ProgressBar Progress, Label Status) BuildProgressSection(VBoxContainer body)
	{
		var progressBar = UIStyle.CreateProgressBar(460f, 20f);
		progressBar.Visible = false;
		body.AddChild(progressBar);

		var statusLabel = new Label();
		statusLabel.Name = "StatusLabel_Label";
		statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		statusLabel.AddThemeFontSizeOverride("font_size", 12);
		statusLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		statusLabel.Visible = false;
		body.AddChild(statusLabel);
		return (progressBar, statusLabel);
	}

	private static (Button Upgrade, Button Cancel) BuildButtonRow(VBoxContainer body)
	{
		var buttonRow = new HBoxContainer();
		buttonRow.Name = "ButtonRow_HBoxContainer";
		buttonRow.Alignment = BoxContainer.AlignmentMode.Center;
		buttonRow.AddThemeConstantOverride("separation", 20);
		body.AddChild(buttonRow);

		var upgradeBtn = UIStyle.CreateActionButton("UpgradeBtn_Button", TranslationServer.Translate("UPGRADE MAP"), 14);
		upgradeBtn.CustomMinimumSize = new Vector2(160, 38);
		buttonRow.AddChild(upgradeBtn);

		var cancelBtn = UIStyle.CreateActionButton("CancelBtn_Button", TranslationServer.Translate("CANCEL"), 14);
		cancelBtn.CustomMinimumSize = new Vector2(120, 38);
		buttonRow.AddChild(cancelBtn);

		return (upgradeBtn, cancelBtn);
	}
}
