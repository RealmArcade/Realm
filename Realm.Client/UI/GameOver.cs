using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Shared.Distribution;
using HttpClient = System.Net.Http.HttpClient;
using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Utils;

namespace Realm.Client.UI;

[GlobalClass]
public partial class GameOver : Control
{
	public override void _EnterTree()
	{
		Realm.Client.Services.GameContext.EnsureServices();
	}

	private Panel _bgPanel;
	private Panel _leftPillar;
	private Panel _rightPillar;
	private PanelContainer _mainPanel;
	private PanelContainer _tableFrame;
	private PanelContainer _graphFrame;

	private Label _titleLabel;
	private Button _btnOverview;
	private Button _btnEconomy;
	private Button _btnMilitary;
	private Button _btnClose;
	private Button _btnReportCopyright;
	private Button _btnWriteReview;

	private VBoxContainer _tableRowsContainer;
	private Control _graphContainer;

	private Label _playerColHeader;
	private Label _factionColHeader;
	private Label _scoreColHeader;
	private Label _killsColHeader;
	private Label _resourcesColHeader;

	private const int OverviewTabIndex = 0;
	private const int EconomyTabIndex = 1;
	private const int MilitaryTabIndex = 2;
	private const int FiveStarRatingOptionIndex = 0;
	private const string UnknownAuthorContributorName = "Realm Builder";

	private bool _isVictory = true;
	private int _activeTab = OverviewTabIndex;

	private readonly List<MatchPlayerResult> _results = new();

	private readonly string[] _runes = { "ᚠ", "ᚢ", "ᚦ", "ᚨ", "ᚱ", "ᚲ", "ᚷ", "ᚹ", "ᚺ", "ᚾ", "ᛁ", "ᛃ", "ᛇ", "ᛈ", "ᛉ", "ᛊ", "ᛏ", "ᛒ", "ᛖ", "ᛗ", "ᛚ", "ᛜ", "ᛞ", "ᛟ" };


	public override void _Ready()
	{
		_bgPanel = GetNode<Panel>("Background");
		_leftPillar = GetNode<Panel>("LeftPillar");
		_rightPillar = GetNode<Panel>("RightPillar");
		_mainPanel = GetNode<PanelContainer>("MainPanel");
		_tableFrame = GetNode<PanelContainer>("MainPanel/VBox/TableFrame");
		_graphFrame = GetNode<PanelContainer>("MainPanel/VBox/GraphFrame");


		_titleLabel = GetNode<Label>("TitleLabel");
		_btnOverview = GetNode<Button>("MainPanel/VBox/TabContainer/BtnOverview");
		_btnEconomy = GetNode<Button>("MainPanel/VBox/TabContainer/BtnEconomy");
		_btnMilitary = GetNode<Button>("MainPanel/VBox/TabContainer/BtnMilitary");
		_btnClose = GetNode<Button>("BottomButtons/CloseButton");
		_btnReportCopyright = GetNode<Button>("BottomButtons/BtnReportCopyright");
		_btnWriteReview = GetNode<Button>("BottomButtons/BtnWriteReview");

		_tableRowsContainer = GetNode<VBoxContainer>("MainPanel/VBox/TableFrame/VBox/TableRowsContainer");
		_graphContainer = GetNode<Control>("MainPanel/VBox/GraphFrame/VBox/TimelineGraph");


		_playerColHeader = GetNode<Label>("MainPanel/VBox/TableFrame/VBox/TableHeader/PlayerCol");
		_factionColHeader = GetNode<Label>("MainPanel/VBox/TableFrame/VBox/TableHeader/FactionCol");
		_scoreColHeader = GetNode<Label>("MainPanel/VBox/TableFrame/VBox/TableHeader/ScoreCol");
		_killsColHeader = GetNode<Label>("MainPanel/VBox/TableFrame/VBox/TableHeader/KillsCol");
		_resourcesColHeader = GetNode<Label>("MainPanel/VBox/TableFrame/VBox/TableHeader/ResourcesCol");

		ApplyThemeStyles();
		SetupTabs();
		SetupCloseButton();
		SetupReportCopyrightButton();
		SetupWriteReviewButton();
		SetupWishlistButton();

		UpdateTabDisplay();

		string mapName = GetActiveMapName();
		double playtimeMinutes = 0.0;
		if (GodotObject.IsInstanceValid(GameContext.Lobby) && GameContext.Lobby.GameSessionStartTime != null)
		{
			playtimeMinutes = (DateTime.UtcNow - GameContext.Lobby.GameSessionStartTime.Value).TotalMinutes;
		}

		AddLocalPlaytime(mapName, playtimeMinutes);

		_ = ReportMetricsAsync(mapName, playtimeMinutes, stars: 0, isComplete: true);
	}

	private void ApplyThemeStyles()
	{
		_bgPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateGameOverBg());
		_bgPanel.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		_leftPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(true));
		_leftPillar.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		_rightPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(false));
		_rightPillar.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		_mainPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateGameOverMainPanel());
		_mainPanel.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		_tableFrame.AddThemeStyleboxOverride("panel", UIStyle.CreateBackdropPanel());
		_tableFrame.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		_graphFrame.AddThemeStyleboxOverride("panel", UIStyle.CreateBackdropPanel());
		_graphFrame.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;

		var graphHeader = GetNodeOrNull<Label>("MainPanel/VBox/GraphFrame/VBox/GraphHeaderLabel");
		if (graphHeader != null)
		{
			graphHeader.Text = Tr("TIMELINE PROGRESSION");
			graphHeader.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			graphHeader.AddThemeFontSizeOverride("font_size", 14);
		}

		SetStatus(_isVictory);


		var headers = new[] { _playerColHeader, _factionColHeader, _scoreColHeader, _killsColHeader, _resourcesColHeader };
		foreach (var lbl in headers)
		{
			lbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			lbl.AddThemeFontSizeOverride("font_size", 15);
		}


		PopulateRunicPillar(GetNode<VBoxContainer>("LeftPillar/RuneContainer"));
		PopulateRunicPillar(GetNode<VBoxContainer>("RightPillar/RuneContainer"));
	}

	private void PopulateRunicPillar(VBoxContainer container)
	{
		container.Visible = false;
	}

	public void SetStatus(bool isVictory)
	{
		_isVictory = isVictory;
		if (_titleLabel != null)
		{
			UIStyle.ApplyTitle(_titleLabel, _isVictory ? "VICTORY" : "DEFEAT", 48);
			_titleLabel.AddThemeColorOverride("font_color", _isVictory ? UIStyle.ColorGold : new Color(0.9f, 0.2f, 0.2f));
		}
	}

	private void SetupTabs()
	{
		SetupTabButton(_btnOverview, "OVERVIEW", OverviewTabIndex);
		SetupTabButton(_btnEconomy, "ECONOMY", EconomyTabIndex);
		SetupTabButton(_btnMilitary, "MILITARY", MilitaryTabIndex);
	}

	private void SetupTabButton(Button btn, string text, int index)
	{
		btn.Flat = false;
		UIStyle.ApplyButtonText(btn, text, 16);

		var activeStyle = new StyleBoxFlat();
		activeStyle.BgColor = new Color(0.18f, 0.19f, 0.23f, 0.95f);
		activeStyle.BorderColor = UIStyle.ColorGold;
		activeStyle.SetBorderWidthAll(2);
		activeStyle.CornerRadiusTopLeft = 4;
		activeStyle.CornerRadiusTopRight = 4;

		var normStyle = UIStyle.CreateButtonNormal();
		if (normStyle is StyleBoxFlat flatNorm)
		{
			flatNorm.CornerRadiusBottomLeft = 0;
			flatNorm.CornerRadiusBottomRight = 0;
		}

		var hoverStyle = UIStyle.CreateButtonHover();
		if (hoverStyle is StyleBoxFlat flatHover)
		{
			flatHover.CornerRadiusBottomLeft = 0;
			flatHover.CornerRadiusBottomRight = 0;
		}

		btn.AddThemeStyleboxOverride("normal", normStyle);
		btn.AddThemeStyleboxOverride("hover", hoverStyle);
		btn.AddThemeStyleboxOverride("pressed", activeStyle);
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		btn.Pressed += () =>
		{
			GameContext.UserInterface.PlayClickSound();
			SetTab(index);
		};
		btn.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();
	}

	private void SetTab(int tabIndex)
	{
		if (_activeTab == tabIndex) return;
		_activeTab = tabIndex;
		UpdateTabDisplay();

		if (_graphContainer is TimelineGraph graph)
		{
			graph.SetTab(tabIndex);
		}
	}

	private void SetupCloseButton()
	{
		_btnClose.Flat = false;
		_btnClose.Icon = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/cancel_button_2.webp");
		_btnClose.ExpandIcon = true;

		string closeText = GameContext.SessionState.IsTestMode ? "Back to Editor" : "Close";
		UIStyle.ApplyButtonText(_btnClose, closeText, 18);

		_btnClose.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_btnClose.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_btnClose.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_btnClose.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		_btnClose.Pressed += () =>
		{
			GameContext.UserInterface.PlayClickSound();
			if (GameContext.SessionState.IsTestMode)
			{
				GameContext.SessionState.IsTestMode = false;
				GameContext.SessionState.ReturningFromTest = true;
				if (GameContext.Lobby != null)
				{
					GameContext.Lobby.Disconnect();
				}

				GameContext.UserInterface.TransitionTo(GameScreen.MapEditorHUD);
			}
			else
			{
				GameContext.UserInterface.TransitionTo(GameScreen.MainMenu);
			}
		};
		_btnClose.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();
	}

	private void SetupReportCopyrightButton()
	{
		_btnReportCopyright.Flat = false;
		_btnReportCopyright.Text = Tr("[!] REPORT INFRINGEMENT\n(DMCA/CopyRiGHT)");


		_btnReportCopyright.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_btnReportCopyright.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_btnReportCopyright.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_btnReportCopyright.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());


		_btnReportCopyright.AddThemeColorOverride("font_color", new Color(0.85f, 0.35f, 0.35f));
		_btnReportCopyright.AddThemeColorOverride("font_hover_color", new Color(1.0f, 0.45f, 0.45f));
		_btnReportCopyright.AddThemeColorOverride("font_pressed_color", new Color(1.0f, 0.55f, 0.55f));
		_btnReportCopyright.AddThemeFontSizeOverride("font_size", 14);

		_btnReportCopyright.Pressed += () =>
		{
			GameContext.UserInterface.PlayClickSound();
			OS.ShellOpen("https://github.com/RealmArcade/Realm/issues/new?title=DMCA/Copyright+Infringement+Report");
		};
		_btnReportCopyright.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();
	}


	private static readonly Dictionary<string, ReviewEntry> _loadedReviews = new();

	private string GetActiveMapName()
	{
		if (GameContext.Lobby != null && !string.IsNullOrEmpty(GameContext.Lobby.ActiveMapName))
		{
			return GameContext.Lobby.ActiveMapName;
		}

		return "castle_td";
	}

	private void SetupWriteReviewButton()
	{
		_btnWriteReview.Flat = false;
		string mapName = GetActiveMapName();
		bool hasExisting = _loadedReviews.ContainsKey(mapName);
		UIStyle.ApplyButtonText(_btnWriteReview, hasExisting ? "Edit Your Review" : "Write a Review", 18);

		_btnWriteReview.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_btnWriteReview.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_btnWriteReview.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_btnWriteReview.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		_btnWriteReview.Pressed += () =>
		{
			GameContext.UserInterface.PlayClickSound();
			ShowReviewPopup();
		};
		_btnWriteReview.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();
	}

	private void SetupWishlistButton()
	{
		if (!GameContext.Steam.CanOpenStorePage) return;

		var wishlistButton = new Button { Name = "BtnWishlist" };
		wishlistButton.Flat = false;
		wishlistButton.Icon = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/steam_icon.webp");
		wishlistButton.ExpandIcon = true;
		wishlistButton.AddThemeConstantOverride("icon_max_width", 24);
		UIStyle.ApplyButtonText(wishlistButton, TranslationServer.Translate("WISHLIST ON STEAM"), 18);
		wishlistButton.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		wishlistButton.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		wishlistButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		wishlistButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		wishlistButton.Pressed += () =>
		{
			GameContext.UserInterface.PlayClickSound();
			GameContext.Steam.OpenFullGameStorePage();
		};
		wishlistButton.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();

		var bottomButtons = GetNode<HBoxContainer>("BottomButtons");
		bottomButtons.AddChild(wishlistButton);
		bottomButtons.MoveChild(wishlistButton, _btnWriteReview.GetIndex());
	}

	private void ShowReviewPopup()
	{
		string mapName = GetActiveMapName();
		bool hasExisting = _loadedReviews.ContainsKey(mapName);
		var existingReview = hasExisting ? _loadedReviews[mapName] : null;


		var rootPopup = new ColorRect();
		rootPopup.Name = "RootPopup_ColorRect";
		rootPopup.Color = new Color(0, 0, 0, 0.6f);
		rootPopup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(rootPopup);


		var panel = new PanelContainer();
		panel.Name = "Panel_PanelContainer";
		panel.CustomMinimumSize = new Vector2(550, 420);
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		rootPopup.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.Name = "Vbox_VBoxContainer";
		vbox.AddThemeConstantOverride("separation", 15);
		panel.AddChild(vbox);


		var titleLabel = new Label();
		titleLabel.Name = "TitleLabel_Label";
		UIStyle.ApplyTitle(titleLabel, hasExisting ? Tr("EDIT YOUR REVIEW") : Tr("WRITE A REVIEW"), 22);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(titleLabel);


		var mapLabel = new Label();
		mapLabel.Name = "MapLabel_Label";
		mapLabel.Text = $"{Tr("Map")}: {mapName.ToUpper()}";
		mapLabel.HorizontalAlignment = HorizontalAlignment.Center;
		mapLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		mapLabel.AddThemeFontSizeOverride("font_size", 14);
		vbox.AddChild(mapLabel);


		var ratingHBox = new HBoxContainer();
		ratingHBox.Name = "RatingHBox_HBoxContainer";
		ratingHBox.Alignment = BoxContainer.AlignmentMode.Center;
		ratingHBox.AddThemeConstantOverride("separation", 10);
		vbox.AddChild(ratingHBox);

		var ratingTitle = new Label();
		ratingTitle.Name = "RatingTitle_Label";
		ratingTitle.Text = Tr("Rating:");
		ratingTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		ratingTitle.AddThemeFontSizeOverride("font_size", 16);
		ratingHBox.AddChild(ratingTitle);

		var ratingOption = new OptionButton();
		ratingOption.Name = "RatingOption_OptionButton";
		ratingOption.CustomMinimumSize = new Vector2(180, 36);
		ratingOption.AddThemeStyleboxOverride("normal", UIStyle.CreateTextInput(false));
		ratingOption.AddThemeStyleboxOverride("hover", UIStyle.CreateTextInput(true));
		ratingOption.AddThemeStyleboxOverride("pressed", UIStyle.CreateTextInput(true));
		ratingOption.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
		ratingOption.AddItem(Tr("5 Stars (Excellent)"), 5);
		ratingOption.AddItem(Tr("4 Stars (Good)"), 4);
		ratingOption.AddItem(Tr("3 Stars (Average)"), 3);
		ratingOption.AddItem(Tr("2 Stars (Poor)"), 2);
		ratingOption.AddItem(Tr("1 Star (Terrible)"), 1);

		if (hasExisting && existingReview != null)
		{
			int idx = 5 - existingReview.Rating;
			ratingOption.Select(idx);
		}
		else
		{
			ratingOption.Select(FiveStarRatingOptionIndex);
		}

		ratingHBox.AddChild(ratingOption);


		var commentsLabel = new Label();
		commentsLabel.Name = "CommentsLabel_Label";
		commentsLabel.Text = Tr("Your Review comments:");
		commentsLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		commentsLabel.AddThemeFontSizeOverride("font_size", 15);
		vbox.AddChild(commentsLabel);

		var commentsEdit = new TextEdit();
		commentsEdit.Name = "CommentsEdit_TextEdit";
		commentsEdit.SizeFlagsVertical = SizeFlags.ExpandFill;
		commentsEdit.PlaceholderText = Tr("Write your thoughts about this map here...");
		commentsEdit.AddThemeStyleboxOverride("normal", UIStyle.CreateTextInput(false));
		commentsEdit.AddThemeStyleboxOverride("focus", UIStyle.CreateTextInput(true));
		commentsEdit.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.75f));
		commentsEdit.AddThemeColorOverride("font_placeholder_color", new Color(0.5f, 0.5f, 0.5f));

		if (hasExisting && existingReview != null)
		{
			commentsEdit.Text = existingReview.Comments;
		}

		vbox.AddChild(commentsEdit);


		var actionsHBox = new HBoxContainer();
		actionsHBox.Name = "ActionsHBox_HBoxContainer";
		actionsHBox.Alignment = BoxContainer.AlignmentMode.Center;
		actionsHBox.AddThemeConstantOverride("separation", 25);
		vbox.AddChild(actionsHBox);

		var btnSubmit = new Button();
		btnSubmit.Name = "BtnSubmit_Button";
		btnSubmit.AddThemeConstantOverride("icon_max_width", 0);
		btnSubmit.CustomMinimumSize = new Vector2(150, 42);
		UIStyle.ApplyButtonText(btnSubmit, "Submit", 16);
		btnSubmit.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btnSubmit.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btnSubmit.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btnSubmit.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		actionsHBox.AddChild(btnSubmit);

		var btnCancel = new Button();
		btnCancel.Name = "BtnCancel_Button";
		btnCancel.AddThemeConstantOverride("icon_max_width", 0);
		btnCancel.CustomMinimumSize = new Vector2(150, 42);
		UIStyle.ApplyButtonText(btnCancel, "Cancel", 16);
		btnCancel.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btnCancel.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btnCancel.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btnCancel.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		actionsHBox.AddChild(btnCancel);


		btnCancel.Pressed += () =>
		{
			GameContext.UserInterface.PlayClickSound();
			rootPopup.QueueFree();
		};
		btnCancel.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();

		btnSubmit.Pressed += async () =>
		{
			GameContext.UserInterface.PlayClickSound();
			int selectedRating = ratingOption.GetSelectedId();
			string commentText = commentsEdit.Text;

			var entry = new ReviewEntry { Rating = selectedRating, Comments = commentText };
			_loadedReviews[mapName] = entry;


			UIStyle.ApplyButtonText(_btnWriteReview, "Edit Your Review", 18);
			rootPopup.QueueFree();

			double playtimeMin = 0.0;
			if (GodotObject.IsInstanceValid(GameContext.Lobby) && GameContext.Lobby.GameSessionStartTime != null)
			{
				playtimeMin = (DateTime.UtcNow - GameContext.Lobby.GameSessionStartTime.Value).TotalMinutes;
			}

			await ReportMetricsAsync(mapName, playtimeMin, selectedRating, true);
		};
		btnSubmit.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();
	}

	private void UpdateTabDisplay()
	{
		var activeStyle = new StyleBoxFlat();
		activeStyle.BgColor = new Color(0.18f, 0.19f, 0.23f, 0.95f);
		activeStyle.BorderColor = UIStyle.ColorGold;
		activeStyle.SetBorderWidthAll(2);
		activeStyle.CornerRadiusTopLeft = 4;
		activeStyle.CornerRadiusTopRight = 4;

		var normStyle = UIStyle.CreateButtonNormal();
		if (normStyle is StyleBoxFlat flatNorm)
		{
			flatNorm.CornerRadiusBottomLeft = 0;
			flatNorm.CornerRadiusBottomRight = 0;
		}

		_btnOverview.AddThemeStyleboxOverride("normal", _activeTab == OverviewTabIndex ? activeStyle : normStyle);
		_btnEconomy.AddThemeStyleboxOverride("normal", _activeTab == EconomyTabIndex ? activeStyle : normStyle);
		_btnMilitary.AddThemeStyleboxOverride("normal", _activeTab == MilitaryTabIndex ? activeStyle : normStyle);


		if (_activeTab == OverviewTabIndex)
		{
			_scoreColHeader.Text = TranslationServer.Translate("TOTAL RESOURCES");
			_killsColHeader.Text = TranslationServer.Translate("UNITS KILLED");
			_resourcesColHeader.Text = TranslationServer.Translate("POPULATION");
		}
		else if (_activeTab == EconomyTabIndex)
		{
			_scoreColHeader.Text = TranslationServer.Translate("GOLD");
			_killsColHeader.Text = TranslationServer.Translate("WOOD");
			_resourcesColHeader.Text = TranslationServer.Translate("STONE");
		}
		else
		{
			_scoreColHeader.Text = TranslationServer.Translate("UNITS KILLED");
			_killsColHeader.Text = TranslationServer.Translate("ARMY");
			_resourcesColHeader.Text = TranslationServer.Translate("POPULATION");
		}

		PopulateRows();
	}

	private void PopulateRows()
	{
		foreach (Node child in _tableRowsContainer.GetChildren())
		{
			child.QueueFree();
		}

		MatchScoreboardReader.Collect(_results);
		_results.Sort(_activeTab == MilitaryTabIndex ? CompareByKills : CompareByTotalResources);

		var rows = new List<StatsRow>(_results.Count);
		for (int index = 0; index < _results.Count; index++)
		{
			MatchPlayerResult result = _results[index];
			string thirdColumn;
			string fourthColumn;
			string fifthColumn;

			if (_activeTab == OverviewTabIndex)
			{
				thirdColumn = FormatNumber(result.TotalResources);
				fourthColumn = FormatNumber(result.Kills);
				fifthColumn = FormatPopulation(result);
			}
			else if (_activeTab == EconomyTabIndex)
			{
				thirdColumn = FormatNumber(result.Gold);
				fourthColumn = FormatNumber(result.Wood);
				fifthColumn = FormatNumber(result.Stone);
			}
			else
			{
				thirdColumn = FormatNumber(result.Kills);
				fourthColumn = FormatNumber(result.ArmySize);
				fifthColumn = FormatPopulation(result);
			}

			rows.Add(new StatsRow
			{
				Player = result.Name,
				Faction = string.IsNullOrWhiteSpace(result.Faction) ? result.TeamName : result.Faction,
				Col3 = thirdColumn,
				Col4 = fourthColumn,
				Col5 = fifthColumn
			});
		}

		foreach (var r in rows)
		{
			var rowContainer = new HBoxContainer();
			rowContainer.Name = "RowContainer_HBoxContainer";
			rowContainer.CustomMinimumSize = new Vector2(0, 32);

			var pLbl = new Label();
			pLbl.Name = "PLbl_Label";
			pLbl.Text = $"  {r.Player}";
			pLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			pLbl.AddThemeFontSizeOverride("font_size", 15);
			pLbl.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
			pLbl.VerticalAlignment = VerticalAlignment.Center;
			rowContainer.AddChild(pLbl);

			var fLbl = new Label();
			fLbl.Name = "FLbl_Label";
			fLbl.Text = r.Faction;
			fLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			fLbl.AddThemeFontSizeOverride("font_size", 14);
			fLbl.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
			fLbl.VerticalAlignment = VerticalAlignment.Center;
			rowContainer.AddChild(fLbl);

			var c3Lbl = new Label();
			c3Lbl.Name = "C3Lbl_Label";
			c3Lbl.Text = r.Col3;
			c3Lbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			c3Lbl.AddThemeFontSizeOverride("font_size", 15);
			c3Lbl.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
			c3Lbl.VerticalAlignment = VerticalAlignment.Center;
			rowContainer.AddChild(c3Lbl);

			var c4Lbl = new Label();
			c4Lbl.Name = "C4Lbl_Label";
			c4Lbl.Text = r.Col4;
			c4Lbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			c4Lbl.AddThemeFontSizeOverride("font_size", 14);
			c4Lbl.AddThemeColorOverride("font_color", new Color(0.85f, 0.8f, 0.6f));
			c4Lbl.VerticalAlignment = VerticalAlignment.Center;
			rowContainer.AddChild(c4Lbl);

			var c5Lbl = new Label();
			c5Lbl.Name = "C5Lbl_Label";
			c5Lbl.Text = $"{r.Col5}  ";
			c5Lbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			c5Lbl.HorizontalAlignment = HorizontalAlignment.Right;
			c5Lbl.AddThemeFontSizeOverride("font_size", 15);
			c5Lbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			c5Lbl.VerticalAlignment = VerticalAlignment.Center;
			rowContainer.AddChild(c5Lbl);

			_tableRowsContainer.AddChild(rowContainer);
		}
	}

	private static int CompareByKills(MatchPlayerResult left, MatchPlayerResult right)
	{
		int comparison = right.Kills.CompareTo(left.Kills);
		return comparison != 0 ? comparison : left.Slot.CompareTo(right.Slot);
	}

	private static int CompareByTotalResources(MatchPlayerResult left, MatchPlayerResult right)
	{
		int comparison = right.TotalResources.CompareTo(left.TotalResources);
		return comparison != 0 ? comparison : left.Slot.CompareTo(right.Slot);
	}

	private static string FormatNumber(int value) => value.ToString("N0");

	private static string FormatPopulation(MatchPlayerResult result) => $"{result.PopulationUsed} / {result.PopulationCapacity}";

	private async Task ReportMetricsAsync(string mapName, double playtimeMinutes, int stars, bool isComplete)
	{
		string mapTitle = CleanMapName(mapName);
		string mapVersion = GameContext.Lobby?.ActiveMapVersion ?? "1.0.0";

		ExtractManifestInfo(mapName, mapTitle, ref mapVersion, out string authorPubKey);

		var payload = new
		{
			MapTitle = mapTitle,
			MapVersion = mapVersion,
			AuthorPublicKey = authorPubKey,
			PlaytimeMinutes = playtimeMinutes,
			Stars = stars,
			IsCompleteGame = isComplete,
			SessionToken = GameContext.Account.SessionToken
		};

		string seedServerUrl = GodotObject.IsInstanceValid(GameContext.Lobby) ? GameContext.Lobby.RegistryServerUrl : ServersConfigHelper.GetDefaultServerUrl();
		try
		{
			using var httpClient = new HttpClient();
			var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
			await httpClient.PostAsync(seedServerUrl + "/api/maps/report_metrics", content);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[GameOver] Failed to report map metrics: {ex.Message}");
		}
	}

	private void AddLocalPlaytime(string mapName, double playtimeMinutes)
	{
		try
		{
			string rawName = CleanMapName(mapName);
			var contributors = GetContributorsFromMetadata(rawName);

			if (contributors.Count == 0)
			{
				contributors.Add(UnknownAuthorContributorName);
			}

			UpdatePlaytimeStats(contributors, playtimeMinutes);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[GameOver] Failed to update local playtime stats: {ex.Message}");
		}
	}

	private static string CleanMapName(string mapName)
	{
		string mapTitle = mapName;
		if (mapTitle.StartsWith("[Beta-Testing] "))
		{
			mapTitle = mapTitle.Substring("[Beta-Testing] ".Length);
		}

		int dashIdx = mapTitle.LastIndexOf(" - ");
		if (dashIdx != -1)
		{
			mapTitle = mapTitle.Substring(0, dashIdx);
		}

		return mapTitle.Trim();
	}

	private static void ExtractManifestInfo(string mapName, string mapTitle, ref string mapVersion, out string authorPubKey)
	{
		authorPubKey = "";
		try
		{
			if (TryExtractFromManifest(mapName, mapTitle, ref mapVersion, out string pubKey))
			{
				authorPubKey = pubKey;
				return;
			}

			authorPubKey = ExtractAuthorPubKeyFromMetadata(mapTitle);
		}
		catch
		{
		}
	}

	private static bool TryExtractFromManifest(string mapName, string mapTitle, ref string mapVersion, out string authorPubKey)
	{
		authorPubKey = "";
		string? manifestPath = MapAssetManager.FindManifestPath(mapName, mapVersion)
		                       ?? MapAssetManager.FindManifestPath(mapTitle, mapVersion);

		if (string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath))
			return false;

		string json = File.ReadAllText(manifestPath);
		using var mapDoc = JsonDocument.Parse(json);
		var root = mapDoc.RootElement;

		if (root.TryGetProperty("author_key", out var keyProp))
			authorPubKey = keyProp.GetString() ?? "";

		if (root.TryGetProperty("Version", out var vProp) && vProp.ValueKind == JsonValueKind.String)
			mapVersion = vProp.GetString() ?? mapVersion;

		return true;
	}

	private static string ExtractAuthorPubKeyFromMetadata(string mapTitle)
	{
		string[] possiblePaths =
		{
			ProjectSettings.GlobalizePath($"user://maps/{mapTitle}/metadata.json"),
			ProjectSettings.GlobalizePath($"res://Maps/{mapTitle}/metadata.json"),
			ProjectSettings.GlobalizePath($"{GameContext.EditorService.TempWorkspaceGodotPath}/metadata.json")
		};

		foreach (var p in possiblePaths)
		{
			if (!File.Exists(p))
				continue;

			string json = File.ReadAllText(p);
			using var mapDoc = JsonDocument.Parse(json);
			var root = mapDoc.RootElement;
			if (root.TryGetProperty("author_key", out var keyProp))
				return keyProp.GetString() ?? "";
			break;
		}

		return "";
	}

	private static List<string> GetContributorsFromMetadata(string rawName)
	{
		var contributors = new List<string>();
		string[] paths =
		{
			ProjectSettings.GlobalizePath($"res://Maps/{rawName}/metadata.json"),
			ProjectSettings.GlobalizePath($"user://maps/{rawName}/metadata.json"),
			ProjectSettings.GlobalizePath($"{GameContext.EditorService.TempWorkspaceGodotPath}/metadata.json")
		};

		foreach (var path in paths)
		{
			if (!File.Exists(path)) continue;

			string json = File.ReadAllText(path);
			using var doc = JsonDocument.Parse(json);
			if (!doc.RootElement.TryGetProperty("Contributors", out var conts) || conts.ValueKind != JsonValueKind.Array) break;

			foreach (var el in conts.EnumerateArray())
			{
				var s = el.GetString();
				if (!string.IsNullOrEmpty(s)) contributors.Add(s);
			}

			break;
		}

		return contributors;
	}

	private static void UpdatePlaytimeStats(List<string> contributors, double playtimeMinutes)
	{
		string statsPath = ProjectSettings.GlobalizePath("user://appdata/playtime_stats.json");
		var stats = new Dictionary<string, int>();
		if (File.Exists(statsPath))
		{
			string statsJson = File.ReadAllText(statsPath);
			stats = JsonSerializer.Deserialize<Dictionary<string, int>>(statsJson) ?? stats;
		}

		foreach (var contributor in contributors)
		{
			if (!stats.ContainsKey(contributor)) stats[contributor] = 0;
			stats[contributor] += (int)playtimeMinutes;
		}

		Directory.CreateDirectory(Path.GetDirectoryName(statsPath)!);
		File.WriteAllText(statsPath, JsonSerializer.Serialize(stats));
	}
}
