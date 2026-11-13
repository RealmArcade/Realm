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
	private ProgressBar _downloadProgress = null!;
	private Label _downloadLabel = null!;

	private void CreateDownloadProgressUI()
	{
		_downloadLabel = new Label();
		_downloadLabel.Name = "DownloadLabel_Label";
		_downloadLabel.Text = Tr("Checking map package...");
		_downloadLabel.Position = new Vector2(1450, 805);
		_downloadLabel.Size = new Vector2(340, 25);
		_downloadLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_downloadLabel.AddThemeFontSizeOverride("font_size", 14);
		AddChild(_downloadLabel);


		_downloadProgress = new ProgressBar();
		_downloadProgress.Name = "DownloadProgress_ProgressBar";
		_downloadProgress.Position = new Vector2(1450, 835);
		_downloadProgress.Size = new Vector2(340, 24);
		_downloadProgress.ShowPercentage = true;
		_downloadProgress.Value = 0;


		UIStyle.ApplyProgressBar(_downloadProgress);
		_downloadProgress.AddThemeColorOverride("font_color", new Color(1, 1, 1));
		_downloadProgress.AddThemeFontSizeOverride("font_size", 12);
		AddChild(_downloadProgress);

		bool isReady = !string.IsNullOrEmpty(Lobby.ActiveMapName) && MapAssetManager.IsMapDownloaded(Lobby.ActiveMapName);
		if (isReady)
		{
			_downloadProgress.Value = 100.0f;
			_downloadLabel.Text = Tr("Map package ready.");
			_downloadLabel.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		}

		Lobby.MapDownloadProgressChanged += OnMapDownloadProgress;
		Lobby.MapDownloadCompleted += OnMapDownloadCompleted;
		Lobby.MapDownloadFailed += OnMapDownloadFailed;

		SetupStartButton();
	}

	private void OnMapDownloadProgress(float progress)
	{
		if (_downloadProgress != null)
		{
			_downloadProgress.Value = progress * 100.0f;
		}

		if (_downloadLabel != null)
		{
			_downloadLabel.Text = $"{Tr("Downloading map package:")} {Math.Round(progress * 100.0f)}%";
		}
	}

	private void OnMapDownloadCompleted()
	{
		if (_downloadProgress != null)
		{
			_downloadProgress.Value = 100.0f;
		}

		if (_downloadLabel != null)
		{
			_downloadLabel.Text = Tr("Map package ready.");
			_downloadLabel.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		}

		GameContext.Lobby?.ReportLocalMapReadyState(true);
		SetupStartButton();
	}

	private void OnMapDownloadFailed()
	{
		if (_downloadLabel != null)
		{
			_downloadLabel.Text = Tr("Map download failed!");
			_downloadLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.3f, 0.3f));
		}
	}

	private void OnStartButtonMouseEntered() => GameContext.UserInterface?.PlayHoverSound();

	private void CreateSpectatorDelayUI()
	{
		_spectatorDelayCheck = new CheckBox();
		_spectatorDelayCheck.Name = "SpectatorDelayCheck_CheckBox";
		_spectatorDelayCheck.Text = Tr("Spectator Delay (5m)");
		_spectatorDelayCheck.Position = new Vector2(1450, 770);
		_spectatorDelayCheck.Size = new Vector2(340, 40);
		_spectatorDelayCheck.ButtonPressed = Lobby.SpectatorDelay;
		_spectatorDelayCheck.Disabled = !Lobby.IsHost;

		if (_connectingPopup != null && GodotObject.IsInstanceValid(_connectingPopup))
		{
			_spectatorDelayCheck.Visible = false;
		}

		UIStyle.ApplyCheckboxStyle(_spectatorDelayCheck);

		_spectatorDelayCheck.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			Lobby.UpdateSpectatorDelay(_spectatorDelayCheck.ButtonPressed);
		};

		_spectatorDelayCheck.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		AddChild(_spectatorDelayCheck);
	}

	private void OnSpectatorDelayChanged(bool enabled)
	{
		if (_spectatorDelayCheck != null)
		{
			_spectatorDelayCheck.ButtonPressed = enabled;
		}
	}

	/// <summary>
	///     Offers the lobby's Teams Together switch. Every seat reads it, but only the host may change it: a seat that
	///     does not administer the lobby sees the setting its match will play with and cannot touch it.
	/// </summary>
	private void CreateTeamsTogetherUI()
	{
		_teamsTogetherCheck = new CheckBox();
		_teamsTogetherCheck.Name = "TeamsTogetherCheck_CheckBox";
		_teamsTogetherCheck.Text = Tr("Teams Together");
		_teamsTogetherCheck.TooltipText = Tr("Groups each team's players into consecutive player slots, so a side sits together in the match.");
		_teamsTogetherCheck.Position = new Vector2(1450, 720);
		_teamsTogetherCheck.Size = new Vector2(340, 40);
		_teamsTogetherCheck.ButtonPressed = Lobby.TeamsTogether;
		_teamsTogetherCheck.Disabled = !Lobby.IsHost;

		if (_connectingPopup != null && GodotObject.IsInstanceValid(_connectingPopup))
		{
			_teamsTogetherCheck.Visible = false;
		}

		UIStyle.ApplyCheckboxStyle(_teamsTogetherCheck);

		_teamsTogetherCheck.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			Lobby.UpdateTeamsTogether(_teamsTogetherCheck.ButtonPressed);
		};

		_teamsTogetherCheck.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		AddChild(_teamsTogetherCheck);
	}

	private void OnTeamsTogetherChanged(bool enabled)
	{
		if (_teamsTogetherCheck == null) return;
		if (!GodotObject.IsInstanceValid(_teamsTogetherCheck)) return;

		_teamsTogetherCheck.ButtonPressed = enabled;
	}
}
