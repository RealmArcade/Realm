using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Services;
using Realm.Shared.Distribution;
using Environment = System.Environment;
using static Godot.Control;
using static Godot.CanvasItem;

namespace Realm.Client.UI;

[GlobalClass]
public partial class StorageMenu : Control
{
	public override void _EnterTree()
	{
		Realm.Client.Services.GameContext.EnsureServices();
	}

	private Button _backButton = null!;
	private Label _titleLabel = null!;
	private Button _importMapButton = null!;

	private LineEdit _searchBar = null!;
	private Label _mapCountLabel = null!;
	private VBoxContainer _mapListContainer = null!;

	private PanelContainer _rightNavPanel = null!;
	private Label _emptySelectionLabel = null!;
	private VBoxContainer _selectedMapContainer = null!;

	private TextureRect _mapThumbnail = null!;
	private Label _selectedTitleLabel = null!;
	private Label _selectedAuthorLabel = null!;
	private Label _selectedGenreLabel = null!;
	private Label _selectedDescriptionLabel = null!;
	private Label _selectedTotalSizeLabel = null!;
	private Button _downloadUpdateButton = null!;

	private VBoxContainer _versionsListContainer = null!;

	private List<DownloadedMapInfo> _allDownloadedMaps = new();
	private DownloadedMapInfo? _selectedMap;
	private string _searchFilter = string.Empty;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Pass;
		TextureFilter = TextureFilterEnum.LinearWithMipmaps;

		BuildLayout();
		RefreshDownloadedMaps();
	}

	private void OnOpenInEditorPressed(string mapTitle, string casVersionDirectory)
	{
		if (string.IsNullOrEmpty(casVersionDirectory) || !Directory.Exists(casVersionDirectory))
		{
			return;
		}

		string sanitizedTitle = string.IsNullOrWhiteSpace(mapTitle) ? "MyMap" : mapTitle.Trim();
		string defaultSaveFolder = Path.Combine(OS.GetSystemDir(OS.SystemDir.Documents), sanitizedTitle);

		MapEditorHUD.RequestOpenFromCas(casVersionDirectory, defaultSaveFolder);
		GameContext.UserInterface?.TransitionTo(GameScreen.MapEditorHUD);
	}

	// Both are selected from the storage list and neither had a catch: a fault in one was unobserved, and the second
	// leaves the update button disabled, because it disables it before awaiting the download.
	private async void CheckForServerUpdatesAsync(DownloadedMapInfo map)
	{
		try
		{
			await CheckForServerUpdatesCoreAsync(map);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[StorageMenu] Checking '{map.Title}' for a newer server version failed: {exception}");
		}
	}

	private async Task CheckForServerUpdatesCoreAsync(DownloadedMapInfo map)
	{
		var localVersions = map.Versions.Select(v => v.Version).ToList();
		var (hasNewer, newerVer, mapId) = await GameContext.MapStorageService.CheckForNewerVersionAsync(map.Title, localVersions);

		if (hasNewer && !string.IsNullOrEmpty(newerVer) && _selectedMap != null && string.Equals(_selectedMap.Title, map.Title, StringComparison.OrdinalIgnoreCase))
		{
			map.HasServerUpdate = true;
			map.ServerNewerVersion = newerVer;
			map.ServerMapId = mapId ?? map.Title;

			_downloadUpdateButton.Visible = true;
			UIStyle.ApplyButtonText(_downloadUpdateButton, $"⚡ {TranslationServer.Translate("DOWNLOAD UPDATED VERSION")} (v{newerVer})", 14);
		}
	}

	private async void OnDownloadUpdatePressed()
	{
		try
		{
			await DownloadMapUpdateAsync();
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[StorageMenu] Downloading the map update failed: {exception}");
		}
	}

	private async Task DownloadMapUpdateAsync()
	{
		if (_selectedMap == null || string.IsNullOrEmpty(_selectedMap.ServerMapId))
		{
			return;
		}

		_downloadUpdateButton.Disabled = true;
		UIStyle.ApplyButtonText(_downloadUpdateButton, TranslationServer.Translate("DOWNLOADING UPDATE..."), 14);

		string mapId = _selectedMap.ServerMapId;
		bool success = await GameContext.MapStorageService.DownloadUpdatedVersionAsync(
			mapId,
			progressCallback: p => { UIStyle.ApplyButtonText(_downloadUpdateButton, $"{(int)(p * 100)}% {TranslationServer.Translate("DOWNLOADING UPDATE...")}", 14); }
		);

		_downloadUpdateButton.Disabled = false;

		if (success)
		{
			RefreshDownloadedMaps();
		}
		else
		{
			UIStyle.ApplyButtonText(_downloadUpdateButton, "❌ " + TranslationServer.Translate("Update Failed"), 14);
		}
	}

	private void OnImportMapPressed()
	{
		GameContext.UserInterface?.PromptAndImportMapArchive((mapTitle, mapVersion) =>
		{
			RefreshDownloadedMaps();
			if (!string.IsNullOrEmpty(mapTitle))
			{
				var imported = _allDownloadedMaps.FirstOrDefault(m => string.Equals(m.Title, mapTitle, StringComparison.OrdinalIgnoreCase));
				if (imported != null)
				{
					SelectMap(imported);
					RenderMapList();
				}
			}
		});
	}

	/// <summary>The header strip: a fill ruled along its bottom edge only, so it reads as sitting above the list.</summary>
	private static StyleBoxFlat CreateHeaderPanelStyle()
	{
		var style = UIStyle.CreateCardPlate(new Color(0.06f, 0.07f, 0.09f, 0.95f), UIStyle.ColorBronze, 0, 0, 25, 25, 12, 12);
		style.BorderWidthBottom = 2;
		return style;
	}

	private static StyleBoxFlat CreateCardStyle(bool isSelected, bool isHover)
	{
		var style = UIStyle.CreateCardPlate(
			isSelected
				? new Color(0.16f, 0.18f, 0.24f, 0.95f)
				: (isHover ? new Color(0.12f, 0.13f, 0.17f, 0.90f) : new Color(0.09f, 0.10f, 0.13f, 0.85f)),
			isSelected ? UIStyle.ColorGold : (isHover ? UIStyle.ColorCyanGlowDim : new Color(0.25f, 0.25f, 0.30f, 0.7f)),
			1, 4, 10, 10, 6, 6);

		if (isSelected)
		{
			style.BorderWidthLeft = 4;
		}

		return style;
	}

	private static StyleBoxFlat CreateVersionRowStyle()
	{
		return UIStyle.CreateCardPlate(new Color(0.10f, 0.11f, 0.15f, 0.90f), new Color(0.28f, 0.28f, 0.35f, 0.7f), 1, 4, 14, 14, 8, 8);
	}

	private static StyleBoxFlat CreateDeleteButtonStyle(bool isHover)
	{
		return isHover
			? UIStyle.CreateButtonPlate(new Color(0.4f, 0.12f, 0.12f, 0.9f), new Color(0.9f, 0.3f, 0.3f, 1.0f))
			: UIStyle.CreateButtonPlate(new Color(0.25f, 0.08f, 0.08f, 0.8f), new Color(0.5f, 0.2f, 0.2f, 0.8f));
	}
}
