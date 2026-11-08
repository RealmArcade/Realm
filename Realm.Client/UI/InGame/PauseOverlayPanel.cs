using Godot;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Utils;

namespace Realm.Client.UI.InGame;

/// <summary>
/// The multiplayer pause overlay: who paused, everyone still connected, and the resume-for-everyone button with its
/// countdown. The panel builds itself on the first pause (there is no reason to hold an invisible modal for a whole
/// match) and re-reads the network service each time it is asked to update.
/// </summary>
public sealed class PauseOverlayPanel
{
	public PanelContainer? Root { get; private set; }

	private Label? _titleLabel;
	private Button? _resumeButton;
	private VBoxContainer? _playerListContainer;

	/// <summary>
	/// Brings the overlay in line with the pause state: shown only while paused, and rebuilt on every update while it is
	/// because the player list changes as people leave.
	/// </summary>
	public void Update(Node owner)
	{
		if (Root == null) Build(owner);
		Root!.Visible = GameContext.NetworkService.IsPaused;
		if (!Root.Visible) return;

		UpdateTitle();
		UpdateResumeButton();

		ClearPlayerList();
		PopulatePlayerList();
	}

	private void Build(Node owner)
	{
		Root = new PanelContainer();
		Root.Name = "PausePanel";

		var style = UIStyle.CreateCardPlate(new Color(0.1f, 0.1f, 0.12f, 0.95f), UIStyle.ColorBronze, 2, 8, 20, 20, 20, 20);
		Root.AddThemeStyleboxOverride("panel", style);

		Root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
		Root.GrowHorizontal = Control.GrowDirection.Both;
		Root.GrowVertical = Control.GrowDirection.Both;

		var mainVBox = new VBoxContainer();
		mainVBox.Name = "MainVBox_VBoxContainer";
		mainVBox.AddThemeConstantOverride("separation", 15);
		Root.AddChild(mainVBox);

		_titleLabel = new Label();
		_titleLabel.Name = "PauseTitleLabel_Label";
		_titleLabel.Text = "Game Paused";
		_titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_titleLabel.AddThemeFontSizeOverride("font_size", 24);
		mainVBox.AddChild(_titleLabel);

		var scroll = new ScrollContainer();
		scroll.Name = "Scroll_ScrollContainer";
		scroll.CustomMinimumSize = new Vector2(450, 150);
		mainVBox.AddChild(scroll);

		_playerListContainer = new VBoxContainer();
		_playerListContainer.Name = "PausePlayerListContainer_VBoxContainer";
		_playerListContainer.AddThemeConstantOverride("separation", 10);
		scroll.AddChild(_playerListContainer);

		_resumeButton = new Button();
		_resumeButton.Name = "PauseResumeButton";
		_resumeButton.Text = TranslationServer.Translate("RESUME FOR EVERYONE");
		_resumeButton.CustomMinimumSize = new Vector2(0, 34);
		UIStyle.ApplyHudButtonStyleboxes(_resumeButton);
		_resumeButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		_resumeButton.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_resumeButton.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		_resumeButton.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.Host?.RequestPause(false);
		};
		mainVBox.AddChild(_resumeButton);

		owner.AddChild(Root);
	}

	/// <summary>
	/// The resume button is held down while the countdown runs so nobody can restart it mid-way; the label counts that
	/// countdown rather than saying "resume".
	/// </summary>
	private void UpdateResumeButton()
	{
		if (_resumeButton == null) return;

		int countdown = GameContext.NetworkService.PauseResumeCountdownSeconds;
		_resumeButton.Disabled = countdown > 0;
		_resumeButton.Text = countdown > 0
			? string.Format(TranslationServer.Translate("RESUMING IN {0}"), countdown)
			: TranslationServer.Translate("RESUME FOR EVERYONE");
	}

	private void UpdateTitle()
	{
		if (_titleLabel == null) return;

		int countdown = GameContext.NetworkService.PauseResumeCountdownSeconds;
		if (countdown > 0)
		{
			_titleLabel.Text = string.Format(TranslationServer.Translate("Resuming in {0}"), countdown);
			return;
		}

		string pausedByName = ResolvePlayerName(GameContext.NetworkService.PausedByPlayerIndex);
		_titleLabel.Text = string.IsNullOrEmpty(pausedByName)
			? TranslationServer.Translate("Game Paused")
			: string.Format(TranslationServer.Translate("Paused by {0}"), pausedByName);
	}

	private void ClearPlayerList()
	{
		if (_playerListContainer == null) return;

		foreach (var child in _playerListContainer.GetChildren()) child.QueueFree();
	}

	/// <summary>
	/// Only the players still in the match are listed; spectators are not paused and would be noise here.
	/// </summary>
	private void PopulatePlayerList()
	{
		if (GameContext.Lobby == null || _playerListContainer == null) return;

		foreach (PlayerInfo player in GameContext.Lobby.PlayerList)
		{
			if (player.Team == "Spectator") continue;

			var row = new HBoxContainer();
			row.Name = "Row_HBoxContainer";
			row.AddThemeConstantOverride("separation", 15);

			var nameLabel = new Label { Text = player.Name, CustomMinimumSize = new Vector2(150, 0) };
			row.AddChild(nameLabel);

			_playerListContainer.AddChild(row);
		}
	}

	private static string ResolvePlayerName(int playerSlot)
	{
		if (playerSlot < 0 || GameContext.Lobby == null) return string.Empty;

		foreach (PlayerInfo player in GameContext.Lobby.PlayerList)
		{
			if (player.Slot == playerSlot) return player.Name;
		}

		return string.Empty;
	}
}
