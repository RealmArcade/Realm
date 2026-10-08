using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Realm.Ecs.Services;
using Realm.Godot.Animation;
using Realm.Godot.Services;
using Realm.Godot.Services.ModelOptimization;
using Realm.Godot.Utils;
using Realm.Shared.Audio;
using Realm.Shared.Metadata;
using Realm.Shared.Textures;

public partial class AssetManagerDialog : FloatingDialogBase
{
	private const float CellWidth = 120.0f;
	private const float CellHeight = 140.0f;
	private const float SpacingX = 10.0f;
	private const float SpacingY = 10.0f;
	private const float GridPadding = 10.0f;

	private OptionButton _optAssetTypeFilter;
	private LineEdit _txtSearchFilter;
	private Label _lblAssetCount;

	private Button _btnImportAsset;
	private Button _btnImportShader;
	private Button _btnConvertImage;
	private Button _btnConvertAudio;
	private Button _btnConvert3DModel;
	private Button _btnConvertMixamo;
	private Button _btnGenerateNoise;
	private Button _btnPruneUnused;
	private Button _btnNormalize;

	private ScrollContainer _scrollContainer;
	private Control _virtualGridContent;
	private Label _lblEmptyState;

	private PanelContainer _footerPanel;
	private TextureRect _footerThumbnail;
	private Label _lblSelectedFileName;
	private Label _lblSelectedPath;
	private Label _lblSelectedSize;
	private Label _lblSelectedDetails;
	private Button _btnAudioPlay;
	private Button _btnDeleteAsset;

	private AudioStreamPlayer _audioPlayer;

	private readonly List<AssetGridCell> _cellPool = new();
	private readonly List<IndexedAsset> _matchingAssets = new();

	private SpritesheetAssetEditDialog _spritesheetEditDialog;
	private TerrainTextureEditDialog _textureEditDialog;
	private DecalSettingsDialog _decalEditDialog;
	private ShaderEditorDialog _shaderEditDialog;

	private string _selectedAssetType = "All";
	private string _searchFilterText = string.Empty;
	private IndexedAsset? _selectedAsset;

	private static readonly string[] ValidAssetTypes = new[]
	{
		"All",
		"Character",
		"Building",
		"Prop",
		"Item",
		"Terrain",
		"Spritesheet",
		"vfx_radial",
		"vfx_vertical",
		"Animation",
		"SoundEffect",
		"Music",
		"Icon",
		"Decal",
		"Ribbon",
		"Noise",
		"Skybox",
		"Shader"
	};

	public AssetManagerDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Asset Manager"), new Vector2(860, 720))
	{
		SetUncompressedPanelTexture("res://Assets/UI/map_editor_assets_importer.png", 34, 40, 60, 60);

		_spritesheetEditDialog = new SpritesheetAssetEditDialog(hud);
		_textureEditDialog = new TerrainTextureEditDialog(hud);
		_decalEditDialog = new DecalSettingsDialog(hud);
		_shaderEditDialog = new ShaderEditorDialog(hud);

		_audioPlayer = new AudioStreamPlayer();
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
		base._ExitTree();
	}

	public override void CloseDialog()
	{
		StopAudio();
		base.CloseDialog();
	}

	public override void OpenDialog()
	{
		base.OpenDialog();
		RefreshAssetList();
		UpdateVirtualGridSize();
		UpdateVisibleGridCells();
	}

	private void BuildControls()
	{
		BodyContainer.AddThemeConstantOverride("separation", 8);

		// 1. TOP HEADER: Asset Type Filter Dropdown & Search Filter Input
		var headerRow = new HBoxContainer();
		headerRow.AddThemeConstantOverride("separation", 8);

		var lblType = new Label();
		lblType.Text = "🏷 " + TranslationServer.Translate("Asset Type:");
		lblType.AddThemeFontSizeOverride("font_size", 11);
		lblType.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		headerRow.AddChild(lblType);

		_optAssetTypeFilter = new OptionButton();
		_optAssetTypeFilter.CustomMinimumSize = new Vector2(160, 26);
		_optAssetTypeFilter.AddThemeFontSizeOverride("font_size", 11);
		_optAssetTypeFilter.FocusMode = FocusModeEnum.None;

		for (int i = 0; i < ValidAssetTypes.Length; i++)
		{
			string typeName = ValidAssetTypes[i];
			string label = typeName == "All" ? TranslationServer.Translate("All Asset Types") : TranslationServer.Translate(typeName);
			_optAssetTypeFilter.AddItem(label, i);
			_optAssetTypeFilter.SetItemMetadata(i, typeName);
		}

		_optAssetTypeFilter.ItemSelected += (idx) =>
		{
			_selectedAssetType = _optAssetTypeFilter.GetItemMetadata((int)idx).AsString();
			UpdateActionButtonVisibility();
			RefreshAssetList();
		};
		headerRow.AddChild(_optAssetTypeFilter);

		var lblSearch = new Label();
		lblSearch.Text = "🔍 " + TranslationServer.Translate("Search:");
		lblSearch.AddThemeFontSizeOverride("font_size", 11);
		lblSearch.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		headerRow.AddChild(lblSearch);

		_txtSearchFilter = new LineEdit();
		_txtSearchFilter.PlaceholderText = TranslationServer.Translate("Search by filename or metadata tags...");
		_txtSearchFilter.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_txtSearchFilter.AddThemeFontSizeOverride("font_size", 11);
		_txtSearchFilter.TextChanged += (text) =>
		{
			_searchFilterText = text?.Trim() ?? string.Empty;
			RefreshAssetList();
		};
		headerRow.AddChild(_txtSearchFilter);

		_lblAssetCount = new Label();
		_lblAssetCount.Text = "0 " + TranslationServer.Translate("assets");
		_lblAssetCount.AddThemeFontSizeOverride("font_size", 11);
		_lblAssetCount.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlowDim);
		headerRow.AddChild(_lblAssetCount);

		BodyContainer.AddChild(headerRow);

		// 2. ACTION BUTTONS TOOLBAR
		var actionsRow = new HBoxContainer();
		actionsRow.AddThemeConstantOverride("separation", 6);

		_btnImportAsset = AddButton(actionsRow, "📥 " + TranslationServer.Translate("Import Asset"), () => OpenImportFileDialog(), "Import an asset from asset index or disk", 11, new Vector2(110, 26));
		_btnImportShader = AddButton(actionsRow, "✨ " + TranslationServer.Translate("Import Shader"), () => OnImportShaderPressed(), "Import shader configuration file", 11, new Vector2(115, 26));
		_btnConvertImage = AddButton(actionsRow, "🔄 " + TranslationServer.Translate("Convert Image (.rtex)"), () => OnConvertImagePressed(), "Convert image to Realm RTEX format", 11, new Vector2(150, 26));
		_btnConvertAudio = AddButton(actionsRow, "🔄 " + TranslationServer.Translate("Convert Audio (.raud)"), () => OnConvertAudioPressed(), "Convert audio file to Realm RAUD/OGG format", 11, new Vector2(145, 26));
		_btnConvert3DModel = AddButton(actionsRow, "🔄 " + TranslationServer.Translate("Convert 3D Model (.rmesh)"), () => OnConvert3DModelPressed(), "Optimize and convert 3D model to RMESH format", 11, new Vector2(165, 26));

		var btnWebAi = new Button();
		btnWebAi.Set("icon_max_width", 16);
		btnWebAi.AddThemeConstantOverride("icon_max_width", 16);
		btnWebAi.ExpandIcon = false;
		btnWebAi.IconAlignment = HorizontalAlignment.Center;
		btnWebAi.VerticalIconAlignment = VerticalAlignment.Center;
		if (ResourceLoader.Exists("res://Assets/UI/globe_icon.png"))
		{
			btnWebAi.Icon = GD.Load<Texture2D>("res://Assets/UI/globe_icon.png");
		}
		else
		{
			btnWebAi.Text = "🌐";
		}
		btnWebAi.TooltipText = TranslationServer.Translate("Generate 3D Model with AI Online");
		btnWebAi.CustomMinimumSize = new Vector2(24, 24);
		btnWebAi.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		btnWebAi.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		btnWebAi.FocusMode = FocusModeEnum.None;
		btnWebAi.Pressed += () => OS.ShellOpen("https://3d.hunyuanglobal.com");
		actionsRow.AddChild(btnWebAi);
		_btnConvertMixamo = AddButton(actionsRow, "🔄 " + TranslationServer.Translate("Convert Mixamo (.ranim)"), () => OnConvertMixamoPressed(), "Extract and convert Mixamo animations to RANIM format", 11, new Vector2(165, 26));
		_btnGenerateNoise = AddButton(actionsRow, "🎲 " + TranslationServer.Translate("Generate Noise"), () => Hud?.OpenNoiseTextureDialog((_) => RefreshAssetList()), "Generate procedural noise texture", 11, new Vector2(120, 26));
		_btnPruneUnused = AddButton(actionsRow, "🗑 " + TranslationServer.Translate("Prune Unused"), () => PruneUnusedAssets(), "Remove assets not referenced anywhere in the map", 11, new Vector2(110, 26));
		_btnNormalize = AddButton(actionsRow, "🔗 " + TranslationServer.Translate("Normalize References"), () => NormalizeGreenlitReferences(), "Normalize shared assets from greenlit maps", 11, new Vector2(150, 26));

		BodyContainer.AddChild(actionsRow);

		// 3. SCROLLABLE ASSET GRID (Extra Large Icons)
		var gridPanel = new PanelContainer();
		gridPanel.CustomMinimumSize = new Vector2(0, 360);
		gridPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
		gridPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		BodyContainer.AddChild(gridPanel);

		_scrollContainer = new ScrollContainer();
		_scrollContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		_scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		_scrollContainer.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
		gridPanel.AddChild(_scrollContainer);

		_virtualGridContent = new Control();
		_virtualGridContent.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_virtualGridContent.MouseFilter = MouseFilterEnum.Pass;
		_scrollContainer.AddChild(_virtualGridContent);

		_lblEmptyState = new Label();
		_lblEmptyState.Text = TranslationServer.Translate("No matching assets found in manifest.json.");
		_lblEmptyState.HorizontalAlignment = HorizontalAlignment.Center;
		_lblEmptyState.VerticalAlignment = VerticalAlignment.Center;
		_lblEmptyState.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lblEmptyState.SizeFlagsVertical = SizeFlags.ExpandFill;
		_lblEmptyState.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_lblEmptyState.AddThemeFontSizeOverride("font_size", 12);
		_lblEmptyState.Visible = false;
		gridPanel.AddChild(_lblEmptyState);

		_scrollContainer.GetVScrollBar().ValueChanged += (_) => UpdateVisibleGridCells();
		_scrollContainer.Resized += () =>
		{
			UpdateVirtualGridSize();
			UpdateVisibleGridCells();
		};

		// 4. FOOTER DETAILS PANEL
		_footerPanel = new PanelContainer();
		_footerPanel.CustomMinimumSize = new Vector2(0, 95);
		_footerPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		var detailStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.08f, 0.09f, 0.11f, 0.95f),
			BorderColor = new Color(0.25f, 0.23f, 0.20f, 0.8f)
		};
		detailStyle.SetBorderWidthAll(1);
		detailStyle.CornerRadiusTopLeft = 4;
		detailStyle.CornerRadiusTopRight = 4;
		detailStyle.CornerRadiusBottomLeft = 4;
		detailStyle.CornerRadiusBottomRight = 4;
		detailStyle.ContentMarginLeft = 10;
		detailStyle.ContentMarginRight = 10;
		detailStyle.ContentMarginTop = 8;
		detailStyle.ContentMarginBottom = 8;
		_footerPanel.AddThemeStyleboxOverride("panel", detailStyle);
		BodyContainer.AddChild(_footerPanel);

		var footerHBox = new HBoxContainer();
		footerHBox.AddThemeConstantOverride("separation", 12);
		_footerPanel.AddChild(footerHBox);

		_footerThumbnail = new TextureRect();
		_footerThumbnail.CustomMinimumSize = new Vector2(74, 74);
		_footerThumbnail.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_footerThumbnail.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		footerHBox.AddChild(_footerThumbnail);

		var footerInfoVBox = new VBoxContainer();
		footerInfoVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		footerInfoVBox.AddThemeConstantOverride("separation", 3);
		footerHBox.AddChild(footerInfoVBox);

		var titleRow = new HBoxContainer();
		titleRow.AddThemeConstantOverride("separation", 8);

		_lblSelectedFileName = new Label();
		_lblSelectedFileName.Text = TranslationServer.Translate("No asset selected");
		_lblSelectedFileName.AddThemeFontSizeOverride("font_size", 12);
		_lblSelectedFileName.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		titleRow.AddChild(_lblSelectedFileName);

		_lblSelectedSize = new Label();
		_lblSelectedSize.AddThemeFontSizeOverride("font_size", 10);
		_lblSelectedSize.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		titleRow.AddChild(_lblSelectedSize);

		_btnAudioPlay = new Button();
		_btnAudioPlay.Set("icon_max_width", 0);
		_btnAudioPlay.Text = "▶ " + TranslationServer.Translate("Play");
		_btnAudioPlay.AddThemeFontSizeOverride("font_size", 10);
		_btnAudioPlay.CustomMinimumSize = new Vector2(70, 22);
		_btnAudioPlay.FocusMode = FocusModeEnum.None;
		_btnAudioPlay.TooltipText = TranslationServer.Translate("Play loaded audio");
		_btnAudioPlay.Pressed += OnAudioPlayPausePressed;
		_btnAudioPlay.Visible = false;
		titleRow.AddChild(_btnAudioPlay);

		footerInfoVBox.AddChild(titleRow);

		_lblSelectedPath = new Label();
		_lblSelectedPath.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_lblSelectedPath.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lblSelectedPath.AddThemeFontSizeOverride("font_size", 10);
		_lblSelectedPath.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		footerInfoVBox.AddChild(_lblSelectedPath);

		_lblSelectedDetails = new Label();
		_lblSelectedDetails.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_lblSelectedDetails.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lblSelectedDetails.AddThemeFontSizeOverride("font_size", 10);
		_lblSelectedDetails.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlowDim);
		footerInfoVBox.AddChild(_lblSelectedDetails);

		// Right side buttons in footer: Trash Can Icon
		var footerBtnVBox = new VBoxContainer();
		footerBtnVBox.Alignment = BoxContainer.AlignmentMode.Center;
		footerBtnVBox.AddThemeConstantOverride("separation", 6);
		footerHBox.AddChild(footerBtnVBox);

		_btnDeleteAsset = new Button();
		_btnDeleteAsset.Set("icon_max_width", 0);
		_btnDeleteAsset.Text = "🗑 " + TranslationServer.Translate("Delete");
		_btnDeleteAsset.AddThemeFontSizeOverride("font_size", 11);
		_btnDeleteAsset.AddThemeColorOverride("font_color", new Color(0.95f, 0.4f, 0.4f));
		_btnDeleteAsset.CustomMinimumSize = new Vector2(90, 28);
		_btnDeleteAsset.FocusMode = FocusModeEnum.None;
		_btnDeleteAsset.TooltipText = TranslationServer.Translate("Remove asset from manifest.json with orphan protection check");
		_btnDeleteAsset.Pressed += OnDeleteAssetClicked;
		_btnDeleteAsset.Disabled = true;
		footerBtnVBox.AddChild(_btnDeleteAsset);

		UpdateActionButtonVisibility();
	}

	private void UpdateActionButtonVisibility()
	{
		bool isAll = _selectedAssetType == "All";
		bool isShader = _selectedAssetType == "Shader";
		bool isImage = isAll || _selectedAssetType is "Terrain" or "Spritesheet" or "vfx_radial" or "vfx_vertical" or "Icon" or "Decal" or "Ribbon" or "Noise" or "Skybox";
		bool isAudio = isAll || _selectedAssetType is "Music" or "SoundEffect";
		bool isModel = isAll || _selectedAssetType is "Character" or "Building" or "Prop" or "Item";
		bool isAnim = isAll || _selectedAssetType == "Animation";
		bool isNoise = isAll || _selectedAssetType == "Noise";

		if (_btnImportShader != null) _btnImportShader.Visible = isShader || isAll;
		if (_btnConvertImage != null) _btnConvertImage.Visible = isImage;
		if (_btnConvertAudio != null) _btnConvertAudio.Visible = isAudio;
		if (_btnConvert3DModel != null) _btnConvert3DModel.Visible = isModel;
		if (_btnConvertMixamo != null) _btnConvertMixamo.Visible = isAnim;
		if (_btnGenerateNoise != null) _btnGenerateNoise.Visible = isNoise;
	}

	private string GetWorkspacePath()
	{
		if (!string.IsNullOrEmpty(Hud?.TempWorkspacePath))
		{
			return Hud.TempWorkspacePath;
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

	public void RefreshAssetList()
	{
		string wsPath = GetWorkspacePath();
		_matchingAssets.Clear();

		string manifestPath = Path.Combine(wsPath, "manifest.json");
		if (!File.Exists(manifestPath))
		{
			_lblAssetCount.Text = "0 " + TranslationServer.Translate("assets");
			_lblEmptyState.Visible = true;
			UpdateVirtualGridSize();
			UpdateVisibleGridCells();
			UpdateSelectedAssetDisplay();
			return;
		}

		try
		{
			string manifestJson = File.ReadAllText(manifestPath);
			if (string.IsNullOrWhiteSpace(manifestJson)) return;
			var manifestDoc = JsonNode.Parse(manifestJson)?.AsObject();
			var assetsObj = manifestDoc?["Assets"]?.AsObject();

			if (assetsObj != null)
			{
				foreach (var categoryKvp in assetsObj)
				{
					string groupKey = categoryKvp.Key.ToLowerInvariant();
					string normalizedCategory = MapAssetHelper.NormalizeCategoryKey(categoryKvp.Key);

					if (categoryKvp.Value is JsonObject categoryDict)
					{
						foreach (var itemKvp in categoryDict)
						{
							string fileName = itemKvp.Key;
							string itemAssetType = string.Empty;

							if (groupKey == "ranim" || fileName.EndsWith(".ranim", StringComparison.OrdinalIgnoreCase))
							{
								itemAssetType = "Animation";
							}
							else if (groupKey == "shaders" || fileName.EndsWith(".gdshader", StringComparison.OrdinalIgnoreCase))
							{
								itemAssetType = "Shader";
							}
							else if (itemKvp.Value is JsonObject itemObj)
							{
								itemAssetType = itemObj["asset_type"]?.ToString()
									?? itemObj["AssetType"]?.ToString()
									?? string.Empty;
							}

							if (string.IsNullOrEmpty(itemAssetType))
							{
								if (!string.IsNullOrEmpty(normalizedCategory) && !normalizedCategory.Equals("other", StringComparison.OrdinalIgnoreCase) && !normalizedCategory.Equals("assets", StringComparison.OrdinalIgnoreCase))
								{
									itemAssetType = normalizedCategory;
								}
								else
								{
									string ext = Path.GetExtension(fileName).ToLowerInvariant();
									itemAssetType = ext switch
									{
										".rmesh" => "Prop",
										".rtex" => "Terrain",
										".raud" => "SoundEffect",
										".ranim" => "Animation",
										_ => "Prop"
									};
								}
							}

							if (_selectedAssetType != "All" && !string.Equals(_selectedAssetType, itemAssetType, StringComparison.OrdinalIgnoreCase))
							{
								continue;
							}

							string subFolder = itemAssetType switch
							{
								"Character" => "models/units",
								"Building" => "models/buildings",
								"Prop" => "models/props",
								"Item" => "models/items",
								"Spritesheet" => "vfx",
								"vfx_radial" => "vfx_radial",
								"vfx_vertical" => "vfx_vertical",
								"Animation" => "animations",
								"SoundEffect" => "audio/sfx",
								"Music" => "audio/music",
								"Icon" => "icons",
								"Decal" => "decals",
								"Ribbon" => "ribbons",
								"Noise" => "noise",
								"Skybox" => "skyboxes",
								"Terrain" => "textures",
								"Shader" => "shaders",
								_ => groupKey
							};

							string? fullPath = (itemAssetType is "Character" or "Building" or "Prop" or "Item")
								? MapAssetHelper.FindModelOnDisk(wsPath, subFolder, fileName)
								: MapAssetHelper.FindAssetOnDisk(wsPath, subFolder, fileName);

							if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
							{
								fullPath = Path.Combine(wsPath, "Assets", subFolder, fileName);
							}

							string blake3 = itemKvp.Value?.ToString() ?? string.Empty;
							long fileSize = File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0;
							DateTime lastModified = File.Exists(fullPath) ? File.GetLastWriteTimeUtc(fullPath) : DateTime.UtcNow;

							var tags = new List<string>();
							if (File.Exists(fullPath))
							{
								string? metaJson = RealmMetadataHelper.ExtractMetadata(fullPath);
								if (!string.IsNullOrEmpty(metaJson))
								{
									try
									{
										var metaObj = JsonNode.Parse(metaJson)?.AsObject();
										if (metaObj?["tags"] is JsonArray tArr)
										{
											foreach (var t in tArr)
											{
												if (t != null) tags.Add(t.ToString());
											}
										}
									}
									catch { }
								}
							}

							if (!string.IsNullOrEmpty(_searchFilterText))
							{
								bool matchName = fileName.IndexOf(_searchFilterText, StringComparison.OrdinalIgnoreCase) >= 0;
								bool matchTag = tags.Any(t => t.IndexOf(_searchFilterText, StringComparison.OrdinalIgnoreCase) >= 0);
								if (!matchName && !matchTag)
								{
									continue;
								}
							}

							var asset = new IndexedAsset
							{
								FilePath = fullPath,
								FileName = fileName,
								Extension = Path.GetExtension(fileName),
								FileSizeBytes = fileSize,
								LastModifiedUtc = lastModified,
								Blake3 = blake3,
								AssetType = itemAssetType,
								Tags = tags
							};

							_matchingAssets.Add(asset);
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetManagerDialog] RefreshAssetList error: {ex.Message}");
		}

		_matchingAssets.Sort((a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));

		_lblAssetCount.Text = $"{_matchingAssets.Count} " + TranslationServer.Translate("assets");
		_lblEmptyState.Visible = _matchingAssets.Count == 0;

		if (_selectedAsset != null && !_matchingAssets.Any(a => string.Equals(a.FilePath, _selectedAsset.FilePath, StringComparison.OrdinalIgnoreCase)))
		{
			_selectedAsset = null;
		}

		UpdateVirtualGridSize();
		UpdateVisibleGridCells();
		UpdateSelectedAssetDisplay();
	}

	public void RefreshAssetListAndSelect(string preferredAssetKey)
	{
		RefreshAssetList();
		if (!string.IsNullOrEmpty(preferredAssetKey))
		{
			string targetFileName = Path.GetFileName(preferredAssetKey);
			var target = _matchingAssets.FirstOrDefault(a => string.Equals(a.FileName, targetFileName, StringComparison.OrdinalIgnoreCase));
			if (target != null)
			{
				_selectedAsset = target;
				UpdateSelectedAssetDisplay();
				UpdateVisibleGridCells();
			}
		}
	}

	private void UpdateVirtualGridSize()
	{
		float availableWidth = Mathf.Max(100.0f, _scrollContainer.Size.X - GridPadding * 2.0f);
		int columns = Math.Max(1, (int)((availableWidth + SpacingX) / (CellWidth + SpacingX)));
		int totalRows = (_matchingAssets.Count + columns - 1) / columns;
		float totalHeight = _matchingAssets.Count > 0
			? (GridPadding * 2.0f + totalRows * CellHeight + Math.Max(0, totalRows - 1) * SpacingY)
			: 0;

		_virtualGridContent.CustomMinimumSize = new Vector2(0, totalHeight);
	}

	private void UpdateVisibleGridCells()
	{
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
			foreach (var cell in _cellPool)
			{
				cell.Visible = false;
			}
			return;
		}

		float scrollY = (float)_scrollContainer.GetVScrollBar().Value;
		float viewHeight = _scrollContainer.Size.Y;
		if (viewHeight <= 0) viewHeight = 360.0f;

		int startRow = Math.Max(0, (int)((scrollY - GridPadding) / (CellHeight + SpacingY)) - 1);
		int endRow = Math.Min(totalRows - 1, (int)((scrollY + viewHeight - GridPadding) / (CellHeight + SpacingY)) + 1);

		int startIndex = Math.Max(0, startRow * columns);
		int endIndex = Math.Min(_matchingAssets.Count - 1, (endRow + 1) * columns - 1);
		int visibleCount = endIndex >= startIndex ? (endIndex - startIndex + 1) : 0;

		while (_cellPool.Count < visibleCount)
		{
			var newCell = new AssetGridCell();
			_cellPool.Add(newCell);
			_virtualGridContent.AddChild(newCell);
		}

		for (int i = 0; i < visibleCount; i++)
		{
			int assetIndex = startIndex + i;
			var asset = _matchingAssets[assetIndex];
			int row = assetIndex / columns;
			int col = assetIndex % columns;

			float posX = GridPadding + col * (CellWidth + SpacingX);
			float posY = GridPadding + row * (CellHeight + SpacingY);

			var cell = _cellPool[i];
			cell.Position = new Vector2(posX, posY);
			cell.Size = new Vector2(CellWidth, CellHeight);
			cell.Visible = true;

			bool isSelected = _selectedAsset != null && string.Equals(_selectedAsset.FilePath, asset.FilePath, StringComparison.OrdinalIgnoreCase);
			cell.Bind(asset, isSelected, OnAssetCellClicked);
		}

		for (int i = visibleCount; i < _cellPool.Count; i++)
		{
			_cellPool[i].Visible = false;
		}
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

		if (_selectedAsset != null)
		{
			_lblSelectedFileName.Text = _selectedAsset.FileName;
			_lblSelectedPath.Text = _selectedAsset.FilePath;
			_lblSelectedSize.Text = FormatFileSize(_selectedAsset.FileSizeBytes);
			_footerThumbnail.Texture = AssetThumbnailProvider.GetThumbnail(_selectedAsset);
			_btnDeleteAsset.Disabled = false;

			string metaSummary = "";
			if (File.Exists(_selectedAsset.FilePath))
			{
				string? metaJson = RealmMetadataHelper.ExtractMetadata(_selectedAsset.FilePath);
				if (!string.IsNullOrEmpty(metaJson))
				{
					metaSummary = $"{TranslationServer.Translate("Type")}: {_selectedAsset.AssetType} • {TranslationServer.Translate("Metadata Header")}: {metaJson}";
				}
				else
				{
					metaSummary = $"{TranslationServer.Translate("Type")}: {_selectedAsset.AssetType} • BLAKE3: {_selectedAsset.Blake3}";
				}
			}
			_lblSelectedDetails.Text = metaSummary;

			string ext = _selectedAsset.Extension?.ToLowerInvariant() ?? "";
			bool isAudio = ext is ".raud" or ".ogg" or ".wav" or ".mp3" or ".flac" or ".aac";
			if (isAudio && File.Exists(_selectedAsset.FilePath))
			{
				try
				{
					if (ext == ".raud")
					{
						byte[] raudBytes = File.ReadAllBytes(_selectedAsset.FilePath);
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
						var oggStream = AudioStreamOggVorbis.LoadFromFile(_selectedAsset.FilePath);
						if (oggStream != null)
						{
							oggStream.Loop = false;
							_audioPlayer.Stream = oggStream;
						}
					}
					else
					{
						_audioPlayer.Stream = GD.Load<AudioStream>(_selectedAsset.FilePath);
					}
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[AssetManagerDialog] Failed to load audio stream: {ex.Message}");
				}
				_btnAudioPlay.Visible = (_audioPlayer.Stream != null);
				_btnAudioPlay.Text = "▶ " + TranslationServer.Translate("Play");
			}
			else
			{
				_btnAudioPlay.Visible = false;
			}
		}
		else
		{
			_lblSelectedFileName.Text = TranslationServer.Translate("No asset selected");
			_lblSelectedPath.Text = string.Empty;
			_lblSelectedSize.Text = string.Empty;
			_lblSelectedDetails.Text = string.Empty;
			_footerThumbnail.Texture = null;
			_btnAudioPlay.Visible = false;
			_btnDeleteAsset.Disabled = true;
		}
	}

	private void OnAudioPlayPausePressed()
	{
		if (_audioPlayer == null || _audioPlayer.Stream == null) return;

		if (_audioPlayer.Playing)
		{
			_audioPlayer.Stop();
			_btnAudioPlay.Text = "▶ " + TranslationServer.Translate("Play");
		}
		else
		{
			_audioPlayer.Play();
			_btnAudioPlay.Text = "⏹ " + TranslationServer.Translate("Stop");
		}
	}

	private void OnAudioFinished()
	{
		if (_btnAudioPlay != null)
		{
			_btnAudioPlay.Text = "▶ " + TranslationServer.Translate("Play");
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
		if (_btnAudioPlay != null)
		{
			_btnAudioPlay.Text = "▶ " + TranslationServer.Translate("Play");
		}
	}

	private void OnDeleteAssetClicked()
	{
		if (_selectedAsset == null) return;

		string wsPath = GetWorkspacePath();
		string assetFileName = _selectedAsset.FileName;
		string categoryKey = _selectedAsset.AssetType ?? "Prop";

		// Search metadata.json for any references to the asset
		var orphanedObjects = FindOrphanedObjectsInMetadata(wsPath, assetFileName);

		if (orphanedObjects.Count > 0)
		{
			string objectListStr = string.Join("\n • ", orphanedObjects.Take(8));
			if (orphanedObjects.Count > 8)
			{
				objectListStr += $"\n • ... and {orphanedObjects.Count - 8} more";
			}

			string warningMessage = string.Format(
				TranslationServer.Translate("Warning: Asset '{0}' is referenced by {1} object(s) in metadata.json:\n\n • {2}\n\nDeleting this asset from manifest.json will orphan these objects. Are you sure you want to proceed?"),
				assetFileName, orphanedObjects.Count, objectListStr);

			Hud?.ShowConfirmationDialog(
				warningMessage,
				() => ExecuteDeleteAsset(wsPath, categoryKey, assetFileName),
				confirmText: "DELETE ANYWAY",
				cancelText: "CANCEL"
			);
		}
		else
		{
			string confirmMessage = string.Format(TranslationServer.Translate("Are you sure you want to remove asset '{0}' from manifest.json?"), assetFileName);
			Hud?.ShowConfirmationDialog(
				confirmMessage,
				() => ExecuteDeleteAsset(wsPath, categoryKey, assetFileName),
				confirmText: "DELETE",
				cancelText: "CANCEL"
			);
		}
	}

	private List<string> FindOrphanedObjectsInMetadata(string wsPath, string assetFileName)
	{
		var orphans = new List<string>();
		string metaPath = Path.Combine(wsPath, "metadata.json");
		if (!File.Exists(metaPath)) return orphans;

		try
		{
			string metaJson = File.ReadAllText(metaPath);
			var metaRoot = JsonNode.Parse(metaJson)?.AsObject();
			if (metaRoot == null) return orphans;

			string cleanName = Path.GetFileName(assetFileName);
			string cleanNoExt = Path.GetFileNameWithoutExtension(assetFileName);

			bool MatchString(string? val)
			{
				if (string.IsNullOrWhiteSpace(val)) return false;
				string v = val.Trim();
				return string.Equals(v, cleanName, StringComparison.OrdinalIgnoreCase) ||
					   string.Equals(v, cleanNoExt, StringComparison.OrdinalIgnoreCase) ||
					   string.Equals(Path.GetFileName(v), cleanName, StringComparison.OrdinalIgnoreCase) ||
					   string.Equals(Path.GetFileNameWithoutExtension(v), cleanNoExt, StringComparison.OrdinalIgnoreCase);
			}

			var arrayKeys = new (string Key, string Label)[]
			{
				("Units", "Unit"),
				("Buildings", "Building"),
				("Resources", "Resource"),
				("Props", "Prop"),
				("Attachments", "Attachment"),
				("Weapons", "Weapon"),
				("Abilities", "Ability"),
				("TechTree", "Tech"),
				("StatusEffects", "Status Effect")
			};

			JsonObject? templatesObj = null;
			if (metaRoot.TryGetPropertyValue("Templates", out var templatesNode) && templatesNode is JsonObject tObj)
			{
				templatesObj = tObj;
			}

			foreach (var (arrKey, label) in arrayKeys)
			{
				if (templatesObj != null && templatesObj.TryGetPropertyValue(arrKey, out var arrNode) && arrNode is JsonArray arr)
				{
					foreach (var itemNode in arr)
					{
						if (itemNode is JsonObject obj)
						{
							string name = obj["Name"]?.ToString() ?? obj["UnitId"]?.ToString() ?? obj["AbilityId"]?.ToString() ?? obj["Id"]?.ToString() ?? "Unknown";
							bool matched = false;

							foreach (var prop in obj)
							{
								if (prop.Value is JsonValue jv && MatchString(jv.ToString()))
								{
									matched = true;
									break;
								}
							}

							if (matched)
							{
								orphans.Add($"{label}: {name}");
							}
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetManagerDialog] FindOrphanedObjectsInMetadata error: {ex.Message}");
		}

		return orphans;
	}

	private void ExecuteDeleteAsset(string wsPath, string categoryKey, string assetFileName)
	{
		try
		{
			MapAssetHelper.RemoveManifestAsset(wsPath, categoryKey, assetFileName);
			string subFolder = categoryKey switch
			{
				"Character" => "models/units",
				"Building" => "models/buildings",
				"Prop" => "models/props",
				"Item" => "models/items",
				"Spritesheet" => "vfx",
				"vfx_radial" => "vfx_radial",
				"vfx_vertical" => "vfx_vertical",
				"Animation" => "animations",
				"SoundEffect" => "audio/sfx",
				"Music" => "audio/music",
				"Icon" => "icons",
				"Decal" => "decals",
				"Ribbon" => "ribbons",
				"Noise" => "noise",
				"Skybox" => "skyboxes",
				"Terrain" => "textures",
				"Shader" => "shaders",
				_ => categoryKey.ToLowerInvariant()
			};

			string fullPath = Path.Combine(wsPath, "Assets", subFolder, assetFileName);
			if (File.Exists(fullPath))
			{
				File.Delete(fullPath);
			}

			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Deleted asset '{0}' from manifest.json"), assetFileName));
			RefreshAssetList();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetManagerDialog] ExecuteDeleteAsset error: {ex.Message}");
			Hud?.ShowFeedback($"Error deleting asset: {ex.Message}");
		}
	}

	private void OpenImportFileDialog()
	{
		string[] extensions = _selectedAssetType switch
		{
			"Character" or "Building" or "Prop" or "Item" => new[] { ".rmesh" },
			"Terrain" or "Spritesheet" or "vfx_radial" or "vfx_vertical" or "Icon" or "Decal" or "Ribbon" or "Noise" or "Skybox" => new[] { ".rtex" },
			"Animation" => new[] { ".ranim" },
			"SoundEffect" or "Music" => new[] { ".raud" },
			_ => new[] { ".rmesh", ".rtex", ".ranim", ".raud" }
		};

		string? requiredType = _selectedAssetType == "All" ? null : _selectedAssetType;
		Hud?.OpenAssetBrowser($"Import Asset ({_selectedAssetType})", extensions, (filePath, preferredName) => OnImportFileSelected(filePath, preferredName), requireRealmMetadata: true, requiredAssetType: requiredType);
	}

	private void OnImportFileSelected(string sourceFilePath, string? preferredFileName = null)
	{
		if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath)) return;

		string wsPath = GetWorkspacePath();
		string fileName = !string.IsNullOrWhiteSpace(preferredFileName) && !AssetIndexService.IsHexHash(Path.GetFileNameWithoutExtension(preferredFileName))
			? preferredFileName
			: (AssetIndexService.Instance?.ResolvePrettyFileName(sourceFilePath, preferredFileName) ?? Path.GetFileName(sourceFilePath));
		string ext = Path.GetExtension(fileName).ToLowerInvariant();

		try
		{
			string? embeddedAssetType = RealmMetadataHelper.ExtractAssetType(sourceFilePath);
			string targetCategory = !string.IsNullOrEmpty(embeddedAssetType)
				? MapAssetHelper.NormalizeCategoryKey(embeddedAssetType)
				: (_selectedAssetType != "All" ? _selectedAssetType : (ext switch
				{
					".rmesh" => "Prop",
					".rtex" => "Terrain",
					".ranim" => "Animation",
					".raud" => "SoundEffect",
					_ => "Prop"
				}));

			string subFolder = targetCategory switch
			{
				"Character" => "models/units",
				"Building" => "models/buildings",
				"Prop" => "models/props",
				"Item" => "models/items",
				"Spritesheet" => "vfx",
				"vfx_radial" => "vfx_radial",
				"vfx_vertical" => "vfx_vertical",
				"Animation" => "animations",
				"SoundEffect" => "audio/sfx",
				"Music" => "audio/music",
				"Icon" => "icons",
				"Decal" => "decals",
				"Ribbon" => "ribbons",
				"Noise" => "noise",
				"Skybox" => "skyboxes",
				"Terrain" => "textures",
				"Shader" => "shaders",
				_ => targetCategory.ToLowerInvariant()
			};

			string destDir = Path.Combine(wsPath, "Assets", subFolder);
			Directory.CreateDirectory(destDir);
			string destPath = Path.Combine(destDir, fileName);

			File.Copy(sourceFilePath, destPath, true);
			RealmMetadataHelper.EnsureMetadata(destPath);
			byte[] finalBytes = File.ReadAllBytes(destPath);
			string hash = RealmMetadataHelper.ComputeBlake3(finalBytes, ext);

			MapAssetHelper.UpdateManifestAsset(wsPath, targetCategory, fileName, hash);

			EnsureTemplateForAsset(wsPath, targetCategory, fileName, hash);

			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Imported asset '{0}' into {1}"), fileName, targetCategory));
			RefreshAssetListAndSelect(fileName);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetManagerDialog] OnImportFileSelected error: {ex.Message}");
			Hud?.ShowFeedback($"Error importing asset: {ex.Message}");
		}
	}

	private void OnImportShaderPressed()
	{
		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Select Shader File to Import"),
			PathUtils.GetProjectRoot(),
			"",
			false,
			DisplayServer.FileDialogMode.OpenFile,
			new[] { "*.json,*.gdshader,*.shader ; Supported Shader Files (*.json, *.gdshader, *.shader)", "*.json ; JSON Shader Files (*.json)", "*.gdshader,*.shader ; Godot Shader Files (*.gdshader, *.shader)" },
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
			{
				if (status && selectedPaths.Length > 0)
				{
					ImportShaderFromFile(selectedPaths[0]);
				}
			})
		);

		if (err != Error.Ok)
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Failed to show file dialog"));
		}
	}

	private void ImportShaderFromFile(string sourceFilePath)
	{
		if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath)) return;

		string wsPath = GetWorkspacePath();
		string ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();

		try
		{
			if (ext == ".json")
			{
				string jsonText = File.ReadAllText(sourceFilePath);
				var rootNode = JsonNode.Parse(jsonText);
				if (rootNode is JsonObject rootObj)
				{
					if (rootObj.TryGetPropertyValue("shaders", out var shadersNode) && shadersNode is JsonObject shadersObj)
					{
						int count = 0;
						string lastKey = "";
						foreach (var kvp in shadersObj)
						{
							if (kvp.Value != null)
							{
								var config = CustomShaderConfig.FromJson(kvp.Key, kvp.Value);
								SpawnDeathShaderManager.SaveCustomShader(config, wsPath);
								EnsureTemplateForAsset(wsPath, "SpawnShader", config.Key, string.Empty);
								lastKey = config.Key;
								count++;
							}
						}
						RefreshAssetListAndSelect(lastKey);
						Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Imported {0} shaders successfully."), count));
						return;
					}
					else if (rootObj.ContainsKey("transition_mode") || rootObj.ContainsKey("edge_color") || rootObj.ContainsKey("name") || rootObj.ContainsKey("TransitionMode"))
					{
						string defaultKey = Path.GetFileNameWithoutExtension(sourceFilePath).ToLowerInvariant().Replace(" ", "_");
						string key = rootObj.TryGetPropertyValue("key", out var keyNode) && !string.IsNullOrWhiteSpace(keyNode?.ToString())
							? keyNode.ToString()
							: (rootObj.TryGetPropertyValue("Key", out var keyNodeCap) && !string.IsNullOrWhiteSpace(keyNodeCap?.ToString()) ? keyNodeCap.ToString() : defaultKey);
						var config = CustomShaderConfig.FromJson(key, rootObj);
						SpawnDeathShaderManager.SaveCustomShader(config, wsPath);
						EnsureTemplateForAsset(wsPath, "SpawnShader", config.Key, string.Empty);
						RefreshAssetListAndSelect(config.Key);
						Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Imported shader '{0}' successfully."), config.Name));
						return;
					}
				}
			}
			else if (ext is ".gdshader" or ".shader")
			{
				string fileName = Path.GetFileName(sourceFilePath);
				string targetDir = Path.Combine(wsPath, "Assets", "shaders");
				Directory.CreateDirectory(targetDir);
				string destPath = Path.Combine(targetDir, fileName);
				File.Copy(sourceFilePath, destPath, true);

				EnsureTemplateForAsset(wsPath, "Shader", fileName, string.Empty);
				RefreshAssetListAndSelect(fileName);
				Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Imported shader '{0}' successfully."), fileName));
				return;
			}

			Hud?.ShowFeedback(TranslationServer.Translate("Unrecognized shader file format."));
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetManagerDialog] ImportShaderFromFile error: {ex.Message}");
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Error importing shader: {0}"), ex.Message));
		}
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

		string wsPath = GetWorkspacePath();
		string resolvedName = AssetIndexService.Instance?.ResolvePrettyFileName(sourceFilePath) ?? Path.GetFileName(sourceFilePath);
		string cleanBase = Path.GetFileNameWithoutExtension(resolvedName).ToLowerInvariant().Replace(' ', '_');
		string ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();

		try
		{
			string targetCategory = _selectedAssetType is "Decal" or "Icon" or "Noise" or "Ribbon" or "Skybox" or "Spritesheet" or "Terrain" or "vfx_radial" or "vfx_vertical"
				? _selectedAssetType
				: "Terrain";

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
			string destPath = Path.Combine(destDir, $"{cleanBase}.rtex");

			TextureConversionResult convResult = default;
			int decalCols = 1;
			int decalRows = 1;
			int vfxCols = 4;
			int vfxRows = 4;
			float vfxFps = 20.0f;

			if (targetCategory == "Skybox")
			{
				convResult = TextureConverter.ProcessAndSaveSkybox(sourceFilePath, destPath);
			}
			else if (targetCategory == "Decal")
			{
				convResult = TextureConverter.ProcessAndSaveDecalTexture(sourceFilePath, destPath, columns: decalCols, rows: decalRows);
			}
			else if (targetCategory == "Icon")
			{
				convResult = TextureConverter.ProcessAndSaveIconTexture(sourceFilePath, destPath);
			}
			else if (targetCategory == "Spritesheet")
			{
				convResult = TextureConverter.ProcessAndSaveSpritesheet(sourceFilePath, destPath, vfxCols, vfxRows, vfxFps);
			}
			else if (targetCategory == "vfx_radial")
			{
				convResult = TextureConverter.ProcessAndSaveVfxRadialTexture(sourceFilePath, destPath);
			}
			else if (targetCategory == "vfx_vertical")
			{
				convResult = TextureConverter.ProcessAndSaveVfxVerticalTexture(sourceFilePath, destPath);
			}
			else if (targetCategory == "Ribbon")
			{
				convResult = TextureConverter.ProcessAndSaveRibbonTexture(sourceFilePath, destPath);
			}
			else if (targetCategory == "Noise")
			{
				convResult = TextureConverter.ProcessAndSaveSingleLayerTexture(sourceFilePath, destPath, "Noise");
			}
			else
			{
				convResult = TextureConverter.ProcessAndSaveTerrainTexture(sourceFilePath, destPath);
			}

			if (!convResult.Success)
			{
				Hud?.ShowFeedback($"Failed to convert image: {convResult.ErrorMessage}");
				return;
			}

			byte[] bytes = File.ReadAllBytes(destPath);
			string hash = RealmMetadataHelper.ComputeBlake3(bytes, ".rtex");

			MapAssetHelper.UpdateManifestAsset(wsPath, targetCategory, $"{cleanBase}.rtex", hash);
			EnsureTemplateForAsset(wsPath, targetCategory, $"{cleanBase}.rtex", hash);

			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Converted and imported {0}.rtex"), cleanBase));
			AssetIndexService.Instance?.RescanAllDirectories();
			RefreshAssetListAndSelect($"{cleanBase}.rtex");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetManagerDialog] ConvertImageToRealmFormat error: {ex.Message}");
			Hud?.ShowFeedback($"Error converting image: {ex.Message}");
		}
	}

	public static void EnsureTemplateForAsset(string wsPath, string category, string fileName, string hash = "")
	{
		if (string.IsNullOrWhiteSpace(wsPath) || string.IsNullOrWhiteSpace(fileName)) return;

		try
		{
			string slug = TemplateIDHelper.GenerateSlug(fileName);
			string normalizedCategory = MapAssetHelper.NormalizeCategoryKey(category);

			MetadataService.Instance.UpdateMetadata(wsPath, meta =>
			{
				switch (normalizedCategory.ToLowerInvariant())
				{
					case "character" or "unit" or "characters" or "units":
						string unitTemplateId = TemplateIDHelper.NormalizeTemplateID("unit", slug);
						meta.Templates ??= new();
						meta.Templates.Units ??= new();
						if (!meta.Templates.Units.Any(u => string.Equals(u.TemplateID, unitTemplateId, StringComparison.OrdinalIgnoreCase)))
						{
							meta.Templates.Units.Add(new UnitMetadata
							{
								TemplateID = unitTemplateId,
								Name = slug,
								Description = "",
								ModelPath = fileName,
								Scale = 1.0f,
								PathingType = 9,
								DespillPlayerColor = false,
								NormalizeLuminance = true
							});
						}
						break;

					case "building" or "buildings":
						string buildingTemplateId = TemplateIDHelper.NormalizeTemplateID("building", slug);
						meta.Templates ??= new();
						meta.Templates.Buildings ??= new();
						if (!meta.Templates.Buildings.Any(b => string.Equals(b.TemplateID, buildingTemplateId, StringComparison.OrdinalIgnoreCase)))
						{
							meta.Templates.Buildings.Add(new UnitMetadata
							{
								TemplateID = buildingTemplateId,
								Name = slug,
								Description = "",
								ModelPath = fileName,
								Scale = 1.5f,
								PathingType = 32,
								DespillPlayerColor = false,
								NormalizeLuminance = true
							});
						}
						break;

					case "prop" or "props":
						string propTemplateId = TemplateIDHelper.NormalizeTemplateID("prop", slug);
						meta.Templates ??= new();
						meta.Templates.Props ??= new();
						if (!meta.Templates.Props.Any(p => string.Equals(p.TemplateID, propTemplateId, StringComparison.OrdinalIgnoreCase)))
						{
							meta.Templates.Props.Add(new PropMetadata
							{
								TemplateID = propTemplateId,
								Name = slug,
								Description = "",
								ModelPath = fileName,
								Scale = 1.25f,
								PathingType = 255,
								DespillPlayerColor = false,
								NormalizeLuminance = true,
								IgnorePlayerColor = true
							});
						}
						break;

					case "resource" or "resources":
						string resourceTemplateId = TemplateIDHelper.NormalizeTemplateID("resource", slug);
						meta.Templates ??= new();
						meta.Templates.Resources ??= new();
						if (!meta.Templates.Resources.Any(r => string.Equals(r.TemplateID, resourceTemplateId, StringComparison.OrdinalIgnoreCase)))
						{
							meta.Templates.Resources.Add(new ResourceMetadata
							{
								TemplateID = resourceTemplateId,
								Name = slug,
								Description = "",
								ModelPath = fileName,
								Scale = 2.75f,
								PathingType = 255,
								DespillPlayerColor = false,
								NormalizeLuminance = true,
								IgnorePlayerColor = true
							});
						}
						break;

					case "item" or "items":
						if (fileName.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
						{
							string itemTemplateId = TemplateIDHelper.NormalizeTemplateID("item", slug);
							meta.Templates ??= new();
							meta.Templates.Items ??= new();
							if (!meta.Templates.Items.Any(i => string.Equals(i.TemplateID, itemTemplateId, StringComparison.OrdinalIgnoreCase)))
							{
								meta.Templates.Items.Add(new ItemMetadata
								{
									TemplateID = itemTemplateId,
									Name = slug,
									Description = "",
									ItemClass = "consumable",
									IconPath = fileName,
									CanDrop = true
								});
							}
						}
						break;

					case "decal" or "decals":
						string decalTemplateId = TemplateIDHelper.NormalizeTemplateID("decal", slug);
						meta.Decals ??= new(StringComparer.OrdinalIgnoreCase);
						if (!meta.Decals.ContainsKey(decalTemplateId))
						{
							meta.Decals[decalTemplateId] = new DecalMetadata
							{
								TexturePath = fileName,
								Opacity = 1.0f,
								Brightness = 1.0f,
								Contrast = 1.0f,
								Saturation = 1.0f
							};
						}
						break;

					case "terrain" or "textures":
						string terrainTemplateId = TemplateIDHelper.NormalizeTemplateID("terrain", slug);
						meta.Textures ??= new(StringComparer.OrdinalIgnoreCase);
						if (!meta.Textures.ContainsKey(terrainTemplateId))
						{
							var occupiedSlots = new bool[TextureSwatchSlots.MaxSlots];
							foreach (var existingTex in meta.Textures.Values)
							{
								if (existingTex != null && existingTex.SwatchIndex >= 0 && existingTex.SwatchIndex < TextureSwatchSlots.MaxSlots)
								{
									occupiedSlots[existingTex.SwatchIndex] = true;
								}
							}
							int nextFreeSlot = TextureSwatchSlots.FirstFreeSlot(occupiedSlots);
							if (nextFreeSlot < 0) nextFreeSlot = 0;

							meta.Textures[terrainTemplateId] = new TextureMetadata
							{
								TexturePath = fileName,
								SwatchIndex = nextFreeSlot,
								ScaleFactor = 1.0f,
								Brightness = 1.0f,
								Contrast = 1.0f,
								Saturation = 1.0f,
								TileMode = "Stochastic",
								DefaultPathingCode = 8 | 32 | 4,
								DecalBombingRules = new(),
								VfxBombingRules = new()
							};
						}
						break;

					case "spritesheet" or "spritesheets" or "vfx":
						string spritesheetTemplateId = TemplateIDHelper.NormalizeTemplateID("spritesheet", slug);
						meta.VfxSpritesheets ??= new(StringComparer.OrdinalIgnoreCase);
						if (!meta.VfxSpritesheets.ContainsKey(spritesheetTemplateId))
						{
							meta.VfxSpritesheets[spritesheetTemplateId] = new VfxMetadata
							{
								TexturePath = fileName,
								Columns = 4,
								Rows = 4,
								Fps = 20.0f
							};
						}
						break;

					case "icon" or "icons":
						string iconTemplateId = TemplateIDHelper.NormalizeTemplateID("icon", slug);
						meta.Icons ??= new(StringComparer.OrdinalIgnoreCase);
						if (!meta.Icons.ContainsKey(iconTemplateId))
						{
							meta.Icons[iconTemplateId] = new IconMetadata
							{
								TexturePath = fileName
							};
						}
						break;

					case "ribbon" or "ribbons":
						string ribbonTemplateId = TemplateIDHelper.NormalizeTemplateID("ribbon", slug);
						meta.Ribbons ??= new(StringComparer.OrdinalIgnoreCase);
						if (!meta.Ribbons.ContainsKey(ribbonTemplateId))
						{
							meta.Ribbons[ribbonTemplateId] = new RibbonMetadata
							{
								TexturePath = fileName
							};
						}
						break;

					case "noise":
						meta.NoiseTextures ??= new(StringComparer.OrdinalIgnoreCase);
						if (!meta.NoiseTextures.ContainsKey(fileName))
						{
							meta.NoiseTextures[fileName] = new TextureMetadata
							{
								TexturePath = fileName,
								ScaleFactor = 1.0f
							};
						}
						break;

					case "skybox" or "skyboxes":
						string skyboxTemplateId = TemplateIDHelper.NormalizeTemplateID("skybox", slug);
						meta.Skyboxes ??= new(StringComparer.OrdinalIgnoreCase);
						if (!meta.Skyboxes.ContainsKey(skyboxTemplateId))
						{
							meta.Skyboxes[skyboxTemplateId] = new SkyboxMetadata
							{
								TexturePath = fileName
							};
						}
						break;

					case "shader" or "shaders" or "gdshader":
						string gdshaderTemplateId = TemplateIDHelper.NormalizeTemplateID("gdshader", slug);
						meta.GdShaders ??= new(StringComparer.OrdinalIgnoreCase);
						if (!meta.GdShaders.ContainsKey(gdshaderTemplateId))
						{
							meta.GdShaders[gdshaderTemplateId] = new ShaderMetadata
							{
							};
						}
						break;

					case "spawnshader" or "spawnshaders":
						string spawnShaderTemplateId = TemplateIDHelper.NormalizeTemplateID("SpawnShader", slug);
						meta.SpawnShaders ??= new(StringComparer.OrdinalIgnoreCase);
						if (!meta.SpawnShaders.ContainsKey(spawnShaderTemplateId))
						{
							meta.SpawnShaders[spawnShaderTemplateId] = new SpawnShaderMetadata
							{
								Name = slug
							};
						}
						break;
				}
			});

			if (normalizedCategory.Equals("terrain", StringComparison.OrdinalIgnoreCase) || normalizedCategory.Equals("textures", StringComparison.OrdinalIgnoreCase))
			{
				MapEditorHUD.Instance?.SetupTextureSwatches(false);
				if (GameHost.Instance?.GroundTerrain != null)
				{
					GameHost.Instance.GroundTerrain.ClearLiveSwatchOverrides();
					GameHost.Instance.GroundTerrain.ReloadTerrainTextures(true);
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetManagerDialog] EnsureTemplateForAsset error: {ex.Message}");
		}
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
		string resolvedName = AssetIndexService.Instance?.ResolvePrettyFileName(sourceFilePath) ?? Path.GetFileName(sourceFilePath);
		string cleanBase = Path.GetFileNameWithoutExtension(resolvedName).ToLowerInvariant().Replace(' ', '_');

		try
		{
			string targetCategory = _selectedAssetType == "Music" ? "Music" : "SoundEffect";
			string sub = targetCategory == "Music" ? "music" : "sfx";
			string destDir = Path.Combine(wsPath, "Assets", "audio", sub);
			Directory.CreateDirectory(destDir);
			string destPath = Path.Combine(destDir, $"{cleanBase}.ogg");

			var res = AudioConverter.ConvertToOgg(sourceFilePath, destPath);
			if (!res.Success)
			{
				Hud?.ShowFeedback($"Failed to convert audio: {res.ErrorMessage}");
				return;
			}

			byte[] bytes = File.ReadAllBytes(destPath);
			string hash = RealmMetadataHelper.ComputeBlake3(bytes, ".ogg");

			MapAssetHelper.UpdateManifestAsset(wsPath, targetCategory, $"{cleanBase}.ogg", hash);

			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Converted and imported audio {0}.ogg"), cleanBase));
			AssetIndexService.Instance?.RescanAllDirectories();
			RefreshAssetListAndSelect($"{cleanBase}.ogg");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetManagerDialog] ConvertAudioToRealmFormat error: {ex.Message}");
			Hud?.ShowFeedback($"Error converting audio: {ex.Message}");
		}
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

			int importedCount = 0;
			int skippedCount = 0;
			string firstSavedFileName = null;

			foreach (var (animName, animData) in extracted)
			{
				var (savedFileName, blake3, alreadyExisted) = MixamoAnimationImporter.SaveAnimationWithDeduplication(animsDir, animName, animData);
				MapAssetHelper.UpdateManifestAsset(wsPath, "Animation", savedFileName, blake3);
				if (alreadyExisted) skippedCount++;
				else importedCount++;
				if (firstSavedFileName == null) firstSavedFileName = savedFileName;
			}

			if (importedCount > 0)
			{
				Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Successfully converted and imported {0} .ranim animation(s)!"), importedCount));
			}
			else
			{
				Hud?.ShowFeedback(TranslationServer.Translate("Animation(s) already existed in map workspace (identical BLAKE3 hash)."));
			}

			AssetIndexService.Instance?.RescanAllDirectories();
			RefreshAssetListAndSelect(firstSavedFileName);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetManagerDialog] ConvertMixamoFileToRanim error: {ex.Message}");
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Error converting animation: {0}"), ex.Message));
		}
	}

	private void PruneUnusedAssets()
	{
		Hud?.ShowConfirmationDialog(
			TranslationServer.Translate("Are you sure you want to prune all unused assets in manifest.json?"),
			() =>
			{
				string wsPath = GetWorkspacePath();
				MapAssetHelper.PruneNonExistentAssetsFromManifest(wsPath);
				RefreshAssetList();
				Hud?.ShowFeedback(TranslationServer.Translate("Pruned non-existent and unreferenced assets."));
			},
			confirmText: "PRUNE",
			cancelText: "CANCEL"
		);
	}

	private void NormalizeGreenlitReferences()
	{
		string wsPath = GetWorkspacePath();
		var suggestions = MapNormalizationHelper.FindGreenlitReferenceSuggestions(wsPath);
		var currentRefs = MapNormalizationHelper.GetCurrentGreenlitReferences(wsPath);

		if (suggestions.Count == 0)
		{
			if (currentRefs.Count == 0)
			{
				Hud?.ShowFeedback(TranslationServer.Translate("No shared greenlit assets found. All assets are unique to this map."));
			}
			else
			{
				Hud?.ShowFeedback(string.Format(TranslationServer.Translate("All shared greenlit assets are already normalized ({0} references active)."), currentRefs.Count));
			}
			return;
		}

		ShowNormalizeReferencesDialog(wsPath, suggestions, currentRefs);
	}

	private void ShowNormalizeReferencesDialog(string wsPath, List<GreenlitReferenceSuggestion> suggestions, List<string> currentRefs)
	{
		var overlay = new Panel();
		overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		overlay.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		overlay.ZIndex = 1200;
		AddChild(overlay);

		var cardPanel = new Panel();
		cardPanel.CustomMinimumSize = new Vector2(650, 480);
		cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		overlay.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		vbox.CustomMinimumSize = new Vector2(610, 440);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		vbox.AddThemeConstantOverride("separation", 10);
		cardPanel.AddChild(vbox);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 5) });

		var titleLabel = new Label();
		UIStyle.ApplyTitle(titleLabel, "🔗 " + TranslationServer.Translate("NORMALIZE GREENLIT REFERENCES"), 18);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(titleLabel);

		var descLabel = new Label();
		descLabel.Text = TranslationServer.Translate("The following greenlit maps contain identical assets. Referencing them allows .rmap exports to exclude these duplicate files to minimize download size for players.");
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		descLabel.AddThemeFontSizeOverride("font_size", 12);
		descLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.85f, 0.95f));
		vbox.AddChild(descLabel);

		var scroll = new ScrollContainer();
		scroll.CustomMinimumSize = new Vector2(590, 260);
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.AddChild(scroll);

		var listContainer = new VBoxContainer();
		listContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		listContainer.AddThemeConstantOverride("separation", 6);
		scroll.AddChild(listContainer);

		var checkboxes = new List<(CheckBox CheckBox, GreenlitReferenceSuggestion Suggestion)>();

		foreach (var suggestion in suggestions)
		{
			var row = new PanelContainer();
			var rowStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.16f, 0.17f, 0.22f, 0.9f),
				CornerRadiusTopLeft = 4,
				CornerRadiusTopRight = 4,
				CornerRadiusBottomLeft = 4,
				CornerRadiusBottomRight = 4
			};
			row.AddThemeStyleboxOverride("panel", rowStyle);
			listContainer.AddChild(row);

			var rowHBox = new HBoxContainer();
			rowHBox.AddThemeConstantOverride("separation", 10);
			row.AddChild(rowHBox);

			var cb = new CheckBox();
			cb.ButtonPressed = suggestion.IsSelected;
			cb.AddThemeConstantOverride("icon_max_width", 0);
			UIStyle.ApplyCheckboxStyle(cb);
			rowHBox.AddChild(cb);
			checkboxes.Add((cb, suggestion));

			var infoVBox = new VBoxContainer();
			infoVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			rowHBox.AddChild(infoVBox);

			var mapNameLabel = new Label();
			mapNameLabel.Text = $"{suggestion.MapTitle} (v{suggestion.MapVersion})";
			mapNameLabel.AddThemeFontSizeOverride("font_size", 13);
			mapNameLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			infoVBox.AddChild(mapNameLabel);

			var statsLabel = new Label();
			statsLabel.Text = string.Format(TranslationServer.Translate("{0} matching asset(s) • Estimated savings: {1}"), suggestion.MatchedAssetPaths.Count, MapStorageService.FormatBytes(suggestion.SavedBytes));
			statsLabel.AddThemeFontSizeOverride("font_size", 11);
			statsLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.8f, 0.9f));
			infoVBox.AddChild(statsLabel);
		}

		var summaryLabel = new Label();
		Action updateSummary = () =>
		{
			long totalSavings = checkboxes.Where(c => c.CheckBox.ButtonPressed).Sum(c => c.Suggestion.SavedBytes);
			int selectedCount = checkboxes.Count(c => c.CheckBox.ButtonPressed);
			summaryLabel.Text = string.Format(TranslationServer.Translate("Selected: {0} map reference(s) • Total export savings: {1}"), selectedCount, MapStorageService.FormatBytes(totalSavings));
		};
		summaryLabel.HorizontalAlignment = HorizontalAlignment.Center;
		summaryLabel.AddThemeFontSizeOverride("font_size", 12);
		summaryLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		vbox.AddChild(summaryLabel);
		updateSummary();

		foreach (var (cb, _) in checkboxes)
		{
			cb.Toggled += (_) => updateSummary();
		}

		var btnRow = new HBoxContainer();
		btnRow.Alignment = BoxContainer.AlignmentMode.Center;
		btnRow.AddThemeConstantOverride("separation", 15);
		vbox.AddChild(btnRow);

		var btnApply = new Button();
		btnApply.AddThemeConstantOverride("icon_max_width", 0);
		btnApply.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btnApply.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btnApply.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btnApply.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(btnApply, TranslationServer.Translate("APPLY REFERENCES"), 13);
		btnApply.CustomMinimumSize = new Vector2(170, 34);
		btnRow.AddChild(btnApply);

		btnApply.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			var selectedTitles = checkboxes
				.Where(c => c.CheckBox.ButtonPressed)
				.Select(c => c.Suggestion.MapTitle)
				.ToList();

			if (selectedTitles.Count > 0)
			{
				MapNormalizationHelper.ApplyGreenlitReferences(wsPath, selectedTitles);
				long saved = checkboxes.Where(c => c.CheckBox.ButtonPressed).Sum(c => c.Suggestion.SavedBytes);
				Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Applied {0} greenlit reference(s). {1} saved in .rmap exports!"), selectedTitles.Count, MapStorageService.FormatBytes(saved)));
			}
			overlay.QueueFree();
		};

		var btnCancel = new Button();
		btnCancel.AddThemeConstantOverride("icon_max_width", 0);
		btnCancel.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btnCancel.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btnCancel.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btnCancel.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(btnCancel, TranslationServer.Translate("CANCEL"), 13);
		btnCancel.CustomMinimumSize = new Vector2(110, 34);
		btnRow.AddChild(btnCancel);

		btnCancel.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			overlay.QueueFree();
		};
	}

	private static string FormatFileSize(long bytes)
	{
		if (bytes < 1024) return $"{bytes} B";
		if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
		return $"{bytes / (1024.0 * 1024.0):F2} MB";
	}
}
