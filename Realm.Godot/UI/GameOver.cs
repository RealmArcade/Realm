using Godot;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq;
using System.Collections.Generic;
using Realm.Shared.Distribution;

public partial class GameOver : Control
{
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

	private bool _isVictory = true;
	private int _activeTab = 0; // 0=Overview, 1=Economy, 2=Military

	private readonly string[] _runes = { "ᚠ", "ᚢ", "ᚦ", "ᚨ", "ᚱ", "ᚲ", "ᚷ", "ᚹ", "ᚺ", "ᚾ", "ᛁ", "ᛃ", "ᛇ", "ᛈ", "ᛉ", "ᛊ", "ᛏ", "ᛒ", "ᛖ", "ᛗ", "ᛚ", "ᛜ", "ᛞ", "ᛟ" };

	private struct StatsRow
	{
		public string Player;
		public string Faction;
		public string Col3;
		public string Col4;
		public string Col5;
	}

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
		_graphContainer = GetNode<Control>("MainPanel/VBox/GraphFrame/TimelineGraph");


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

		UpdateTabDisplay();

		string mapName = GetActiveMapName();
		double playtimeMinutes = 0.0;
		if (GodotObject.IsInstanceValid(LobbyManager.Instance) && LobbyManager.Instance.GameSessionStartTime != null)
		{
			playtimeMinutes = (DateTime.UtcNow - LobbyManager.Instance.GameSessionStartTime.Value).TotalMinutes;
		}

		// Save local playtime statistics
		AddLocalPlaytime(mapName, playtimeMinutes);

		// Report baseline playtime metrics to server
		_ = ReportMetricsAsync(mapName, playtimeMinutes, 0, true);
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
		SetupTabButton(_btnOverview, "OVERVIEW", 0);
		SetupTabButton(_btnEconomy, "ECONOMY", 1);
		SetupTabButton(_btnMilitary, "MILITARY", 2);
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
			UIManager.Instance.PlayClickSound();
			SetTab(index);
		};
		btn.MouseEntered += () => UIManager.Instance.PlayHoverSound();
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
		_btnClose.Icon = GD.Load<Texture2D>("res://Assets/UI/cancel_button_2.png");
		_btnClose.ExpandIcon = true;

		string closeText = MapEditorHUD.IsTestMode ? "Back to Editor" : "Close";
		UIStyle.ApplyButtonText(_btnClose, closeText, 18);

		_btnClose.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_btnClose.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_btnClose.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_btnClose.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		_btnClose.Pressed += () => 
		{
			UIManager.Instance.PlayClickSound();
			if (MapEditorHUD.IsTestMode)
			{
				MapEditorHUD.IsTestMode = false;
				MapEditorHUD.ReturningFromTest = true;
				if (LobbyManager.Instance != null)
				{
					LobbyManager.Instance.Disconnect();
				}
				UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
			}
			else
			{
				UIManager.Instance.TransitionTo(GameScreen.MainMenu);
			}
		};
		_btnClose.MouseEntered += () => UIManager.Instance.PlayHoverSound();
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
			UIManager.Instance.PlayClickSound();
			OS.ShellOpen("https://github.com/RealmArcade/Realm/issues/new?title=DMCA/Copyright+Infringement+Report");
		};
		_btnReportCopyright.MouseEntered += () => UIManager.Instance.PlayHoverSound();
	}

	private class ReviewEntry
	{
		public int Rating { get; set; }
		public string Comments { get; set; }
	}


	private static readonly Dictionary<string, ReviewEntry> _loadedReviews = new();

	private string GetActiveMapName()
	{
		if (LobbyManager.Instance != null && !string.IsNullOrEmpty(LobbyManager.Instance.ActiveMapName))
		{
			return LobbyManager.Instance.ActiveMapName;
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
			UIManager.Instance.PlayClickSound();
			ShowReviewPopup();
		};
		_btnWriteReview.MouseEntered += () => UIManager.Instance.PlayHoverSound();
	}

	private void ShowReviewPopup()
	{
		string mapName = GetActiveMapName();
		bool hasExisting = _loadedReviews.ContainsKey(mapName);
		var existingReview = hasExisting ? _loadedReviews[mapName] : null;


		var rootPopup = new ColorRect();
		rootPopup.Color = new Color(0, 0, 0, 0.6f);
		rootPopup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(rootPopup);


		var panel = new PanelContainer();
		panel.CustomMinimumSize = new Vector2(550, 420);
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		rootPopup.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 15);
		panel.AddChild(vbox);


		var titleLabel = new Label();
		UIStyle.ApplyTitle(titleLabel, hasExisting ? Tr("EDIT YOUR REVIEW") : Tr("WRITE A REVIEW"), 22);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(titleLabel);


		var mapLabel = new Label();
		mapLabel.Text = $"{Tr("Map")}: {mapName.ToUpper()}";
		mapLabel.HorizontalAlignment = HorizontalAlignment.Center;
		mapLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		mapLabel.AddThemeFontSizeOverride("font_size", 14);
		vbox.AddChild(mapLabel);


		var ratingHBox = new HBoxContainer();
		ratingHBox.Alignment = BoxContainer.AlignmentMode.Center;
		ratingHBox.AddThemeConstantOverride("separation", 10);
		vbox.AddChild(ratingHBox);

		var ratingTitle = new Label();
		ratingTitle.Text = Tr("Rating:");
		ratingTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		ratingTitle.AddThemeFontSizeOverride("font_size", 16);
		ratingHBox.AddChild(ratingTitle);

		var ratingOption = new OptionButton();
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
			ratingOption.Select(0); // 5 stars default
		}
		ratingHBox.AddChild(ratingOption);


		var commentsLabel = new Label();
		commentsLabel.Text = Tr("Your Review comments:");
		commentsLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		commentsLabel.AddThemeFontSizeOverride("font_size", 15);
		vbox.AddChild(commentsLabel);

		var commentsEdit = new TextEdit();
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
		actionsHBox.Alignment = BoxContainer.AlignmentMode.Center;
		actionsHBox.AddThemeConstantOverride("separation", 25);
		vbox.AddChild(actionsHBox);

		var btnSubmit = new Button();
		btnSubmit.AddThemeConstantOverride("icon_max_width", 0);
		btnSubmit.CustomMinimumSize = new Vector2(150, 42);
		UIStyle.ApplyButtonText(btnSubmit, "Submit", 16);
		btnSubmit.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btnSubmit.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btnSubmit.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btnSubmit.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		actionsHBox.AddChild(btnSubmit);

		var btnCancel = new Button();
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
			UIManager.Instance.PlayClickSound();
			rootPopup.QueueFree();
		};
		btnCancel.MouseEntered += () => UIManager.Instance.PlayHoverSound();

		btnSubmit.Pressed += async () =>
		{
			UIManager.Instance.PlayClickSound();
			int selectedRating = ratingOption.GetSelectedId();
			string commentText = commentsEdit.Text;

			var entry = new ReviewEntry { Rating = selectedRating, Comments = commentText };
			_loadedReviews[mapName] = entry;


			UIStyle.ApplyButtonText(_btnWriteReview, "Edit Your Review", 18);
			rootPopup.QueueFree();

			double playtimeMin = 0.0;
			if (GodotObject.IsInstanceValid(LobbyManager.Instance) && LobbyManager.Instance.GameSessionStartTime != null)
			{
				playtimeMin = (DateTime.UtcNow - LobbyManager.Instance.GameSessionStartTime.Value).TotalMinutes;
			}
			await ReportMetricsAsync(mapName, playtimeMin, selectedRating, true);
		};
		btnSubmit.MouseEntered += () => UIManager.Instance.PlayHoverSound();
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

		_btnOverview.AddThemeStyleboxOverride("normal", _activeTab == 0 ? activeStyle : normStyle);
		_btnEconomy.AddThemeStyleboxOverride("normal", _activeTab == 1 ? activeStyle : normStyle);
		_btnMilitary.AddThemeStyleboxOverride("normal", _activeTab == 2 ? activeStyle : normStyle);


		if (_activeTab == 0) // Overview
		{
			_scoreColHeader.Text = Tr("TOTAL SCORE");
			_killsColHeader.Text = Tr("UNITS KILLED");
			_resourcesColHeader.Text = Tr("RESOURCES GATHERED") + "  ";
		}
		else if (_activeTab == 1) // Economy
		{
			_scoreColHeader.Text = Tr("GOLD GATHERED");
			_killsColHeader.Text = Tr("WOOD GATHERED");
			_resourcesColHeader.Text = Tr("TOTAL RESOURCES") + "  ";
		}
		else // Military
		{
			_scoreColHeader.Text = Tr("UNITS TRAINED");
			_killsColHeader.Text = Tr("UNITS KILLED");
			_resourcesColHeader.Text = Tr("UNITS LOST") + "  ";
		}

		PopulateRows();
	}

	private void PopulateRows()
	{
		foreach (Node child in _tableRowsContainer.GetChildren())
		{
			child.QueueFree();
		}

		List<StatsRow> rows = new List<StatsRow>();

		if (_activeTab == 0) // Overview
		{
			rows.Add(new StatsRow { Player = "PLAYER 1", Faction = "HUMAN", Col3 = "1,314", Col4 = "29", Col5 = "3,000" });
			rows.Add(new StatsRow { Player = "PLAYER 2", Faction = "HUMAN", Col3 = "592", Col4 = "24", Col5 = "2,500" });
			rows.Add(new StatsRow { Player = "PLAYER KILLED", Faction = "HUMAN", Col3 = "365", Col4 = "29", Col5 = "2,500" });
			rows.Add(new StatsRow { Player = "PLAYER KILLED", Faction = "HUMAN", Col3 = "207", Col4 = "35", Col5 = "4,600" });
			rows.Add(new StatsRow { Player = "Muothea6", Faction = "UNDEAD", Col3 = "207", Col4 = "24", Col5 = "4,600" });
			rows.Add(new StatsRow { Player = "CrawalMottros", Faction = "UNDEAD", Col3 = "163", Col4 = "2", Col5 = "1,000" });
		}
		else if (_activeTab == 1) // Economy
		{
			rows.Add(new StatsRow { Player = "PLAYER 1", Faction = "HUMAN", Col3 = "1,800", Col4 = "1,200", Col5 = "3,000" });
			rows.Add(new StatsRow { Player = "PLAYER 2", Faction = "HUMAN", Col3 = "1,500", Col4 = "1,000", Col5 = "2,500" });
			rows.Add(new StatsRow { Player = "PLAYER KILLED", Faction = "HUMAN", Col3 = "1,500", Col4 = "1,000", Col5 = "2,500" });
			rows.Add(new StatsRow { Player = "PLAYER KILLED", Faction = "HUMAN", Col3 = "2,600", Col4 = "2,000", Col5 = "4,600" });
			rows.Add(new StatsRow { Player = "Muothea6", Faction = "UNDEAD", Col3 = "2,600", Col4 = "2,000", Col5 = "4,600" });
			rows.Add(new StatsRow { Player = "CrawalMottros", Faction = "UNDEAD", Col3 = "600", Col4 = "400", Col5 = "1,000" });
		}
		else // Military
		{
			rows.Add(new StatsRow { Player = "PLAYER 1", Faction = "HUMAN", Col3 = "42", Col4 = "29", Col5 = "12" });
			rows.Add(new StatsRow { Player = "PLAYER 2", Faction = "HUMAN", Col3 = "35", Col4 = "24", Col5 = "18" });
			rows.Add(new StatsRow { Player = "PLAYER KILLED", Faction = "HUMAN", Col3 = "38", Col4 = "29", Col5 = "26" });
			rows.Add(new StatsRow { Player = "PLAYER KILLED", Faction = "HUMAN", Col3 = "55", Col4 = "35", Col5 = "48" });
			rows.Add(new StatsRow { Player = "Muothea6", Faction = "UNDEAD", Col3 = "49", Col4 = "24", Col5 = "38" });
			rows.Add(new StatsRow { Player = "CrawalMottros", Faction = "UNDEAD", Col3 = "15", Col4 = "2", Col5 = "14" });
		}

		foreach (var r in rows)
		{
			var rowContainer = new HBoxContainer();
			rowContainer.CustomMinimumSize = new Vector2(0, 32);

			var pLbl = new Label();
			pLbl.Text = $"  {r.Player}";
			pLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			pLbl.AddThemeFontSizeOverride("font_size", 15);
			pLbl.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
			pLbl.VerticalAlignment = VerticalAlignment.Center;
			rowContainer.AddChild(pLbl);

			var fLbl = new Label();
			fLbl.Text = r.Faction;
			fLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			fLbl.AddThemeFontSizeOverride("font_size", 14);
			fLbl.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
			fLbl.VerticalAlignment = VerticalAlignment.Center;
			rowContainer.AddChild(fLbl);

			var c3Lbl = new Label();
			c3Lbl.Text = r.Col3;
			c3Lbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			c3Lbl.AddThemeFontSizeOverride("font_size", 15);
			c3Lbl.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
			c3Lbl.VerticalAlignment = VerticalAlignment.Center;
			rowContainer.AddChild(c3Lbl);

			var c4Lbl = new Label();
			c4Lbl.Text = r.Col4;
			c4Lbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			c4Lbl.AddThemeFontSizeOverride("font_size", 14);
			c4Lbl.AddThemeColorOverride("font_color", new Color(0.85f, 0.8f, 0.6f));
			c4Lbl.VerticalAlignment = VerticalAlignment.Center;
			rowContainer.AddChild(c4Lbl);

			var c5Lbl = new Label();
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

	private async System.Threading.Tasks.Task ReportMetricsAsync(string mapName, double playtimeMinutes, int stars, bool isComplete)
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
		mapTitle = mapTitle.Trim();

		string mapVersion = LobbyManager.Instance?.ActiveMapVersion ?? "1.0.0";
		string authorPubKey = "";

		try
		{
			string? manifestPath = MapAssetManager.FindManifestPath(mapName, mapVersion)
				?? MapAssetManager.FindManifestPath(mapTitle, mapVersion);

			if (!string.IsNullOrEmpty(manifestPath) && System.IO.File.Exists(manifestPath))
			{
				string json = System.IO.File.ReadAllText(manifestPath);
				using var mapDoc = JsonDocument.Parse(json);
				var root = mapDoc.RootElement;
				if (root.TryGetProperty("author_key", out var keyProp))
				{
					authorPubKey = keyProp.GetString() ?? "";
				}
				if (root.TryGetProperty("Version", out var vProp) && vProp.ValueKind == JsonValueKind.String)
				{
					mapVersion = vProp.GetString() ?? mapVersion;
				}
			}
			else
			{
				string[] possiblePaths = {
					ProjectSettings.GlobalizePath($"user://maps/{mapTitle}/metadata.json"),
					ProjectSettings.GlobalizePath($"res://Maps/{mapTitle}/metadata.json"),
					ProjectSettings.GlobalizePath($"{MapEditorHUD.TempWorkspaceGodotPath}/metadata.json")
				};
				foreach (var p in possiblePaths)
				{
					if (System.IO.File.Exists(p))
					{
						string json = System.IO.File.ReadAllText(p);
						using var mapDoc = JsonDocument.Parse(json);
						var root = mapDoc.RootElement;
						if (root.TryGetProperty("author_key", out var keyProp))
						{
							authorPubKey = keyProp.GetString() ?? "";
						}
						break;
					}
				}
			}
		}
		catch {}
		
		var payload = new
		{
			MapTitle = mapTitle,
			MapVersion = mapVersion,
			AuthorPublicKey = authorPubKey,
			PlaytimeMinutes = playtimeMinutes,
			Stars = stars,
			IsCompleteGame = isComplete,
			PlayerId = LobbyManager.Instance?.AuthenticatedUsername ?? "Anonymous",
			AuthToken = LobbyManager.Instance?.AuthToken,
			AuthProvider = LobbyManager.Instance?.AuthProvider
		};
		
		string seedServerUrl = GodotObject.IsInstanceValid(LobbyManager.Instance) ? LobbyManager.Instance.RegistryServerUrl : ServersConfigHelper.GetDefaultServerUrl();
		try
		{
			using (var httpClient = new System.Net.Http.HttpClient())
			{
				var content = new System.Net.Http.StringContent(System.Text.Json.JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
				await httpClient.PostAsync(seedServerUrl + "/api/maps/report_metrics", content);
			}
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
			var contributors = new List<string>();
			string rawName = mapName;
			if (rawName.StartsWith("[Beta-Testing] "))
			{
				rawName = rawName.Substring("[Beta-Testing] ".Length);
			}
			int dashIdx = rawName.LastIndexOf(" - ");
			if (dashIdx != -1)
			{
				rawName = rawName.Substring(0, dashIdx);
			}
			rawName = rawName.Trim();

			string[] paths = {
				ProjectSettings.GlobalizePath($"res://Maps/{rawName}/metadata.json"),
				ProjectSettings.GlobalizePath($"user://maps/{rawName}/metadata.json"),
				ProjectSettings.GlobalizePath($"{MapEditorHUD.TempWorkspaceGodotPath}/metadata.json")
			};

			foreach (var path in paths)
			{
				if (System.IO.File.Exists(path))
				{
					string json = System.IO.File.ReadAllText(path);
					using var doc = JsonDocument.Parse(json);
					if (doc.RootElement.TryGetProperty("Contributors", out var conts) && conts.ValueKind == JsonValueKind.Array)
					{
						foreach (var el in conts.EnumerateArray())
						{
							var s = el.GetString();
							if (!string.IsNullOrEmpty(s)) contributors.Add(s);
						}
					}
					break;
				}
			}

			if (contributors.Count == 0)
			{
				contributors.Add("Realm Builder"); // fallback
			}

			string statsPath = ProjectSettings.GlobalizePath("user://appdata/playtime_stats.json");
			var stats = new Dictionary<string, int>();
			if (System.IO.File.Exists(statsPath))
			{
				string statsJson = System.IO.File.ReadAllText(statsPath);
				stats = JsonSerializer.Deserialize<Dictionary<string, int>>(statsJson) ?? stats;
			}

			foreach (var contributor in contributors)
			{
				if (!stats.ContainsKey(contributor)) stats[contributor] = 0;
				stats[contributor] += (int)playtimeMinutes;
			}

			System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(statsPath));
			System.IO.File.WriteAllText(statsPath, JsonSerializer.Serialize(stats));
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[GameOver] Failed to update local playtime stats: {ex.Message}");
		}
	}
}

