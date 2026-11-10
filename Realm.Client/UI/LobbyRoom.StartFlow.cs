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
	private void SetupStartButton()
	{
		_startButton.Flat = false;
		ApplyStartButtonStyle();
		ApplyStartButtonState();
	}

	private void ApplyStartButtonStyle()
	{
		var normStyle = UIStyle.CreateButtonNormal();
		if (normStyle is StyleBoxFlat flatNorm)
		{
			flatNorm.BorderColor = UIStyle.ColorCyanGlow;
		}

		var hoverStyle = UIStyle.CreateButtonHover();
		if (hoverStyle is StyleBoxFlat flatHover)
		{
			flatHover.BgColor = new Color(0.1f, 0.5f, 0.9f, 0.15f);
			flatHover.BorderColor = UIStyle.ColorCyanGlow;
			flatHover.ShadowColor = UIStyle.ColorCyanGlow;
			flatHover.ShadowSize = 10;
		}

		var pressedStyle = UIStyle.CreateButtonPressed();
		if (pressedStyle is StyleBoxFlat flatPressed)
		{
			flatPressed.BgColor = new Color(0.1f, 0.5f, 0.9f, 0.3f);
		}

		_startButton.CustomMinimumSize = new Vector2(300, 86);
		UIStyle.ApplyCustomLobbyStartGameButton(_startButton);
	}

	private void ApplyStartButtonState()
	{
		DisconnectStartButtonSignals();

		if (Lobby.IsHost)
		{
			ApplyHostStartButtonState();
		}
		else
		{
			ApplyClientStartButtonState();
		}

		var mouseEnteredCallable = Callable.From(OnStartButtonMouseEntered);
		if (_startButton.IsConnected(Control.SignalName.MouseEntered, mouseEnteredCallable))
		{
			_startButton.Disconnect(Control.SignalName.MouseEntered, mouseEnteredCallable);
		}

		_startButton.MouseEntered += OnStartButtonMouseEntered;
	}

	private void DisconnectStartButtonSignals()
	{
		var pressedCallable = Callable.From(OnStartPressed);
		if (_startButton.IsConnected(Button.SignalName.Pressed, pressedCallable))
		{
			_startButton.Disconnect(Button.SignalName.Pressed, pressedCallable);
		}

		var readyToggleCallable = Callable.From(OnClientReadyTogglePressed);
		if (_startButton.IsConnected(Button.SignalName.Pressed, readyToggleCallable))
		{
			_startButton.Disconnect(Button.SignalName.Pressed, readyToggleCallable);
		}
	}

	private void ApplyHostStartButtonState()
	{
		UIStyle.ApplyButtonText(_startButton, TranslationServer.Translate("START GAME"), 22);
		_startButton.Disabled = false;
		_startButton.Pressed += OnStartPressed;
	}

	private void ApplyClientStartButtonState()
	{
		string activeMap = Lobby.ActiveMapName ?? "";
		bool isMapReady = !string.IsNullOrEmpty(activeMap) && MapAssetManager.IsMapDownloaded(activeMap);
		if (!isMapReady)
		{
			int downloadPercent = Math.Clamp(GetLocalPlayer()?.MapDownloadPercent ?? 0, 0, 100);
			UIStyle.ApplyButtonText(_startButton, $"{Tr("DOWNLOADING MAP...")} {downloadPercent}%", 18);
			_startButton.Disabled = true;
			return;
		}

		var localPlayer = GetLocalPlayer();
		bool isReady = localPlayer != null && localPlayer.IsReady;
		UIStyle.ApplyButtonText(_startButton, isReady ? TranslationServer.Translate("NOT READY") : TranslationServer.Translate("READY"), 22);
		_startButton.Disabled = false;
		_startButton.Pressed += OnClientReadyTogglePressed;
	}

	private void OnClientReadyTogglePressed()
	{
		GameContext.UserInterface?.PlayClickSound();
		var localPlayer = GetLocalPlayer();
		if (localPlayer != null)
		{
			bool newReady = !localPlayer.IsReady;
			localPlayer.IsReady = newReady;
			Lobby.UpdateReadyState(localPlayer.PeerId, newReady);
			SetupStartButton();
		}
	}


	private void ShowConfirmationDialog(string message, Action? onConfirm)
	{
		var overlay = new ColorRect();
		overlay.Name = "Overlay_ColorRect";
		overlay.Color = new Color(0, 0, 0, 0.5f);
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(overlay);

		var panel = new PanelContainer();
		panel.Name = "Panel_PanelContainer";
		panel.CustomMinimumSize = new Vector2(400, 200);
		panel.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		panel.SizeFlagsVertical = SizeFlags.ShrinkCenter;

		var center = UIStyle.CreateCenteredOverlay();
		overlay.AddChild(center);
		center.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.Name = "Vbox_VBoxContainer";
		panel.AddChild(vbox);

		var lblMsg = new Label();
		lblMsg.Name = "LblMsg_Label";
		lblMsg.Text = message;
		lblMsg.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		lblMsg.CustomMinimumSize = new Vector2(400, 0);
		lblMsg.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.AddChild(lblMsg);

		var btnConfirm = new Button();
		btnConfirm.Name = "BtnConfirm_Button";
		btnConfirm.Text = Tr("OK");
		btnConfirm.Pressed += () =>
		{
			overlay.QueueFree();
			onConfirm?.Invoke();
		};
		vbox.AddChild(btnConfirm);
	}

	private void OnStartPressed()
	{
		GameContext.UserInterface?.PlayClickSound();
		_ignoreReadinessOnStart = false;
		string mapName = Lobby.ActiveMapName ?? "melee";

		bool anyDownloading = false;
		foreach (var player in Lobby.PlayerList)
		{
			if (player.PeerId > 1 && !player.IsMapReady)
			{
				anyDownloading = true;
				break;
			}
		}

		if (anyDownloading)
		{
			ShowDownloadingClientsPopup();
			return;
		}

		bool anyLoading = false;
		foreach (var player in Lobby.PlayerList)
		{
			if (player.PeerId > 1 && !player.IsLoadReady)
			{
				anyLoading = true;
				break;
			}
		}

		if (anyLoading)
		{
			ShowLoadingClientsPopup();
			return;
		}

		bool anyNotReady = false;
		foreach (var player in Lobby.PlayerList)
		{
			if (player.IsHost || player.IsDisconnected) continue;
			if (player.PeerId <= 0) continue;
			if (!player.IsReady)
			{
				anyNotReady = true;
				break;
			}
		}

		if (anyNotReady)
		{
			ShowStartWarningPopup(mapName);
		}
		else
		{
			Lobby.StartGame(mapName);
		}
	}

	private void ShowVersionMismatchPopup(string hostVersion, string clientVersion)
	{
		string msg = $"{string.Format(Tr("Host Version: {0}"), hostVersion)}\n{string.Format(Tr("Client Version: {0}"), clientVersion)}\n\n{Tr("Connection aborted.")}";
		GameContext.UserInterface?.ShowPopupMessage(Tr("VERSION MISMATCH"), msg, ReturnFromLobby);
	}

	private void ShowStartWarningPopup(string mapName)
	{
		_ignoreReadinessOnStart = true;

		GameContext.UserInterface?.ShowConfirmationDialog(TranslationServer.Translate("Not all players are ready, start anyway?"), () =>
		{
			Lobby.StartGame(mapName, ignoreReadiness: true);
		}, confirmText: TranslationServer.Translate("START GAME"), cancelText: TranslationServer.Translate("CANCEL"), titleOverride: TranslationServer.Translate("NOT ALL PLAYERS READY"));
	}

	/// <summary>
	///     Shows the warning the lobby's map asked its host to read before the match starts. The authority has not begun
	///     its countdown, so leaving the dialog alone abandons the start and accepting it starts the match the host asked
	///     for, on the reading of readiness it had already chosen.
	/// </summary>
	/// <param name="warning">The text the map wants its host to read.</param>
	private void OnStartWarningReceived(string warning)
	{
		if (!Lobby.IsHost) return;

		string mapName = Lobby.ActiveMapName ?? "melee";
		bool ignoreReadiness = _ignoreReadinessOnStart;

		GameContext.UserInterface?.ShowConfirmationDialog(TranslationServer.Translate(warning), () =>
		{
			Lobby.StartGame(mapName, ignoreReadiness, acknowledgeStartWarning: true);
		}, confirmText: TranslationServer.Translate("START GAME"), cancelText: TranslationServer.Translate("CANCEL"), titleOverride: TranslationServer.Translate("MAP START WARNING"));
	}

	private void ShowDownloadingClientsPopup()
	{
		GameContext.UserInterface?.ShowPopupMessage(Tr("CLIENTS DOWNLOADING MAP"), Tr("Some players are still downloading the map.\nWait for them or Kick them before starting the game."));
	}

	private void ShowLoadingClientsPopup()
	{
		GameContext.UserInterface?.ShowPopupMessage(Tr("CLIENTS LOADING MATCH"), Tr("Some players are still loading the match.\nWait for them or Kick them before starting the game."));
	}
}
