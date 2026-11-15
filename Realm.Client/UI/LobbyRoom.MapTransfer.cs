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

}
