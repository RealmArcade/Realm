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
	private const int MaximumRowsRendered = 200;

	private VBoxContainer _statusBox;
	private Label _statusLabel;
	private Label _backupLabel;
	private VBoxContainer _versionList;
	private VBoxContainer _backupList;
	private Button _btnCreateVersion;
	private Button _btnBackUpNow;
	private Button _btnShowBackups;
	private Button _btnShowVersions;
	private bool _showingBackups;
	private bool _isBusy;

	public MapVersionsDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("MAP VERSIONS"), new Vector2(560, 520))
	{
		Name = "MapVersionsDialog";
		SetFooterCloseOnly("CLOSE");
		BuildControls();
	}

	public override void OpenDialog()
	{
		_showingBackups = false;
		RefreshStatus();
		RefreshVersions();
		base.OpenDialog();
	}

	private void BuildControls()
	{
		var scrollBody = CreateScrollBody(380);

		_statusBox = new VBoxContainer();
		_statusBox.Name = "StatusBox_VBoxContainer";
		_statusBox.AddThemeConstantOverride("separation", 4);
		scrollBody.AddChild(_statusBox);

		_statusLabel = AddLabel(_statusBox, TranslationServer.Translate("Reading version history..."), 12);
		_statusLabel.Name = "StatusLabel_Label";
		_statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_statusLabel.CustomMinimumSize = new Vector2(420, 0);
		_statusLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		_backupLabel = AddLabel(_statusBox, string.Empty, 11, new Color(0.75f, 0.78f, 0.8f));
		_backupLabel.Name = "BackupLabel_Label";
		_backupLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_backupLabel.CustomMinimumSize = new Vector2(420, 0);
		_backupLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		var actionRow = new HBoxContainer();
		actionRow.Name = "ActionRow_HBoxContainer";
		actionRow.AddThemeConstantOverride("separation", 6);
		scrollBody.AddChild(actionRow);

		_btnCreateVersion = AddButton(actionRow, TranslationServer.Translate("SAVE A VERSION NOW"), () => RequestManualVersion(), "Creates a version you can return to later", 11, new Vector2(180, 26));
		_btnCreateVersion.Name = "BtnCreateVersion";

		_btnBackUpNow = AddButton(actionRow, TranslationServer.Translate("BACK UP NOW"), () => RequestBackup(), "Copies the map history somewhere else on this computer", 11, new Vector2(130, 26));
		_btnBackUpNow.Name = "BtnBackUpNow";

		_btnShowBackups = AddButton(actionRow, TranslationServer.Translate("RECOVER FROM BACKUP"), () => ToggleBackups(true), "Restore a deleted map folder from its backup", 11, new Vector2(170, 26));
		_btnShowBackups.Name = "BtnShowBackups";

		_btnShowVersions = AddButton(actionRow, TranslationServer.Translate("BACK TO VERSIONS"), () => ToggleBackups(false), "Return to the version list", 11, new Vector2(150, 26));
		_btnShowVersions.Name = "BtnShowVersions";
		_btnShowVersions.Visible = false;

		AddSectionHeader(scrollBody, TranslationServer.Translate("VERSIONS"), UIStyle.ColorGold);

		_versionList = new VBoxContainer();
		_versionList.Name = "VersionList_VBoxContainer";
		_versionList.AddThemeConstantOverride("separation", 4);
		_versionList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scrollBody.AddChild(_versionList);

		_backupList = new VBoxContainer();
		_backupList.Name = "BackupList_VBoxContainer";
		_backupList.AddThemeConstantOverride("separation", 4);
		_backupList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_backupList.Visible = false;
		scrollBody.AddChild(_backupList);
	}

	private static string ResolveEditingDirectory()
	{
		string workspace = GameContext.EditorService._tempWorkspacePath;
		if (!string.IsNullOrEmpty(workspace))
		{
			return workspace;
		}

		return ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath);
	}

	private void SetBusy(bool isBusy, string? message = null)
	{
		_isBusy = isBusy;
		foreach (Button button in new[] { _btnCreateVersion, _btnBackUpNow, _btnShowBackups })
		{
			if (button != null && GodotObject.IsInstanceValid(button))
			{
				button.Disabled = isBusy;
			}
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
			_backupLabel.Text = string.Empty;
			return;
		}

		_statusLabel.Text = status.LastVersionAt.HasValue
			? string.Format(TranslationServer.Translate("Last saved version: {0}"), FormatTimestamp(status.LastVersionAt.Value))
			: TranslationServer.Translate("No versions recorded yet.");

		if (status.BackupAvailable && status.LastBackupAt.HasValue)
		{
			_backupLabel.Text = string.Format(TranslationServer.Translate("Backup on this computer: {0}"), FormatTimestamp(status.LastBackupAt.Value));
		}
		else if (status.BackupAvailable)
		{
			_backupLabel.Text = TranslationServer.Translate("A backup of this map exists on this computer.");
		}
		else
		{
			_backupLabel.Text = TranslationServer.Translate("No backup of this map yet. Backups protect against losing the map folder.");
		}
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
		if (_showingBackups) return;

		ClearChildren(_versionList);

		if (versions.Count == 0)
		{
			var emptyLabel = AddLabel(_versionList, TranslationServer.Translate("No versions yet. Saving the map creates the first one."), 11, new Color(0.8f, 0.8f, 0.8f));
			emptyLabel.Name = "EmptyLabel_Label";
			emptyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			emptyLabel.CustomMinimumSize = new Vector2(420, 0);
			emptyLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			return;
		}

		int renderedCount = 0;
		foreach (MapVersionEntry version in versions)
		{
			if (renderedCount >= MaximumRowsRendered) break;
			BuildVersionRow(version);
			renderedCount++;
		}

		if (versions.Count > MaximumRowsRendered)
		{
			AddLabel(_versionList, string.Format(TranslationServer.Translate("Showing the newest {0} versions."), MaximumRowsRendered), 10, new Color(0.7f, 0.7f, 0.7f));
		}
	}

	private void BuildVersionRow(MapVersionEntry version)
	{
		var row = new HBoxContainer();
		row.Name = "VersionRow_HBoxContainer";
		row.AddThemeConstantOverride("separation", 8);
		row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_versionList.AddChild(row);

		string kindLabel = DescribeKind(version.Kind);
		string referenceSuffix = string.IsNullOrEmpty(version.ReferenceName) || version.ReferenceName == MapGitRepository.DefaultBranchName
			? string.Empty
			: $"  [{version.ReferenceName}]";

		var descriptionLabel = new Label();
		descriptionLabel.Name = "DescriptionLabel_Label";
		descriptionLabel.Text = $"{FormatTimestamp(version.CreatedAt)}  •  {kindLabel}{referenceSuffix}";
		descriptionLabel.AddThemeFontSizeOverride("font_size", 11);
		descriptionLabel.AddThemeColorOverride("font_color", new Color(0.93f, 0.93f, 0.9f));
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

		var restoreButton = AddButton(row, TranslationServer.Translate("RESTORE"), () => ConfirmRestore(version), "Replace the map with this version", 10, new Vector2(78, 22));
		restoreButton.Name = "BtnRestore";
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

			Hud.ShowFeedback(string.Format(TranslationServer.Translate("Restored the version from {0}. {1} file(s) restored, {2} removed."), FormatTimestamp(result.NewVersion?.CreatedAt ?? DateTimeOffset.Now), result.FilesWritten, result.FilesRemoved));
			EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;
			RefreshVersions();
		}
		finally
		{
			GameContext.EditorService.IsSyncing = false;
				SetBusy(false, null);
			RefreshStatus();
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
			}).CallDeferred();
		});
	}

	private void RequestBackup()
	{
		if (_isBusy) return;

		string editingDirectory = ResolveEditingDirectory();
		SetBusy(true, TranslationServer.Translate("Updating backup..."));
		GameContext.MapHistoryService.BackUpNow(editingDirectory, _ =>
		{
			Callable.From(() =>
			{
				SetBusy(false, null);
				Hud.ShowFeedback(TranslationServer.Translate("The map backup is up to date."));
				RefreshStatus();
			}).CallDeferred();
		});
	}

	private void ToggleBackups(bool showBackups)
	{
		_showingBackups = showBackups;
		_versionList.Visible = !showBackups;
		_backupList.Visible = showBackups;
		_btnShowBackups.Visible = !showBackups;
		_btnShowVersions.Visible = showBackups;

		if (showBackups)
		{
			PopulateBackups();
		}
		else
		{
			RefreshVersions();
		}
	}

	private void PopulateBackups()
	{
		ClearChildren(_backupList);

		var explanation = AddLabel(_backupList, TranslationServer.Translate("Backups are copies of map history kept outside the map folder. Use this if a map folder was deleted or damaged."), 11, new Color(0.8f, 0.8f, 0.8f));
		explanation.Name = "ExplanationLabel_Label";
		explanation.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		explanation.CustomMinimumSize = new Vector2(420, 0);
		explanation.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		var backups = GameContext.MapHistoryService.ListBackupOrigins();
		if (backups.Count == 0)
		{
			AddLabel(_backupList, TranslationServer.Translate("No backups were found on this computer yet."), 11, new Color(0.8f, 0.8f, 0.8f));
			return;
		}

		foreach (var backup in backups.OrderByDescending(entry => entry.LastBackupAt))
		{
			var row = new HBoxContainer();
			row.Name = "BackupRow_HBoxContainer";
			row.AddThemeConstantOverride("separation", 8);
			row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			_backupList.AddChild(row);

			var label = new Label();
			label.Name = "BackupLabel_Label";
			label.Text = $"{backup.FolderName}  •  {FormatTimestamp(backup.LastBackupAt)}";
			label.AddThemeFontSizeOverride("font_size", 11);
			label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			row.AddChild(label);

			string mapGuid = backup.MapGuid;
			string folderName = backup.FolderName;
			var recoverButton = AddButton(row, TranslationServer.Translate("RECOVER TO..."), () => PromptRecoveryDestination(mapGuid, folderName), "Choose a folder to restore this map into", 10, new Vector2(120, 22));
			recoverButton.Name = "BtnRecover";
		}
	}

	private void PromptRecoveryDestination(string mapGuid, string folderName)
	{
		if (_isBusy) return;

		string initialDirectory = Hud.GetInitialDirectory();
		var error = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Choose where to put the recovered map"),
			initialDirectory,
			string.Empty,
			false,
			DisplayServer.FileDialogMode.OpenDir,
			Array.Empty<string>(),
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
			{
				if (!status || selectedPaths.Length == 0) return;

				string destination = Path.Combine(selectedPaths[0], folderName);
				RunRecovery(mapGuid, destination);
			}));

		if (error != Error.Ok)
		{
			Hud.ShowFeedback(TranslationServer.Translate("Could not open the folder picker."));
		}
	}

	private void RunRecovery(string mapGuid, string destinationDirectory)
	{
		SetBusy(true, TranslationServer.Translate("Recovering map from backup..."));
		GameContext.MapHistoryService.RecoverFromOrigin(mapGuid, destinationDirectory, result =>
		{
			Callable.From(() =>
			{
				SetBusy(false, null);
				if (!result.Success)
				{
					Hud.ShowFeedback(string.Format(TranslationServer.Translate("Could not recover the map: {0}"), result.ErrorMessage ?? string.Empty));
					return;
				}

				Hud.ShowFeedback(string.Format(TranslationServer.Translate("Recovered {0} version(s) into {1}. Open that folder to continue working."), result.RecoveredVersionCount, result.RecoveredMapDirectory ?? destinationDirectory));
			}).CallDeferred();
		});
	}

	private static string DescribeKind(MapVersionKind kind)
	{
		return kind switch
		{
			MapVersionKind.Initial => TranslationServer.Translate("First version"),
			MapVersionKind.ManualSave => TranslationServer.Translate("Save"),
			MapVersionKind.SaveAs => TranslationServer.Translate("Save as"),
			MapVersionKind.AutosaveCheckpoint => TranslationServer.Translate("Autosave"),
			MapVersionKind.BeforePublish => TranslationServer.Translate("Before publish"),
			MapVersionKind.BeforeMapUpgrade => TranslationServer.Translate("Before map upgrade"),
			MapVersionKind.BeforeRestore => TranslationServer.Translate("Before restore"),
			MapVersionKind.RestoredVersion => TranslationServer.Translate("Restored"),
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
