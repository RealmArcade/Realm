using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Realm.Godot.Services;
using Realm.Godot.Utils;

public partial class EntityVisualEditDialog : FloatingDialogBase
{
	private string _category = "units"; // "units", "buildings", "resources", "props"
	private string _objectType = "unit";
	private string _slug = "";
	private string _originalObjectID = "";
	private string _modelPath = "";
	private string _portraitModelPath = "";
	private string _visualMode = "GroundPlane";
	private string _name = "";
	private string _description = "";

	private float _scale = 1.0f;
	private float _yOffset = 0.0f;
	private float _collisionCircle = 0.5f;
	private float _brightness = 0.5f;
	private Color _tint = Colors.White;
	private bool _normalizeLuminance = true;
	private bool _ignorePlayerColor = false;
	private bool _despillPlayerColor = false;
	private string _spawnShader = "";
	private string _deathShader = "";

	private bool _isSyncing = false;
	private Action<string, string> _onAppliedCallback; // (oldObjectID, newObjectID)

	private Label _lblObjectTypePrefix;
	private LineEdit _txtSlug;
	private Label _lblSlugValidation;
	private LineEdit _txtModelPath;
	private Action<string> _setModelPathValue;
	private LineEdit _txtPortraitModelPath;
	private Action<string> _setPortraitModelPathValue;
	private OptionButton _optVisualMode;
	private LineEdit _txtName;
	private LineEdit _txtDescription;

	private HSlider _sldScale;
	private Label _lblScale;
	private HSlider _sldYOffset;
	private Label _lblYOffset;
	private HSlider _sldCollisionCircle;
	private Label _lblCollisionCircle;
	private HSlider _sldBrightness;
	private Label _lblBrightness;
	private ColorPickerButton _btnTint;
	private CheckBox _chkNormalizeLuminance;
	private CheckBox _chkIgnorePlayerColor;
	private CheckBox _chkDespillPlayerColor;

	private LineEdit _txtSpawnShader;
	private Action<string> _setSpawnShaderValue;
	private LineEdit _txtDeathShader;
	private Action<string> _setDeathShaderValue;

	public EntityVisualEditDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Edit Entity Visual Properties"), new Vector2(520, 740))
	{
		BuildControls();
	}

	private void BuildControls()
	{
		var scrollBody = CreateScrollBody(600);
		var contentVBox = new VBoxContainer();
		contentVBox.AddThemeConstantOverride("separation", 10);
		contentVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scrollBody.AddChild(contentVBox);

		// SECTION 1: IDENTITY & ASSET
		AddSectionHeader(contentVBox, "🆔 " + TranslationServer.Translate("IDENTITY & ASSET MODEL"), new Color(0.95f, 0.8f, 0.4f));

		// Object ID Row (Fixed object_type prefix + Editable slug)
		var idRow = new HBoxContainer();
		idRow.AddThemeConstantOverride("separation", 6);

		var lblId = new Label();
		lblId.Text = TranslationServer.Translate("ObjectID:");
		lblId.CustomMinimumSize = new Vector2(140, 0);
		lblId.AddThemeFontSizeOverride("font_size", 11);
		idRow.AddChild(lblId);

		_lblObjectTypePrefix = new Label();
		_lblObjectTypePrefix.Text = "unit/";
		_lblObjectTypePrefix.AddThemeFontSizeOverride("font_size", 11);
		_lblObjectTypePrefix.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		idRow.AddChild(_lblObjectTypePrefix);

		_txtSlug = new LineEdit();
		_txtSlug.PlaceholderText = TranslationServer.Translate("snake_case_slug");
		_txtSlug.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_txtSlug.AddThemeFontSizeOverride("font_size", 11);
		_txtSlug.TextChanged += (val) =>
		{
			if (_isSyncing) return;
			_slug = ObjectIDHelper.ToSnakeCase(val);
			ValidateSlug();
		};
		idRow.AddChild(_txtSlug);
		contentVBox.AddChild(idRow);

		_lblSlugValidation = new Label();
		_lblSlugValidation.AddThemeFontSizeOverride("font_size", 10);
		_lblSlugValidation.AddThemeColorOverride("font_color", new Color(0.95f, 0.4f, 0.4f));
		_lblSlugValidation.Visible = false;
		contentVBox.AddChild(_lblSlugValidation);

		// Model Asset Dropdown (rmesh from manifest.json or rtex / textures)
		(_txtModelPath, _setModelPathValue) = AddAssetFilterDropdown(
			contentVBox,
			TranslationServer.Translate("Model / Visual Asset:"),
			_modelPath,
			(all) => ScanCompatibleModels(all),
			(val) =>
			{
				if (_isSyncing) return;
				_modelPath = val ?? string.Empty;
				if (!string.IsNullOrEmpty(_modelPath))
				{
					if (_modelPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) || _modelPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || _modelPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
					{
						if (_visualMode == "Mesh")
						{
							_visualMode = "GroundPlane";
							SyncVisualModeControl();
						}
					}
					else if (_modelPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
					{
						_visualMode = "Mesh";
						SyncVisualModeControl();
					}
				}
			},
			TranslationServer.Translate("Select .rmesh or .rtex asset..."),
			140f
		);

		// Visual Primitive Mode
		var modeRow = new HBoxContainer();
		modeRow.AddThemeConstantOverride("separation", 8);

		var lblMode = new Label();
		lblMode.Text = TranslationServer.Translate("Visual Primitive:");
		lblMode.CustomMinimumSize = new Vector2(140, 0);
		lblMode.AddThemeFontSizeOverride("font_size", 11);
		modeRow.AddChild(lblMode);

		_optVisualMode = new OptionButton();
		_optVisualMode.CustomMinimumSize = new Vector2(180, 24);
		_optVisualMode.AddThemeFontSizeOverride("font_size", 11);
		_optVisualMode.AddItem(TranslationServer.Translate("Ground Plane (Horizontal)"), 0);
		_optVisualMode.SetItemMetadata(0, "GroundPlane");
		_optVisualMode.AddItem(TranslationServer.Translate("Slope-Aligned Quad"), 1);
		_optVisualMode.SetItemMetadata(1, "SlopeAlignedQuad");
		_optVisualMode.AddItem(TranslationServer.Translate("3D Mesh (.rmesh)"), 2);
		_optVisualMode.SetItemMetadata(2, "Mesh");
		_optVisualMode.ItemSelected += (idx) =>
		{
			if (_isSyncing) return;
			_visualMode = _optVisualMode.GetItemMetadata((int)idx).AsString();
		};
		modeRow.AddChild(_optVisualMode);
		contentVBox.AddChild(modeRow);

		// Portrait Model Path
		(_txtPortraitModelPath, _setPortraitModelPathValue) = AddAssetFilterDropdown(
			contentVBox,
			TranslationServer.Translate("Portrait Model:"),
			_portraitModelPath,
			(all) => ScanCompatibleModels(all),
			(val) =>
			{
				if (_isSyncing) return;
				_portraitModelPath = val ?? string.Empty;
			},
			TranslationServer.Translate("Select optional portrait .rmesh..."),
			140f
		);

		_txtName = AddTextInput(
			contentVBox,
			TranslationServer.Translate("Display Name:"),
			_name,
			(val) => { if (!_isSyncing) _name = val ?? string.Empty; },
			TranslationServer.Translate("Entity display name..."),
			140f
		);

		_txtDescription = AddTextInput(
			contentVBox,
			TranslationServer.Translate("Description:"),
			_description,
			(val) => { if (!_isSyncing) _description = val ?? string.Empty; },
			TranslationServer.Translate("Entity description..."),
			140f
		);

		// SECTION 2: 3D TRANSFORM & RENDERING
		AddSectionHeader(contentVBox, "📐 " + TranslationServer.Translate("TRANSFORM & VISUAL RENDERING"), new Color(0.5f, 0.85f, 1.0f));

		(_sldScale, _lblScale) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Model Scale:"),
			0.1f,
			10.0f,
			0.05f,
			_scale,
			(val) => { if (!_isSyncing) _scale = val; },
			"0.00x",
			140f
		);

		(_sldYOffset, _lblYOffset) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Y Ground Offset:"),
			-5.0f,
			5.0f,
			0.02f,
			_yOffset,
			(val) => { if (!_isSyncing) _yOffset = val; },
			"0.00",
			140f
		);

		(_sldCollisionCircle, _lblCollisionCircle) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Collision Radius:"),
			0.0f,
			10.0f,
			0.05f,
			_collisionCircle,
			(val) => { if (!_isSyncing) _collisionCircle = val; },
			"0.00",
			140f
		);

		(_sldBrightness, _lblBrightness) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Brightness:"),
			0.0f,
			2.0f,
			0.02f,
			_brightness,
			(val) => { if (!_isSyncing) _brightness = val; },
			"0.00x",
			140f
		);

		var rowTint = new HBoxContainer();
		rowTint.AddThemeConstantOverride("separation", 8);
		var lblTint = new Label();
		lblTint.Text = TranslationServer.Translate("Tint Color:");
		lblTint.CustomMinimumSize = new Vector2(140, 0);
		lblTint.AddThemeFontSizeOverride("font_size", 11);
		rowTint.AddChild(lblTint);

		_btnTint = new ColorPickerButton();
		_btnTint.CustomMinimumSize = new Vector2(90, 24);
		_btnTint.Color = _tint;
		_btnTint.ColorChanged += (newCol) =>
		{
			if (_isSyncing) return;
			_tint = newCol;
		};
		rowTint.AddChild(_btnTint);
		contentVBox.AddChild(rowTint);

		_chkNormalizeLuminance = AddCheckBox(contentVBox, TranslationServer.Translate("Normalize Luminance"), _normalizeLuminance, (val) =>
		{
			if (_isSyncing) return;
			_normalizeLuminance = val;
		});

		_chkIgnorePlayerColor = AddCheckBox(contentVBox, TranslationServer.Translate("Ignore Player Color"), _ignorePlayerColor, (val) =>
		{
			if (_isSyncing) return;
			_ignorePlayerColor = val;
		});

		_chkDespillPlayerColor = AddCheckBox(contentVBox, TranslationServer.Translate("Despill Player Color"), _despillPlayerColor, (val) =>
		{
			if (_isSyncing) return;
			_despillPlayerColor = val;
		});

		// SECTION 3: SHADERS
		AddSectionHeader(contentVBox, "✨ " + TranslationServer.Translate("SPAWN & DEATH SHADERS"), new Color(0.95f, 0.7f, 0.95f));

		(_txtSpawnShader, _setSpawnShaderValue) = AddAssetFilterDropdown(
			contentVBox,
			TranslationServer.Translate("Spawn Shader:"),
			_spawnShader,
			(all) => ScanAvailableShaders(all),
			(val) => { if (!_isSyncing) _spawnShader = val ?? string.Empty; },
			TranslationServer.Translate("Select spawn shader..."),
			140f
		);

		(_txtDeathShader, _setDeathShaderValue) = AddAssetFilterDropdown(
			contentVBox,
			TranslationServer.Translate("Death Shader:"),
			_deathShader,
			(all) => ScanAvailableShaders(all),
			(val) => { if (!_isSyncing) _deathShader = val ?? string.Empty; },
			TranslationServer.Translate("Select death shader..."),
			140f
		);
	}

	private void SyncVisualModeControl()
	{
		if (_optVisualMode == null) return;
		for (int i = 0; i < _optVisualMode.ItemCount; i++)
		{
			if (string.Equals(_optVisualMode.GetItemMetadata(i).AsString(), _visualMode, StringComparison.OrdinalIgnoreCase))
			{
				_optVisualMode.Select(i);
				return;
			}
		}
		_optVisualMode.Select(0);
	}

	private List<string> ScanCompatibleModels(bool allFolders)
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		try
		{
			var assets = MapAssetHelper.LoadUnionedAssets(wsPath);
			if (assets["rmesh"] is System.Text.Json.Nodes.JsonObject rmeshObj)
			{
				foreach (var kvp in rmeshObj)
				{
					string fn = kvp.Key;
					string aType = kvp.Value?["asset_type"]?.ToString() ?? "";
					bool match = allFolders;
					if (!match)
					{
						match = _category switch
						{
							"units" => aType.Equals("Character", StringComparison.OrdinalIgnoreCase),
							"buildings" => aType.Equals("Building", StringComparison.OrdinalIgnoreCase),
							"resources" or "props" => aType.Equals("Prop", StringComparison.OrdinalIgnoreCase) || aType.Equals("Item", StringComparison.OrdinalIgnoreCase),
							_ => true
						};
					}

					if (match || allFolders)
					{
						results.Add(fn);
					}
				}
			}

			if (assets["decals"] is System.Text.Json.Nodes.JsonObject decalsObj)
			{
				foreach (var kvp in decalsObj)
				{
					results.Add(kvp.Key);
				}
			}

			if (assets["textures"] is System.Text.Json.Nodes.JsonObject texturesObj)
			{
				foreach (var kvp in texturesObj)
				{
					results.Add(kvp.Key);
				}
			}
		}
		catch { }

		string[] candidateFolders = new[]
		{
			Path.Combine(wsPath, "Assets", "decals"),
			Path.Combine(wsPath, "Assets", "textures"),
			Path.Combine("MapTemplate", "Assets", "decals"),
			Path.Combine("MapTemplate", "Assets", "textures")
		};

		foreach (var folder in candidateFolders)
		{
			if (Directory.Exists(folder))
			{
				foreach (var file in Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories))
				{
					if (file.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
					{
						results.Add(Path.GetFileName(file));
					}
				}
			}
		}

		if (results.Count == 0)
		{
			return ScanAvailableAssets("models", allFolders);
		}

		var sorted = results.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
		return sorted;
	}

	private List<string> ScanAvailableShaders(bool all)
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		var list = new List<string>();
		try
		{
			var shaders = SpawnDeathShaderManager.LoadAllCustomShaders(wsPath);
			foreach (var k in shaders.Keys) list.Add(k);
		}
		catch { }
		return list.OrderBy(x => x).ToList();
	}

	private void ValidateSlug()
	{
		string fullId = $"{_objectType}/{_slug}";
		if (string.IsNullOrWhiteSpace(_slug))
		{
			_lblSlugValidation.Text = TranslationServer.Translate("Slug cannot be empty.");
			_lblSlugValidation.Visible = true;
			return;
		}

		if (!fullId.Equals(_originalObjectID, StringComparison.OrdinalIgnoreCase))
		{
			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
			if (MetadataService.Instance.TryLoadMetadata(wsPath, out var meta) && meta != null)
			{
				bool exists = _category switch
				{
					"units" => meta.CustomUnits?.Any(u => string.Equals(u.ObjectID, fullId, StringComparison.OrdinalIgnoreCase)) ?? false,
					"buildings" => meta.CustomBuildings?.Any(b => string.Equals(b.ObjectID, fullId, StringComparison.OrdinalIgnoreCase)) ?? false,
					"resources" => meta.CustomResources?.Any(r => string.Equals(r.ObjectID, fullId, StringComparison.OrdinalIgnoreCase)) ?? false,
					"props" => meta.CustomProps?.Any(p => string.Equals(p.ObjectID, fullId, StringComparison.OrdinalIgnoreCase)) ?? false,
					_ => false
				};

				if (exists)
				{
					_lblSlugValidation.Text = TranslationServer.Translate("Slug already exists in this category.");
					_lblSlugValidation.Visible = true;
					return;
				}
			}
		}

		_lblSlugValidation.Visible = false;
	}

	public void OpenForObject(string category, string objectId, Action<string, string> onApplied)
	{
		_category = category.ToLowerInvariant();
		_objectType = _category switch
		{
			"units" => "unit",
			"buildings" => "building",
			"resources" => "resource",
			"props" => "prop",
			_ => "unit"
		};

		_originalObjectID = objectId;
		var (parsedType, parsedSlug) = ObjectIDHelper.ParseObjectID(objectId);
		_slug = !string.IsNullOrEmpty(parsedSlug) ? parsedSlug : ObjectIDHelper.GenerateSlug(objectId);

		_onAppliedCallback = onApplied;

		// Load properties from metadata.json
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		if (MetadataService.Instance.TryLoadMetadata(wsPath, out var meta) && meta != null)
		{
			switch (_category)
			{
				case "units":
					var u = meta.CustomUnits?.FirstOrDefault(x => string.Equals(x.ObjectID, objectId, StringComparison.OrdinalIgnoreCase));
					if (u.HasValue) LoadFromUnitMetadata(u.Value);
					break;
				case "buildings":
					var b = meta.CustomBuildings?.FirstOrDefault(x => string.Equals(x.ObjectID, objectId, StringComparison.OrdinalIgnoreCase));
					if (b.HasValue) LoadFromUnitMetadata(b.Value);
					break;
				case "resources":
					var r = meta.CustomResources?.FirstOrDefault(x => string.Equals(x.ObjectID, objectId, StringComparison.OrdinalIgnoreCase));
					if (r.HasValue) LoadFromResourceMetadata(r.Value);
					break;
				case "props":
					var p = meta.CustomProps?.FirstOrDefault(x => string.Equals(x.ObjectID, objectId, StringComparison.OrdinalIgnoreCase));
					if (p.HasValue) LoadFromPropMetadata(p.Value);
					break;
			}
		}

		SyncControls();
		OpenDialog();
	}

	private void LoadFromUnitMetadata(GameHost.UnitMetadata u)
	{
		_modelPath = u.ModelPath ?? "";
		_portraitModelPath = u.PortraitModelPath ?? "";
		_visualMode = !string.IsNullOrEmpty(u.VisualMode) ? u.VisualMode : (u.ModelPath?.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) == true ? "GroundPlane" : "Mesh");
		_name = u.Name ?? "";
		_description = u.Description ?? "";
		_scale = u.Scale > 0 ? u.Scale : 1.0f;
		_yOffset = u.YOffset;
		_collisionCircle = u.CollisionCircle;
		_brightness = u.Brightness;
		_tint = !string.IsNullOrEmpty(u.Tint) && u.Tint.StartsWith("#") ? Color.FromHtml(u.Tint) : Colors.White;
		_normalizeLuminance = u.NormalizeLuminance;
		_ignorePlayerColor = u.IgnorePlayerColor;
		_despillPlayerColor = u.DespillPlayerColor;
		_spawnShader = u.SpawnShader ?? "";
		_deathShader = u.DeathShader ?? "";
	}

	private void LoadFromResourceMetadata(GameHost.ResourceMetadata r)
	{
		_modelPath = r.ModelPath ?? "";
		_portraitModelPath = r.PortraitModelPath ?? "";
		_visualMode = !string.IsNullOrEmpty(r.VisualMode) ? r.VisualMode : (r.ModelPath?.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) == true ? "GroundPlane" : "Mesh");
		_name = r.Name ?? "";
		_description = r.Description ?? "";
		_scale = r.Scale > 0 ? r.Scale : 2.75f;
		_yOffset = r.YOffset;
		_collisionCircle = r.CollisionCircle;
		_brightness = r.Brightness;
		_tint = !string.IsNullOrEmpty(r.Tint) && r.Tint.StartsWith("#") ? Color.FromHtml(r.Tint) : Colors.White;
		_normalizeLuminance = r.NormalizeLuminance;
		_ignorePlayerColor = r.IgnorePlayerColor;
		_despillPlayerColor = r.DespillPlayerColor;
		_spawnShader = r.SpawnShader ?? "";
		_deathShader = r.DeathShader ?? "";
	}

	private void LoadFromPropMetadata(GameHost.PropMetadata p)
	{
		_modelPath = p.ModelPath ?? "";
		_portraitModelPath = p.PortraitModelPath ?? "";
		_visualMode = !string.IsNullOrEmpty(p.VisualMode) ? p.VisualMode : (p.ModelPath?.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) == true ? "GroundPlane" : "Mesh");
		_name = p.Name ?? "";
		_description = p.Description ?? "";
		_scale = p.Scale > 0 ? p.Scale : 1.25f;
		_yOffset = p.YOffset;
		_collisionCircle = p.CollisionCircle;
		_brightness = p.Brightness;
		_tint = !string.IsNullOrEmpty(p.Tint) && p.Tint.StartsWith("#") ? Color.FromHtml(p.Tint) : Colors.White;
		_normalizeLuminance = p.NormalizeLuminance;
		_ignorePlayerColor = p.IgnorePlayerColor;
		_despillPlayerColor = p.DespillPlayerColor;
		_spawnShader = p.SpawnShader ?? "";
		_deathShader = p.DeathShader ?? "";
	}

	private void SyncControls()
	{
		_isSyncing = true;
		try
		{
			if (_lblObjectTypePrefix != null) _lblObjectTypePrefix.Text = $"{_objectType}/";
			if (_txtSlug != null) _txtSlug.Text = _slug;
			_setModelPathValue?.Invoke(_modelPath);
			_setPortraitModelPathValue?.Invoke(_portraitModelPath);
			SyncVisualModeControl();
			if (_txtName != null) _txtName.Text = _name;
			if (_txtDescription != null) _txtDescription.Text = _description;

			if (_sldScale != null) { _sldScale.Value = _scale; _lblScale.Text = $"{_scale:F2}x"; }
			if (_sldYOffset != null) { _sldYOffset.Value = _yOffset; _lblYOffset.Text = $"{_yOffset:F2}"; }
			if (_sldCollisionCircle != null) { _sldCollisionCircle.Value = _collisionCircle; _lblCollisionCircle.Text = $"{_collisionCircle:F2}"; }
			if (_sldBrightness != null) { _sldBrightness.Value = _brightness; _lblBrightness.Text = $"{_brightness:F2}x"; }
			if (_btnTint != null) _btnTint.Color = _tint;
			if (_chkNormalizeLuminance != null) _chkNormalizeLuminance.ButtonPressed = _normalizeLuminance;
			if (_chkIgnorePlayerColor != null) _chkIgnorePlayerColor.ButtonPressed = _ignorePlayerColor;
			if (_chkDespillPlayerColor != null) _chkDespillPlayerColor.ButtonPressed = _despillPlayerColor;

			_setSpawnShaderValue?.Invoke(_spawnShader);
			_setDeathShaderValue?.Invoke(_deathShader);
			ValidateSlug();
		}
		finally
		{
			_isSyncing = false;
		}
	}

	protected override void OnApply()
	{
		if (string.IsNullOrWhiteSpace(_slug))
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Cannot save entity: Slug is required."));
			return;
		}

		string newObjectID = $"{_objectType}/{_slug}";
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();

		MetadataService.Instance.UpdateMetadata(wsPath, meta =>
		{
			string tintHex = $"#{_tint.ToHtml(false)}";

			switch (_category)
			{
				case "units":
					bool updatedU = meta.UpdateUnit(_originalObjectID, u =>
					{
						u.ObjectID = newObjectID;
						u.Name = _name;
						u.Description = _description;
						u.ModelPath = _modelPath;
						u.PortraitModelPath = _portraitModelPath;
						u.VisualMode = _visualMode;
						u.Scale = _scale;
						u.YOffset = _yOffset;
						u.CollisionCircle = _collisionCircle;
						u.Brightness = _brightness;
						u.Tint = tintHex;
						u.NormalizeLuminance = _normalizeLuminance;
						u.IgnorePlayerColor = _ignorePlayerColor;
						u.DespillPlayerColor = _despillPlayerColor;
						u.SpawnShader = string.IsNullOrWhiteSpace(_spawnShader) ? null : _spawnShader;
						u.DeathShader = string.IsNullOrWhiteSpace(_deathShader) ? null : _deathShader;
						return u;
					});
					if (!updatedU)
					{
						meta.AddOrUpdateUnit(new GameHost.UnitMetadata
						{
							ObjectID = newObjectID,
							Name = !string.IsNullOrEmpty(_name) ? _name : _slug,
							Description = _description,
							ModelPath = _modelPath,
							PortraitModelPath = _portraitModelPath,
							VisualMode = _visualMode,
							Scale = _scale,
							YOffset = _yOffset,
							CollisionCircle = _collisionCircle,
							Brightness = _brightness,
							Tint = tintHex,
							NormalizeLuminance = _normalizeLuminance,
							IgnorePlayerColor = _ignorePlayerColor,
							DespillPlayerColor = _despillPlayerColor,
							PathingType = 8,
							MaxHp = 100,
							SpawnShader = string.IsNullOrWhiteSpace(_spawnShader) ? null : _spawnShader,
							DeathShader = string.IsNullOrWhiteSpace(_deathShader) ? null : _deathShader
						});
					}
					break;

				case "buildings":
					bool updatedB = meta.UpdateBuilding(_originalObjectID, b =>
					{
						b.ObjectID = newObjectID;
						b.Name = _name;
						b.Description = _description;
						b.ModelPath = _modelPath;
						b.PortraitModelPath = _portraitModelPath;
						b.VisualMode = _visualMode;
						b.Scale = _scale;
						b.YOffset = _yOffset;
						b.CollisionCircle = _collisionCircle;
						b.Brightness = _brightness;
						b.Tint = tintHex;
						b.NormalizeLuminance = _normalizeLuminance;
						b.IgnorePlayerColor = _ignorePlayerColor;
						b.DespillPlayerColor = _despillPlayerColor;
						b.SpawnShader = string.IsNullOrWhiteSpace(_spawnShader) ? null : _spawnShader;
						b.DeathShader = string.IsNullOrWhiteSpace(_deathShader) ? null : _deathShader;
						return b;
					});
					if (!updatedB)
					{
						meta.AddOrUpdateBuilding(new GameHost.UnitMetadata
						{
							ObjectID = newObjectID,
							Name = !string.IsNullOrEmpty(_name) ? _name : _slug,
							Description = _description,
							ModelPath = _modelPath,
							PortraitModelPath = _portraitModelPath,
							VisualMode = _visualMode,
							Scale = _scale,
							YOffset = _yOffset,
							CollisionCircle = _collisionCircle,
							Brightness = _brightness,
							Tint = tintHex,
							NormalizeLuminance = _normalizeLuminance,
							IgnorePlayerColor = _ignorePlayerColor,
							DespillPlayerColor = _despillPlayerColor,
							PathingType = 32,
							MaxHp = 1000,
							SpawnShader = string.IsNullOrWhiteSpace(_spawnShader) ? null : _spawnShader,
							DeathShader = string.IsNullOrWhiteSpace(_deathShader) ? null : _deathShader
						});
					}
					break;

				case "resources":
					bool updatedR = meta.UpdateResource(_originalObjectID, r =>
					{
						r.ObjectID = newObjectID;
						r.Name = _name;
						r.Description = _description;
						r.ModelPath = _modelPath;
						r.PortraitModelPath = _portraitModelPath;
						r.VisualMode = _visualMode;
						r.Scale = _scale;
						r.YOffset = _yOffset;
						r.CollisionCircle = _collisionCircle;
						r.Brightness = _brightness;
						r.Tint = tintHex;
						r.NormalizeLuminance = _normalizeLuminance;
						r.IgnorePlayerColor = _ignorePlayerColor;
						r.DespillPlayerColor = _despillPlayerColor;
						r.SpawnShader = string.IsNullOrWhiteSpace(_spawnShader) ? null : _spawnShader;
						r.DeathShader = string.IsNullOrWhiteSpace(_deathShader) ? null : _deathShader;
						return r;
					});
					if (!updatedR)
					{
						meta.AddOrUpdateResource(new GameHost.ResourceMetadata
						{
							ObjectID = newObjectID,
							Name = !string.IsNullOrEmpty(_name) ? _name : _slug,
							Description = _description,
							ModelPath = _modelPath,
							PortraitModelPath = _portraitModelPath,
							VisualMode = _visualMode,
							Scale = _scale,
							YOffset = _yOffset,
							CollisionCircle = _collisionCircle,
							Brightness = _brightness,
							Tint = tintHex,
							NormalizeLuminance = _normalizeLuminance,
							IgnorePlayerColor = _ignorePlayerColor,
							DespillPlayerColor = _despillPlayerColor,
							PathingType = 255,
							MaxCapacity = 2000,
							HarvestRate = 10,
							MaxWorkers = 5,
							SpawnShader = string.IsNullOrWhiteSpace(_spawnShader) ? null : _spawnShader,
							DeathShader = string.IsNullOrWhiteSpace(_deathShader) ? null : _deathShader
						});
					}
					break;

				case "props":
					bool updatedP = meta.UpdateProp(_originalObjectID, p =>
					{
						p.ObjectID = newObjectID;
						p.Name = _name;
						p.Description = _description;
						p.ModelPath = _modelPath;
						p.PortraitModelPath = _portraitModelPath;
						p.VisualMode = _visualMode;
						p.Scale = _scale;
						p.YOffset = _yOffset;
						p.CollisionCircle = _collisionCircle;
						p.Brightness = _brightness;
						p.Tint = tintHex;
						p.NormalizeLuminance = _normalizeLuminance;
						p.IgnorePlayerColor = _ignorePlayerColor;
						p.DespillPlayerColor = _despillPlayerColor;
						p.SpawnShader = string.IsNullOrWhiteSpace(_spawnShader) ? null : _spawnShader;
						p.DeathShader = string.IsNullOrWhiteSpace(_deathShader) ? null : _deathShader;
						return p;
					});
					if (!updatedP)
					{
						meta.AddOrUpdateProp(new GameHost.PropMetadata
						{
							ObjectID = newObjectID,
							Name = !string.IsNullOrEmpty(_name) ? _name : _slug,
							Description = _description,
							ModelPath = _modelPath,
							PortraitModelPath = _portraitModelPath,
							VisualMode = _visualMode,
							Scale = _scale,
							YOffset = _yOffset,
							CollisionCircle = _collisionCircle,
							Brightness = _brightness,
							Tint = tintHex,
							NormalizeLuminance = _normalizeLuminance,
							IgnorePlayerColor = _ignorePlayerColor,
							DespillPlayerColor = _despillPlayerColor,
							PathingType = 255,
							SpawnShader = string.IsNullOrWhiteSpace(_spawnShader) ? null : _spawnShader,
							DeathShader = string.IsNullOrWhiteSpace(_deathShader) ? null : _deathShader
						});
					}
					break;
			}
		});

		GameHost.Instance?.LoadUnitMetadata(wsPath);
		Hud?.RefreshEntityPalette();
		_onAppliedCallback?.Invoke(_originalObjectID, newObjectID);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Saved visual properties for '{0}'"), newObjectID));
		CloseDialog();
	}
}
