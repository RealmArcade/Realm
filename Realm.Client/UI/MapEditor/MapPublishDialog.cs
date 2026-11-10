using Godot;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// The publish modal: the card, the map name and version it announces, the test-version switch, the
/// maintainer-key note, the progress bar with its status line, and the close button the publish flow
/// reveals when it stops on an error. It owns every node it builds, so the editor only reads the
/// controls it needs instead of constructing the dialog inline.
/// </summary>
public partial class MapPublishDialog : Control
{
	public CheckBox TestVersionCheck { get; private set; } = null!;
	public ProgressBar Progress { get; private set; } = null!;
	public Label StatusLabel { get; private set; } = null!;
	public Button CloseButton { get; private set; } = null!;

	public MapPublishDialog() { }

	/// <summary>
	/// Builds the dialog for one publish attempt. Call before adding it to the tree; the nodes exist
	/// immediately, which is what lets the publish flow read the controls straight after construction.
	/// </summary>
	public void Initialize(string mapTitle, string mapVersion)
	{
		Name = "MapPublishDialog";
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		ZIndex = 1100;

		var body = BuildFrame();

		BuildHeader(body, mapTitle, mapVersion);

		TestVersionCheck = BuildTestVersionRow(body);

		BuildMaintainerInfo(body);

		body.AddChild(new Control { CustomMinimumSize = new Vector2(0, 5) });

		(Progress, StatusLabel) = BuildProgressSection(body);

		body.AddChild(new Control { CustomMinimumSize = new Vector2(0, 5) });

		CloseButton = BuildCloseButton(body);
	}

	private VBoxContainer BuildFrame()
	{
		var (_, body) = UIStyle.CreateModalCard(this, new Vector2(580, 280), new Vector2(540, 240));
		body.AddThemeConstantOverride("separation", 10);
		return body;
	}

	private static void BuildHeader(VBoxContainer body, string mapTitle, string mapVersion)
	{
		body.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

		var titleLabel = new Label();
		titleLabel.Name = "TitleLabel_Label";
		UIStyle.ApplyTitle(titleLabel, "🚀 " + TranslationServer.Translate("PUBLISHING MAP PACKAGE"), 20);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		body.AddChild(titleLabel);

		var descLabel = new Label();
		descLabel.Name = "DescLabel_Label";
		descLabel.Text = string.Format(TranslationServer.Translate("Map: {0} (v{1})"), mapTitle, mapVersion);
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.AddThemeFontSizeOverride("font_size", 13);
		descLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		body.AddChild(descLabel);
	}

	private static CheckBox BuildTestVersionRow(VBoxContainer body)
	{
		var publishOptionsRow = new HBoxContainer();
		publishOptionsRow.Name = "PublishOptions_HBoxContainer";
		publishOptionsRow.AddThemeConstantOverride("separation", 12);
		publishOptionsRow.Alignment = BoxContainer.AlignmentMode.Center;

		var testVersionCheck = new CheckBox
		{
			Name = "TestVersionCheck_CheckBox",
			Text = TranslationServer.Translate("Publish as test version"),
			TooltipText = TranslationServer.Translate("Test versions are listed separately and never replace the official version players download.")
		};
		testVersionCheck.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		publishOptionsRow.AddChild(testVersionCheck);

		body.AddChild(publishOptionsRow);
		return testVersionCheck;
	}

	private static void BuildMaintainerInfo(VBoxContainer body)
	{
		var maintainerInfoLabel = new Label
		{
			Name = "MaintainerInfo_Label",
			Text = TranslationServer.Translate("Maintainer keys: every listed key can publish this map. Keys are added or revoked by an existing key holder through the registry maintainer endpoints; revocations take effect immediately."),
			AutowrapMode = TextServer.AutowrapMode.Word,
			CustomMinimumSize = new Vector2(520, 0),
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		maintainerInfoLabel.AddThemeFontSizeOverride("font_size", 11);
		maintainerInfoLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		body.AddChild(maintainerInfoLabel);
	}

	private static (ProgressBar Progress, Label Status) BuildProgressSection(VBoxContainer body)
	{
		var progressBar = UIStyle.CreateProgressBar(480f, 24f);
		body.AddChild(progressBar);

		var statusLabel = new Label();
		statusLabel.Name = "StatusLabel_Label";
		statusLabel.Text = TranslationServer.Translate("Checking publish eligibility...");
		statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		statusLabel.AddThemeFontSizeOverride("font_size", 13);
		statusLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		statusLabel.CustomMinimumSize = new Vector2(520, 0);
		statusLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		body.AddChild(statusLabel);
		return (progressBar, statusLabel);
	}

	private static Button BuildCloseButton(VBoxContainer body)
	{
		var buttonRow = new HBoxContainer();
		buttonRow.Name = "ButtonRow_HBoxContainer";
		buttonRow.Alignment = BoxContainer.AlignmentMode.Center;
		body.AddChild(buttonRow);

		var closeBtn = new Button();
		closeBtn.Name = "CloseBtn_Button";
		closeBtn.Flat = false;
		closeBtn.AddThemeConstantOverride("icon_max_width", 0);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(closeBtn);
		UIStyle.ApplyButtonText(closeBtn, TranslationServer.Translate("CLOSE"), 14);
		closeBtn.CustomMinimumSize = new Vector2(130, 36);
		closeBtn.Visible = false;
		buttonRow.AddChild(closeBtn);
		return closeBtn;
	}
}
