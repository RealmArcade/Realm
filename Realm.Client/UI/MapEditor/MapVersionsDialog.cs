using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Services;
using Realm.Client.Services.MapHistory;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class MapVersionsDialog : FloatingDialogBase
{
	private Label _statusLabel;
	private Label _noticeLabel;
	private VBoxContainer _versionList;
	private Label _storageLabel;
	private LineEdit _searchEdit;
	private HBoxContainer _labelEditorRow;
	private LineEdit _labelEdit;
	private Button _btnCreateVersion;
	private Button _btnToggleAllVersions;
	private bool _isBusy;
	private bool _showAllVersions;
	private string _searchText = string.Empty;
	private string _labelTargetCommitId = string.Empty;
	private IReadOnlyList<MapVersionEntry> _versions = Array.Empty<MapVersionEntry>();

	public MapVersionsDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("MAP VERSIONS"), new Vector2(560, 520))
	{
		Name = "MapVersionsDialog";
		SetFooterCloseOnly(TranslationServer.Translate("CLOSE"));
		BuildControls();
	}

	public override void OpenDialog()
	{
		_showAllVersions = false;
		_searchText = string.Empty;
		if (_searchEdit != null && GodotObject.IsInstanceValid(_searchEdit))
		{
			_searchEdit.Text = string.Empty;
		}

		RefreshStatus();
		RefreshVersions();
		RefreshStorage();
		base.OpenDialog();
	}

	private void BuildControls()
	{
		var scrollBody = CreateScrollBody(380);

		_statusLabel = AddLabel(scrollBody, TranslationServer.Translate("Reading version history..."), 12);
		_statusLabel.Name = "StatusLabel_Label";
		_statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_statusLabel.CustomMinimumSize = new Vector2(420, 0);
		_statusLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		_noticeLabel = AddLabel(scrollBody, string.Empty, 10, new Color(0.95f, 0.78f, 0.45f));
		_noticeLabel.Name = "NoticeLabel_Label";
		_noticeLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_noticeLabel.CustomMinimumSize = new Vector2(420, 0);
		_noticeLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		var actionRow = new HBoxContainer();
		actionRow.Name = "ActionRow_HBoxContainer";
		actionRow.AddThemeConstantOverride("separation", 6);
		scrollBody.AddChild(actionRow);

		_btnCreateVersion = AddButton(actionRow, TranslationServer.Translate("SAVE A VERSION NOW"), () => RequestManualVersion(), TranslationServer.Translate("Creates a version you can return to later"), 11, new Vector2(180, 26));
		_btnCreateVersion.Name = "BtnCreateVersion";

		AddSectionHeader(scrollBody, TranslationServer.Translate("VERSIONS"), UIStyle.ColorGold);

		var searchRow = new HBoxContainer();
		searchRow.Name = "SearchRow_HBoxContainer";
		searchRow.AddThemeConstantOverride("separation", 6);
		scrollBody.AddChild(searchRow);

		_searchEdit = new LineEdit();
		_searchEdit.Name = "SearchEdit_LineEdit";
		_searchEdit.PlaceholderText = TranslationServer.Translate("Search versions by name or date...");
		_searchEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_searchEdit.AddThemeFontSizeOverride("font_size", 11);
		_searchEdit.TextChanged += OnSearchTextChanged;
		searchRow.AddChild(_searchEdit);

		_btnToggleAllVersions = AddButton(searchRow, TranslationServer.Translate("SHOW ALL"), () => ToggleAllVersions(), TranslationServer.Translate("Show every saved version instead of the summary"), 10, new Vector2(96, 24));
		_btnToggleAllVersions.Name = "BtnToggleAllVersions";

		_labelEditorRow = new HBoxContainer();
		_labelEditorRow.Name = "LabelEditorRow_HBoxContainer";
		_labelEditorRow.AddThemeConstantOverride("separation", 6);
		_labelEditorRow.Visible = false;
		scrollBody.AddChild(_labelEditorRow);

		_labelEdit = new LineEdit();
		_labelEdit.Name = "LabelEdit_LineEdit";
		_labelEdit.PlaceholderText = TranslationServer.Translate("Name this version...");
		_labelEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_labelEdit.AddThemeFontSizeOverride("font_size", 11);
		_labelEditorRow.AddChild(_labelEdit);

		var btnSaveLabel = AddButton(_labelEditorRow, TranslationServer.Translate("SAVE NAME"), () => RequestRenameVersion(_labelEdit.Text), TranslationServer.Translate("Names the selected version so you can find it later"), 10, new Vector2(96, 24));
		btnSaveLabel.Name = "BtnSaveLabel";

		var btnClearLabel = AddButton(_labelEditorRow, TranslationServer.Translate("CLEAR"), () => RequestRenameVersion(string.Empty), TranslationServer.Translate("Removes the name from the selected version"), 10, new Vector2(70, 24));
		btnClearLabel.Name = "BtnClearLabel";

		var btnCancelLabel = AddButton(_labelEditorRow, TranslationServer.Translate("CANCEL"), () => HideLabelEditor(), TranslationServer.Translate("Close the name field"), 10, new Vector2(70, 24));
		btnCancelLabel.Name = "BtnCancelLabel";

		_versionList = new VBoxContainer();
		_versionList.Name = "VersionList_VBoxContainer";
		_versionList.AddThemeConstantOverride("separation", 4);
		_versionList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scrollBody.AddChild(_versionList);

		BuildStorageSection(scrollBody);
	}

	private void BuildStorageSection(VBoxContainer scrollBody)
	{
		AddSectionHeader(scrollBody, TranslationServer.Translate("HISTORY SIZE"), new Color(0.7f, 0.85f, 0.7f));

		var storageRow = new HBoxContainer();
		storageRow.Name = "StorageRow_HBoxContainer";
		storageRow.AddThemeConstantOverride("separation", 6);
		scrollBody.AddChild(storageRow);

		_storageLabel = AddLabel(storageRow, TranslationServer.Translate("Measuring history..."), 11, new Color(0.8f, 0.85f, 0.8f));
		_storageLabel.Name = "StorageLabel_Label";
		_storageLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_storageLabel.CustomMinimumSize = new Vector2(260, 0);
		_storageLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
	}

	private static string ResolveEditingDirectory()
	{
		return GameContext.EditorService._editingDirectory;
	}

	private void SetBusy(bool isBusy, string? message = null)
	{
		_isBusy = isBusy;
		if (_btnCreateVersion != null && GodotObject.IsInstanceValid(_btnCreateVersion))
		{
			_btnCreateVersion.Disabled = isBusy;
		}

		if (!string.IsNullOrEmpty(message) && _statusLabel != null && GodotObject.IsInstanceValid(_statusLabel))
		{
			_statusLabel.Text = message;
		}
	}

	private void RefreshStatus()
	{
		string editingDirectory = ResolveEditingDirectory();
		GameContext.MapHistoryService.ReadStatus(editingDirectory, status =>
		{
			Callable.From(() => ApplyStatus(status)).CallDeferred();
		});
	}

	private void ApplyStatus(MapHistoryStatus status)
	{
		if (!GodotObject.IsInstanceValid(this) || !Visible) return;

		if (!status.IsRepositoryReady)
		{
			_statusLabel.Text = string.IsNullOrEmpty(status.ErrorMessage)
				? TranslationServer.Translate("Save the map once to start keeping versions of it.")
				: status.ErrorMessage;
			_noticeLabel.Text = string.Empty;
			return;
		}

		_statusLabel.Text = status.LastVersionAt.HasValue
			? string.Format(
				TranslationServer.Translate("Last saved version: {0}  •  {1} version(s) kept"),
				FormatTimestamp(status.LastVersionAt.Value),
				status.VersionCount)
			: TranslationServer.Translate("No versions recorded yet.");

		_noticeLabel.Text = status.Notice ?? string.Empty;
	}

	private void RefreshVersions()
	{
		ClearChildren(_versionList);
		var loadingLabel = AddLabel(_versionList, TranslationServer.Translate("Reading version history..."), 11, new Color(0.8f, 0.8f, 0.8f));
		loadingLabel.Name = "LoadingLabel_Label";

		string editingDirectory = ResolveEditingDirectory();
		GameContext.MapHistoryService.ReadVersions(editingDirectory, versions =>
		{
			Callable.From(() => PopulateVersions(versions)).CallDeferred();
		});
	}

	private void PopulateVersions(IReadOnlyList<MapVersionEntry> versions)
	{
		if (!GodotObject.IsInstanceValid(this) || _versionList == null || !GodotObject.IsInstanceValid(_versionList)) return;

		_versions = versions;
		RenderVersionRows();
	}

	private void RenderVersionRows()
	{
		if (_versionList == null || !GodotObject.IsInstanceValid(_versionList)) return;

		ClearChildren(_versionList);

		if (_versions.Count == 0)
		{
			AddWrappedLabel(_versionList, TranslationServer.Translate("No versions yet. Saving the map creates the first one."), "EmptyLabel_Label");
			return;
		}

		List<MapVersionEntry> visibleVersions = SelectVisibleVersions();

		if (visibleVersions.Count == 0)
		{
			AddWrappedLabel(_versionList, TranslationServer.Translate("No version matches that search."), "NoMatchLabel_Label");
			return;
		}

		foreach (MapVersionEntry version in visibleVersions)
		{
			BuildVersionRow(version);
		}

		int hiddenCount = _versions.Count - visibleVersions.Count;
		if (hiddenCount > 0)
		{
			AddWrappedLabel(
				_versionList,
				string.Format(TranslationServer.Translate("{0} older version(s) hidden by the summary. Use SHOW ALL or search."), hiddenCount),
				"HiddenVersionsLabel_Label");
		}

		if (_btnToggleAllVersions != null && GodotObject.IsInstanceValid(_btnToggleAllVersions))
		{
			_btnToggleAllVersions.Text = _showAllVersions
				? TranslationServer.Translate("SHOW SUMMARY")
				: TranslationServer.Translate("SHOW ALL");
		}
	}

	private void AddWrappedLabel(VBoxContainer parent, string text, string nodeName)
	{
		var label = AddLabel(parent, text, 10, new Color(0.75f, 0.76f, 0.75f));
		label.Name = nodeName;
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.CustomMinimumSize = new Vector2(420, 0);
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
	}

	private List<MapVersionEntry> SelectVisibleVersions()
	{
		if (!string.IsNullOrWhiteSpace(_searchText))
		{
			string search = _searchText.Trim();
			return _versions.Where(version => MatchesSearch(version, search)).ToList();
		}

		if (_showAllVersions) return _versions.ToList();

		var summary = new List<MapVersionEntry>();
		var seenDays = new HashSet<DateTime>();
		foreach (MapVersionEntry version in _versions)
		{
			bool isNewestInDay = !seenDays.Contains(version.CreatedAt.ToLocalTime().Date);
			bool isLabelled = !string.IsNullOrWhiteSpace(version.Label);
			if (isNewestInDay || isLabelled)
			{
				summary.Add(version);
			}

			seenDays.Add(version.CreatedAt.ToLocalTime().Date);
		}

		MapVersionEntry? oldestVersion = _versions.LastOrDefault();
		if (oldestVersion != null && !summary.Contains(oldestVersion))
		{
			summary.Add(oldestVersion);
		}

		return summary;
	}

	private static bool MatchesSearch(MapVersionEntry version, string search)
	{
		if (version.SaveVersion > 0 && $"#{version.SaveVersion}".Contains(search, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		if (!string.IsNullOrWhiteSpace(version.Label) && version.Label.Contains(search, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		if (DescribeKind(version.Kind).Contains(search, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return version.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm").Contains(search, StringComparison.OrdinalIgnoreCase);
	}

	private void OnSearchTextChanged(string searchText)
	{
		_searchText = searchText ?? string.Empty;
		RenderVersionRows();
	}

	private void ToggleAllVersions()
	{
		_showAllVersions = !_showAllVersions;
		RenderVersionRows();
	}

	private void BuildVersionRow(MapVersionEntry version)
	{
		var row = new HBoxContainer();
		row.Name = "VersionRow_HBoxContainer";
		row.AddThemeConstantOverride("separation", 8);
		row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_versionList.AddChild(row);

		string kindLabel = DescribeKind(version.Kind);
		string sourceSuffix = BuildSourceFolderSuffix(version.SourceFolder);
		string labelPrefix = string.IsNullOrWhiteSpace(version.Label) ? string.Empty : $"{version.Label}  •  ";
		string saveVersionPrefix = version.SaveVersion > 0 ? $"#{version.SaveVersion}  •  " : string.Empty;

		var descriptionLabel = new Label();
		descriptionLabel.Name = "DescriptionLabel_Label";
		descriptionLabel.Text = $"{saveVersionPrefix}{labelPrefix}{FormatTimestamp(version.CreatedAt)}  •  {kindLabel}{sourceSuffix}";
		descriptionLabel.AddThemeFontSizeOverride("font_size", 11);
		descriptionLabel.AddThemeColorOverride("font_color", string.IsNullOrWhiteSpace(version.Label) ? new Color(0.93f, 0.93f, 0.9f) : new Color(0.98f, 0.9f, 0.6f));
		descriptionLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		descriptionLabel.ClipText = false;
		row.AddChild(descriptionLabel);

		var detailLabel = new Label();
		detailLabel.Name = "DetailLabel_Label";
		detailLabel.Text = string.Format(TranslationServer.Translate("{0} file(s)"), version.ChangedFileCount);
		detailLabel.AddThemeFontSizeOverride("font_size", 10);
		detailLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.72f, 0.7f));
		detailLabel.CustomMinimumSize = new Vector2(74, 0);
		detailLabel.HorizontalAlignment = HorizontalAlignment.Right;
		row.AddChild(detailLabel);

		MapVersionEntry capturedVersion = version;
		var labelButton = AddButton(row, TranslationServer.Translate("NAME"), () => ShowLabelEditor(capturedVersion), TranslationServer.Translate("Give this version a name you will recognise"), 10, new Vector2(60, 22));
		labelButton.Name = "BtnLabel";

		var restoreButton = AddButton(row, TranslationServer.Translate("RESTORE"), () => ConfirmRestore(capturedVersion), TranslationServer.Translate("Replace the map with this version"), 10, new Vector2(78, 22));
		restoreButton.Name = "BtnRestore";
	}

	private static string BuildSourceFolderSuffix(string? sourceFolder)
	{
		if (string.IsNullOrWhiteSpace(sourceFolder)) return string.Empty;

		string currentDirectory = ResolveEditingDirectory();
		try
		{
			if (string.Equals(
				    Path.GetFullPath(sourceFolder).TrimEnd(Path.DirectorySeparatorChar),
				    Path.GetFullPath(currentDirectory).TrimEnd(Path.DirectorySeparatorChar),
				    StringComparison.OrdinalIgnoreCase))
			{
				return string.Empty;
			}
		}
		catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
		{
			return string.Empty;
		}

		string folderName = Path.GetFileName(sourceFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		if (string.IsNullOrEmpty(folderName)) return string.Empty;

		const int maximumFolderNameLength = 24;
		if (folderName.Length > maximumFolderNameLength)
		{
			folderName = folderName[..(maximumFolderNameLength - 1)] + "…";
		}

		return $"  ·  {string.Format(TranslationServer.Translate("from {0}"), folderName)}";
	}

	private void ShowLabelEditor(MapVersionEntry version)
	{
		_labelTargetCommitId = version.CommitId;
		_labelEdit.Text = version.Label ?? string.Empty;
		_labelEditorRow.Visible = true;
		_labelEdit.GrabFocus();
		_labelEdit.SelectAll();
	}

	private void HideLabelEditor()
	{
		_labelTargetCommitId = string.Empty;
		_labelEditorRow.Visible = false;
	}

	private void RequestRenameVersion(string label)
	{
		if (_isBusy || string.IsNullOrEmpty(_labelTargetCommitId)) return;

		string editingDirectory = ResolveEditingDirectory();
		string commitId = _labelTargetCommitId;
		GameContext.MapHistoryService.RenameVersion(editingDirectory, commitId, label, result =>
		{
			Callable.From(() =>
			{
				HideLabelEditor();
				Hud.ShowFeedback(result.Success
					? TranslationServer.Translate("Version name saved.")
					: string.Format(TranslationServer.Translate("Could not name that version: {0}"), result.ErrorMessage ?? string.Empty));
				RefreshVersions();
			}).CallDeferred();
		});
	}

	private void RefreshStorage()
	{
		string editingDirectory = ResolveEditingDirectory();
		GameContext.MapHistoryService.ReadStorageUsage(editingDirectory, info =>
		{
			Callable.From(() => ApplyStorageInfo(info)).CallDeferred();
		});
	}

	private void ApplyStorageInfo(MapHistoryStorageInfo info)
	{
		if (_storageLabel == null || !GodotObject.IsInstanceValid(_storageLabel)) return;

		if (!string.IsNullOrEmpty(info.ErrorMessage))
		{
			_storageLabel.Text = info.ErrorMessage;
			return;
		}

		_storageLabel.Text = string.Format(
			TranslationServer.Translate("{0} version(s) use {1}. Packed: {2}."),
			info.VersionCount,
			FormatBytes(info.TotalBytes),
			FormatBytes(info.PackedBytes));
	}

	private void ConfirmRestore(MapVersionEntry version)
	{
		if (_isBusy) return;

		string message = string.Format(
			TranslationServer.Translate("Restore the map to the version from {0}?\n\nYour current map is saved as a new version first, so nothing is lost and you can undo this."),
			FormatTimestamp(version.CreatedAt));

		Hud.ShowConfirmationDialog(
			message,
			() => RequestRestore(version),
			confirmText: TranslationServer.Translate("RESTORE"),
			cancelText: TranslationServer.Translate("CANCEL"));
	}

	private void RequestRestore(MapVersionEntry version)
	{
		if (_isBusy) return;

		string editingDirectory = ResolveEditingDirectory();
		SetBusy(true, TranslationServer.Translate("Restoring version..."));
		Hud.ShowFeedback(TranslationServer.Translate("Restoring version..."));

		GameContext.EditorService.IsSyncing = true;

		GameContext.MapHistoryService.RestoreVersion(editingDirectory, version.CommitId, result =>
		{
			Callable.From(() => CompleteRestore(editingDirectory, result)).CallDeferred();
		});
	}

	private void CompleteRestore(string editingDirectory, MapHistoryRestoreResult result)
	{
		try
		{
			if (!result.Success)
			{
				Hud.ShowFeedback(string.Format(TranslationServer.Translate("Could not restore that version: {0}"), result.ErrorMessage ?? string.Empty));
				return;
			}

			if (result.TerrainRestored && GameContext.Host != null)
			{
				string terrainPath = Path.Combine(editingDirectory, "terrain.json");
				GameContext.Host.LoadMapFromFile(terrainPath, ensureGlbOptimized: false);
			}

			if (result.MetadataRestored)
			{
				Hud.ReadMetadataAndRefreshTextures();
				Hud.ViewLoadMapProperties();
			}

			if (GameContext.Host != null)
			{
				GameContext.Host.EditorHasUnsavedChanges = false;
			}

			Hud.ShowFeedback(string.Format(TranslationServer.Translate("Restored the version from {0}. {1} file(s) restored, {2} removed."), FormatTimestamp(result.NewVersion?.CreatedAt ?? DateTimeOffset.Now), result.FilesWritten, result.FilesRemoved));
			RefreshVersions();
		}
		finally
		{
			GameContext.EditorService.IsSyncing = false;
			SetBusy(false, null);
			RefreshStatus();
			RefreshStorage();
		}
	}

	private void RequestManualVersion()
	{
		if (_isBusy) return;

		string editingDirectory = ResolveEditingDirectory();
		SetBusy(true, TranslationServer.Translate("Saving a version..."));
		GameContext.MapHistoryService.CreateVersion(editingDirectory, MapVersionKind.ManualSave, TranslationServer.Translate("Save point"), result =>
		{
			Callable.From(() =>
			{
				SetBusy(false, null);
				Hud.ShowFeedback(result.Success
					? TranslationServer.Translate("Saved a new version of the map.")
					: string.Format(TranslationServer.Translate("Could not save a version: {0}"), result.ErrorMessage ?? string.Empty));
				RefreshStatus();
				RefreshVersions();
				RefreshStorage();
			}).CallDeferred();
		});
	}

	private static string DescribeKind(MapVersionKind kind)
	{
		return kind switch
		{
			MapVersionKind.Initial => TranslationServer.Translate("First version"),
			MapVersionKind.ManualSave => TranslationServer.Translate("Save"),
			MapVersionKind.AutosaveCheckpoint => TranslationServer.Translate("Autosave"),
			MapVersionKind.BeforePublish => TranslationServer.Translate("Before publish"),
			MapVersionKind.BeforeMapUpgrade => TranslationServer.Translate("Before map upgrade"),
			MapVersionKind.BeforeRestore => TranslationServer.Translate("Before restore"),
			MapVersionKind.RestoredVersion => TranslationServer.Translate("Restored"),
			MapVersionKind.Condensed => TranslationServer.Translate("Condensed earlier versions"),
			_ => TranslationServer.Translate("Change")
		};
	}

	private static string FormatTimestamp(DateTimeOffset timestamp)
	{
		DateTimeOffset localTimestamp = timestamp.ToLocalTime();
		TimeSpan age = DateTimeOffset.Now - localTimestamp;

		if (age.TotalSeconds < 60)
		{
			return string.Format(TranslationServer.Translate("{0} (just now)"), localTimestamp.ToString("yyyy-MM-dd HH:mm"));
		}

		if (age.TotalHours < 1)
		{
			return string.Format(TranslationServer.Translate("{0} ({1} min ago)"), localTimestamp.ToString("yyyy-MM-dd HH:mm"), Math.Max(1, (int)age.TotalMinutes));
		}

		if (age.TotalDays < 1)
		{
			return string.Format(TranslationServer.Translate("{0} ({1} h ago)"), localTimestamp.ToString("yyyy-MM-dd HH:mm"), (int)age.TotalHours);
		}

		return localTimestamp.ToString("yyyy-MM-dd HH:mm");
	}

	private static string FormatBytes(long byteCount)
	{
		if (byteCount < 1024) return string.Format(TranslationServer.Translate("{0} B"), byteCount);
		if (byteCount < 1024 * 1024) return string.Format(TranslationServer.Translate("{0} KB"), (byteCount / 1024.0).ToString("0.#"));
		if (byteCount < 1024L * 1024 * 1024) return string.Format(TranslationServer.Translate("{0} MB"), (byteCount / (1024.0 * 1024)).ToString("0.#"));
		return string.Format(TranslationServer.Translate("{0} GB"), (byteCount / (1024.0 * 1024 * 1024)).ToString("0.##"));
	}

	private static void ClearChildren(Node parent)
	{
		if (parent == null) return;

		foreach (Node child in parent.GetChildren())
		{
			parent.RemoveChild(child);
			child.QueueFree();
		}
	}
}
