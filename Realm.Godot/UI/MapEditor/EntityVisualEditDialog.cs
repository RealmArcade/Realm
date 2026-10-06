using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Realm.Godot.Services;
using Realm.Godot.Utils;
using Realm.Shared.Metadata;

public class EntityVisualEditUndoAction : IEditorAction
{
	private readonly string _assetKey;
	private readonly string _category;
	private readonly EntityVisualEditDialog.VisualOverridesSnapshot _before;
	private readonly EntityVisualEditDialog.VisualOverridesSnapshot _after;

	public EntityVisualEditUndoAction(string assetKey, string category, EntityVisualEditDialog.VisualOverridesSnapshot before, EntityVisualEditDialog.VisualOverridesSnapshot after)
	{
		_assetKey = assetKey;
		_category = category;
		_before = before;
		_after = after;
	}

	public void Undo()
	{
		ApplySnapshot(_before);
	}

	public void Redo()
	{
		ApplySnapshot(_after);
	}

	private void ApplySnapshot(EntityVisualEditDialog.VisualOverridesSnapshot snapshot)
	{
		if (GameHost.Instance == null || string.IsNullOrEmpty(_assetKey)) return;

		EntityVisualEditDialog.UpdateStaticRegistryModelAndVisualMode(_category, _assetKey, snapshot.ModelPath, snapshot.VisualMode);

		GameHost.Instance.SetModelScale(_assetKey, snapshot.Scale);
		GameHost.Instance.SetModelYOffset(_assetKey, snapshot.YOffset);
		GameHost.Instance.SetModelCollisionCircleRatio(_assetKey, snapshot.CollisionCircleRatio);
		GameHost.Instance.SetModelBrightness(_assetKey, snapshot.Brightness);
		GameHost.Instance.SetModelColorTint(_assetKey, snapshot.ColorTint);
		GameHost.Instance.SetModelNormalizeLuminance(_assetKey, snapshot.NormalizeLuminance);
		GameHost.Instance.SetModelIgnorePlayerColor(_assetKey, snapshot.IgnorePlayerColor);
		GameHost.Instance.SetModelDespillPlayerColor(_assetKey, snapshot.DespillPlayerColor);
		GameHost.Instance.SetModelSpawnShader(_assetKey, snapshot.SpawnShader);
		GameHost.Instance.SetModelDeathShader(_assetKey, snapshot.DeathShader);
		GameHost.Instance.SetModelEnableProceduralAnimation(_assetKey, snapshot.EnableProceduralAnimation);
		GameHost.Instance.SetModelProceduralAnimation(_assetKey, snapshot.ProceduralAnimation);

		Prop3D.InvalidateModelPathCache(_assetKey);
		ModelCache.InvalidateModelPath(_assetKey);
		if (!string.IsNullOrEmpty(snapshot.ModelPath))
		{
			Prop3D.InvalidateModelPathCache(snapshot.ModelPath);
			ModelCache.InvalidateModelPath(snapshot.ModelPath);
		}

		GameHost.Instance.RefreshAllPlacedObjectModels(_assetKey);
		GameHost.Instance.FlushModelYOffsetSave();
		GameHost.Instance.FlushModelCollisionCircleSave();
	}
}

public partial class EntityVisualEditDialog : FloatingDialogBase
{
	public struct VisualOverridesSnapshot
	{
		public string ModelPath;
		public string VisualMode;
		public float Scale;
		public float YOffset;
		public float CollisionCircleRatio;
		public float Brightness;
		public Color ColorTint;
		public bool NormalizeLuminance;
		public bool IgnorePlayerColor;
		public bool DespillPlayerColor;
		public string SpawnShader;
		public string DeathShader;
		public bool EnableProceduralAnimation;
		public string ProceduralAnimation;
	}

	private string _category = "units";
	private string _objectType = "unit";
	private string _slug = "";
	private string _originalTemplateID = "";
	private string _modelPath = "";
	private string _portraitModelPath = "";
	private string _visualMode = "GroundPlane";
	private string _name = "";
	private string _description = "";

	private float _scale = 1.0f;
	private float _yOffset = 0.0f;
	private float _collisionCircle = 1.0f;
	private float _brightness = 0.5f;
	private Color _tint = Colors.White;
	private bool _normalizeLuminance = true;
	private bool _ignorePlayerColor = false;
	private bool _despillPlayerColor = false;
	private string _spawnShader = "";
	private string _deathShader = "";
	private bool _enableProceduralAnimation = false;
	private string _proceduralAnimation = "";

	private bool _isSyncing = false;
	private Node _currentSelectedObject;
	private VisualOverridesSnapshot _initialSnapshot;
	private float _modelLocalMinY = 0f;
	private float _previousScale = 1.0f;
	private Action<string, string> _onAppliedCallback;

	private Label _lblDescription;
	private Label _lblIdentitySectionHeader;
	private HBoxContainer _idRow;
	private Label _lblObjectTypePrefix;
	private LineEdit _txtSlug;
	private Label _lblSlugValidation;
	private HBoxContainer _visualModeRow;
	private OptionButton _optVisualMode;
	private LineEdit _txtModelPath;
	private Action<string> _setModelPathValue;
	private LineEdit _txtPortraitModelPath;
	private Action<string> _setPortraitModelPathValue;
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
	private HSlider _sldTintHue;
	private CheckBox _chkNormalizeLuminance;
	private CheckBox _chkIgnorePlayerColor;
	private CheckBox _chkDespillPlayerColor;

	private OptionButton _optSpawnShader;
	private OptionButton _optDeathShader;
	private CheckBox _chkEnableProceduralAnim;
	private OptionButton _optProceduralAnim;
	private Button _btnOpenProcAnimStudio;

	public EntityVisualEditDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Visual Properties & Overrides"), new Vector2(520, 740))
	{
		BuildControls();
	}

	private void BuildControls()
	{
		_lblDescription = AddDescription(BodyContainer, TranslationServer.Translate("Modify visual, collision, and procedural motion settings for this entity or model."));

		var scrollBody = CreateScrollBody(600);
		var contentVBox = new VBoxContainer();
		contentVBox.AddThemeConstantOverride("separation", 10);
		contentVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scrollBody.AddChild(contentVBox);

		_lblIdentitySectionHeader = AddSectionHeader(contentVBox, "🆔 " + TranslationServer.Translate("IDENTITY & ASSET MODEL"), new Color(0.95f, 0.8f, 0.4f));

		_idRow = new HBoxContainer();
		_idRow.AddThemeConstantOverride("separation", 6);

		var lblId = new Label();
		lblId.Text = TranslationServer.Translate("TemplateID:");
		lblId.CustomMinimumSize = new Vector2(140, 0);
		lblId.AddThemeFontSizeOverride("font_size", 11);
		_idRow.AddChild(lblId);

		_lblObjectTypePrefix = new Label();
		_lblObjectTypePrefix.Text = "unit/";
		_lblObjectTypePrefix.AddThemeFontSizeOverride("font_size", 11);
		_lblObjectTypePrefix.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_idRow.AddChild(_lblObjectTypePrefix);

		_txtSlug = new LineEdit();
		_txtSlug.PlaceholderText = TranslationServer.Translate("snake_case_slug");
		_txtSlug.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_txtSlug.AddThemeFontSizeOverride("font_size", 11);
		_txtSlug.TextChanged += (val) =>
		{
			if (_isSyncing) return;
			_slug = TemplateIDHelper.ToSnakeCase(val);
			ValidateSlug();
		};
		_idRow.AddChild(_txtSlug);
		contentVBox.AddChild(_idRow);

		_lblSlugValidation = new Label();
		_lblSlugValidation.AddThemeFontSizeOverride("font_size", 10);
		_lblSlugValidation.AddThemeColorOverride("font_color", new Color(0.95f, 0.4f, 0.4f));
		_lblSlugValidation.Visible = false;
		contentVBox.AddChild(_lblSlugValidation);

		_visualModeRow = new HBoxContainer();
		_visualModeRow.AddThemeConstantOverride("separation", 8);

		var lblMode = new Label();
		lblMode.Text = TranslationServer.Translate("Visual Primitive:");
		lblMode.CustomMinimumSize = new Vector2(140, 0);
		lblMode.AddThemeFontSizeOverride("font_size", 11);
		_visualModeRow.AddChild(lblMode);

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
			ApplyLiveModelAndVisualMode();
		};
		_visualModeRow.AddChild(_optVisualMode);
		contentVBox.AddChild(_visualModeRow);

		(_txtModelPath, _setModelPathValue) = AddAssetFilterDropdown(
			contentVBox,
			TranslationServer.Translate("Visual Asset:"),
			_modelPath,
			(all) => ScanCompatibleModels(all),
			(val) =>
			{
				if (_isSyncing) return;
				_modelPath = val ?? string.Empty;
				ApplyLiveModelAndVisualMode();
			},
			TranslationServer.Translate("Select asset..."),
			140f
		);

		(_txtPortraitModelPath, _setPortraitModelPathValue) = AddAssetFilterDropdown(
			contentVBox,
			TranslationServer.Translate("Portrait Model:"),
			_portraitModelPath,
			(all) => ScanPortraitModels(all),
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

		AddSectionHeader(contentVBox, "📐 " + TranslationServer.Translate("TRANSFORM & VISUAL RENDERING"), new Color(0.5f, 0.85f, 1.0f));

		(_sldScale, _lblScale) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Scale:"),
			0.1f,
			10.0f,
			0.05f,
			_scale,
			(val) =>
			{
				if (_isSyncing) return;
				float newScale = val;
				float deltaScale = newScale - _previousScale;
				_previousScale = newScale;
				_scale = newScale;

				if (_modelLocalMinY != 0f)
				{
					float oldYOffset = (float)_sldYOffset.Value;
					float newYOffset = oldYOffset - deltaScale * _modelLocalMinY;

					_isSyncing = true;
					if (newYOffset < _sldYOffset.MinValue) _sldYOffset.MinValue = newYOffset - 5.0f;
					if (newYOffset > _sldYOffset.MaxValue) _sldYOffset.MaxValue = newYOffset + 5.0f;
					_sldYOffset.Value = newYOffset;
					_lblYOffset.Text = newYOffset.ToString("0.00");
					_yOffset = newYOffset;
					_isSyncing = false;

					if (GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
					{
						GameHost.Instance.SetModelYOffset(_originalTemplateID, newYOffset);
					}
				}

				if (GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
				{
					GameHost.Instance.SetModelScale(_originalTemplateID, newScale);
					GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
				}
			},
			"0.00x",
			140f
		);

		(_sldYOffset, _lblYOffset) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Y Ground Offset:"),
			-10.0f,
			10.0f,
			0.05f,
			_yOffset,
			(val) =>
			{
				if (_isSyncing) return;
				_yOffset = val;
				if (GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
				{
					GameHost.Instance.SetModelYOffset(_originalTemplateID, val);
					GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
				}
			},
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
			(val) =>
			{
				if (_isSyncing) return;
				_collisionCircle = val;
				if (GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
				{
					GameHost.Instance.SetModelCollisionCircleRatio(_originalTemplateID, val);
					GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
				}
			},
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
			(val) =>
			{
				if (_isSyncing) return;
				_brightness = val;
				if (GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
				{
					GameHost.Instance.SetModelBrightness(_originalTemplateID, val);
					GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
				}
			},
			"0.00x",
			140f
		);

		(_btnTint, _sldTintHue) = AddColorPicker(
			contentVBox,
			TranslationServer.Translate("Tint Color:"),
			_tint,
			(color) =>
			{
				if (_isSyncing) return;
				_tint = color;
				if (GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
				{
					GameHost.Instance.SetModelColorTint(_originalTemplateID, color);
					GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
				}
			},
			140f
		);

		_chkNormalizeLuminance = AddCheckBox(contentVBox, TranslationServer.Translate("Normalize Luminance"), _normalizeLuminance, (val) =>
		{
			if (_isSyncing) return;
			_normalizeLuminance = val;
			if (GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
			{
				GameHost.Instance.SetModelNormalizeLuminance(_originalTemplateID, val);
				GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
			}
		});

		_chkIgnorePlayerColor = AddCheckBox(contentVBox, TranslationServer.Translate("Ignore Player Color"), _ignorePlayerColor, (val) =>
		{
			if (_isSyncing) return;
			_ignorePlayerColor = val;
			if (GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
			{
				GameHost.Instance.SetModelIgnorePlayerColor(_originalTemplateID, val);
				GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
			}
		});

		_chkDespillPlayerColor = AddCheckBox(contentVBox, TranslationServer.Translate("Despill Player Color"), _despillPlayerColor, (val) =>
		{
			if (_isSyncing) return;
			_despillPlayerColor = val;
			if (GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
			{
				GameHost.Instance.SetModelDespillPlayerColor(_originalTemplateID, val);
				GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
			}
		});

		AddSectionHeader(contentVBox, "✨ " + TranslationServer.Translate("SPAWN, DEATH & PROCEDURAL SHADERS"), new Color(0.95f, 0.7f, 0.95f));

		var shaders = SpawnDeathShaderManager.LoadAllCustomShaders();
		var shaderOptions = new List<string> { TranslationServer.Translate("(None)") };
		foreach (var s in shaders.Values)
		{
			shaderOptions.Add(s.Name);
		}

		_optSpawnShader = AddOptionDropdown(contentVBox, TranslationServer.Translate("Spawn Shader:"), shaderOptions.ToArray(), 0, (idx) =>
		{
			if (_isSyncing) return;
			var currentShaders = SpawnDeathShaderManager.LoadAllCustomShaders();
			string selectedKey = idx > 0 && idx - 1 < currentShaders.Count ? currentShaders.ElementAt(idx - 1).Key : "";
			_spawnShader = selectedKey;
			if (GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
			{
				GameHost.Instance.SetModelSpawnShader(_originalTemplateID, selectedKey);
				GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
			}
		}, 140f);

		_optDeathShader = AddOptionDropdown(contentVBox, TranslationServer.Translate("Death Shader:"), shaderOptions.ToArray(), 0, (idx) =>
		{
			if (_isSyncing) return;
			var currentShaders = SpawnDeathShaderManager.LoadAllCustomShaders();
			string selectedKey = idx > 0 && idx - 1 < currentShaders.Count ? currentShaders.ElementAt(idx - 1).Key : "";
			_deathShader = selectedKey;
			if (GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
			{
				GameHost.Instance.SetModelDeathShader(_originalTemplateID, selectedKey);
				GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
			}
		}, 140f);

		_chkEnableProceduralAnim = AddCheckBox(contentVBox, TranslationServer.Translate("Enable Procedural Sway / Wind"), _enableProceduralAnimation, (pressed) =>
		{
			if (_isSyncing) return;
			_enableProceduralAnimation = pressed;
			if (GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
			{
				GameHost.Instance.SetModelEnableProceduralAnimation(_originalTemplateID, pressed);
				GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
			}
		});

		var animConfigs = ProceduralAnimationManager.LoadAllConfigs();
		var animOptions = new List<string> { TranslationServer.Translate("(None)") };
		foreach (var a in animConfigs.Values)
		{
			animOptions.Add(a.Name);
		}

		_optProceduralAnim = AddOptionDropdown(contentVBox, TranslationServer.Translate("Procedural Motion Profile:"), animOptions.ToArray(), 0, (idx) =>
		{
			if (_isSyncing) return;
			var currentConfigs = ProceduralAnimationManager.LoadAllConfigs();
			string selectedKey = idx > 0 && idx - 1 < currentConfigs.Count ? currentConfigs.ElementAt(idx - 1).Key : "";
			_proceduralAnimation = selectedKey;
			if (GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
			{
				GameHost.Instance.SetModelProceduralAnimation(_originalTemplateID, selectedKey);
				GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
			}
		}, 140f);

		_btnOpenProcAnimStudio = AddButton(contentVBox, "✨ " + TranslationServer.Translate("Procedural Animation Studio..."), () =>
		{
			var currentConfigs = ProceduralAnimationManager.LoadAllConfigs();
			string selectedKey = "";
			if (_optProceduralAnim != null && _optProceduralAnim.Selected > 0 && _optProceduralAnim.Selected - 1 < currentConfigs.Count)
			{
				selectedKey = currentConfigs.ElementAt(_optProceduralAnim.Selected - 1).Key;
			}
			if (string.IsNullOrEmpty(selectedKey))
			{
				selectedKey = _proceduralAnimation;
			}
			if (string.IsNullOrEmpty(selectedKey) && GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
			{
				selectedKey = GameHost.Instance.GetModelProceduralAnimation(_originalTemplateID) ?? "";
			}

			string targetKey = !string.IsNullOrEmpty(_originalTemplateID) ? _originalTemplateID : (!string.IsNullOrEmpty(_slug) ? $"{_objectType}/{_slug}" : "object");
			var cfg = ProceduralAnimationManager.GetConfig(selectedKey) ?? new ProceduralAnimationConfig { Id = targetKey + "_anim", Name = targetKey + " Animation" };
			string selectedMesh = !string.IsNullOrEmpty(_modelPath) ? _modelPath : (GameHost.Instance?.GetModelAssetKey(_currentSelectedObject ?? (object)targetKey) ?? targetKey);

			Hud?.OpenProceduralAnimationStudioDialog(cfg, (savedCfg) =>
			{
				if (savedCfg != null)
				{
					_proceduralAnimation = savedCfg.Id;
					_enableProceduralAnimation = true;
					if (_chkEnableProceduralAnim != null) _chkEnableProceduralAnim.ButtonPressed = true;

					if (GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
					{
						GameHost.Instance.SetModelProceduralAnimation(_originalTemplateID, savedCfg.Id);
						GameHost.Instance.SetModelEnableProceduralAnimation(_originalTemplateID, true);
						GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
					}
					RefreshProceduralAnimationDropdown(savedCfg.Id);
				}
			}, selectedMesh);
		}, "Open Procedural Animation Studio to edit math formulas and motion parameters", 11, new Vector2(0, 28));
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

		bool isMesh = string.Equals(_visualMode, "Mesh", StringComparison.OrdinalIgnoreCase);

		if (isMesh)
		{
			try
			{
				var assets = MapAssetHelper.LoadAssets(wsPath);
				if (assets != null)
				{
					foreach (var groupKvp in assets.GetAllCategories())
					{
						string groupName = groupKvp.Key;
						bool groupMatches = allFolders;
						if (!groupMatches)
						{
							groupMatches = _category switch
							{
								"units" => groupName.Equals("Character", StringComparison.OrdinalIgnoreCase) || groupName.Equals("units", StringComparison.OrdinalIgnoreCase) || groupName.Equals("characters", StringComparison.OrdinalIgnoreCase),
								"buildings" => groupName.Equals("Building", StringComparison.OrdinalIgnoreCase) || groupName.Equals("buildings", StringComparison.OrdinalIgnoreCase),
								"resources" => groupName.Equals("Resource", StringComparison.OrdinalIgnoreCase) || groupName.Equals("resources", StringComparison.OrdinalIgnoreCase) || groupName.Equals("Prop", StringComparison.OrdinalIgnoreCase) || groupName.Equals("props", StringComparison.OrdinalIgnoreCase),
								"props" => groupName.Equals("Prop", StringComparison.OrdinalIgnoreCase) || groupName.Equals("props", StringComparison.OrdinalIgnoreCase) || groupName.Equals("Item", StringComparison.OrdinalIgnoreCase) || groupName.Equals("items", StringComparison.OrdinalIgnoreCase) || groupName.Equals("Resource", StringComparison.OrdinalIgnoreCase) || groupName.Equals("resources", StringComparison.OrdinalIgnoreCase),
								_ => true
							};
						}

						if (allFolders || groupMatches)
						{
							foreach (var itemKvp in groupKvp.Value)
							{
								string fn = itemKvp.Key;
								if (fn.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
								{
									results.Add(Path.GetFileName(fn));
								}
							}
						}
					}
				}
			}
			catch { }

			string[] modelSubFolders = _category switch
			{
				"units" => allFolders ? new[] { "units", "buildings", "props", "resources", "items", "projectiles", "attachments" } : new[] { "units" },
				"buildings" => allFolders ? new[] { "units", "buildings", "props", "resources", "items", "projectiles", "attachments" } : new[] { "buildings" },
				"resources" => allFolders ? new[] { "units", "buildings", "props", "resources", "items", "projectiles", "attachments" } : new[] { "resources", "props" },
				"props" => allFolders ? new[] { "units", "buildings", "props", "resources", "items", "projectiles", "attachments" } : new[] { "props", "resources", "items" },
				_ => new[] { "units", "buildings", "props", "resources", "items", "projectiles", "attachments" }
			};

			string[] baseLocations = new[]
			{
				Path.Combine(wsPath, "Assets", "models"),
				Path.Combine("MapTemplate", "Assets", "models"),
				Path.Combine(ProjectSettings.GlobalizePath("res://"), "Assets", "models"),
				Path.Combine(ProjectSettings.GlobalizePath("res://"), "MapTemplate", "Assets", "models")
			};

			foreach (var baseLoc in baseLocations)
			{
				if (Directory.Exists(baseLoc))
				{
					foreach (var sub in modelSubFolders)
					{
						string subDir = Path.Combine(baseLoc, sub);
						if (Directory.Exists(subDir))
						{
							foreach (var file in Directory.GetFiles(subDir, "*.rmesh", SearchOption.AllDirectories))
							{
								results.Add(Path.GetFileName(file));
							}
						}
					}

					foreach (var file in Directory.GetFiles(baseLoc, "*.rmesh", SearchOption.TopDirectoryOnly))
					{
						results.Add(Path.GetFileName(file));
					}
				}
			}

			if (results.Count == 0)
			{
				var fallbackModels = ScanAvailableAssets("models", allFolders);
				foreach (var f in fallbackModels)
				{
					if (f.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
					{
						results.Add(Path.GetFileName(f));
					}
				}
			}
		}
		else
		{
			try
			{
				var assets = MapAssetHelper.LoadAssets(wsPath);
				if (assets != null)
				{
					foreach (var groupKvp in assets.GetAllCategories())
					{
						foreach (var itemKvp in groupKvp.Value)
						{
							string fn = itemKvp.Key;
							if (fn.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
							{
								results.Add(Path.GetFileName(fn));
							}
						}
					}
				}
			}
			catch { }

			string[] candidateFolders = new[]
			{
				Path.Combine(wsPath, "Assets", "decals"),
				Path.Combine(wsPath, "Assets", "textures"),
				Path.Combine("MapTemplate", "Assets", "decals"),
				Path.Combine("MapTemplate", "Assets", "textures"),
				Path.Combine(ProjectSettings.GlobalizePath("res://"), "Assets", "decals"),
				Path.Combine(ProjectSettings.GlobalizePath("res://"), "Assets", "textures"),
				Path.Combine(ProjectSettings.GlobalizePath("res://"), "MapTemplate", "Assets", "decals"),
				Path.Combine(ProjectSettings.GlobalizePath("res://"), "MapTemplate", "Assets", "textures")
			};

			foreach (var folder in candidateFolders)
			{
				if (Directory.Exists(folder))
				{
					foreach (var file in Directory.GetFiles(folder, "*.rtex", SearchOption.AllDirectories))
					{
						results.Add(Path.GetFileName(file));
					}
				}
			}
		}

		return results.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private List<string> ScanPortraitModels(bool allFolders)
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		try
		{
			var assets = MapAssetHelper.LoadAssets(wsPath);
			if (assets != null)
			{
				foreach (var groupKvp in assets.GetAllCategories())
				{
					foreach (var itemKvp in groupKvp.Value)
					{
						string fn = itemKvp.Key;
						if (fn.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
						{
							results.Add(Path.GetFileName(fn));
						}
					}
				}
			}
		}
		catch { }

		string[] baseLocations = new[]
		{
			Path.Combine(wsPath, "Assets", "models"),
			Path.Combine("MapTemplate", "Assets", "models"),
			Path.Combine(ProjectSettings.GlobalizePath("res://"), "Assets", "models"),
			Path.Combine(ProjectSettings.GlobalizePath("res://"), "MapTemplate", "Assets", "models")
		};

		foreach (var baseLoc in baseLocations)
		{
			if (Directory.Exists(baseLoc))
			{
				foreach (var file in Directory.GetFiles(baseLoc, "*.rmesh", SearchOption.AllDirectories))
				{
					results.Add(Path.GetFileName(file));
				}
			}
		}

		if (results.Count == 0)
		{
			var fallbackModels = ScanAvailableAssets("models", allFolders);
			foreach (var f in fallbackModels)
			{
				if (f.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
				{
					results.Add(Path.GetFileName(f));
				}
			}
		}

		return results.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
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

		if (!fullId.Equals(_originalTemplateID, StringComparison.OrdinalIgnoreCase))
		{
			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
			if (MetadataService.Instance.TryLoadMetadata(wsPath, out var meta) && meta != null)
			{
				bool exists = _category switch
				{
					"units" => meta.GetUnit(fullId) != null,
					"buildings" => meta.GetBuilding(fullId) != null,
					"resources" => meta.GetResource(fullId) != null,
					"props" => meta.GetProp(fullId) != null,
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

	private static (string Category, string ObjectId) ResolveObjectCategoryAndId(Node selectedObject)
	{
		if (selectedObject == null || !GodotObject.IsInstanceValid(selectedObject))
			return ("units", string.Empty);

		string objectId = GameHost.Instance?.GetSelectedEntityOrAssetKey(selectedObject) ?? string.Empty;

		if (selectedObject is Unit3D unit)
		{
			string cat = unit.IsBuilding ? "buildings" : "units";
			string id = !string.IsNullOrEmpty(unit.UnitId) ? unit.UnitId : objectId;
			return (cat, id);
		}

		if (selectedObject is Prop3D prop)
		{
			string id = !string.IsNullOrEmpty(prop.PropId) ? prop.PropId : objectId;
			string cat = (id.StartsWith("resource/", StringComparison.OrdinalIgnoreCase) ||
				(GameHost.ResourceRegistry != null && GameHost.ResourceRegistry.ContainsKey(id)))
				? "resources"
				: "props";
			return (cat, id);
		}

		if (!string.IsNullOrEmpty(objectId))
		{
			if (objectId.StartsWith("unit/", StringComparison.OrdinalIgnoreCase)) return ("units", objectId);
			if (objectId.StartsWith("building/", StringComparison.OrdinalIgnoreCase)) return ("buildings", objectId);
			if (objectId.StartsWith("resource/", StringComparison.OrdinalIgnoreCase)) return ("resources", objectId);
			if (objectId.StartsWith("prop/", StringComparison.OrdinalIgnoreCase)) return ("props", objectId);

			if (GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.ContainsKey(objectId)) return ("buildings", objectId);
			if (GameHost.UnitRegistry != null && GameHost.UnitRegistry.ContainsKey(objectId)) return ("units", objectId);
			if (GameHost.ResourceRegistry != null && GameHost.ResourceRegistry.ContainsKey(objectId)) return ("resources", objectId);
			if (GameHost.PropRegistry != null && GameHost.PropRegistry.ContainsKey(objectId)) return ("props", objectId);
		}

		return ("props", objectId);
	}

	public static void UpdateStaticRegistryModelAndVisualMode(string category, string key, string modelPath, string visualMode)
	{
		if (string.IsNullOrEmpty(key)) return;
		StringName sn = (StringName)key;

		switch (category)
		{
			case "units":
				if (GameHost.UnitRegistry.TryGetValue(sn, out var u))
				{
					u.ModelPath = modelPath;
					u.VisualMode = visualMode;
					GameHost.UnitRegistry[sn] = u;
				}
				else
				{
					GameHost.UnitRegistry[sn] = new UnitMetadata
					{
						TemplateID = key,
						ModelPath = modelPath,
						VisualMode = visualMode
					};
				}
				break;

			case "buildings":
				if (GameHost.BuildingRegistry.TryGetValue(sn, out var b))
				{
					b.ModelPath = modelPath;
					b.VisualMode = visualMode;
					GameHost.BuildingRegistry[sn] = b;
				}
				else
				{
					GameHost.BuildingRegistry[sn] = new UnitMetadata
					{
						TemplateID = key,
						ModelPath = modelPath,
						VisualMode = visualMode
					};
				}
				break;

			case "resources":
				if (GameHost.ResourceRegistry.TryGetValue(sn, out var r))
				{
					r.ModelPath = modelPath;
					r.VisualMode = visualMode;
					GameHost.ResourceRegistry[sn] = r;
				}
				else
				{
					GameHost.ResourceRegistry[sn] = new ResourceMetadata
					{
						TemplateID = key,
						ModelPath = modelPath,
						VisualMode = visualMode
					};
				}
				break;

			case "props":
				if (GameHost.PropRegistry.TryGetValue(sn, out var p))
				{
					p.ModelPath = modelPath;
					p.VisualMode = visualMode;
					GameHost.PropRegistry[sn] = p;
				}
				else
				{
					GameHost.PropRegistry[sn] = new PropMetadata
					{
						TemplateID = key,
						ModelPath = modelPath,
						VisualMode = visualMode
					};
				}
				break;
		}
	}

	private void ApplyLiveModelAndVisualMode()
	{
		if (GameHost.Instance == null || string.IsNullOrEmpty(_originalTemplateID)) return;

		UpdateStaticRegistryModelAndVisualMode(_category, _originalTemplateID, _modelPath, _visualMode);

		string cleanId = Path.GetFileNameWithoutExtension(_originalTemplateID);
		if (!string.IsNullOrEmpty(cleanId) && !cleanId.Equals(_originalTemplateID, StringComparison.OrdinalIgnoreCase))
		{
			UpdateStaticRegistryModelAndVisualMode(_category, cleanId, _modelPath, _visualMode);
		}

		if (!string.IsNullOrEmpty(_slug))
		{
			UpdateStaticRegistryModelAndVisualMode(_category, $"{_objectType}/{_slug}", _modelPath, _visualMode);
			UpdateStaticRegistryModelAndVisualMode(_category, _slug, _modelPath, _visualMode);
		}

		if (_currentSelectedObject is Unit3D unit && !string.IsNullOrEmpty(unit.UnitId))
		{
			UpdateStaticRegistryModelAndVisualMode(_category, unit.UnitId, _modelPath, _visualMode);
		}
		else if (_currentSelectedObject is Prop3D prop && !string.IsNullOrEmpty(prop.PropId))
		{
			UpdateStaticRegistryModelAndVisualMode(_category, prop.PropId, _modelPath, _visualMode);
		}

		Prop3D.InvalidateModelPathCache(_originalTemplateID);
		ModelCache.InvalidateModelPath(_originalTemplateID);
		if (!string.IsNullOrEmpty(_modelPath))
		{
			Prop3D.InvalidateModelPathCache(_modelPath);
			ModelCache.InvalidateModelPath(_modelPath);
		}

		GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);

		if (!string.IsNullOrEmpty(_slug))
		{
			GameHost.Instance.RefreshAllPlacedObjectModels($"{_objectType}/{_slug}");
		}

		if (_currentSelectedObject != null && GodotObject.IsInstanceValid(_currentSelectedObject))
		{
			if (_currentSelectedObject is Unit3D u3d)
			{
				string fallbackModel = GameHost.Instance.GetFallbackModelPath(_modelPath, u3d.IsBuilding);
				u3d.LoadModel(fallbackModel);
			}
			else if (_currentSelectedObject is Prop3D p3d)
			{
				p3d.RefreshPropVisual();
			}
			_modelLocalMinY = CalculateModelLocalMinY(_currentSelectedObject);
		}
	}

	public void OpenForObject(Node selectedObject)
	{
		if (selectedObject == null || !GodotObject.IsInstanceValid(selectedObject) || GameHost.Instance == null) return;
		var (category, objectId) = ResolveObjectCategoryAndId(selectedObject);
		if (string.IsNullOrEmpty(objectId)) return;
		OpenForObject(category, objectId, null, selectedObject);
	}

	public void OpenForObject(string category, string objectId, Action<string, string> onApplied = null)
	{
		OpenForObject(category, objectId, onApplied, null);
	}

	public void OpenForObject(string category, string objectId, Action<string, string> onApplied, Node selectedObject)
	{
		_currentSelectedObject = selectedObject;
		_modelLocalMinY = selectedObject != null ? CalculateModelLocalMinY(selectedObject) : (GameHost.Instance?.SelectedEditorObject != null ? CalculateModelLocalMinY(GameHost.Instance.SelectedEditorObject) : 0f);

		_category = (category ?? "units").ToLowerInvariant();
		_objectType = _category switch
		{
			"units" => "unit",
			"buildings" => "building",
			"resources" => "resource",
			"props" => "prop",
			"items" => "item",
			_ => "unit"
		};

		_originalTemplateID = objectId ?? "";
		var (parsedType, parsedSlug) = TemplateIDHelper.ParseTemplateID(objectId);
		_slug = !string.IsNullOrEmpty(parsedSlug) ? parsedSlug : TemplateIDHelper.GenerateSlug(objectId);
		_onAppliedCallback = onApplied;

		object targetLookup = (object)selectedObject ?? (object)_originalTemplateID;

		_scale = GameHost.Instance != null ? GameHost.Instance.GetModelScale(targetLookup) : 1.0f;
		_yOffset = GameHost.Instance != null ? GameHost.Instance.GetModelYOffset(targetLookup) : 0.0f;
		_collisionCircle = GameHost.Instance != null ? GameHost.Instance.GetModelCollisionCircleRatio(targetLookup) : 1.0f;
		_brightness = GameHost.Instance != null ? GameHost.Instance.GetModelBrightness(targetLookup) : 0.5f;
		_tint = GameHost.Instance != null ? GameHost.Instance.GetModelColorTint(targetLookup) : Colors.White;
		_normalizeLuminance = GameHost.Instance != null ? GameHost.Instance.GetModelNormalizeLuminance(targetLookup) : true;
		_ignorePlayerColor = GameHost.Instance != null ? GameHost.Instance.GetModelIgnorePlayerColor(targetLookup) : false;
		_despillPlayerColor = GameHost.Instance != null ? GameHost.Instance.GetModelDespillPlayerColor(targetLookup) : false;
		_spawnShader = GameHost.Instance != null ? (GameHost.Instance.GetModelSpawnShader(targetLookup) ?? "") : "";
		_deathShader = GameHost.Instance != null ? (GameHost.Instance.GetModelDeathShader(targetLookup) ?? "") : "";
		_enableProceduralAnimation = GameHost.Instance != null && GameHost.Instance.GetModelEnableProceduralAnimation(targetLookup);
		_proceduralAnimation = GameHost.Instance != null ? (GameHost.Instance.GetModelProceduralAnimation(targetLookup) ?? "") : "";
		_modelPath = GameHost.Instance != null ? (GameHost.Instance.GetModelAssetKey(targetLookup) ?? "") : "";
		_portraitModelPath = "";
		_visualMode = (_modelPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) || _modelPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || _modelPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) ? "GroundPlane" : "Mesh";
		_name = !string.IsNullOrEmpty(parsedSlug) ? parsedSlug.Replace("_", " ") : _originalTemplateID;
		_description = "";

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		if (MetadataService.Instance.TryLoadMetadata(wsPath, out var meta) && meta != null)
		{
			switch (_category)
			{
				case "units":
					var u = meta.GetUnit(objectId);
					if (u != null) LoadFromUnitMetadata(u);
					break;
				case "buildings":
					var b = meta.GetBuilding(objectId);
					if (b != null) LoadFromUnitMetadata(b);
					break;
				case "resources":
					var r = meta.GetResource(objectId);
					if (r != null) LoadFromResourceMetadata(r);
					break;
				case "props":
					var p = meta.GetProp(objectId);
					if (p != null) LoadFromPropMetadata(p);
					break;
			}
		}

		_previousScale = _scale;

		_initialSnapshot = new VisualOverridesSnapshot
		{
			ModelPath = _modelPath,
			VisualMode = _visualMode,
			Scale = _scale,
			YOffset = _yOffset,
			CollisionCircleRatio = _collisionCircle,
			Brightness = _brightness,
			ColorTint = _tint,
			NormalizeLuminance = _normalizeLuminance,
			IgnorePlayerColor = _ignorePlayerColor,
			DespillPlayerColor = _despillPlayerColor,
			SpawnShader = _spawnShader,
			DeathShader = _deathShader,
			EnableProceduralAnimation = _enableProceduralAnimation,
			ProceduralAnimation = _proceduralAnimation
		};

		TitleLabel.Text = $"{TranslationServer.Translate("Visual Properties & Overrides")} - {_originalTemplateID}";

		SyncControls();
		OpenDialog();
	}

	private void LoadFromUnitMetadata(UnitMetadata u)
	{
		_modelPath = u.ModelPath ?? _modelPath;
		_portraitModelPath = u.PortraitModelPath ?? _portraitModelPath;
		_visualMode = !string.IsNullOrEmpty(u.VisualMode) ? u.VisualMode : _visualMode;
		_name = u.Name ?? _name;
		_description = u.Description ?? _description;
		_scale = u.Scale > 0 ? u.Scale : _scale;
		_yOffset = u.YOffset;
		_collisionCircle = u.CollisionCircle > 0 ? u.CollisionCircle : _collisionCircle;
		_brightness = u.Brightness;
		_tint = !string.IsNullOrEmpty(u.Tint) && u.Tint.StartsWith("#") ? Color.FromHtml(u.Tint) : _tint;
		_normalizeLuminance = u.NormalizeLuminance;
		_ignorePlayerColor = u.IgnorePlayerColor;
		_despillPlayerColor = u.DespillPlayerColor;
		if (!string.IsNullOrEmpty(u.SpawnShader)) _spawnShader = u.SpawnShader;
		if (!string.IsNullOrEmpty(u.DeathShader)) _deathShader = u.DeathShader;

		string normKey = NormalizeModelAssetKey(u.TemplateID);
		string normModel = !string.IsNullOrEmpty(u.ModelPath) ? NormalizeModelAssetKey(u.ModelPath) : "";
		if (GameHost.Instance != null && (!string.IsNullOrEmpty(normKey) || !string.IsNullOrEmpty(normModel)))
		{
			string procAnim = GameHost.Instance.GetModelProceduralAnimation(u.TemplateID);
			if (!string.IsNullOrEmpty(procAnim)) _proceduralAnimation = procAnim;
			_enableProceduralAnimation = GameHost.Instance.GetModelEnableProceduralAnimation(u.TemplateID);
		}
	}

	private void LoadFromResourceMetadata(ResourceMetadata r)
	{
		_modelPath = r.ModelPath ?? _modelPath;
		_portraitModelPath = r.PortraitModelPath ?? _portraitModelPath;
		_visualMode = !string.IsNullOrEmpty(r.VisualMode) ? r.VisualMode : _visualMode;
		_name = r.Name ?? _name;
		_description = r.Description ?? _description;
		_scale = r.Scale > 0 ? r.Scale : _scale;
		_yOffset = r.YOffset;
		_collisionCircle = r.CollisionCircle > 0 ? r.CollisionCircle : _collisionCircle;
		_brightness = r.Brightness;
		_tint = !string.IsNullOrEmpty(r.Tint) && r.Tint.StartsWith("#") ? Color.FromHtml(r.Tint) : _tint;
		_normalizeLuminance = r.NormalizeLuminance;
		_ignorePlayerColor = r.IgnorePlayerColor;
		_despillPlayerColor = r.DespillPlayerColor;
		if (!string.IsNullOrEmpty(r.SpawnShader)) _spawnShader = r.SpawnShader;
		if (!string.IsNullOrEmpty(r.DeathShader)) _deathShader = r.DeathShader;

		string normKey = NormalizeModelAssetKey(r.TemplateID);
		string normModel = !string.IsNullOrEmpty(r.ModelPath) ? NormalizeModelAssetKey(r.ModelPath) : "";
		if (GameHost.Instance != null && (!string.IsNullOrEmpty(normKey) || !string.IsNullOrEmpty(normModel)))
		{
			string procAnim = GameHost.Instance.GetModelProceduralAnimation(r.TemplateID);
			if (!string.IsNullOrEmpty(procAnim)) _proceduralAnimation = procAnim;
			_enableProceduralAnimation = GameHost.Instance.GetModelEnableProceduralAnimation(r.TemplateID);
		}
	}

	private void LoadFromPropMetadata(PropMetadata p)
	{
		_modelPath = p.ModelPath ?? _modelPath;
		_portraitModelPath = p.PortraitModelPath ?? _portraitModelPath;
		_visualMode = !string.IsNullOrEmpty(p.VisualMode) ? p.VisualMode : _visualMode;
		_name = p.Name ?? _name;
		_description = p.Description ?? _description;
		_scale = p.Scale > 0 ? p.Scale : _scale;
		_yOffset = p.YOffset;
		_collisionCircle = p.CollisionCircle > 0 ? p.CollisionCircle : _collisionCircle;
		_brightness = p.Brightness;
		_tint = !string.IsNullOrEmpty(p.Tint) && p.Tint.StartsWith("#") ? Color.FromHtml(p.Tint) : _tint;
		_normalizeLuminance = p.NormalizeLuminance;
		_ignorePlayerColor = p.IgnorePlayerColor;
		_despillPlayerColor = p.DespillPlayerColor;
		if (!string.IsNullOrEmpty(p.SpawnShader)) _spawnShader = p.SpawnShader;
		if (!string.IsNullOrEmpty(p.DeathShader)) _deathShader = p.DeathShader;

		string normKey = NormalizeModelAssetKey(p.TemplateID);
		string normModel = !string.IsNullOrEmpty(p.ModelPath) ? NormalizeModelAssetKey(p.ModelPath) : "";
		if (GameHost.Instance != null && (!string.IsNullOrEmpty(normKey) || !string.IsNullOrEmpty(normModel)))
		{
			string procAnim = GameHost.Instance.GetModelProceduralAnimation(p.TemplateID);
			if (!string.IsNullOrEmpty(procAnim)) _proceduralAnimation = procAnim;
			_enableProceduralAnimation = GameHost.Instance.GetModelEnableProceduralAnimation(p.TemplateID);
		}
	}

	private static string NormalizeModelAssetKey(string key)
	{
		if (string.IsNullOrEmpty(key)) return "";
		string trimmed = key.Trim();
		if (trimmed.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
			trimmed = trimmed.Substring(6);
		return trimmed.Replace("\\", "/").TrimStart('/');
	}

	private void SyncControls()
	{
		_isSyncing = true;
		try
		{
			if (_lblObjectTypePrefix != null) _lblObjectTypePrefix.Text = $"{_objectType}/";
			if (_txtSlug != null) _txtSlug.Text = _slug;
			SyncVisualModeControl();
			_setModelPathValue?.Invoke(_modelPath);
			_setPortraitModelPathValue?.Invoke(_portraitModelPath);
			if (_txtName != null) _txtName.Text = _name;
			if (_txtDescription != null) _txtDescription.Text = _description;

			if (_sldScale != null)
			{
				if (_scale < _sldScale.MinValue) _sldScale.MinValue = _scale;
				if (_scale > _sldScale.MaxValue) _sldScale.MaxValue = _scale;
				_sldScale.Value = _scale;
				_lblScale.Text = $"{_scale:F2}x";
			}

			if (_sldYOffset != null)
			{
				if (_yOffset < _sldYOffset.MinValue) _sldYOffset.MinValue = _yOffset - 5.0f;
				if (_yOffset > _sldYOffset.MaxValue) _sldYOffset.MaxValue = _yOffset + 5.0f;
				_sldYOffset.Value = _yOffset;
				_lblYOffset.Text = $"{_yOffset:F2}";
			}

			if (_sldCollisionCircle != null)
			{
				if (_collisionCircle < _sldCollisionCircle.MinValue) _sldCollisionCircle.MinValue = _collisionCircle;
				if (_collisionCircle > _sldCollisionCircle.MaxValue) _sldCollisionCircle.MaxValue = _collisionCircle;
				_sldCollisionCircle.Value = _collisionCircle;
				_lblCollisionCircle.Text = $"{_collisionCircle:F2}";
			}

			if (_sldBrightness != null)
			{
				_sldBrightness.Value = _brightness;
				_lblBrightness.Text = $"{_brightness:F2}x";
			}

			if (_btnTint != null) _btnTint.Color = _tint;
			if (_sldTintHue != null)
			{
				if (Mathf.Abs(_tint.R - 1.0f) < 0.001f && Mathf.Abs(_tint.G - 1.0f) < 0.001f && Mathf.Abs(_tint.B - 1.0f) < 0.001f)
				{
					_sldTintHue.Value = 0.0f;
				}
				else
				{
					_sldTintHue.Value = _tint.H;
				}
			}

			if (_chkNormalizeLuminance != null) _chkNormalizeLuminance.ButtonPressed = _normalizeLuminance;
			if (_chkIgnorePlayerColor != null) _chkIgnorePlayerColor.ButtonPressed = _ignorePlayerColor;
			if (_chkDespillPlayerColor != null) _chkDespillPlayerColor.ButtonPressed = _despillPlayerColor;
			if (_chkEnableProceduralAnim != null) _chkEnableProceduralAnim.ButtonPressed = _enableProceduralAnimation;

			var allShaders = SpawnDeathShaderManager.LoadAllCustomShaders();
			if (_optSpawnShader != null)
			{
				_optSpawnShader.Clear();
				_optSpawnShader.AddItem(TranslationServer.Translate("(None)"));
				foreach (var s in allShaders.Values)
				{
					_optSpawnShader.AddItem(s.Name);
				}
			}

			if (_optDeathShader != null)
			{
				_optDeathShader.Clear();
				_optDeathShader.AddItem(TranslationServer.Translate("(None)"));
				foreach (var s in allShaders.Values)
				{
					_optDeathShader.AddItem(s.Name);
				}
			}

			int spawnIdx = 0;
			int deathIdx = 0;
			int sIdx = 1;
			foreach (var s in allShaders)
			{
				if (string.Equals(s.Key, _spawnShader, StringComparison.OrdinalIgnoreCase))
				{
					spawnIdx = sIdx;
				}
				if (string.Equals(s.Key, _deathShader, StringComparison.OrdinalIgnoreCase))
				{
					deathIdx = sIdx;
				}
				sIdx++;
			}
			if (_optSpawnShader != null) _optSpawnShader.Selected = spawnIdx;
			if (_optDeathShader != null) _optDeathShader.Selected = deathIdx;

			RefreshProceduralAnimationDropdown(_proceduralAnimation);
			ValidateSlug();
		}
		finally
		{
			_isSyncing = false;
		}
	}

	private void RefreshProceduralAnimationDropdown(string selectedKey)
	{
		if (_optProceduralAnim == null) return;
		_optProceduralAnim.Clear();
		_optProceduralAnim.AddItem(TranslationServer.Translate("(None)"));

		var animConfigs = ProceduralAnimationManager.LoadAllConfigs();
		int selectedIdx = 0;
		int idx = 1;
		foreach (var a in animConfigs)
		{
			_optProceduralAnim.AddItem(a.Value.Name);
			if (string.Equals(a.Key, selectedKey, StringComparison.OrdinalIgnoreCase))
			{
				selectedIdx = idx;
			}
			idx++;
		}

		_optProceduralAnim.Selected = selectedIdx;
	}

	protected override void OnApply()
	{
		if (string.IsNullOrWhiteSpace(_slug))
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Cannot save entity: Slug is required."));
			return;
		}

		string newTemplateID = $"{_objectType}/{_slug}";
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();

		var allShaders = SpawnDeathShaderManager.LoadAllCustomShaders();
		string spawnKey = "";
		if (_optSpawnShader != null && _optSpawnShader.Selected > 0 && _optSpawnShader.Selected - 1 < allShaders.Count)
		{
			spawnKey = allShaders.ElementAt(_optSpawnShader.Selected - 1).Key;
		}
		string deathKey = "";
		if (_optDeathShader != null && _optDeathShader.Selected > 0 && _optDeathShader.Selected - 1 < allShaders.Count)
		{
			deathKey = allShaders.ElementAt(_optDeathShader.Selected - 1).Key;
		}

		var animConfigs = ProceduralAnimationManager.LoadAllConfigs();
		string animKey = "";
		if (_optProceduralAnim != null && _optProceduralAnim.Selected > 0 && _optProceduralAnim.Selected - 1 < animConfigs.Count)
		{
			animKey = animConfigs.ElementAt(_optProceduralAnim.Selected - 1).Key;
		}

		var currentSnapshot = new VisualOverridesSnapshot
		{
			ModelPath = _modelPath,
			VisualMode = _visualMode,
			Scale = (float)_sldScale.Value,
			YOffset = (float)_sldYOffset.Value,
			CollisionCircleRatio = (float)_sldCollisionCircle.Value,
			Brightness = (float)_sldBrightness.Value,
			ColorTint = _btnTint.Color,
			NormalizeLuminance = _chkNormalizeLuminance.ButtonPressed,
			IgnorePlayerColor = _chkIgnorePlayerColor.ButtonPressed,
			DespillPlayerColor = _chkDespillPlayerColor.ButtonPressed,
			SpawnShader = spawnKey,
			DeathShader = deathKey,
			EnableProceduralAnimation = _chkEnableProceduralAnim != null && _chkEnableProceduralAnim.ButtonPressed,
			ProceduralAnimation = animKey
		};

		MetadataService.Instance.UpdateMetadata(wsPath, meta =>
		{
			string tintHex = $"#{_tint.ToHtml(false)}";

			switch (_category)
			{
				case "units":
					bool updatedU = meta.UpdateUnit(_originalTemplateID, u =>
					{
						u.TemplateID = newTemplateID;
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
						u.SpawnShader = string.IsNullOrWhiteSpace(spawnKey) ? null : spawnKey;
						u.DeathShader = string.IsNullOrWhiteSpace(deathKey) ? null : deathKey;
						return u;
					});
					if (!updatedU)
					{
						meta.AddOrUpdateUnit(new UnitMetadata
						{
							TemplateID = newTemplateID,
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
							SpawnShader = string.IsNullOrWhiteSpace(spawnKey) ? null : spawnKey,
							DeathShader = string.IsNullOrWhiteSpace(deathKey) ? null : deathKey
						});
					}
					break;

				case "buildings":
					bool updatedB = meta.UpdateBuilding(_originalTemplateID, b =>
					{
						b.TemplateID = newTemplateID;
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
						b.SpawnShader = string.IsNullOrWhiteSpace(spawnKey) ? null : spawnKey;
						b.DeathShader = string.IsNullOrWhiteSpace(deathKey) ? null : deathKey;
						return b;
					});
					if (!updatedB)
					{
						meta.AddOrUpdateBuilding(new UnitMetadata
						{
							TemplateID = newTemplateID,
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
							SpawnShader = string.IsNullOrWhiteSpace(spawnKey) ? null : spawnKey,
							DeathShader = string.IsNullOrWhiteSpace(deathKey) ? null : deathKey
						});
					}
					break;

				case "resources":
					bool updatedR = meta.UpdateResource(_originalTemplateID, r =>
					{
						r.TemplateID = newTemplateID;
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
						r.SpawnShader = string.IsNullOrWhiteSpace(spawnKey) ? null : spawnKey;
						r.DeathShader = string.IsNullOrWhiteSpace(deathKey) ? null : deathKey;
						return r;
					});
					if (!updatedR)
					{
						meta.AddOrUpdateResource(new ResourceMetadata
						{
							TemplateID = newTemplateID,
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
							SpawnShader = string.IsNullOrWhiteSpace(spawnKey) ? null : spawnKey,
							DeathShader = string.IsNullOrWhiteSpace(deathKey) ? null : deathKey
						});
					}
					break;

				case "props":
					bool updatedP = meta.UpdateProp(_originalTemplateID, p =>
					{
						p.TemplateID = newTemplateID;
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
						p.SpawnShader = string.IsNullOrWhiteSpace(spawnKey) ? null : spawnKey;
						p.DeathShader = string.IsNullOrWhiteSpace(deathKey) ? null : deathKey;
						return p;
					});
					if (!updatedP)
					{
						meta.AddOrUpdateProp(new PropMetadata
						{
							TemplateID = newTemplateID,
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
							SpawnShader = string.IsNullOrWhiteSpace(spawnKey) ? null : spawnKey,
							DeathShader = string.IsNullOrWhiteSpace(deathKey) ? null : deathKey
						});
					}
					break;
			}
		});

		if (GameHost.Instance != null)
		{
			GameHost.Instance.SetModelScale(newTemplateID, _scale);
			GameHost.Instance.SetModelYOffset(newTemplateID, _yOffset);
			GameHost.Instance.SetModelCollisionCircleRatio(newTemplateID, _collisionCircle);
			GameHost.Instance.SetModelBrightness(newTemplateID, _brightness);
			GameHost.Instance.SetModelColorTint(newTemplateID, _tint);
			GameHost.Instance.SetModelNormalizeLuminance(newTemplateID, _normalizeLuminance);
			GameHost.Instance.SetModelIgnorePlayerColor(newTemplateID, _ignorePlayerColor);
			GameHost.Instance.SetModelDespillPlayerColor(newTemplateID, _despillPlayerColor);
			GameHost.Instance.SetModelSpawnShader(newTemplateID, spawnKey);
			GameHost.Instance.SetModelDeathShader(newTemplateID, deathKey);
			GameHost.Instance.SetModelEnableProceduralAnimation(newTemplateID, _enableProceduralAnimation);
			GameHost.Instance.SetModelProceduralAnimation(newTemplateID, animKey);

			if (!string.Equals(_originalTemplateID, newTemplateID, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(_originalTemplateID))
			{
				GameHost.Instance.SetModelScale(_originalTemplateID, _scale);
				GameHost.Instance.SetModelYOffset(_originalTemplateID, _yOffset);
				GameHost.Instance.SetModelCollisionCircleRatio(_originalTemplateID, _collisionCircle);
				GameHost.Instance.SetModelBrightness(_originalTemplateID, _brightness);
				GameHost.Instance.SetModelColorTint(_originalTemplateID, _tint);
				GameHost.Instance.SetModelNormalizeLuminance(_originalTemplateID, _normalizeLuminance);
				GameHost.Instance.SetModelIgnorePlayerColor(_originalTemplateID, _ignorePlayerColor);
				GameHost.Instance.SetModelDespillPlayerColor(_originalTemplateID, _despillPlayerColor);
				GameHost.Instance.SetModelSpawnShader(_originalTemplateID, spawnKey);
				GameHost.Instance.SetModelDeathShader(_originalTemplateID, deathKey);
				GameHost.Instance.SetModelEnableProceduralAnimation(_originalTemplateID, _enableProceduralAnimation);
				GameHost.Instance.SetModelProceduralAnimation(_originalTemplateID, animKey);
				GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
			}

			GameHost.Instance.LoadUnitMetadata(wsPath);

			Prop3D.InvalidateModelPathCache(newTemplateID);
			ModelCache.InvalidateModelPath(newTemplateID);
			Prop3D.InvalidateModelPathCache(_originalTemplateID);
			ModelCache.InvalidateModelPath(_originalTemplateID);
			if (!string.IsNullOrEmpty(_modelPath))
			{
				Prop3D.InvalidateModelPathCache(_modelPath);
				ModelCache.InvalidateModelPath(_modelPath);
			}

			var action = new EntityVisualEditUndoAction(newTemplateID, _category, _initialSnapshot, currentSnapshot);
			EditorHistoryManager.RecordAction(action);

			GameHost.Instance.RefreshAllPlacedObjectModels(newTemplateID);
			if (!string.Equals(_originalTemplateID, newTemplateID, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(_originalTemplateID))
			{
				GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
			}

			if (_currentSelectedObject != null && GodotObject.IsInstanceValid(_currentSelectedObject))
			{
				if (_currentSelectedObject is Unit3D u3d)
				{
					string fallbackModel = GameHost.Instance.GetFallbackModelPath(_modelPath, u3d.IsBuilding);
					u3d.LoadModel(fallbackModel);
				}
				else if (_currentSelectedObject is Prop3D p3d)
				{
					p3d.RefreshPropVisual();
				}
			}

			GameHost.Instance.FlushModelYOffsetSave();
			GameHost.Instance.FlushModelCollisionCircleSave();
		}

		Hud?.RefreshEntityPalette();
		_onAppliedCallback?.Invoke(_originalTemplateID, newTemplateID);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Saved visual properties for '{0}'"), newTemplateID));
	}

	protected override void OnCancel()
	{
		if (GameHost.Instance == null || string.IsNullOrEmpty(_originalTemplateID)) return;

		UpdateStaticRegistryModelAndVisualMode(_category, _originalTemplateID, _initialSnapshot.ModelPath, _initialSnapshot.VisualMode);

		string cleanId = Path.GetFileNameWithoutExtension(_originalTemplateID);
		if (!string.IsNullOrEmpty(cleanId) && !cleanId.Equals(_originalTemplateID, StringComparison.OrdinalIgnoreCase))
		{
			UpdateStaticRegistryModelAndVisualMode(_category, cleanId, _initialSnapshot.ModelPath, _initialSnapshot.VisualMode);
		}

		if (!string.IsNullOrEmpty(_slug))
		{
			UpdateStaticRegistryModelAndVisualMode(_category, $"{_objectType}/{_slug}", _initialSnapshot.ModelPath, _initialSnapshot.VisualMode);
			UpdateStaticRegistryModelAndVisualMode(_category, _slug, _initialSnapshot.ModelPath, _initialSnapshot.VisualMode);
		}

		if (_currentSelectedObject is Unit3D unit && !string.IsNullOrEmpty(unit.UnitId))
		{
			UpdateStaticRegistryModelAndVisualMode(_category, unit.UnitId, _initialSnapshot.ModelPath, _initialSnapshot.VisualMode);
		}
		else if (_currentSelectedObject is Prop3D prop && !string.IsNullOrEmpty(prop.PropId))
		{
			UpdateStaticRegistryModelAndVisualMode(_category, prop.PropId, _initialSnapshot.ModelPath, _initialSnapshot.VisualMode);
		}

		GameHost.Instance.SetModelScale(_originalTemplateID, _initialSnapshot.Scale);
		GameHost.Instance.SetModelYOffset(_originalTemplateID, _initialSnapshot.YOffset);
		GameHost.Instance.SetModelCollisionCircleRatio(_originalTemplateID, _initialSnapshot.CollisionCircleRatio);
		GameHost.Instance.SetModelBrightness(_originalTemplateID, _initialSnapshot.Brightness);
		GameHost.Instance.SetModelColorTint(_originalTemplateID, _initialSnapshot.ColorTint);
		GameHost.Instance.SetModelNormalizeLuminance(_originalTemplateID, _initialSnapshot.NormalizeLuminance);
		GameHost.Instance.SetModelIgnorePlayerColor(_originalTemplateID, _initialSnapshot.IgnorePlayerColor);
		GameHost.Instance.SetModelDespillPlayerColor(_originalTemplateID, _initialSnapshot.DespillPlayerColor);
		GameHost.Instance.SetModelSpawnShader(_originalTemplateID, _initialSnapshot.SpawnShader);
		GameHost.Instance.SetModelDeathShader(_originalTemplateID, _initialSnapshot.DeathShader);
		GameHost.Instance.SetModelEnableProceduralAnimation(_originalTemplateID, _initialSnapshot.EnableProceduralAnimation);
		GameHost.Instance.SetModelProceduralAnimation(_originalTemplateID, _initialSnapshot.ProceduralAnimation);

		Prop3D.InvalidateModelPathCache(_originalTemplateID);
		ModelCache.InvalidateModelPath(_originalTemplateID);
		if (!string.IsNullOrEmpty(_initialSnapshot.ModelPath))
		{
			Prop3D.InvalidateModelPathCache(_initialSnapshot.ModelPath);
			ModelCache.InvalidateModelPath(_initialSnapshot.ModelPath);
		}

		GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);

		if (_currentSelectedObject != null && GodotObject.IsInstanceValid(_currentSelectedObject))
		{
			if (_currentSelectedObject is Unit3D u3d)
			{
				string fallback = GameHost.Instance.GetFallbackModelPath(_initialSnapshot.ModelPath, u3d.IsBuilding);
				u3d.LoadModel(fallback);
			}
			else if (_currentSelectedObject is Prop3D p3d)
			{
				p3d.RefreshPropVisual();
			}
		}

		GameHost.Instance.FlushModelYOffsetSave();
		GameHost.Instance.FlushModelCollisionCircleSave();
	}

	private static float CalculateModelLocalMinY(Node selectedObject)
	{
		if (selectedObject == null || !GodotObject.IsInstanceValid(selectedObject))
			return 0f;

		Node3D visualNode = null;
		if (selectedObject is Unit3D unit && unit.ModelNode != null && GodotObject.IsInstanceValid(unit.ModelNode))
		{
			visualNode = unit.ModelNode;
		}
		else if (selectedObject is Node rootNode)
		{
			visualNode = rootNode.GetNodeOrNull<Node3D>("VisualModel") ?? (selectedObject as Node3D);
		}

		if (visualNode == null)
			return 0f;

		float minY = float.MaxValue;
		bool foundMesh = false;

		void Collect(Node current)
		{
			if (current is MeshInstance3D meshInst && meshInst.Mesh != null && meshInst.Visible)
			{
				Transform3D relXform = Transform3D.Identity;
				Node curr = meshInst;
				while (curr != null && curr != visualNode)
				{
					if (curr is Node3D n3d)
					{
						relXform = n3d.Transform * relXform;
					}
					curr = curr.GetParent();
				}

				if (Mathf.Abs(relXform.Basis.Determinant()) > 0.0001f)
				{
					Aabb mAabb = meshInst.Mesh.GetAabb();
					Vector3 min = mAabb.Position;
					Vector3 max = mAabb.End;
					Vector3[] corners = new[]
					{
						new Vector3(min.X, min.Y, min.Z),
						new Vector3(min.X, min.Y, max.Z),
						new Vector3(min.X, max.Y, min.Z),
						new Vector3(min.X, max.Y, max.Z),
						new Vector3(max.X, min.Y, min.Z),
						new Vector3(max.X, min.Y, max.Z),
						new Vector3(max.X, max.Y, min.Z),
						new Vector3(max.X, max.Y, max.Z)
					};
					for (int i = 0; i < 8; i++)
					{
						Vector3 pt = relXform * corners[i];
						if (pt.Y < minY)
						{
							minY = pt.Y;
							foundMesh = true;
						}
					}
				}
			}
			foreach (Node child in current.GetChildren())
			{
				if (child is not BoneAttachment3D &&
					!child.Name.ToString().StartsWith("PseudoSocket_", StringComparison.OrdinalIgnoreCase) &&
					!child.Name.ToString().StartsWith("SocketAttachment_", StringComparison.OrdinalIgnoreCase) &&
					!child.Name.ToString().StartsWith("Att_", StringComparison.OrdinalIgnoreCase) &&
					!child.Name.ToString().StartsWith("AttVisual_", StringComparison.OrdinalIgnoreCase))
				{
					Collect(child);
				}
			}
		}

		Collect(visualNode);
		return foundMesh ? minY : 0f;
	}
}
