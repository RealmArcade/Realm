using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Realm.Godot.Animation;
using Realm.Godot.Utils;
using Realm.Godot.Services.ModelOptimization;
using Realm.Ecs.Services;
using Realm.Shared.Textures;
using Realm.Shared.Audio;
using Realm.Shared.Metadata;
using Realm.Godot.Services;

public partial class ObjectManagerDialog : FloatingPreview3DDialogBase
{
	private Node3D _simRoot;
	private Node3D _currentModelRoot;
	private AnimatedSprite3D _vfxSprite;

	private PanelContainer _preview2DContainer;
	private TextureRect _preview2DImage;
	private Label _lblPreview2DInfo;
	private Texture2D[]? _preview2DFrames;
	private int _preview2DFrameIndex;
	private double _preview2DFrameTimer;
	private float _preview2DFps = 12.0f;

	private PanelContainer _previewAudioContainer;
	private Label _lblAudioInfo;
	private Button _btnAudioPlay;
	private AudioStreamPlayer _audioPlayer;

	private HBoxContainer _ranimBaseModelRow;
	private LineEdit _txtRanimBaseModel;
	private Action<string> _setRanimBaseModelValue;
	private string _selectedRanimBaseModel = "";

	private HBoxContainer _cameraPresetRow;

	private OptionButton _optAssetCategory;
	private Label _lblModelTypeDescription;
	private LineEdit _txtSearchFilter;
	private VBoxContainer _listVBox;

	private CustomShaderConfig _currentShaderConfig;
	private float _shaderPreviewTime = 0f;
	private bool _shaderPreviewForward = true;

	private SpritesheetAssetEditDialog _spritesheetEditDialog;
	private TerrainTextureEditDialog _textureEditDialog;
	private DecalSettingsDialog _decalEditDialog;
	private ShaderEditorDialog _shaderEditDialog;

	private string _currentCategory = "rmesh_characters";
	private string _searchFilter = "";
	private string _currentPreviewAssetKey = "";
	private string _currentPreviewAssetCategory = "";

	public ObjectManagerDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Map Assets Manager & Importer"), new Vector2(720, 780))
	{
		SetUncompressedPanelTexture("res://Assets/UI/map_editor_assets_importer.png", 34, 40, 60, 60);

		_spritesheetEditDialog = new SpritesheetAssetEditDialog(hud);
		_textureEditDialog = new TerrainTextureEditDialog(hud);
		_decalEditDialog = new DecalSettingsDialog(hud);
		_shaderEditDialog = new ShaderEditorDialog(hud);

		DefaultDistance = 5.0f;
		CameraDistance = 5.0f;
		DefaultYaw = Mathf.DegToRad(45.0f);
		DefaultPitch = Mathf.DegToRad(25.0f);
		CameraYaw = Mathf.DegToRad(45.0f);
		CameraPitch = Mathf.DegToRad(25.0f);
		DefaultTargetPosition = Vector3.Zero;
		TargetPosition = DefaultTargetPosition;

		_audioPlayer = new AudioStreamPlayer();
		_audioPlayer.Finished += OnAudioFinished;
		AddChild(_audioPlayer);

		BuildControls();
		SetFooterCloseOnly();
	}

	private void BuildControls()
	{
		BodyContainer.AddThemeConstantOverride("separation", 6);

		// 1. TOP LIVE PREVIEW SECTION (3D / 2D / Audio)
		var previewStack = new PanelContainer();
		previewStack.CustomMinimumSize = new Vector2(360, 180);
		previewStack.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		previewStack.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		BodyContainer.AddChild(previewStack);

		// 3D Viewport
		Add3DPreviewViewport(previewStack, new Vector2(360, 180));

		Setup3DEnvironment();

		// 2D Static Preview
		_preview2DContainer = new PanelContainer();
		_preview2DContainer.CustomMinimumSize = new Vector2(360, 180);
		_preview2DContainer.Visible = false;
		var preview2DVBox = new VBoxContainer();
		preview2DVBox.Alignment = BoxContainer.AlignmentMode.Center;
		preview2DVBox.AddThemeConstantOverride("separation", 6);

		_preview2DImage = new TextureRect();
		_preview2DImage.CustomMinimumSize = new Vector2(140, 140);
		_preview2DImage.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_preview2DImage.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		_preview2DImage.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		_preview2DImage.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		preview2DVBox.AddChild(_preview2DImage);

		_lblPreview2DInfo = new Label();
		_lblPreview2DInfo.HorizontalAlignment = HorizontalAlignment.Center;
		_lblPreview2DInfo.AddThemeFontSizeOverride("font_size", 10);
		_lblPreview2DInfo.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.70f));
		preview2DVBox.AddChild(_lblPreview2DInfo);

		_preview2DContainer.AddChild(preview2DVBox);
		previewStack.AddChild(_preview2DContainer);

		// Audio Preview
		_previewAudioContainer = new PanelContainer();
		_previewAudioContainer.CustomMinimumSize = new Vector2(360, 180);
		_previewAudioContainer.Visible = false;
		var audioVBox = new VBoxContainer();
		audioVBox.Alignment = BoxContainer.AlignmentMode.Center;
		audioVBox.AddThemeConstantOverride("separation", 10);

		var lblAudioIcon = new Label();
		lblAudioIcon.Text = "\uf028";
		var faFont = Hud?.GetFontAwesomeFont();
		if (faFont != null) lblAudioIcon.AddThemeFontOverride("font", faFont);
		lblAudioIcon.HorizontalAlignment = HorizontalAlignment.Center;
		lblAudioIcon.AddThemeFontSizeOverride("font_size", 36);
		audioVBox.AddChild(lblAudioIcon);

		_lblAudioInfo = new Label();
		_lblAudioInfo.Text = TranslationServer.Translate("No audio loaded");
		_lblAudioInfo.HorizontalAlignment = HorizontalAlignment.Center;
		_lblAudioInfo.AddThemeFontSizeOverride("font_size", 12);
		_lblAudioInfo.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		audioVBox.AddChild(_lblAudioInfo);

		var audioBtnRow = new HBoxContainer();
		audioBtnRow.Alignment = BoxContainer.AlignmentMode.Center;
		audioBtnRow.AddThemeConstantOverride("separation", 8);

		_btnAudioPlay = AddButton(audioBtnRow, "\uf04b " + TranslationServer.Translate("Play"), () => ToggleAudioPlayback(), "Play loaded audio", 11, new Vector2(80, 26));

		audioVBox.AddChild(audioBtnRow);
		_previewAudioContainer.AddChild(audioVBox);
		previewStack.AddChild(_previewAudioContainer);

		// 2. CAMERA PRESETS BAR
		_cameraPresetRow = AddCameraPresetToolbar(BodyContainer, includeBack: true);

		// 3. RANIM BASE MODEL DROPDOWN ROW (Visible for .ranim and shaders)
		_ranimBaseModelRow = new HBoxContainer();
		_ranimBaseModelRow.AddThemeConstantOverride("separation", 6);
		_ranimBaseModelRow.Visible = false;

		(_txtRanimBaseModel, _setRanimBaseModelValue) = AddAssetFilterDropdown(
			_ranimBaseModelRow,
			TranslationServer.Translate("Preview Mesh:"),
			_selectedRanimBaseModel,
			(all) => GetPreviewMeshModels(),
			(val) =>
			{
				_selectedRanimBaseModel = val ?? string.Empty;
				if ((_currentCategory == "animations" || _currentCategory == "shaders") && !string.IsNullOrEmpty(_currentPreviewAssetKey))
				{
					LoadPreviewForAsset(_currentCategory, _currentPreviewAssetKey);
				}
			},
			TranslationServer.Translate("Select GLB base mesh for preview..."),
			100f
		);
		BodyContainer.AddChild(_ranimBaseModelRow);

		// 4. CATEGORY DROPDOWN & ACTIONS BAR
		var catRow = new HBoxContainer();
		catRow.AddThemeConstantOverride("separation", 8);

		var lblCat = new Label();
		lblCat.Text = TranslationServer.Translate("Asset Type:");
		lblCat.AddThemeFontSizeOverride("font_size", 11);
		lblCat.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		catRow.AddChild(lblCat);

		_optAssetCategory = new OptionButton();
		_optAssetCategory.AddThemeFontSizeOverride("font_size", 11);
		_optAssetCategory.CustomMinimumSize = new Vector2(170, 26);
		_optAssetCategory.AddItem(TranslationServer.Translate("3D Models (Characters)"), 0);
		_optAssetCategory.SetItemMetadata(0, "rmesh_characters");
		_optAssetCategory.AddItem(TranslationServer.Translate("3D Models (Buildings)"), 1);
		_optAssetCategory.SetItemMetadata(1, "rmesh_buildings");
		_optAssetCategory.AddItem(TranslationServer.Translate("3D Models (Props)"), 2);
		_optAssetCategory.SetItemMetadata(2, "rmesh_props");
		_optAssetCategory.AddItem(TranslationServer.Translate("3D Models (Items)"), 3);
		_optAssetCategory.SetItemMetadata(3, "rmesh_items");
		_optAssetCategory.AddItem(TranslationServer.Translate("Terrain"), 4);
		_optAssetCategory.SetItemMetadata(4, "textures");
		_optAssetCategory.AddItem(TranslationServer.Translate("Spritesheets"), 5);
		_optAssetCategory.SetItemMetadata(5, "vfx_spritesheets");
		_optAssetCategory.AddItem(TranslationServer.Translate("VFX Radial"), 6);
		_optAssetCategory.SetItemMetadata(6, "vfx_radial");
		_optAssetCategory.AddItem(TranslationServer.Translate("VFX Vertical"), 7);
		_optAssetCategory.SetItemMetadata(7, "vfx_vertical");
		_optAssetCategory.AddItem(TranslationServer.Translate("Animations (.ranim)"), 8);
		_optAssetCategory.SetItemMetadata(8, "animations");
		_optAssetCategory.AddItem(TranslationServer.Translate("Sound Effects (SFX)"), 9);
		_optAssetCategory.SetItemMetadata(9, "sfx");
		_optAssetCategory.AddItem(TranslationServer.Translate("Music"), 10);
		_optAssetCategory.SetItemMetadata(10, "music");
		_optAssetCategory.AddItem(TranslationServer.Translate("Icons"), 11);
		_optAssetCategory.SetItemMetadata(11, "icons");
		_optAssetCategory.AddItem(TranslationServer.Translate("Decals"), 12);
		_optAssetCategory.SetItemMetadata(12, "decals");
		_optAssetCategory.AddItem(TranslationServer.Translate("Ribbons"), 13);
		_optAssetCategory.SetItemMetadata(13, "ribbons");
		_optAssetCategory.AddItem(TranslationServer.Translate("Noise"), 14);
		_optAssetCategory.SetItemMetadata(14, "noise_textures");
		_optAssetCategory.AddItem(TranslationServer.Translate("Skyboxes"), 15);
		_optAssetCategory.SetItemMetadata(15, "skyboxes");
		_optAssetCategory.AddItem(TranslationServer.Translate("Custom Shaders"), 16);
		_optAssetCategory.SetItemMetadata(16, "shaders");

		_optAssetCategory.ItemSelected += (idx) =>
		{
			string cat = _optAssetCategory.GetItemMetadata((int)idx).AsString();
			SetCurrentCategory(cat);
		};
		catRow.AddChild(_optAssetCategory);

		BodyContainer.AddChild(catRow);

		_lblModelTypeDescription = new Label();
		_lblModelTypeDescription.AddThemeFontSizeOverride("font_size", 10);
		_lblModelTypeDescription.AddThemeColorOverride("font_color", new Color(0.7f, 0.75f, 0.85f));
		_lblModelTypeDescription.AutowrapMode = TextServer.AutowrapMode.Word;
		_lblModelTypeDescription.Visible = false;
		BodyContainer.AddChild(_lblModelTypeDescription);

		// 5. SEARCH FILTER INPUT
		var searchRow = new HBoxContainer();
		searchRow.AddThemeConstantOverride("separation", 6);

		var lblSearch = new Label();
		lblSearch.Text = "\uf002 " + TranslationServer.Translate("Filter:");
		if (faFont != null) lblSearch.AddThemeFontOverride("font", faFont);
		lblSearch.AddThemeFontSizeOverride("font_size", 11);
		searchRow.AddChild(lblSearch);

		_txtSearchFilter = new LineEdit();
		_txtSearchFilter.PlaceholderText = TranslationServer.Translate("Type to filter assets by name...");
		_txtSearchFilter.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_txtSearchFilter.AddThemeFontSizeOverride("font_size", 11);
		_txtSearchFilter.TextChanged += (text) =>
		{
			_searchFilter = text ?? string.Empty;
			RefreshAssetList();
		};
		searchRow.AddChild(_txtSearchFilter);

		BodyContainer.AddChild(searchRow);

		// 6. SCROLLABLE ASSET LIST GRID
		_listVBox = CreateScrollBody(340);
	}

	private void Setup3DEnvironment()
	{
		if (PreviewSubViewport == null) return;

		_simRoot = new Node3D();
		PreviewSubViewport.AddChild(_simRoot);

		_currentModelRoot = new Node3D();
		_simRoot.AddChild(_currentModelRoot);

		_vfxSprite = new AnimatedSprite3D
		{
			Position = Vector3.Zero,
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			Transparent = true,
			AlphaCut = SpriteBase3D.AlphaCutMode.Disabled,
			Visible = false
		};
		_simRoot.AddChild(_vfxSprite);
	}

	private static bool IsRmeshCategory(string category, out string subCategory)
	{
		if (string.IsNullOrEmpty(category))
		{
			subCategory = string.Empty;
			return false;
		}
		if (category.StartsWith("rmesh_", StringComparison.OrdinalIgnoreCase))
		{
			subCategory = category.Substring(6).ToLowerInvariant();
			return true;
		}
		if (category.Equals("rmesh", StringComparison.OrdinalIgnoreCase))
		{
			subCategory = "props";
			return true;
		}
		if (category.Equals("characters", StringComparison.OrdinalIgnoreCase) || category.Equals("units", StringComparison.OrdinalIgnoreCase))
		{
			subCategory = "characters";
			return true;
		}
		if (category.Equals("buildings", StringComparison.OrdinalIgnoreCase))
		{
			subCategory = "buildings";
			return true;
		}
		if (category.Equals("props", StringComparison.OrdinalIgnoreCase) || category.Equals("resources", StringComparison.OrdinalIgnoreCase))
		{
			subCategory = "props";
			return true;
		}
		if (category.Equals("items", StringComparison.OrdinalIgnoreCase) || category.Equals("projectiles", StringComparison.OrdinalIgnoreCase) || category.Equals("attachments", StringComparison.OrdinalIgnoreCase) || category.Equals("weapons", StringComparison.OrdinalIgnoreCase))
		{
			subCategory = "items";
			return true;
		}
		subCategory = string.Empty;
		return false;
	}

	public override void OpenDialog()
	{
		base.OpenDialog();
		SetCurrentCategory(_currentCategory);
		ResetCameraDefault();
	}

	private void SetCurrentCategory(string category)
	{
		_currentCategory = category;

		if (_lblModelTypeDescription != null)
		{
			if (IsRmeshCategory(_currentCategory, out string rmeshSub))
			{
				_lblModelTypeDescription.Visible = true;
				_lblModelTypeDescription.Text = rmeshSub switch
				{
					"characters" or "units" => TranslationServer.Translate("Units: Controllable characters, heroes, monsters, and mobile entities."),
					"buildings" => TranslationServer.Translate("Buildings: Player bases, towers, barracks, and stationary structures."),
					"props" or "resources" => TranslationServer.Translate("Props: Static environmental decorations, rocks, clutter, and obstacles."),
					"items" or "projectiles" or "attachments" or "weapons" => TranslationServer.Translate("Items: Weapons, attachments, projectiles, and wearable equipment."),
					_ => ""
				};
			}
			else
			{
				_lblModelTypeDescription.Visible = false;
				_lblModelTypeDescription.Text = "";
			}
		}

		bool isRanim = _currentCategory == "animations";
		bool isShader = _currentCategory == "shaders";
		bool isRtexCategory = category is "textures" or "vfx_spritesheets" or "vfx_radial" or "vfx_vertical" or "decals" or "ribbons" or "ribbon_textures" or "noise_textures" or "skyboxes" or "icons";
		bool isAudioCategory = category is "sfx" or "music";

		if (_ranimBaseModelRow != null)
		{
			_ranimBaseModelRow.Visible = (isRanim || isShader);
			if (isRanim || isShader)
			{
				var models = GetPreviewMeshModels();
				if (string.IsNullOrEmpty(_selectedRanimBaseModel) || !models.Contains(_selectedRanimBaseModel))
				{
					_selectedRanimBaseModel = models.Count > 0 ? models[0] : string.Empty;
					_setRanimBaseModelValue?.Invoke(_selectedRanimBaseModel);
				}
			}
		}

		RefreshAssetList();

		var items = FilterAssets(GetAssetsForCategory(_currentCategory), _searchFilter);
		if (items.Count > 0)
		{
			LoadPreviewForAsset(_currentCategory, items[0].Key, items[0].SubCategory);
		}
		else
		{
			ClearPreview();
		}
	}

	private List<AssetItemInfo> FilterAssets(List<AssetItemInfo> items, string filterText)
	{
		if (string.IsNullOrWhiteSpace(filterText))
		{
			return items;
		}

		var searchTerms = filterText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (searchTerms.Length == 0)
		{
			return items;
		}

		var matchedItems = new List<(AssetItemInfo Item, int MatchCount)>();
		foreach (var item in items)
		{
			int matchCount = 0;
			foreach (var term in searchTerms)
			{
				if (item.Key != null && item.Key.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					matchCount++;
				}
			}

			if (matchCount > 0)
			{
				matchedItems.Add((item, matchCount));
			}
		}

		return matchedItems
			.OrderByDescending(m => m.MatchCount)
			.Select(m => m.Item)
			.ToList();
	}

	public void RefreshAssetList()
	{
		if (_listVBox == null) return;

		foreach (Node child in _listVBox.GetChildren())
		{
			child.QueueFree();
		}

		var items = FilterAssets(GetAssetsForCategory(_currentCategory), _searchFilter);

		if (items.Count == 0)
		{
			var lblEmpty = new Label();
			lblEmpty.Text = TranslationServer.Translate("No assets found for this category.");
			lblEmpty.AddThemeFontSizeOverride("font_size", 11);
			lblEmpty.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.70f));
			_listVBox.AddChild(lblEmpty);
			return;
		}

		foreach (var item in items)
		{
			var row = CreateAssetRow(item.Category, item.Key, item.SubCategory, item.ExtraData);
			_listVBox.AddChild(row);
		}
	}

	public void RefreshAssetListAndPreview(string preferredAssetKey)
	{
		if (_listVBox == null) return;

		foreach (Node child in _listVBox.GetChildren())
		{
			child.QueueFree();
		}

		var items = FilterAssets(GetAssetsForCategory(_currentCategory), _searchFilter);

		if (items.Count == 0)
		{
			var lblEmpty = new Label();
			lblEmpty.Text = TranslationServer.Translate("No assets found for this category.");
			lblEmpty.AddThemeFontSizeOverride("font_size", 11);
			lblEmpty.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.70f));
			_listVBox.AddChild(lblEmpty);
			return;
		}

		string targetKey = string.IsNullOrEmpty(preferredAssetKey) ? null
			: System.IO.Path.GetFileName(preferredAssetKey);

		Control targetRow = null;
		foreach (var item in items)
		{
			var row = CreateAssetRow(item.Category, item.Key, item.SubCategory, item.ExtraData);
			_listVBox.AddChild(row);
			if (targetKey != null && (string.Equals(item.Key, targetKey, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(System.IO.Path.GetFileName(item.Key), targetKey, StringComparison.OrdinalIgnoreCase)))
			{
				targetRow = row;
			}
		}

		if (targetRow != null && _listVBox.GetParent() is ScrollContainer scroll)
		{
			scroll.EnsureControlVisible(targetRow);
		}

		var match = items.FirstOrDefault(i =>
			string.Equals(i.Key, targetKey, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(System.IO.Path.GetFileName(i.Key), targetKey, StringComparison.OrdinalIgnoreCase));

		var toPreview = match.Key != null ? match : (items.Count > 0 ? items[0] : default);
		if (toPreview.Key != null)
		{
			LoadPreviewForAsset(toPreview.Category, toPreview.Key, toPreview.SubCategory);
			if (toPreview.Category == "sfx" || toPreview.Category == "music")
			{
				PlayCurrentAudio();
			}
		}
	}

	private struct AssetItemInfo
	{
		public string Category;
		public string SubCategory;
		public string Key;
		public JsonNode ExtraData;
	}

	private string GetWorkspacePath()
	{
		if (!string.IsNullOrEmpty(Hud?.TempWorkspacePath))
		{
			return Hud.TempWorkspacePath;
		}
		return MapWorkspaceService.GetActiveWorkspacePath();
	}

	private string ResolveAssetType(string fileName, string subCategoryOrFolder, JsonNode? extraData)
	{
		if (extraData is JsonObject obj)
		{
			string? typeVal = obj["asset_type"]?.ToString()
				?? obj["AssetType"]?.ToString()
				?? obj["default_asset_type"]?.ToString()
				?? obj["type"]?.ToString();
			if (!string.IsNullOrEmpty(typeVal) && Realm.Shared.Metadata.RealmMetadataHelper.IsValidAssetTypeForExtension(fileName, typeVal, out string canonical, out _))
			{
				return canonical;
			}
		}

		string wsPath = GetWorkspacePath();
		string filePath = ResolveAssetFilePath(wsPath, fileName, subCategoryOrFolder);
		if (File.Exists(filePath))
		{
			string? embeddedType = Realm.Shared.Metadata.RealmMetadataHelper.ExtractAssetType(filePath);
			if (!string.IsNullOrEmpty(embeddedType))
			{
				return embeddedType;
			}
		}

		string ext = Path.GetExtension(fileName).ToLowerInvariant();
		if (ext is ".rmesh")
		{
			return subCategoryOrFolder switch
			{
				"units" or "characters" => "Character",
				"buildings" => "Building",
				"resources" or "props" or "environment" => "Prop",
				"projectiles" or "attachments" or "weapons" or "items" => "Item",
				_ => "Prop"
			};
		}
		else if (ext is ".rtex")
		{
			return subCategoryOrFolder switch
			{
				"textures" => "Terrain",
				"vfx_radial" or "vfx_radials" or "radial" => "vfx_radial",
				"vfx_vertical" or "vfx_verticals" or "vertical" => "vfx_vertical",
				"vfx_spritesheets" or "vfx" => "Spritesheet",
				"icons" => "Icon",
				"decals" => "Decal",
				"ribbon_textures" or "ribbons" or "ribbon" => "Ribbon",
				"skyboxes" => "Skybox",
				"noise_textures" or "noise" => "Noise",
				_ => "Terrain"
			};
		}
		else if (ext == ".ranim")
		{
			return "Animation";
		}
		else if (ext is ".ogg" or ".raud")
		{
			return subCategoryOrFolder == "music" ? "Music" : "SoundEffect";
		}

		return string.Empty;
	}

	private string ResolveAssetFilePath(string wsPath, string fileName, string subCategoryOrFolder)
	{
		string ext = Path.GetExtension(fileName).ToLowerInvariant();
		if (ext is ".rmesh")
		{
			string path = Path.Combine(wsPath, "Assets", "models", subCategoryOrFolder, fileName);
			if (File.Exists(path)) return path;
			foreach (var sub in new[] { "units", "buildings", "resources", "props", "projectiles", "attachments", "weapons" })
			{
				string p = Path.Combine(wsPath, "Assets", "models", sub, fileName);
				if (File.Exists(p)) return p;
			}
			return path;
		}
		else if (ext is ".rtex")
		{
			string sub = subCategoryOrFolder switch
			{
				"vfx_spritesheets" => "vfx",
				"vfx_radial" or "vfx_radials" => "vfx_radial",
				"vfx_vertical" or "vfx_verticals" => "vfx_vertical",
				"ribbon_textures" or "ribbons" => "ribbons",
				"noise_textures" or "noise" => "noise",
				_ => subCategoryOrFolder
			};
			string path = Path.Combine(wsPath, "Assets", sub, fileName);
			if (File.Exists(path)) return path;
			foreach (var altSub in new[] { "textures", "vfx_radial", "vfx_vertical", "vfx", "icons", "decals", "ribbons", "noise", "skyboxes" })
			{
				string p = Path.Combine(wsPath, "Assets", altSub, fileName);
				if (File.Exists(p)) return p;
			}
			return path;
		}
		else if (ext == ".ranim")
		{
			return Path.Combine(wsPath, "Assets", "animations", fileName);
		}
		else if (ext is ".ogg" or ".raud")
		{
			string sub = subCategoryOrFolder == "music" ? "music" : "sfx";
			string path = Path.Combine(wsPath, "Assets", "audio", sub, fileName);
			if (File.Exists(path)) return path;
			string pathDirect = Path.Combine(wsPath, "Assets", sub, fileName);
			if (File.Exists(pathDirect)) return pathDirect;
			return path;
		}
		return Path.Combine(wsPath, "Assets", fileName);
	}

	private List<AssetItemInfo> GetAssetsForCategory(string category)
	{
		var result = new List<AssetItemInfo>();
		string wsPath = GetWorkspacePath();

		try
		{
			var assetsObj = MapAssetHelper.LoadUnionedAssets(wsPath);

			string expectedAssetType = category switch
			{
				"rmesh_characters" or "rmesh_units" => "Character",
				"rmesh_buildings" => "Building",
				"rmesh_props" or "rmesh_resources" => "Prop",
				"rmesh_items" or "rmesh_attachments" or "rmesh_weapons" or "rmesh_projectiles" => "Item",
				"textures" => "Terrain",
				"vfx_radial" or "vfx_radials" => "vfx_radial",
				"vfx_vertical" or "vfx_verticals" => "vfx_vertical",
				"vfx_spritesheets" => "Spritesheet",
				"animations" => "Animation",
				"sfx" => "SoundEffect",
				"music" => "Music",
				"icons" => "Icon",
				"decals" => "Decal",
				"ribbons" or "ribbon_textures" => "Ribbon",
				"noise_textures" => "Noise",
				"skyboxes" => "Skybox",
				_ => category
			};

			if (IsRmeshCategory(category, out string rmeshSub))
			{
				foreach (var topKey in new[] { "glb", "rmesh", "models" })
				{
					if (assetsObj[topKey] is JsonObject modelContainer)
					{
						foreach (var subKvp in modelContainer)
						{
							if (subKvp.Value is JsonObject subCatObj)
							{
								foreach (var model in subCatObj)
								{
									string resolvedType = ResolveAssetType(model.Key, subKvp.Key, model.Value);
									if (resolvedType.Equals(expectedAssetType, StringComparison.OrdinalIgnoreCase))
									{
										if (!result.Any(r => r.Key.Equals(model.Key, StringComparison.OrdinalIgnoreCase)))
										{
											result.Add(new AssetItemInfo { Category = category, SubCategory = subKvp.Key, Key = model.Key, ExtraData = model.Value });
										}
									}
								}
							}
						}
					}
				}
			}
			else if (category == "glb")
			{
				var glbObj = assetsObj["glb"]?.AsObject();
				if (glbObj != null)
				{
					foreach (var sub in glbObj)
					{
						if (sub.Value is JsonObject subCatObj)
						{
							foreach (var model in subCatObj)
							{
								result.Add(new AssetItemInfo { Category = "glb", SubCategory = sub.Key, Key = model.Key, ExtraData = model.Value });
							}
						}
					}
				}
			}
			else if (category is "textures" or "vfx_spritesheets" or "vfx_radial" or "vfx_vertical" or "icons" or "decals" or "ribbons" or "ribbon_textures" or "noise_textures" or "skyboxes")
			{
				foreach (var catName in new[] { "textures", "vfx_spritesheets", "vfx_radial", "vfx_vertical", "vfx", "icons", "decals", "ribbons", "ribbon_textures", "noise_textures", "skyboxes" })
				{
					if (assetsObj[catName] is JsonObject catObj)
					{
						foreach (var item in catObj)
						{
							string resolvedType = ResolveAssetType(item.Key, catName, item.Value);
							if (resolvedType.Equals(expectedAssetType, StringComparison.OrdinalIgnoreCase))
							{
								result.Add(new AssetItemInfo { Category = category, SubCategory = "", Key = item.Key, ExtraData = item.Value });
							}
						}
					}
				}
			}
			else if (category is "sfx" or "music")
			{
				foreach (var catName in new[] { "sfx", "music", "audio" })
				{
					if (assetsObj[catName] is JsonObject catObj)
					{
						foreach (var item in catObj)
						{
							string resolvedType = ResolveAssetType(item.Key, catName, item.Value);
							if (resolvedType.Equals(expectedAssetType, StringComparison.OrdinalIgnoreCase))
							{
								result.Add(new AssetItemInfo { Category = category, SubCategory = "", Key = item.Key, ExtraData = item.Value });
							}
						}
					}
				}
			}
			else if (category == "animations")
			{
				var animObj = assetsObj["animations"]?.AsObject();
				if (animObj != null)
				{
					foreach (var anim in animObj)
					{
						string resolvedType = ResolveAssetType(anim.Key, "animations", anim.Value);
						if (resolvedType.Equals("Animation", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(resolvedType))
						{
							result.Add(new AssetItemInfo { Category = "animations", SubCategory = "", Key = anim.Key, ExtraData = anim.Value });
						}
					}
				}
			}
			else if (category == "shaders")
			{
				var shaders = SpawnDeathShaderManager.LoadAllCustomShaders(wsPath);
				foreach (var kvp in shaders)
				{
					result.Add(new AssetItemInfo
					{
						Category = "shaders",
						SubCategory = "shaders",
						Key = kvp.Key,
						ExtraData = kvp.Value.ToJsonObject()
					});
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ObjectManagerDialog] GetAssetsForCategory error: {ex.Message}");
		}

		return result.OrderBy(r => r.Key).ToList();
	}

	private Control CreateAssetRow(string category, string key, string subCategory, JsonNode extraData)
	{
		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());

		var hBox = new HBoxContainer();
		hBox.AddThemeConstantOverride("separation", 8);

		var faFont = Hud?.GetFontAwesomeFont();

		var lblName = new Label();
		lblName.Text = key;
		lblName.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		lblName.AddThemeFontSizeOverride("font_size", 11);
		hBox.AddChild(lblName);

		// Action 1: Preview Button
		var btnPreview = new Button();
		btnPreview.Set("icon_max_width", 0);
		btnPreview.Text = "\uf06e " + TranslationServer.Translate("Preview");
		if (faFont != null) btnPreview.AddThemeFontOverride("font", faFont);
		btnPreview.AddThemeFontSizeOverride("font_size", 10);
		btnPreview.FocusMode = FocusModeEnum.None;
		btnPreview.CustomMinimumSize = new Vector2(75, 22);
		btnPreview.Pressed += () => LoadPreviewForAsset(category, key, subCategory);
		hBox.AddChild(btnPreview);

		// Action 2: Play Button (Audio only)
		bool isAudio = category == "sfx" || category == "music" || key.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase);
		if (isAudio)
		{
			var btnPlay = new Button();
			btnPlay.Set("icon_max_width", 0);
			btnPlay.Text = "\uf04b";
			if (faFont != null) btnPlay.AddThemeFontOverride("font", faFont);
			btnPlay.AddThemeFontSizeOverride("font_size", 11);
			btnPlay.FocusMode = FocusModeEnum.None;
			btnPlay.CustomMinimumSize = new Vector2(26, 22);
			btnPlay.TooltipText = TranslationServer.Translate("Play audio");
			btnPlay.Pressed += () =>
			{
				if (_audioPlayer != null && _audioPlayer.Playing && _currentPreviewAssetKey == key)
				{
					StopCurrentAudio();
				}
				else
				{
					LoadPreviewForAsset(category, key, subCategory);
					PlayCurrentAudio();
				}
			};
			hBox.AddChild(btnPlay);
		}

		// Action 3: Edit Button (Spritesheets, Textures, Decals, Shaders)
		bool hasEditDialog = category == "vfx_spritesheets" || category == "vfx" || category == "textures" || category == "decals" || category == "shaders" || (extraData is JsonObject edObj && edObj.ContainsKey("asset_type") && (edObj["asset_type"]?.ToString() is "Spritesheet" or "SpellSpritesheet" || edObj["asset_type"]?.ToString() == "Decal" || edObj["asset_type"]?.ToString() == "Shader"));
		if (hasEditDialog)
		{
			var btnEdit = new Button();
			btnEdit.Set("icon_max_width", 0);
			btnEdit.Text = "\uf044";
			if (faFont != null) btnEdit.AddThemeFontOverride("font", faFont);
			btnEdit.AddThemeFontSizeOverride("font_size", 11);
			btnEdit.FocusMode = FocusModeEnum.None;
			btnEdit.CustomMinimumSize = new Vector2(26, 22);
			btnEdit.TooltipText = TranslationServer.Translate("Edit asset parameters");
			btnEdit.Pressed += () => OpenEditSubDialog(category, key, extraData);
			hBox.AddChild(btnEdit);
		}

		// Action 4: Delete Button
		var btnDelete = new Button();
		btnDelete.Set("icon_max_width", 0);
		btnDelete.Text = "\uf00d";
		if (faFont != null) btnDelete.AddThemeFontOverride("font", faFont);
		btnDelete.AddThemeFontSizeOverride("font_size", 11);
		btnDelete.AddThemeColorOverride("font_color", new Color(0.95f, 0.35f, 0.35f));
		btnDelete.FocusMode = FocusModeEnum.None;
		btnDelete.CustomMinimumSize = new Vector2(26, 22);
		btnDelete.TooltipText = TranslationServer.Translate("Delete asset");
		btnDelete.Pressed += () => DeleteAsset(category, key, subCategory);
		hBox.AddChild(btnDelete);

		panel.AddChild(hBox);
		return panel;
	}

	private void LoadPreviewForAsset(string category, string key, string subCategory = "")
	{
		_currentPreviewAssetKey = key;
		_currentPreviewAssetCategory = category;

		// 1. Clear previous previews
		Clear3DModelPreview();
		StopCurrentAudio();
		_preview2DFrames = null;
		_preview2DFrameIndex = 0;
		_preview2DFrameTimer = 0.0;
		if (_preview2DImage != null) _preview2DImage.Modulate = Colors.White;

		if (IsRmeshCategory(category, out string rmeshSub) || category == "animations" || category == "vfx_spritesheets" || category == "shaders")
		{
			PreviewViewportContainer.Visible = true;
			_preview2DContainer.Visible = false;
			_previewAudioContainer.Visible = false;
			if (_cameraPresetRow != null) _cameraPresetRow.Visible = (IsRmeshCategory(category, out _) || category == "animations" || category == "shaders");

			if (IsRmeshCategory(category, out rmeshSub))
			{
				Load3DModel(key, !string.IsNullOrEmpty(subCategory) ? subCategory : rmeshSub);
			}
			else if (category == "animations")
			{
				LoadRanimAnimation(key);
			}
			else if (category == "vfx_spritesheets")
			{
				LoadVfxSpritesheet(key);
			}
			else if (category == "shaders")
			{
				LoadCustomShaderPreview(key);
			}
		}
		else if (category == "sfx" || category == "music")
		{
			PreviewViewportContainer.Visible = false;
			_preview2DContainer.Visible = false;
			_previewAudioContainer.Visible = true;
			if (_cameraPresetRow != null) _cameraPresetRow.Visible = false;

			_lblAudioInfo.Text = key;
			LoadAudioStream(key, category);
		}
		else
		{
			PreviewViewportContainer.Visible = false;
			_preview2DContainer.Visible = true;
			_previewAudioContainer.Visible = false;
			if (_cameraPresetRow != null) _cameraPresetRow.Visible = false;

			bool isDecal = category == "decals" || IsDecalAsset(key, category, subCategory);
			if (isDecal)
			{
				LoadDecal2DPreview(key, category);
			}
			else
			{
				LoadStatic2DTexture(key, category);
			}
		}
	}

	private void ClearPreview()
	{
		_currentPreviewAssetKey = "";
		_currentShaderConfig = null;
		_preview2DFrames = null;
		_preview2DFrameIndex = 0;
		_preview2DFrameTimer = 0.0;
		Clear3DModelPreview();
		StopCurrentAudio();
		if (_preview2DImage != null)
		{
			_preview2DImage.Texture = null;
			_preview2DImage.Modulate = Colors.White;
		}
		if (_lblPreview2DInfo != null) _lblPreview2DInfo.Text = "";
		if (_cameraPresetRow != null) _cameraPresetRow.Visible = false;
	}

	private void Clear3DModelPreview()
	{
		_currentShaderConfig = null;
		if (_currentModelRoot != null)
		{
			foreach (Node child in _currentModelRoot.GetChildren())
			{
				child.QueueFree();
			}
		}
		if (_vfxSprite != null)
		{
			_vfxSprite.Visible = false;
			_vfxSprite.Stop();
			_vfxSprite.SpriteFrames = null;
		}
	}

	private void Load3DModel(string key, string subCategory)
	{
		Clear3DModelPreview();
		string wsPath = GetWorkspacePath();
		string modelPath = Path.Combine(wsPath, "Assets", "models", subCategory ?? "props", key);
		if (!File.Exists(modelPath))
		{
			foreach (var sub in new[] { "units", "buildings", "resources", "props", "projectiles", "characters", "items", "attachments", "weapons" })
			{
				string p = Path.Combine(wsPath, "Assets", "models", sub, key);
				if (File.Exists(p))
				{
					modelPath = p;
					break;
				}
			}
		}

		if (!File.Exists(modelPath)) return;

		var loadedNode = ModelCache.GetModel(modelPath) ?? ModelCache.GetModel(key);
		if (loadedNode is Node3D node3D)
		{
			_currentModelRoot.AddChild(node3D);
			CenterAndFrameNode(node3D);
			return;
		}

		var gltfDoc = new GltfDocument();
		var gltfState = new GltfState();
		Error err;
		if (modelPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
		{
			byte[] rmeshBytes = File.ReadAllBytes(modelPath);
			byte[] glbBytes = Realm.Shared.ModelOptimization.RmeshFile.GetGlbBytes(rmeshBytes) ?? rmeshBytes;
			err = gltfDoc.AppendFromBuffer(glbBytes, "", gltfState);
		}
		else
		{
			err = gltfDoc.AppendFromFile(modelPath, gltfState);
		}
		if (err == Error.Ok)
		{
			var node = gltfDoc.GenerateScene(gltfState);
			if (node is Node3D gltfNode3D)
			{
				_currentModelRoot.AddChild(gltfNode3D);
				CenterAndFrameNode(gltfNode3D);
			}
		}
	}

	private void LoadRanimAnimation(string ranimKey)
	{
		Clear3DModelPreview();
		var rigged = GetRiggedGlbModels();
		if (string.IsNullOrEmpty(_selectedRanimBaseModel) || !rigged.Contains(_selectedRanimBaseModel))
		{
			_selectedRanimBaseModel = rigged.Count > 0 ? rigged[0] : string.Empty;
			_setRanimBaseModelValue?.Invoke(_selectedRanimBaseModel);
		}

		if (string.IsNullOrEmpty(_selectedRanimBaseModel)) return;

		string wsPath = GetWorkspacePath();
		string modelPath = Path.Combine(wsPath, "Assets", "models", "units", _selectedRanimBaseModel);
		if (!File.Exists(modelPath))
		{
			foreach (var sub in new[] { "units", "buildings", "resources", "props", "projectiles", "characters", "items", "attachments", "weapons" })
			{
				string p = Path.Combine(wsPath, "Assets", "models", sub, _selectedRanimBaseModel);
				if (File.Exists(p)) { modelPath = p; break; }
			}
		}

		Node3D? animModelNode = null;
		var loaded = ModelCache.GetModel(modelPath ?? _selectedRanimBaseModel) ?? ModelCache.GetModel(_selectedRanimBaseModel);
		if (loaded is Node3D n)
		{
			animModelNode = n;
		}
		else if (modelPath != null && File.Exists(modelPath))
		{
			var gltfDoc = new GltfDocument();
			var gltfState = new GltfState();
			Error err;
			if (modelPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
			{
				byte[] rmeshBytes = File.ReadAllBytes(modelPath);
				byte[] glbBytes = Realm.Shared.ModelOptimization.RmeshFile.GetGlbBytes(rmeshBytes) ?? rmeshBytes;
				err = gltfDoc.AppendFromBuffer(glbBytes, "", gltfState);
			}
			else
			{
				err = gltfDoc.AppendFromFile(modelPath, gltfState);
			}
			if (err == Error.Ok)
			{
				var node = gltfDoc.GenerateScene(gltfState);
				if (node is Node3D node3D) animModelNode = node3D;
			}
		}

		if (animModelNode != null)
		{
			_currentModelRoot.AddChild(animModelNode);
			CenterAndFrameNode(animModelNode);

			// Load and bind .ranim animation
			string animPath = Path.Combine(wsPath, "Assets", "animations", ranimKey);
			if (File.Exists(animPath))
			{
				try
				{
					var animData = AnimationRetargetingService.GetOrLoadRanimData(animPath);
					if (animData != null)
					{
						if (AnimationRetargetingService.RetargetAndBind(animData, animModelNode, "preview_loop", out _))
						{
							var animPlayer = AnimationRetargetingService.FindOrCreateAnimationPlayer(animModelNode);
							if (animPlayer != null && animPlayer.HasAnimation("preview_loop"))
							{
								var anim = animPlayer.GetAnimation("preview_loop");
								if (anim != null) anim.LoopMode = Godot.Animation.LoopModeEnum.Linear;
								animPlayer.Play("preview_loop");
							}
						}
					}
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[ObjectManagerDialog] LoadRanimAnimation error: {ex.Message}");
				}
			}
		}
	}

	private static Texture2D? LoadTextureFromFileOrRtex(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;

		try
		{
			Image? img = null;
			if (filePath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
			{
				byte[] rtexBytes = File.ReadAllBytes(filePath);
				byte[]? layer0Bytes = Realm.Shared.Textures.RtexFile.IsRtexBytes(rtexBytes)
					? Realm.Shared.Textures.RtexFile.GetLayer(rtexBytes, 0)
					: rtexBytes;
				if (layer0Bytes != null && layer0Bytes.Length > 0)
				{
					img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
					if (img.LoadWebpFromBuffer(layer0Bytes) != Error.Ok)
					{
						if (img.LoadPngFromBuffer(layer0Bytes) != Error.Ok)
						{
							if (img.LoadJpgFromBuffer(layer0Bytes) != Error.Ok)
							{
								if (img.LoadTgaFromBuffer(layer0Bytes) != Error.Ok)
								{
									img.LoadBmpFromBuffer(layer0Bytes);
								}
							}
						}
					}
				}
			}
			else
			{
				img = Image.LoadFromFile(filePath);
			}

			if (img != null)
			{
				if (!img.HasMipmaps())
				{
					img.GenerateMipmaps();
				}
				return ImageTexture.CreateFromImage(img);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ObjectManagerDialog] Failed to load texture from '{filePath}': {ex.Message}");
		}

		return null;
	}

	private void LoadVfxSpritesheet(string key)
	{
		Clear3DModelPreview();
		string wsPath = GetWorkspacePath();
		string cleanPath = key.Replace("\\", "/").TrimStart('/');
		string fileName = Path.GetFileName(key);
		string cleanBase = Path.GetFileNameWithoutExtension(key);

		var candidatePaths = new List<string?>
		{
			Path.Combine(wsPath, "Assets", "vfx", fileName),
			Path.Combine(wsPath, "Assets", "vfx", key),
			Path.Combine(wsPath, "Assets", cleanPath),
		};

		if (!fileName.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			candidatePaths.Add(Path.Combine(wsPath, "Assets", "vfx", cleanBase + ".rtex"));
		}
		if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
		{
			candidatePaths.Add(Path.Combine(wsPath, "Assets", "vfx", cleanBase + ".png"));
		}

		Texture2D? texture = null;
		foreach (var candidate in candidatePaths)
		{
			if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
			{
				texture = LoadTextureFromFileOrRtex(candidate);
				if (texture != null) break;
			}
		}

		if (texture == null) return;

		int cols = 4;
		int rows = 4;
		float fps = 20.0f;

		try
		{
			var assets = MapAssetHelper.LoadUnionedAssets(wsPath);
			var vfxSheets = assets["vfx_spritesheets"]?.AsObject();
			if (vfxSheets != null)
			{
				JsonObject? sheetObj = null;
				if (vfxSheets.TryGetPropertyValue(fileName, out var s1) && s1 is JsonObject so1) sheetObj = so1;
				else if (vfxSheets.TryGetPropertyValue(key, out var s2) && s2 is JsonObject so2) sheetObj = so2;
				else if (vfxSheets.TryGetPropertyValue($"{cleanBase}.rtex", out var s3) && s3 is JsonObject so3) sheetObj = so3;
				else if (vfxSheets.TryGetPropertyValue($"{cleanBase}.png", out var s4) && s4 is JsonObject so4) sheetObj = so4;

				if (sheetObj != null)
				{
					if (sheetObj.TryGetPropertyValue("columns", out var cNode) && int.TryParse(cNode?.ToString(), out int parsedCols) && parsedCols > 0)
						cols = parsedCols;
					if (sheetObj.TryGetPropertyValue("rows", out var rNode) && int.TryParse(rNode?.ToString(), out int parsedRows) && parsedRows > 0)
						rows = parsedRows;
					if (sheetObj.TryGetPropertyValue("fps", out var fNode) && float.TryParse(fNode?.ToString(), out float parsedFps) && parsedFps > 0.001f)
						fps = parsedFps;
				}
			}
		}
		catch { }

		if (cols <= 0) cols = 1;
		if (rows <= 0) rows = 1;

		int totalFrames = cols * rows;
		var frames = new SpriteFrames();
		frames.AddAnimation("play");
		frames.SetAnimationLoopMode("play", SpriteFrames.LoopMode.Linear);
		frames.SetAnimationSpeed("play", fps);

		int frameWidth = Math.Max(1, (int)texture.GetWidth() / cols);
		int frameHeight = Math.Max(1, (int)texture.GetHeight() / rows);

		for (int frameIndex = 0; frameIndex < totalFrames; frameIndex++)
		{
			int col = frameIndex % cols;
			int row = frameIndex / cols;
			var atlasFrame = new AtlasTexture
			{
				Atlas = texture,
				Region = new Rect2(col * frameWidth, row * frameHeight, frameWidth, frameHeight)
			};
			frames.AddFrame("play", atlasFrame);
		}

		if (_vfxSprite != null)
		{
			_vfxSprite.SpriteFrames = frames;
			_vfxSprite.Animation = "play";
			_vfxSprite.PixelSize = 6.0f / frameWidth;
			_vfxSprite.Position = Vector3.Zero;
			_vfxSprite.Visible = true;
			_vfxSprite.Play("play");
		}

		DefaultDistance = 3.5f;
		DefaultYaw = 0f;
		DefaultPitch = 0f;
		DefaultTargetPosition = Vector3.Zero;
		ResetCameraDefault();
	}

	private void LoadCustomShaderPreview(string shaderKey)
	{
		Clear3DModelPreview();
		_currentShaderConfig = SpawnDeathShaderManager.GetShaderConfig(shaderKey);
		if (_currentShaderConfig == null) return;

		_shaderPreviewTime = 0f;
		_shaderPreviewForward = true;

		var models = GetPreviewMeshModels();
		if (string.IsNullOrEmpty(_selectedRanimBaseModel) || !models.Contains(_selectedRanimBaseModel))
		{
			_selectedRanimBaseModel = models.Count > 0 ? models[0] : string.Empty;
			_setRanimBaseModelValue?.Invoke(_selectedRanimBaseModel);
		}

		string wsPath = GetWorkspacePath();
		string modelPath = null;
		if (!string.IsNullOrEmpty(_selectedRanimBaseModel))
		{
			foreach (var sub in new[] { "units", "buildings", "resources", "props", "projectiles", "characters", "items", "attachments", "weapons" })
			{
				string p = Path.Combine(wsPath, "Assets", "models", sub, _selectedRanimBaseModel);
				if (File.Exists(p)) { modelPath = p; break; }
			}

			if (!File.Exists(modelPath))
			{
				string modelsDir = Path.Combine(wsPath, "Assets", "models");
				if (Directory.Exists(modelsDir))
				{
					var files = Directory.GetFiles(modelsDir, _selectedRanimBaseModel, SearchOption.AllDirectories);
					if (files.Length > 0) modelPath = files[0];
				}
			}
		}

		Node3D? previewNode = null;
		if (!string.IsNullOrEmpty(_selectedRanimBaseModel))
		{
			var loaded = ModelCache.GetModel(modelPath ?? _selectedRanimBaseModel) ?? ModelCache.GetModel(_selectedRanimBaseModel);
			if (loaded is Node3D n) previewNode = n;
		}

		if (previewNode == null && modelPath != null && File.Exists(modelPath))
		{
			var gltfDoc = new GltfDocument();
			var gltfState = new GltfState();
			Error err;
			if (modelPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
			{
				byte[] rmeshBytes = File.ReadAllBytes(modelPath);
				byte[] glbBytes = Realm.Shared.ModelOptimization.RmeshFile.GetGlbBytes(rmeshBytes) ?? rmeshBytes;
				err = gltfDoc.AppendFromBuffer(glbBytes, "", gltfState);
				
				if (err == Error.Ok)
				{
					var node = gltfDoc.GenerateScene(gltfState);
					if (node is Node3D n3d) previewNode = n3d;
				}
			}
		}

		if (previewNode != null)
		{
			_currentModelRoot.AddChild(previewNode);
			CenterAndFrameNode(previewNode);
			SpawnDeathShaderManager.ApplyShaderPreview(_currentModelRoot, _currentShaderConfig, 0.5f);
		}
		else
		{
			var meshInst = new MeshInstance3D
			{
				Mesh = new CapsuleMesh { Radius = 0.5f, Height = 1.8f },
				Position = new Vector3(0, 0.9f, 0)
			};
			var mat = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.7f, 0.5f) };
			meshInst.MaterialOverride = mat;
			_currentModelRoot.AddChild(meshInst);
			CenterAndFrameNode(_currentModelRoot);
			SpawnDeathShaderManager.ApplyShaderPreview(_currentModelRoot, _currentShaderConfig, 0.5f);
		}
	}

	public override void _Process(double delta)
	{
		base._Process(delta);
		if (_currentPreviewAssetCategory == "shaders" && _currentShaderConfig != null && _currentModelRoot != null && Visible)
		{
			float dur = _currentShaderConfig.Duration > 0.05f ? _currentShaderConfig.Duration : 1.5f;
			if (_shaderPreviewForward)
			{
				_shaderPreviewTime += (float)delta;
				if (_shaderPreviewTime >= dur)
				{
					_shaderPreviewTime = dur;
					_shaderPreviewForward = false;
				}
			}
			else
			{
				_shaderPreviewTime -= (float)delta;
				if (_shaderPreviewTime <= 0f)
				{
					_shaderPreviewTime = 0f;
					_shaderPreviewForward = true;
				}
			}
			float prog = Mathf.Clamp(_shaderPreviewTime / dur, 0f, 1f);
			SpawnDeathShaderManager.ApplyShaderPreview(_currentModelRoot, _currentShaderConfig, prog);
		}

		if (Visible && _preview2DContainer != null && _preview2DContainer.Visible && _preview2DFrames != null && _preview2DFrames.Length > 1 && _preview2DImage != null)
		{
			_preview2DFrameTimer += delta;
			double duration = 1.0 / (_preview2DFps > 0.001f ? _preview2DFps : 12.0f);
			if (_preview2DFrameTimer >= duration)
			{
				_preview2DFrameTimer -= duration;
				if (_preview2DFrameTimer >= duration)
				{
					_preview2DFrameTimer %= duration;
				}
				_preview2DFrameIndex = (_preview2DFrameIndex + 1) % _preview2DFrames.Length;
				_preview2DImage.Texture = _preview2DFrames[_preview2DFrameIndex];
			}
		}
	}

	private bool IsDecalAsset(string key, string category, string subCategory)
	{
		if (category == "decals") return true;
		try
		{
			string wsPath = GetWorkspacePath();
			var assetsObj = MapAssetHelper.LoadUnionedAssets(wsPath);
			if (assetsObj != null)
			{
				if (assetsObj["decals"] is JsonObject decalsObj)
				{
					string clean = Path.GetFileName(key);
					string cleanBase = Path.GetFileNameWithoutExtension(key);
					if (decalsObj.ContainsKey(clean) || decalsObj.ContainsKey($"{cleanBase}.rtex") || decalsObj.ContainsKey(cleanBase))
						return true;
				}

				if (assetsObj.TryGetPropertyValue(category, out var catNode) && catNode is JsonObject catObj)
				{
					if (catObj.TryGetPropertyValue(key, out var itemNode) && itemNode is JsonObject itemObj)
					{
						string? aType = itemObj["asset_type"]?.ToString() ?? itemObj["AssetType"]?.ToString() ?? itemObj["type"]?.ToString();
						if (string.Equals(aType, "Decal", StringComparison.OrdinalIgnoreCase)) return true;
					}
				}
			}

			string filePath = ResolveAssetFilePath(wsPath, key, category);
			if (File.Exists(filePath))
			{
				string? embeddedType = Realm.Shared.Metadata.RealmMetadataHelper.ExtractAssetType(filePath);
				if (string.Equals(embeddedType, "Decal", StringComparison.OrdinalIgnoreCase)) return true;
			}
		}
		catch { }

		return false;
	}

	private void LoadDecal2DPreview(string key, string category)
	{
		_preview2DFrames = null;
		_preview2DFrameIndex = 0;
		_preview2DFrameTimer = 0.0;
		if (_preview2DImage != null) _preview2DImage.Modulate = Colors.White;

		string wsPath = GetWorkspacePath();
		string subFolder = category switch
		{
			"decals" => "decals",
			_ => category
		};

		string filePath = ResolveAssetFilePath(wsPath, key, subFolder);
		if (!File.Exists(filePath) && subFolder != "decals")
		{
			string altPath = ResolveAssetFilePath(wsPath, key, "decals");
			if (File.Exists(altPath)) filePath = altPath;
		}

		GameHost.DecalAssetData? assetData = null;
		if (GameHost.Instance != null)
		{
			GameHost.Instance.InvalidateDecalCache(key);
			assetData = GameHost.Instance.LoadDecalAsset(key, forceReload: true);
			if ((assetData == null || assetData.PrimaryTexture == null || assetData.PrimaryTexture.ResourcePath == "res://icon.svg") && File.Exists(filePath))
			{
				GameHost.Instance.InvalidateDecalCache(filePath);
				assetData = GameHost.Instance.LoadDecalAsset(filePath, forceReload: true);
			}
		}

		if (assetData == null && File.Exists(filePath))
		{
			Texture2D? baseTex = LoadTextureFromFileOrRtex(filePath);
			if (baseTex != null)
			{
				assetData = new GameHost.DecalAssetData
				{
					DecalId = key,
					PrimaryTexture = baseTex,
					Columns = 1,
					Rows = 1,
					Fps = 12.0f
				};
			}
		}

		if (assetData == null) return;

		var metaObj = DecalSettingsDialog.ResolveDecalMetadata(key);
		float brightness = metaObj.TryGetPropertyValue("brightness", out var bNode) && float.TryParse(bNode?.ToString(), out float b) ? b : 1.0f;
		float opacity = metaObj.TryGetPropertyValue("opacity", out var oNode) && float.TryParse(oNode?.ToString(), out float o) ? o : 1.0f;
		Color tint = Colors.White;
		if (metaObj.TryGetPropertyValue("tint", out var tNode) && tNode != null)
		{
			string tStr = tNode.ToString();
			if (tStr.StartsWith("#")) tint = Color.FromHtml(tStr);
		}

		if (_preview2DImage != null)
		{
			float mr = Mathf.Clamp(tint.R * brightness, 0f, 2f);
			float mg = Mathf.Clamp(tint.G * brightness, 0f, 2f);
			float mb = Mathf.Clamp(tint.B * brightness, 0f, 2f);
			_preview2DImage.Modulate = new Color(mr, mg, mb, Mathf.Clamp(opacity, 0f, 1f));
			_preview2DImage.Texture = assetData.PrimaryTexture;
		}

		if (_lblPreview2DInfo != null)
		{
			var baseTex = assetData.PrimaryTexture;
			_lblPreview2DInfo.Text = baseTex != null
				? $"{key} ({baseTex.GetWidth()}x{baseTex.GetHeight()})"
				: key;
		}
	}

	private void LoadStatic2DTexture(string key, string category)
	{
		_preview2DFrames = null;
		_preview2DFrameIndex = 0;
		_preview2DFrameTimer = 0.0;
		if (_preview2DImage != null) _preview2DImage.Modulate = Colors.White;

		string wsPath = GetWorkspacePath();
		string subFolder = category switch
		{
			"textures" => "textures",
			"vfx_radial" or "vfx_radials" => "vfx_radial",
			"vfx_vertical" or "vfx_verticals" => "vfx_vertical",
			"icons" => "icons",
			"decals" => "decals",
			"ribbons" or "ribbon_textures" => "ribbons",
			"noise_textures" or "noise" => "noise",
			"skyboxes" => "skyboxes",
			_ => category
		};

		string filePath = ResolveAssetFilePath(wsPath, key, subFolder);
		if (!File.Exists(filePath)) return;

		Texture2D? tex = LoadTextureFromFileOrRtex(filePath);
		if (_preview2DImage != null) _preview2DImage.Texture = tex;
		if (_lblPreview2DInfo != null)
		{
			_lblPreview2DInfo.Text = tex != null
				? $"{key} ({tex.GetWidth()}x{tex.GetHeight()})"
				: key;
		}
	}

	private void LoadAudioStream(string key, string category)
	{
		string wsPath = GetWorkspacePath();
		string sub = category == "music" ? "music" : "sfx";
		string audioPath = Path.Combine(wsPath, "Assets", "audio", sub, key);
		if (!File.Exists(audioPath))
		{
			audioPath = Path.Combine(wsPath, "Assets", sub, key);
		}

		if (File.Exists(audioPath) && _audioPlayer != null)
		{
			try
			{
				if (audioPath.EndsWith(".raud", StringComparison.OrdinalIgnoreCase))
				{
					byte[] raudBytes = File.ReadAllBytes(audioPath);
					byte[]? oggBytes = Realm.Shared.Audio.RaudFile.GetTrack(raudBytes, 0);
					if (oggBytes != null && oggBytes.Length > 0)
					{
						_audioPlayer.Stream = AudioStreamOggVorbis.LoadFromBuffer(oggBytes);
					}
				}
				else if (audioPath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
				{
					_audioPlayer.Stream = AudioStreamOggVorbis.LoadFromFile(audioPath);
				}
				else
				{
					_audioPlayer.Stream = GD.Load<AudioStream>(audioPath);
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ObjectManagerDialog] LoadAudioStream error: {ex.Message}");
			}
		}
	}

	private void PlayCurrentAudio()
	{
		if (_audioPlayer != null && _audioPlayer.Stream != null)
		{
			_audioPlayer.Play();
			UpdateAudioPlayButtonState();
		}
	}

	private void StopCurrentAudio()
	{
		if (_audioPlayer != null && _audioPlayer.Playing)
		{
			_audioPlayer.Stop();
		}
		UpdateAudioPlayButtonState();
	}

	private void ToggleAudioPlayback()
	{
		if (_audioPlayer != null && _audioPlayer.Playing)
		{
			StopCurrentAudio();
		}
		else
		{
			PlayCurrentAudio();
		}
	}

	private void OnAudioFinished()
	{
		UpdateAudioPlayButtonState();
	}

	private void UpdateAudioPlayButtonState()
	{
		if (_btnAudioPlay != null)
		{
			bool isPlaying = _audioPlayer != null && _audioPlayer.Playing;
			_btnAudioPlay.Text = isPlaying ? "⏹ " + TranslationServer.Translate("Stop") : "▶ " + TranslationServer.Translate("Play");
			_btnAudioPlay.TooltipText = isPlaying ? TranslationServer.Translate("Stop audio playback") : TranslationServer.Translate("Play loaded audio");
		}
	}

	private void CenterAndFrameNode(Node3D root)
	{
		Aabb aabb = new Aabb();
		bool hasAabb = false;
		if (root == null || Mathf.Abs(root.GlobalTransform.Basis.Determinant()) < 0.0001f)
		{
			return;
		}

		void CalculateAabb(Node node)
		{
			if (node is VisualInstance3D visual)
			{
				if (Mathf.Abs(visual.GlobalTransform.Basis.Determinant()) < 0.0001f) return;
				Aabb itemAabb = visual.GetAabb();
				if (itemAabb.Size.LengthSquared() > 0.001f)
				{
					Transform3D localXform = root.GlobalTransform.AffineInverse() * visual.GlobalTransform;
					Aabb transformedAabb = localXform * itemAabb;
					aabb = hasAabb ? aabb.Merge(transformedAabb) : transformedAabb;
					hasAabb = true;
				}
			}
			foreach (Node child in node.GetChildren())
			{
				CalculateAabb(child);
			}
		}

		CalculateAabb(root);

		if (hasAabb)
		{
			Vector3 center = aabb.Position + aabb.Size * 0.5f;
			root.Position = -center;
			float maxDim = Mathf.Max(aabb.Size.X, Mathf.Max(aabb.Size.Y, aabb.Size.Z));
			DefaultDistance = Mathf.Max(2.5f, maxDim * 2.2f);
		}
		else
		{
			root.Position = Vector3.Zero;
			DefaultDistance = 5.0f;
		}

		ResetCameraDefault();
	}

	private static readonly Dictionary<string, bool> _riggedModelCache = new(StringComparer.OrdinalIgnoreCase);

	private List<string> GetRiggedGlbModels()
	{
		var candidateModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		string wsPath = GetWorkspacePath();
		if (MetadataService.Instance.TryLoadMetadata(wsPath, out var metadata))
		{
			if (metadata.CustomUnits != null)
			{
				foreach (var u in metadata.CustomUnits)
				{
					if (!string.IsNullOrEmpty(u.ModelPath) && (u.ModelPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) || !Path.HasExtension(u.ModelPath)))
					{
						string fileName = Path.GetFileName(u.ModelPath);
						string? resolved = ModelCache.ResolveModelPath(fileName);
						if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
						{
							candidateModels.Add(fileName);
						}
					}
				}
			}
		}

		try
		{
			var assets = MapAssetHelper.LoadUnionedAssets(wsPath);
			foreach (var topKey in new[] { "rmesh", "glb", "models" })
			{
				if (assets[topKey] is JsonObject glbObj)
				{
					foreach (var subKey in new[] { "characters", "units" })
					{
						if (glbObj[subKey] is JsonObject subObj)
						{
							foreach (var model in subObj)
							{
								if (!string.IsNullOrEmpty(model.Key))
								{
									string fileName = Path.GetFileName(model.Key);
									string? resolved = ModelCache.ResolveModelPath(fileName);
									if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
									{
										candidateModels.Add(fileName);
									}
								}
							}
						}
					}
				}
			}
		}
		catch { }

		// 2. Scan GameHost.UnitRegistry
		if (GameHost.UnitRegistry != null)
		{
			foreach (var kvp in GameHost.UnitRegistry)
			{
				if (!string.IsNullOrEmpty(kvp.Value.ModelPath) && (kvp.Value.ModelPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) || !Path.HasExtension(kvp.Value.ModelPath)))
				{
					string fileName = Path.GetFileName(kvp.Value.ModelPath);
					string? resolved = ModelCache.ResolveModelPath(fileName);
					if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
					{
						candidateModels.Add(fileName);
					}
				}
			}
		}

		// 3. Scan filesystem directories for unit and character rmesh models
		var unitDirs = new List<string>
		{
			Path.Combine(wsPath, "Assets", "models", "characters"),
			Path.Combine(wsPath, "Assets", "models", "units"),
		};

		foreach (var dir in unitDirs)
		{
			if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
			{
				foreach (var file in Directory.GetFiles(dir, "*.rmesh"))
				{
					candidateModels.Add(Path.GetFileName(file));
				}
			}
		}

		// 4. Filter for rigged models (containing Skeleton3D)
		var riggedList = new List<string>();
		foreach (var modelFile in candidateModels)
		{
			string? resolvedPath = ModelCache.ResolveModelPath(modelFile);
			if (string.IsNullOrEmpty(resolvedPath) || !File.Exists(resolvedPath))
			{
				_riggedModelCache.Remove(modelFile);
				continue;
			}

			if (_riggedModelCache.TryGetValue(modelFile, out bool isRigged))
			{
				if (isRigged) riggedList.Add(modelFile);
				continue;
			}

			bool hasSkeleton = false;
			try
			{
				byte[] rmeshBytes = File.ReadAllBytes(resolvedPath);
				byte[] glbBytes = Realm.Shared.ModelOptimization.RmeshFile.GetGlbBytes(rmeshBytes) ?? rmeshBytes;
				var (jsonNode, _, _) = Realm.Shared.GlbManifestUtils.ParseGlb(glbBytes);
				if (jsonNode is JsonObject root && root["skins"] is JsonArray skins && skins.Count > 0)
				{
					hasSkeleton = true;
				}
				else if (jsonNode != null)
				{
					hasSkeleton = false;
				}
				else
				{
					var doc = new GltfDocument();
					var state = new GltfState();
					if (doc.AppendFromBuffer(glbBytes, "", state) == Error.Ok)
					{
						hasSkeleton = state.Skins != null && state.Skins.Count > 0;
					}
				}
			}
			catch { }

			_riggedModelCache[modelFile] = hasSkeleton;
			if (hasSkeleton)
			{
				riggedList.Add(modelFile);
			}
		}

		if (riggedList.Count == 0 && candidateModels.Count > 0)
		{
			riggedList.AddRange(candidateModels);
		}

		riggedList.Sort(StringComparer.OrdinalIgnoreCase);
		return riggedList;
	}

	private List<string> GetPreviewMeshModels()
	{
		if (_currentCategory == "animations")
		{
			return GetRiggedGlbModels();
		}
		return GetAllGlbModels();
	}

	private List<string> GetAllGlbModels()
	{
		var models = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string wsPath = GetWorkspacePath();
		try
		{
			var assets = MapAssetHelper.LoadUnionedAssets(wsPath);
			foreach (var topKey in new[] { "rmesh", "glb", "models" })
			{
				if (assets[topKey] is JsonObject glbObj)
				{
					foreach (var sub in glbObj)
					{
						if (sub.Value is JsonObject subObj)
						{
							foreach (var model in subObj)
							{
								if (!string.IsNullOrEmpty(model.Key))
								{
									string fileName = Path.GetFileName(model.Key);
									string? resolved = ModelCache.ResolveModelPath(fileName);
									if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
									{
										models.Add(fileName);
									}
								}
							}
						}
					}
				}
			}
		}
		catch { }

		foreach (var sub in new[] { "units", "buildings", "resources", "props", "projectiles", "characters", "items", "attachments", "weapons" })
		{
			string dir = Path.Combine(wsPath, "Assets", "models", sub);
			if (Directory.Exists(dir))
			{
				foreach (var file in Directory.GetFiles(dir))
				{
					string ext = Path.GetExtension(file).ToLowerInvariant();
					if (ext is ".rmesh" or ".glb")
					{
						models.Add(Path.GetFileName(file));
					}
				}
			}
		}

		var list = models.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
		return list.Count > 0 ? list : GetRiggedGlbModels();
	}

	private string? GetRequiredAssetTypeForCategory(string category)
	{
		if (IsRmeshCategory(category, out string rmeshSub))
		{
			return rmeshSub switch
			{
				"characters" or "units" => "Character",
				"buildings" => "Building",
				"props" or "resources" or "environment" => "Prop",
				"items" or "projectiles" or "attachments" or "weapons" => "Item",
				_ => "Prop"
			};
		}

		return category switch
		{
			"textures" => "Terrain",
			"vfx_radial" or "vfx_radials" => "vfx_radial",
			"vfx_vertical" or "vfx_verticals" => "vfx_vertical",
			"vfx_spritesheets" => "Spritesheet",
			"icons" => "Icon",
			"decals" => "Decal",
			"ribbons" or "ribbon_textures" => "Ribbon",
			"noise_textures" or "noise" => "Noise",
			"skyboxes" => "Skybox",
			"animations" => "Animation",
			"music" => "Music",
			"sfx" => "SoundEffect",
			_ => null
		};
	}

	private void OpenImportFileDialog()
	{
		if (_currentCategory == "shaders")
		{
			_shaderEditDialog.OpenForShader("", (savedConfig) => RefreshAssetListAndPreview(savedConfig?.Key));
			return;
		}

		bool requireRealmMetadata = true;
		string[] extensions;
		string? requiredAssetType = GetRequiredAssetTypeForCategory(_currentCategory);

		if (IsRmeshCategory(_currentCategory, out _))
		{
			extensions = new[] { ".rmesh" };
		}
		else
		{
			switch (_currentCategory)
			{
				case "textures":
				case "vfx_spritesheets":
				case "vfx_radial":
				case "vfx_vertical":
				case "decals":
				case "ribbons":
				case "ribbon_textures":
				case "noise_textures":
				case "skyboxes":
				case "icons":
					extensions = new[] { ".rtex" };
					break;
				case "sfx":
				case "music":
					extensions = new[] { ".ogg", ".raud" };
					break;
				case "animations":
					extensions = new[] { ".ranim" };
					break;
				default:
					extensions = Array.Empty<string>();
					requireRealmMetadata = false;
					break;
			}
		}

		Hud?.OpenAssetBrowser($"Import Asset ({_currentCategory})", extensions, OnImportFileSelected, requireRealmMetadata, requiredAssetType);
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
					string sourceFilePath = selectedPaths[0];
					ImportShaderFromFile(sourceFilePath);
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
								lastKey = config.Key;
								count++;
							}
						}
						RefreshAssetListAndPreview(lastKey);
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
						RefreshAssetListAndPreview(config.Key);
						Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Imported shader '{0}' successfully."), config.Name));
						return;
					}
					else
					{
						int count = 0;
						string lastKey = "";
						foreach (var kvp in rootObj)
						{
							if (kvp.Value is JsonObject subObj && (subObj.ContainsKey("transition_mode") || subObj.ContainsKey("edge_color") || subObj.ContainsKey("name") || subObj.ContainsKey("TransitionMode")))
							{
								var config = CustomShaderConfig.FromJson(kvp.Key, subObj);
								SpawnDeathShaderManager.SaveCustomShader(config, wsPath);
								lastKey = config.Key;
								count++;
							}
						}
						if (count > 0)
						{
							RefreshAssetListAndPreview(lastKey);
							Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Imported {0} shaders successfully."), count));
							return;
						}
					}
				}
			}
			else if (ext is ".gdshader" or ".shader")
			{
				string fileName = Path.GetFileName(sourceFilePath);
				string shaderKey = Path.GetFileNameWithoutExtension(sourceFilePath).ToLowerInvariant().Replace(" ", "_");
				string shaderName = Path.GetFileNameWithoutExtension(sourceFilePath);

				string targetDir = Path.Combine(wsPath, "Assets", "shaders");
				Directory.CreateDirectory(targetDir);
				string destPath = Path.Combine(targetDir, fileName);
				File.Copy(sourceFilePath, destPath, true);

				var config = new CustomShaderConfig
				{
					Key = shaderKey,
					Name = shaderName
				};
				SpawnDeathShaderManager.SaveCustomShader(config, wsPath);
				RefreshAssetListAndPreview(config.Key);
				Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Imported shader '{0}' successfully."), config.Name));
				return;
			}

			Hud?.ShowFeedback(TranslationServer.Translate("Unrecognized shader file format."));
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ObjectManagerDialog] ImportShaderFromFile error: {ex.Message}");
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Error importing shader: {0}"), ex.Message));
		}
	}

	private void OnConvertImagePressed()
	{
		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Select Image File to Convert to Realm format"),
			PathUtils.GetProjectRoot(),
			"",
			false,
			DisplayServer.FileDialogMode.OpenFile,
			new[] { "*.png,*.jpg,*.jpeg,*.bmp,*.webp,*.tga,*.dds,*.rtex ; Supported Image Files (*.png, *.jpg, *.jpeg, *.bmp, *.webp, *.tga, *.dds, *.rtex)" },
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
			{
				if (status && selectedPaths.Length > 0)
				{
					string sourceFilePath = selectedPaths[0];
					ConvertImageToRealmFormat(sourceFilePath);
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
		string cleanBase = Path.GetFileNameWithoutExtension(sourceFilePath).ToLowerInvariant().Replace(' ', '_');
		string ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();

		try
		{
			var assetsObj = MapAssetHelper.LoadUnionedAssets(wsPath);

			string targetCategory = _currentCategory;
			string subDir = targetCategory switch
			{
				"decals" => "decals",
				"icons" => "icons",
				"vfx_spritesheets" => "vfx",
				"vfx_radial" or "vfx_radials" => "vfx_radial",
				"vfx_vertical" or "vfx_verticals" => "vfx_vertical",
				"ribbons" or "ribbon_textures" => "ribbons",
				"noise_textures" or "noise" => "noise",
				"skyboxes" => "skyboxes",
				_ => "textures"
			};

			string destDir = Path.Combine(wsPath, "Assets", subDir);
			Directory.CreateDirectory(destDir);
			string destPath = Path.Combine(destDir, $"{cleanBase}.rtex");

			bool isRtexWithMeta = ext == ".rtex" && RealmMetadataHelper.HasRealmMetadata(sourceFilePath);
			TextureConversionResult convResult = default;
			int decalCols = 1;
			int decalRows = 1;
			int vfxCols = 4;
			int vfxRows = 4;
			float vfxFps = 20.0f;

			if (targetCategory == "decals" || targetCategory == "vfx_spritesheets")
			{
				string? sourceMeta = RealmMetadataHelper.ExtractMetadata(sourceFilePath);
				if (!string.IsNullOrEmpty(sourceMeta))
				{
					try
					{
						var node = JsonNode.Parse(sourceMeta);
						if (node?["columns"] != null && int.TryParse(node["columns"]?.ToString(), out int c) && c > 0)
						{
							if (targetCategory == "decals") decalCols = c;
							else vfxCols = c;
						}
						if (node?["rows"] != null && int.TryParse(node["rows"]?.ToString(), out int r) && r > 0)
						{
							if (targetCategory == "decals") decalRows = r;
							else vfxRows = r;
						}
						if (node?["fps"] != null && float.TryParse(node["fps"]?.ToString(), out float f) && f > 0.001f)
						{
							if (targetCategory == "vfx_spritesheets") vfxFps = f;
						}
					}
					catch { }
				}
			}

			if (isRtexWithMeta)
			{
				File.Copy(sourceFilePath, destPath, true);
			}
			else
			{
				if (targetCategory == "skyboxes")
				{
					convResult = TextureConverter.ProcessAndSaveSkybox(sourceFilePath, destPath);
				}
				else if (targetCategory == "decals")
				{
					convResult = TextureConverter.ProcessAndSaveDecalTexture(sourceFilePath, destPath, columns: decalCols, rows: decalRows);
				}
				else if (targetCategory == "icons")
				{
					convResult = TextureConverter.ProcessAndSaveIconTexture(sourceFilePath, destPath);
				}
				else if (targetCategory == "vfx_spritesheets")
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
				else if (targetCategory is "ribbons" or "ribbon_textures")
				{
					convResult = TextureConverter.ProcessAndSaveRibbonTexture(sourceFilePath, destPath);
				}
				else if (targetCategory == "noise_textures")
				{
					convResult = TextureConverter.ProcessAndSaveSingleLayerTexture(sourceFilePath, destPath, "noise_texture");
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
			}

			byte[] bytes = File.ReadAllBytes(destPath);
			string hash = RealmMetadataHelper.ComputeBlake3(bytes, ".rtex");

			if (!assetsObj.ContainsKey(targetCategory) || assetsObj[targetCategory] == null)
			{
				assetsObj[targetCategory] = new JsonObject();
			}

			if (targetCategory == "vfx_spritesheets")
			{
				assetsObj["vfx_spritesheets"].AsObject()[$"{cleanBase}.rtex"] = new JsonObject
				{
					["columns"] = vfxCols,
					["rows"] = vfxRows,
					["fps"] = Math.Round(vfxFps, 2),
					["hash"] = hash
				};
			}
			else if (targetCategory == "decals")
			{
				var decalDict = assetsObj["decals"].AsObject();
				string destFileName = $"{cleanBase}.rtex";
				var decalObj = (decalDict.ContainsKey(destFileName) && decalDict[destFileName] is JsonObject exObj) ? (exObj.DeepClone() as JsonObject) : new JsonObject();
				decalObj["hash"] = hash;
				if (decalCols > 1 || decalRows > 1 || decalObj.ContainsKey("columns") || decalObj.ContainsKey("rows"))
				{
					decalObj["columns"] = decalCols;
					decalObj["rows"] = decalRows;
				}
				decalDict[destFileName] = decalObj;
			}
			else if (targetCategory == "textures")
			{
				float calculatedScaleFactor = isRtexWithMeta
					? TextureConverter.CalculateLuminanceScaleFactor(destPath)
					: convResult.ScaleFactor;

				var texDict = assetsObj["textures"].AsObject();
				string destFileName = $"{cleanBase}.rtex";
				int nextSwatchIdx = 0;
				foreach (var kvp in texDict)
				{
					if (kvp.Value is JsonObject sObj && sObj.TryGetPropertyValue("swatchIndex", out var idxNode) && int.TryParse(idxNode?.ToString(), out int s))
					{
						if (s >= nextSwatchIdx) nextSwatchIdx = s + 1;
					}
				}

				texDict[destFileName] = new JsonObject
				{
					["hash"] = hash,
					["swatchIndex"] = nextSwatchIdx,
					["Scale_Factor"] = calculatedScaleFactor
				};

				if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
				{
					GameHost.Instance.GroundTerrain.ReloadTerrainTextures(true);
					Hud?.SetupTextureSwatches(false);
				}
			}
			else
			{
				assetsObj[targetCategory].AsObject()[$"{cleanBase}.rtex"] = hash;
			}

			MapAssetHelper.SaveAssetsToManifest(wsPath, assetsObj);
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Converted and imported {0}.rtex"), cleanBase));

			AssetIndexService.Instance?.RescanAllDirectories();
			RefreshAssetListAndPreview($"{cleanBase}.rtex");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ObjectManagerDialog] ConvertImageToRealmFormat error: {ex.Message}");
			Hud?.ShowFeedback($"Error converting image: {ex.Message}");
		}
	}

	private void OnConvertAudioPressed()
	{
		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Select Audio File to Convert to .ogg"),
			PathUtils.GetProjectRoot(),
			"",
			false,
			DisplayServer.FileDialogMode.OpenFile,
			new[] { "*.mp3,*.wav,*.aiff,*.aif,*.flac,*.aac,*.m4a,*.wma,*.ogg ; Audio Files (*.mp3, *.wav, *.aiff, *.aif, *.flac, *.aac, *.m4a, *.wma, *.ogg)" },
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
			{
				if (status && selectedPaths.Length > 0)
				{
					string sourceFilePath = selectedPaths[0];
					ConvertAudioToRealmFormat(sourceFilePath);
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
		string cleanBase = Path.GetFileNameWithoutExtension(sourceFilePath).ToLowerInvariant().Replace(' ', '_');

		try
		{
			string targetCategory = _currentCategory == "music" ? "music" : "sfx";
			string sub = targetCategory == "music" ? "music" : "sfx";
			string destDir = Path.Combine(wsPath, "Assets", "audio", sub);
			Directory.CreateDirectory(destDir);
			string destPath = Path.Combine(destDir, $"{cleanBase}.ogg");

			var res = AudioConverter.ConvertToOgg(sourceFilePath, destPath);
			if (!res.Success)
			{
				Hud?.ShowFeedback($"Failed to convert audio: {res.ErrorMessage}");
				return;
			}

			var assetsObj = MapAssetHelper.LoadUnionedAssets(wsPath);
			if (!assetsObj.ContainsKey(targetCategory) || assetsObj[targetCategory] == null)
			{
				assetsObj[targetCategory] = new JsonObject();
			}

			byte[] bytes = File.ReadAllBytes(destPath);
			string hash = RealmMetadataHelper.ComputeBlake3(bytes, ".ogg");
			assetsObj[targetCategory]!.AsObject()[$"{cleanBase}.ogg"] = hash;

			MapAssetHelper.SaveAssetsToManifest(wsPath, assetsObj);
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Converted and imported audio {0}.ogg"), cleanBase));

			AssetIndexService.Instance?.RescanAllDirectories();
			RefreshAssetListAndPreview($"{cleanBase}.ogg");
			PlayCurrentAudio();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ObjectManagerDialog] ConvertAudioToRealmFormat error: {ex.Message}");
			Hud?.ShowFeedback($"Error converting audio: {ex.Message}");
		}
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
					string sourceFilePath = selectedPaths[0];
					ConvertMixamoFileToRanim(sourceFilePath);
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

			var assetsObj = MapAssetHelper.LoadUnionedAssets(wsPath);
			if (!assetsObj.ContainsKey("animations") || assetsObj["animations"] == null)
			{
				assetsObj["animations"] = new JsonObject();
			}
			var animsObj = assetsObj["animations"]!.AsObject();

			int importedCount = 0;
			int skippedCount = 0;
			string firstSavedFileName = null;

			foreach (var (animName, animData) in extracted)
			{
				var (savedFileName, blake3, alreadyExisted) = MixamoAnimationImporter.SaveAnimationWithDeduplication(animsDir, animName, animData);
				animsObj[savedFileName] = blake3;
				if (alreadyExisted) skippedCount++;
				else importedCount++;
				if (firstSavedFileName == null) firstSavedFileName = savedFileName;
			}

			MapAssetHelper.SaveAssetsToManifest(wsPath, assetsObj);

			if (importedCount > 0)
			{
				Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Successfully converted and imported {0} .ranim animation(s)!"), importedCount));
			}
			else
			{
				Hud?.ShowFeedback(TranslationServer.Translate("Animation(s) already existed in map workspace (identical BLAKE3 hash)."));
			}

			AssetIndexService.Instance?.RescanAllDirectories();
			if (!string.IsNullOrEmpty(firstSavedFileName))
			{
				RefreshAssetListAndPreview(firstSavedFileName);
			}
			else
			{
				RefreshAssetList();
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ObjectManagerDialog] ConvertMixamoFileToRanim error: {ex.Message}");
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Error converting animation: {0}"), ex.Message));
		}
	}

	private void ConvertGlbToRealmFormat(string sourceFilePath)
	{
		if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath)) return;

		string wsPath = GetWorkspacePath();
		string cleanBase = Path.GetFileNameWithoutExtension(sourceFilePath).ToLowerInvariant().Replace(' ', '_');

		try
		{
			string subCat = "props";
			if (IsRmeshCategory(_currentCategory, out string rmeshSub))
			{
				subCat = rmeshSub switch
				{
					"characters" => "units",
					"items" => "projectiles",
					_ => rmeshSub
				};
			}

			string destDir = Path.Combine(wsPath, "Assets", "models", subCat);
			Directory.CreateDirectory(destDir);
			string destPath = Path.Combine(destDir, $"{cleanBase}.rmesh");

			int maxRes = subCat is "attachments" or "items" or "projectiles" or "weapons" ? 512 : 1024;
			string canonicalAssetType = subCat switch
			{
				"units" or "characters" => "Character",
				"buildings" => "Building",
				"attachments" or "items" or "projectiles" or "weapons" => "Item",
				_ => "Prop"
			};

			var convRes = Realm.Shared.ModelOptimization.ModelConverter.ConvertToRmesh(
				sourceFilePath,
				destPath,
				canonicalAssetType,
				force: true,
				options: new Realm.Shared.OptimizationOptions
				{
					SimplificationRatio = 0.5f,
					MaxTextureResolution = maxRes,
					ForceReDecimate = true
				});

			if (!convRes.Success)
			{
				Hud?.ShowFeedback($"Failed to optimize model: {convRes.ErrorMessage}");
				return;
			}

			var assetsObj = MapAssetHelper.LoadUnionedAssets(wsPath) ?? new JsonObject();
			if (!assetsObj.ContainsKey("glb") || assetsObj["glb"] == null) assetsObj["glb"] = new JsonObject();
			var glbObj = assetsObj["glb"]!.AsObject();
			if (!glbObj.ContainsKey(subCat) || glbObj[subCat] == null) glbObj[subCat] = new JsonObject();

			string hash = convRes.OutputBytes != null
				? Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(convRes.OutputBytes, ".rmesh")
				: Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(destPath);

			float defaultScale = subCat switch
			{
				"resources" => 2.75f,
				"buildings" => 1.5f,
				"props" => 1.25f,
				"units" or "characters" => 1.0f,
				"attachments" or "items" or "projectiles" or "weapons" => 1.0f,
				_ => 1.0f
			};

			var (minY, autoYOffset) = Realm.Godot.Utils.ModelCache.CalculateModelBounds(destPath, defaultScale);
			bool isPropOrRes = subCat == "resources" || subCat == "props" || subCat == "attachments" || subCat == "weapons" || subCat == "items" || subCat == "projectiles";

			var glbMetaObj = new JsonObject
			{
				["hash"] = hash,
				["min_y"] = minY,
				["scale"] = defaultScale,
				["y_offset"] = autoYOffset,
				["default_asset_type"] = subCat,
				["despill_player_color"] = false,
				["normalize_luminance"] = true,
				["ignore_player_color"] = isPropOrRes,
				["team_color"] = convRes.SupportsTeamColor
			};
			glbObj[subCat]![$"{cleanBase}.rmesh"] = glbMetaObj;

			string unitId = cleanBase;

			MetadataService.Instance.UpdateMetadata(wsPath, meta =>
			{
				meta.SetModelYOffset($"{cleanBase}.rmesh", autoYOffset);
				meta.SetModelScale($"{cleanBase}.rmesh", defaultScale);

				switch (subCat)
				{
					case "units" or "characters":
						bool updatedU = meta.UpdateUnit(unitId, u =>
						{
							if (autoYOffset != 0f) u.YOffset = autoYOffset;
							return u;
						});
						if (!updatedU)
						{
							meta.AddOrUpdateUnit(new GameHost.UnitMetadata
							{
								UnitId = unitId,
								Name = unitId,
								Description = "",
								ModelPath = $"{cleanBase}.rmesh",
								Scale = defaultScale,
								YOffset = autoYOffset,
								PathingType = 9,
								DespillPlayerColor = false,
								NormalizeLuminance = true
							});
						}
						break;
					case "buildings":
						bool updatedB = meta.UpdateBuilding(unitId, b =>
						{
							if (autoYOffset != 0f) b.YOffset = autoYOffset;
							return b;
						});
						if (!updatedB)
						{
							meta.AddOrUpdateBuilding(new GameHost.UnitMetadata
							{
								UnitId = unitId,
								Name = unitId,
								Description = "",
								ModelPath = $"{cleanBase}.rmesh",
								Scale = defaultScale,
								YOffset = autoYOffset,
								PathingType = 32,
								DespillPlayerColor = false,
								NormalizeLuminance = true
							});
						}
						break;
					case "resources":
						bool updatedR = meta.UpdateResource(unitId, r =>
						{
							if (autoYOffset != 0f) r.YOffset = autoYOffset;
							return r;
						});
						if (!updatedR)
						{
							meta.AddOrUpdateResource(new GameHost.ResourceMetadata
							{
								UnitId = unitId,
								Name = unitId,
								Description = "",
								ModelPath = $"{cleanBase}.rmesh",
								Scale = defaultScale,
								YOffset = autoYOffset,
								PathingType = 255,
								DespillPlayerColor = false,
								NormalizeLuminance = true,
								IgnorePlayerColor = true
							});
						}
						break;
					case "props":
						bool updatedP = meta.UpdateProp(unitId, p =>
						{
							if (autoYOffset != 0f) p.YOffset = autoYOffset;
							return p;
						});
						if (!updatedP)
						{
							meta.AddOrUpdateProp(new GameHost.PropMetadata
							{
								UnitId = unitId,
								Name = unitId,
								Description = "",
								ModelPath = $"{cleanBase}.rmesh",
								Scale = defaultScale,
								YOffset = autoYOffset,
								PathingType = 255,
								DespillPlayerColor = false,
								NormalizeLuminance = true,
								IgnorePlayerColor = true
							});
						}
						break;
				}
			});

			GameHost.Instance?.SetModelYOffset($"{cleanBase}.rmesh", autoYOffset);
			GameHost.Instance?.SetModelScale($"{cleanBase}.rmesh", defaultScale);

			MapAssetHelper.SaveAssetsToManifest(wsPath, assetsObj);
			MetadataService.Instance.CleanMetadata(wsPath);
			GameHost.Instance?.LoadUnitMetadata(wsPath);
			Hud?.RefreshEntityPalette();
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Converted and imported 3D model {0}.rmesh"), cleanBase));

			RefreshAssetList();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ObjectManagerDialog] ConvertGlbToRealmFormat error: {ex.Message}");
			Hud?.ShowFeedback($"Error converting 3D model: {ex.Message}");
		}
	}



	private void OnImportFileSelected(string sourceFilePath)
	{
		if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath)) return;

		string wsPath = GetWorkspacePath();
		string fileName = Path.GetFileName(sourceFilePath);
		string sourceExtension = Path.GetExtension(sourceFilePath).ToLowerInvariant();

		var indexedAsset = AssetIndexService.Instance.GetAssetByPath(sourceFilePath);
		if (indexedAsset != null && !string.IsNullOrWhiteSpace(indexedAsset.FileName) && !string.Equals(indexedAsset.FileName, Path.GetFileName(sourceFilePath), StringComparison.OrdinalIgnoreCase))
		{
			string clean = Path.GetFileName(indexedAsset.FileName.Trim());
			fileName = clean.EndsWith(sourceExtension, StringComparison.OrdinalIgnoreCase) ? clean : $"{clean}{sourceExtension}";
		}
		else
		{
			string? metaJson = Realm.Shared.Metadata.RealmMetadataHelper.ExtractMetadata(sourceFilePath);
			if (!string.IsNullOrEmpty(metaJson))
			{
				try
				{
					var metaObj = JsonNode.Parse(metaJson)?.AsObject();
					string? friendly = metaObj?["asset_name"]?.ToString()
						?? metaObj?["name"]?.ToString()
						?? metaObj?["original_filename"]?.ToString()
						?? metaObj?["FileName"]?.ToString();
					if (!string.IsNullOrWhiteSpace(friendly))
					{
						string clean = Path.GetFileName(friendly.Trim());
						fileName = clean.EndsWith(sourceExtension, StringComparison.OrdinalIgnoreCase) ? clean : $"{clean}{sourceExtension}";
					}
				}
				catch { }
			}
		}

		try
		{
			var assetsObj = MapAssetHelper.LoadUnionedAssets(wsPath);

			byte[] fileBytes = File.ReadAllBytes(sourceFilePath);
			string hash = ComputeHashHex(fileBytes);

			if (IsRmeshCategory(_currentCategory, out string subCategory))
			{
				string targetSub = subCategory switch
				{
					"characters" => "units",
					"items" => "projectiles",
					_ => subCategory
				};
				if (!sourceExtension.Equals(".rmesh", StringComparison.OrdinalIgnoreCase))
				{
					Hud?.OpenConvertGlbDialog(sourceFilePath, targetSub, (_) => RefreshAssetList());
					return;
				}
				string destDir = Path.Combine(wsPath, "Assets", "models", targetSub);
				Directory.CreateDirectory(destDir);
				string destPath = Path.Combine(destDir, fileName);
				File.Copy(sourceFilePath, destPath, true);
				Realm.Shared.Metadata.RealmMetadataHelper.EnsureMetadata(destPath);
				byte[] finalBytes = File.ReadAllBytes(destPath);
				hash = Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(finalBytes, ".rmesh");

				float defaultScale = targetSub switch
				{
					"resources" => 2.75f,
					"buildings" => 1.5f,
					"props" => 1.25f,
					"units" or "characters" => 1.0f,
					"attachments" or "items" or "projectiles" or "weapons" => 1.0f,
					_ => 1.0f
				};

				var (minY, autoYOffset) = Realm.Godot.Utils.ModelCache.CalculateModelBounds(destPath, defaultScale);
				bool isPropOrRes = targetSub == "resources" || targetSub == "props" || targetSub == "attachments" || targetSub == "weapons" || targetSub == "items" || targetSub == "projectiles";

				if (!assetsObj.ContainsKey("glb") || assetsObj["glb"] == null) assetsObj["glb"] = new JsonObject();
				var glbObj = assetsObj["glb"].AsObject();
				if (!glbObj.ContainsKey(targetSub) || glbObj[targetSub] == null) glbObj[targetSub] = new JsonObject();

				var glbMetaObj = new JsonObject
				{
					["hash"] = hash,
					["min_y"] = minY,
					["scale"] = defaultScale,
					["y_offset"] = autoYOffset,
					["default_asset_type"] = targetSub,
					["despill_player_color"] = false,
					["normalize_luminance"] = true,
					["ignore_player_color"] = isPropOrRes
				};
				glbObj[targetSub].AsObject()[fileName] = glbMetaObj;

				string unitId = Path.GetFileNameWithoutExtension(fileName);

				MetadataService.Instance.UpdateMetadata(wsPath, meta =>
				{
					meta.SetModelYOffset(fileName, autoYOffset);
					meta.SetModelScale(fileName, defaultScale);

					switch (targetSub)
					{
						case "units" or "characters":
							bool updatedU = meta.UpdateUnit(unitId, u =>
							{
								if (autoYOffset != 0f) u.YOffset = autoYOffset;
								return u;
							});
							if (!updatedU)
							{
								meta.AddOrUpdateUnit(new GameHost.UnitMetadata
								{
									UnitId = unitId,
									Name = unitId,
									Description = "",
									ModelPath = fileName,
									Scale = defaultScale,
									YOffset = autoYOffset,
									PathingType = 9,
									DespillPlayerColor = false,
									NormalizeLuminance = true
								});
							}
							break;
						case "buildings":
							bool updatedB = meta.UpdateBuilding(unitId, b =>
							{
								if (autoYOffset != 0f) b.YOffset = autoYOffset;
								return b;
							});
							if (!updatedB)
							{
								meta.AddOrUpdateBuilding(new GameHost.UnitMetadata
								{
									UnitId = unitId,
									Name = unitId,
									Description = "",
									ModelPath = fileName,
									Scale = defaultScale,
									YOffset = autoYOffset,
									PathingType = 32,
									DespillPlayerColor = false,
									NormalizeLuminance = true
								});
							}
							break;
						case "resources":
							bool updatedR = meta.UpdateResource(unitId, r =>
							{
								if (autoYOffset != 0f) r.YOffset = autoYOffset;
								return r;
							});
							if (!updatedR)
							{
								meta.AddOrUpdateResource(new GameHost.ResourceMetadata
								{
									UnitId = unitId,
									Name = unitId,
									Description = "",
									ModelPath = fileName,
									Scale = defaultScale,
									YOffset = autoYOffset,
									PathingType = 255,
									DespillPlayerColor = false,
									NormalizeLuminance = true,
									IgnorePlayerColor = true
								});
							}
							break;
						case "props":
							bool updatedP = meta.UpdateProp(unitId, p =>
							{
								if (autoYOffset != 0f) p.YOffset = autoYOffset;
								return p;
							});
							if (!updatedP)
							{
								meta.AddOrUpdateProp(new GameHost.PropMetadata
								{
									UnitId = unitId,
									Name = unitId,
									Description = "",
									ModelPath = fileName,
									Scale = defaultScale,
									YOffset = autoYOffset,
									PathingType = 255,
									DespillPlayerColor = false,
									NormalizeLuminance = true,
									IgnorePlayerColor = true
								});
							}
							break;
					}
				});

				GameHost.Instance?.SetModelYOffset(fileName, autoYOffset);
				GameHost.Instance?.SetModelScale(fileName, defaultScale);
			}
			else if (_currentCategory == "textures")
			{
				string effectiveRtexName = sourceExtension.Equals(".rtex", StringComparison.OrdinalIgnoreCase)
					? fileName
					: $"{Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant().Replace(' ', '_')}.rtex";

				var knownRibbons = Realm.Godot.Utils.TextureSwatchSlots.BuildKnownRibbonsCache(assetsObj, wsPath);

				if (!Realm.Godot.Utils.TextureSwatchSlots.ValidateCategory(effectiveRtexName, null, knownRibbons))
				{
					GD.PrintErr($"[ObjectManagerDialog] Asset '{effectiveRtexName}' cannot be imported as terrain texture.");
					Hud?.ShowFeedback(TranslationServer.Translate("Selected asset is not a valid terrain texture."));
					return;
				}

				if (!assetsObj.ContainsKey("textures") || assetsObj["textures"] == null) assetsObj["textures"] = new JsonObject();
				var texturesObj = assetsObj["textures"].AsObject();

				var occupied = new bool[Realm.Godot.Utils.TextureSwatchSlots.MaxSlots];
				int existingSlot = -1;
				if (texturesObj.TryGetPropertyValue(effectiveRtexName, out var existingEntry) && existingEntry is JsonObject exObj &&
					exObj.TryGetPropertyValue("swatchIndex", out var exIdx) && exIdx != null && int.TryParse(exIdx.ToString(), out int pEx))
				{
					existingSlot = pEx;
				}
				foreach (var kvp in texturesObj)
				{
					if (Realm.Godot.Utils.TextureSwatchSlots.ValidateCategory(kvp.Key, kvp.Value, knownRibbons))
					{
						if (kvp.Value is JsonObject sObj && sObj.TryGetPropertyValue("swatchIndex", out var idxNode) && idxNode != null && int.TryParse(idxNode.ToString(), out int parsed))
						{
							if (parsed >= 0 && parsed < Realm.Godot.Utils.TextureSwatchSlots.MaxSlots)
							{
								occupied[parsed] = true;
							}
						}
					}
				}

				int assignedSlot = (existingSlot >= 0 && existingSlot < Realm.Godot.Utils.TextureSwatchSlots.MaxSlots)
					? existingSlot
					: Realm.Godot.Utils.TextureSwatchSlots.FirstFreeSlot(occupied);

				if (assignedSlot < 0)
				{
					int selectedSlot = GameHost.Instance != null ? GameHost.Instance.EditorPaintTextureIndex : -1;
					if (selectedSlot >= 0 && selectedSlot < Realm.Godot.Utils.TextureSwatchSlots.MaxSlots)
					{
						assignedSlot = selectedSlot;
					}
				}

				if (assignedSlot < 0)
				{
					GD.PrintErr($"[ObjectManagerDialog] All 32 texture slots are occupied. Cannot import '{effectiveRtexName}'.");
					Hud?.ShowFeedback(TranslationServer.Translate("Cannot import texture: All 32 terrain slots are full."));
					return;
				}

				string prevOccupant = null;
				foreach (var kvp in texturesObj)
				{
					if (!kvp.Key.Equals(effectiveRtexName, StringComparison.OrdinalIgnoreCase))
					{
						if (kvp.Value is JsonObject sObj && sObj.TryGetPropertyValue("swatchIndex", out var idxNode) && int.TryParse(idxNode?.ToString(), out int parsed) && parsed == assignedSlot)
						{
							prevOccupant = kvp.Key;
							break;
						}
					}
				}

				Action doCommit = () =>
				{
					if (!string.IsNullOrEmpty(prevOccupant))
					{
						texturesObj.Remove(prevOccupant);
					}

					string destDir = Path.Combine(wsPath, "Assets", "textures");
					Directory.CreateDirectory(destDir);
					string destPath = Path.Combine(destDir, effectiveRtexName);

					if (sourceExtension.Equals(".rtex", StringComparison.OrdinalIgnoreCase))
					{
						File.Copy(sourceFilePath, destPath, true);
					}
					else
					{
						var convRes = Realm.Shared.Textures.TextureConverter.ConvertTextureFile(sourceFilePath, destPath, "terrain_texture", 4, 4);
						if (!convRes.Success)
						{
							Hud?.ShowFeedback($"Texture conversion failed: {convRes.ErrorMessage}");
							return;
						}
					}

					fileName = effectiveRtexName;
					Realm.Shared.Metadata.RealmMetadataHelper.EnsureMetadata(destPath);
					byte[] rtexBytes = File.ReadAllBytes(destPath);
					hash = Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(rtexBytes, ".rtex");

					float scaleFactor = Realm.Shared.Textures.TextureConverter.CalculateLuminanceScaleFactor(destPath);
					if (scaleFactor <= 0.0001f) scaleFactor = 1.0f;

					texturesObj[effectiveRtexName] = new JsonObject
					{
						["hash"] = hash,
						["swatchIndex"] = assignedSlot,
						["Scale_Factor"] = scaleFactor
					};

					MapAssetHelper.SaveAssetsToManifest(wsPath, assetsObj);
					MetadataService.Instance.CleanMetadata(wsPath);
					RefreshAssetList();
					LoadPreviewForAsset(_currentCategory, effectiveRtexName);
					Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Imported asset {0} successfully."), effectiveRtexName));

					if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
					{
						GameHost.Instance.GroundTerrain.ReloadTerrainTextures(true);
						Hud?.SetupTextureSwatches(false);
					}
				};

				if (!string.IsNullOrEmpty(prevOccupant))
				{
					string cleanPrevName = Path.GetFileNameWithoutExtension(prevOccupant).Replace('_', ' ');
					string msg = string.Format(TranslationServer.Translate("Replacing texture in Slot {0} ({1}) will update all areas of the terrain painted with this slot. Do you want to proceed?"), assignedSlot, cleanPrevName);
					Hud?.ShowConfirmationDialog(msg, doCommit, confirmText: "REPLACE", cancelText: "CANCEL");
					return;
				}

				doCommit();
				return;
			}
			else if (_currentCategory == "vfx_spritesheets")
			{
				string destDir = Path.Combine(wsPath, "Assets", "vfx");
				Directory.CreateDirectory(destDir);
				string destPath = Path.Combine(destDir, fileName);
				File.Copy(sourceFilePath, destPath, true);
				Realm.Shared.Metadata.RealmMetadataHelper.EnsureMetadata(destPath);
				byte[] bytes = File.ReadAllBytes(destPath);
				hash = Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(bytes, ".rtex");

				if (!assetsObj.ContainsKey("vfx_spritesheets") || assetsObj["vfx_spritesheets"] == null) assetsObj["vfx_spritesheets"] = new JsonObject();
				assetsObj["vfx_spritesheets"].AsObject()[fileName] = new JsonObject
				{
					["columns"] = 4,
					["rows"] = 4,
					["hash"] = hash
				};
			}
			else if (_currentCategory == "animations")
			{
				string destDir = Path.Combine(wsPath, "Assets", "animations");
				Directory.CreateDirectory(destDir);

				if (fileName.EndsWith(".ranim", StringComparison.OrdinalIgnoreCase))
				{
					string destPath = Path.Combine(destDir, fileName);
					File.Copy(sourceFilePath, destPath, true);
					Realm.Shared.Metadata.RealmMetadataHelper.EnsureMetadata(destPath);
					byte[] bytes = File.ReadAllBytes(destPath);
					hash = Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(bytes, ".ranim");
					if (!assetsObj.ContainsKey("animations") || assetsObj["animations"] == null) assetsObj["animations"] = new JsonObject();
					assetsObj["animations"].AsObject()[fileName] = hash;
				}
				else if (fileName.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
				{
					var res = Realm.Godot.Animation.MixamoAnimationImporter.ImportMixamoGlb(sourceFilePath, wsPath, "units");
					if (res.Success)
					{
						Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Imported {0} animations from {1}"), res.ExtractedAnimationFiles.Count, fileName));
					}
				}
			}
			else if (_currentCategory == "sfx" || _currentCategory == "music")
			{
				string sub = _currentCategory == "music" ? "music" : "sfx";
				string destDir = Path.Combine(wsPath, "Assets", "audio", sub);
				Directory.CreateDirectory(destDir);
				string destPath = Path.Combine(destDir, fileName);
				File.Copy(sourceFilePath, destPath, true);
				Realm.Shared.Metadata.RealmMetadataHelper.EnsureMetadata(destPath);
				byte[] bytes = File.ReadAllBytes(destPath);
				hash = Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(bytes, ".ogg");

				if (!assetsObj.ContainsKey(_currentCategory) || assetsObj[_currentCategory] == null) assetsObj[_currentCategory] = new JsonObject();
				assetsObj[_currentCategory].AsObject()[fileName] = hash;
			}
			else
			{
				string sub = _currentCategory switch
				{
					"vfx_spritesheets" => "vfx",
					"vfx_radial" or "vfx_radials" => "vfx_radial",
					"vfx_vertical" or "vfx_verticals" => "vfx_vertical",
					"decals" => "decals",
					"icons" => "icons",
					"ribbons" or "ribbon_textures" => "ribbons",
					"noise_textures" or "noise" => "noise",
					"skyboxes" => "skyboxes",
					_ => _currentCategory
				};
				string destDir = Path.Combine(wsPath, "Assets", sub);
				Directory.CreateDirectory(destDir);
				string destPath = Path.Combine(destDir, fileName);
				File.Copy(sourceFilePath, destPath, true);
				Realm.Shared.Metadata.RealmMetadataHelper.EnsureMetadata(destPath);
				byte[] bytes = File.ReadAllBytes(destPath);
				string ext = Path.GetExtension(destPath).ToLowerInvariant();
				hash = Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(bytes, ext);

				if (!assetsObj.ContainsKey(_currentCategory) || assetsObj[_currentCategory] == null) assetsObj[_currentCategory] = new JsonObject();
				assetsObj[_currentCategory].AsObject()[fileName] = hash;
			}

			MapAssetHelper.SaveAssetsToManifest(wsPath, assetsObj);
			MetadataService.Instance.CleanMetadata(wsPath);
			GameHost.Instance?.LoadUnitMetadata(wsPath);
			Hud?.RefreshEntityPalette();
			RefreshAssetList();
			string importedKey = _currentCategory == "textures" ? (Path.GetExtension(fileName).ToLowerInvariant() == ".rtex" ? fileName : $"{Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant().Replace(' ', '_')}.rtex") : fileName;
			LoadPreviewForAsset(_currentCategory, importedKey);
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Imported asset {0} successfully."), importedKey));

			if (_currentCategory == "textures")
			{
				if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
				{
					GameHost.Instance.GroundTerrain.ReloadTerrainTextures(true);
					Hud?.SetupTextureSwatches(false);
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ObjectManagerDialog] OnImportFileSelected error: {ex.Message}");
			Hud?.ShowFeedback($"Import error: {ex.Message}");
		}
	}

	private void OpenEditSubDialog(string category, string key, JsonNode extraData)
	{
		if (category == "vfx_spritesheets" || category == "vfx" || (extraData is JsonObject edObjVfx && edObjVfx.ContainsKey("asset_type") && edObjVfx["asset_type"]?.ToString() == "SpellSpritesheet"))
		{
			int cols = 4;
			int rows = 4;
			float fps = 20.0f;
			bool subframeBlend = true;

			string wsPath = GetWorkspacePath();
			try
			{
				var assetsObj = MapAssetHelper.LoadUnionedAssets(wsPath);
				var vfxSheets = assetsObj["vfx_spritesheets"]?.AsObject();
				if (vfxSheets != null)
				{
					string fileName = Path.GetFileName(key);
					string cleanBase = Path.GetFileNameWithoutExtension(key);

					JsonObject? sheetObj = null;
					if (vfxSheets.TryGetPropertyValue(fileName, out var s1) && s1 is JsonObject so1) sheetObj = so1;
					else if (vfxSheets.TryGetPropertyValue(key, out var s2) && s2 is JsonObject so2) sheetObj = so2;
					else if (vfxSheets.TryGetPropertyValue($"{cleanBase}.rtex", out var s3) && s3 is JsonObject so3) sheetObj = so3;
					else if (vfxSheets.TryGetPropertyValue($"{cleanBase}.png", out var s4) && s4 is JsonObject so4) sheetObj = so4;

					if (sheetObj != null)
					{
						if (sheetObj.TryGetPropertyValue("columns", out var cNode) && int.TryParse(cNode?.ToString(), out int parsedCols) && parsedCols > 0)
							cols = parsedCols;
						if (sheetObj.TryGetPropertyValue("rows", out var rNode) && int.TryParse(rNode?.ToString(), out int parsedRows) && parsedRows > 0)
							rows = parsedRows;
						if (sheetObj.TryGetPropertyValue("fps", out var fNode) && float.TryParse(fNode?.ToString(), out float parsedFps) && parsedFps > 0.001f)
							fps = parsedFps;
						if (sheetObj.TryGetPropertyValue("subframe_blend", out var sbNode) && bool.TryParse(sbNode?.ToString(), out bool parsedSb))
							subframeBlend = parsedSb;
					}
					else if (extraData is JsonObject obj)
					{
						if (obj.ContainsKey("columns")) cols = (int)obj["columns"];
						if (obj.ContainsKey("rows")) rows = (int)obj["rows"];
						if (obj.ContainsKey("fps") && float.TryParse(obj["fps"]?.ToString(), out float f) && f > 0.001f) fps = f;
						if (obj.ContainsKey("subframe_blend") && bool.TryParse(obj["subframe_blend"]?.ToString(), out bool sb)) subframeBlend = sb;
					}
				}
				else if (extraData is JsonObject obj)
				{
					if (obj.ContainsKey("columns")) cols = (int)obj["columns"];
					if (obj.ContainsKey("rows")) rows = (int)obj["rows"];
					if (obj.ContainsKey("fps") && float.TryParse(obj["fps"]?.ToString(), out float f) && f > 0.001f) fps = f;
					if (obj.ContainsKey("subframe_blend") && bool.TryParse(obj["subframe_blend"]?.ToString(), out bool sb)) subframeBlend = sb;
				}
			}
			catch
			{
				if (extraData is JsonObject obj)
				{
					if (obj.ContainsKey("columns")) cols = (int)obj["columns"];
					if (obj.ContainsKey("rows")) rows = (int)obj["rows"];
					if (obj.ContainsKey("fps") && float.TryParse(obj["fps"]?.ToString(), out float f) && f > 0.001f) fps = f;
					if (obj.ContainsKey("subframe_blend") && bool.TryParse(obj["subframe_blend"]?.ToString(), out bool sb)) subframeBlend = sb;
				}
			}

			_spritesheetEditDialog.OpenForSheet(key, cols, rows, fps, subframeBlend, (newCols, newRows, newFps, newSubframeBlend) =>
			{
				SaveSpritesheetGrid(key, newCols, newRows, newFps, newSubframeBlend);
				LoadVfxSpritesheet(key);
				RefreshAssetList();
			});
		}
		else if (category == "textures")
		{
			var texData = extraData as JsonObject ?? new JsonObject();
			_textureEditDialog.OpenForTexture(key, texData, (updatedData) =>
			{
				SaveTextureSwatch(key, updatedData);
				RefreshAssetList();
			});
		}
		else if (category == "decals" || (extraData is JsonObject edObj && edObj.ContainsKey("asset_type") && edObj["asset_type"]?.ToString() == "Decal"))
		{
			var decalData = extraData as JsonObject ?? new JsonObject();
			_decalEditDialog.OpenForDecal(key, decalData, (updatedData) =>
			{
				SaveDecalMetadata(key, updatedData);
				LoadPreviewForAsset(category, key);
				RefreshAssetList();
			});
		}
		else if (category == "shaders" || (extraData is JsonObject shObj && shObj.ContainsKey("asset_type") && shObj["asset_type"]?.ToString() == "Shader"))
		{
			_shaderEditDialog.OpenForShader(key, (updatedConfig) =>
			{
				RefreshAssetListAndPreview(updatedConfig?.Key ?? key);
			});
		}
	}

	private void SaveDecalMetadata(string key, JsonObject updatedData)
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		try
		{
			var assetsObj = MapAssetHelper.LoadUnionedAssets(wsPath);
			if (!assetsObj.ContainsKey("decals") || assetsObj["decals"] == null) assetsObj["decals"] = new JsonObject();
			var decalsDict = assetsObj["decals"]!.AsObject();

			JsonObject newObj;
			if (decalsDict.TryGetPropertyValue(key, out var exNode) && exNode is JsonObject exObj)
			{
				newObj = exObj;
			}
			else
			{
				newObj = new JsonObject();
				if (exNode is JsonValue v) newObj["hash"] = v.ToString();
			}

			foreach (var prop in updatedData)
			{
				newObj[prop.Key] = prop.Value?.DeepClone();
			}

			string targetKey = key;
			if (updatedData.TryGetPropertyValue("decal_id", out var newIdNode) && !string.IsNullOrWhiteSpace(newIdNode?.ToString()))
			{
				targetKey = newIdNode.ToString();
			}
			else if (updatedData.TryGetPropertyValue("DecalId", out var newIdNode2) && !string.IsNullOrWhiteSpace(newIdNode2?.ToString()))
			{
				targetKey = newIdNode2.ToString();
			}

			if (!string.Equals(key, targetKey, StringComparison.OrdinalIgnoreCase))
			{
				decalsDict.Remove(key);
				GameHost.Instance?.InvalidateDecalCache(key);
			}

			decalsDict[targetKey] = newObj;
			MapAssetHelper.SaveAssetsToManifest(wsPath, assetsObj);
			GameHost.Instance?.InvalidateDecalCache(targetKey);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ObjectManagerDialog] SaveDecalMetadata error: {ex.Message}");
		}
	}

	private void SaveSpritesheetGrid(string key, int columns, int rows, float fps = 20.0f, bool subframeBlend = true)
	{
		string wsPath = GetWorkspacePath();

		try
		{
			var assetsObj = MapAssetHelper.LoadUnionedAssets(wsPath);
			if (!assetsObj.ContainsKey("vfx_spritesheets") || assetsObj["vfx_spritesheets"] == null)
			{
				assetsObj["vfx_spritesheets"] = new JsonObject();
			}

			var vfxSheets = assetsObj["vfx_spritesheets"]!.AsObject();
			string fileName = Path.GetFileName(key);
			string cleanBase = Path.GetFileNameWithoutExtension(key);

			string targetKey = fileName;
			JsonNode? existingNode = null;
			if (vfxSheets.TryGetPropertyValue(fileName, out var s1)) { targetKey = fileName; existingNode = s1; }
			else if (vfxSheets.TryGetPropertyValue(key, out var s2)) { targetKey = key; existingNode = s2; }
			else if (vfxSheets.TryGetPropertyValue($"{cleanBase}.rtex", out var s3)) { targetKey = $"{cleanBase}.rtex"; existingNode = s3; }
			else if (vfxSheets.TryGetPropertyValue($"{cleanBase}.png", out var s4)) { targetKey = $"{cleanBase}.png"; existingNode = s4; }

			JsonObject newSheetObj;
			if (existingNode is JsonObject exObj)
			{
				newSheetObj = exObj;
			}
			else
			{
				newSheetObj = new JsonObject();
				if (existingNode is JsonValue v)
				{
					newSheetObj["hash"] = v.ToString();
				}
			}

			newSheetObj["columns"] = columns;
			newSheetObj["rows"] = rows;
			newSheetObj["fps"] = Math.Round(fps, 2);
			newSheetObj["subframe_blend"] = subframeBlend;

			vfxSheets[targetKey] = newSheetObj;
			MapAssetHelper.SaveAssetsToManifest(wsPath, assetsObj);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ObjectManagerDialog] SaveSpritesheetGrid error: {ex.Message}");
		}
	}

	private void SaveTextureSwatch(string key, JsonObject updatedData)
	{
		string wsPath = GetWorkspacePath();
		JsonObject? assets = null;
		try
		{
			assets = MapAssetHelper.LoadUnionedAssets(wsPath);
		}
		catch { }

		var knownRibbons = Realm.Godot.Utils.TextureSwatchSlots.BuildKnownRibbonsCache(assets, wsPath);
		if (!Realm.Godot.Utils.TextureSwatchSlots.ValidateCategory(key, null, knownRibbons))
		{
			GD.PrintErr($"[ObjectManagerDialog] Cannot save swatch for non-terrain texture '{key}'.");
			return;
		}

		try
		{
			if (assets == null) assets = MapAssetHelper.LoadUnionedAssets(wsPath);
			if (!assets.ContainsKey("textures") || assets["textures"] is not JsonObject)
			{
				assets["textures"] = new JsonObject();
			}
			var texturesDict = assets["textures"]!.AsObject();
			var node = texturesDict.ContainsKey(key) ? texturesDict[key] : null;
			if (node == null)
			{
				foreach (var kvp in texturesDict)
				{
					if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase) ||
						string.Equals(Path.GetFileNameWithoutExtension(kvp.Key), Path.GetFileNameWithoutExtension(key), StringComparison.OrdinalIgnoreCase))
					{
						node = kvp.Value;
						key = kvp.Key;
						break;
					}
				}
			}

			string hash = node is JsonObject o && o.ContainsKey("hash") ? o["hash"]?.ToString() : (node is JsonValue v ? v.ToString() : "");
			int swatchIdx = -1;
			if (node is JsonObject sObj && sObj.TryGetPropertyValue("swatchIndex", out var idxNode) && idxNode != null && int.TryParse(idxNode.ToString(), out int parsedIdx))
			{
				swatchIdx = parsedIdx;
			}

			if (swatchIdx < 0 || swatchIdx >= Realm.Godot.Utils.TextureSwatchSlots.MaxSlots)
			{
				var occupied = new bool[Realm.Godot.Utils.TextureSwatchSlots.MaxSlots];
				foreach (var kvp in texturesDict)
				{
					if (Realm.Godot.Utils.TextureSwatchSlots.ValidateCategory(kvp.Key, kvp.Value, knownRibbons))
					{
						if (kvp.Value is JsonObject itemObj && itemObj.TryGetPropertyValue("swatchIndex", out var sNode) && sNode != null && int.TryParse(sNode.ToString(), out int p) && p >= 0 && p < Realm.Godot.Utils.TextureSwatchSlots.MaxSlots)
						{
							occupied[p] = true;
						}
					}
				}
				swatchIdx = Realm.Godot.Utils.TextureSwatchSlots.FirstFreeSlot(occupied);
			}

			var newObj = new JsonObject();
			if (!string.IsNullOrEmpty(hash)) newObj["hash"] = hash;
			if (swatchIdx >= 0) newObj["swatchIndex"] = swatchIdx;
			if (node is JsonObject origObj)
			{
				if (origObj.TryGetPropertyValue("Scale_Factor", out var sf)) newObj["Scale_Factor"] = sf?.DeepClone();
			}
			foreach (var prop in updatedData)
			{
				if (prop.Key.Equals("swatchIndex", StringComparison.OrdinalIgnoreCase)) continue;
				newObj[prop.Key] = prop.Value?.DeepClone();
			}

			texturesDict[key] = newObj;
			MapAssetHelper.SaveAssetsToManifest(wsPath, assets, removeFromMetadata: true);

			if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
			{
				GameHost.Instance.GroundTerrain.ReloadTerrainTextures(true);
				Hud?.SetupTextureSwatches(false);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ObjectManagerDialog] SaveTextureSwatch error: {ex.Message}");
		}
	}

	private void DeleteAsset(string category, string key, string subCategory)
	{
		UIManager.Instance?.ShowConfirmationDialog(
			string.Format(TranslationServer.Translate("Are you sure you want to delete asset '{0}'?"), key),
			() =>
			{
				string wsPath = GetWorkspacePath();
				try
				{
					var assetsObj = MapAssetHelper.LoadUnionedAssets(wsPath);
					if (IsRmeshCategory(category, out string rmeshSub) || category == "glb" || category == "rmesh")
					{
						string targetSub = !string.IsNullOrEmpty(subCategory) ? subCategory : rmeshSub;
						assetsObj["glb"]?[targetSub]?.AsObject()?.Remove(key);
						assetsObj["rmesh"]?[targetSub]?.AsObject()?.Remove(key);
						string p = Path.Combine(wsPath, "Assets", "models", targetSub ?? "props", key);
						if (File.Exists(p)) File.Delete(p);
						if (File.Exists(p + ".import")) File.Delete(p + ".import");
						foreach (var sub in new[] { "units", "buildings", "resources", "props", "projectiles", "characters", "items", "attachments", "weapons" })
						{
							string cand = Path.Combine(wsPath, "Assets", "models", sub, key);
							if (File.Exists(cand)) File.Delete(cand);
							if (File.Exists(cand + ".import")) File.Delete(cand + ".import");
						}

						string unitId = Path.GetFileNameWithoutExtension(key);
						MetadataService.Instance.UpdateMetadata(wsPath, meta =>
						{
							switch (targetSub)
							{
								case "units" or "characters":
									meta.RemoveUnit(unitId);
									meta.CustomUnits?.RemoveAll(u => key.Equals(u.ModelPath, StringComparison.OrdinalIgnoreCase) || unitId.Equals(u.UnitId, StringComparison.OrdinalIgnoreCase));
									break;
								case "buildings":
									meta.RemoveBuilding(unitId);
									meta.CustomBuildings?.RemoveAll(b => key.Equals(b.ModelPath, StringComparison.OrdinalIgnoreCase) || unitId.Equals(b.UnitId, StringComparison.OrdinalIgnoreCase));
									break;
								case "resources":
									meta.RemoveResource(unitId);
									meta.CustomResources?.RemoveAll(r => key.Equals(r.ModelPath, StringComparison.OrdinalIgnoreCase) || unitId.Equals(r.UnitId, StringComparison.OrdinalIgnoreCase));
									break;
								case "props":
									meta.RemoveProp(unitId);
									meta.CustomProps?.RemoveAll(p => key.Equals(p.ModelPath, StringComparison.OrdinalIgnoreCase) || unitId.Equals(p.UnitId, StringComparison.OrdinalIgnoreCase));
									break;
							}
						});
					}
					else if (category == "textures")
					{
						if (assetsObj["textures"] is JsonObject texturesObj)
						{
							int deletedIdx = -1;
							if (texturesObj.ContainsKey(key) && texturesObj[key] is JsonObject delObj)
							{
								if (delObj.TryGetPropertyValue("swatchIndex", out var idxNode) && idxNode != null && int.TryParse(idxNode.ToString(), out int parsed))
								{
									deletedIdx = parsed;
								}
							}
							texturesObj.Remove(key);
							string p = Path.Combine(wsPath, "Assets", "textures", key);
							if (File.Exists(p)) File.Delete(p);

							if (deletedIdx >= 0)
							{
								var remap = new Dictionary<int, int>();
								remap[deletedIdx] = 0;

								foreach (var kvp in texturesObj)
								{
									if (kvp.Value is JsonObject sObj)
									{
										if (sObj.TryGetPropertyValue("swatchIndex", out var idxNode) && idxNode != null && int.TryParse(idxNode.ToString(), out int parsedIdx))
										{
											if (parsedIdx > deletedIdx)
											{
												int newIdx = parsedIdx - 1;
												sObj["swatchIndex"] = newIdx;
												remap[parsedIdx] = newIdx;
											}
										}
									}
								}

								GameHost.Instance?.GroundTerrain?.RemapSplatIndices(remap);
								SaveLoadService.RemapSplatExrFiles(wsPath, remap);
							}
						}
					}
					else if (category == "shaders")
					{
						if (assetsObj.ContainsKey("shaders") && assetsObj["shaders"] is JsonObject shObj)
						{
							shObj.Remove(key);
						}
					}
					else
					{
						assetsObj[category]?.AsObject()?.Remove(key);
						string sub = category switch
						{
							"vfx_spritesheets" => "vfx",
							"vfx_radial" or "vfx_radials" => "vfx_radial",
							"vfx_vertical" or "vfx_verticals" => "vfx_vertical",
							"animations" => "animations",
							"sfx" => "sfx",
							"music" => "music",
							"icons" => "icons",
							"decals" => "decals",
							"ribbons" or "ribbon_textures" => "ribbons",
							"noise_textures" or "noise" => "noise",
							"skyboxes" => "skyboxes",
							_ => category
						};
						string p = Path.Combine(wsPath, "Assets", sub, key);
						if (File.Exists(p)) File.Delete(p);
						if (File.Exists(p + ".import")) File.Delete(p + ".import");
						if (category is "sfx" or "music")
						{
							string pAudio = Path.Combine(wsPath, "Assets", "audio", sub, key);
							if (File.Exists(pAudio)) File.Delete(pAudio);
							if (File.Exists(pAudio + ".import")) File.Delete(pAudio + ".import");
						}
					}

					_riggedModelCache.Remove(key);
					_riggedModelCache.Remove(Path.GetFileName(key));
					ModelCache.Clear();

					MapAssetHelper.SaveAssetsToManifest(wsPath, assetsObj);
					SaveLoadService.SyncMetadataAssetsAndPrune(wsPath);
					GameHost.Instance?.LoadUnitMetadata(wsPath);
					Hud?.RefreshEntityPalette();

					if (_currentCategory == "animations" || _currentCategory == "shaders")
					{
						var validModels = GetPreviewMeshModels();
						if (string.IsNullOrEmpty(_selectedRanimBaseModel) || !validModels.Contains(_selectedRanimBaseModel))
						{
							_selectedRanimBaseModel = validModels.Count > 0 ? validModels[0] : string.Empty;
							_setRanimBaseModelValue?.Invoke(_selectedRanimBaseModel);
						}
					}

					if (category == "textures")
					{
						GameHost.Instance?.GroundTerrain?.ReloadTerrainTextures(true);
						Hud?.ReadMetadataAndRefreshTextures();
					}
					RefreshAssetList();
					ClearPreview();
					Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Deleted asset {0}."), key));
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[ObjectManagerDialog] DeleteAsset error: {ex.Message}");
				}
			}
		);
	}

	private string GetCategoryDisplayName(string cat) => cat switch
	{
		"rmesh_characters" or "rmesh_units" => TranslationServer.Translate("3D Models (Characters)"),
		"rmesh_buildings" => TranslationServer.Translate("3D Models (Buildings)"),
		"rmesh_props" or "rmesh_resources" => TranslationServer.Translate("3D Models (Props)"),
		"rmesh_items" or "rmesh_attachments" or "rmesh_weapons" or "rmesh_projectiles" => TranslationServer.Translate("3D Models (Items)"),
		"rmesh" => TranslationServer.Translate("3D Models (.rmesh)"),
		"textures" => TranslationServer.Translate("Terrain"),
		"vfx_spritesheets" => TranslationServer.Translate("Spritesheets"),
		"vfx_radial" => TranslationServer.Translate("VFX Radial"),
		"vfx_vertical" => TranslationServer.Translate("VFX Vertical"),
		"animations" => TranslationServer.Translate("Animations (.ranim)"),
		"sfx" => TranslationServer.Translate("Sound Effects (SFX)"),
		"music" => TranslationServer.Translate("Music"),
		"icons" => TranslationServer.Translate("Icons"),
		"decals" => TranslationServer.Translate("Decals"),
		"ribbons" or "ribbon_textures" => TranslationServer.Translate("Ribbons"),
		"noise_textures" or "noise" => TranslationServer.Translate("Noise"),
		"skyboxes" => TranslationServer.Translate("Skyboxes"),
		_ => cat
	};

	private void PruneUnusedAssets()
	{
		string catName = GetCategoryDisplayName(_currentCategory);
		UIManager.Instance?.ShowConfirmationDialog(
			string.Format(TranslationServer.Translate("Are you sure you want to prune all unused assets in '{0}'?"), catName),
			() => PerformPruneUnused()
		);
	}

	private void PerformPruneUnused()
	{
		string wsPath = GetWorkspacePath();

		try
		{
			var assetsObj = MapAssetHelper.LoadUnionedAssets(wsPath);
			if (assetsObj == null || assetsObj.Count == 0) return;

			string metaPath = Path.Combine(wsPath, "metadata.json");
			var root = File.Exists(metaPath)
				? JsonNode.Parse(File.ReadAllText(metaPath))?.AsObject()
				: null;

			var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			void AddRef(string val)
			{
				if (string.IsNullOrWhiteSpace(val)) return;
				string trimmed = val.Trim();
				referenced.Add(trimmed);
				string fn = Path.GetFileName(trimmed);
				if (!string.IsNullOrEmpty(fn))
				{
					referenced.Add(fn);
					string fnNoExt = Path.GetFileNameWithoutExtension(fn);
					if (!string.IsNullOrEmpty(fnNoExt))
					{
						referenced.Add(fnNoExt);
					}
				}
			}

			void CollectReferencesFromNode(JsonNode node)
			{
				if (node == null) return;
				if (node is JsonValue jVal)
				{
					AddRef(jVal.ToString());
				}
				else if (node is JsonArray jArr)
				{
					foreach (var child in jArr)
					{
						CollectReferencesFromNode(child);
					}
				}
				else if (node is JsonObject jObj)
				{
					foreach (var prop in jObj)
					{
						CollectReferencesFromNode(prop.Value);
					}
				}
			}

			// 1. Collect from all metadata.json sections EXCEPT "Assets"
			if (root != null)
			{
				foreach (var prop in root)
				{
					if (prop.Key.Equals("Assets", StringComparison.OrdinalIgnoreCase)) continue;
					CollectReferencesFromNode(prop.Value);
				}
			}

			// 2. Collect from terrain.json if present
			string terrainPath = Path.Combine(wsPath, "terrain.json");
			if (File.Exists(terrainPath))
			{
				try
				{
					var terrainRoot = JsonNode.Parse(File.ReadAllText(terrainPath));
					if (terrainRoot != null) CollectReferencesFromNode(terrainRoot);
				}
				catch { }
			}

			// 3. Collect from active ECS state / GameHost
			if (GameHost.Instance != null)
			{
				if (GameHost.Instance.EcsWorld != null && GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity))
				{
					if (GameHost.Instance.EcsWorld.Has<Realm.Ecs.Components.Core.EditorState>(GameHost.Instance.WorldEntity))
					{
						var es = GameHost.Instance.EcsWorld.Get<Realm.Ecs.Components.Core.EditorState>(GameHost.Instance.WorldEntity);
						if (!string.IsNullOrEmpty(es.SkyboxPath)) AddRef(es.SkyboxPath);
					}
				}
			}

			// 4. Collect string literals from all workspace code and data files
			foreach (var file in Directory.GetFiles(wsPath, "*.*", SearchOption.AllDirectories))
			{
				string ext = Path.GetExtension(file).ToLowerInvariant();
				if (ext == ".cs" || ext == ".json" || ext == ".txt" || ext == ".xml" || ext == ".gdshader" || ext == ".csproj")
				{
					if (Path.GetFileName(file).Equals("metadata.json", StringComparison.OrdinalIgnoreCase)) continue;
					if (Path.GetFileName(file).Equals("manifest.json", StringComparison.OrdinalIgnoreCase)) continue;
					if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
						file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) ||
						file.Contains(Path.DirectorySeparatorChar + ".godot" + Path.DirectorySeparatorChar) ||
						file.Contains(Path.DirectorySeparatorChar + ".vscode" + Path.DirectorySeparatorChar) ||
						file.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar)) continue;

					try
					{
						string content = File.ReadAllText(file);
						var matches = StringLiteralRegex().Matches(content);
						foreach (Match m in matches)
						{
							if (m.Groups.Count > 1 && m.Groups[1].Value.Length < 120)
							{
								AddRef(m.Groups[1].Value);
							}
						}
					}
					catch { }
				}
			}

			int prunedCount = 0;

			if (IsRmeshCategory(_currentCategory, out string rmeshSub))
			{
				string folderName = MapAssetHelper.NormalizeGlbSubCategory(rmeshSub);
				foreach (var containerKey in new[] { "rmesh", "glb", "models" })
				{
					if (assetsObj[containerKey] is JsonObject cObj && cObj[rmeshSub] is JsonObject subObj)
					{
						foreach (var modelProp in subObj.ToList())
						{
							if (!referenced.Contains(modelProp.Key) && !referenced.Contains(Path.GetFileNameWithoutExtension(modelProp.Key)))
							{
								subObj.Remove(modelProp.Key);
								string p = Path.Combine(wsPath, "Assets", "models", folderName, modelProp.Key);
								if (File.Exists(p)) File.Delete(p);
								prunedCount++;
							}
						}
					}
					else if (assetsObj[containerKey] is JsonObject cObj2 && cObj2[folderName] is JsonObject subObj2)
					{
						foreach (var modelProp in subObj2.ToList())
						{
							if (!referenced.Contains(modelProp.Key) && !referenced.Contains(Path.GetFileNameWithoutExtension(modelProp.Key)))
							{
								subObj2.Remove(modelProp.Key);
								string p = Path.Combine(wsPath, "Assets", "models", folderName, modelProp.Key);
								if (File.Exists(p)) File.Delete(p);
								prunedCount++;
							}
						}
					}
				}
			}
			else if (_currentCategory == "rmesh" || _currentCategory == "glb")
			{
				foreach (var containerKey in new[] { "rmesh", "glb", "models" })
				{
					if (assetsObj[containerKey] is JsonObject cObj)
					{
						foreach (var subProp in cObj.ToList())
						{
							if (subProp.Value is JsonObject subObj)
							{
								string folderName = MapAssetHelper.NormalizeGlbSubCategory(subProp.Key);
								foreach (var modelProp in subObj.ToList())
								{
									if (!referenced.Contains(modelProp.Key) && !referenced.Contains(Path.GetFileNameWithoutExtension(modelProp.Key)))
									{
										subObj.Remove(modelProp.Key);
										string p = Path.Combine(wsPath, "Assets", "models", folderName, modelProp.Key);
										if (File.Exists(p)) File.Delete(p);
										prunedCount++;
									}
								}
							}
						}
					}
				}
			}
			else if (_currentCategory == "textures")
			{
				if (assetsObj["textures"] is JsonObject texObj)
				{
					var usedIndices = new HashSet<int>();
					if (GameHost.Instance?.GroundTerrain != null)
					{
						var splatMap = GameHost.Instance.GroundTerrain.SplatMap;
						if (splatMap != null)
						{
							int sw = splatMap.GetLength(0);
							int sd = splatMap.GetLength(1);
							for (int z = 0; z < sd; z++)
							{
								for (int x = 0; x < sw; x++)
								{
									var s = splatMap[x, z];
									if (s.Weight0 > 0.001f) usedIndices.Add(s.Index0);
									if (s.Weight1 > 0.001f) usedIndices.Add(s.Index1);
									if (s.Weight2 > 0.001f) usedIndices.Add(s.Index2);
									if (s.Weight3 > 0.001f) usedIndices.Add(s.Index3);
								}
							}
						}

						var cliffSplat = GameHost.Instance.GroundTerrain.CliffSplatMap;
						if (cliffSplat != null)
						{
							int cw = cliffSplat.GetLength(0);
							int cd = cliffSplat.GetLength(1);
							for (int z = 0; z < cd; z++)
							{
								for (int x = 0; x < cw; x++)
								{
									var c = cliffSplat[x, z];
									if (c.Weight0 > 0.001f) usedIndices.Add(c.Index0);
									if (c.Weight1 > 0.001f) usedIndices.Add(c.Index1);
									if (c.Weight2 > 0.001f) usedIndices.Add(c.Index2);
									if (c.Weight3 > 0.001f) usedIndices.Add(c.Index3);
								}
							}
						}
					}

					var keptEntries = new List<(string Key, int OldSwatchIdx, JsonObject Obj)>();
					var indexRemap = new Dictionary<int, int>();

					foreach (var itemProp in texObj.ToList())
					{
						int swatchIdx = -1;
						if (itemProp.Value is JsonObject sObj && sObj.TryGetPropertyValue("swatchIndex", out var idxNode) && idxNode != null && int.TryParse(idxNode.ToString(), out int parsed))
						{
							swatchIdx = parsed;
						}

						bool isUsed = referenced.Contains(itemProp.Key) || referenced.Contains(Path.GetFileNameWithoutExtension(itemProp.Key));
						if (!isUsed && swatchIdx >= 0 && usedIndices.Contains(swatchIdx))
						{
							isUsed = true;
						}

						if (isUsed)
						{
							var keptObj = itemProp.Value as JsonObject ?? new JsonObject();
							keptEntries.Add((itemProp.Key, swatchIdx, keptObj));
						}
						else
						{
							texObj.Remove(itemProp.Key);
							string p = Path.Combine(wsPath, "Assets", "textures", itemProp.Key);
							if (File.Exists(p)) File.Delete(p);
							if (swatchIdx >= 0)
							{
								indexRemap[swatchIdx] = 0;
							}
							prunedCount++;
						}
					}

					keptEntries.Sort((a, b) => a.OldSwatchIdx.CompareTo(b.OldSwatchIdx));
					for (int i = 0; i < keptEntries.Count; i++)
					{
						var entry = keptEntries[i];
						int newIdx = i;
						if (entry.OldSwatchIdx >= 0)
						{
							indexRemap[entry.OldSwatchIdx] = newIdx;
						}
						entry.Obj["swatchIndex"] = newIdx;
					}

					if (prunedCount > 0)
					{
						GameHost.Instance?.GroundTerrain?.RemapSplatIndices(indexRemap);
						SaveLoadService.RemapSplatExrFiles(wsPath, indexRemap);
					}
				}
			}
			else
			{
				if (assetsObj[_currentCategory] is JsonObject catObj)
				{
					string subDir = _currentCategory switch
					{
						"vfx_spritesheets" => "vfx",
						"vfx_radial" or "vfx_radials" => "vfx_radial",
						"vfx_vertical" or "vfx_verticals" => "vfx_vertical",
						"animations" => "animations",
						"sfx" => "sfx",
						"music" => "music",
						"icons" => "icons",
						"decals" => "decals",
						"ribbons" or "ribbon_textures" => "ribbons",
						"noise_textures" or "noise" => "noise",
						"skyboxes" => "skyboxes",
						_ => _currentCategory
					};

					foreach (var itemProp in catObj.ToList())
					{
						if (!referenced.Contains(itemProp.Key) && !referenced.Contains(Path.GetFileNameWithoutExtension(itemProp.Key)))
						{
							catObj.Remove(itemProp.Key);
							string p = Path.Combine(wsPath, "Assets", subDir, itemProp.Key);
							if (File.Exists(p)) File.Delete(p);
							if (File.Exists(p + ".import")) File.Delete(p + ".import");
							prunedCount++;
						}
					}
				}
			}

			if (prunedCount > 0)
			{
				MapAssetHelper.SaveAssetsToManifest(wsPath, assetsObj);
				SaveLoadService.SyncMetadataAssetsAndPrune(wsPath);
				if (_currentCategory == "textures")
				{
					GameHost.Instance?.GroundTerrain?.ReloadTerrainTextures(true);
					Hud?.ReadMetadataAndRefreshTextures();
				}
				RefreshAssetList();
				ClearPreview();
				Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Pruned {0} unused asset(s) from {1}."), prunedCount, GetCategoryDisplayName(_currentCategory)));
			}
			else
			{
				Hud?.ShowFeedback(string.Format(TranslationServer.Translate("No unused assets found for '{0}'. All assets are currently referenced."), GetCategoryDisplayName(_currentCategory)));
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ObjectManagerDialog] PruneUnusedAssets error: {ex.Message}");
		}
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

	private static string ComputeHashHex(byte[] bytes)
	{
		using var sha = SHA256.Create();
		byte[] hash = sha.ComputeHash(bytes);
		return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
	}

	public override void CloseDialog()
	{
		StopCurrentAudio();
		ClearPreview();
		base.CloseDialog();
	}

	[GeneratedRegex("\"([^\"]*)\"")]
	private static partial Regex StringLiteralRegex();
}
