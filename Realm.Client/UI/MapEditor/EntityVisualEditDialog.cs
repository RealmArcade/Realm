using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class EntityVisualEditDialog : FloatingDialogBase
{
	public EntityVisualEditDialog() { }

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
	private Node? _currentSelectedObject;
	private VisualOverridesSnapshot _initialSnapshot;
	private string? _initialTemplateJson = null;
	private bool _templateExistedInitially = false;
	private float _modelLocalMinY = 0f;
	private float _previousScale = 1.0f;
	private Action<string, string>? _onAppliedCallback;

	private Label _lblDescription = null!;
	private Label _lblIdentitySectionHeader = null!;
	private HBoxContainer _idRow = null!;
	private Label _lblObjectTypePrefix = null!;
	private LineEdit _txtSlug = null!;
	private Label _lblSlugValidation = null!;
	private HBoxContainer _visualModeRow = null!;
	private OptionButton _optVisualMode = null!;
	private LineEdit _txtModelPath = null!;
	private Action<string> _setModelPathValue = null!;
	private LineEdit _txtPortraitModelPath = null!;
	private Action<string> _setPortraitModelPathValue = null!;
	private LineEdit _txtName = null!;
	private LineEdit _txtDescription = null!;

	private VBoxContainer _transformSection = null!;
	private VBoxContainer _shaderSection = null!;

	private HSlider _sldScale = null!;
	private Label _lblScale = null!;
	private HSlider _sldYOffset = null!;
	private Label _lblYOffset = null!;
	private HSlider _sldCollisionCircle = null!;
	private Label _lblCollisionCircle = null!;
	private HSlider _sldBrightness = null!;
	private Label _lblBrightness = null!;
	private ColorPickerButton _btnTint = null!;
	private HSlider _sldTintHue = null!;
	private CheckBox _chkNormalizeLuminance = null!;
	private CheckBox _chkIgnorePlayerColor = null!;
	private CheckBox _chkDespillPlayerColor = null!;

	private OptionButton _optSpawnShader = null!;
	private OptionButton _optDeathShader = null!;
	private CheckBox _chkEnableProceduralAnim = null!;
	private OptionButton _optProceduralAnim = null!;
	private Button _btnOpenProcAnimStudio = null!;
	private Button _btnOpenRiggedAnimStudio = null!;
	private Button _btnOpenSocketsAndVfx = null!;

	public EntityVisualEditDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Visual Properties & Overrides"), new Vector2(520, 740))
	{
		BuildControls();
	}


	private void BuildControls()
	{
		_lblDescription = AddDescription(BodyContainer, TranslationServer.Translate("Modify visual, collision, and procedural motion settings for this entity or model."));

		var scrollBody = CreateScrollBody(600);
		var contentVBox = CreateExpandingContentColumn();
		scrollBody.AddChild(contentVBox);

		BuildIdentitySection(contentVBox);

		_transformSection = new VBoxContainer();
		_transformSection.Name = "TransformSection_VBoxContainer";
		_transformSection.AddThemeConstantOverride("separation", 6);
		contentVBox.AddChild(_transformSection);
		BuildTransformSection(_transformSection);

		_shaderSection = new VBoxContainer();
		_shaderSection.Name = "ShaderSection_VBoxContainer";
		_shaderSection.AddThemeConstantOverride("separation", 6);
		contentVBox.AddChild(_shaderSection);
		BuildProceduralAnimationSection(_shaderSection);

		UpdateStudioButtonVisibility();
	}

	private void BuildIdentitySection(VBoxContainer contentVBox)
	{
		_lblIdentitySectionHeader = AddSectionHeader(contentVBox, "🆔 " + TranslationServer.Translate("IDENTITY & ASSET MODEL"), new Color(0.95f, 0.8f, 0.4f));
		BuildIdRow(contentVBox);
		BuildVisualModeRow(contentVBox);
		BuildAssetDropdowns(contentVBox);
		BuildTextInputs(contentVBox);
	}

	private void BuildIdRow(VBoxContainer contentVBox)
	{
		_idRow = BuildIdentityRow(contentVBox, "unit/", out _lblObjectTypePrefix);

		_txtSlug = CreateSlugField("snake_case_slug");
		_txtSlug.TextChanged += (val) =>
		{
			if (_isSyncing) return;
			_slug = TemplateIDHelper.ToSnakeCase(val);
			ValidateSlug();
		};
		_idRow.AddChild(_txtSlug);
		

		_lblSlugValidation = new Label();
		_lblSlugValidation.Name = "LblSlugValidation_Label";
		_lblSlugValidation.AddThemeFontSizeOverride("font_size", 10);
		_lblSlugValidation.AddThemeColorOverride("font_color", new Color(0.95f, 0.4f, 0.4f));
		_lblSlugValidation.Visible = false;
		contentVBox.AddChild(_lblSlugValidation);
	}

	private void BuildVisualModeRow(VBoxContainer contentVBox)
	{
		_visualModeRow = new HBoxContainer();
		_visualModeRow.Name = "VisualModeRow_HBoxContainer";
		_visualModeRow.AddThemeConstantOverride("separation", 8);

		var lblMode = new Label();
		lblMode.Name = "LblMode_Label";
		lblMode.Text = TranslationServer.Translate("Visual Primitive:");
		lblMode.CustomMinimumSize = new Vector2(140, 0);
		lblMode.AddThemeFontSizeOverride("font_size", 11);
		_visualModeRow.AddChild(lblMode);

		_optVisualMode = new OptionButton();
		_optVisualMode.Name = "OptVisualMode_OptionButton";
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
			UpdateStudioButtonVisibility();
		};
		_visualModeRow.AddChild(_optVisualMode);
		contentVBox.AddChild(_visualModeRow);
	}

	private void BuildAssetDropdowns(VBoxContainer contentVBox)
	{
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
				UpdateStudioButtonVisibility();
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
	}

	private void BuildTextInputs(VBoxContainer contentVBox)
	{
		_txtName = AddTextInput(
			contentVBox,
			TranslationServer.Translate("Display Name:"),
			_name,
			(val) =>
			{
				if (!_isSyncing) _name = val ?? string.Empty;
			},
			TranslationServer.Translate("Entity display name..."),
			140f
		);

		_txtDescription = AddTextInput(
			contentVBox,
			TranslationServer.Translate("Description:"),
			_description,
			(val) =>
			{
				if (!_isSyncing) _description = val ?? string.Empty;
			},
			TranslationServer.Translate("Entity description..."),
			140f
		);
	}

	private void BuildTransformSection(VBoxContainer contentVBox)
	{
		AddSectionHeader(contentVBox, "📐 " + TranslationServer.Translate("TRANSFORM & VISUAL RENDERING"), new Color(0.5f, 0.85f, 1.0f));
		BuildTransformSliders(contentVBox);
		BuildTransformColorControls(contentVBox);
		BuildTransformCheckboxes(contentVBox);
	}

	private void BuildTransformSliders(VBoxContainer contentVBox)
	{
		(_sldScale, _lblScale) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Scale:"),
			0.1f,
			10.0f,
			0.05f,
			_scale,
			HandleScaleChanged,
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
				if (GameContext.Host != null && !string.IsNullOrEmpty(_originalTemplateID))
				{
					GameContext.IEditorAPI.SetModelYOffset(_originalTemplateID, val);
					GameContext.IEditorAPI.RefreshPlacedObjectModels(_originalTemplateID);
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
				if (GameContext.Host != null && !string.IsNullOrEmpty(_originalTemplateID))
				{
					GameContext.IEditorAPI.SetModelCollisionCircleRatio(_originalTemplateID, val);
					GameContext.IEditorAPI.RefreshPlacedObjectModels(_originalTemplateID);
				}
			},
			"0.00",
			140f
		);
	}

	private void HandleScaleChanged(float val)
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

			if (GameContext.Host != null && !string.IsNullOrEmpty(_originalTemplateID))
			{
				GameContext.IEditorAPI.SetModelYOffset(_originalTemplateID, newYOffset);
			}
		}

		if (GameContext.Host != null && !string.IsNullOrEmpty(_originalTemplateID))
		{
			GameContext.IEditorAPI.SetModelScale(_originalTemplateID, newScale);
			GameContext.IEditorAPI.RefreshPlacedObjectModels(_originalTemplateID);
		}
	}

	private void BuildTransformColorControls(VBoxContainer contentVBox)
	{
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
				if (GameContext.Host != null && !string.IsNullOrEmpty(_originalTemplateID))
				{
					GameContext.IEditorAPI.SetModelBrightness(_originalTemplateID, val);
					GameContext.IEditorAPI.RefreshPlacedObjectModels(_originalTemplateID);
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
				if (GameContext.Host != null && !string.IsNullOrEmpty(_originalTemplateID))
				{
					GameContext.IEditorAPI.SetModelColorTint(_originalTemplateID, color.R, color.G, color.B, color.A);
					GameContext.IEditorAPI.RefreshPlacedObjectModels(_originalTemplateID);
				}
			},
			140f
		);
	}

	private void BuildTransformCheckboxes(VBoxContainer contentVBox)
	{
		_chkNormalizeLuminance = AddCheckBox(contentVBox, TranslationServer.Translate("Normalize Luminance"), _normalizeLuminance, (val) =>
		{
			if (_isSyncing) return;
			_normalizeLuminance = val;
			if (GameContext.Host != null && !string.IsNullOrEmpty(_originalTemplateID))
			{
				GameContext.IEditorAPI.SetModelNormalizeLuminance(_originalTemplateID, val);
				GameContext.IEditorAPI.RefreshPlacedObjectModels(_originalTemplateID);
			}
		});

		_chkIgnorePlayerColor = AddCheckBox(contentVBox, TranslationServer.Translate("Ignore Player Color (model only)"), _ignorePlayerColor, (val) =>
		{
			if (_isSyncing) return;
			_ignorePlayerColor = val;
			if (GameContext.Host != null && !string.IsNullOrEmpty(_originalTemplateID))
			{
				GameContext.IEditorAPI.SetModelIgnorePlayerColor(_originalTemplateID, val);
				GameContext.IEditorAPI.RefreshPlacedObjectModels(_originalTemplateID);
			}
		});

		_chkDespillPlayerColor = AddCheckBox(contentVBox, TranslationServer.Translate("Despill Player Color"), _despillPlayerColor, (val) =>
		{
			if (_isSyncing) return;
			_despillPlayerColor = val;
			if (GameContext.Host != null && !string.IsNullOrEmpty(_originalTemplateID))
			{
				GameContext.IEditorAPI.SetModelDespillPlayerColor(_originalTemplateID, val);
				GameContext.IEditorAPI.RefreshPlacedObjectModels(_originalTemplateID);
			}
		});
	}

	private void BuildProceduralAnimationSection(VBoxContainer contentVBox)
	{
		AddSectionHeader(contentVBox, "✨ " + TranslationServer.Translate("SPAWN, DEATH & PROCEDURAL SHADERS"), new Color(0.95f, 0.7f, 0.95f));

		var shaders = SpawnDeathShaderManager.LoadAllCustomShaders();
		var shaderOptions = new List<string> { TranslationServer.Translate("(None)") };
		foreach (var s in shaders.Values)
		{
			shaderOptions.Add(s.Name);
		}

		BuildShaderDropdowns(contentVBox, shaderOptions);
		BuildProceduralAnimationControls(contentVBox);
		BuildStudioButtons(contentVBox);
	}

	private void BuildShaderDropdowns(VBoxContainer contentVBox, List<string> shaderOptions)
	{
		_optSpawnShader = AddOptionDropdown(contentVBox, TranslationServer.Translate("Spawn Shader:"), shaderOptions.ToArray(), 0, HandleSpawnShaderSelection, 140f);
		_optDeathShader = AddOptionDropdown(contentVBox, TranslationServer.Translate("Death Shader:"), shaderOptions.ToArray(), 0, HandleDeathShaderSelection, 140f);
	}

	private void HandleSpawnShaderSelection(int idx)
	{
		if (_isSyncing) return;
		var currentShaders = SpawnDeathShaderManager.LoadAllCustomShaders();
		string selectedKey = idx > 0 && idx - 1 < currentShaders.Count ? currentShaders.ElementAt(idx - 1).Key : "";
		_spawnShader = selectedKey;
		UpdateModelSpawnShader(selectedKey);
	}

	private void HandleDeathShaderSelection(int idx)
	{
		if (_isSyncing) return;
		var currentShaders = SpawnDeathShaderManager.LoadAllCustomShaders();
		string selectedKey = idx > 0 && idx - 1 < currentShaders.Count ? currentShaders.ElementAt(idx - 1).Key : "";
		_deathShader = selectedKey;
		UpdateModelDeathShader(selectedKey);
	}

	private void UpdateModelSpawnShader(string selectedKey)
	{
		if (GameContext.Host == null || string.IsNullOrEmpty(_originalTemplateID)) return;
		GameContext.IEditorAPI.SetModelSpawnShader(_originalTemplateID, selectedKey);
		GameContext.IEditorAPI.RefreshPlacedObjectModels(_originalTemplateID);
	}

	private void UpdateModelDeathShader(string selectedKey)
	{
		if (GameContext.Host == null || string.IsNullOrEmpty(_originalTemplateID)) return;
		GameContext.IEditorAPI.SetModelDeathShader(_originalTemplateID, selectedKey);
		GameContext.IEditorAPI.RefreshPlacedObjectModels(_originalTemplateID);
	}

	private void BuildProceduralAnimationControls(VBoxContainer contentVBox)
	{
		_chkEnableProceduralAnim = AddCheckBox(contentVBox, TranslationServer.Translate("Enable Procedural Sway / Wind"), _enableProceduralAnimation, (pressed) =>
		{
			if (_isSyncing) return;
			_enableProceduralAnimation = pressed;
			if (GameContext.Host != null && !string.IsNullOrEmpty(_originalTemplateID))
			{
				GameContext.IEditorAPI.SetModelEnableProceduralAnimation(_originalTemplateID, pressed);
				GameContext.IEditorAPI.RefreshPlacedObjectModels(_originalTemplateID);
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
			if (GameContext.Host != null && !string.IsNullOrEmpty(_originalTemplateID))
			{
				GameContext.IEditorAPI.SetModelProceduralAnimation(_originalTemplateID, selectedKey);
				GameContext.IEditorAPI.RefreshPlacedObjectModels(_originalTemplateID);
			}
		}, 140f);
	}

	private void BuildStudioButtons(VBoxContainer contentVBox)
	{
		_btnOpenProcAnimStudio = AddButton(contentVBox, "✨ " + TranslationServer.Translate("Procedural Animation Studio [un-rigged]"), HandleOpenProcAnimStudio, "Open Procedural Animation Studio to edit math formulas and motion parameters", 11, new Vector2(0, 28));
		_btnOpenRiggedAnimStudio = AddButton(contentVBox, "🎬 " + TranslationServer.Translate("Animation Studio [rigged]"), HandleOpenRiggedAnimStudio, "Open Animation Studio to preview and assign skeletal animations", 11, new Vector2(0, 28));
		_btnOpenSocketsAndVfx = AddButton(contentVBox, "📎 " + TranslationServer.Translate("Sockets & VFX"), HandleOpenSocketsAndVfx, "Open Socket & VFX Attachment Studio", 11, new Vector2(0, 28));
	}

	private void HandleOpenRiggedAnimStudio()
	{
		string targetKey = GetTargetKey();
		string selectedMesh = GetSelectedMesh(targetKey);
		Hud?.OpenAnimationPreviewDialog(targetKey, selectedMesh);
	}

	private void HandleOpenSocketsAndVfx()
	{
		string targetKey = GetTargetKey();
		bool isBuilding = _objectType == "building" || (GameHost.BuildingRegistry?.ContainsKey(targetKey) == true);
		string defaultSocket = isBuilding ? "Center" : "RightHand";
		Node3D? sourceModel = _currentSelectedObject as Node3D;
		Hud?.OpenObjectAttachmentDialog(targetKey, null, defaultSocket, sourceModel, orient => HandleAttachmentOrientation(targetKey));
	}

	private string GetTargetKey()
	{
		if (!string.IsNullOrEmpty(_originalTemplateID)) return _originalTemplateID;
		return !string.IsNullOrEmpty(_slug) ? $"{_objectType}/{_slug}" : "object";
	}

	private string GetSelectedMesh(string targetKey)
	{
		if (!string.IsNullOrEmpty(_modelPath)) return _modelPath;
		return GameContext.Host?.GetModelAssetKey(_currentSelectedObject ?? (object)targetKey) ?? targetKey;
	}

	private void HandleAttachmentOrientation(string targetKey)
	{
		if (_currentSelectedObject is Unit3D u)
		{
			u.ApplyAllConfiguredAttachments();
			return;
		}

		if (GameContext.Host != null && !string.IsNullOrEmpty(targetKey))
		{
			GameContext.IEditorAPI.RefreshPlacedObjectModels(targetKey);
		}
	}

	private void HandleOpenProcAnimStudio()
	{
		string selectedKey = DetermineProceduralAnimationKey();
		string targetKey = GetTargetKey();
		var cfg = ProceduralAnimationManager.GetConfig(selectedKey) ?? new ProceduralAnimationConfig { Id = targetKey + "_anim", Name = targetKey + " Animation" };
		string selectedMesh = GetSelectedMesh(targetKey);

		Hud?.OpenProceduralAnimationStudioDialog(cfg, HandleProceduralAnimationStudioDialogSaved, selectedMesh);
	}

	private string DetermineProceduralAnimationKey()
	{
		var currentConfigs = ProceduralAnimationManager.LoadAllConfigs();
		if (_optProceduralAnim != null && _optProceduralAnim.Selected > 0 && _optProceduralAnim.Selected - 1 < currentConfigs.Count)
		{
			return currentConfigs.ElementAt(_optProceduralAnim.Selected - 1).Key;
		}

		if (!string.IsNullOrEmpty(_proceduralAnimation)) return _proceduralAnimation;

		if (GameContext.Host != null && !string.IsNullOrEmpty(_originalTemplateID))
		{
			return GameContext.Host.GetModelProceduralAnimation(_originalTemplateID) ?? "";
		}

		return "";
	}

	private void HandleProceduralAnimationStudioDialogSaved(ProceduralAnimationConfig savedCfg)
	{
		if (savedCfg == null) return;

		_proceduralAnimation = savedCfg.Id;
		_enableProceduralAnimation = true;
		if (_chkEnableProceduralAnim != null) _chkEnableProceduralAnim.ButtonPressed = true;

		UpdateModelProceduralAnimation(savedCfg.Id);
		RefreshProceduralAnimationDropdown(savedCfg.Id);
	}

	private void UpdateModelProceduralAnimation(string animId)
	{
		if (GameContext.Host == null || string.IsNullOrEmpty(_originalTemplateID)) return;

		GameContext.IEditorAPI.SetModelProceduralAnimation(_originalTemplateID, animId);
		GameContext.IEditorAPI.SetModelEnableProceduralAnimation(_originalTemplateID, true);
		GameContext.IEditorAPI.RefreshPlacedObjectModels(_originalTemplateID);
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
			if (GameContext.MetadataService.TryLoadMetadata(wsPath, out var meta) && meta != null)
			{
				bool exists = _category switch
				{
					"units" => meta.GetUnit(fullId) != null,
					"buildings" => meta.GetBuilding(fullId) != null,
					"resources" => meta.GetResource(fullId) != null,
					"props" => meta.GetProp(fullId) != null,
					"items" => meta.GetItem(fullId) != null,
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



	public void OpenForObject(Node selectedObject)
	{
		if (selectedObject == null || !GodotObject.IsInstanceValid(selectedObject) || GameContext.Host == null) return;
		var (category, objectId) = ResolveObjectCategoryAndId(selectedObject);
		if (string.IsNullOrEmpty(objectId)) return;
		OpenForObject(category, objectId, null, selectedObject);
	}

	public void OpenForObject(string? category, string objectId, Action<string, string>? onApplied = null)
	{
		OpenForObject(category, objectId, onApplied, null);
	}

	public void OpenForObject(string? category, string objectId, Action<string, string>? onApplied, Node? selectedObject)
	{
		InitializeDialogState(category, objectId, selectedObject, onApplied);

		object targetLookup = selectedObject is { } selectedNode ? selectedNode : _originalTemplateID;

		InitializeModelProperties(targetLookup, _slug);

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		if (GameContext.MetadataService.TryLoadMetadata(wsPath, out var meta) && meta != null)
		{
			LoadMetadataByCategory(meta, objectId);
		}

		InitializeSnapshot();

		TitleLabel.Text = $"{TranslationServer.Translate("Visual Properties & Overrides")} - {_originalTemplateID}";

		ApplyItemCategoryLayout();
		SyncControls();
		OpenDialog();
	}

	protected override string GetRandomizeTemplateSlug()
	{
		return !string.IsNullOrEmpty(_originalTemplateID) ? _originalTemplateID : $"{_objectType}/{_slug}";
	}

	protected override void OnAssetsRandomized()
	{
		string targetId = GetRandomizeTemplateSlug();
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();

		if (GameContext.MetadataService.TryLoadMetadata(wsPath, out var loadedMeta) && loadedMeta != null)
		{
			LoadMetadataByCategory(loadedMeta, targetId);
			if (string.IsNullOrEmpty(_modelPath) && !string.IsNullOrEmpty(_slug))
			{
				LoadMetadataByCategory(loadedMeta, _slug);
			}
		}

		SyncControls();
		ApplyLiveModelAndVisualMode();
		UpdateStudioButtonVisibility();
	}







}