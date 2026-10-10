using Godot;
using Realm.Client.Services;
using Realm.Client.VFX;
using Realm.Shared.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Realm.Client.UI.MapEditor;

public partial class TemplateManagerDialog : Realm.Client.UI.MapEditor.FloatingPreview3DDialogBase
{
	private Node3D _simRoot;
	private Node3D _currentModelRoot;
	private AnimatedSprite3D _vfxSprite;
	private ProceduralVfxInstance3D? _previewVfxInstance;
	private Realm.Client.VisualProjectile3D? _previewProjectile;

	private PanelContainer _preview2DContainer;
	private VBoxContainer _tooltipPreviewContainer;
	private TextureRect _preview2DImage;
	private Label _lblPreview2DInfo;

	private PanelContainer _previewAudioContainer;
	private Label _lblAudioInfo;
	private Button _btnAudioPlay;
	private AudioStreamPlayer _audioPlayer;

	private OptionButton _optObjectCategory;
	private LineEdit _txtSearchFilter;
	private Button _btnAddObject;
	private VBoxContainer _listVBox;

	private Realm.Client.UI.MapEditor.EntityVisualEditDialog _entityVisualEditDialog;
	private WeaponVfxDialog _weaponVfxDialog;
	private Realm.Client.UI.MapEditor.AbilityVfxDialog _abilityVfxDialog;
	private VfxStudioDialog _vfxStudioDialog;
	private Realm.Client.UI.MapEditor.DecalSettingsDialog _decalEditDialog;
	private ShaderEditorDialog _shaderEditDialog;
	private SpritesheetAssetEditDialog _spritesheetEditDialog;
	private TerrainTextureEditDialog _textureEditDialog;
	private Realm.Client.UI.MapEditor.ItemUpgradeEditDialog _itemUpgradeEditDialog;

	private string _currentCategory = "units";
	private string _searchFilter = "";
	private string _currentPreviewTemplateID = "";

	private HBoxContainer _previewMeshRow;
	private OptionButton _optPreviewMesh;
	private List<string> _availablePreviewMeshes = new();
	private string _selectedPreviewMesh = "";

	private CustomShaderConfig? _currentShaderConfig;
	private float _shaderAnimTime;
	private bool _shaderAnimForward = true;

	public TemplateManagerDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Templates Manager"), new Vector2(720, 780))
	{
		SetUncompressedPanelTexture("res://Assets/UI/map_editor_assets_importer.png", 34, 40, 60, 60);

		_entityVisualEditDialog = new Realm.Client.UI.MapEditor.EntityVisualEditDialog(hud);
		_weaponVfxDialog = new WeaponVfxDialog(hud);
		_abilityVfxDialog = new Realm.Client.UI.MapEditor.AbilityVfxDialog(hud);
		_vfxStudioDialog = new VfxStudioDialog(hud);
		_decalEditDialog = new Realm.Client.UI.MapEditor.DecalSettingsDialog(hud);
		_shaderEditDialog = new ShaderEditorDialog(hud);
		_spritesheetEditDialog = new SpritesheetAssetEditDialog(hud);
		_textureEditDialog = new TerrainTextureEditDialog(hud);
		_itemUpgradeEditDialog = new Realm.Client.UI.MapEditor.ItemUpgradeEditDialog(hud);

		_entityVisualEditDialog.DialogClosed += ReloadCurrentPreview;
		_weaponVfxDialog.DialogClosed += ReloadCurrentPreview;
		_abilityVfxDialog.DialogClosed += ReloadCurrentPreview;
		_vfxStudioDialog.DialogClosed += ReloadCurrentPreview;
		_decalEditDialog.DialogClosed += ReloadCurrentPreview;
		_shaderEditDialog.DialogClosed += ReloadCurrentPreview;
		_spritesheetEditDialog.DialogClosed += ReloadCurrentPreview;
		_textureEditDialog.DialogClosed += ReloadCurrentPreview;
		_itemUpgradeEditDialog.DialogClosed += ReloadCurrentPreview;

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

		_tooltipPreviewContainer = new VBoxContainer();
		_tooltipPreviewContainer.Alignment = BoxContainer.AlignmentMode.Center;
		_tooltipPreviewContainer.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		_tooltipPreviewContainer.Visible = false;
		preview2DVBox.AddChild(_tooltipPreviewContainer);

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
		lblAudioIcon.Text = UnicodeIcons.AUDIO;
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

		_btnAudioPlay = AddButton(audioBtnRow, $"{UnicodeIcons.PLAY} " + TranslationServer.Translate("Play"), () => ToggleAudioPlayback(), "Play loaded audio", 11, new Vector2(80, 26));

		audioVBox.AddChild(audioBtnRow);
		_previewAudioContainer.AddChild(audioVBox);
		previewStack.AddChild(_previewAudioContainer);

		// 2. CAMERA PRESETS BAR
		AddCameraPresetToolbar(BodyContainer, includeBack: true);

		// 3. CATEGORY & FILTER & ADD BAR
		var catRow = new HBoxContainer();
		catRow.AddThemeConstantOverride("separation", 8);

		var lblCat = new Label();
		lblCat.Text = TranslationServer.Translate("Type:");
		lblCat.AddThemeFontSizeOverride("font_size", 11);
		lblCat.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		catRow.AddChild(lblCat);

		_optObjectCategory = new OptionButton();
		_optObjectCategory.AddThemeFontSizeOverride("font_size", 11);
		_optObjectCategory.CustomMinimumSize = new Vector2(170, 26);
		_optObjectCategory.AddItem(TranslationServer.Translate("👥 Units (unit/...)"), 0);
		_optObjectCategory.SetItemMetadata(0, "units");
		_optObjectCategory.AddItem(TranslationServer.Translate("🏢 Buildings (building/...)"), 1);
		_optObjectCategory.SetItemMetadata(1, "buildings");
		_optObjectCategory.AddItem(TranslationServer.Translate("🪵 Resources (resource/...)"), 2);
		_optObjectCategory.SetItemMetadata(2, "resources");
		_optObjectCategory.AddItem(TranslationServer.Translate("📦 Props (prop/...)"), 3);
		_optObjectCategory.SetItemMetadata(3, "props");
		_optObjectCategory.AddItem(TranslationServer.Translate("⚔️ Weapons (weapon/...)"), 4);
		_optObjectCategory.SetItemMetadata(4, "weapons");
		_optObjectCategory.AddItem(TranslationServer.Translate("🪄 Abilities (ability/...)"), 5);
		_optObjectCategory.SetItemMetadata(5, "abilities");
		_optObjectCategory.AddItem(TranslationServer.Translate("🛡️ Upgrades (upgrade/...)"), 6);
		_optObjectCategory.SetItemMetadata(6, "upgrades");
		_optObjectCategory.AddItem(TranslationServer.Translate("📦 Items (item/...)"), 7);
		_optObjectCategory.SetItemMetadata(7, "items");
		_optObjectCategory.AddItem(TranslationServer.Translate("🌱 Terrain"), 8);
		_optObjectCategory.SetItemMetadata(8, "terrain");
		_optObjectCategory.AddItem(TranslationServer.Translate("🎞️ Spritesheets"), 9);
		_optObjectCategory.SetItemMetadata(9, "spritesheets");
		_optObjectCategory.AddItem(TranslationServer.Translate("🎯 Decals"), 10);
		_optObjectCategory.SetItemMetadata(10, "decals");
		_optObjectCategory.AddItem(TranslationServer.Translate("✨ Shader"), 11);
		_optObjectCategory.SetItemMetadata(11, "shaders");
		_optObjectCategory.AddItem(TranslationServer.Translate("✨ VFX (vfx/...)"), 12);
		_optObjectCategory.SetItemMetadata(12, "vfx");

		_optObjectCategory.ItemSelected += (idx) =>
		{
			string cat = _optObjectCategory.GetItemMetadata((int)idx).AsString();
			SetCurrentCategory(cat);
		};
		catRow.AddChild(_optObjectCategory);

		_previewMeshRow = new HBoxContainer();
		_previewMeshRow.AddThemeConstantOverride("separation", 6);
		_previewMeshRow.Visible = false;

		var lblMesh = new Label();
		lblMesh.Text = TranslationServer.Translate("Preview Mesh:");
		lblMesh.AddThemeFontSizeOverride("font_size", 11);
		lblMesh.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_previewMeshRow.AddChild(lblMesh);

		_optPreviewMesh = new OptionButton();
		_optPreviewMesh.AddThemeFontSizeOverride("font_size", 11);
		_optPreviewMesh.CustomMinimumSize = new Vector2(160, 26);
		_optPreviewMesh.ItemSelected += (idx) =>
		{
			if (idx >= 0 && idx < _availablePreviewMeshes.Count)
			{
				_selectedPreviewMesh = _availablePreviewMeshes[(int)idx];
				ReloadCurrentPreview();
			}
		};
		_previewMeshRow.AddChild(_optPreviewMesh);
		catRow.AddChild(_previewMeshRow);

		var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		catRow.AddChild(spacer);

		_btnAddObject = AddButton(catRow, $"{UnicodeIcons.PLUS} " + TranslationServer.Translate("Add"), () => AddNewObjectForCategory(_currentCategory), "Add new object to metadata.json", 11, new Vector2(120, 26));

		BodyContainer.AddChild(catRow);

		// 4. SEARCH FILTER INPUT
		var searchRow = new HBoxContainer();
		searchRow.AddThemeConstantOverride("separation", 6);

		var lblSearch = new Label();
		lblSearch.Text = $"{UnicodeIcons.SEARCH} " + TranslationServer.Translate("Filter:");
		if (faFont != null) lblSearch.AddThemeFontOverride("font", faFont);
		lblSearch.AddThemeFontSizeOverride("font_size", 11);
		searchRow.AddChild(lblSearch);

		_txtSearchFilter = new LineEdit();
		_txtSearchFilter.PlaceholderText = TranslationServer.Translate("Type to filter objects by name or ID...");
		_txtSearchFilter.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_txtSearchFilter.AddThemeFontSizeOverride("font_size", 11);
		_txtSearchFilter.TextChanged += (text) =>
		{
			_searchFilter = text ?? string.Empty;
			RefreshObjectList();
		};
		searchRow.AddChild(_txtSearchFilter);

		BodyContainer.AddChild(searchRow);

		// 5. SCROLLABLE OBJECT LIST
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

	public override void OpenDialog()
	{
		base.OpenDialog();
		SetCurrentCategory(_currentCategory);
		ResetCameraDefault();
	}

	public void SetCurrentCategory(string category)
	{
		_currentCategory = category.ToLowerInvariant();
		for (int i = 0; i < _optObjectCategory.ItemCount; i++)
		{
			if (_optObjectCategory.GetItemMetadata(i).AsString().Equals(_currentCategory, StringComparison.OrdinalIgnoreCase))
			{
				_optObjectCategory.Select(i);
				break;
			}
		}

		bool isShader = _currentCategory is "shaders" or "shader";
		if (_previewMeshRow != null)
		{
			_previewMeshRow.Visible = isShader;
		}
		if (isShader)
		{
			PopulatePreviewMeshList();
		}

		if (_currentCategory == "weapons")
		{
			DefaultDistance = 5.5f;
			DefaultYaw = Mathf.DegToRad(30.0f);
			DefaultPitch = Mathf.DegToRad(15.0f);
			DefaultTargetPosition = new Vector3(0, 0.5f, 0);
		}
		else
		{
			DefaultDistance = 5.0f;
			DefaultYaw = Mathf.DegToRad(45.0f);
			DefaultPitch = Mathf.DegToRad(25.0f);
			DefaultTargetPosition = Vector3.Zero;
		}
		ResetCameraDefault();

		RefreshObjectList();
	}

	public override void CloseDialog()
	{
		ClearPreviewProjectile();
		base.CloseDialog();
	}

	public override void _Process(double delta)
	{
		base._Process(delta);

		if (Visible && (_currentCategory is "shaders" or "shader") && _currentShaderConfig != null && _currentModelRoot != null && GodotObject.IsInstanceValid(_currentModelRoot))
		{
			float speed = (float)delta;
			float dur = _currentShaderConfig.Duration > 0.05f ? _currentShaderConfig.Duration : 1.5f;

			if (_shaderAnimForward)
			{
				_shaderAnimTime += speed;
				if (_shaderAnimTime >= dur)
				{
					_shaderAnimTime = dur;
					_shaderAnimForward = false;
				}
			}
			else
			{
				_shaderAnimTime -= speed;
				if (_shaderAnimTime <= 0.0f)
				{
					_shaderAnimTime = 0.0f;
					_shaderAnimForward = true;
				}
			}

			float prog = Mathf.Clamp(_shaderAnimTime / dur, 0.0f, 1.0f);
			SpawnDeathShaderManager.ApplyShaderPreview(_currentModelRoot, _currentShaderConfig, prog);
		}
	}


	private void PopulatePreviewMeshList()
	{
		_availablePreviewMeshes.Clear();
		if (_optPreviewMesh == null) return;
		_optPreviewMesh.Clear();

		string wsPath = Services.MapWorkspaceService.GetActiveWorkspacePath();

		LoadPreviewMeshesFromManifest(wsPath);
		LoadPreviewMeshesFromDirectory(wsPath);
		EnsureDefaultMeshesAvailable();
		PopulatePreviewMeshOptions();
		SelectCurrentPreviewMesh();
	}

	private void LoadPreviewMeshesFromManifest(string wsPath)
	{
		try
		{
			var manifest = MapFileService.LoadManifest(wsPath);
			if (manifest?.Assets == null) return;

			foreach (var catKvp in manifest.Assets.GetAllCategories())
			{
				if (catKvp.Value == null) continue;
				foreach (var kvp in catKvp.Value)
				{
					AddMeshKeyIfValid(kvp.Key);
				}
			}
		}
		catch { }
	}

	private void AddMeshKeyIfValid(string key)
	{
		if (string.IsNullOrEmpty(key) || !key.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase)) return;
		
		string name = Path.GetFileName(key);
		if (!_availablePreviewMeshes.Contains(name))
		{
			_availablePreviewMeshes.Add(name);
		}
	}

	private void LoadPreviewMeshesFromDirectory(string wsPath)
	{
		string modelsDir = Path.Combine(wsPath, "Assets", "models");
		if (!Directory.Exists(modelsDir)) return;

		try
		{
			var files = Directory.GetFiles(modelsDir, "*.rmesh", SearchOption.AllDirectories);
			foreach (var f in files)
			{
				string name = Path.GetFileName(f);
				if (!_availablePreviewMeshes.Contains(name))
				{
					_availablePreviewMeshes.Add(name);
				}
			}
		}
		catch { }
	}

	private void EnsureDefaultMeshesAvailable()
	{
		if (_availablePreviewMeshes.Count == 0)
		{
			_availablePreviewMeshes.Add("(Sample Building Cube)");
			_availablePreviewMeshes.Add("(Sample Unit Capsule)");
		}
	}

	private void PopulatePreviewMeshOptions()
	{
		int idx = 0;
		foreach (var m in _availablePreviewMeshes)
		{
			_optPreviewMesh.AddItem(m, idx++);
		}
	}

	private void SelectCurrentPreviewMesh()
	{
		int selectedIdx = !string.IsNullOrEmpty(_selectedPreviewMesh) ? _availablePreviewMeshes.IndexOf(_selectedPreviewMesh) : -1;
		if (selectedIdx >= 0)
		{
			_optPreviewMesh.Selected = selectedIdx;
		}
		else if (_availablePreviewMeshes.Count > 0)
		{
			_selectedPreviewMesh = _availablePreviewMeshes[0];
			_optPreviewMesh.Selected = 0;
		}
	}

	public void RefreshObjectList()
	{
		if (_listVBox == null) return;

		foreach (Node child in _listVBox.GetChildren())
		{
			child.QueueFree();
		}

		var items = GetObjectsForCategory(_currentCategory);
		if (!string.IsNullOrEmpty(_searchFilter))
		{
			items = items.Where(i =>
				i.TemplateID.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
				i.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
				i.Description.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)
			).ToList();
		}

		if (items.Count == 0)
		{
			var lblEmpty = new Label();
			lblEmpty.Text = TranslationServer.Translate("No objects found in metadata.json for this domain.");
			lblEmpty.AddThemeFontSizeOverride("font_size", 11);
			lblEmpty.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.70f));
			_listVBox.AddChild(lblEmpty);
			return;
		}

		foreach (var item in items)
		{
			var row = CreateObjectRow(item);
			_listVBox.AddChild(row);
		}

		ReloadCurrentPreview();
	}

	public void ReloadCurrentPreview()
	{
		RtexIconLoader.ClearCache();
		var items = GetObjectsForCategory(_currentCategory);
		if (items.Count == 0) return;

		var match = items.FirstOrDefault(i => i.TemplateID.Equals(_currentPreviewTemplateID, StringComparison.OrdinalIgnoreCase));
		if (!string.IsNullOrEmpty(match.TemplateID))
		{
			LoadPreviewForObject(match);
		}
		else
		{
			LoadPreviewForObject(items[0]);
		}
	}

	private struct ObjectItemInfo
	{
		public string Category;
		public string TemplateID;
		public string Name;
		public string Description;
		public string ModelPath;
		public string VisualMode;
		public string IconPath;
		public float Scale;
	}


	private static readonly Dictionary<string, Func<TemplateManagerDialog, MapMetadata, List<ObjectItemInfo>>> CategoryHandlers = new(StringComparer.OrdinalIgnoreCase)
	{
		{ "units", (dialog, meta) => dialog.GetUnitsInfo(meta) },
		{ "buildings", (dialog, meta) => dialog.GetBuildingsInfo(meta) },
		{ "resources", (dialog, meta) => dialog.GetResourcesInfo(meta) },
		{ "props", (dialog, meta) => dialog.GetPropsInfo(meta) },
		{ "weapons", (dialog, meta) => dialog.GetWeaponsInfo(meta) },
		{ "abilities", (dialog, meta) => dialog.GetAbilitiesInfo(meta) },
		{ "upgrades", (dialog, meta) => dialog.GetUpgradesInfo(meta) },
		{ "items", (dialog, meta) => dialog.GetItemsInfo(meta) },
		{ "terrain", (dialog, meta) => dialog.GetTerrainInfo(meta) },
		{ "spritesheets", (dialog, meta) => dialog.GetSpritesheetsInfo(meta) },
		{ "decals", (dialog, meta) => dialog.GetDecalsInfo(meta) },
		{ "shaders", (dialog, meta) => dialog.GetShadersInfo(meta) },
		{ "shader", (dialog, meta) => dialog.GetShadersInfo(meta) },
		{ "spawnshader", (dialog, meta) => dialog.GetShadersInfo(meta) },
		{ "spawnshaders", (dialog, meta) => dialog.GetShadersInfo(meta) },
		{ "vfx", (dialog, meta) => dialog.GetVfxInfo(meta) }
	};

	private List<ObjectItemInfo> GetObjectsForCategory(string category)
	{
		string wsPath = Services.MapWorkspaceService.GetActiveWorkspacePath();
		if (!MetadataService.Instance.TryLoadMetadata(wsPath, out var meta) || meta == null)
		{
			return new List<ObjectItemInfo>();
		}

		return CategoryHandlers.TryGetValue(category, out var handler)
			? handler(this, meta)
			: new List<ObjectItemInfo>();
	}

	private List<ObjectItemInfo> GetUnitsInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.Templates?.Units == null) return list;
		foreach (var u in meta.Templates.Units)
		{
			list.Add(new ObjectItemInfo
			{
				Category = "units",
				TemplateID = u.TemplateID,
				Name = u.Name ?? u.TemplateID,
				Description = u.Description ?? "",
				ModelPath = u.ModelPath ?? "",
				VisualMode = u.VisualMode ?? "GroundPlane",
				Scale = u.Scale > 0 ? u.Scale : 1.0f
			});
		}
		return list;
	}

	private List<ObjectItemInfo> GetBuildingsInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.Templates?.Buildings == null) return list;
		foreach (var b in meta.Templates.Buildings)
		{
			list.Add(new ObjectItemInfo
			{
				Category = "buildings",
				TemplateID = b.TemplateID,
				Name = b.Name ?? b.TemplateID,
				Description = b.Description ?? "",
				ModelPath = b.ModelPath ?? "",
				VisualMode = b.VisualMode ?? "GroundPlane",
				Scale = b.Scale > 0 ? b.Scale : 1.0f
			});
		}
		return list;
	}

	private List<ObjectItemInfo> GetResourcesInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.Templates?.Resources == null) return list;
		foreach (var r in meta.Templates.Resources)
		{
			list.Add(new ObjectItemInfo
			{
				Category = "resources",
				TemplateID = r.TemplateID,
				Name = r.Name ?? r.TemplateID,
				Description = r.Description ?? "",
				ModelPath = r.ModelPath ?? "",
				VisualMode = r.VisualMode ?? "GroundPlane",
				Scale = r.Scale > 0 ? r.Scale : 1.0f
			});
		}
		return list;
	}

	private List<ObjectItemInfo> GetPropsInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.Templates?.Props == null) return list;
		foreach (var p in meta.Templates.Props)
		{
			list.Add(new ObjectItemInfo
			{
				Category = "props",
				TemplateID = p.TemplateID,
				Name = p.Name ?? p.TemplateID,
				Description = p.Description ?? "",
				ModelPath = p.ModelPath ?? "",
				VisualMode = p.VisualMode ?? "GroundPlane",
				Scale = p.Scale > 0 ? p.Scale : 1.0f
			});
		}
		return list;
	}

	private List<ObjectItemInfo> GetWeaponsInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.Templates?.Weapons == null) return list;
		foreach (var w in meta.Templates.Weapons)
		{
			list.Add(new ObjectItemInfo
			{
				Category = "weapons",
				TemplateID = w.TemplateID,
				Name = w.Name ?? w.TemplateID,
				Description = $"Damage: {w.Damage} | Range: {w.Range} | Type: {w.AttackType}",
				ModelPath = w.ProjectileModelPath ?? ""
			});
		}
		return list;
	}

	private List<ObjectItemInfo> GetAbilitiesInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.Templates?.Abilities == null) return list;
		foreach (var a in meta.Templates.Abilities)
		{
			list.Add(new ObjectItemInfo
			{
				Category = "abilities",
				TemplateID = a.TemplateID,
				Name = a.Name ?? a.TemplateID,
				Description = a.Description ?? "",
				IconPath = a.IconPath ?? ""
			});
		}
		return list;
	}

	private List<ObjectItemInfo> GetUpgradesInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.Templates?.Upgrades == null) return list;
		foreach (var u in meta.Templates.Upgrades)
		{
			list.Add(new ObjectItemInfo
			{
				Category = "upgrades",
				TemplateID = u.TemplateID,
				Name = u.Name ?? u.TemplateID,
				Description = u.Description ?? "",
				IconPath = u.IconPath ?? ""
			});
		}
		return list;
	}

	private List<ObjectItemInfo> GetItemsInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.Templates?.Items == null) return list;
		foreach (var itm in meta.Templates.Items)
		{
			list.Add(new ObjectItemInfo
			{
				Category = "items",
				TemplateID = itm.TemplateID,
				Name = itm.Name ?? itm.TemplateID,
				Description = itm.Description ?? "",
				IconPath = itm.IconPath ?? ""
			});
		}
		return list;
	}

	private List<ObjectItemInfo> GetTerrainInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.Textures == null) return list;
		foreach (var kvp in meta.Textures)
		{
			string normalizedId = TemplateIDHelper.NormalizeTemplateID("terrain", kvp.Key);
			var (_, slug) = TemplateIDHelper.ParseTemplateID(normalizedId);
			string rtex = !string.IsNullOrWhiteSpace(kvp.Value?.TexturePath) ? kvp.Value.TexturePath : (!string.IsNullOrWhiteSpace(slug) ? $"{slug}.rtex" : kvp.Key);
			list.Add(new ObjectItemInfo
			{
				Category = "terrain",
				TemplateID = normalizedId,
				Name = rtex,
				Description = "Terrain texture swatch config",
				ModelPath = rtex
			});
		}
		return list;
	}

	private List<ObjectItemInfo> GetSpritesheetsInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.VfxSpritesheets == null) return list;
		foreach (var kvp in meta.VfxSpritesheets)
		{
			list.Add(CreateSpritesheetInfo(kvp.Key, kvp.Value));
		}
		return list;
	}

	private ObjectItemInfo CreateSpritesheetInfo(string key, VfxMetadata sheetObj)
	{
		string normalizedId = TemplateIDHelper.NormalizeTemplateID("spritesheet", key);
		var (_, slug) = TemplateIDHelper.ParseTemplateID(normalizedId);
		int cols = sheetObj?.Columns ?? 1;
		int rows = sheetObj?.Rows ?? 1;
		float fps = sheetObj?.Fps ?? 20.0f;
		string rtex = !string.IsNullOrEmpty(sheetObj?.TexturePath) ? sheetObj.TexturePath : (!string.IsNullOrWhiteSpace(slug) ? $"{slug}.rtex" : key);
		return new ObjectItemInfo
		{
			Category = "spritesheets",
			TemplateID = normalizedId,
			Name = rtex,
			Description = $"{cols}x{rows} @ {fps} FPS",
			ModelPath = rtex
		};
	}

	private List<ObjectItemInfo> GetDecalsInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.Decals == null) return list;
		foreach (var kvp in meta.Decals)
		{
			string normalizedId = TemplateIDHelper.NormalizeTemplateID("decal", kvp.Key);
			var (_, slug) = TemplateIDHelper.ParseTemplateID(normalizedId);
			string rtex = !string.IsNullOrWhiteSpace(kvp.Value?.TexturePath) ? kvp.Value.TexturePath : (!string.IsNullOrWhiteSpace(slug) ? $"{slug}.rtex" : kvp.Key);
			list.Add(new ObjectItemInfo
			{
				Category = "decals",
				TemplateID = normalizedId,
				Name = rtex,
				Description = "Decal configuration",
				ModelPath = rtex
			});
		}
		return list;
	}

	private List<ObjectItemInfo> GetShadersInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.SpawnShaders == null) return list;
		foreach (var kvp in meta.SpawnShaders)
		{
			string normalizedId = TemplateIDHelper.NormalizeTemplateID("SpawnShader", kvp.Key);
			var (_, slug) = TemplateIDHelper.ParseTemplateID(normalizedId);
			list.Add(new ObjectItemInfo
			{
				Category = "shaders",
				TemplateID = normalizedId,
				Name = slug,
				Description = "Custom visual shader config"
			});
		}
		return list;
	}

	private List<ObjectItemInfo> GetVfxInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.Templates?.Vfx == null) return list;
		foreach (var v in meta.Templates.Vfx)
		{
			list.Add(new ObjectItemInfo
			{
				Category = "vfx",
				TemplateID = v.VfxId,
				Name = v.Name ?? v.VfxId,
				Description = $"{v.PrimitiveType} | {v.BlendMode}",
				ModelPath = v.BaseTexture
			});
		}
		return list;
	}

	private Control CreateObjectRow(ObjectItemInfo item)
	{
		var row = new PanelContainer();
		row.CustomMinimumSize = new Vector2(0, 42);
		row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		row.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());

		var hBox = new HBoxContainer();
		hBox.AddThemeConstantOverride("separation", 8);
		row.AddChild(hBox);

		// Clickable selection for preview
		var selectBtn = new Button();
		selectBtn.Flat = true;
		selectBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		selectBtn.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		selectBtn.Alignment = HorizontalAlignment.Left;
		selectBtn.Pressed += () => LoadPreviewForObject(item);

		var contentBox = new HBoxContainer();
		contentBox.AddThemeConstantOverride("separation", 8);
		selectBtn.AddChild(contentBox);

		var infoVBox = new VBoxContainer();
		infoVBox.AddThemeConstantOverride("separation", 2);

		var lblName = new Label();
		lblName.Text = item.Name;
		lblName.AddThemeFontSizeOverride("font_size", 12);
		lblName.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		infoVBox.AddChild(lblName);

		var lblId = new Label();
		lblId.Text = item.TemplateID;
		lblId.AddThemeFontSizeOverride("font_size", 10);
		lblId.AddThemeColorOverride("font_color", new Color(0.6f, 0.65f, 0.75f));
		infoVBox.AddChild(lblId);

		contentBox.AddChild(infoVBox);
		hBox.AddChild(selectBtn);

		// Action buttons: Preview, Edit & Delete
		var actionsHBox = new HBoxContainer();
		actionsHBox.AddThemeConstantOverride("separation", 4);

		var faFont = Hud?.GetFontAwesomeFont();

		var btnPreview = new Button();
		btnPreview.Text = UnicodeIcons.EYE;
		btnPreview.Set("icon_max_width", 0);
		if (faFont != null) btnPreview.AddThemeFontOverride("font", faFont);
		btnPreview.TooltipText = TranslationServer.Translate("Preview Object");
		btnPreview.CustomMinimumSize = new Vector2(28, 28);
		btnPreview.Pressed += () => LoadPreviewForObject(item);
		actionsHBox.AddChild(btnPreview);

		var btnEdit = new Button();
		btnEdit.Text = UnicodeIcons.EDIT;
		btnEdit.Set("icon_max_width", 0);
		if (faFont != null) btnEdit.AddThemeFontOverride("font", faFont);
		btnEdit.TooltipText = TranslationServer.Translate("Edit Object Properties");
		btnEdit.CustomMinimumSize = new Vector2(28, 28);
		btnEdit.Pressed += () => OpenEditDialogForObject(item);
		actionsHBox.AddChild(btnEdit);

		var btnDelete = new Button();
		btnDelete.Text = UnicodeIcons.TRASH_ALT;
		btnDelete.Set("icon_max_width", 0);
		if (faFont != null) btnDelete.AddThemeFontOverride("font", faFont);
		btnDelete.TooltipText = TranslationServer.Translate("Delete Object from metadata.json");
		btnDelete.CustomMinimumSize = new Vector2(28, 28);
		btnDelete.Pressed += () => DeleteObject(item);
		actionsHBox.AddChild(btnDelete);

		hBox.AddChild(actionsHBox);
		return row;
	}


	private void OpenEditDialogForObject(ObjectItemInfo item)
	{
		_currentPreviewTemplateID = item.TemplateID;
		string wsPath = Services.MapWorkspaceService.GetActiveWorkspacePath();

		switch (item.Category)
		{
			case "units" or "buildings" or "resources" or "props":
			case "upgrades" or "items":
				OpenDefaultEditDialog(item);
				break;
			case "weapons":
				OpenWeaponEditDialog(item, wsPath);
				break;
			case "abilities":
				OpenAbilityEditDialog(item, wsPath);
				break;
			case "terrain":
				OpenTerrainEditDialog(item, wsPath);
				break;
			case "spritesheets":
				OpenSpritesheetEditDialog(item, wsPath);
				break;
			case "decals":
				OpenDecalEditDialog(item, wsPath);
				break;
			case "shaders" or "shader" or "spawnshader" or "spawnshaders":
				OpenShaderEditDialog(item, wsPath);
				break;
			case "vfx":
				OpenVfxEditDialog(item, wsPath);
				break;
			default:
				OpenDefaultEditDialog(item);
				break;
		}
	}

	private void OpenWeaponEditDialog(ObjectItemInfo item, string wsPath)
	{
		if (MetadataService.Instance.TryLoadMetadata(wsPath, out var meta) && meta?.Templates?.Weapons != null)
		{
			int wIdx = meta.Templates.Weapons.FindIndex(x => string.Equals(x.TemplateID, item.TemplateID, StringComparison.OrdinalIgnoreCase));
			if (wIdx >= 0)
			{
				var w = meta.Templates.Weapons[wIdx];
				_weaponVfxDialog.OpenForWeapon(w.TemplateID, w, updatedWeapon =>
				{
					MetadataService.Instance.UpdateMetadata(wsPath, m =>
					{
						if (!string.Equals(item.TemplateID, updatedWeapon.TemplateID, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(item.TemplateID))
						{
							m.RemoveWeapon(item.TemplateID);
						}
						m.AddOrUpdateWeapon(updatedWeapon);
					});
					_currentPreviewTemplateID = updatedWeapon.TemplateID;
					RefreshObjectList();
				});
			}
		}
	}

	private void OpenAbilityEditDialog(ObjectItemInfo item, string wsPath)
	{
		if (!MetadataService.Instance.TryLoadMetadata(wsPath, out var metaAbi) || metaAbi?.Templates?.Abilities == null) return;

		var a = metaAbi.Templates.Abilities.FirstOrDefault(x => string.Equals(x.TemplateID, item.TemplateID, StringComparison.OrdinalIgnoreCase));
		if (a == null) return;

		var jsonObj = new JsonObject
		{
			["TemplateID"] = a.TemplateID,
			["AbilityId"] = a.TemplateID,
			["Name"] = a.Name,
			["VisualEffect"] = a.VisualEffect,
			["CastSound"] = a.CastSound,
			["IconPath"] = a.IconPath,
			["AreaOfEffectRadius"] = a.AreaOfEffectRadius
		};

		_abilityVfxDialog.OpenForAbility(a.TemplateID, jsonObj, updatedObj => HandleAbilityUpdate(item.TemplateID, a, updatedObj, wsPath));
	}

	private void HandleAbilityUpdate(string oldTemplateID, AbilityMetadata a, JsonObject updatedObj, string wsPath)
	{
		string newAbilityId = GetNewAbilityId(updatedObj, oldTemplateID);
		bool idChanged = !string.Equals(oldTemplateID, newAbilityId, StringComparison.OrdinalIgnoreCase);
		bool hasOldId = !string.IsNullOrEmpty(oldTemplateID);

		MetadataService.Instance.UpdateMetadata(wsPath, m =>
		{
			if (idChanged && hasOldId)
			{
				m.RemoveAbility(oldTemplateID);
			}
			
			UpdateAbilityMetadata(a, updatedObj, newAbilityId);
			m.AddOrUpdateAbility(a);
		});
		_currentPreviewTemplateID = newAbilityId;
		RefreshObjectList();
	}

	private string GetNewAbilityId(JsonObject updatedObj, string oldTemplateID)
	{
		if (updatedObj.TryGetPropertyValue("TemplateID", out var tNode))
		{
			if (tNode != null && !string.IsNullOrEmpty(tNode.ToString())) return tNode.ToString();
		}
		if (updatedObj.TryGetPropertyValue("AbilityId", out var aNode))
		{
			if (aNode != null && !string.IsNullOrEmpty(aNode.ToString())) return aNode.ToString();
		}
		return oldTemplateID;
	}

	private void UpdateAbilityMetadata(AbilityMetadata a, JsonObject updatedObj, string newAbilityId)
	{
		a.TemplateID = newAbilityId;
		
		if (updatedObj.TryGetPropertyValue("VisualEffect", out var vNode)) a.VisualEffect = vNode?.ToString();
		if (updatedObj.TryGetPropertyValue("CastSound", out var cNode)) a.CastSound = cNode?.ToString();
		if (updatedObj.TryGetPropertyValue("IconPath", out var iNode)) a.IconPath = iNode?.ToString();
		
		if (updatedObj.TryGetPropertyValue("AreaOfEffectRadius", out var rNode) && float.TryParse(rNode?.ToString(), out float radius))
		{
			a.AreaOfEffectRadius = radius;
		}
	}

	private void OpenTerrainEditDialog(ObjectItemInfo item, string wsPath)
	{
		JsonObject curTerData = new JsonObject();
		if (MetadataService.Instance.TryLoadMetadata(wsPath, out var metaTer) && metaTer?.Textures != null)
		{
			TextureMetadata? tNode = FindTextureMetadata(metaTer, item);
			if (tNode != null)
			{
				curTerData = System.Text.Json.JsonSerializer.SerializeToNode(tNode) as JsonObject ?? new JsonObject();
			}
		}
		_textureEditDialog.OpenForTexture(item.TemplateID, curTerData, updatedObj =>
		{
			string newId = updatedObj?["TemplateID"]?.ToString() ?? item.TemplateID;
			_currentPreviewTemplateID = newId;
			RefreshObjectList();
		});
	}

	private TextureMetadata? FindTextureMetadata(MapMetadata metaTer, ObjectItemInfo item)
	{
		if (metaTer.Textures.TryGetValue(item.TemplateID, out var tNode)) return tNode;
		var (_, slug) = TemplateIDHelper.ParseTemplateID(item.TemplateID);
		if (!string.IsNullOrEmpty(slug) && metaTer.Textures.TryGetValue(slug, out tNode)) return tNode;
		if (!string.IsNullOrEmpty(item.ModelPath) && metaTer.Textures.TryGetValue(item.ModelPath, out tNode)) return tNode;
		if (!string.IsNullOrEmpty(slug) && metaTer.Textures.TryGetValue($"{slug}.rtex", out tNode)) return tNode;
		if (!string.IsNullOrEmpty(slug))
		{
			var matchKvp = metaTer.Textures.FirstOrDefault(k => string.Equals(Path.GetFileNameWithoutExtension(k.Key), slug, StringComparison.OrdinalIgnoreCase));
			return matchKvp.Value;
		}
		return null;
	}

	private void OpenSpritesheetEditDialog(ObjectItemInfo item, string wsPath)
	{
		int initCols = 1, initRows = 1;
		float initFps = 20.0f;
		bool initBlend = true;
		string initRtex = "";

		if (MetadataService.Instance.TryLoadMetadata(wsPath, out var metaSpr) && metaSpr?.VfxSpritesheets != null)
		{
			VfxMetadata? sNode = FindSpritesheetMetadata(metaSpr, item);
			if (sNode != null)
			{
				initCols = sNode.Columns > 0 ? sNode.Columns : 1;
				initRows = sNode.Rows > 0 ? sNode.Rows : 1;
				initFps = sNode.Fps > 0 ? sNode.Fps : 20.0f;
				initBlend = sNode.SubframeBlend;
				initRtex = sNode.TexturePath ?? "";
			}
		}

		_spritesheetEditDialog.OpenForSheet(item.TemplateID, initRtex, initCols, initRows, initFps, initBlend, (newId, rtex, cols, rows, fps, blend) =>
		{
			UpdateSpritesheetMetadata(wsPath, item, newId, rtex, cols, rows, fps, blend);
			_currentPreviewTemplateID = newId;
			RefreshObjectList();
		});
	}

	private VfxMetadata? FindSpritesheetMetadata(MapMetadata metaSpr, ObjectItemInfo item)
	{
		if (metaSpr.VfxSpritesheets.TryGetValue(item.TemplateID, out var sNode)) return sNode;
		var (_, slug) = TemplateIDHelper.ParseTemplateID(item.TemplateID);
		if (!string.IsNullOrEmpty(slug) && metaSpr.VfxSpritesheets.TryGetValue(slug, out sNode)) return sNode;
		if (!string.IsNullOrEmpty(item.ModelPath) && metaSpr.VfxSpritesheets.TryGetValue(item.ModelPath, out sNode)) return sNode;
		if (!string.IsNullOrEmpty(slug) && metaSpr.VfxSpritesheets.TryGetValue($"{slug}.rtex", out sNode)) return sNode;
		if (!string.IsNullOrEmpty(slug))
		{
			var matchKvp = metaSpr.VfxSpritesheets.FirstOrDefault(k => string.Equals(Path.GetFileNameWithoutExtension(k.Key), slug, StringComparison.OrdinalIgnoreCase));
			return matchKvp.Value;
		}
		return null;
	}

	private void UpdateSpritesheetMetadata(string wsPath, ObjectItemInfo item, string newId, string rtex, int cols, int rows, float fps, bool blend)
	{
		MetadataService.Instance.UpdateMetadata(wsPath, m =>
		{
			m.VfxSpritesheets ??= new(StringComparer.OrdinalIgnoreCase);
			if (!string.Equals(item.TemplateID, newId, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(item.TemplateID))
			{
				RemoveOldSpritesheetKeys(m, item);
			}
			m.VfxSpritesheets[newId] = new VfxMetadata
			{
				Columns = cols,
				Rows = rows,
				Fps = fps,
				SubframeBlend = blend,
				TexturePath = rtex
			};
		});
	}

	private void RemoveOldSpritesheetKeys(MapMetadata m, ObjectItemInfo item)
	{
		var (_, oldSlug) = TemplateIDHelper.ParseTemplateID(item.TemplateID);
		var keysToRemove = m.VfxSpritesheets.Keys.Where(k =>
			string.Equals(k, item.TemplateID, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(k, oldSlug, StringComparison.OrdinalIgnoreCase) ||
			(!string.IsNullOrEmpty(item.ModelPath) && string.Equals(k, item.ModelPath, StringComparison.OrdinalIgnoreCase)) ||
			(!string.IsNullOrEmpty(oldSlug) && (string.Equals(k, $"{oldSlug}.rtex", StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileNameWithoutExtension(k), oldSlug, StringComparison.OrdinalIgnoreCase))) ||
			string.Equals(TemplateIDHelper.NormalizeTemplateID("spritesheet", k), item.TemplateID, StringComparison.OrdinalIgnoreCase)
		).ToList();
		foreach (var k in keysToRemove) m.VfxSpritesheets.Remove(k);
	}

	private JsonObject TryGetDecalData(string wsPath, string templateID)
	{
		if (!MetadataService.Instance.TryLoadMetadata(wsPath, out var metaDec) || metaDec?.Decals == null)
		{
			return new JsonObject();
		}

		if (metaDec.Decals.TryGetValue(templateID, out var dNode) && dNode != null)
		{
			return SerializeDecalNode(dNode);
		}
		
		var (_, slug) = TemplateIDHelper.ParseTemplateID(templateID);
		if (!string.IsNullOrEmpty(slug) && metaDec.Decals.TryGetValue(slug, out var sNode) && sNode != null)
		{
			return SerializeDecalNode(sNode);
		}
		
		return new JsonObject();
	}

	private JsonObject SerializeDecalNode(object dNode)
	{
		var node = System.Text.Json.JsonSerializer.SerializeToNode(dNode);
		if (node is JsonObject jObj) return jObj;
		return new JsonObject();
	}

	private string GetDecalIdFromUpdatedObj(JsonObject updatedObj, string fallbackId)
	{
		if (updatedObj.TryGetPropertyValue("decal_id", out var idNode) && !string.IsNullOrWhiteSpace(idNode?.ToString()))
		{
			return idNode.ToString().Trim();
		}
		
		if (updatedObj.TryGetPropertyValue("DecalId", out var idNode2) && !string.IsNullOrWhiteSpace(idNode2?.ToString()))
		{
			return idNode2.ToString().Trim();
		}
		
		return fallbackId;
	}

	private void OpenDecalEditDialog(ObjectItemInfo item, string wsPath)
	{
		JsonObject curDecData = TryGetDecalData(wsPath, item.TemplateID);
		_decalEditDialog.OpenForDecal(item.TemplateID, curDecData, updatedObj =>
		{
			string newDecalId = GetDecalIdFromUpdatedObj(updatedObj, item.TemplateID);

			var serializerOptions = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
			var deserializedDecal = System.Text.Json.JsonSerializer.Deserialize<DecalMetadata>(updatedObj.ToJsonString(), serializerOptions) ?? new DecalMetadata();

			MetadataService.Instance.UpdateMetadata(wsPath, m =>
			{
				m.Decals ??= new(StringComparer.OrdinalIgnoreCase);
				bool idChanged = !string.Equals(item.TemplateID, newDecalId, StringComparison.OrdinalIgnoreCase);
				
				if (idChanged)
				{
					m.Decals.Remove(item.TemplateID);
					var (_, oldSlug) = TemplateIDHelper.ParseTemplateID(item.TemplateID);
					if (!string.IsNullOrEmpty(oldSlug))
					{
						m.Decals.Remove(oldSlug);
					}
				}
				m.Decals[newDecalId] = deserializedDecal;
			});
			_currentPreviewTemplateID = newDecalId;
			RefreshObjectList();
		});
	}

	private void OpenShaderEditDialog(ObjectItemInfo item, string wsPath)
	{
		_shaderEditDialog.OpenForShader(item.TemplateID, updatedConfig =>
		{
			MetadataService.Instance.UpdateMetadata(wsPath, m =>
			{
				m.SpawnShaders ??= new(StringComparer.OrdinalIgnoreCase);
				if (!string.Equals(item.TemplateID, updatedConfig.Key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(item.TemplateID))
				{
					m.SpawnShaders.Remove(item.TemplateID);
					var (_, oldSlug) = TemplateIDHelper.ParseTemplateID(item.TemplateID);
					if (!string.IsNullOrEmpty(oldSlug))
					{
						m.SpawnShaders.Remove(oldSlug);
					}
				}
				m.SpawnShaders[updatedConfig.Key] = new SpawnShaderMetadata
				{
					Name = updatedConfig.Name,
					TransitionMode = updatedConfig.TransitionMode,
					Direction = updatedConfig.Direction,
					EdgeColor = "#" + updatedConfig.EdgeColor.ToHtml(true),
					EdgeWidth = updatedConfig.EdgeWidth,
					EdgeEmission = updatedConfig.EdgeEmission,
					NoiseScale = updatedConfig.NoiseScale,
					NoiseRoughness = updatedConfig.NoiseRoughness,
					FresnelPower = updatedConfig.FresnelPower,
					VertexDisplacement = updatedConfig.VertexDisplacement,
					AlphaFade = updatedConfig.AlphaFade,
					Duration = updatedConfig.Duration,
					AssetType = !string.IsNullOrWhiteSpace(updatedConfig.AssetType) ? updatedConfig.AssetType : "SpawnShader"
				};
			});
			_currentPreviewTemplateID = updatedConfig.Key;
			RefreshObjectList();
		});
	}

	private void OpenVfxEditDialog(ObjectItemInfo item, string wsPath)
	{
		if (MetadataService.Instance.TryLoadMetadata(wsPath, out var metaVfx) && metaVfx?.Templates?.Vfx != null)
		{
			var vfxConfig = metaVfx.Templates.Vfx.FirstOrDefault(x => string.Equals(x.VfxId, item.TemplateID, StringComparison.OrdinalIgnoreCase));
			if (vfxConfig != null)
			{
				_vfxStudioDialog.OpenForConfig(vfxConfig, updatedCfg =>
				{
					MetadataService.Instance.UpdateMetadata(wsPath, m =>
					{
						if (!string.Equals(item.TemplateID, updatedCfg.VfxId, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(item.TemplateID))
						{
							m.RemoveVfx(item.TemplateID);
						}
						m.AddOrUpdateVfx(updatedCfg);
					});
					_currentPreviewTemplateID = updatedCfg.VfxId;
					RefreshObjectList();
				});
			}
		}
	}

	private void OpenDefaultEditDialog(ObjectItemInfo item)
	{
		if (item.Category == "upgrades" || item.Category == "items")
		{
			_itemUpgradeEditDialog.OpenForObject(item.Category, item.TemplateID, (oldId, newId) =>
			{
				_currentPreviewTemplateID = newId;
				RefreshObjectList();
			});
		}
		else
		{
			_entityVisualEditDialog.OpenForObject(item.Category, item.TemplateID, (oldId, newId) =>
			{
				_currentPreviewTemplateID = newId;
				RefreshObjectList();
			});
		}
	}


	private void AddNewObjectForCategory(string category)
	{
		string wsPath = Services.MapWorkspaceService.GetActiveWorkspacePath();
		string objectType = GetObjectTypeFromCategory(category);
		var existingIDs = GetExistingObjectIDs(wsPath, category);

		string newTemplateID = TemplateIDHelper.GenerateTemplateID(objectType, $"new_{objectType}", existingIDs);
		var (parsedType, parsedSlug) = TemplateIDHelper.ParseTemplateID(newTemplateID);

		MetadataService.Instance.UpdateMetadata(wsPath, m =>
		{
			AddMetadataForCategory(m, category, newTemplateID, parsedSlug);
		});

		HandlePostAddObject(category, newTemplateID, parsedSlug);
	}

	private static readonly Dictionary<string, string> ObjectTypeMap = new(StringComparer.OrdinalIgnoreCase)
	{
		{ "units", "unit" },
		{ "buildings", "building" },
		{ "resources", "resource" },
		{ "props", "prop" },
		{ "weapons", "weapon" },
		{ "abilities", "ability" },
		{ "upgrades", "upgrade" },
		{ "items", "item" },
		{ "terrain", "terrain" },
		{ "spritesheets", "spritesheet" },
		{ "decals", "decal" },
		{ "shaders", "SpawnShader" },
		{ "shader", "SpawnShader" },
		{ "spawnshader", "SpawnShader" },
		{ "spawnshaders", "SpawnShader" },
		{ "vfx", "vfx" }
	};

	private string GetObjectTypeFromCategory(string category)
	{
		return ObjectTypeMap.TryGetValue(category, out var type) ? type : "unit";
	}

	private HashSet<string> GetExistingObjectIDs(string wsPath, string category)
	{
		var existingIDs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (MetadataService.Instance.TryLoadMetadata(wsPath, out var meta) && meta != null)
		{
			var currentList = GetObjectsForCategory(category);
			foreach (var obj in currentList) existingIDs.Add(obj.TemplateID);
		}
		return existingIDs;
	}

	private static readonly Dictionary<string, Action<TemplateManagerDialog, MapMetadata, string, string>> AddMetadataHandlers = new(StringComparer.OrdinalIgnoreCase)
	{
		{ "units", (dialog, m, id, slug) => dialog.AddUnitMetadata(m, id, slug) },
		{ "buildings", (dialog, m, id, slug) => dialog.AddBuildingMetadata(m, id, slug) },
		{ "resources", (dialog, m, id, slug) => dialog.AddResourceMetadata(m, id, slug) },
		{ "props", (dialog, m, id, slug) => dialog.AddPropMetadata(m, id, slug) },
		{ "weapons", (dialog, m, id, slug) => dialog.AddWeaponMetadata(m, id, slug) },
		{ "abilities", (dialog, m, id, slug) => dialog.AddAbilityMetadata(m, id, slug) },
		{ "upgrades", (dialog, m, id, slug) => dialog.AddUpgradeMetadata(m, id, slug) },
		{ "items", (dialog, m, id, slug) => dialog.AddItemMetadata(m, id, slug) },
		{ "terrain", (dialog, m, id, slug) => dialog.AddTerrainMetadata(m, id, slug) },
		{ "spritesheets", (dialog, m, id, slug) => dialog.AddSpritesheetMetadata(m, id, slug) },
		{ "decals", (dialog, m, id, slug) => dialog.AddDecalMetadata(m, id, slug) },
		{ "shaders", (dialog, m, id, slug) => dialog.AddShaderMetadata(m, id, slug) },
		{ "shader", (dialog, m, id, slug) => dialog.AddShaderMetadata(m, id, slug) },
		{ "spawnshader", (dialog, m, id, slug) => dialog.AddShaderMetadata(m, id, slug) },
		{ "spawnshaders", (dialog, m, id, slug) => dialog.AddShaderMetadata(m, id, slug) },
		{ "vfx", (dialog, m, id, slug) => dialog.AddVfxMetadata(m, id, slug) }
	};

	private void AddMetadataForCategory(MapMetadata m, string category, string newTemplateID, string parsedSlug)
	{
		if (AddMetadataHandlers.TryGetValue(category, out var handler))
		{
			handler(this, m, newTemplateID, parsedSlug);
		}
	}

	private void AddUnitMetadata(MapMetadata m, string id, string slug) => m.AddOrUpdateUnit(new UnitMetadata { TemplateID = id, Name = slug, Description = "A new unit entity.", Scale = 1.0f, MaxHp = 100, Damage = 10, Range = 2.0f, Armor = 0, Speed = 5.0f, AttackCooldown = 1.5f, CostGold = 100, ProductionTime = 10.0f, PopCost = 1, AttackType = "melee", PathingType = 8 });
	private void AddBuildingMetadata(MapMetadata m, string id, string slug) => m.AddOrUpdateBuilding(new UnitMetadata { TemplateID = id, Name = slug, Description = "A new building entity.", Scale = 1.5f, MaxHp = 1000, Armor = 10, CostGold = 200, CostWood = 100, ProductionTime = 15.0f, AttackType = "none", PathingType = 32 });
	private void AddResourceMetadata(MapMetadata m, string id, string slug) => m.AddOrUpdateResource(new ResourceMetadata { TemplateID = id, Name = slug, Description = "Harvestable resource deposit.", Scale = 1.0f, MaxCapacity = 2000.0f, HarvestRate = 10.0f, MaxWorkers = 5, PathingType = 255 });
	private void AddPropMetadata(MapMetadata m, string id, string slug) => m.AddOrUpdateProp(new PropMetadata { TemplateID = id, Name = slug, Description = "Decorative prop.", Scale = 1.0f, PathingType = 255 });
	private void AddWeaponMetadata(MapMetadata m, string id, string slug) => m.AddOrUpdateWeapon(new WeaponMetadata { TemplateID = id, Name = slug, Damage = 10, Range = 8.0f, AttackCooldown = 1.5f, AttackType = "ranged", ProjectileSpeed = 25.0f, ArcHeight = 2.0f, OrientToTrajectory = true });
	private void AddAbilityMetadata(MapMetadata m, string id, string slug) => m.AddOrUpdateAbility(new AbilityMetadata { TemplateID = id, Name = slug, Description = "A new ability.", AbilityType = "target_spell", Cooldown = 10, AreaOfEffectRadius = 4.0f });
	private void AddUpgradeMetadata(MapMetadata m, string id, string slug) => m.AddOrUpdateUpgrade(new UpgradeMetadata { TemplateID = id, Name = slug, Description = "A new upgrade." });
	private void AddItemMetadata(MapMetadata m, string id, string slug) => m.AddOrUpdateItem(new ItemMetadata { TemplateID = id, Name = slug, Description = "A new item.", ItemClass = "consumable" });
	
	private void AddTerrainMetadata(MapMetadata m, string id, string slug)
	{
		m.Textures ??= new(StringComparer.OrdinalIgnoreCase);
		m.Textures[id] = new TextureMetadata { TileMode = "Stochastic", UvScale = 1.0f, Brightness = 1.0f, TexturePath = $"{slug}.rtex", SwatchIndex = GetNextTerrainSwatchSlot(m), DefaultPathingCode = Realm.Client.EditableTerrain.PATHING_GROUND | Realm.Client.EditableTerrain.PATHING_BUILDABLE | Realm.Client.EditableTerrain.PATHING_FLYING };
	}
	
	private void AddSpritesheetMetadata(MapMetadata m, string id, string slug)
	{
		m.VfxSpritesheets ??= new(StringComparer.OrdinalIgnoreCase);
		m.VfxSpritesheets[id] = new VfxMetadata { Columns = 4, Rows = 4, Fps = 20.0f, SubframeBlend = true };
	}
	
	private void AddDecalMetadata(MapMetadata m, string id, string slug)
	{
		m.Decals ??= new(StringComparer.OrdinalIgnoreCase);
		m.Decals[id] = new DecalMetadata();
	}
	
	private void AddShaderMetadata(MapMetadata m, string id, string slug)
	{
		m.SpawnShaders ??= new(StringComparer.OrdinalIgnoreCase);
		m.SpawnShaders[id] = new SpawnShaderMetadata { Name = slug, TransitionMode = 0, Direction = 0, EdgeColor = "#00e5ffff", EdgeWidth = 0.06f, EdgeEmission = 6.0f, NoiseScale = 12.0f, NoiseRoughness = 0.4f, FresnelPower = 3.0f, VertexDisplacement = 0.0f, AlphaFade = 0.9f, Duration = 1.2f, AssetType = "SpawnShader" };
	}
	
	private void AddVfxMetadata(MapMetadata m, string id, string slug) => m.AddOrUpdateVfx(new VfxAttachmentConfig { VfxId = id, Name = slug });

	private int GetNextTerrainSwatchSlot(MapMetadata m)
	{
		var occupiedSlots = new bool[TextureSwatchSlots.MaxSlots];
		foreach (var existingTex in m.Textures.Values)
		{
			if (existingTex != null && existingTex.SwatchIndex >= 0 && existingTex.SwatchIndex < TextureSwatchSlots.MaxSlots)
			{
				occupiedSlots[existingTex.SwatchIndex] = true;
			}
		}
		int nextFreeSlot = TextureSwatchSlots.FirstFreeSlot(occupiedSlots);
		return nextFreeSlot < 0 ? 0 : nextFreeSlot;
	}

	private void HandlePostAddObject(string category, string newTemplateID, string parsedSlug)
	{
		if (category == "terrain")
		{
			Hud?.SetupTextureSwatches(false);
			if (Realm.Client.Core.GameHost.Instance?.GroundTerrain != null)
			{
				Realm.Client.Core.GameHost.Instance.GroundTerrain.ReloadTerrainTextures(true);
			}
		}

		RefreshObjectList();
		OpenEditDialogForObject(new ObjectItemInfo
		{
			Category = category,
			TemplateID = newTemplateID,
			Name = parsedSlug,
			Description = ""
		});
	}


	private static readonly Dictionary<string, Action<TemplateManagerDialog, MapMetadata, ObjectItemInfo, string>> DeleteMetadataHandlers = new(StringComparer.OrdinalIgnoreCase)
	{
		{ "units", (dialog, meta, item, delSlug) => { meta.RemoveUnit(item.TemplateID); if (!string.IsNullOrEmpty(delSlug)) meta.RemoveUnit(delSlug); } },
		{ "buildings", (dialog, meta, item, delSlug) => { meta.RemoveBuilding(item.TemplateID); if (!string.IsNullOrEmpty(delSlug)) meta.RemoveBuilding(delSlug); } },
		{ "resources", (dialog, meta, item, delSlug) => { meta.RemoveResource(item.TemplateID); if (!string.IsNullOrEmpty(delSlug)) meta.RemoveResource(delSlug); } },
		{ "props", (dialog, meta, item, delSlug) => { meta.RemoveProp(item.TemplateID); if (!string.IsNullOrEmpty(delSlug)) meta.RemoveProp(delSlug); } },
		{ "weapons", (dialog, meta, item, delSlug) => { meta.RemoveWeapon(item.TemplateID); if (!string.IsNullOrEmpty(delSlug)) meta.RemoveWeapon(delSlug); } },
		{ "abilities", (dialog, meta, item, delSlug) => { meta.RemoveAbility(item.TemplateID); if (!string.IsNullOrEmpty(delSlug)) meta.RemoveAbility(delSlug); } },
		{ "upgrades", (dialog, meta, item, delSlug) => { meta.RemoveUpgrade(item.TemplateID); if (!string.IsNullOrEmpty(delSlug)) meta.RemoveUpgrade(delSlug); } },
		{ "items", (dialog, meta, item, delSlug) => { meta.RemoveItem(item.TemplateID); if (!string.IsNullOrEmpty(delSlug)) meta.RemoveItem(delSlug); } },
		{ "terrain", (dialog, meta, item, delSlug) => dialog.RemoveTerrainTexture(meta, item, delSlug) },
		{ "spritesheets", (dialog, meta, item, delSlug) => dialog.RemoveSpritesheet(meta, item, delSlug) },
		{ "decals", (dialog, meta, item, delSlug) => dialog.RemoveDecal(meta, item, delSlug) },
		{ "shaders", (dialog, meta, item, delSlug) => dialog.RemoveSpawnShader(meta, item, delSlug) },
		{ "shader", (dialog, meta, item, delSlug) => dialog.RemoveSpawnShader(meta, item, delSlug) },
		{ "spawnshader", (dialog, meta, item, delSlug) => dialog.RemoveSpawnShader(meta, item, delSlug) },
		{ "spawnshaders", (dialog, meta, item, delSlug) => dialog.RemoveSpawnShader(meta, item, delSlug) },
		{ "vfx", (dialog, meta, item, delSlug) => { meta.RemoveVfx(item.TemplateID); if (!string.IsNullOrEmpty(delSlug)) meta.RemoveVfx(delSlug); } }
	};

	private void DeleteObject(ObjectItemInfo item)
	{
		string wsPath = Services.MapWorkspaceService.GetActiveWorkspacePath();
		var references = CheckObjectReferences(wsPath, item);

		string message = references.Count > 0
			? string.Format(TranslationServer.Translate("Object '{0}' is referenced:\n- {1}\n\nAre you sure you want to delete it?"), item.TemplateID, string.Join("\n- ", references))
			: string.Format(TranslationServer.Translate("Are you sure you want to delete object '{0}' from metadata.json?"), item.TemplateID);

		Hud?.ShowConfirmationDialog(message, () =>
		{
			MetadataService.Instance.UpdateMetadata(wsPath, meta =>
			{
				var (_, delSlug) = TemplateIDHelper.ParseTemplateID(item.TemplateID);
				if (DeleteMetadataHandlers.TryGetValue(item.Category, out var handler))
				{
					handler(this, meta, item, delSlug);
				}
			});

			if (item.Category == "terrain")
			{
				Hud?.SetupTextureSwatches(false);
				if (Realm.Client.Core.GameHost.Instance?.GroundTerrain != null)
				{
					Realm.Client.Core.GameHost.Instance.GroundTerrain.ReloadTerrainTextures(true);
				}
			}

			RefreshObjectList();
		});
	}
	private List<string> CheckObjectReferences(string wsPath, ObjectItemInfo item)
	{
		var references = new List<string>();
		string terrainPath = Path.Combine(wsPath, "terrain.json");
		if (File.Exists(terrainPath))
		{
			try
			{
				string terrainJson = File.ReadAllText(terrainPath);
				if (terrainJson.Contains(item.TemplateID, StringComparison.OrdinalIgnoreCase))
				{
					references.Add($"Placed instances found on map terrain (terrain.json)");
				}
			}
			catch { }
		}
		return references;
	}

	private void RemoveTerrainTexture(MapMetadata meta, ObjectItemInfo item, string delSlug)
	{
		if (meta.Textures == null) return;
		var keysToRemove = meta.Textures.Keys.Where(k =>
			string.Equals(k, item.TemplateID, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(k, delSlug, StringComparison.OrdinalIgnoreCase) ||
			(!string.IsNullOrEmpty(item.ModelPath) && string.Equals(k, item.ModelPath, StringComparison.OrdinalIgnoreCase)) ||
			(!string.IsNullOrEmpty(delSlug) && (string.Equals(k, $"{delSlug}.rtex", StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileNameWithoutExtension(k), delSlug, StringComparison.OrdinalIgnoreCase))) ||
			string.Equals(TemplateIDHelper.NormalizeTemplateID("terrain", k), item.TemplateID, StringComparison.OrdinalIgnoreCase)
		).ToList();

		foreach (var k in keysToRemove)
		{
			meta.Textures.Remove(k);
		}
	}

	private void RemoveSpritesheet(MapMetadata meta, ObjectItemInfo item, string delSlug)
	{
		if (meta.VfxSpritesheets == null) return;
		var keysToRemove = meta.VfxSpritesheets.Keys.Where(k =>
			string.Equals(k, item.TemplateID, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(k, delSlug, StringComparison.OrdinalIgnoreCase) ||
			(!string.IsNullOrEmpty(item.ModelPath) && string.Equals(k, item.ModelPath, StringComparison.OrdinalIgnoreCase)) ||
			(!string.IsNullOrEmpty(delSlug) && (string.Equals(k, $"{delSlug}.rtex", StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileNameWithoutExtension(k), delSlug, StringComparison.OrdinalIgnoreCase))) ||
			string.Equals(TemplateIDHelper.NormalizeTemplateID("spritesheet", k), item.TemplateID, StringComparison.OrdinalIgnoreCase)
		).ToList();

		foreach (var k in keysToRemove)
		{
			meta.VfxSpritesheets.Remove(k);
		}
	}

	private void RemoveDecal(MapMetadata meta, ObjectItemInfo item, string delSlug)
	{
		if (meta.Decals == null) return;
		var keysToRemove = meta.Decals.Keys.Where(k =>
			string.Equals(k, item.TemplateID, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(k, delSlug, StringComparison.OrdinalIgnoreCase) ||
			(!string.IsNullOrEmpty(item.ModelPath) && string.Equals(k, item.ModelPath, StringComparison.OrdinalIgnoreCase)) ||
			(!string.IsNullOrEmpty(delSlug) && (string.Equals(k, $"{delSlug}.rtex", StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileNameWithoutExtension(k), delSlug, StringComparison.OrdinalIgnoreCase))) ||
			string.Equals(TemplateIDHelper.NormalizeTemplateID("decal", k), item.TemplateID, StringComparison.OrdinalIgnoreCase)
		).ToList();

		foreach (var k in keysToRemove)
		{
			meta.Decals.Remove(k);
		}
	}

	private void RemoveSpawnShader(MapMetadata meta, ObjectItemInfo item, string delSlug)
	{
		if (meta.SpawnShaders == null) return;
		var keysToRemove = meta.SpawnShaders.Keys.Where(k =>
			string.Equals(k, item.TemplateID, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(k, delSlug, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(TemplateIDHelper.NormalizeTemplateID("SpawnShader", k), item.TemplateID, StringComparison.OrdinalIgnoreCase)
		).ToList();

		foreach (var k in keysToRemove)
		{
			meta.SpawnShaders.Remove(k);
		}
	}


	private void LoadPreviewForObject(ObjectItemInfo item)
	{
		_currentPreviewTemplateID = item.TemplateID;
		_currentShaderConfig = null;
		ClearPreviewProjectile();
		ClearPreviewNodes();

		string cat = item.Category?.ToLowerInvariant() ?? "";
		switch (cat)
		{
			case "terrain": LoadTerrainPreview(item); break;
			case "decals": LoadDecalPreview(item); break;
			case "upgrades" or "items": LoadItemUpgradePreview(item); break;
			case "spritesheets": LoadSpritesheetPreview(item); break;
			case "shaders" or "shader": LoadShaderPreview(item); break;
			case "weapons": LoadWeaponPreview(item); break;
			case "vfx": LoadVfxPreview(item); break;
			default:
				if (!string.IsNullOrEmpty(item.ModelPath)) LoadModelPreview(item);
				else if (!string.IsNullOrEmpty(item.IconPath)) LoadIconPreview(item);
				else ShowDefaultPreview();
				break;
		}
	}

	private void ClearPreviewNodes()
	{
		if (_currentModelRoot != null && GodotObject.IsInstanceValid(_currentModelRoot))
		{
			foreach (Node child in _currentModelRoot.GetChildren()) child.QueueFree();
		}

		if (_vfxSprite != null && GodotObject.IsInstanceValid(_vfxSprite))
		{
			_vfxSprite.Visible = false;
			_vfxSprite.Stop();
		}

		if (_tooltipPreviewContainer != null)
		{
			foreach (Node child in _tooltipPreviewContainer.GetChildren()) child.QueueFree();
			_tooltipPreviewContainer.Visible = false;
		}
	}

	private void ShowDefaultPreview()
	{
		_preview2DContainer.Visible = false;
		_previewAudioContainer.Visible = false;
		PreviewSubViewport.GetParent<Control>().Visible = true;
	}

	private void LoadTerrainPreview(ObjectItemInfo item)
	{
		PreviewSubViewport.GetParent<Control>().Visible = false;
		_previewAudioContainer.Visible = false;
		_preview2DContainer.Visible = true;

		_preview2DImage.CustomMinimumSize = new Vector2(140, 140);
		string texPath = !string.IsNullOrEmpty(item.ModelPath) ? item.ModelPath : item.TemplateID;
		_preview2DImage.Texture = LoadTexture2D(texPath, "textures");
		_lblPreview2DInfo.Text = item.Name;
	}

	private void LoadDecalPreview(ObjectItemInfo item)
	{
		PreviewSubViewport.GetParent<Control>().Visible = false;
		_previewAudioContainer.Visible = false;
		_preview2DContainer.Visible = true;

		_preview2DImage.CustomMinimumSize = new Vector2(140, 140);
		Texture2D? decalTex = Realm.Client.Core.GameHost.Instance?.LoadDecalTexture(item.TemplateID) ?? LoadTexture2D(item.ModelPath, "decals");
		_preview2DImage.Texture = decalTex;
		_lblPreview2DInfo.Text = item.Name;
	}

	private void LoadItemUpgradePreview(ObjectItemInfo item)
	{
		PreviewSubViewport.GetParent<Control>().Visible = false;
		_previewAudioContainer.Visible = false;
		_preview2DContainer.Visible = true;

		string tooltipText = !string.IsNullOrWhiteSpace(item.Description)
			? item.Description
			: $"<b>{item.Name}</b>\n<color=#888888>{item.TemplateID}</color>";

		Control tooltipWidget = RichTooltip.Create(tooltipText);
		_tooltipPreviewContainer.AddChild(tooltipWidget);
		_tooltipPreviewContainer.Visible = true;

		_preview2DImage.CustomMinimumSize = new Vector2(56, 56);
		string iconPath = !string.IsNullOrEmpty(item.IconPath) ? item.IconPath : item.ModelPath;
		_preview2DImage.Texture = LoadTexture2D(iconPath, "icons");
		_lblPreview2DInfo.Text = item.Name;
	}

	private bool TryGetSpritesheetMeta(string wsPath, string templateID, out VfxMetadata? sheetMeta)
	{
		sheetMeta = null;
		if (MetadataService.Instance.TryLoadMetadata(wsPath, out var meta) && meta?.VfxSpritesheets != null)
		{
			if (meta.VfxSpritesheets.TryGetValue(templateID, out var sm) && sm != null)
			{
				sheetMeta = sm;
				return true;
			}
		}
		return false;
	}

	private void ResolveSpritesheetMeta(string wsPath, ObjectItemInfo item, out int columns, out int rows, out float fps, out string spritePath)
	{
		columns = 4;
		rows = 4;
		fps = 20.0f;
		spritePath = item.TemplateID;

		if (!string.IsNullOrEmpty(item.ModelPath))
		{
			spritePath = item.ModelPath;
		}

		if (!TryGetSpritesheetMeta(wsPath, item.TemplateID, out var sheetMeta)) return;
		if (sheetMeta == null) return;

		if (sheetMeta.Columns > 0) columns = sheetMeta.Columns;
		if (sheetMeta.Rows > 0) rows = sheetMeta.Rows;
		if (sheetMeta.Fps > 0) fps = sheetMeta.Fps;

		if (!string.IsNullOrEmpty(sheetMeta.TexturePath))
		{
			spritePath = sheetMeta.TexturePath;
		}
	}

	private void LoadSpritesheetPreview(ObjectItemInfo item)
	{
		_preview2DContainer.Visible = false;
		_previewAudioContainer.Visible = false;
		PreviewSubViewport.GetParent<Control>().Visible = true;

		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		
		ResolveSpritesheetMeta(wsPath, item, out int columns, out int rows, out float fps, out string spritePath);

		Texture2D? sheetTex = LoadTexture2D(spritePath, "vfx");
		
		if (sheetTex == null) return;
		if (_vfxSprite == null) return;
		if (!GodotObject.IsInstanceValid(_vfxSprite)) return;

		int frameWidth = sheetTex.GetWidth() / columns;
		int frameHeight = sheetTex.GetHeight() / rows;

		if (frameWidth <= 0) return;
		if (frameHeight <= 0) return;

		ApplySpritesheetFrames(sheetTex, columns, rows, fps, frameWidth, frameHeight);
	}

	private void ApplySpritesheetFrames(Texture2D sheetTex, int columns, int rows, float fps, int frameWidth, int frameHeight)
	{
		int totalFrames = columns * rows;
		var frames = new SpriteFrames();
		frames.AddAnimation("play");
		frames.SetAnimationLoopMode("play", SpriteFrames.LoopMode.Linear);
		frames.SetAnimationSpeed("play", fps);

		for (int frameIndex = 0; frameIndex < totalFrames; frameIndex++)
		{
			int c = frameIndex % columns;
			int r = frameIndex / columns;
			var atlasFrame = new AtlasTexture();
			atlasFrame.Atlas = sheetTex;
			atlasFrame.Region = new Rect2(c * frameWidth, r * frameHeight, frameWidth, frameHeight);
			frames.AddFrame("play", atlasFrame);
		}

		_vfxSprite.SpriteFrames = frames;
		_vfxSprite.Animation = "play";
		_vfxSprite.Position = Vector3.Zero;
		_vfxSprite.PixelSize = 2.0f / Math.Max(frameWidth, frameHeight);
		_vfxSprite.Visible = true;
		_vfxSprite.Play("play");
	}
	private void LoadShaderPreview(ObjectItemInfo item)
	{
		_preview2DContainer.Visible = false;
		_previewAudioContainer.Visible = false;
		PreviewSubViewport.GetParent<Control>().Visible = true;

		string meshKey = !string.IsNullOrEmpty(_selectedPreviewMesh) ? _selectedPreviewMesh : (_availablePreviewMeshes.Count > 0 ? _availablePreviewMeshes[0] : "");
		Node3D? loadedNode3D = GetShaderPreviewMesh(meshKey);

		if (loadedNode3D != null)
		{
			_currentModelRoot.AddChild(loadedNode3D);
			Realm.Client.Animation.AnimationRetargetingService.TryApplyRiggedIdlePose(loadedNode3D, meshKey);

			_currentShaderConfig = SpawnDeathShaderManager.GetShaderConfig(item.TemplateID);
			_shaderAnimTime = 0.0f;
			_shaderAnimForward = true;
			if (_currentShaderConfig != null)
			{
				SpawnDeathShaderManager.ApplyShaderPreview(loadedNode3D, _currentShaderConfig, 0.0f);
			}

			CenterAndFrameNode(loadedNode3D);
		}
	}

	private Node3D GetFallbackMesh(string meshKey)
	{
		var meshInst = new MeshInstance3D();
		if (meshKey.Contains("Capsule"))
		{
			meshInst.Mesh = new CapsuleMesh { Radius = 0.5f, Height = 1.8f };
			meshInst.Position = new Vector3(0, 0.9f, 0);
		}
		else
		{
			meshInst.Mesh = new BoxMesh { Size = new Vector3(2f, 2f, 2f) };
			meshInst.Position = new Vector3(0, 1.0f, 0);
		}
		meshInst.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.7f, 0.5f) };
		return meshInst;
	}

	private string? TryFindModelPath(string wsPath, string meshKey)
	{
		string[] subDirs = { "units", "buildings", "resources", "props", "projectiles", "characters", "items", "attachments", "weapons" };
		foreach (var sub in subDirs)
		{
			string p = Path.Combine(wsPath, "Assets", "models", sub, meshKey);
			if (File.Exists(p)) return p;
		}

		string modelsDir = Path.Combine(wsPath, "Assets", "models");
		if (Directory.Exists(modelsDir))
		{
			var files = Directory.GetFiles(modelsDir, meshKey, SearchOption.AllDirectories);
			if (files.Length > 0 && files[0].EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
			{
				return files[0];
			}
		}

		return null;
	}

	private Node3D? TryLoadRMeshOrGlb(string modelPath, string meshKey)
	{
		if (string.IsNullOrEmpty(modelPath) || !modelPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) || !File.Exists(modelPath))
			return null;

		var loaded = ModelCache.GetModel(modelPath) ?? ModelCache.GetModel(meshKey);
		if (loaded is Node3D n) return n;

		var gltfDoc = new GltfDocument();
		var gltfState = new GltfState();
		byte[] rmeshBytes = File.ReadAllBytes(modelPath);
		byte[] glbBytes = Realm.Shared.ModelOptimization.RmeshFile.GetGlbBytes(rmeshBytes) ?? rmeshBytes;
		if (gltfDoc.AppendFromBuffer(glbBytes, "", gltfState) == Error.Ok)
		{
			var node = gltfDoc.GenerateScene(gltfState);
			if (node is Node3D n3d) return n3d;
		}

		return null;
	}

	private Node3D? GetShaderPreviewMesh(string meshKey)
	{
		if (meshKey.StartsWith("(")) return GetFallbackMesh(meshKey);
		if (string.IsNullOrEmpty(meshKey)) return null;

		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string? modelPath = TryFindModelPath(wsPath, meshKey);

		return modelPath != null ? TryLoadRMeshOrGlb(modelPath, meshKey) : null;
	}
	private void LoadWeaponPreview(ObjectItemInfo item)
	{
		_preview2DContainer.Visible = false;
		_previewAudioContainer.Visible = false;
		PreviewSubViewport.GetParent<Control>().Visible = true;

		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		WeaponMetadata? weaponMeta = null;
		if (MetadataService.Instance.TryLoadMetadata(wsPath, out var meta) && meta?.Templates?.Weapons != null)
		{
			weaponMeta = meta.Templates.Weapons.FirstOrDefault(w => string.Equals(w.TemplateID, item.TemplateID, StringComparison.OrdinalIgnoreCase));
		}

		weaponMeta ??= new WeaponMetadata
		{
			TemplateID = item.TemplateID,
			Name = item.Name,
			ProjectileModelPath = item.ModelPath
		};

		RestartPreviewWeaponProjectile(weaponMeta);
	}

	private void LoadVfxPreview(ObjectItemInfo item)
	{
		_preview2DContainer.Visible = false;
		_previewAudioContainer.Visible = false;
		PreviewSubViewport.GetParent<Control>().Visible = true;

		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		if (MetadataService.Instance.TryLoadMetadata(wsPath, out var meta) && meta?.Templates?.Vfx != null)
		{
			var vfxConfig = meta.Templates.Vfx.FirstOrDefault(v => string.Equals(v.VfxId, item.TemplateID, StringComparison.OrdinalIgnoreCase));
			if (vfxConfig != null)
			{
				_previewVfxInstance = new ProceduralVfxInstance3D { IsPreview = true };
				_currentModelRoot.AddChild(_previewVfxInstance);
				_previewVfxInstance.Initialize(vfxConfig);
			}
		}
	}

	private void LoadModelPreview(ObjectItemInfo item)
	{
		_preview2DContainer.Visible = false;
		_previewAudioContainer.Visible = false;
		PreviewSubViewport.GetParent<Control>().Visible = true;

		try
		{
			Node mesh = ModelCache.GetModel(item.ModelPath);
			if (mesh != null)
			{
				_currentModelRoot.AddChild(mesh);
				float s = item.Scale > 0 ? item.Scale : 1.0f;
				_currentModelRoot.Scale = new Vector3(s, s, s);
			}
		}
		catch { }
	}

	private void LoadIconPreview(ObjectItemInfo item)
	{
		PreviewSubViewport.GetParent<Control>().Visible = false;
		_previewAudioContainer.Visible = false;
		_preview2DContainer.Visible = true;

		_preview2DImage.CustomMinimumSize = new Vector2(100, 100);
		_preview2DImage.Texture = LoadTexture2D(item.IconPath, "icons");
		_lblPreview2DInfo.Text = item.Name;
	}

	private void RestartPreviewWeaponProjectile(WeaponMetadata weapon)
	{
		ClearPreviewProjectile();
		if (PreviewSubViewport == null) return;

		_previewProjectile = new Realm.Client.VisualProjectile3D();
		PreviewSubViewport.AddChild(_previewProjectile);

		Vector3 startPos = new Vector3(-2.8f, 0.5f, 0f);
		Vector3 targetPos = new Vector3(2.8f, 0.5f, 0f);

		_previewProjectile.Initialize(weapon, startPos, targetPos, default, (proj) =>
		{
			if (Visible && PreviewSubViewport != null && _currentCategory == "weapons" && string.Equals(_currentPreviewTemplateID, weapon.TemplateID, StringComparison.OrdinalIgnoreCase))
			{
				Callable.From(() => RestartPreviewWeaponProjectile(weapon)).CallDeferred();
			}
		});
	}

	private void ClearPreviewProjectile()
	{
		if (_previewProjectile != null && GodotObject.IsInstanceValid(_previewProjectile))
		{
			_previewProjectile.QueueFree();
			_previewProjectile = null;
		}
	}

	private void CenterAndFrameNode(Node3D targetNode)
	{
		var aabb = SpawnDeathShaderManager.CalculateNodeAabb(targetNode);
		TargetPosition = aabb.Position + aabb.Size * 0.5f;
		float maxDim = Mathf.Max(aabb.Size.X, Mathf.Max(aabb.Size.Y, aabb.Size.Z));
		CameraDistance = Mathf.Clamp(maxDim * 2.2f, 2.0f, 30.0f);
		DefaultDistance = CameraDistance;
		UpdateCameraTransform();
	}


	private static readonly string[] _textureSubfolders = { "icons", "vfx", "vfx_radial", "vfx_vertical", "decals", "textures" };

	private Texture2D? LoadTexture2D(string? assetPath, string defaultSubFolder = "textures")
	{
		if (string.IsNullOrWhiteSpace(assetPath)) return null;

		var loaded = RtexIconLoader.Load(assetPath);
		if (loaded != null) return loaded;

		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string? diskPath = MapAssetHelper.FindAssetOnDisk(wsPath, defaultSubFolder, assetPath);

		if (string.IsNullOrEmpty(diskPath))
		{
			foreach (var folder in _textureSubfolders)
			{
				diskPath = MapAssetHelper.FindAssetOnDisk(wsPath, folder, assetPath);
				if (!string.IsNullOrEmpty(diskPath)) break;
			}
		}

		if (!string.IsNullOrEmpty(diskPath) && File.Exists(diskPath))
		{
			return RtexIconLoader.Load(diskPath);
		}

		return null;
	}

	private void ToggleAudioPlayback()
	{
		if (_audioPlayer.Playing)
		{
			_audioPlayer.Stop();
			_btnAudioPlay.Text = $"{UnicodeIcons.PLAY} " + TranslationServer.Translate("Play");
		}
		else if (_audioPlayer.Stream != null)
		{
			_audioPlayer.Play();
			_btnAudioPlay.Text = $"{UnicodeIcons.PAUSE} " + TranslationServer.Translate("Pause");
		}
	}

	private void OnAudioFinished()
	{
		_btnAudioPlay.Text = $"{UnicodeIcons.PLAY} " + TranslationServer.Translate("Play");
	}
}