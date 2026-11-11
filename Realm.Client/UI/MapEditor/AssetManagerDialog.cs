using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.EditorAPI;
using Realm.Shared.Audio;
using Realm.Shared.Textures;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class AssetManagerDialog : AssetGridDialogBase
{
	public AssetManagerDialog()
	{
		CellWidth = 120.0f;
		CellHeight = 140.0f;
		SpacingX = 10.0f;
		SpacingY = 10.0f;
		GridPadding = 10.0f;
	}

	private OptionButton _optAssetTypeFilter = null!;
	private LineEdit _txtSearchFilter = null!;
	private Label _lblAssetCount = null!;

	private Button _btnImportAsset = null!;
	private Button _btnImportShader = null!;
	private Button _btnConvertFormat = null!;
	private Button _btnGenerateAiAsset = null!;
	private Button _btnGenerateNoise = null!;

	private DropdownSuggestionList _convertFormatMenu = null!;
	private DropdownSuggestionList _generateAiAssetMenu = null!;

	private Label _lblEmptyState = null!;
	private Label _lblAssetPackStatus = null!;
	private bool _assetPackDownloadRunning;

	private PanelContainer _footerPanel = null!;
	private TextureRect _footerThumbnail = null!;
	private Label _lblSelectedFileName = null!;
	private Label _lblSelectedPath = null!;
	private Label _lblSelectedSize = null!;
	private Label _lblSelectedDetails = null!;
	private Button _btnAudioPlay = null!;
	private OptionButton _optRaudEntries = null!;
	private Button _btnDeleteAsset = null!;

	private AudioStreamPlayer _audioPlayer = null!;

	private AssetConversionProgressOverlay? _conversionOverlay;

	private readonly List<IndexedAsset> _matchingAssets = new();

	private SpritesheetAssetEditDialog _spritesheetEditDialog = null!;
	private TerrainTextureEditDialog _textureEditDialog = null!;
	private DecalSettingsDialog _decalEditDialog = null!;
	private ShaderEditorDialog _shaderEditDialog = null!;

	private string _selectedAssetType = "All";
	private string _searchFilterText = string.Empty;
	private IndexedAsset? _selectedAsset;
	private string? _selectedRaudEntryName;

	private static readonly string[] ValidAssetTypes = new[] { "All" }
		.Concat(RealmMetadataHelper.GetAssetTypeDisplayOrder())
		.ToArray();

	public AssetManagerDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Asset Manager"), new Vector2(860, 720))
	{
		SetUncompressedPanelTexture("res://Assets/UI/map_editor_assets_importer.webp", 34, 40, 60, 60);

		_spritesheetEditDialog = new SpritesheetAssetEditDialog(hud);
		_textureEditDialog = new TerrainTextureEditDialog(hud);
		_decalEditDialog = new DecalSettingsDialog(hud);
		_shaderEditDialog = new ShaderEditorDialog(hud);

		_audioPlayer = new AudioStreamPlayer { Name = "AssetManagerPreviewAudio", Bus = AudioService.PreviewBusName };
		_audioPlayer.Finished += OnAudioFinished;
		AddChild(_audioPlayer);

		BuildControls();
		SetFooterCloseOnly();

		AssetThumbnailProvider.ThumbnailGenerated += OnThumbnailGenerated;
	}

	public override void _ExitTree()
	{
		StopAudio();
		AssetThumbnailProvider.ThumbnailGenerated -= OnThumbnailGenerated;
		_convertFormatMenu?.Free();
		_generateAiAssetMenu?.Free();
		base._ExitTree();
	}

	public override void CloseDialog()
	{
		StopAudio();
		_convertFormatMenu?.Hide();
		_generateAiAssetMenu?.Hide();
		base.CloseDialog();
	}

	public override void OpenDialog()
	{
		base.OpenDialog();
		RefreshAssetList();
		UpdateVirtualGridSize(_matchingAssets.Count);
		UpdateVisibleGridCells();
	}


	private void UpdateActionButtonVisibility()
	{
		bool isAll = _selectedAssetType == "All";

		SetControlVisible(_btnImportShader, isAll || _selectedAssetType == "Shader");
		SetControlVisible(_btnGenerateNoise, isAll || _selectedAssetType == "Noise");
	}

	private static void SetControlVisible(Control? control, bool visible)
	{
		if (control != null) control.Visible = visible;
	}

	private void ShowConvertFormatMenu()
	{
		_convertFormatMenu?.ShowItems(new[]
		{
			TranslationServer.Translate("Image (.rtex)").ToString(),
			TranslationServer.Translate("Audio (.raud)").ToString(),
			TranslationServer.Translate("3D Model (.rmesh)").ToString(),
			TranslationServer.Translate("Mixamo (.ranim)").ToString()
		});
	}

	private void OnConvertFormatSelected(int index)
	{
		switch (index)
		{
			case 0:
				OnConvertImagePressed();
				break;
			case 1:
				OnConvertAudioPressed();
				break;
			case 2:
				OnConvert3DModelPressed();
				break;
			case 3:
				OnConvertMixamoPressed();
				break;
		}
	}

	private static readonly string[] GenerateAiAssetUrls = new[]
	{
		"https://huggingface.co/spaces/black-forest-labs/flux-3-image-klein?tab=generate",
		"https://huggingface.co/spaces/stabilityai/stable-audio-3",
		"https://3d.hunyuanglobal.com"
	};

	private void ShowGenerateAiAssetMenu()
	{
		_generateAiAssetMenu?.ShowItems(new[]
		{
			TranslationServer.Translate("Image").ToString(),
			TranslationServer.Translate("Audio").ToString(),
			TranslationServer.Translate("3D Mesh").ToString()
		});
	}

	private void OnGenerateAiAssetSelected(int index)
	{
		if (index < 0 || index >= GenerateAiAssetUrls.Length) return;

		OS.ShellOpen(GenerateAiAssetUrls[index]);
	}

	private string GetWorkspacePath()
	{
		if (!string.IsNullOrEmpty(GameContext.EditorService._editingDirectory))
		{
			return GameContext.EditorService._editingDirectory;
		}

		return MapWorkspaceService.GetActiveWorkspacePath();
	}

	private void OnThumbnailGenerated(string filePath, Texture2D texture)
	{
		if (_selectedAsset != null)
		{
			string selectedNorm = AssetThumbnailProvider.NormalizePath(_selectedAsset.FilePath);
			string eventNorm = AssetThumbnailProvider.NormalizePath(filePath);
			if (string.Equals(selectedNorm, eventNorm, StringComparison.OrdinalIgnoreCase))
			{
				_footerThumbnail.Texture = texture;
			}
		}
	}



	private void UpdateVisibleGridCells()
	{
			UpdateVisibleCellsForScroll(
				_scrollContainer,
				_matchingAssets.Count,
				out int startIndex,
				out int visibleCount,
				out int columns
			);

		EnsureCellPoolCapacity(visibleCount);
		BindVisibleGridCells(startIndex, visibleCount, columns, _matchingAssets, _selectedAsset, OnAssetCellClicked);
		HideUnusedGridCells(visibleCount);
	}

	private void OnAssetCellClicked(IndexedAsset asset, bool isDoubleClick)
	{
		_selectedAsset = asset;
		UpdateSelectedAssetDisplay();
		UpdateVisibleGridCells();
	}

	private void UpdateSelectedAssetDisplay()
	{
		StopAudio();

		if (_selectedAsset == null)
		{
			ClearSelectedAssetDisplay();
			return;
		}

		UpdateSelectedAssetLabels();
		UpdateSelectedAssetThumbnail();
		UpdateSelectedAssetMetadataSummary();
		UpdateSelectedAssetAudioPreview();
	}

	private void ClearSelectedAssetDisplay()
	{
		_lblSelectedFileName.Text = TranslationServer.Translate("No asset selected");
		_lblSelectedPath.Text = string.Empty;
		_lblSelectedSize.Text = string.Empty;
		_lblSelectedDetails.Text = string.Empty;
		_footerThumbnail.Texture = null;
		_btnAudioPlay.Visible = false;
		_optRaudEntries.Visible = false;
		_optRaudEntries.Clear();
		_selectedRaudEntryName = null;
		_btnDeleteAsset.Disabled = true;
	}

	private void UpdateSelectedAssetLabels()
	{
		_lblSelectedFileName.Text = _selectedAsset!.FileName;
		_lblSelectedPath.Text = _selectedAsset.FilePath;
		_lblSelectedSize.Text = FormatFileSize(_selectedAsset.FileSizeBytes);
		_btnDeleteAsset.Disabled = false;
	}

	private void UpdateSelectedAssetThumbnail()
	{
		_footerThumbnail.Texture = AssetThumbnailProvider.GetThumbnail(_selectedAsset!);
	}

	private void UpdateSelectedAssetMetadataSummary()
	{
		if (!File.Exists(_selectedAsset!.FilePath))
		{
			_lblSelectedDetails.Text = string.Empty;
			return;
		}

		string? metaJson = RealmMetadataHelper.ExtractMetadata(_selectedAsset.FilePath);
		string displayType = _selectedAsset.AssetType?.ToCanonicalString() ?? string.Empty;
		_lblSelectedDetails.Text = !string.IsNullOrEmpty(metaJson)
			? $"{TranslationServer.Translate("Type")}: {displayType} • {TranslationServer.Translate("Metadata Header")}: {metaJson}"
			: $"{TranslationServer.Translate("Type")}: {displayType} • BLAKE3: {_selectedAsset.Blake3}";
	}




	private void OnConvertImagePressed()
	{
		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Select Image File to Convert to Realm format (.rtex)"),
			PathUtils.GetProjectRoot(),
			"",
			false,
			DisplayServer.FileDialogMode.OpenFile,
			new[] { "*.png,*.jpg,*.jpeg,*.bmp,*.webp,*.tga,*.dds,*.rtex ; Supported Image Files (*.png, *.jpg, *.jpeg, *.bmp, *.webp, *.tga, *.dds, *.rtex)" },
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
			{
				if (status && selectedPaths.Length > 0)
				{
					ConvertImageToRealmFormat(selectedPaths[0]);
				}
			})
		);

		if (err != Error.Ok)
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Failed to show file dialog"));
		}
	}

	private void ConvertImageToRealmFormat(string sourceFilePath)
	{
		if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath)) return;

		string defaultCategory = _selectedAssetType is "Decal" or "Icon" or "Noise" or "Ribbon" or "Skybox" or "Spritesheet" or "Terrain" or "vfx_radial" or "vfx_vertical"
			? _selectedAssetType
			: "Terrain";

		new AssetImageConvertDialog(this, Path.GetFileName(sourceFilePath), defaultCategory, targetCategory => StartImageConversion(sourceFilePath, targetCategory)).Show();
	}

	private void StartImageConversion(string sourceFilePath, string targetCategory)
	{
		string wsPath = GetWorkspacePath();
		string resolvedName = GameContext.AssetIndexService?.ResolvePrettyFileName(sourceFilePath) ?? Path.GetFileName(sourceFilePath);
		string cleanBase = Path.GetFileNameWithoutExtension(resolvedName).ToLowerInvariant().Replace(' ', '_');
		string destPath = SetupImageDestinationPath(wsPath, targetCategory, cleanBase);

		RunBackgroundConversion(
			TranslationServer.Translate("CONVERTING IMAGE"),
			string.Format(TranslationServer.Translate("Converting {0} ..."), Path.GetFileName(sourceFilePath)),
			() =>
			{
				TextureConversionResult result = ProcessImageConversion(sourceFilePath, destPath, targetCategory);
				return (result.Success, result.ErrorMessage);
			},
			outcome =>
			{
				if (!outcome.Success)
				{
					Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Failed to convert image: {0}"), outcome.ErrorMessage));
					return;
				}

				FinalizeImageConversionSafely(wsPath, targetCategory, cleanBase, destPath);
			});
	}

	private void FinalizeImageConversionSafely(string wsPath, string targetCategory, string cleanBase, string destPath)
	{
		try
		{
			FinalizeImageConversion(wsPath, targetCategory, cleanBase, destPath);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[AssetManagerDialog] FinalizeImageConversion error: {ex.Message}");
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Error converting image: {0}"), ex.Message));
		}
	}

	private void RunBackgroundConversion(string title, string status, Func<(bool Success, string ErrorMessage)> work, Action<(bool Success, string ErrorMessage)> onCompleted)
	{
		if (_conversionOverlay != null && GodotObject.IsInstanceValid(_conversionOverlay)) return;

		Node host = Hud != null ? Hud : this;
		var overlay = new AssetConversionProgressOverlay();
		host.AddChild(overlay);
		overlay.Initialize(title, status);
		_conversionOverlay = overlay;

		Task.Run(work).ContinueWith(task =>
		{
			(bool Success, string ErrorMessage) outcome = task.IsCompletedSuccessfully
				? task.Result
				: (false, task.Exception?.GetBaseException().Message ?? string.Empty);

			Callable.From(() =>
			{
				if (GodotObject.IsInstanceValid(overlay)) overlay.QueueFree();
				_conversionOverlay = null;

				if (GodotObject.IsInstanceValid(this)) onCompleted(outcome);
			}).CallDeferred();
		}, TaskScheduler.Default);
	}

	private string SetupImageDestinationPath(string wsPath, string targetCategory, string cleanBase)
	{
		string subDir = targetCategory switch
		{
			"Decal" => "decals",
			"Icon" => "icons",
			"Spritesheet" => "vfx",
			"vfx_radial" => "vfx_radial",
			"vfx_vertical" => "vfx_vertical",
			"Ribbon" => "ribbons",
			"Noise" => "noise",
			"Skybox" => "skyboxes",
			_ => "textures"
		};

		string destDir = Path.Combine(wsPath, "Assets", subDir);
		Directory.CreateDirectory(destDir);
		return Path.Combine(destDir, $"{cleanBase}.rtex");
	}

	private static TextureConversionResult ProcessImageConversion(string sourceFilePath, string destPath, string targetCategory)
	{
		int decalCols = 1, decalRows = 1;
		int vfxCols = 4, vfxRows = 4;
		float vfxFps = 20.0f;

		return targetCategory switch
		{
			"Skybox" => TextureConverter.ProcessAndSaveSkybox(sourceFilePath, destPath),
			"Decal" => TextureConverter.ProcessAndSaveDecalTexture(sourceFilePath, destPath, columns: decalCols, rows: decalRows),
			"Icon" => TextureConverter.ProcessAndSaveIconTexture(sourceFilePath, destPath),
			"Spritesheet" => TextureConverter.ProcessAndSaveSpritesheet(sourceFilePath, destPath, vfxCols, vfxRows, vfxFps),
			"vfx_radial" => TextureConverter.ProcessAndSaveVfxRadialTexture(sourceFilePath, destPath),
			"vfx_vertical" => TextureConverter.ProcessAndSaveVfxVerticalTexture(sourceFilePath, destPath),
			"Ribbon" => TextureConverter.ProcessAndSaveRibbonTexture(sourceFilePath, destPath),
			"Noise" => TextureConverter.ProcessAndSaveSingleLayerTexture(sourceFilePath, destPath, "Noise"),
			_ => TextureConverter.ProcessAndSaveTerrainTexture(sourceFilePath, destPath)
		};
	}

	private void FinalizeImageConversion(string wsPath, string targetCategory, string cleanBase, string destPath)
	{
		byte[] bytes = File.ReadAllBytes(destPath);
		string hash = RealmMetadataHelper.ComputeBlake3(bytes, ".rtex");

		MapAssetHelper.UpdateManifestAsset(wsPath, targetCategory, $"{cleanBase}.rtex", hash);
		EnsureTemplateForAsset(wsPath, targetCategory, $"{cleanBase}.rtex", hash);

		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Converted and imported {0}.rtex"), cleanBase));
		GameContext.AssetIndexService?.RescanAllDirectories();
		RefreshAssetListAndSelect($"{cleanBase}.rtex");
	}


	private void OnConvertAudioPressed()
	{
		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Select Audio File to Convert to Realm format (.raud)"),
			PathUtils.GetProjectRoot(),
			"",
			false,
			DisplayServer.FileDialogMode.OpenFile,
			new[] { "*.mp3,*.wav,*.aiff,*.aif,*.flac,*.aac,*.m4a,*.wma,*.ogg ; Audio Files (*.mp3, *.wav, *.aiff, *.aif, *.flac, *.aac, *.m4a, *.wma, *.ogg)" },
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
			{
				if (status && selectedPaths.Length > 0)
				{
					ConvertAudioToRealmFormat(selectedPaths[0]);
				}
			})
		);

		if (err != Error.Ok)
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Failed to show file dialog"));
		}
	}

	private void ConvertAudioToRealmFormat(string sourceFilePath)
	{
		if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath)) return;

		string wsPath = GetWorkspacePath();
		string cleanBase = GetCleanBaseAudioName(sourceFilePath);
		string targetCategory = _selectedAssetType == "Music" ? "Music" : "SoundEffect";
		string destPath = SetupAudioDestinationPath(wsPath, targetCategory, cleanBase);
		string temporaryPath = $"{destPath}.converting.ogg";

		RunBackgroundConversion(
			TranslationServer.Translate("CONVERTING AUDIO"),
			string.Format(TranslationServer.Translate("Converting {0} to Ogg Vorbis ..."), Path.GetFileName(sourceFilePath)),
			() => ConvertAudioIntoPlace(sourceFilePath, temporaryPath, destPath),
			outcome =>
			{
				if (!outcome.Success)
				{
					Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Failed to convert audio: {0}"), outcome.ErrorMessage));
					return;
				}

				FinalizeAudioConversionSafely(wsPath, targetCategory, destPath, cleanBase);
			});
	}

	private static (bool Success, string ErrorMessage) ConvertAudioIntoPlace(string sourceFilePath, string temporaryPath, string destPath)
	{
		var result = AudioConverter.ConvertToOgg(sourceFilePath, temporaryPath);
		if (!result.Success)
		{
			SafeDeletePartialConversion(temporaryPath);
			return (false, result.ErrorMessage);
		}

		try
		{
			File.Move(temporaryPath, destPath, true);
			return (true, result.ErrorMessage);
		}
		catch (Exception ex)
		{
			SafeDeletePartialConversion(temporaryPath);
			return (false, ex.Message);
		}
	}

	private void FinalizeAudioConversionSafely(string wsPath, string targetCategory, string destPath, string cleanBase)
	{
		try
		{
			UpdateAudioManifest(wsPath, targetCategory, destPath, $"{cleanBase}.ogg");

			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Converted and imported audio {0}.ogg"), cleanBase));
			GameContext.AssetIndexService?.RescanAllDirectories();
			RefreshAssetListAndSelect($"{cleanBase}.ogg");
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[AssetManagerDialog] ConvertAudioToRealmFormat error: {ex.Message}");
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Error converting audio: {0}"), ex.Message));
		}
	}

	private static void SafeDeletePartialConversion(string destPath)
	{
		try
		{
			if (File.Exists(destPath)) File.Delete(destPath);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[AssetManagerDialog] Could not delete partial conversion {destPath}: {ex.Message}");
		}
	}

	private static string GetCleanBaseAudioName(string sourceFilePath)
	{
		string resolvedName = GameContext.AssetIndexService?.ResolvePrettyFileName(sourceFilePath) ?? Path.GetFileName(sourceFilePath);
		return Path.GetFileNameWithoutExtension(resolvedName).ToLowerInvariant().Replace(' ', '_');
	}

	private static string SetupAudioDestinationPath(string wsPath, string targetCategory, string cleanBase)
	{
		string sub = targetCategory == "Music" ? "music" : "sfx";
		string destDir = Path.Combine(wsPath, "Assets", "audio", sub);
		Directory.CreateDirectory(destDir);
		return Path.Combine(destDir, $"{cleanBase}.ogg");
	}

	private static void UpdateAudioManifest(string wsPath, string targetCategory, string destPath, string fileName)
	{
		byte[] bytes = File.ReadAllBytes(destPath);
		string hash = RealmMetadataHelper.ComputeBlake3(bytes, ".ogg");
		MapAssetHelper.UpdateManifestAsset(wsPath, targetCategory, fileName, hash);
	}

	private void OnConvert3DModelPressed()
	{
		string targetSub = _selectedAssetType switch
		{
			"Character" => "characters",
			"Building" => "buildings",
			"Item" => "items",
			_ => "props"
		};
		Hud?.OpenConvertGlbDialog(null, targetSub, (_) => RefreshAssetList());
	}

	private void OnConvertMixamoPressed()
	{
		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Select Mixamo FBX or GLB File to Convert to .ranim"),
			PathUtils.GetProjectRoot(),
			"",
			false,
			DisplayServer.FileDialogMode.OpenFile,
			new[] { "*.fbx,*.glb,*.gltf ; 3D Animation Files (*.fbx, *.glb, *.gltf)" },
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
			{
				if (status && selectedPaths.Length > 0)
				{
					ConvertMixamoFileToRanim(selectedPaths[0]);
				}
			})
		);

		if (err != Error.Ok)
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Failed to show file dialog"));
		}
	}

	private void ConvertMixamoFileToRanim(string sourceFilePath)
	{
		if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath)) return;

		string wsPath = GetWorkspacePath();
		string animsDir = Path.Combine(wsPath, "Assets", "animations");
		Directory.CreateDirectory(animsDir);

		try
		{
			string originalFileName = Path.GetFileNameWithoutExtension(sourceFilePath);
			var extracted = MixamoAnimationImporter.ExtractAnimationsFromFile(sourceFilePath, originalFileName);
			if (extracted.Count == 0)
			{
				Hud?.ShowFeedback(TranslationServer.Translate("No skeletal animations found in the selected file."));
				return;
			}

			ProcessExtractedAnimations(wsPath, animsDir, extracted);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[AssetManagerDialog] ConvertMixamoFileToRanim error: {ex.Message}");
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Error converting animation: {0}"), ex.Message));
		}
	}

	private void ProcessExtractedAnimations(string wsPath, string animsDir, List<(string AnimationName, RealmAnimationData Data)> extracted)
	{
		int importedCount = 0;
		int skippedCount = 0;
		string? firstSavedFileName = null;

		foreach (var (animName, animData) in extracted)
		{
			var (savedFileName, blake3, alreadyExisted) = MixamoAnimationImporter.SaveAnimationWithDeduplication(animsDir, animName, animData);
			MapAssetHelper.UpdateManifestAsset(wsPath, "Animation", savedFileName, blake3);

			if (alreadyExisted) skippedCount++;
			else importedCount++;

			firstSavedFileName ??= savedFileName;
		}

		if (importedCount > 0)
		{
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Successfully converted and imported {0} .ranim animation(s)!"), importedCount));
		}
		else
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Animation(s) already existed in map workspace (identical BLAKE3 hash)."));
		}

		GameContext.AssetIndexService?.RescanAllDirectories();
		RefreshAssetListAndSelect(firstSavedFileName ?? string.Empty);
	}

	private static string FormatFileSize(long bytes)
	{
		if (bytes < 1024) return $"{bytes} B";
		if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
		return $"{bytes / (1024.0 * 1024.0):F2} MB";
	}
}