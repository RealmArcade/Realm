// The lobby table's look: the theme pass over the panels and buttons, then the header layout - column
// widths, the difficulty header icons, and the faction column visibility that depends on the map.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using NSec.Cryptography;
using Realm.Client.Network;
using Realm.Shared;
using Realm.Shared.Network;
using HttpClient = System.Net.Http.HttpClient;
using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Utils;
using Realm.Client.UI.InGame;
using Realm.Client.Services;
using Realm.Shared.Accounts;


namespace Realm.Client.UI;

public partial class LobbyRoom
{
	private void ApplyThemeStyles()
	{
		_bgPanel.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		_bgPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLobbyRoomBg());
		_leftPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(true));
		_rightPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(false));
		_playersPanel.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		_playersPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLobbyRoomMainPanel());
		_briefingPanel.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		_briefingPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLobbyRoomSecondPanel());
		_chatPanel.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		_chatPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLobbyRoomChatPanel());


		UIStyle.ApplyTitle(_lobbyTitle, "LOBBY" + (Lobby.IsSinglePlayer ? " (Offline / Single Player)" : ""), 36);
		UIStyle.ApplyTitle(_playersTitle, TranslationServer.Translate("TEAMS & PLAYERS"), 20);
		UIStyle.ApplyTitle(_briefingTitle, TranslationServer.Translate("MATCH INFO & BRIEFING"), 20);

		var playersVBox = GetNodeOrNull<VBoxContainer>("PlayersPanel/VBoxContainer");
		if (playersVBox != null)
		{
			playersVBox.AddThemeConstantOverride("separation", 16);
		}

		_briefingLabel.AddThemeColorOverride("default_color", new Color(0.85f, 0.85f, 0.9f));
		_briefingLabel.ScrollActive = true;
		_briefingLabel.ScrollFollowing = false;


		string[] headers =
		{
			"PlayersPanel/VBoxContainer/TableHeader/ReadyCol",
			"PlayersPanel/VBoxContainer/TableHeader/TeamCol",
			"PlayersPanel/VBoxContainer/TableHeader/ColorCol",
			"PlayersPanel/VBoxContainer/TableHeader/NameCol",
			"PlayersPanel/VBoxContainer/TableHeader/FactionCol"
		};
		foreach (var path in headers)
		{
			var lbl = GetNodeOrNull<Label>(path);
			if (lbl != null)
			{
				lbl.Text = Tr(lbl.Text);
				lbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
				lbl.AddThemeFontSizeOverride("font_size", 15);
			}
		}

		ApplyTableHeaderLayout();
		UpdateFactionColumnVisibility();


		_inviteFeedback.Modulate = new Color(1, 1, 1, 0);


		SetupLobbyButton(_backButton, TranslationServer.Translate(" LEAVE LOBBY"), ReturnFromLobby);
		SetupLobbyButton(_inviteButton, TranslationServer.Translate("INVITE FRIEND"), OpenInviteFriendsDialog);
		SetupLobbyButton(_addBotButton, TranslationServer.Translate("ADD AI BOT"), () => Lobby.AddAIBot());
		SetupStartButton();

		_mapNameLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_mapNameLabel.AddThemeFontSizeOverride("font_size", 18);
		_mapNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_mapNameLabel.VerticalAlignment = VerticalAlignment.Center;

		_mapNameLabel.AddThemeStyleboxOverride("normal", UIStyle.CreateLobbyMapNamePlate());


		_chatLog.AddThemeStyleboxOverride("normal", UIStyle.CreateLobbyChatLogPlate());

		_chatInput.AddThemeStyleboxOverride("normal", UIStyle.CreateLobbyChatInput(false));
		_chatInput.AddThemeStyleboxOverride("focus", UIStyle.CreateLobbyChatInput(true));
		_chatInput.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));


		PopulateRunicPillar(GetNode<VBoxContainer>("LeftPillar/RuneContainer"));
		PopulateRunicPillar(GetNode<VBoxContainer>("RightPillar/RuneContainer"));
	}

	/// <summary>
	///     Leaves this lobby for the screen that led here: a hosted lobby is one of several in the lobby browser, while a
	///     single-player lobby was opened straight from the main menu and has no browser behind it.
	/// </summary>
	private static void ReturnFromLobby()
	{
		bool singlePlayer = Lobby.IsSinglePlayer;
		Lobby.Disconnect();
		if (singlePlayer) Lobby.IsSinglePlayer = false;
		GameContext.UserInterface?.TransitionTo(singlePlayer ? GameScreen.MainMenu : GameScreen.LobbyBrowser);
	}

	private void ApplyTableHeaderLayout()
	{
		var header = GetNodeOrNull<HBoxContainer>("PlayersPanel/VBoxContainer/TableHeader");
		if (header == null) return;

		header.AddThemeConstantOverride("separation", RowColumnSeparation);

		if (header.GetNodeOrNull<Control>("HeaderInset") == null)
		{
			var inset = new Control();
			inset.Name = "HeaderInset";
			inset.CustomMinimumSize = new Vector2(RowContentInset, 0);
			header.AddChild(inset);
			header.MoveChild(inset, 0);
		}

		ApplyHeaderColumnWidth("PlayersPanel/VBoxContainer/TableHeader/ReadyCol", ReadyColumnWidth);
		ApplyHeaderColumnWidth("PlayersPanel/VBoxContainer/TableHeader/TeamCol", TeamColumnWidth);
		ApplyHeaderColumnWidth("PlayersPanel/VBoxContainer/TableHeader/ColorCol", ColorColumnWidth);
		ApplyHeaderColumnWidth("PlayersPanel/VBoxContainer/TableHeader/NameCol", NameColumnWidth);
		ApplyHeaderColumnWidth("PlayersPanel/VBoxContainer/TableHeader/FactionCol", FactionColumnWidth);
		ApplyDifficultyHeader(header);
	}

	private void ApplyDifficultyHeader(HBoxContainer header)
	{
		var lblDifficulty = header.GetNodeOrNull<Label>("DifficultyCol");
		if (lblDifficulty == null)
		{
			lblDifficulty = new Label();
			lblDifficulty.Name = "DifficultyCol";
			lblDifficulty.Text = TranslationServer.Translate("DIFFICULTY");
			lblDifficulty.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			lblDifficulty.AddThemeFontSizeOverride("font_size", 15);
			header.AddChild(lblDifficulty);
		}

		lblDifficulty.ClipText = true;
		lblDifficulty.CustomMinimumSize = new Vector2(DifficultyColumnWidth, 0);
	}

	private void ApplyHeaderColumnWidth(string path, int width)
	{
		var lbl = GetNodeOrNull<Label>(path);
		if (lbl == null) return;

		lbl.ClipText = true;
		lbl.CustomMinimumSize = new Vector2(width, 0);
	}

	private void UpdateFactionColumnVisibility()
	{
		var factionHeader = GetNodeOrNull<Control>("PlayersPanel/VBoxContainer/TableHeader/FactionCol");
		if (factionHeader != null) factionHeader.Visible = HasFactions;

		foreach (var row in GetExistingPlayerRows().Values)
		{
			var factionCell = row.GetNodeOrNull<Control>("HBox_HBoxContainer/FactionCell");
			if (factionCell != null) factionCell.Visible = HasFactions;
		}
	}
}
