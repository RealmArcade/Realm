using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Realm.Godot.Animation;
using Realm.Godot.Utils;
using Realm.Godot.Services.ModelOptimization;
using Realm.Ecs.Services;
using Realm.Shared.Textures;
using Realm.Shared.Audio;
using Realm.Shared.Metadata;
using Realm.Godot.Services;

public partial class TemplateManagerDialog : FloatingPreview3DDialogBase
{
	private Node3D _simRoot;
	private Node3D _currentModelRoot;
	private AnimatedSprite3D _vfxSprite;

	private PanelContainer _preview2DContainer;
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

	private EntityVisualEditDialog _entityVisualEditDialog;
	private WeaponVfxDialog _weaponVfxDialog;
	private AbilityVfxDialog _abilityVfxDialog;
	private DecalSettingsDialog _decalEditDialog;
	private ShaderEditorDialog _shaderEditDialog;
	private SpritesheetAssetEditDialog _spritesheetEditDialog;
	private TerrainTextureEditDialog _textureEditDialog;
	private ItemUpgradeEditDialog _itemUpgradeEditDialog;

	private string _currentCategory = "units";
	private string _searchFilter = "";
	private string _currentPreviewTemplateID = "";

	public TemplateManagerDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Templates Manager"), new Vector2(720, 780))
	{
		SetUncompressedPanelTexture("res://Assets/UI/map_editor_assets_importer.png", 34, 40, 60, 60);

		_entityVisualEditDialog = new EntityVisualEditDialog(hud);
		_weaponVfxDialog = new WeaponVfxDialog(hud);
		_abilityVfxDialog = new AbilityVfxDialog(hud);
		_decalEditDialog = new DecalSettingsDialog(hud);
		_shaderEditDialog = new ShaderEditorDialog(hud);
		_spritesheetEditDialog = new SpritesheetAssetEditDialog(hud);
		_textureEditDialog = new TerrainTextureEditDialog(hud);
		_itemUpgradeEditDialog = new ItemUpgradeEditDialog(hud);

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

		_optObjectCategory.ItemSelected += (idx) =>
		{
			string cat = _optObjectCategory.GetItemMetadata((int)idx).AsString();
			SetCurrentCategory(cat);
		};
		catRow.AddChild(_optObjectCategory);

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

		RefreshObjectList();
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

		if (string.IsNullOrEmpty(_currentPreviewTemplateID) || !items.Any(i => i.TemplateID.Equals(_currentPreviewTemplateID, StringComparison.OrdinalIgnoreCase)))
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

	private List<ObjectItemInfo> GetObjectsForCategory(string category)
	{
		var list = new List<ObjectItemInfo>();
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		if (!MetadataService.Instance.TryLoadMetadata(wsPath, out var meta) || meta == null)
		{
			return list;
		}

		switch (category)
		{
			case "units":
				if (meta.Templates?.Units != null)
				{
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
				}
				break;

			case "buildings":
				if (meta.Templates?.Buildings != null)
				{
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
				}
				break;

			case "resources":
				if (meta.Templates?.Resources != null)
				{
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
				}
				break;

			case "props":
				if (meta.Templates?.Props != null)
				{
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
				}
				break;

			case "weapons":
				if (meta.Templates?.Weapons != null)
				{
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
				}
				break;

			case "abilities":
				if (meta.Templates?.Abilities != null)
				{
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
				}
				break;

			case "upgrades":
				if (meta.Templates?.Upgrades != null)
				{
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
				}
				break;

			case "items":
				if (meta.Templates?.Items != null)
				{
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
				}
				break;

			case "terrain":
				if (meta.Textures != null)
				{
					foreach (var kvp in meta.Textures)
					{
						list.Add(new ObjectItemInfo
						{
							Category = "terrain",
							TemplateID = kvp.Key,
							Name = kvp.Key,
							Description = "Terrain texture swatch config",
							ModelPath = kvp.Key
						});
					}
				}
				if (meta.TerrainProfiles != null)
				{
					foreach (var tp in meta.TerrainProfiles)
					{
						if (!list.Any(x => string.Equals(x.TemplateID, tp.SwatchName, StringComparison.OrdinalIgnoreCase)))
						{
							list.Add(new ObjectItemInfo
							{
								Category = "terrain",
								TemplateID = tp.SwatchName,
								Name = tp.SwatchName,
								Description = "Terrain swatch profile",
								ModelPath = tp.SwatchName
							});
						}
					}
				}
				break;

			case "spritesheets":
				if (meta.VfxSpritesheets != null)
				{
					foreach (var kvp in meta.VfxSpritesheets)
					{
						var sheetObj = kvp.Value;
						int cols = sheetObj?.Columns ?? 1;
						int rows = sheetObj?.Rows ?? 1;
						float fps = sheetObj?.Fps ?? 20.0f;
						list.Add(new ObjectItemInfo
						{
							Category = "spritesheets",
							TemplateID = kvp.Key,
							Name = kvp.Key,
							Description = $"{cols}x{rows} @ {fps} FPS",
							ModelPath = kvp.Key
						});
					}
				}
				break;

			case "decals":
				if (meta.Decals != null)
				{
					foreach (var kvp in meta.Decals)
					{
						list.Add(new ObjectItemInfo
						{
							Category = "decals",
							TemplateID = kvp.Key,
							Name = kvp.Key,
							Description = "Decal configuration",
							ModelPath = kvp.Key
						});
					}
				}
				break;

			case "shaders" or "shader":
				if (meta.Shaders != null)
				{
					foreach (var kvp in meta.Shaders)
					{
						list.Add(new ObjectItemInfo
						{
							Category = "shaders",
							TemplateID = kvp.Key,
							Name = kvp.Key,
							Description = "Custom visual shader config"
						});
					}
				}
				break;
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

		// Action buttons: Edit & Delete
		var actionsHBox = new HBoxContainer();
		actionsHBox.AddThemeConstantOverride("separation", 4);

		var faFont = Hud?.GetFontAwesomeFont();

		var btnEdit = new Button();
		btnEdit.Text = UnicodeIcons.EDIT;
		if (faFont != null) btnEdit.AddThemeFontOverride("font", faFont);
		btnEdit.TooltipText = TranslationServer.Translate("Edit Object Properties");
		btnEdit.CustomMinimumSize = new Vector2(28, 28);
		btnEdit.Pressed += () => OpenEditDialogForObject(item);
		actionsHBox.AddChild(btnEdit);

		var btnDelete = new Button();
		btnDelete.Text = UnicodeIcons.TRASH_ALT;
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
		switch (item.Category)
		{
			case "units" or "buildings" or "resources" or "props":
				_entityVisualEditDialog.OpenForObject(item.Category, item.TemplateID, (oldId, newId) =>
				{
					RefreshObjectList();
				});
				break;

			case "upgrades" or "items":
				_itemUpgradeEditDialog.OpenForObject(item.Category, item.TemplateID, (oldId, newId) =>
				{
					RefreshObjectList();
				});
				break;

			case "weapons":
				string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
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
								m.AddOrUpdateWeapon(updatedWeapon);
							});
							RefreshObjectList();
						});
					}
				}
				break;

			case "abilities":
				string wsPathAbi = MapWorkspaceService.GetActiveWorkspacePath();
				if (MetadataService.Instance.TryLoadMetadata(wsPathAbi, out var metaAbi) && metaAbi?.Templates?.Abilities != null)
				{
					int aIdx = metaAbi.Templates.Abilities.FindIndex(x => string.Equals(x.TemplateID, item.TemplateID, StringComparison.OrdinalIgnoreCase));
					if (aIdx >= 0)
					{
						var a = metaAbi.Templates.Abilities[aIdx];
						var jsonObj = new JsonObject
						{
							["VisualEffect"] = a.VisualEffect,
							["CastSound"] = a.CastSound,
							["IconPath"] = a.IconPath,
							["AreaOfEffectRadius"] = a.AreaOfEffectRadius
						};
						_abilityVfxDialog.OpenForAbility(a.TemplateID, jsonObj, updatedObj =>
						{
							MetadataService.Instance.UpdateMetadata(wsPathAbi, m =>
							{
								m.UpdateAbility(item.TemplateID, abi =>
								{
									abi.VisualEffect = updatedObj["VisualEffect"]?.ToString();
									abi.CastSound = updatedObj["CastSound"]?.ToString();
									abi.IconPath = updatedObj["IconPath"]?.ToString();
									if (float.TryParse(updatedObj["AreaOfEffectRadius"]?.ToString(), out float radius)) abi.AreaOfEffectRadius = radius;
									return abi;
								});
							});
							RefreshObjectList();
						});
					}
				}
				break;

			case "terrain":
				string wsPathTer = MapWorkspaceService.GetActiveWorkspacePath();
				JsonObject curTerData = new JsonObject();
				if (MetadataService.Instance.TryLoadMetadata(wsPathTer, out var metaTer) && metaTer?.Textures != null)
				{
					if (metaTer.Textures.TryGetValue(item.TemplateID, out var tNode) && tNode != null)
					{
						curTerData = System.Text.Json.JsonSerializer.SerializeToNode(tNode) as JsonObject ?? new JsonObject();
					}
				}
				_textureEditDialog.OpenForTexture(item.TemplateID, curTerData, updatedObj =>
				{
					MetadataService.Instance.UpdateMetadata(wsPathTer, m =>
					{
						m.Textures ??= new(StringComparer.OrdinalIgnoreCase);
						m.Textures[item.TemplateID] = System.Text.Json.JsonSerializer.Deserialize<TextureMetadata>(updatedObj.ToJsonString()) ?? new TextureMetadata();
					});
					RefreshObjectList();
				});
				break;

			case "spritesheets":
				string wsPathSpr = MapWorkspaceService.GetActiveWorkspacePath();
				int initCols = 1;
				int initRows = 1;
				float initFps = 20.0f;
				bool initBlend = true;
				string initRtex = "";
				if (MetadataService.Instance.TryLoadMetadata(wsPathSpr, out var metaSpr) && metaSpr?.VfxSpritesheets != null)
				{
					if (metaSpr.VfxSpritesheets.TryGetValue(item.TemplateID, out var sNode) && sNode != null)
					{
						initCols = sNode.Columns > 0 ? sNode.Columns : 1;
						initRows = sNode.Rows > 0 ? sNode.Rows : 1;
						initFps = sNode.Fps > 0 ? sNode.Fps : 20.0f;
						initBlend = sNode.SubframeBlend;
						initRtex = sNode.AssetType ?? "";
					}
				}
				_spritesheetEditDialog.OpenForSheet(item.TemplateID, initRtex, initCols, initRows, initFps, initBlend, (newId, rtex, cols, rows, fps, blend) =>
				{
					MetadataService.Instance.UpdateMetadata(wsPathSpr, m =>
					{
						m.VfxSpritesheets ??= new(StringComparer.OrdinalIgnoreCase);
						if (!string.Equals(item.TemplateID, newId, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(item.TemplateID))
						{
							m.VfxSpritesheets.Remove(item.TemplateID);
						}
						m.VfxSpritesheets[newId] = new VfxMetadata
						{
							Columns = cols,
							Rows = rows,
							Fps = fps,
							SubframeBlend = blend,
							AssetType = rtex
						};
					});
					RefreshObjectList();
				});
				break;

			case "decals":
				string wsPathDec = MapWorkspaceService.GetActiveWorkspacePath();
				JsonObject curDecData = new JsonObject();
				if (MetadataService.Instance.TryLoadMetadata(wsPathDec, out var metaDec) && metaDec?.Decals != null)
				{
					if (metaDec.Decals.TryGetValue(item.TemplateID, out var dNode) && dNode != null)
					{
						curDecData = System.Text.Json.JsonSerializer.SerializeToNode(dNode) as JsonObject ?? new JsonObject();
					}
				}
				_decalEditDialog.OpenForDecal(item.TemplateID, curDecData, updatedObj =>
				{
					MetadataService.Instance.UpdateMetadata(wsPathDec, m =>
					{
						m.Decals ??= new(StringComparer.OrdinalIgnoreCase);
						m.Decals[item.TemplateID] = System.Text.Json.JsonSerializer.Deserialize<DecalMetadata>(updatedObj.ToJsonString()) ?? new DecalMetadata();
					});
					RefreshObjectList();
				});
				break;

			case "shaders" or "shader":
				string wsPathSha = MapWorkspaceService.GetActiveWorkspacePath();
				_shaderEditDialog.OpenForShader(item.TemplateID, updatedConfig =>
				{
					MetadataService.Instance.UpdateMetadata(wsPathSha, m =>
					{
						m.Shaders ??= new(StringComparer.OrdinalIgnoreCase);
						m.Shaders[item.TemplateID] = new ShaderMetadata();
					});
					RefreshObjectList();
				});
				break;

			default:
				if (item.Category == "upgrades" || item.Category == "items")
				{
					_itemUpgradeEditDialog.OpenForObject(item.Category, item.TemplateID, (oldId, newId) =>
					{
						RefreshObjectList();
					});
				}
				else
				{
					_entityVisualEditDialog.OpenForObject(item.Category, item.TemplateID, (oldId, newId) =>
					{
						RefreshObjectList();
					});
				}
				break;
		}
	}

	private void AddNewObjectForCategory(string category)
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string objectType = category switch
		{
			"units" => "unit",
			"buildings" => "building",
			"resources" => "resource",
			"props" => "prop",
			"weapons" => "weapon",
			"abilities" => "ability",
			"upgrades" => "upgrade",
			"items" => "item",
			"terrain" => "terrain",
			"spritesheets" => "spritesheet",
			"decals" => "decal",
			"shaders" or "shader" => "shader",
			_ => "unit"
		};

		var existingIDs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (MetadataService.Instance.TryLoadMetadata(wsPath, out var meta) && meta != null)
		{
			var currentList = GetObjectsForCategory(category);
			foreach (var obj in currentList) existingIDs.Add(obj.TemplateID);
		}

		string newTemplateID = TemplateIDHelper.GenerateTemplateID(objectType, $"new_{objectType}", existingIDs);
		var (parsedType, parsedSlug) = TemplateIDHelper.ParseTemplateID(newTemplateID);

		MetadataService.Instance.UpdateMetadata(wsPath, m =>
		{
			switch (category)
			{
				case "units":
					m.AddOrUpdateUnit(new UnitMetadata
					{
						TemplateID = newTemplateID,
						Name = parsedSlug,
						Description = "A new unit entity.",
						Scale = 1.0f,
						MaxHp = 100,
						Damage = 10,
						Range = 2.0f,
						Armor = 0,
						Speed = 5.0f,
						AttackCooldown = 1.5f,
						CostGold = 100,
						ProductionTime = 10.0f,
						PopCost = 1,
						AttackType = "melee",
						PathingType = 8
					});
					break;

				case "buildings":
					m.AddOrUpdateBuilding(new UnitMetadata
					{
						TemplateID = newTemplateID,
						Name = parsedSlug,
						Description = "A new building entity.",
						Scale = 1.5f,
						MaxHp = 1000,
						Armor = 10,
						CostGold = 200,
						CostWood = 100,
						ProductionTime = 15.0f,
						AttackType = "none",
						PathingType = 32
					});
					break;

				case "resources":
					m.AddOrUpdateResource(new ResourceMetadata
					{
						TemplateID = newTemplateID,
						Name = parsedSlug,
						Description = "Harvestable resource deposit.",
						Scale = 1.0f,
						MaxCapacity = 2000.0f,
						HarvestRate = 10.0f,
						MaxWorkers = 5,
						PathingType = 255
					});
					break;

				case "props":
					m.AddOrUpdateProp(new PropMetadata
					{
						TemplateID = newTemplateID,
						Name = parsedSlug,
						Description = "Decorative prop.",
						Scale = 1.0f,
						PathingType = 255
					});
					break;

				case "weapons":
					m.AddOrUpdateWeapon(new WeaponMetadata
					{
						TemplateID = newTemplateID,
						Name = parsedSlug,
						Damage = 10,
						Range = 8.0f,
						AttackCooldown = 1.5f,
						AttackType = "ranged",
						ProjectileSpeed = 25.0f,
						ArcHeight = 2.0f,
						OrientToTrajectory = true
					});
					break;

				case "abilities":
					m.AddOrUpdateAbility(new AbilityMetadata
					{
						TemplateID = newTemplateID,
						Name = parsedSlug,
						Description = "A new ability.",
						AbilityType = "target_spell",
						Cooldown = 10,
						AreaOfEffectRadius = 4.0f
					});
					break;

				case "upgrades":
					m.AddOrUpdateUpgrade(new UpgradeMetadata
					{
						TemplateID = newTemplateID,
						Name = parsedSlug,
						Description = "A new upgrade."
					});
					break;

				case "items":
					m.AddOrUpdateItem(new ItemMetadata
					{
						TemplateID = newTemplateID,
						Name = parsedSlug,
						Description = "A new item.",
						ItemClass = "consumable"
					});
					break;

				case "terrain":
					m.Textures ??= new(StringComparer.OrdinalIgnoreCase);
					m.Textures[newTemplateID] = new TextureMetadata
					{
						TileMode = "Stochastic",
						UvScale = 1.0f,
						Brightness = 1.0f
					};
					break;

				case "spritesheets":
					m.VfxSpritesheets ??= new(StringComparer.OrdinalIgnoreCase);
					m.VfxSpritesheets[newTemplateID] = new VfxMetadata
					{
						Columns = 4,
						Rows = 4,
						Fps = 20.0f,
						SubframeBlend = true
					};
					break;

				case "decals":
					m.Decals ??= new(StringComparer.OrdinalIgnoreCase);
					m.Decals[newTemplateID] = new DecalMetadata();
					break;

				case "shaders" or "shader":
					m.Shaders ??= new(StringComparer.OrdinalIgnoreCase);
					m.Shaders[newTemplateID] = new ShaderMetadata();
					break;
			}
		});

		RefreshObjectList();
		OpenEditDialogForObject(new ObjectItemInfo
		{
			Category = category,
			TemplateID = newTemplateID,
			Name = parsedSlug,
			Description = ""
		});
	}

	private void DeleteObject(ObjectItemInfo item)
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		// Reference check in placed map entities
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

		string message = references.Count > 0
			? string.Format(TranslationServer.Translate("Object '{0}' is referenced:\n- {1}\n\nAre you sure you want to delete it?"), item.TemplateID, string.Join("\n- ", references))
			: string.Format(TranslationServer.Translate("Are you sure you want to delete object '{0}' from metadata.json?"), item.TemplateID);

		Hud?.ShowConfirmationDialog(message, () =>
		{
			MetadataService.Instance.UpdateMetadata(wsPath, meta =>
			{
				switch (item.Category)
				{
					case "units": meta.RemoveUnit(item.TemplateID); break;
					case "buildings": meta.RemoveBuilding(item.TemplateID); break;
					case "resources": meta.RemoveResource(item.TemplateID); break;
					case "props": meta.RemoveProp(item.TemplateID); break;
					case "weapons": meta.RemoveWeapon(item.TemplateID); break;
					case "abilities": meta.RemoveAbility(item.TemplateID); break;
					case "upgrades": meta.RemoveUpgrade(item.TemplateID); break;
					case "items": meta.RemoveItem(item.TemplateID); break;
					case "terrain":
						meta.Textures?.Remove(item.TemplateID);
						meta.RemoveTerrainProfile(item.TemplateID);
						break;
					case "spritesheets":
						meta.VfxSpritesheets?.Remove(item.TemplateID);
						break;
					case "decals":
						meta.Decals?.Remove(item.TemplateID);
						break;
					case "shaders" or "shader":
						meta.Shaders?.Remove(item.TemplateID);
						break;
				}
			});

			RefreshObjectList();
		});
	}

	private void LoadPreviewForObject(ObjectItemInfo item)
	{
		_currentPreviewTemplateID = item.TemplateID;

		// Clear previous 3D model
		if (_currentModelRoot != null && GodotObject.IsInstanceValid(_currentModelRoot))
		{
			foreach (Node child in _currentModelRoot.GetChildren())
			{
				child.QueueFree();
			}
		}

		if (_vfxSprite != null && GodotObject.IsInstanceValid(_vfxSprite))
		{
			_vfxSprite.Visible = false;
		}

		if (item.Category == "decals")
		{
			PreviewSubViewport.GetParent<Control>().Visible = false;
			_previewAudioContainer.Visible = false;
			_preview2DContainer.Visible = true;

			Texture2D? decalTex = GameHost.Instance?.LoadDecalTexture(item.TemplateID);
			_preview2DImage.Texture = decalTex;
			_lblPreview2DInfo.Text = item.Name;
		}
		else if (!string.IsNullOrEmpty(item.ModelPath))
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
		else if (!string.IsNullOrEmpty(item.IconPath))
		{
			PreviewSubViewport.GetParent<Control>().Visible = false;
			_previewAudioContainer.Visible = false;
			_preview2DContainer.Visible = true;

			_preview2DImage.Texture = GD.Load<Texture2D>(item.IconPath);
			_lblPreview2DInfo.Text = item.Name;
		}
		else
		{
			_preview2DContainer.Visible = false;
			_previewAudioContainer.Visible = false;
			PreviewSubViewport.GetParent<Control>().Visible = true;
		}
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
