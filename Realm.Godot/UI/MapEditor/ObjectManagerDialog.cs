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

public partial class ObjectManagerDialog : FloatingPreview3DDialogBase
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

	private string _currentCategory = "units";
	private string _searchFilter = "";
	private string _currentPreviewObjectID = "";

	public ObjectManagerDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Map Objects Manager"), new Vector2(720, 780))
	{
		SetUncompressedPanelTexture("res://Assets/UI/map_editor_assets_importer.png", 34, 40, 60, 60);

		_entityVisualEditDialog = new EntityVisualEditDialog(hud);
		_weaponVfxDialog = new WeaponVfxDialog(hud);
		_abilityVfxDialog = new AbilityVfxDialog(hud);
		_decalEditDialog = new DecalSettingsDialog(hud);
		_shaderEditDialog = new ShaderEditorDialog(hud);
		_spritesheetEditDialog = new SpritesheetAssetEditDialog(hud);
		_textureEditDialog = new TerrainTextureEditDialog(hud);

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
		AddCameraPresetToolbar(BodyContainer, includeBack: true);

		// 3. CATEGORY & FILTER & ADD BAR
		var catRow = new HBoxContainer();
		catRow.AddThemeConstantOverride("separation", 8);

		var lblCat = new Label();
		lblCat.Text = TranslationServer.Translate("Object Domain:");
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

		_optObjectCategory.ItemSelected += (idx) =>
		{
			string cat = _optObjectCategory.GetItemMetadata((int)idx).AsString();
			SetCurrentCategory(cat);
		};
		catRow.AddChild(_optObjectCategory);

		var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		catRow.AddChild(spacer);

		_btnAddObject = AddButton(catRow, "\uf067 " + TranslationServer.Translate("Add Object"), () => AddNewObjectForCategory(_currentCategory), "Add new object to metadata.json", 11, new Vector2(120, 26));

		BodyContainer.AddChild(catRow);

		// 4. SEARCH FILTER INPUT
		var searchRow = new HBoxContainer();
		searchRow.AddThemeConstantOverride("separation", 6);

		var lblSearch = new Label();
		lblSearch.Text = "\uf002 " + TranslationServer.Translate("Filter:");
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
				i.ObjectID.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
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

		if (string.IsNullOrEmpty(_currentPreviewObjectID) || !items.Any(i => i.ObjectID.Equals(_currentPreviewObjectID, StringComparison.OrdinalIgnoreCase)))
		{
			LoadPreviewForObject(items[0]);
		}
	}

	private struct ObjectItemInfo
	{
		public string Category;
		public string ObjectID;
		public string Name;
		public string Description;
		public string ModelPath;
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
				if (meta.CustomUnits != null)
				{
					foreach (var u in meta.CustomUnits)
					{
						list.Add(new ObjectItemInfo
						{
							Category = "units",
							ObjectID = u.ObjectID,
							Name = u.Name ?? u.ObjectID,
							Description = u.Description ?? "",
							ModelPath = u.ModelPath ?? "",
							Scale = u.Scale > 0 ? u.Scale : 1.0f
						});
					}
				}
				break;

			case "buildings":
				if (meta.CustomBuildings != null)
				{
					foreach (var b in meta.CustomBuildings)
					{
						list.Add(new ObjectItemInfo
						{
							Category = "buildings",
							ObjectID = b.ObjectID,
							Name = b.Name ?? b.ObjectID,
							Description = b.Description ?? "",
							ModelPath = b.ModelPath ?? "",
							Scale = b.Scale > 0 ? b.Scale : 1.0f
						});
					}
				}
				break;

			case "resources":
				if (meta.CustomResources != null)
				{
					foreach (var r in meta.CustomResources)
					{
						list.Add(new ObjectItemInfo
						{
							Category = "resources",
							ObjectID = r.ObjectID,
							Name = r.Name ?? r.ObjectID,
							Description = r.Description ?? "",
							ModelPath = r.ModelPath ?? "",
							Scale = r.Scale > 0 ? r.Scale : 1.0f
						});
					}
				}
				break;

			case "props":
				if (meta.CustomProps != null)
				{
					foreach (var p in meta.CustomProps)
					{
						list.Add(new ObjectItemInfo
						{
							Category = "props",
							ObjectID = p.ObjectID,
							Name = p.Name ?? p.ObjectID,
							Description = p.Description ?? "",
							ModelPath = p.ModelPath ?? "",
							Scale = p.Scale > 0 ? p.Scale : 1.0f
						});
					}
				}
				break;

			case "weapons":
				if (meta.CustomWeapons != null)
				{
					foreach (var w in meta.CustomWeapons)
					{
						list.Add(new ObjectItemInfo
						{
							Category = "weapons",
							ObjectID = w.ObjectID,
							Name = w.Name ?? w.ObjectID,
							Description = $"Damage: {w.Damage} | Range: {w.Range} | Type: {w.AttackType}",
							ModelPath = w.ProjectileModelPath ?? ""
						});
					}
				}
				break;

			case "abilities":
				if (meta.CustomAbilities != null)
				{
					foreach (var a in meta.CustomAbilities)
					{
						list.Add(new ObjectItemInfo
						{
							Category = "abilities",
							ObjectID = a.ObjectID,
							Name = a.Name ?? a.ObjectID,
							Description = a.Description ?? "",
							IconPath = a.IconPath ?? ""
						});
					}
				}
				break;

			case "upgrades":
				if (meta.CustomUpgrades != null)
				{
					foreach (var u in meta.CustomUpgrades)
					{
						list.Add(new ObjectItemInfo
						{
							Category = "upgrades",
							ObjectID = u.ObjectID,
							Name = u.Name ?? u.ObjectID,
							Description = u.Description ?? ""
						});
					}
				}
				break;

			case "items":
				if (meta.CustomItems != null)
				{
					foreach (var itm in meta.CustomItems)
					{
						list.Add(new ObjectItemInfo
						{
							Category = "items",
							ObjectID = itm.ObjectID,
							Name = itm.Name ?? itm.ObjectID,
							Description = itm.Description ?? "",
							IconPath = itm.IconPath ?? ""
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
		lblId.Text = item.ObjectID;
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
		btnEdit.Text = "\uf044";
		if (faFont != null) btnEdit.AddThemeFontOverride("font", faFont);
		btnEdit.TooltipText = TranslationServer.Translate("Edit Object Properties");
		btnEdit.CustomMinimumSize = new Vector2(28, 28);
		btnEdit.Pressed += () => OpenEditDialogForObject(item);
		actionsHBox.AddChild(btnEdit);

		var btnDelete = new Button();
		btnDelete.Text = "\uf2ed";
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
			case "units" or "buildings" or "resources" or "props" or "items":
				_entityVisualEditDialog.OpenForObject(item.Category, item.ObjectID, (oldId, newId) =>
				{
					RefreshObjectList();
				});
				break;

			case "weapons":
				string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
				if (MetadataService.Instance.TryLoadMetadata(wsPath, out var meta) && meta?.CustomWeapons != null)
				{
					int wIdx = meta.CustomWeapons.FindIndex(x => string.Equals(x.ObjectID, item.ObjectID, StringComparison.OrdinalIgnoreCase));
					if (wIdx >= 0)
					{
						var w = meta.CustomWeapons[wIdx];
						_weaponVfxDialog.OpenForWeapon(w.ObjectID, w, updatedWeapon =>
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
				if (MetadataService.Instance.TryLoadMetadata(wsPathAbi, out var metaAbi) && metaAbi?.CustomAbilities != null)
				{
					int aIdx = metaAbi.CustomAbilities.FindIndex(x => string.Equals(x.ObjectID, item.ObjectID, StringComparison.OrdinalIgnoreCase));
					if (aIdx >= 0)
					{
						var a = metaAbi.CustomAbilities[aIdx];
						var jsonObj = new JsonObject
						{
							["VisualEffect"] = a.VisualEffect,
							["CastSound"] = a.CastSound,
							["IconPath"] = a.IconPath,
							["AreaOfEffectRadius"] = a.AreaOfEffectRadius
						};
						_abilityVfxDialog.OpenForAbility(a.ObjectID, jsonObj, updatedObj =>
						{
							MetadataService.Instance.UpdateMetadata(wsPathAbi, m =>
							{
								m.UpdateAbility(item.ObjectID, abi =>
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

			default:
				_entityVisualEditDialog.OpenForObject(item.Category, item.ObjectID, (oldId, newId) =>
				{
					RefreshObjectList();
				});
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
			_ => "unit"
		};

		var existingIDs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (MetadataService.Instance.TryLoadMetadata(wsPath, out var meta) && meta != null)
		{
			var currentList = GetObjectsForCategory(category);
			foreach (var obj in currentList) existingIDs.Add(obj.ObjectID);
		}

		string newObjectID = ObjectIDHelper.GenerateObjectID(objectType, $"new_{objectType}", existingIDs);
		var (parsedType, parsedSlug) = ObjectIDHelper.ParseObjectID(newObjectID);

		MetadataService.Instance.UpdateMetadata(wsPath, m =>
		{
			switch (category)
			{
				case "units":
					m.AddOrUpdateUnit(new GameHost.UnitMetadata
					{
						ObjectID = newObjectID,
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
					m.AddOrUpdateBuilding(new GameHost.UnitMetadata
					{
						ObjectID = newObjectID,
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
					m.AddOrUpdateResource(new GameHost.ResourceMetadata
					{
						ObjectID = newObjectID,
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
					m.AddOrUpdateProp(new GameHost.PropMetadata
					{
						ObjectID = newObjectID,
						Name = parsedSlug,
						Description = "Decorative prop.",
						Scale = 1.0f,
						PathingType = 255
					});
					break;

				case "weapons":
					m.AddOrUpdateWeapon(new GameHost.WeaponMetadata
					{
						ObjectID = newObjectID,
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
					m.AddOrUpdateAbility(new GameHost.AbilityMetadata
					{
						ObjectID = newObjectID,
						Name = parsedSlug,
						Description = "A new ability.",
						AbilityType = "target_spell",
						Cooldown = 10,
						AreaOfEffectRadius = 4.0f
					});
					break;

				case "upgrades":
					m.AddOrUpdateUpgrade(new GameHost.UpgradeMetadata
					{
						ObjectID = newObjectID,
						Name = parsedSlug,
						Description = "A new upgrade."
					});
					break;

				case "items":
					m.AddOrUpdateItem(new GameHost.ItemMetadata
					{
						ObjectID = newObjectID,
						Name = parsedSlug,
						Description = "A new item.",
						ItemClass = "consumable"
					});
					break;
			}
		});

		RefreshObjectList();
		OpenEditDialogForObject(new ObjectItemInfo
		{
			Category = category,
			ObjectID = newObjectID,
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
				if (terrainJson.Contains(item.ObjectID, StringComparison.OrdinalIgnoreCase))
				{
					references.Add($"Placed instances found on map terrain (terrain.json)");
				}
			}
			catch { }
		}

		string message = references.Count > 0
			? string.Format(TranslationServer.Translate("Object '{0}' is referenced:\n- {1}\n\nAre you sure you want to delete it?"), item.ObjectID, string.Join("\n- ", references))
			: string.Format(TranslationServer.Translate("Are you sure you want to delete object '{0}' from metadata.json?"), item.ObjectID);

		Hud?.ShowConfirmationDialog(message, () =>
		{
			MetadataService.Instance.UpdateMetadata(wsPath, meta =>
			{
				switch (item.Category)
				{
					case "units": meta.RemoveUnit(item.ObjectID); break;
					case "buildings": meta.RemoveBuilding(item.ObjectID); break;
					case "resources": meta.RemoveResource(item.ObjectID); break;
					case "props": meta.RemoveProp(item.ObjectID); break;
					case "weapons": meta.RemoveWeapon(item.ObjectID); break;
					case "abilities": meta.RemoveAbility(item.ObjectID); break;
					case "upgrades": meta.RemoveUpgrade(item.ObjectID); break;
					case "items": meta.RemoveItem(item.ObjectID); break;
				}
			});

			RefreshObjectList();
		});
	}

	private void LoadPreviewForObject(ObjectItemInfo item)
	{
		_currentPreviewObjectID = item.ObjectID;

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

		if (!string.IsNullOrEmpty(item.ModelPath))
		{
			_preview2DContainer.Visible = false;
			_previewAudioContainer.Visible = false;
			PreviewSubViewport.GetParent<Control>().Visible = true;

			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
			string subFolder = item.Category switch
			{
				"units" => "models/units",
				"buildings" => "models/buildings",
				"resources" => "models/resources",
				"props" => "models/props",
				_ => "models"
			};

			string? fullPath = MapAssetHelper.FindModelOnDisk(wsPath, subFolder, item.ModelPath);
			if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
			{
				fullPath = Path.Combine(wsPath, "Assets", subFolder, item.ModelPath);
			}

			if (File.Exists(fullPath))
			{
				try
				{
					Realm.Godot.Services.ModelOptimization.GltfDocumentExtensionMsftLod.RegisterExtension();
					var doc = new GltfDocument();
					var state = new GltfState();
					Error err = doc.AppendFromFile(fullPath, state);
					if (err == Error.Ok)
					{
						var mesh = doc.GenerateScene(state);
						if (mesh != null)
						{
							_currentModelRoot.AddChild(mesh);
							float s = item.Scale > 0 ? item.Scale : 1.0f;
							_currentModelRoot.Scale = new Vector3(s, s, s);
						}
					}
				}
				catch { }
			}
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
			_btnAudioPlay.Text = "\uf04b " + TranslationServer.Translate("Play");
		}
		else if (_audioPlayer.Stream != null)
		{
			_audioPlayer.Play();
			_btnAudioPlay.Text = "\uf04c " + TranslationServer.Translate("Pause");
		}
	}

	private void OnAudioFinished()
	{
		_btnAudioPlay.Text = "\uf04b " + TranslationServer.Translate("Play");
	}
}
