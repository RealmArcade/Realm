// The map picker overlay: the grid of tiles, the tag/search filters behind it, the thumbnail load,
// and the version list for whichever map is selected. Kept out of the lobby flow because it is a
// self-contained browser that only reports a selection back.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Shared;
using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Utils;
using Realm.Client.Services;


namespace Realm.Client.UI;

public partial class LobbyCreate
{
	private void ShowMapPicker()
	{
		var overlay = new Panel();
		overlay.Name = "MapPickerOverlay";
		overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		overlay.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		overlay.ZIndex = 1150;
		AddChild(overlay);
		_mapPickerOverlay = overlay;

		var center = new CenterContainer();
		center.Name = "MapPickerCenter";
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		overlay.AddChild(center);

		var panel = new PanelContainer();
		panel.Name = "MapPickerPanel";
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.CustomMinimumSize = new Vector2(1060, 640);
		center.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.Name = "MapPickerVBox";
		vbox.AddThemeConstantOverride("separation", 12);
		panel.AddChild(vbox);

		var titleLabel = new Label();
		titleLabel.Name = "MapPickerTitle_Label";
		UIStyle.ApplyTitle(titleLabel, TranslationServer.Translate("MAP LIBRARY"), 22);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(titleLabel);

		var searchBar = new LineEdit();
		searchBar.Name = "MapPickerSearch_LineEdit";
		searchBar.PlaceholderText = TranslationServer.Translate("Search maps by name...");
		searchBar.ClearButtonEnabled = true;
		searchBar.CustomMinimumSize = new Vector2(1000, 44);
		searchBar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		searchBar.AddThemeStyleboxOverride("normal", UIStyle.CreateCustomLobbySearchInput(false));
		searchBar.AddThemeStyleboxOverride("focus", UIStyle.CreateCustomLobbySearchInput(true));
		searchBar.TextChanged += _ => RefreshMapPickerGrid();
		vbox.AddChild(searchBar);
		_mapPickerSearch = searchBar;

		var filterRow = new HFlowContainer();
		filterRow.Name = "MapPickerFilters_HFlowContainer";
		filterRow.AddThemeConstantOverride("h_separation", 18);
		filterRow.AddThemeConstantOverride("v_separation", 6);
		vbox.AddChild(filterRow);

		_mapPickerTagChecks.Clear();
		foreach (string tag in MapTagCatalog.MapFilters)
		{
			var tagCheckBox = new CheckBox();
			tagCheckBox.Name = "MapPickerTag_" + tag.Replace(" ", string.Empty);
			tagCheckBox.Text = MapTagCatalog.GetDisplayName(tag);
			tagCheckBox.FocusMode = FocusModeEnum.None;
			tagCheckBox.Pressed += () =>
			{
				GameContext.UserInterface?.PlayClickSound();
				RefreshMapPickerGrid();
			};
			tagCheckBox.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();

			if (string.Equals(tag, MapTagCatalog.AssetPackTag, StringComparison.Ordinal))
			{
				tagCheckBox.TooltipText = TranslationServer.Translate("Also list asset packs, which carry shared assets and cannot be hosted on their own");
			}

			UIStyle.ApplyCheckboxStyle(tagCheckBox);
			filterRow.AddChild(tagCheckBox);
			_mapPickerTagChecks[tag] = tagCheckBox;
		}

		var scroll = new ScrollContainer();
		scroll.Name = "MapPickerScroll_ScrollContainer";
		scroll.CustomMinimumSize = new Vector2(1000, 460);
		scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		vbox.AddChild(scroll);

		var grid = new GridContainer();
		grid.Name = "MapPickerGrid_GridContainer";
		grid.Columns = 3;
		grid.AddThemeConstantOverride("h_separation", 12);
		grid.AddThemeConstantOverride("v_separation", 10);
		grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.AddChild(grid);
		_mapPickerGrid = grid;

		var emptyLabel = new Label();
		emptyLabel.Name = "MapPickerEmpty_Label";
		emptyLabel.Text = TranslationServer.Translate("No map matches the search and filters.");
		emptyLabel.Visible = false;
		emptyLabel.HorizontalAlignment = HorizontalAlignment.Center;
		emptyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		emptyLabel.CustomMinimumSize = new Vector2(1000, 0);
		emptyLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		emptyLabel.AddThemeFontSizeOverride("font_size", 14);
		emptyLabel.AddThemeColorOverride("font_color", new Color(0.88f, 0.9f, 0.95f));
		vbox.AddChild(emptyLabel);
		_mapPickerEmptyLabel = emptyLabel;

		var footer = new HBoxContainer();
		footer.Name = "MapPickerFooter_HBoxContainer";
		footer.Alignment = BoxContainer.AlignmentMode.Center;
		footer.AddThemeConstantOverride("separation", 16);
		vbox.AddChild(footer);

		var importButton = new Button();
		importButton.Name = "MapPickerImportButton";
		importButton.CustomMinimumSize = new Vector2(260, 44);
		importButton.FocusMode = FocusModeEnum.None;
		importButton.MouseFilter = MouseFilterEnum.Stop;
		importButton.AddThemeConstantOverride("icon_max_width", 24);
		UIStyle.ApplyButtonText(importButton, "📥 " + TranslationServer.Translate("Import Map (.rmap)"), 13);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(importButton);
		importButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		importButton.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.UserInterface?.PromptAndImportMapArchive((mapTitle, mapVersion) =>
			{
				RefreshAvailableMaps(mapTitle, mapVersion);
				CloseMapPicker();
			});
		};
		footer.AddChild(importButton);

		var closeButton = new Button();
		closeButton.Name = "MapPickerCloseButton";
		closeButton.CustomMinimumSize = new Vector2(200, 44);
		closeButton.FocusMode = FocusModeEnum.None;
		closeButton.AddThemeConstantOverride("icon_max_width", 0);
		UIStyle.ApplyButtonText(closeButton, TranslationServer.Translate("CLOSE"), 15);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(closeButton);
		closeButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		closeButton.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			CloseMapPicker();
		};
		footer.AddChild(closeButton);

		RefreshMapPickerGrid();
	}

	private void CloseMapPicker()
	{
		_mapPickerSearch = null;
		_mapPickerGrid = null;
		_mapPickerEmptyLabel = null;
		_mapPickerTagChecks.Clear();

		if (_mapPickerOverlay != null && GodotObject.IsInstanceValid(_mapPickerOverlay))
		{
			_mapPickerOverlay.QueueFree();
		}

		_mapPickerOverlay = null;
	}

	private void RefreshMapPickerGrid()
	{
		if (_mapPickerGrid == null || !GodotObject.IsInstanceValid(_mapPickerGrid)) return;

		foreach (Node child in _mapPickerGrid.GetChildren())
		{
			_mapPickerGrid.RemoveChild(child);
			child.QueueFree();
		}

		string searchText = _mapPickerSearch != null && GodotObject.IsInstanceValid(_mapPickerSearch) ? _mapPickerSearch.Text.Trim() : string.Empty;
		bool includeAssetPacks = IsMapPickerTagChecked(MapTagCatalog.AssetPackTag);

		var requiredTags = new List<string>();
		foreach (string tag in MapTagCatalog.MapFilters)
		{
			if (string.Equals(tag, MapTagCatalog.AssetPackTag, StringComparison.Ordinal)) continue;
			if (IsMapPickerTagChecked(tag)) requiredTags.Add(tag);
		}

		int shownMaps = 0;
		for (int i = 0; i < _availableMaps.Count; i++)
		{
			var map = _availableMaps[i];

			if (!includeAssetPacks && IsAssetPackMap(map)) continue;
			if (searchText.Length > 0 && !MatchesSearchText(map, searchText)) continue;
			if (requiredTags.Count > 0 && !HasAnyTag(map, requiredTags)) continue;

			_mapPickerGrid.AddChild(CreateMapPickerTile(map, i));
			shownMaps++;
		}

		if (_mapPickerEmptyLabel != null && GodotObject.IsInstanceValid(_mapPickerEmptyLabel))
		{
			_mapPickerEmptyLabel.Visible = shownMaps == 0;
		}
	}

	private bool IsMapPickerTagChecked(string tag)
	{
		return _mapPickerTagChecks.TryGetValue(tag, out var tagCheckBox) &&
		       GodotObject.IsInstanceValid(tagCheckBox) &&
		       tagCheckBox.ButtonPressed;
	}

	private static bool MatchesSearchText(MapBriefingDetails map, string searchText)
	{
		if (map.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase)) return true;
		if (map.PathName.Contains(searchText, StringComparison.OrdinalIgnoreCase)) return true;
		if (map.Tags == null) return false;

		foreach (string mapTag in map.Tags)
		{
			if (!string.IsNullOrWhiteSpace(mapTag) && mapTag.Contains(searchText, StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
	}

	private static bool HasAnyTag(MapBriefingDetails map, List<string> requiredTags)
	{
		if (map.Tags == null) return false;

		foreach (string mapTag in map.Tags)
		{
			if (string.IsNullOrWhiteSpace(mapTag)) continue;

			foreach (string requiredTag in requiredTags)
			{
				if (string.Equals(mapTag.Trim(), requiredTag, StringComparison.OrdinalIgnoreCase)) return true;
			}
		}

		return false;
	}

	private Button CreateMapPickerTile(MapBriefingDetails map, int mapIndex)
	{
		var tile = new Button();
		tile.Name = "MapPickerTile_" + map.PathName;
		tile.Text = map.DisplayName;
		tile.TooltipText = DescribeMapForPicker(map);
		tile.CustomMinimumSize = new Vector2(320, 44);
		tile.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		tile.ClipText = true;
		tile.FocusMode = FocusModeEnum.None;
		tile.AddThemeConstantOverride("icon_max_width", 0);
		tile.AddThemeStyleboxOverride("normal", UIStyle.CreateDropdownStyle(false, false));
		tile.AddThemeStyleboxOverride("hover", UIStyle.CreateDropdownStyle(true, false));
		tile.AddThemeStyleboxOverride("pressed", UIStyle.CreateDropdownStyle(false, true));
		tile.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		tile.AddThemeColorOverride("font_color", mapIndex == _selectedMapIndex ? UIStyle.ColorCyanGlow : UIStyle.ColorGoldDull);
		tile.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		tile.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
		tile.AddThemeFontSizeOverride("font_size", 15);
		tile.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		tile.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			SelectMap(mapIndex);
			CloseMapPicker();
		};

		return tile;
	}

	private static string DescribeMapForPicker(MapBriefingDetails map)
	{
		string version = string.IsNullOrWhiteSpace(map.Version) ? "1.0.0" : map.Version.TrimStart('v', 'V');
		string description = $"{map.DisplayName}\n{string.Format(TranslationServer.Translate("Version: {0}"), version)}";

		if (map.Tags != null)
		{
			var shownTags = new List<string>();
			foreach (string mapTag in map.Tags)
			{
				if (!string.IsNullOrWhiteSpace(mapTag)) shownTags.Add(mapTag.Trim());
			}

			if (shownTags.Count > 0) description += $"\n{string.Join(", ", shownTags)}";
		}

		return description;
	}

	private string GetMapThumbnailPath(MapBriefingDetails selectedMap)
	{
		if (!string.IsNullOrEmpty(selectedMap.ThumbnailPath) && File.Exists(selectedMap.ThumbnailPath))
		{
			return selectedMap.ThumbnailPath;
		}

		return MapInfoHelper.FindThumbnailForMap(selectedMap.PathName, selectedMap.Version);
	}

	private void LoadThumbnailTexture(string thumbPath)
	{
		if (string.IsNullOrEmpty(thumbPath) || !File.Exists(thumbPath))
		{
			_mapThumbnail.Texture = UIStyle.EmptyBlackTexture;
			return;
		}

		try
		{
			var img = Image.LoadFromFile(thumbPath);
			if (img != null && !img.IsEmpty())
			{
				_mapThumbnail.Texture = ImageTexture.CreateFromImage(img);
				_mapThumbnail.Modulate = Colors.White;
			}
			else
			{
				_mapThumbnail.Texture = UIStyle.EmptyBlackTexture;
			}
		}
		catch
		{
			_mapThumbnail.Texture = UIStyle.EmptyBlackTexture;
		}
	}

	private void RefreshVersionsForSelectedMap(MapBriefingDetails selectedMap, string? targetVersion = null)
	{
		_availableVersions = MapInfoHelper.GetDownloadedVersionsForMap(selectedMap.PathName, selectedMap.DisplayName);
		_versionSelectButton.Clear();
		int selectedVersionIdx = 0;

		for (int i = 0; i < _availableVersions.Count; i++)
		{
			string ver = _availableVersions[i];
			string label = !string.IsNullOrWhiteSpace(ver) ? $"v{ver.TrimStart('v', 'V')}" : "v1.0.0";
			_versionSelectButton.AddItem(label, i);

			if (!string.IsNullOrEmpty(targetVersion))
			{
				string cleanTarget = targetVersion.TrimStart('v', 'V');
				if (ver.Contains(cleanTarget, StringComparison.OrdinalIgnoreCase) || label.Contains(cleanTarget, StringComparison.OrdinalIgnoreCase))
				{
					selectedVersionIdx = i;
				}
			}
		}

		if (_versionSelectButton.ItemCount > 0)
		{
			_versionSelectButton.Selected = selectedVersionIdx;
		}
	}

}