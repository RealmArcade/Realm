using System;
using System.Collections.Generic;
using System.Globalization;
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

	private Panel _bgPanel = null!;
	private Panel _leftPillar = null!;
	private Panel _rightPillar = null!;
	private PanelContainer _mainPanel = null!;
	private PanelContainer _tableFrame = null!;
	private PanelContainer _graphFrame = null!;

	private Label _titleLabel = null!;
	private Button _btnOverview = null!;
	private Button _btnEconomy = null!;
	private Button _btnMilitary = null!;
	private Button _btnClose = null!;
	private Button _btnReportCopyright = null!;
	private Button _btnWriteReview = null!;

	private VBoxContainer _tableRowsContainer = null!;
	private Control _graphContainer = null!;

	private Label _placementColHeader = null!;
	private Label _friendColHeader = null!;
	private Label _playerColHeader = null!;
	private Label _factionColHeader = null!;
	private Label _scoreColHeader = null!;
	private Label _killsColHeader = null!;
	private Label _resourcesColHeader = null!;

	private const int OverviewTabIndex = 0;
	private const int EconomyTabIndex = 1;
	private const int MilitaryTabIndex = 2;
	private const int FiveStarRatingOptionIndex = 0;
	private const int TwoTeamMatchTeamCount = 2;
	private const int FirstPlace = 1;
	private const int SecondPlace = 2;
	private const float PlacementRefreshSeconds = 2f;
	private const float PlacementColumnWidth = 88f;
	private const float FriendColumnWidth = 196f;
	private const string UnknownAuthorContributorName = "Realm Builder";

	private bool _isVictory = true;
	private int _activeTab = OverviewTabIndex;

	private readonly List<MatchPlayerResult> _results = new();
	private readonly List<MatchPlayerResult> _placementProbe = new();
	private ulong _placementSignature;
	private double _placementRefreshRemaining;

	private readonly string[] _runes = { "ᚠ", "ᚢ", "ᚦ", "ᚨ", "ᚱ", "ᚲ", "ᚷ", "ᚹ", "ᚺ", "ᚾ", "ᛁ", "ᛃ", "ᛇ", "ᛈ", "ᛉ", "ᛊ", "ᛏ", "ᛒ", "ᛖ", "ᛗ", "ᛚ", "ᛜ", "ᛞ", "ᛟ" };


	public override void _Ready()
	{
		(_bgPanel, _leftPillar, _rightPillar) = UIStyle.BindStandardChrome(this);
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


		_placementColHeader = new Label();
		_placementColHeader.Name = "PlacementColHeader_Label";
		_placementColHeader.Text = TranslationServer.Translate("RANK");
		_placementColHeader.CustomMinimumSize = new Vector2(PlacementColumnWidth, 0);
		_placementColHeader.ClipText = true;
		_placementColHeader.Visible = false;

		_friendColHeader = new Label();
		_friendColHeader.Name = "FriendColHeader_Label";
		_friendColHeader.Text = string.Empty;
		_friendColHeader.CustomMinimumSize = new Vector2(FriendColumnWidth, 0);
		_friendColHeader.Visible = false;

		var tableHeader = GetNode<HBoxContainer>("MainPanel/VBox/TableFrame/VBox/TableHeader");
		tableHeader.AddChild(_placementColHeader);
		tableHeader.MoveChild(_placementColHeader, 0);
		tableHeader.AddChild(_friendColHeader);

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
		_placementRefreshRemaining = PlacementRefreshSeconds;

		string mapName = GetActiveMapName();
		double playtimeMinutes = 0.0;
		if (GodotObject.IsInstanceValid(GameContext.Lobby) && GameContext.Lobby.GameSessionStartTime != null)
		{
			playtimeMinutes = (DateTime.UtcNow - GameContext.Lobby.GameSessionStartTime.Value).TotalMinutes;
		}

		AddLocalPlaytime(mapName, playtimeMinutes);

		_ = ReportMetricsAsync(mapName, playtimeMinutes, stars: 0, isComplete: true);
	}

	public override void _Process(double delta)
	{
		if (_tableRowsContainer == null || _placementRefreshRemaining <= 0) return;
		_placementRefreshRemaining -= delta;

		MatchScoreboardReader.Collect(_placementProbe);
		if (ComputePlacementSignature(_placementProbe) == _placementSignature) return;

		_placementRefreshRemaining = 0;
		PopulateRows();
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


		var headers = new[] { _placementColHeader, _playerColHeader, _factionColHeader, _scoreColHeader, _killsColHeader, _resourcesColHeader };
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

	private bool _matchResultReported;

	/// <summary>
	///     Records how this multiplayer match ended against the signed-in player's record, once. A single-player game or a
	///     playtest is not counted, and neither is a screen with no account: the record is the authority's, and it is only
	///     asked for something it can attribute. Called once the outcome is known, because a card that is built before it is
	///     told how the match ended would report the wrong one.
	/// </summary>
	public void ReportMatchResult()
	{
		if (_matchResultReported) return;
		_matchResultReported = true;

		if (GameContext.Host?.IsRankedMultiplayerMatch != true) return;
		if (!GameContext.Account.IsSignedIn) return;

		string matchId = DescribeMatchForRecord();
		if (matchId.Length == 0) return;

		_ = GameContext.Account.ReportMatchResultAsync(matchId, _isVictory);
	}

	/// <summary>
	///     Names this match for the record it is counted in. The seed alone repeats across matches, so the moment the match
	///     began is carried with it: the pair is what the authority uses to count one match once however often it is told.
	/// </summary>
	private static string DescribeMatchForRecord()
	{
		LobbyManager? lobby = GameContext.Lobby;
		if (lobby == null || !GodotObject.IsInstanceValid(lobby)) return string.Empty;

		string startedAt = lobby.GameSessionStartTime?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;
		return $"{lobby.ActiveMapName}|{lobby.MatchSeed}|{startedAt}";
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

		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateGameOverTabNormal());
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateGameOverTabNormal(isHover: true));
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateGameOverTabActive());
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		btn.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			SetTab(index);
		};
		btn.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
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

		UIStyle.ApplyButtonStyleBoxesClearingFocus(_btnClose);

		_btnClose.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			if (GameContext.SessionState.IsTestMode)
			{
				GameContext.SessionState.IsTestMode = false;
				GameContext.SessionState.ReturningFromTest = true;
				if (GameContext.Lobby != null)
				{
					GameContext.Lobby.Disconnect();
				}

				GameContext.UserInterface?.TransitionTo(GameScreen.MapEditorHUD);
			}
			else
			{
				GameContext.UserInterface?.TransitionTo(GameScreen.MainMenu);
			}
		};
		_btnClose.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
	}

	private void SetupReportCopyrightButton()
	{
		_btnReportCopyright.Flat = false;
		_btnReportCopyright.Text = Tr("[!] REPORT INFRINGEMENT\n(DMCA/CopyRiGHT)");


		UIStyle.ApplyButtonStyleBoxesClearingFocus(_btnReportCopyright);


		_btnReportCopyright.AddThemeColorOverride("font_color", new Color(0.85f, 0.35f, 0.35f));
		_btnReportCopyright.AddThemeColorOverride("font_hover_color", new Color(1.0f, 0.45f, 0.45f));
		_btnReportCopyright.AddThemeColorOverride("font_pressed_color", new Color(1.0f, 0.55f, 0.55f));
		_btnReportCopyright.AddThemeFontSizeOverride("font_size", 14);

		_btnReportCopyright.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			OS.ShellOpen("https://github.com/RealmArcade/Realm/issues/new?title=DMCA/Copyright+Infringement+Report");
		};
		_btnReportCopyright.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
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

		UIStyle.ApplyButtonStyleBoxesClearingFocus(_btnWriteReview);

		_btnWriteReview.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			ShowReviewPopup();
		};
		_btnWriteReview.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
	}

	private void UpdateTabDisplay()
	{
		_btnOverview.AddThemeStyleboxOverride("normal", _activeTab == OverviewTabIndex ? UIStyle.CreateGameOverTabActive() : UIStyle.CreateGameOverTabNormal());
		_btnEconomy.AddThemeStyleboxOverride("normal", _activeTab == EconomyTabIndex ? UIStyle.CreateGameOverTabActive() : UIStyle.CreateGameOverTabNormal());
		_btnMilitary.AddThemeStyleboxOverride("normal", _activeTab == MilitaryTabIndex ? UIStyle.CreateGameOverTabActive() : UIStyle.CreateGameOverTabNormal());


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
		_results.Sort(_activeTab == MilitaryTabIndex ? CompareByPlacementThenKills : CompareByPlacementThenResources);
		_placementSignature = ComputePlacementSignature(_results);

		bool showPlacements = HasPlacements(_results);
		_placementColHeader.Visible = showPlacements;

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
				Rank = FormatPlacement(result),
				IsFirstPlace = result.Placement == FirstPlace,
				Player = result.Name,
				SteamId = result.SteamId,
				IsComputer = result.IsComputer,
				Faction = string.IsNullOrWhiteSpace(result.Faction) ? result.TeamName : result.Faction,
				Col3 = thirdColumn,
				Col4 = fourthColumn,
				Col5 = fifthColumn
			});
		}

		bool showFriendColumn = false;
		foreach (StatsRow row in rows)
		{
			if (OffersFriendRequest(row))
			{
				showFriendColumn = true;
				break;
			}
		}

		_friendColHeader.Visible = showFriendColumn;

		foreach (var r in rows)
		{
			var rowContainer = new HBoxContainer();
			rowContainer.Name = "RowContainer_HBoxContainer";
			rowContainer.CustomMinimumSize = new Vector2(0, 32);

			if (showPlacements)
			{
				var rankLabel = new Label();
				rankLabel.Name = "Rank_Label";
				rankLabel.Text = r.Rank;
				rankLabel.CustomMinimumSize = new Vector2(PlacementColumnWidth, 0);
				rankLabel.ClipText = true;
				rankLabel.AddThemeFontSizeOverride("font_size", 13);
				rankLabel.AddThemeColorOverride("font_color", r.IsFirstPlace ? UIStyle.ColorGold : new Color(0.75f, 0.72f, 0.62f));
				rankLabel.VerticalAlignment = VerticalAlignment.Center;
				rowContainer.AddChild(rankLabel);
			}

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

			if (showFriendColumn) rowContainer.AddChild(CreateFriendCell(r));

			_tableRowsContainer.AddChild(rowContainer);
		}
	}

	/// <summary>
	///     Whether a row gets the friend-request button: a seat played by a person, whose own client stated a Steam id, and not
	///     this screen's own seat. Steam has nothing to add for a computer seat, nothing to name for a player who is not
	///     running inside Steam, and nothing to do with a request to itself.
	/// </summary>
	private static bool OffersFriendRequest(in StatsRow row)
	{
		if (row.IsComputer || row.SteamId == 0) return false;

		ulong ownSteamId = GameContext.Steam.SteamId;
		return ownSteamId == 0 || ownSteamId != row.SteamId;
	}

	/// <summary>
	///     Puts Steam's add-friend dialog in front of the player for the person behind a seat. Steam owns the request itself:
	///     no game can send one on a player's behalf, so the dialog is the whole of the action and its confirmation is Steam's.
	/// </summary>
	private static void RequestSteamFriend(string playerName, ulong playerSteamId)
	{
		if (!GameContext.Steam.IsAvailable)
		{
			GameContext.UserInterface?.ShowPopupMessage(TranslationServer.Translate("STEAM UNAVAILABLE"), GameContext.Steam.UnavailableExplanation);
			return;
		}

		if (GameContext.Steam.OpenAddFriendDialog(playerSteamId)) return;

		GameContext.UserInterface?.PlayWarningSound();
		GameContext.UserInterface?.ShowPopupMessage(TranslationServer.Translate("STEAM FRIEND REQUEST"), string.Format(TranslationServer.Translate("Steam could not open the add friend dialog for {0}."), playerName));
	}

	/// <summary>
	///     The last cell of a row, which is the one place a player can act on the people they just played with. Every row
	///     holds the cell whether or not it offers the button, so the columns above and below it stay lined up, and a row whose
	///     seat is a computer or belongs to this screen holds an empty one.
	/// </summary>
	private Control CreateFriendCell(in StatsRow row)
	{
		if (!OffersFriendRequest(row)) return new Control { Name = "FriendCell_Spacer", CustomMinimumSize = new Vector2(FriendColumnWidth, 0) };

		string playerName = row.Player;
		ulong playerSteamId = row.SteamId;

		Button friendButton = UIStyle.CreateActionButton("SendSteamFriendRequest_Button", TranslationServer.Translate("Send Steam Friend Request"), 12);
		friendButton.CustomMinimumSize = new Vector2(FriendColumnWidth, 24);
		friendButton.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		friendButton.TooltipText = string.Format(TranslationServer.Translate("Open Steam's add friend dialog for {0}."), playerName);
		friendButton.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			RequestSteamFriend(playerName, playerSteamId);
		};
		friendButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		return friendButton;
	}

	private static bool HasPlacements(List<MatchPlayerResult> results)
	{
		for (int index = 0; index < results.Count; index++)
		{
			if (results[index].Placement > 0) return true;
		}

		return false;
	}

	private static ulong ComputePlacementSignature(List<MatchPlayerResult> results)
	{
		ulong signature = (ulong)results.Count;
		for (int index = 0; index < results.Count; index++)
		{
			MatchPlayerResult result = results[index];
			signature = (signature * 31) + (ulong)result.Slot;
			signature = (signature * 31) + (ulong)result.Placement;
		}

		return signature;
	}

	private static string FormatPlacement(MatchPlayerResult result)
	{
		if (result.Placement <= 0) return string.Empty;

		if (result.TeamCount == TwoTeamMatchTeamCount)
		{
			if (result.Placement == FirstPlace) return TranslationServer.Translate("Winner");
			if (result.Placement == SecondPlace) return TranslationServer.Translate("Loser");
		}

		return $"#{result.Placement}";
	}

	private static int CompareByPlacement(MatchPlayerResult left, MatchPlayerResult right)
	{
		int leftRank = left.Placement > 0 ? left.Placement : int.MaxValue;
		int rightRank = right.Placement > 0 ? right.Placement : int.MaxValue;
		return leftRank.CompareTo(rightRank);
	}

	private static int CompareByPlacementThenKills(MatchPlayerResult left, MatchPlayerResult right)
	{
		int comparison = CompareByPlacement(left, right);
		return comparison != 0 ? comparison : CompareByKills(left, right);
	}

	private static int CompareByPlacementThenResources(MatchPlayerResult left, MatchPlayerResult right)
	{
		int comparison = CompareByPlacement(left, right);
		return comparison != 0 ? comparison : CompareByTotalResources(left, right);
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

}
