using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Services;
using Realm.Shared.Audio;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class AssetBrowserDialog : AssetGridDialogBase
{
	public AssetBrowserDialog()
	{
		CellWidth = 130.0f;
		CellHeight = 150.0f;
		SpacingX = 10.0f;
		SpacingY = 10.0f;
		GridPadding = 10.0f;
	}

	private HBoxContainer _folderChipsContainer = null!;
	private Button _btnAddFolder = null!;
	private Button _btnRescanAll = null!;
	private OptionButton _optDirectoryFilter = null!;
	private LineEdit _txtSearch = null!;
	private Label _lblResultsCount = null!;
	private Label _lblFilterExtensions = null!;

	private ScrollContainer _folderScroll = null!;
	private Label _lblEmptyState = null!;

	private PanelContainer _bottomDetailsPanel = null!;
	private TextureRect _bottomThumbnail = null!;
	private Label _lblSelectedFileName = null!;
	private Label _lblSelectedPath = null!;
	private Label _lblSelectedSize = null!;
	private Button _btnAudioPlayPause = null!;
	private LineEdit _txtTagsEdit = null!;
	private Button _btnEditTags = null!;
	private LineEdit _txtAssetTypeEdit = null!;
	private Button _btnEditAssetType = null!;
	private PanelContainer _gridPanel = null!;

	private readonly AudioStreamPlayer _audioPlayer = null!;

	private readonly List<IndexedAsset> _matchingAssets = new();

	private static bool _hasAutoRescannedOnFirstOpen = false;
	private HashSet<string> _allowedExtensions = new(StringComparer.OrdinalIgnoreCase);
	private bool _requireRealmMetadata = false;
	private string? _selectedAssetTypeFilter;
	private OptionButton _optAssetTypeFilter = null!;
	private CheckBox _chkHasPlayerColorMask = null!;
	private static string? _selectedDirectoryFilter;
	private static string? _selectedMapNameFilter;
	private static string? _selectedMapVersionFilter;
	private IndexedAsset? _selectedAsset;
	private Action<string>? _onAssetSelectedCallback;
	private Action<string, string?>? _onAssetSelectedWithPreferredNameCallback;
	private Action<IReadOnlyList<AssetBrowserSelection>>? _onAssetsSelectedCallback;

	private readonly HashSet<string> _selectedAssetPaths = new(StringComparer.OrdinalIgnoreCase);
	private bool _multiSelectEnabled;
	private Button _btnSelectAll = null!;
	private Button _btnClearSelection = null!;
	private Label _lblSelectionCount = null!;

	public AssetBrowserDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Asset Browser"), new Vector2(780, 640))
	{
		SetUncompressedPanelTexture("res://Assets/UI/map_editor_assets_importer.webp", 30, 45, 70, 70);

		_audioPlayer = new AudioStreamPlayer { Name = "AssetPreviewAudio", Bus = AudioService.PreviewBusName };
		_audioPlayer.Finished += OnAudioFinished;
		AddChild(_audioPlayer);

		BuildControls();
		AssetThumbnailProvider.ThumbnailGenerated += OnThumbnailGenerated;
		GameContext.AssetIndexService.DirectoryIndexingStateChanged += OnDirectoryIndexingStateChanged;
		GameContext.AssetIndexService.DirectoryScanCompleted += OnDirectoryScanCompleted;
	}

	public override void _ExitTree()
	{
		StopAudio();
		AssetThumbnailProvider.ThumbnailGenerated -= OnThumbnailGenerated;
		GameContext.AssetIndexService.DirectoryIndexingStateChanged -= OnDirectoryIndexingStateChanged;
		GameContext.AssetIndexService.DirectoryScanCompleted -= OnDirectoryScanCompleted;
		base._ExitTree();
	}

	public override void CloseDialog()
	{
		StopAudio();
		base.CloseDialog();
	}

	private void OnDirectoryIndexingStateChanged(string dirPath, bool isIndexing)
	{
		Callable.From(() => RefreshFolderChips()).CallDeferred();
	}

	private void OnDirectoryScanCompleted(string dirPath)
	{
		Callable.From(() =>
		{
			RefreshFolderChips();
			RefreshSearchResults();
		}).CallDeferred();
	}

	private void OnThumbnailGenerated(string filePath, Texture2D texture)
	{
		if (_selectedAsset != null)
		{
			string selectedNorm = AssetThumbnailProvider.NormalizePath(_selectedAsset.FilePath);
			string eventNorm = AssetThumbnailProvider.NormalizePath(filePath);
			if (string.Equals(selectedNorm, eventNorm, StringComparison.OrdinalIgnoreCase))
			{
				_bottomThumbnail.Texture = texture;
			}
		}
	}



	public void OpenForImport(string titleText, IEnumerable<string> allowedExtensions, Action<string, string?> onAssetSelected, bool requireRealmMetadata = false, string? requiredAssetType = null)
	{
		OpenForImport(titleText, allowedExtensions, (Action<string>)null!, requireRealmMetadata, requiredAssetType);
		_onAssetSelectedWithPreferredNameCallback = onAssetSelected;
	}

	public void OpenForImport(string titleText, IEnumerable<string> allowedExtensions, Action<IReadOnlyList<AssetBrowserSelection>> onAssetsSelected, bool requireRealmMetadata = false, string? requiredAssetType = null, bool allowMultiSelect = true)
	{
		OpenForImport(titleText, allowedExtensions, (Action<string>)null!, requireRealmMetadata, requiredAssetType);
		_onAssetsSelectedCallback = onAssetsSelected;
		_multiSelectEnabled = allowMultiSelect && onAssetsSelected != null;

		if (_btnSelectAll != null) _btnSelectAll.Visible = _multiSelectEnabled;
		if (_btnClearSelection != null) _btnClearSelection.Visible = _multiSelectEnabled;
		if (_lblSelectionCount != null) _lblSelectionCount.Visible = _multiSelectEnabled;

		UpdateSelectionIndicators();
		UpdateVisibleGridCells();
	}


	private List<string> GetValidAssetTypes()
	{
		return RealmMetadataHelper.GetAssetTypeDisplayOrder(_allowedExtensions);
	}

	private int PopulateAssetTypeFilterDropdown(List<string> validTypes)
	{
		int selectedIndex = 0;
		for (int i = 0; i < validTypes.Count; i++)
		{
			string typeName = validTypes[i];
			int itemIdx = i + 1;
			_optAssetTypeFilter.AddItem(TranslationServer.Translate(typeName), itemIdx);
			_optAssetTypeFilter.SetItemMetadata(itemIdx, typeName);

			if (IsAssetTypeSelected(typeName))
			{
				selectedIndex = itemIdx;
				_selectedAssetTypeFilter = typeName;
			}
		}

		return selectedIndex;
	}

	private bool IsAssetTypeSelected(string typeName)
	{
		if (string.IsNullOrEmpty(_selectedAssetTypeFilter)) return false;

		if (typeName.Equals(_selectedAssetTypeFilter, StringComparison.OrdinalIgnoreCase)) return true;

		var normType = RealmMetadataHelper.NormalizeAssetType(typeName);
		var normSelected = RealmMetadataHelper.NormalizeAssetType(_selectedAssetTypeFilter);
		return normType.HasValue && normSelected.HasValue && normType.Value == normSelected.Value;
	}

	private void OnAssetTypeFilterChanged(long index)
	{
		int idx = (int)index;
		if (idx <= 0 || _optAssetTypeFilter == null)
		{
			_selectedAssetTypeFilter = null;
		}
		else
		{
			_selectedAssetTypeFilter = _optAssetTypeFilter.GetItemMetadata(idx).AsString();
		}

		RefreshSearchResults();
	}


	private void OnRescanAllPressed()
	{
		GameContext.AssetIndexService.RescanAllDirectories();
		Hud?.ShowFeedback(TranslationServer.Translate("Rescanned all indexed directories."));
		RefreshSearchResults();
	}

	private void RefreshSearchResults()
	{
		try
		{
			string searchTerm = _txtSearch.Text?.Trim() ?? string.Empty;
			bool requirePlayerColorMask = _chkHasPlayerColorMask?.ButtonPressed ?? false;
			_matchingAssets.Clear();
			var searchResults = GameContext.AssetIndexService.SearchAssets(
				searchTerm,
				_allowedExtensions,
				_selectedDirectoryFilter,
				_requireRealmMetadata,
				_selectedAssetTypeFilter,
				_selectedMapNameFilter,
				_selectedMapVersionFilter,
				requirePlayerColorMask);

			_matchingAssets.AddRange(searchResults);

			_lblResultsCount.Text = $"{_matchingAssets.Count} {TranslationServer.Translate("items found")}";
			_lblEmptyState.Visible = _matchingAssets.Count == 0;
			UpdateSelectionIndicators();

			if (_selectedAsset != null && !_matchingAssets.Any(a => string.Equals(a.FilePath, _selectedAsset.FilePath, StringComparison.OrdinalIgnoreCase)))
			{
				_selectedAsset = null;
				UpdateSelectedAssetDisplay();
			}

			UpdateVirtualGridSize(_matchingAssets.Count);
			UpdateVisibleGridCells();
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[AssetBrowserDialog] RefreshSearchResults error: {ex.Message}");
		}
	}


	private void UpdateVisibleGridCells()
	{
		if (_scrollContainer == null || !GodotObject.IsInstanceValid(_scrollContainer)) return;
		if (_virtualGridContent == null || !GodotObject.IsInstanceValid(_virtualGridContent)) return;

		float availableWidth = Mathf.Max(100.0f, _scrollContainer.Size.X - GridPadding * 2.0f);
		int columns = Math.Max(1, (int)((availableWidth + SpacingX) / (CellWidth + SpacingX)));
		int totalRows = (_matchingAssets.Count + columns - 1) / columns;
		float totalHeight = _matchingAssets.Count > 0
			? (GridPadding * 2.0f + totalRows * CellHeight + Math.Max(0, totalRows - 1) * SpacingY)
			: 0;

		if (Math.Abs(_virtualGridContent.CustomMinimumSize.Y - totalHeight) > 0.5f)
		{
			_virtualGridContent.CustomMinimumSize = new Vector2(0, totalHeight);
		}

		if (_matchingAssets.Count == 0)
		{
			HideAllGridCells();
			return;
		}

		UpdateGridCellPositions(columns, totalRows);
	}

	private void HideAllGridCells()
	{
		foreach (var cell in _cellPool)
		{
			cell.Visible = false;
		}
	}

	private void UpdateGridCellPositions(int columns, int totalRows)
	{
		float scrollY = (float)_scrollContainer.GetVScrollBar().Value;
		float viewHeight = _scrollContainer.Size.Y;
		if (viewHeight <= 0)
		{
			viewHeight = 320.0f;
		}

		int startRow = Math.Max(0, (int)((scrollY - GridPadding) / (CellHeight + SpacingY)) - 1);
		int endRow = Math.Min(totalRows - 1, (int)((scrollY + viewHeight - GridPadding) / (CellHeight + SpacingY)) + 1);

		int startIndex = Math.Max(0, startRow * columns);
		int endIndex = Math.Min(_matchingAssets.Count - 1, (endRow + 1) * columns - 1);
		int visibleCount = endIndex >= startIndex ? (endIndex - startIndex + 1) : 0;

		EnsureCellPoolCapacity(visibleCount);
		BindVisibleGridCells(startIndex, visibleCount, columns, _matchingAssets, IsAssetSelected, OnAssetCellClicked);
		HideUnusedGridCells(visibleCount);
	}

	private bool IsAssetSelected(IndexedAsset asset)
	{
		if (_multiSelectEnabled)
		{
			return _selectedAssetPaths.Contains(asset.FilePath);
		}

		return _selectedAsset != null && string.Equals(_selectedAsset.FilePath, asset.FilePath, StringComparison.OrdinalIgnoreCase);
	}

	private void OnAssetCellClicked(IndexedAsset asset, bool isDoubleClick)
	{
		if (_multiSelectEnabled)
		{
			ApplyMultiSelectClick(asset, isDoubleClick);
			return;
		}

		_selectedAsset = asset;
		UpdateSelectedAssetDisplay();
		UpdateVisibleGridCells();

		if (isDoubleClick)
		{
			ApplyAndClose();
		}
	}

	private void ApplyMultiSelectClick(IndexedAsset asset, bool isDoubleClick)
	{
		bool additive = Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Shift);

		if (isDoubleClick)
		{
			_selectedAsset = asset;
			_selectedAssetPaths.Add(asset.FilePath);
			UpdateSelectedAssetDisplay();
			UpdateSelectionIndicators();
			UpdateVisibleGridCells();
			ApplyAndClose();
			return;
		}

		if (additive && _selectedAssetPaths.Contains(asset.FilePath))
		{
			_selectedAssetPaths.Remove(asset.FilePath);
		}
		else if (additive)
		{
			_selectedAssetPaths.Add(asset.FilePath);
		}
		else
		{
			_selectedAssetPaths.Clear();
			_selectedAssetPaths.Add(asset.FilePath);
		}

		_selectedAsset = asset;
		UpdateSelectedAssetDisplay();
		UpdateSelectionIndicators();
		UpdateVisibleGridCells();
	}

	private void OnSelectAllPressed()
	{
		foreach (var asset in _matchingAssets)
		{
			_selectedAssetPaths.Add(asset.FilePath);
		}

		UpdateSelectionIndicators();
		UpdateVisibleGridCells();
	}

	private void OnClearSelectionPressed()
	{
		_selectedAssetPaths.Clear();
		UpdateSelectionIndicators();
		UpdateVisibleGridCells();
	}

	private void UpdateSelectionIndicators()
	{
		int selectedCount = _selectedAssetPaths.Count;
		if (_lblSelectionCount != null)
		{
			_lblSelectionCount.Text = string.Format(TranslationServer.Translate("{0} selected"), selectedCount);
		}

		if (_btnSelectAll != null)
		{
			_btnSelectAll.Disabled = _matchingAssets.Count == 0;
			_btnSelectAll.Text = string.Format(TranslationServer.Translate("Select All ({0})"), _matchingAssets.Count);
		}

		if (_btnClearSelection != null)
		{
			_btnClearSelection.Disabled = selectedCount == 0;
		}

		if (ApplyButton != null)
		{
			ApplyButton.Text = selectedCount > 0
				? string.Format(TranslationServer.Translate("Select ({0})"), selectedCount)
				: TranslationServer.Translate("Select");
		}
	}

	private void UpdateSelectedAssetDisplay()
	{
		StopAudio();

		if (_selectedAsset == null)
		{
			ResetAssetDisplay();
			return;
		}

		_lblSelectedFileName.Text = _selectedAsset.FileName;
		_lblSelectedPath.Text = _selectedAsset.FilePath;
		_lblSelectedSize.Text = FormatFileSize(_selectedAsset.FileSizeBytes);
		_bottomThumbnail.Texture = AssetThumbnailProvider.GetThumbnail(_selectedAsset);
		_txtTagsEdit.Text = _selectedAsset.Tags != null ? string.Join(", ", _selectedAsset.Tags) : string.Empty;
		_txtTagsEdit.Editable = false;
		_btnEditTags.Disabled = false;

		UpdateAssetTypeDisplay();
		UpdateAudioDisplay();
	}

	private void ResetAssetDisplay()
	{
		_lblSelectedFileName.Text = TranslationServer.Translate("No asset selected");
		_lblSelectedPath.Text = string.Empty;
		_lblSelectedSize.Text = string.Empty;
		_bottomThumbnail.Texture = null;
		_txtTagsEdit.Text = string.Empty;
		_txtTagsEdit.Editable = false;
		_btnEditTags.Disabled = true;
		_txtAssetTypeEdit.Text = string.Empty;
		_txtAssetTypeEdit.Editable = false;
		_btnEditAssetType.Disabled = true;
		_btnAudioPlayPause.Visible = false;
	}

	private void UpdateAssetTypeDisplay()
	{
		string embeddedAssetType = RealmMetadataHelper.ExtractAssetType(_selectedAsset!.FilePath) ?? string.Empty;
		_txtAssetTypeEdit.Text = !string.IsNullOrEmpty(embeddedAssetType) ? embeddedAssetType : TranslationServer.Translate("None");
		_txtAssetTypeEdit.Editable = false;
		var validTypes = RealmMetadataHelper.GetValidAssetTypesForExtension(_selectedAsset.FilePath);
		_btnEditAssetType.Disabled = (validTypes.Count == 0);
	}

	private void UpdateAudioDisplay()
	{
		string ext = _selectedAsset!.Extension?.ToLowerInvariant() ?? "";
		bool isAudio = ext is ".raud" or ".ogg" or ".wav" or ".mp3" or ".flac" or ".aac";
		if (isAudio && File.Exists(_selectedAsset.FilePath))
		{
			LoadSelectedAudioFile(ext);
			_btnAudioPlayPause.Visible = (_audioPlayer.Stream != null);
			_btnAudioPlayPause.Text = "▶ " + TranslationServer.Translate("Play");
			_btnAudioPlayPause.TooltipText = TranslationServer.Translate("Play audio");
		}
		else
		{
			_btnAudioPlayPause.Visible = false;
		}
	}

	private void LoadSelectedAudioFile(string ext)
	{
		try
		{
			if (ext == ".raud")
			{
				byte[] raudBytes = File.ReadAllBytes(_selectedAsset!.FilePath);
				byte[]? oggBytes = RaudFile.GetTrack(raudBytes, 0);
				if (oggBytes != null && oggBytes.Length > 0)
				{
					var oggStream = AudioStreamOggVorbis.LoadFromBuffer(oggBytes);
					if (oggStream != null)
					{
						oggStream.Loop = false;
						_audioPlayer.Stream = oggStream;
					}
				}
			}
			else if (ext == ".ogg")
			{
				var oggStream = AudioStreamOggVorbis.LoadFromFile(_selectedAsset!.FilePath);
				if (oggStream != null)
				{
					oggStream.Loop = false;
					_audioPlayer.Stream = oggStream;
				}
			}
			else
			{
				_audioPlayer.Stream = GD.Load<AudioStream>(_selectedAsset!.FilePath);
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[AssetBrowserDialog] Failed to load audio stream: {ex.Message}");
		}
	}

	private void OnAudioPlayPausePressed()
	{
		if (_audioPlayer == null || _audioPlayer.Stream == null) return;

		if (_audioPlayer.Playing)
		{
			_audioPlayer.Stop();
			_btnAudioPlayPause.Text = "▶ " + TranslationServer.Translate("Play");
			_btnAudioPlayPause.TooltipText = TranslationServer.Translate("Play audio");
		}
		else
		{
			_audioPlayer.Play();
			_btnAudioPlayPause.Text = "⏹ " + TranslationServer.Translate("Stop");
			_btnAudioPlayPause.TooltipText = TranslationServer.Translate("Stop audio");

			if (!AudioService.IsMasterVolumeAudible())
			{
				Hud?.ShowFeedback(AudioService.PreviewInaudibleWarning);
			}
		}
	}

	private void OnAudioFinished()
	{
		if (_btnAudioPlayPause != null)
		{
			_btnAudioPlayPause.Text = "▶ " + TranslationServer.Translate("Play");
			_btnAudioPlayPause.TooltipText = TranslationServer.Translate("Play audio");
		}
	}

	private void StopAudio()
	{
		if (_audioPlayer != null)
		{
			if (_audioPlayer.Playing)
			{
				_audioPlayer.Stop();
			}

			_audioPlayer.Stream = null;
		}

		if (_btnAudioPlayPause != null)
		{
			_btnAudioPlayPause.Text = "▶ " + TranslationServer.Translate("Play");
			_btnAudioPlayPause.TooltipText = TranslationServer.Translate("Play audio");
		}
	}

	private void OnEditTagsPressed()
	{
		if (_selectedAsset == null)
		{
			return;
		}

		var dialog = new TagEditorDialog(RequireHud, _selectedAsset, OnTagsSaved);
		dialog.DialogClosed += () => dialog.QueueFree();
		dialog.OpenDialog();
	}

	private void OnTagsSaved(List<string> updatedTags)
	{
		if (_selectedAsset == null)
		{
			return;
		}

		GameContext.AssetIndexService.UpdateAssetTags(_selectedAsset.FilePath, updatedTags);
		_selectedAsset.Tags = updatedTags;
		_txtTagsEdit.Text = string.Join(", ", updatedTags);

		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Updated tags for {0}"), _selectedAsset.FileName));
		RefreshSearchResults();
	}

	private void OnEditAssetTypePressed()
	{
		if (_selectedAsset == null) return;
		var dialog = new AssetTypeEditorDialog(RequireHud, _selectedAsset, OnAssetTypeSaved);
		dialog.DialogClosed += () => dialog.QueueFree();
		dialog.OpenDialog();
	}

	private void OnAssetTypeSaved(string updatedAssetType)
	{
		if (_selectedAsset == null) return;
		GameContext.AssetIndexService.UpdateAssetType(_selectedAsset.FilePath, updatedAssetType);
		_selectedAsset.AssetType = RealmMetadataHelper.NormalizeAssetType(updatedAssetType);
		_txtAssetTypeEdit.Text = updatedAssetType;
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Updated asset type for {0} to {1}"), _selectedAsset.FileName, updatedAssetType));
		RefreshSearchResults();
	}

	protected override void OnApply()
	{
		StopAudio();

		if (_multiSelectEnabled && _selectedAssetPaths.Count > 0)
		{
			ApplyMultiSelectImport();
			return;
		}

		if (_selectedAsset != null && File.Exists(_selectedAsset.FilePath))
		{
			RealmMetadataHelper.EnsureMetadata(_selectedAsset.FilePath);
			_onAssetSelectedWithPreferredNameCallback?.Invoke(_selectedAsset.FilePath, _selectedAsset.FileName);
			_onAssetSelectedCallback?.Invoke(_selectedAsset.FilePath);
		}
	}

	private void ApplyMultiSelectImport()
	{
		var selections = new List<AssetBrowserSelection>(_selectedAssetPaths.Count);
		foreach (string path in _selectedAssetPaths)
		{
			if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;

			RealmMetadataHelper.EnsureMetadata(path);
			selections.Add(new AssetBrowserSelection(path, Path.GetFileName(path)));
		}

		if (selections.Count == 0)
		{
			return;
		}

		_onAssetsSelectedCallback?.Invoke(selections);
	}

	private static string FormatFileSize(long bytes)
	{
		if (bytes < 1024)
		{
			return $"{bytes} B";
		}

		if (bytes < 1024 * 1024)
		{
			return $"{bytes / 1024.0:F1} KB";
		}

		return $"{bytes / (1024.0 * 1024.0):F2} MB";
	}
}