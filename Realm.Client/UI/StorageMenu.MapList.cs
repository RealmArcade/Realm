// The downloaded-map list and its detail side: refresh from disk, render one card per map, keep the
// selection, and build the version rows for the selected map.

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

public partial class StorageMenu
{
	private void RefreshDownloadedMaps()
	{
		_allDownloadedMaps = GameContext.MapStorageService.GetDownloadedMaps().ToList();
		RenderMapList();

		if (_selectedMap != null)
		{
			var currentSelected = _allDownloadedMaps.FirstOrDefault(m => string.Equals(m.Title, _selectedMap.Title, StringComparison.OrdinalIgnoreCase));
			if (currentSelected != null)
			{
				SelectMap(currentSelected);
			}
			else if (_allDownloadedMaps.Count > 0)
			{
				SelectMap(_allDownloadedMaps[0]);
			}
			else
			{
				ClearSelection();
			}
		}
		else if (_allDownloadedMaps.Count > 0)
		{
			SelectMap(_allDownloadedMaps[0]);
		}
		else
		{
			ClearSelection();
		}
	}

	private void RenderMapList()
	{
		foreach (var child in _mapListContainer.GetChildren())
		{
			child.QueueFree();
		}

		var filteredMaps = string.IsNullOrWhiteSpace(_searchFilter)
			? _allDownloadedMaps
			: _allDownloadedMaps.Where(m =>
				m.Title.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
				m.Author.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
				m.Tags.Any(t => t.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))).ToList();

		_mapCountLabel.Text = string.Format(TranslationServer.Translate("Downloaded Maps ({0})"), filteredMaps.Count);

		if (filteredMaps.Count == 0)
		{
			var emptyLabel = new Label();
			emptyLabel.Name = "EmptyLabel_Label";
			emptyLabel.Text = TranslationServer.Translate("No maps currently downloaded to storage.");
			emptyLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			emptyLabel.AddThemeFontSizeOverride("font_size", 13);
			emptyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			emptyLabel.MouseFilter = MouseFilterEnum.Ignore;
			_mapListContainer.AddChild(emptyLabel);
			return;
		}

		foreach (var map in filteredMaps)
		{
			var mapCard = CreateMapCard(map);
			_mapListContainer.AddChild(mapCard);
		}
	}

	private Button CreateMapCard(DownloadedMapInfo map)
	{
		var btn = new Button();
		btn.Name = "Btn_Button";
		btn.AddThemeConstantOverride("icon_max_width", 0);
		btn.CustomMinimumSize = new Vector2(0, 60);
		btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		btn.FocusMode = FocusModeEnum.None;
		btn.MouseFilter = MouseFilterEnum.Stop;

		bool isSelected = _selectedMap != null && string.Equals(_selectedMap.Title, map.Title, StringComparison.OrdinalIgnoreCase);

		btn.AddThemeStyleboxOverride("normal", CreateCardStyle(isSelected, false));
		btn.AddThemeStyleboxOverride("hover", CreateCardStyle(isSelected, true));
		btn.AddThemeStyleboxOverride("pressed", CreateCardStyle(true, false));

		var hbox = new HBoxContainer();
		hbox.Name = "Hbox_HBoxContainer";
		hbox.SetAnchorsPreset(LayoutPreset.FullRect);
		hbox.AddThemeConstantOverride("margin_left", 8);
		hbox.AddThemeConstantOverride("margin_right", 8);
		hbox.AddThemeConstantOverride("separation", 10);
		hbox.MouseFilter = MouseFilterEnum.Ignore;
		btn.AddChild(hbox);

		var cardVBox = new VBoxContainer();
		cardVBox.Name = "CardVBox_VBoxContainer";
		cardVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		cardVBox.Alignment = BoxContainer.AlignmentMode.Center;
		cardVBox.AddThemeConstantOverride("separation", 2);
		cardVBox.MouseFilter = MouseFilterEnum.Ignore;
		hbox.AddChild(cardVBox);

		var titleLbl = new Label();
		titleLbl.Name = "TitleLbl_Label";
		titleLbl.Text = map.Title;
		titleLbl.AddThemeColorOverride("font_color", isSelected ? UIStyle.ColorGold : new Color(0.95f, 0.95f, 0.95f));
		titleLbl.AddThemeFontSizeOverride("font_size", 14);
		titleLbl.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		titleLbl.MouseFilter = MouseFilterEnum.Ignore;
		cardVBox.AddChild(titleLbl);

		var subLbl = new Label();
		subLbl.Name = "SubLbl_Label";
		string versionsText = map.Versions.Count == 1 ? "1 Version" : $"{map.Versions.Count} Versions";
		subLbl.Text = $"{map.Author} • {versionsText} • {map.FormattedTotalSize}";
		subLbl.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		subLbl.AddThemeFontSizeOverride("font_size", 11);
		subLbl.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		subLbl.MouseFilter = MouseFilterEnum.Ignore;
		cardVBox.AddChild(subLbl);

		btn.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		btn.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			SelectMap(map);
			RenderMapList();
		};

		return btn;
	}

	private void SelectMap(DownloadedMapInfo map)
	{
		_selectedMap = map;
		_emptySelectionLabel.Visible = false;
		_selectedMapContainer.Visible = true;

		_selectedTitleLabel.Text = map.Title;
		_selectedAuthorLabel.Text = $"Author: {map.Author}";
		_selectedGenreLabel.Text = $"Genre: {map.Genre}" + (map.Tags.Count > 0 ? $" • Tags: {string.Join(", ", map.Tags)}" : "");
		_selectedTotalSizeLabel.Text = $"Total Storage: {map.FormattedTotalSize}";
		_selectedDescriptionLabel.Text = !string.IsNullOrWhiteSpace(map.Description) ? map.Description : "No description provided.";

		if (!string.IsNullOrEmpty(map.ThumbnailPath) && File.Exists(map.ThumbnailPath))
		{
			var img = Image.LoadFromFile(map.ThumbnailPath);
			if (img != null && !img.IsEmpty())
			{
				_mapThumbnail.Texture = ImageTexture.CreateFromImage(img);
			}
			else
			{
				_mapThumbnail.Texture = UIStyle.EmptyBlackTexture;
			}
		}
		else
		{
			_mapThumbnail.Texture = UIStyle.EmptyBlackTexture;
		}

		_downloadUpdateButton.Visible = false;
		RenderVersionsList(map);
		CheckForServerUpdatesAsync(map);
	}

	private void ClearSelection()
	{
		_selectedMap = null;
		_emptySelectionLabel.Visible = true;
		_selectedMapContainer.Visible = false;
	}

	private void RenderVersionsList(DownloadedMapInfo map)
	{
		foreach (var child in _versionsListContainer.GetChildren())
		{
			child.QueueFree();
		}

		foreach (var version in map.Versions)
		{
			var versionPanel = CreateVersionRow(map, version);
			_versionsListContainer.AddChild(versionPanel);
		}
	}

	private PanelContainer CreateVersionRow(DownloadedMapInfo map, DownloadedMapVersionInfo version)
	{
		var panel = new PanelContainer();
		panel.Name = "Panel_PanelContainer";
		panel.AddThemeStyleboxOverride("panel", CreateVersionRowStyle());
		panel.CustomMinimumSize = new Vector2(0, 52);
		panel.MouseFilter = MouseFilterEnum.Pass;

		var hbox = new HBoxContainer();
		hbox.Name = "Hbox_HBoxContainer";
		hbox.AddThemeConstantOverride("separation", 12);
		hbox.MouseFilter = MouseFilterEnum.Pass;
		panel.AddChild(hbox);

		var infoVBox = UIStyle.CreateInfoColumn();
		infoVBox.Alignment = BoxContainer.AlignmentMode.Center;
		infoVBox.AddThemeConstantOverride("separation", 2);
		infoVBox.MouseFilter = MouseFilterEnum.Ignore;
		hbox.AddChild(infoVBox);

		var verLabel = new Label();
		verLabel.Name = "VerLabel_Label";
		verLabel.Text = $"Version {version.Version}";
		verLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		verLabel.AddThemeFontSizeOverride("font_size", 14);
		verLabel.MouseFilter = MouseFilterEnum.Ignore;
		infoVBox.AddChild(verLabel);

		var detailsLabel = new Label();
		detailsLabel.Name = "DetailsLabel_Label";
		string shortHash = version.ManifestHash.Length >= 12 ? version.ManifestHash.Substring(0, 12) + "..." : version.ManifestHash;
		detailsLabel.Text = $"BLAKE3: {shortHash}  •  Size: {version.FormattedSize}  •  Modified: {version.LastModified:yyyy-MM-dd HH:mm}";
		detailsLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.8f));
		detailsLabel.AddThemeFontSizeOverride("font_size", 11);
		detailsLabel.MouseFilter = MouseFilterEnum.Ignore;
		infoVBox.AddChild(detailsLabel);

		var openInEditorBtn = new Button();
		openInEditorBtn.Name = "OpenInEditorBtn_Button";
		openInEditorBtn.AddThemeConstantOverride("icon_max_width", 20);
		openInEditorBtn.CustomMinimumSize = new Vector2(140, 36);
		openInEditorBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		UIStyle.ApplyButtonText(openInEditorBtn, "✏️ " + TranslationServer.Translate("OPEN IN EDITOR"), 12);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(openInEditorBtn);
		openInEditorBtn.TooltipText = TranslationServer.Translate("Copy map to editor workspace. Saves will go to Documents/MapName.");
		openInEditorBtn.FocusMode = FocusModeEnum.None;
		openInEditorBtn.MouseFilter = MouseFilterEnum.Stop;
		openInEditorBtn.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		openInEditorBtn.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			OnOpenInEditorPressed(map.Title, version.DirectoryPath);
		};
		hbox.AddChild(openInEditorBtn);

		var exportBtn = new Button();
		exportBtn.Name = "ExportBtn_Button";
		exportBtn.AddThemeConstantOverride("icon_max_width", 20);
		exportBtn.CustomMinimumSize = new Vector2(140, 36);
		exportBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		UIStyle.ApplyButtonText(exportBtn, "📦 " + TranslationServer.Translate("EXPORT MAP"), 12);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(exportBtn);
		exportBtn.TooltipText = TranslationServer.Translate("Export this version as a standalone .rmap package");
		exportBtn.FocusMode = FocusModeEnum.None;
		exportBtn.MouseFilter = MouseFilterEnum.Stop;
		exportBtn.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		exportBtn.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			OnExportMapVersionPressed(map, version, exportBtn);
		};
		hbox.AddChild(exportBtn);

		var deleteBtn = new Button();
		deleteBtn.Name = "DeleteBtn_Button";
		deleteBtn.AddThemeConstantOverride("icon_max_width", 24);
		deleteBtn.CustomMinimumSize = new Vector2(36, 36);
		deleteBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		deleteBtn.Text = "✕";
		deleteBtn.AddThemeColorOverride("font_color", new Color(1.0f, 0.4f, 0.4f));
		deleteBtn.AddThemeStyleboxOverride("normal", CreateDeleteButtonStyle(false));
		deleteBtn.AddThemeStyleboxOverride("hover", CreateDeleteButtonStyle(true));
		deleteBtn.AddThemeStyleboxOverride("pressed", CreateDeleteButtonStyle(false));
		deleteBtn.TooltipText = TranslationServer.Translate("Delete version from CAS");
		deleteBtn.FocusMode = FocusModeEnum.None;
		deleteBtn.MouseFilter = MouseFilterEnum.Stop;
		deleteBtn.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();

		deleteBtn.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			string confirmMsg = string.Format(
				TranslationServer.Translate("Are you sure you want to delete version {0} of {1} from storage?"),
				version.Version,
				map.Title
			);

			GameContext.UserInterface?.ShowConfirmationDialog(
				confirmMsg,
				onConfirm: () =>
				{
					bool deleted = GameContext.MapStorageService.DeleteMapVersion(map.Title, version.Version, version.ManifestHash);
					if (deleted)
					{
						RefreshDownloadedMaps();
					}
				},
				confirmText: TranslationServer.Translate("DELETE"),
				cancelText: TranslationServer.Translate("CANCEL")
			);
		};

		hbox.AddChild(deleteBtn);
		return panel;
	}

}