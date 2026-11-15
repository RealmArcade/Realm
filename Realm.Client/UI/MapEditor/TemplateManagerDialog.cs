using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.VFX;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Services;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class TemplateManagerDialog : FloatingPreview3DDialogBase
{
	public TemplateManagerDialog() { }

	private Node3D _simRoot = null!;
	private Node3D _currentModelRoot = null!;
	private AnimatedSprite3D _vfxSprite = null!;
	private ProceduralVfxInstance3D? _previewVfxInstance;
	private VisualProjectile3D? _previewProjectile;

	private PanelContainer _preview2DContainer = null!;
	private VBoxContainer _tooltipPreviewContainer = null!;
	private TextureRect _preview2DImage = null!;
	private Label _lblPreview2DInfo = null!;

	private PanelContainer _previewAudioContainer = null!;
	private Label _lblAudioInfo = null!;
	private Button _btnAudioPlay = null!;
	private AudioStreamPlayer _audioPlayer = null!;

	private OptionButton _optObjectCategory = null!;
	private LineEdit _txtSearchFilter = null!;
	private Button _btnAddObject = null!;
	private VBoxContainer _listVBox = null!;

	private EntityVisualEditDialog _entityVisualEditDialog = null!;
	private WeaponVfxDialog _weaponVfxDialog = null!;
	private AbilityVfxDialog _abilityVfxDialog = null!;
	private VfxStudioDialog _vfxStudioDialog = null!;
	private DecalSettingsDialog _decalEditDialog = null!;
	private ShaderEditorDialog _shaderEditDialog = null!;
	private SpritesheetAssetEditDialog _spritesheetEditDialog = null!;
	private TerrainTextureEditDialog _textureEditDialog = null!;
	private ItemEditDialog _itemEditDialog = null!;
	private UpgradeEditDialog _upgradeEditDialog = null!;

	private string _currentCategory = "units";
	private string _searchFilter = "";
	private string _currentPreviewTemplateID = "";
	private string _pendingNewVfxTemplateId = "";
	private bool _pendingNewVfxWasApplied;

	private HBoxContainer _previewMeshRow = null!;
	private OptionButton _optPreviewMesh = null!;
	private List<string> _availablePreviewMeshes = new();
	private string _selectedPreviewMesh = "";

	private CustomShaderConfig? _currentShaderConfig;
	private float _shaderAnimTime;
	private bool _shaderAnimForward = true;

	public TemplateManagerDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Templates Manager"), new Vector2(720, 780))
	{
		SetUncompressedPanelTexture("res://Assets/UI/map_editor_assets_importer.webp", 34, 40, 60, 60);

		_entityVisualEditDialog = new EntityVisualEditDialog(hud);
		_weaponVfxDialog = new WeaponVfxDialog(hud);
		_abilityVfxDialog = new AbilityVfxDialog(hud);
		_vfxStudioDialog = new VfxStudioDialog(hud);
		_decalEditDialog = new DecalSettingsDialog(hud);
		_shaderEditDialog = new ShaderEditorDialog(hud);
		_spritesheetEditDialog = new SpritesheetAssetEditDialog(hud);
		_textureEditDialog = new TerrainTextureEditDialog(hud);
		_itemEditDialog = new ItemEditDialog(hud);
		_upgradeEditDialog = new UpgradeEditDialog(hud);

		_entityVisualEditDialog.DialogClosed += ReloadCurrentPreview;
		_weaponVfxDialog.DialogClosed += ReloadCurrentPreview;
		_abilityVfxDialog.DialogClosed += ReloadCurrentPreview;
		_vfxStudioDialog.DialogClosed += ReloadCurrentPreview;
		_vfxStudioDialog.DialogClosed += HandleVfxStudioDialogClosed;
		_decalEditDialog.DialogClosed += ReloadCurrentPreview;
		_shaderEditDialog.DialogClosed += ReloadCurrentPreview;
		_spritesheetEditDialog.DialogClosed += ReloadCurrentPreview;
		_textureEditDialog.DialogClosed += ReloadCurrentPreview;
		_itemEditDialog.DialogClosed += ReloadCurrentPreview;
		_upgradeEditDialog.DialogClosed += ReloadCurrentPreview;

		DefaultDistance = 5.0f;
		CameraDistance = 5.0f;
		DefaultYaw = Mathf.DegToRad(45.0f);
		DefaultPitch = Mathf.DegToRad(25.0f);
		CameraYaw = Mathf.DegToRad(45.0f);
		CameraPitch = Mathf.DegToRad(25.0f);
		DefaultTargetPosition = Vector3.Zero;
		TargetPosition = DefaultTargetPosition;

		_audioPlayer = new AudioStreamPlayer { Name = "TemplatePreviewAudio", Bus = AudioService.PreviewBusName };
		_audioPlayer.Finished += OnAudioFinished;
		AddChild(_audioPlayer);

		BuildControls();
		SetFooterCloseOnly();
	}

	private void BuildControls()
	{
		BodyContainer.AddThemeConstantOverride("separation", 6);

		var previewStack = new PanelContainer();
		previewStack.Name = "PreviewStack_PanelContainer";
		previewStack.CustomMinimumSize = new Vector2(360, 180);
		previewStack.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		previewStack.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		BodyContainer.AddChild(previewStack);

		Add3DPreviewViewport(previewStack, new Vector2(360, 180));
		Setup3DEnvironment();

		_preview2DContainer = new PanelContainer();
		_preview2DContainer.Name = "Preview2DContainer_PanelContainer";
		_preview2DContainer.CustomMinimumSize = new Vector2(360, 180);
		_preview2DContainer.Visible = false;
		var preview2DVBox = new VBoxContainer();
		preview2DVBox.Name = "Preview2DVBox_VBoxContainer";
		preview2DVBox.Alignment = BoxContainer.AlignmentMode.Center;
		preview2DVBox.AddThemeConstantOverride("separation", 6);

		_tooltipPreviewContainer = new VBoxContainer();
		_tooltipPreviewContainer.Name = "TooltipPreviewContainer_VBoxContainer";
		_tooltipPreviewContainer.Alignment = BoxContainer.AlignmentMode.Center;
		_tooltipPreviewContainer.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		_tooltipPreviewContainer.Visible = false;
		preview2DVBox.AddChild(_tooltipPreviewContainer);

		_preview2DImage = new TextureRect();
		_preview2DImage.Name = "Preview2DImage_TextureRect";
		_preview2DImage.CustomMinimumSize = new Vector2(140, 140);
		_preview2DImage.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_preview2DImage.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		_preview2DImage.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		_preview2DImage.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		preview2DVBox.AddChild(_preview2DImage);

		_lblPreview2DInfo = new Label();
		_lblPreview2DInfo.Name = "LblPreview2DInfo_Label";
		_lblPreview2DInfo.HorizontalAlignment = HorizontalAlignment.Center;
		_lblPreview2DInfo.AddThemeFontSizeOverride("font_size", 10);
		_lblPreview2DInfo.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.70f));
		preview2DVBox.AddChild(_lblPreview2DInfo);

		_preview2DContainer.AddChild(preview2DVBox);
		previewStack.AddChild(_preview2DContainer);

		_previewAudioContainer = new PanelContainer();
		_previewAudioContainer.Name = "PreviewAudioContainer_PanelContainer";
		_previewAudioContainer.CustomMinimumSize = new Vector2(360, 180);
		_previewAudioContainer.Visible = false;
		var audioVBox = new VBoxContainer();
		audioVBox.Name = "AudioVBox_VBoxContainer";
		audioVBox.Alignment = BoxContainer.AlignmentMode.Center;
		audioVBox.AddThemeConstantOverride("separation", 10);

		var lblAudioIcon = new Label();
		lblAudioIcon.Name = "LblAudioIcon_Label";
		lblAudioIcon.Text = UnicodeIcons.AUDIO;
		var faFont = Hud?.GetFontAwesomeFont();
		if (faFont != null) lblAudioIcon.AddThemeFontOverride("font", faFont);
		lblAudioIcon.HorizontalAlignment = HorizontalAlignment.Center;
		lblAudioIcon.AddThemeFontSizeOverride("font_size", 36);
		audioVBox.AddChild(lblAudioIcon);

		_lblAudioInfo = new Label();
		_lblAudioInfo.Name = "LblAudioInfo_Label";
		_lblAudioInfo.Text = TranslationServer.Translate("No audio loaded");
		_lblAudioInfo.HorizontalAlignment = HorizontalAlignment.Center;
		_lblAudioInfo.AddThemeFontSizeOverride("font_size", 12);
		_lblAudioInfo.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		audioVBox.AddChild(_lblAudioInfo);

		var audioBtnRow = new HBoxContainer();
		audioBtnRow.Name = "AudioBtnRow_HBoxContainer";
		audioBtnRow.Alignment = BoxContainer.AlignmentMode.Center;
		audioBtnRow.AddThemeConstantOverride("separation", 8);

		_btnAudioPlay = AddButton(audioBtnRow, $"{UnicodeIcons.PLAY} " + TranslationServer.Translate("Play"), () => ToggleAudioPlayback(), "Play loaded audio", 11, new Vector2(80, 26));

		audioVBox.AddChild(audioBtnRow);
		_previewAudioContainer.AddChild(audioVBox);
		previewStack.AddChild(_previewAudioContainer);

		AddCameraPresetToolbar(BodyContainer, includeBack: true);

		var catRow = new HBoxContainer();
		catRow.Name = "CatRow_HBoxContainer";
		catRow.AddThemeConstantOverride("separation", 8);

		var lblCat = new Label();
		lblCat.Name = "LblCat_Label";
		lblCat.Text = TranslationServer.Translate("Type:");
		lblCat.AddThemeFontSizeOverride("font_size", 11);
		lblCat.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		catRow.AddChild(lblCat);

		_optObjectCategory = new OptionButton();
		_optObjectCategory.Name = "OptObjectCategory_OptionButton";
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
		_previewMeshRow.Name = "PreviewMeshRow_HBoxContainer";
		_previewMeshRow.AddThemeConstantOverride("separation", 6);
		_previewMeshRow.Visible = false;

		var lblMesh = new Label();
		lblMesh.Name = "LblMesh_Label";
		lblMesh.Text = TranslationServer.Translate("Preview Mesh:");
		lblMesh.AddThemeFontSizeOverride("font_size", 11);
		lblMesh.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_previewMeshRow.AddChild(lblMesh);

		_optPreviewMesh = new OptionButton();
		_optPreviewMesh.Name = "OptPreviewMesh_OptionButton";
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

		var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		catRow.AddChild(spacer);

		_btnAddObject = AddButton(catRow, $"{UnicodeIcons.PLUS} " + TranslationServer.Translate("Add"), () => AddNewObjectForCategory(_currentCategory), "Add new object to metadata.json", 11, new Vector2(120, 26));

		BodyContainer.AddChild(catRow);

		var searchRow = new HBoxContainer();
		searchRow.Name = "SearchRow_HBoxContainer";
		searchRow.AddThemeConstantOverride("separation", 6);

		var lblSearch = new Label();
		lblSearch.Name = "LblSearch_Label";
		lblSearch.Text = $"{UnicodeIcons.SEARCH} " + TranslationServer.Translate("Filter:");
		if (faFont != null) lblSearch.AddThemeFontOverride("font", faFont);
		lblSearch.AddThemeFontSizeOverride("font_size", 11);
		searchRow.AddChild(lblSearch);

		_txtSearchFilter = new LineEdit();
		_txtSearchFilter.Name = "TxtSearchFilter_LineEdit";
		_txtSearchFilter.PlaceholderText = TranslationServer.Translate("Type to filter objects by name or ID...");
		_txtSearchFilter.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_txtSearchFilter.AddThemeFontSizeOverride("font_size", 11);
		_txtSearchFilter.TextChanged += (text) =>
		{
			_searchFilter = text ?? string.Empty;
			RefreshObjectList();
		};
		searchRow.AddChild(_txtSearchFilter);

		BodyContainer.AddChild(searchRow);

		_listVBox = CreateScrollBody(340);
	}

	private void Setup3DEnvironment()
	{
		if (PreviewSubViewport == null) return;

		_simRoot = new Node3D();
		_simRoot.Name = "SimRoot_Node3D";
		PreviewSubViewport.AddChild(_simRoot);

		_currentModelRoot = new Node3D();
		_currentModelRoot.Name = "CurrentModelRoot_Node3D";
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
		ClearPreviewNodes();
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

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();

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
		catch
		{
		}
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
		catch
		{
		}
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
			lblEmpty.Name = "LblEmpty_Label";
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



	private Control CreateObjectRow(ObjectItemInfo item)
	{
		var row = new PanelContainer();
		row.Name = "Row_PanelContainer";
		row.CustomMinimumSize = new Vector2(0, 42);
		row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());

		var hBox = new HBoxContainer();
		hBox.Name = "HBox_HBoxContainer";
		hBox.AddThemeConstantOverride("separation", 8);
		row.AddChild(hBox);

		var selectBtn = new Button();
		selectBtn.Name = "SelectBtn_Button";
		selectBtn.Flat = true;
		selectBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		selectBtn.SizeFlagsVertical = SizeFlags.ExpandFill;
		selectBtn.Alignment = HorizontalAlignment.Left;
		selectBtn.Pressed += () => LoadPreviewForObject(item);

		var contentBox = new HBoxContainer();
		contentBox.Name = "ContentBox_HBoxContainer";
		contentBox.AddThemeConstantOverride("separation", 8);
		selectBtn.AddChild(contentBox);

		var infoVBox = new VBoxContainer();
		infoVBox.Name = "InfoVBox_VBoxContainer";
		infoVBox.AddThemeConstantOverride("separation", 2);

		var lblName = new RichTextLabel();
		lblName.Name = "LblName_RichTextLabel";
		lblName.BbcodeEnabled = true;
		lblName.Text = RichTooltip.ConvertToBbCode(item.Name);
		lblName.FitContent = true;
		lblName.ScrollActive = false;
		lblName.AutowrapMode = TextServer.AutowrapMode.Off;
		lblName.MouseFilter = Control.MouseFilterEnum.Ignore;
		lblName.AddThemeFontSizeOverride("normal_font_size", 12);
		lblName.AddThemeColorOverride("default_color", UIStyle.ColorGold);
		infoVBox.AddChild(lblName);

		var lblId = new Label();
		lblId.Name = "LblId_Label";
		lblId.Text = item.TemplateID;
		lblId.AddThemeFontSizeOverride("font_size", 10);
		lblId.AddThemeColorOverride("font_color", new Color(0.6f, 0.65f, 0.75f));
		infoVBox.AddChild(lblId);

		contentBox.AddChild(infoVBox);
		hBox.AddChild(selectBtn);

		var actionsHBox = new HBoxContainer();
		actionsHBox.Name = "ActionsHBox_HBoxContainer";
		actionsHBox.AddThemeConstantOverride("separation", 4);

		var faFont = Hud?.GetFontAwesomeFont();

		var btnPreview = new Button();
		btnPreview.Name = "BtnPreview_Button";
		btnPreview.Text = UnicodeIcons.EYE;
		btnPreview.Set("icon_max_width", 0);
		if (faFont != null) btnPreview.AddThemeFontOverride("font", faFont);
		btnPreview.TooltipText = TranslationServer.Translate("Preview Object");
		btnPreview.CustomMinimumSize = new Vector2(28, 28);
		btnPreview.Pressed += () => LoadPreviewForObject(item);
		actionsHBox.AddChild(btnPreview);

		var btnEdit = new Button();
		btnEdit.Name = "BtnEdit_Button";
		btnEdit.Text = UnicodeIcons.EDIT;
		btnEdit.Set("icon_max_width", 0);
		if (faFont != null) btnEdit.AddThemeFontOverride("font", faFont);
		btnEdit.TooltipText = TranslationServer.Translate("Edit Object Properties");
		btnEdit.CustomMinimumSize = new Vector2(28, 28);
		btnEdit.Pressed += () => OpenEditDialogForObject(item);
		actionsHBox.AddChild(btnEdit);

		var btnCopy = new Button();
		btnCopy.Name = "BtnCopy_Button";
		btnCopy.Text = UnicodeIcons.COPY;
		btnCopy.Set("icon_max_width", 0);
		if (faFont != null) btnCopy.AddThemeFontOverride("font", faFont);
		btnCopy.TooltipText = TranslationServer.Translate("Duplicate Object");
		btnCopy.CustomMinimumSize = new Vector2(28, 28);
		btnCopy.Pressed += () => DuplicateObject(item);
		actionsHBox.AddChild(btnCopy);

		var btnDelete = new Button();
		btnDelete.Name = "BtnDelete_Button";
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




		private static readonly Action<TemplateManagerDialog, MapMetadata, ObjectItemInfo, string, string> SpawnShaderDuplicateHandler = (dialog, m, item, newId, newSlug) =>
		{
			m.SpawnShaders ??= new(StringComparer.OrdinalIgnoreCase);
			m.SpawnShaders.TryGetValue(item.TemplateID, out var orig);
			if (orig == null)
			{
				var (_, oldSlug) = TemplateIDHelper.ParseTemplateID(item.TemplateID);
				if (!string.IsNullOrEmpty(oldSlug)) m.SpawnShaders.TryGetValue(oldSlug, out orig);
			}
			SpawnShaderMetadata copy = orig != null
				? JsonSerializer.Deserialize<SpawnShaderMetadata>(JsonSerializer.Serialize(orig))!
				: new SpawnShaderMetadata();
			copy.Name = newSlug;
			m.SpawnShaders[newId] = copy;
		};



}