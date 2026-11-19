using System;
using Godot;
using Realm.Client.Services;
using Steamworks;

namespace Realm.Client.UI.MapEditor;

/// <summary>
///     What the publish dialog needs to know before the author decides how their map goes out: how many different people
///     have played it, how many it asks for, whether it has already been promoted, and how a tester's code is checked.
/// </summary>
/// <param name="Testers">How many distinct Steam identities have answered for this map.</param>
/// <param name="Threshold">How many different people this map asks for before it may be published.</param>
/// <param name="NotReady">How many testers asked for more work, shown to the author alone.</param>
/// <param name="Greenlit">Whether this map has been played by enough different people to be published at all.</param>
/// <param name="FirstPublish">Whether this publish creates the item rather than updating one.</param>
/// <param name="DefaultDestination">Where this publish should go unless the author changes it.</param>
/// <param name="AddTesterCode">Checks one pasted code and appends it to the ledger, reporting what happened.</param>
public sealed record PublishAccessModel(
	int Testers,
	int Threshold,
	int NotReady,
	bool Greenlit,
	bool FirstPublish,
	MapPublishDialog.PublishDestination DefaultDestination,
	Func<string, (bool Accepted, string Message)> AddTesterCode);

/// <summary>
///     The publish modal: where the map goes and who can see it, how far its playtest has come, the field a tester's code
///     is pasted into, and the progress bar that takes over once the upload starts.
///     <para>
///         Nothing is uploaded until enough different people have played the map, so before it qualifies this dialog is the
///         playtest's scoreboard: it shows the count, accepts tester codes and offers no publication at all. Once the map
///         qualifies the author chooses the visibility, and that is when the item is created - which is also when the map's
///         Workshop name is claimed, so an unfinished map never holds a name.
///     </para>
/// </summary>
public partial class MapPublishDialog : Control
{
	/// <summary>The destination the author picked, in the order the visibility list offers them.</summary>
	public enum PublishDestination
	{
		Unlisted = 0,
		FriendsOnly = 1,
		Private = 2,
		Public = 3
	}

	public ProgressBar Progress { get; private set; } = null!;
	public Label StatusLabel { get; private set; } = null!;
	public Button CloseButton { get; private set; } = null!;
	public Button PublishButton { get; private set; } = null!;
	public OptionButton VisibilitySelect { get; private set; } = null!;
	public Label GreenlightProgressLabel { get; private set; } = null!;
	public Label TesterCodeMessageLabel { get; private set; } = null!;
	public LineEdit TesterCodeInput { get; private set; } = null!;

	private PublishAccessModel _access = new(0, 0, 0, false, true, PublishDestination.Public, _ => (false, string.Empty));
	private VBoxContainer _choiceSection = null!;

	/// <summary>Raised when the author has chosen a destination and asked for the upload to start.</summary>
	public event Action? PublishRequested;

	/// <summary>Raised when the author gives up instead of publishing.</summary>
	public event Action? Cancelled;

	public MapPublishDialog() { }

	/// <summary>The destination this publish was asked for.</summary>
	public PublishDestination Destination => (PublishDestination)Math.Clamp(VisibilitySelect.Selected, 0, 3);

	/// <summary>The Steam visibility this publish is made with.</summary>
	/// <returns>The visibility the chosen destination means.</returns>
	public ERemoteStoragePublishedFileVisibility GetSteamVisibility() => Destination switch
	{
		PublishDestination.Public => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic,
		PublishDestination.FriendsOnly => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityFriendsOnly,
		PublishDestination.Private => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate,
		_ => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityUnlisted
	};

	/// <summary>
	///     Builds the dialog for one publish attempt. Call before adding it to the tree; the nodes exist immediately, which is
	///     what lets the publish flow read the controls straight after construction.
	/// </summary>
	/// <param name="mapTitle">The map's title.</param>
	/// <param name="mapVersion">The version being published.</param>
	/// <param name="access">What the ledger says about this map's playtest, and how a code is checked.</param>
	public void Initialize(string mapTitle, string mapVersion, PublishAccessModel access)
	{
		_access = access;
		Name = "MapPublishDialog";
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		ZIndex = 1100;

		var body = BuildFrame();

		BuildHeader(body, mapTitle, mapVersion);

		BuildMaintainerInfo(body);

		_choiceSection = new VBoxContainer { Name = "ChoiceSection" };
		_choiceSection.AddThemeConstantOverride("separation", 8);
		body.AddChild(_choiceSection);

		BuildDestinationSection(_choiceSection);
		BuildPlaytestSection(_choiceSection);
		BuildTesterCodeSection(_choiceSection);

		BuildCreatorGuidance(body);

		body.AddChild(new Control { CustomMinimumSize = new Vector2(0, 5) });

		(Progress, StatusLabel) = BuildProgressSection(body);
		Progress.Visible = false;

		body.AddChild(new Control { CustomMinimumSize = new Vector2(0, 5) });

		BuildActionRow(body);
	}

	/// <summary>
	///     Takes the choices away once the upload has started, so a second press cannot start a second publish, and reveals
	///     the progress bar that reports it.
	/// </summary>
	public void BeginUpload()
	{
		_choiceSection.Visible = false;
		PublishButton.Disabled = true;
		CloseButton.Text = TranslationServer.Translate("CLOSE");
		Progress.Visible = true;
	}

	private VBoxContainer BuildFrame()
	{
		var (_, body) = UIStyle.CreateModalCard(this, new Vector2(620, 560), new Vector2(580, 480));
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

	private static void BuildMaintainerInfo(VBoxContainer body)
	{
		var workshopInfoLabel = new Label
		{
			Name = "WorkshopInfo_Label",
			Text = TranslationServer.Translate("This map is published to the Steam Workshop under your account, so Steam decides who can see it and players subscribe to it there."),
			AutowrapMode = TextServer.AutowrapMode.Word,
			CustomMinimumSize = new Vector2(560, 0),
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		workshopInfoLabel.AddThemeFontSizeOverride("font_size", 11);
		workshopInfoLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		body.AddChild(workshopInfoLabel);
	}

	private void BuildDestinationSection(VBoxContainer body)
	{
		var row = new HBoxContainer { Name = "DestinationRow", Visible = _access.Greenlit };
		row.AddThemeConstantOverride("separation", 12);
		row.Alignment = BoxContainer.AlignmentMode.Center;
		body.AddChild(row);

		var label = new Label { Name = "DestinationLabel_Label", Text = TranslationServer.Translate("WHO CAN SEE IT:") };
		label.VerticalAlignment = VerticalAlignment.Center;
		row.AddChild(label);

		VisibilitySelect = new OptionButton { Name = "VisibilitySelect_OptionButton", CustomMinimumSize = new Vector2(220, 40) };
		VisibilitySelect.AddItem(TranslationServer.Translate("Unlisted - only people with the link"));
		VisibilitySelect.AddItem(TranslationServer.Translate("Friends only"));
		VisibilitySelect.AddItem(TranslationServer.Translate("Private - only you"));
		VisibilitySelect.AddItem(TranslationServer.Translate("Public - listed in the Workshop"));
		VisibilitySelect.Selected = (int)_access.DefaultDestination;
		VisibilitySelect.ItemSelected += _ => GameContext.UserInterface?.PlayClickSound();
		row.AddChild(VisibilitySelect);

		var explanation = new Label
		{
			Name = "DestinationExplanation_Label",
			Text = DescribeDestination(),
			AutowrapMode = TextServer.AutowrapMode.Word,
			CustomMinimumSize = new Vector2(560, 0),
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		explanation.AddThemeFontSizeOverride("font_size", 11);
		explanation.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		body.AddChild(explanation);
	}

	private string DescribeDestination()
	{
		if (!_access.Greenlit)
		{
			return string.Format(
				TranslationServer.Translate("Nothing is on the Workshop yet. Publishing unlocks once {0} different people have played this map, and until then you can only share it directly: host a match for testers, or send them the .rmap."),
				_access.Threshold);
		}

		return _access.FirstPublish
			? TranslationServer.Translate("Publishing creates your Workshop item now. Public lists it in the Workshop; every other choice keeps it off the catalogue.")
			: TranslationServer.Translate("An update keeps the visibility you choose here.");
	}

	private void BuildPlaytestSection(VBoxContainer body)
	{
		GreenlightProgressLabel = new Label
		{
			Name = "GreenlightProgress_Label",
			Text = DescribeProgress(),
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(560, 0),
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		GreenlightProgressLabel.AddThemeFontSizeOverride("font_size", 12);
		GreenlightProgressLabel.AddThemeColorOverride("font_color", _access.Greenlit ? UIStyle.ColorCyanGlow : UIStyle.ColorGold);
		body.AddChild(GreenlightProgressLabel);
	}

	private string DescribeProgress()
	{
		if (_access.Greenlit)
		{
			return _access.Testers > 0
				? string.Format(TranslationServer.Translate("Playtested by {0} people."), _access.Testers)
				: TranslationServer.Translate("Playtested.");
		}

		string progress = string.Format(TranslationServer.Translate("{0} of {1} testers have played this."), _access.Testers, _access.Threshold);
		if (_access.NotReady <= 0) return progress;

		return progress + " " + string.Format(TranslationServer.Translate("{0} of them asked for more work."), _access.NotReady);
	}

	private void BuildTesterCodeSection(VBoxContainer body)
	{
		var row = new HBoxContainer { Name = "TesterCodeRow" };
		row.AddThemeConstantOverride("separation", 12);
		row.Alignment = BoxContainer.AlignmentMode.Center;
		body.AddChild(row);

		TesterCodeInput = new LineEdit
		{
			Name = "TesterCodeInput_LineEdit",
			CustomMinimumSize = new Vector2(360, 38),
			PlaceholderText = TranslationServer.Translate("Add a tester's code")
		};
		TesterCodeInput.AddThemeStyleboxOverride("normal", UIStyle.CreateTextInput(false));
		TesterCodeInput.AddThemeStyleboxOverride("focus", UIStyle.CreateTextInput(true));
		row.AddChild(TesterCodeInput);

		var addButton = new Button { Name = "AddTesterCodeButton", Text = TranslationServer.Translate("ADD"), CustomMinimumSize = new Vector2(90, 38) };
		addButton.Set("icon_max_width", 0);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(addButton);
		addButton.Pressed += OnAddTesterCodePressed;
		row.AddChild(addButton);

		TesterCodeMessageLabel = new Label
		{
			Name = "TesterCodeMessage_Label",
			Text = TranslationServer.Translate("After a match, a tester gets a code to copy from their screen. Paste it here and it counts once."),
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(560, 0),
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		TesterCodeMessageLabel.AddThemeFontSizeOverride("font_size", 11);
		TesterCodeMessageLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		body.AddChild(TesterCodeMessageLabel);
	}

	private void OnAddTesterCodePressed()
	{
		string code = (TesterCodeInput.Text ?? string.Empty).Trim();
		if (code.Length == 0) return;

		(bool accepted, string message) = _access.AddTesterCode(code);
		TesterCodeMessageLabel.Text = message;
		TesterCodeMessageLabel.AddThemeColorOverride("font_color", accepted ? UIStyle.ColorCyanGlow : UIStyle.ColorGoldDull);
		if (accepted) TesterCodeInput.Text = string.Empty;

		GameContext.UserInterface?.PlayClickSound();
	}

	/// <summary>
	///     Tells the creator, once, what belongs on Steam and what belongs on a community server instead. It links Valve's
	///     rules rather than quoting a prohibition whose wording it would have to invent, and it offers no support address:
	///     the answer for a creator who wants to take money for their work is their own Discord, not a Realm inbox.
	/// </summary>
	/// <param name="body">The card the guidance is added to.</param>
	private static void BuildCreatorGuidance(VBoxContainer body)
	{
		var guidanceLabel = new Label
		{
			Name = "CreatorGuidance_Label",
			Text = TranslationServer.Translate("Steam bans maps that post links to external payment systems (Patreon etc.). Put your donation links on your Discord server instead."),
			AutowrapMode = TextServer.AutowrapMode.Word,
			CustomMinimumSize = new Vector2(560, 0),
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		guidanceLabel.AddThemeFontSizeOverride("font_size", 11);
		guidanceLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		body.AddChild(guidanceLabel);

		var linkRow = new HBoxContainer();
		linkRow.Name = "CreatorGuidanceLinks_HBoxContainer";
		linkRow.Alignment = BoxContainer.AlignmentMode.Center;
		linkRow.AddThemeConstantOverride("separation", 12);
		body.AddChild(linkRow);

		linkRow.AddChild(CreateExternalLink("SteamOnlineConduct_Button", TranslationServer.Translate("Steam Online Conduct"), "https://store.steampowered.com/online_conduct"));
		linkRow.AddChild(CreateExternalLink("SteamWorkshopLegal_Button", TranslationServer.Translate("Workshop Legal Agreement"), "https://steamcommunity.com/workshop/workshoplegalagreement"));
		linkRow.AddChild(CreateExternalLink("EditSteamProfile_Button", TranslationServer.Translate("EDIT STEAM PROFILE"), $"https://steamcommunity.com/profiles/{GameContext.Steam.SteamId}/edit"));
	}

	private static Button CreateExternalLink(string name, string text, string url)
	{
		var button = new Button { Name = name, Text = text };
		button.Set("icon_max_width", 0);
		button.FocusMode = FocusModeEnum.None;
		button.AddThemeFontSizeOverride("font_size", 11);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(button);
		button.Pressed += () => OS.ShellOpen(url);
		return button;
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
		statusLabel.CustomMinimumSize = new Vector2(560, 0);
		statusLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		body.AddChild(statusLabel);
		return (progressBar, statusLabel);
	}

	private void BuildActionRow(VBoxContainer body)
	{
		var buttonRow = new HBoxContainer();
		buttonRow.Name = "ButtonRow_HBoxContainer";
		buttonRow.Alignment = BoxContainer.AlignmentMode.Center;
		buttonRow.AddThemeConstantOverride("separation", 14);
		body.AddChild(buttonRow);

		PublishButton = new Button { Name = "PublishButton", CustomMinimumSize = new Vector2(180, 40) };
		PublishButton.Set("icon_max_width", 0);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(PublishButton);
		UIStyle.ApplyButtonText(PublishButton, TranslationServer.Translate("PUBLISH"), 14);
		PublishButton.Pressed += () => PublishRequested?.Invoke();
		PublishButton.Disabled = !_access.Greenlit;
		buttonRow.AddChild(PublishButton);

		CloseButton = new Button();
		CloseButton.Name = "CloseBtn_Button";
		CloseButton.Flat = false;
		CloseButton.AddThemeConstantOverride("icon_max_width", 0);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(CloseButton);
		UIStyle.ApplyButtonText(CloseButton, TranslationServer.Translate("CANCEL"), 14);
		CloseButton.CustomMinimumSize = new Vector2(130, 36);
		CloseButton.Pressed += () => Cancelled?.Invoke();
		buttonRow.AddChild(CloseButton);
	}
}
