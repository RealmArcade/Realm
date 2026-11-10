using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Services;
using Realm.Shared.Audio;

namespace Realm.Client.UI.MapEditor;

public partial class AssetBrowserDialog
{
	private void RefreshFolderChips()
	{
		foreach (var child in _folderChipsContainer.GetChildren())
		{
			child.QueueFree();
		}

		_optDirectoryFilter.Clear();
		_optDirectoryFilter.AddItem(TranslationServer.Translate("All Indexed Folders & Maps"), 0);
		_optDirectoryFilter.SetItemMetadata(0, "all");

		int currentItemIndex = 1;
		int selectedIndex = 0;

		selectedIndex = PopulateMapGroupsFilter(ref currentItemIndex, selectedIndex);
		selectedIndex = PopulateIndexedDirsFilter(ref currentItemIndex, selectedIndex);

		if (selectedIndex == 0 && (_selectedDirectoryFilter != null || _selectedMapNameFilter != null))
		{
			_selectedDirectoryFilter = null;
			_selectedMapNameFilter = null;
			_selectedMapVersionFilter = null;
		}

		_optDirectoryFilter.Selected = selectedIndex;
		if (_folderScroll != null)
		{
			_folderScroll.Visible = _folderChipsContainer.GetChildCount() > 0;
		}
	}

	private int PopulateMapGroupsFilter(ref int currentItemIndex, int selectedIndex)
	{
		var downloadedPackages = GameContext.AssetIndexService.GetDownloadedMapPackages();
		var mapGroups = downloadedPackages
			.GroupBy(p => p.MapName, StringComparer.OrdinalIgnoreCase)
			.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

		foreach (var group in mapGroups)
		{
			string mapName = group.Key;
			var sortedVersions = group.OrderBy(p => p.MapVersion, StringComparer.OrdinalIgnoreCase).ToList();
			sortedVersions.Sort((a, b) => AssetIndexService.CompareVersions(b.MapVersion, a.MapVersion));

			bool isFirst = true;
			foreach (var pkg in sortedVersions)
			{
				string ver = pkg.MapVersion;
				string label = isFirst
					? $"📦 [Map] {mapName} ({ver} - {TranslationServer.Translate("Latest")})"
					: $"📦 [Map] {mapName} ({ver})";
				string metaKey = $"map:{mapName}|{ver}";

				_optDirectoryFilter.AddItem(label, currentItemIndex);
				_optDirectoryFilter.SetItemMetadata(currentItemIndex, metaKey);

				if (IsMapFilterSelected(mapName, ver, isFirst))
				{
					selectedIndex = currentItemIndex;
				}

				currentItemIndex++;
				isFirst = false;
			}
		}

		return selectedIndex;
	}

	private bool IsMapFilterSelected(string mapName, string ver, bool isFirst)
	{
		if (string.IsNullOrEmpty(_selectedMapNameFilter)) return false;
		if (!string.Equals(mapName, _selectedMapNameFilter, StringComparison.OrdinalIgnoreCase)) return false;

		if (string.IsNullOrEmpty(_selectedMapVersionFilter)) return true;
		if (string.Equals(_selectedMapVersionFilter, "latest", StringComparison.OrdinalIgnoreCase) && isFirst) return true;
		if (string.Equals(_selectedMapVersionFilter, ver, StringComparison.OrdinalIgnoreCase)) return true;

		return false;
	}

	private int PopulateIndexedDirsFilter(ref int currentItemIndex, int selectedIndex)
	{
		var indexedDirs = GameContext.AssetIndexService.GetIndexedDirectories();

		for (int i = 0; i < indexedDirs.Count; i++)
		{
			string dirPath = indexedDirs[i];
			if (string.Equals(dirPath, AssetIndexService.GlobalCasAssetsDirectory, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			int itemIdx = currentItemIndex++;
			selectedIndex = AddIndexedDirItem(dirPath, itemIdx, selectedIndex);
			AddIndexedDirChip(dirPath);
		}

		return selectedIndex;
	}

	private int AddIndexedDirItem(string dirPath, int itemIdx, int selectedIndex)
	{
		string folderName = Path.GetFileName(dirPath.TrimEnd('/', '\\'));
		if (string.IsNullOrEmpty(folderName))
		{
			folderName = dirPath;
		}

		bool isIndexing = GameContext.AssetIndexService.IsDirectoryIndexing(dirPath);
		string metaKey = $"dir:{dirPath}";
		_optDirectoryFilter.AddItem(isIndexing ? $"⏳ {folderName} ({TranslationServer.Translate("Indexing...")})" : folderName, itemIdx);
		_optDirectoryFilter.SetItemMetadata(itemIdx, metaKey);

		if (string.IsNullOrEmpty(_selectedMapNameFilter) && !string.IsNullOrEmpty(_selectedDirectoryFilter) && string.Equals(dirPath, _selectedDirectoryFilter, StringComparison.OrdinalIgnoreCase))
		{
			return itemIdx;
		}

		return selectedIndex;
	}

	private void AddIndexedDirChip(string dirPath)
	{
		string folderName = Path.GetFileName(dirPath.TrimEnd('/', '\\'));
		if (string.IsNullOrEmpty(folderName))
		{
			folderName = dirPath;
		}

		bool isIndexing = GameContext.AssetIndexService.IsDirectoryIndexing(dirPath);
		var chip = new PanelContainer();
		chip.Name = "Chip_PanelContainer";
		var chipStyle = UIStyle.CreateCardPlate(
			isIndexing ? new Color(0.18f, 0.16f, 0.12f, 0.9f) : new Color(0.15f, 0.16f, 0.19f, 0.9f),
			isIndexing ? UIStyle.ColorGold : new Color(0.35f, 0.32f, 0.28f, 0.7f),
			1, 3, 6, 4, 2, 2);
		chip.AddThemeStyleboxOverride("panel", chipStyle);
		chip.TooltipText = isIndexing
			? $"{dirPath}\n({TranslationServer.Translate("Indexing in progress...")})"
			: dirPath;

		var chipHBox = new HBoxContainer();
		chipHBox.Name = "ChipHBox_HBoxContainer";
		chipHBox.AddThemeConstantOverride("separation", 4);
		chip.AddChild(chipHBox);

		var lblName = new Label();
		lblName.Name = "LblName_Label";
		lblName.Text = isIndexing ? $"⏳ {folderName}" : $"📁 {folderName}";
		lblName.AddThemeFontSizeOverride("font_size", 10);
		lblName.AddThemeColorOverride("font_color", isIndexing ? UIStyle.ColorCyanGlow : UIStyle.ColorGold);
		chipHBox.AddChild(lblName);

		var btnRemove = UIStyle.CreateBareButton("BtnRemove_Button");
		btnRemove.Text = "✕";
		btnRemove.AddThemeFontSizeOverride("font_size", 9);
		btnRemove.CustomMinimumSize = new Vector2(16, 16);
		btnRemove.FocusMode = FocusModeEnum.None;
		btnRemove.TooltipText = $"{TranslationServer.Translate("Remove folder from index")}: {dirPath}";
		btnRemove.Pressed += () =>
		{
			GameContext.AssetIndexService.RemoveDirectory(dirPath);
			RefreshFolderChips();
			RefreshSearchResults();
		};
		chipHBox.AddChild(btnRemove);

		_folderChipsContainer.AddChild(chip);
	}

	private void RefreshAssetTypeFilterOptions()
	{
		if (_optAssetTypeFilter == null) return;
		_optAssetTypeFilter.Clear();

		var validTypes = GetValidAssetTypes();

		if (validTypes.Count == 0)
		{
			_optAssetTypeFilter.Visible = false;
			_selectedAssetTypeFilter = null;
			return;
		}

		_optAssetTypeFilter.Visible = true;
		_optAssetTypeFilter.AddItem(TranslationServer.Translate("All Types"), 0);
		_optAssetTypeFilter.SetItemMetadata(0, "");

		int selectedIndex = PopulateAssetTypeFilterDropdown(validTypes);

		if (selectedIndex == 0 && !string.IsNullOrEmpty(_selectedAssetTypeFilter))
		{
			_selectedAssetTypeFilter = null;
		}

		_optAssetTypeFilter.Selected = selectedIndex;
	}

	private void OnDirectoryFilterChanged(long index)
	{
		int idx = (int)index;
		if (idx <= 0 || _optDirectoryFilter == null)
		{
			_selectedDirectoryFilter = null;
			_selectedMapNameFilter = null;
			_selectedMapVersionFilter = null;
		}
		else
		{
			string meta = _optDirectoryFilter.GetItemMetadata(idx).AsString();
			if (meta.StartsWith("map:"))
			{
				string payload = meta.Substring(4);
				string[] parts = payload.Split('|');
				_selectedDirectoryFilter = null;
				_selectedMapNameFilter = parts[0];
				_selectedMapVersionFilter = parts.Length > 1 ? parts[1] : "latest";
			}
			else if (meta.StartsWith("dir:"))
			{
				_selectedDirectoryFilter = meta.Substring(4);
				_selectedMapNameFilter = null;
				_selectedMapVersionFilter = null;
			}
			else
			{
				_selectedDirectoryFilter = null;
				_selectedMapNameFilter = null;
				_selectedMapVersionFilter = null;
			}
		}

		RefreshSearchResults();
	}

	private void OnAddFolderPressed()
	{
		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Select Folder to Index"),
			PathUtils.GetProjectRoot(),
			"",
			false,
			DisplayServer.FileDialogMode.OpenDir,
			Array.Empty<string>(),
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
			{
				if (status && selectedPaths.Length > 0)
				{
					string folderPath = selectedPaths[0];
					GameContext.AssetIndexService.AddDirectory(folderPath);
					RefreshFolderChips();
					RefreshSearchResults();
				}
			})
		);

		if (err != Error.Ok)
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Failed to show folder dialog"));
		}
	}
}
