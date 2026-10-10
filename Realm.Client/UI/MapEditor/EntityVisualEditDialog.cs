using Godot;
using Realm.Client.Animation;
using Realm.Client.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Realm.Client.UI.MapEditor;

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
	private Button _btnOpenRiggedAnimStudio;
	private Button _btnOpenSocketsAndVfx;

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

		BuildIdentitySection(contentVBox);
		BuildTransformSection(contentVBox);
		BuildProceduralAnimationSection(contentVBox);

		UpdateRiggedAnimationButtonVisibility();
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
	}

	private void BuildVisualModeRow(VBoxContainer contentVBox)
	{
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
			UpdateRiggedAnimationButtonVisibility();
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
				UpdateRiggedAnimationButtonVisibility();
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
				if (Realm.Client.Core.GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
				{
					Realm.Client.Core.GameHost.Instance.SetModelYOffset(_originalTemplateID, val);
					Realm.Client.Core.GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
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
				if (Realm.Client.Core.GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
				{
					Realm.Client.Core.GameHost.Instance.SetModelCollisionCircleRatio(_originalTemplateID, val);
					Realm.Client.Core.GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
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

			if (Realm.Client.Core.GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
			{
				Realm.Client.Core.GameHost.Instance.SetModelYOffset(_originalTemplateID, newYOffset);
			}
		}

		if (Realm.Client.Core.GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
		{
			Realm.Client.Core.GameHost.Instance.SetModelScale(_originalTemplateID, newScale);
			Realm.Client.Core.GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
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
				if (Realm.Client.Core.GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
				{
					Realm.Client.Core.GameHost.Instance.SetModelBrightness(_originalTemplateID, val);
					Realm.Client.Core.GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
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
				if (Realm.Client.Core.GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
				{
					Realm.Client.Core.GameHost.Instance.SetModelColorTint(_originalTemplateID, color);
					Realm.Client.Core.GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
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
			if (Realm.Client.Core.GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
			{
				Realm.Client.Core.GameHost.Instance.SetModelNormalizeLuminance(_originalTemplateID, val);
				Realm.Client.Core.GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
			}
		});

		_chkIgnorePlayerColor = AddCheckBox(contentVBox, TranslationServer.Translate("Ignore Player Color"), _ignorePlayerColor, (val) =>
		{
			if (_isSyncing) return;
			_ignorePlayerColor = val;
			if (Realm.Client.Core.GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
			{
				Realm.Client.Core.GameHost.Instance.SetModelIgnorePlayerColor(_originalTemplateID, val);
				Realm.Client.Core.GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
			}
		});

		_chkDespillPlayerColor = AddCheckBox(contentVBox, TranslationServer.Translate("Despill Player Color"), _despillPlayerColor, (val) =>
		{
			if (_isSyncing) return;
			_despillPlayerColor = val;
				if (Realm.Client.Core.GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
			{
				Realm.Client.Core.GameHost.Instance.SetModelDespillPlayerColor(_originalTemplateID, val);
				Realm.Client.Core.GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
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
		if (Realm.Client.Core.GameHost.Instance == null || string.IsNullOrEmpty(_originalTemplateID)) return;
		Realm.Client.Core.GameHost.Instance.SetModelSpawnShader(_originalTemplateID, selectedKey);
		Realm.Client.Core.GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
	}

	private void UpdateModelDeathShader(string selectedKey)
	{
		if (Realm.Client.Core.GameHost.Instance == null || string.IsNullOrEmpty(_originalTemplateID)) return;
		Realm.Client.Core.GameHost.Instance.SetModelDeathShader(_originalTemplateID, selectedKey);
		Realm.Client.Core.GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
	}

	private void BuildProceduralAnimationControls(VBoxContainer contentVBox)
	{
		_chkEnableProceduralAnim = AddCheckBox(contentVBox, TranslationServer.Translate("Enable Procedural Sway / Wind"), _enableProceduralAnimation, (pressed) =>
		{
			if (_isSyncing) return;
			_enableProceduralAnimation = pressed;
			if (Realm.Client.Core.GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
			{
				Realm.Client.Core.GameHost.Instance.SetModelEnableProceduralAnimation(_originalTemplateID, pressed);
				Realm.Client.Core.GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
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
			if (Realm.Client.Core.GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
			{
				Realm.Client.Core.GameHost.Instance.SetModelProceduralAnimation(_originalTemplateID, selectedKey);
				Realm.Client.Core.GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
			}
		}, 140f);
	}

	private void BuildStudioButtons(VBoxContainer contentVBox)
	{
		_btnOpenProcAnimStudio = AddButton(contentVBox, "✨ " + TranslationServer.Translate("Procedural Animation Studio [un-rigged]"), HandleOpenProcAnimStudio, "Open Procedural Animation Studio to edit math formulas and motion parameters", 11, new Vector2(0, 28));
		_btnOpenRiggedAnimStudio = AddButton(contentVBox, "🎬 " + TranslationServer.Translate("Animation Studio [rigged]"), HandleOpenRiggedAnimStudio, "Open Animation Studio to preview and assign skeletal animations", 11, new Vector2(0, 28));
		_btnOpenSocketsAndVfx = AddButton(contentVBox, "📎 " + TranslationServer.Translate("Sockets & VFX [rigged]"), HandleOpenSocketsAndVfx, "Open Socket & VFX Attachment Studio", 11, new Vector2(0, 28));
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
		bool isBuilding = _objectType == "building" || (Realm.Client.Core.GameHost.BuildingRegistry?.ContainsKey(targetKey) == true);
		string defaultSocket = isBuilding ? "Center" : "RightHand";
		Node3D sourceModel = _currentSelectedObject as Node3D;
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
		return Realm.Client.Core.GameHost.Instance?.GetModelAssetKey(_currentSelectedObject ?? (object)targetKey) ?? targetKey;
	}

	private void HandleAttachmentOrientation(string targetKey)
	{
		if (_currentSelectedObject is Realm.Client.Unit3D u)
		{
			u.ApplyAllConfiguredAttachments();
			return;
		}
		
		if (Realm.Client.Core.GameHost.Instance != null && !string.IsNullOrEmpty(targetKey))
		{
			Realm.Client.Core.GameHost.Instance.RefreshAllPlacedObjectModels(targetKey);
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
		
		if (Realm.Client.Core.GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
		{
			return Realm.Client.Core.GameHost.Instance.GetModelProceduralAnimation(_originalTemplateID) ?? "";
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
		if (Realm.Client.Core.GameHost.Instance == null || string.IsNullOrEmpty(_originalTemplateID)) return;
		
		Realm.Client.Core.GameHost.Instance.SetModelProceduralAnimation(_originalTemplateID, animId);
		Realm.Client.Core.GameHost.Instance.SetModelEnableProceduralAnimation(_originalTemplateID, true);
		Realm.Client.Core.GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
	}

	private void UpdateRiggedAnimationButtonVisibility()
	{
		bool isRigged = IsCurrentModelRigged();
		if (_btnOpenRiggedAnimStudio != null) _btnOpenRiggedAnimStudio.Visible = isRigged;
		if (_btnOpenSocketsAndVfx != null) _btnOpenSocketsAndVfx.Visible = isRigged;
	}


	private bool IsCurrentModelRigged()
	{
		if (!string.Equals(_visualMode, "Mesh", StringComparison.OrdinalIgnoreCase)) return false;

		string meshPath = ResolveMeshPathForRigging();
		
		if (!string.IsNullOrEmpty(meshPath) && TryCheckModelCacheForRigging(meshPath, out bool isRigged))
		{
			return isRigged;
		}

		return CheckSceneGraphForRigging();
	}

	private string ResolveMeshPathForRigging()
	{
		string meshPath = !string.IsNullOrEmpty(_modelPath) ? _modelPath : "";
		if (string.IsNullOrEmpty(meshPath) && _currentSelectedObject is Realm.Client.Prop3D prop)
		{
			meshPath = prop.ModelAssetPath;
		}
		if (string.IsNullOrEmpty(meshPath) && Realm.Client.Core.GameHost.Instance != null && !string.IsNullOrEmpty(_originalTemplateID))
		{
			meshPath = Realm.Client.Core.GameHost.Instance.GetModelAssetKey(_originalTemplateID) ?? "";
		}
		return meshPath;
	}

	private bool TryCheckModelCacheForRigging(string meshPath, out bool isRigged)
	{
		isRigged = false;
		Node loadedNode = ModelCache.GetModel(meshPath);
		if (loadedNode == null)
		{
			string resolved = ModelCache.ResolveModelPath(meshPath);
			if (!string.IsNullOrEmpty(resolved)) loadedNode = ModelCache.GetModel(resolved);
		}
		if (loadedNode != null)
		{
			isRigged = SkeletonValidator.FindSkeleton(loadedNode) != null;
			return true;
		}
		return false;
	}

	private bool CheckSceneGraphForRigging()
	{
		if (_currentSelectedObject == null || !GodotObject.IsInstanceValid(_currentSelectedObject)) return false;

		Node modelRoot = _currentSelectedObject;
		if (_currentSelectedObject is Realm.Client.Unit3D u && u.ModelNode != null)
		{
			modelRoot = u.ModelNode;
		}
		else if (_currentSelectedObject is Realm.Client.Prop3D p)
		{
			modelRoot = p.GetNodeOrNull<Node3D>("VisualModel") ?? _currentSelectedObject;
		}

		return SkeletonValidator.FindSkeleton(modelRoot) != null;
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
		string wsPath = Services.MapWorkspaceService.GetActiveWorkspacePath();
		var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		bool isMesh = string.Equals(_visualMode, "Mesh", StringComparison.OrdinalIgnoreCase);

		if (isMesh)
		{
			ScanCompatibleMeshes(wsPath, allFolders, results);
		}
		else
		{
			ScanCompatibleDecals(wsPath, results);
		}

		var list = results.ToList();
		list.Sort();
		return list;
	}


	private void ScanCompatibleMeshes(string wsPath, bool allFolders, HashSet<string> results)
	{
		ScanMeshMetadataAssets(wsPath, allFolders, results);
		ScanMeshDirectoryAssets(wsPath, allFolders, results);

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

	private void ScanMeshMetadataAssets(string wsPath, bool allFolders, HashSet<string> results)
	{
		try
		{
			var assets = MapAssetHelper.LoadAssets(wsPath);
			if (assets == null) return;
			
			foreach (var groupKvp in assets.GetAllCategories())
			{
				bool matches = allFolders || IsCategoryMatch(groupKvp.Key);
				if (!matches) continue;
				
				foreach (var itemKvp in groupKvp.Value)
				{
					if (itemKvp.Key.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
					{
						results.Add(Path.GetFileName(itemKvp.Key));
					}
				}
			}
		}
		catch { }
	}

	private bool IsCategoryMatch(string groupName)
	{
		if (string.IsNullOrEmpty(groupName)) return false;
		
		return _category switch
		{
			"units" => IsUnitGroup(groupName),
			"buildings" => IsBuildingGroup(groupName),
			"resources" => IsResourceGroup(groupName),
			"props" => IsPropGroup(groupName),
			_ => true
		};
	}

	private bool IsUnitGroup(string groupName)
	{
		return groupName.Equals("Character", StringComparison.OrdinalIgnoreCase) || 
		       groupName.Equals("units", StringComparison.OrdinalIgnoreCase) || 
		       groupName.Equals("characters", StringComparison.OrdinalIgnoreCase);
	}

	private bool IsBuildingGroup(string groupName)
	{
		return groupName.Equals("Building", StringComparison.OrdinalIgnoreCase) || 
		       groupName.Equals("buildings", StringComparison.OrdinalIgnoreCase);
	}

	private bool IsResourceGroup(string groupName)
	{
		return groupName.Equals("Resource", StringComparison.OrdinalIgnoreCase) || 
		       groupName.Equals("resources", StringComparison.OrdinalIgnoreCase) || 
		       groupName.Equals("Prop", StringComparison.OrdinalIgnoreCase) || 
		       groupName.Equals("props", StringComparison.OrdinalIgnoreCase);
	}

	private bool IsPropGroup(string groupName)
	{
		return groupName.Equals("Prop", StringComparison.OrdinalIgnoreCase) || 
		       groupName.Equals("props", StringComparison.OrdinalIgnoreCase) || 
		       groupName.Equals("Item", StringComparison.OrdinalIgnoreCase) || 
		       groupName.Equals("items", StringComparison.OrdinalIgnoreCase) || 
		       groupName.Equals("Resource", StringComparison.OrdinalIgnoreCase) || 
		       groupName.Equals("resources", StringComparison.OrdinalIgnoreCase);
	}

	private void ScanMeshDirectoryAssets(string wsPath, bool allFolders, HashSet<string> results)
	{
		string[] modelSubFolders = GetModelSubFolders(allFolders);
		string[] baseLocations = GetBaseLocations(wsPath);

		foreach (var baseLoc in baseLocations)
		{
			if (!Directory.Exists(baseLoc)) continue;
			
			ScanSubDirectories(baseLoc, modelSubFolders, results);
			ScanTopDirectory(baseLoc, results);
		}
	}

	private string[] GetModelSubFolders(bool allFolders)
	{
		return _category switch
		{
			"units" => allFolders ? new[] { "units", "buildings", "props", "resources", "items", "projectiles", "attachments" } : new[] { "units" },
			"buildings" => allFolders ? new[] { "units", "buildings", "props", "resources", "items", "projectiles", "attachments" } : new[] { "buildings" },
			"resources" => allFolders ? new[] { "units", "buildings", "props", "resources", "items", "projectiles", "attachments" } : new[] { "resources", "props" },
			"props" => allFolders ? new[] { "units", "buildings", "props", "resources", "items", "projectiles", "attachments" } : new[] { "props", "resources", "items" },
			_ => new[] { "units", "buildings", "props", "resources", "items", "projectiles", "attachments" }
		};
	}

	private string[] GetBaseLocations(string wsPath)
	{
		return new[]
		{
			Path.Combine(wsPath, "Assets", "models"),
			Path.Combine("MapTemplate", "Assets", "models"),
			Path.Combine(ProjectSettings.GlobalizePath("res://"), "Assets", "models"),
			Path.Combine(ProjectSettings.GlobalizePath("res://"), "MapTemplate", "Assets", "models")
		};
	}

	private void ScanSubDirectories(string baseLoc, string[] modelSubFolders, HashSet<string> results)
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
	}

	private void ScanTopDirectory(string baseLoc, HashSet<string> results)
	{
		foreach (var file in Directory.GetFiles(baseLoc, "*.rmesh", SearchOption.TopDirectoryOnly))
		{
			results.Add(Path.GetFileName(file));
		}
	}

	private void ScanCompatibleDecals(string wsPath, HashSet<string> results)
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
						if (itemKvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
						{
							results.Add(Path.GetFileName(itemKvp.Key));
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


	private List<string> ScanPortraitModels(bool allFolders)
	{
		string wsPath = Services.MapWorkspaceService.GetActiveWorkspacePath();
		var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		LoadPortraitMetadata(wsPath, results);

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
			ScanFallbackAssets(allFolders, results);
		}

		var list = results.ToList();
		list.Sort();
		return list;
	}

	private void LoadPortraitMetadata(string wsPath, HashSet<string> results)
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
						if (itemKvp.Key.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
						{
							results.Add(Path.GetFileName(itemKvp.Key));
						}
					}
				}
			}
		}
		catch { }
	}

	private void ScanFallbackAssets(bool allFolders, HashSet<string> results)
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
			string wsPath = Services.MapWorkspaceService.GetActiveWorkspacePath();
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

		string objectId = Realm.Client.Core.GameHost.Instance?.GetSelectedEntityOrAssetKey(selectedObject) ?? string.Empty;

		if (selectedObject is Realm.Client.Unit3D unit)
		{
			return ResolveUnitCategoryAndId(unit, objectId);
		}

		if (selectedObject is Realm.Client.Prop3D prop)
		{
			return ResolvePropCategoryAndId(prop, objectId);
		}

		return ResolvePrefixOrRegistryCategoryAndId(objectId);
	}

	private static (string Category, string ObjectId) ResolveUnitCategoryAndId(Realm.Client.Unit3D unit, string objectId)
	{
		string cat = unit.IsBuilding ? "buildings" : "units";
		string id = !string.IsNullOrEmpty(unit.UnitId) ? unit.UnitId : objectId;
		return (cat, id);
	}

	private static (string Category, string ObjectId) ResolvePropCategoryAndId(Realm.Client.Prop3D prop, string objectId)
	{
		string id = !string.IsNullOrEmpty(prop.PropId) ? prop.PropId : objectId;
		string cat = (id.StartsWith("resource/", StringComparison.OrdinalIgnoreCase) ||
					  (Realm.Client.Core.GameHost.ResourceRegistry != null && Realm.Client.Core.GameHost.ResourceRegistry.ContainsKey(id)))
			? "resources"
			: "props";
		return (cat, id);
	}

	private static (string Category, string ObjectId) ResolvePrefixOrRegistryCategoryAndId(string objectId)
	{
		if (!string.IsNullOrEmpty(objectId))
		{
			if (TryGetCategoryFromPrefix(objectId, out string cat)) return (cat, objectId);
			if (TryGetCategoryFromRegistry(objectId, out string regCat)) return (regCat, objectId);
		}

		return ("props", objectId);
	}

	private static bool TryGetCategoryFromPrefix(string objectId, out string category)
	{
		if (objectId.StartsWith("unit/", StringComparison.OrdinalIgnoreCase)) { category = "units"; return true; }
		if (objectId.StartsWith("building/", StringComparison.OrdinalIgnoreCase)) { category = "buildings"; return true; }
		if (objectId.StartsWith("resource/", StringComparison.OrdinalIgnoreCase)) { category = "resources"; return true; }
		if (objectId.StartsWith("prop/", StringComparison.OrdinalIgnoreCase)) { category = "props"; return true; }
		category = null;
		return false;
	}

	private static bool TryGetCategoryFromRegistry(string objectId, out string category)
	{
		if (Realm.Client.Core.GameHost.BuildingRegistry != null && Realm.Client.Core.GameHost.BuildingRegistry.ContainsKey(objectId)) { category = "buildings"; return true; }
		if (Realm.Client.Core.GameHost.UnitRegistry != null && Realm.Client.Core.GameHost.UnitRegistry.ContainsKey(objectId)) { category = "units"; return true; }
		if (Realm.Client.Core.GameHost.ResourceRegistry != null && Realm.Client.Core.GameHost.ResourceRegistry.ContainsKey(objectId)) { category = "resources"; return true; }
		if (Realm.Client.Core.GameHost.PropRegistry != null && Realm.Client.Core.GameHost.PropRegistry.ContainsKey(objectId)) { category = "props"; return true; }
		category = null;
		return false;
	}

	public static void UpdateStaticRegistryModelAndVisualMode(string category, string key, string modelPath, string visualMode)
	{
		if (string.IsNullOrEmpty(key)) return;
		StringName sn = (StringName)key;

		switch (category)
		{
			case "units":
				if (Realm.Client.Core.GameHost.UnitRegistry.TryGetValue(sn, out var u))
				{
					u.ModelPath = modelPath;
					u.VisualMode = visualMode;
					Realm.Client.Core.GameHost.UnitRegistry[sn] = u;
				}
				else
				{
					Realm.Client.Core.GameHost.UnitRegistry[sn] = new UnitMetadata
					{
						TemplateID = key,
						ModelPath = modelPath,
						VisualMode = visualMode
					};
				}
				break;

			case "buildings":
				if (Realm.Client.Core.GameHost.BuildingRegistry.TryGetValue(sn, out var b))
				{
					b.ModelPath = modelPath;
					b.VisualMode = visualMode;
					Realm.Client.Core.GameHost.BuildingRegistry[sn] = b;
				}
				else
				{
					Realm.Client.Core.GameHost.BuildingRegistry[sn] = new UnitMetadata
					{
						TemplateID = key,
						ModelPath = modelPath,
						VisualMode = visualMode
					};
				}
				break;

			case "resources":
				if (Realm.Client.Core.GameHost.ResourceRegistry.TryGetValue(sn, out var r))
				{
					r.ModelPath = modelPath;
					r.VisualMode = visualMode;
					Realm.Client.Core.GameHost.ResourceRegistry[sn] = r;
				}
				else
				{
					Realm.Client.Core.GameHost.ResourceRegistry[sn] = new ResourceMetadata
					{
						TemplateID = key,
						ModelPath = modelPath,
						VisualMode = visualMode
					};
				}
				break;

			case "props":
				if (Realm.Client.Core.GameHost.PropRegistry.TryGetValue(sn, out var p))
				{
					p.ModelPath = modelPath;
					p.VisualMode = visualMode;
					Realm.Client.Core.GameHost.PropRegistry[sn] = p;
				}
				else
				{
					Realm.Client.Core.GameHost.PropRegistry[sn] = new PropMetadata
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
		if (Realm.Client.Core.GameHost.Instance == null || string.IsNullOrEmpty(_originalTemplateID)) return;

		ApplyToMultipleRegistries();

		Realm.Client.Prop3D.InvalidateModelPathCache(_originalTemplateID);
		ModelCache.InvalidateModelPath(_originalTemplateID);
		if (!string.IsNullOrEmpty(_modelPath))
		{
			Realm.Client.Prop3D.InvalidateModelPathCache(_modelPath);
			ModelCache.InvalidateModelPath(_modelPath);
		}

		Realm.Client.Core.GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);

		if (!string.IsNullOrEmpty(_slug))
		{
			Realm.Client.Core.GameHost.Instance.RefreshAllPlacedObjectModels($"{_objectType}/{_slug}");
		}

		if (_currentSelectedObject != null && GodotObject.IsInstanceValid(_currentSelectedObject))
		{
			if (_currentSelectedObject is Realm.Client.Unit3D u3d)
			{
				string fallbackModel = Realm.Client.Core.GameHost.Instance.GetFallbackModelPath(_modelPath, u3d.IsBuilding);
				u3d.LoadModel(fallbackModel);
			}
			else if (_currentSelectedObject is Realm.Client.Prop3D p3d)
			{
				p3d.RefreshPropVisual();
			}
			_modelLocalMinY = CalculateModelLocalMinY(_currentSelectedObject);
		}

		UpdateRiggedAnimationButtonVisibility();
	}

	private void ApplyToMultipleRegistries()
	{
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

		if (_currentSelectedObject is Realm.Client.Unit3D unit && !string.IsNullOrEmpty(unit.UnitId))
		{
			UpdateStaticRegistryModelAndVisualMode(_category, unit.UnitId, _modelPath, _visualMode);
		}
		else if (_currentSelectedObject is Realm.Client.Prop3D prop && !string.IsNullOrEmpty(prop.PropId))
		{
			UpdateStaticRegistryModelAndVisualMode(_category, prop.PropId, _modelPath, _visualMode);
		}
	}

public void OpenForObject(Node selectedObject)
	{
		if (selectedObject == null || !GodotObject.IsInstanceValid(selectedObject) || Realm.Client.Core.GameHost.Instance == null) return;
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
		InitializeDialogState(category, objectId, selectedObject, onApplied);

		object targetLookup = (object)selectedObject ?? (object)_originalTemplateID;

		InitializeModelProperties(targetLookup, _slug);

		string wsPath = Services.MapWorkspaceService.GetActiveWorkspacePath();
		if (MetadataService.Instance.TryLoadMetadata(wsPath, out var meta) && meta != null)
		{
			LoadMetadataByCategory(meta, objectId);
		}

		InitializeSnapshot();

		TitleLabel.Text = $"{TranslationServer.Translate("Visual Properties & Overrides")} - {_originalTemplateID}";

		SyncControls();
		OpenDialog();
	}

	private void InitializeDialogState(string category, string objectId, Node selectedObject, Action<string, string> onApplied)
	{
		_currentSelectedObject = selectedObject;
		_modelLocalMinY = CalculateInitialModelLocalMinY(selectedObject);
		_category = (category ?? "units").ToLowerInvariant();
		_objectType = DetermineObjectType(_category);
		_originalTemplateID = objectId ?? "";
		
		var (parsedType, parsedSlug) = TemplateIDHelper.ParseTemplateID(objectId);
		_slug = !string.IsNullOrEmpty(parsedSlug) ? parsedSlug : TemplateIDHelper.GenerateSlug(objectId);
		_onAppliedCallback = onApplied;
	}

	private float CalculateInitialModelLocalMinY(Node selectedObject)
	{
		if (selectedObject != null) return CalculateModelLocalMinY(selectedObject);
		var editorObj = Realm.Client.Core.GameHost.Instance?.SelectedEditorObject;
		return editorObj != null ? CalculateModelLocalMinY(editorObj) : 0f;
	}

	private string DetermineObjectType(string category)
	{
		return category switch
		{
			"units" => "unit",
			"buildings" => "building",
			"resources" => "resource",
			"props" => "prop",
			"items" => "item",
			_ => "unit"
		};
	}

	private void LoadMetadataByCategory(MapMetadata meta, string objectId)
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

	private void InitializeSnapshot()
	{
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
	}

	private void InitializeModelProperties(object targetLookup, string parsedSlug)
	{
		var host = Realm.Client.Core.GameHost.Instance;
		if (host != null)
		{
			LoadPropertiesFromHost(host, targetLookup);
		}
		else
		{
			SetDefaultModelProperties();
		}
		
		ValidateModelPath();
		
		_portraitModelPath = "";
		_visualMode = DetermineVisualMode();
		_name = !string.IsNullOrEmpty(parsedSlug) ? parsedSlug.Replace("_", " ") : _originalTemplateID;
		_description = "";
	}

	private void LoadPropertiesFromHost(Realm.Client.Core.GameHost host, object targetLookup)
	{
		_scale = host.GetModelScale(targetLookup);
		_yOffset = host.GetModelYOffset(targetLookup);
		_collisionCircle = host.GetModelCollisionCircleRatio(targetLookup);
		_brightness = host.GetModelBrightness(targetLookup);
		_tint = host.GetModelColorTint(targetLookup);
		_normalizeLuminance = host.GetModelNormalizeLuminance(targetLookup);
		_ignorePlayerColor = host.GetModelIgnorePlayerColor(targetLookup);
		_despillPlayerColor = host.GetModelDespillPlayerColor(targetLookup);
		_spawnShader = host.GetModelSpawnShader(targetLookup) ?? "";
		_deathShader = host.GetModelDeathShader(targetLookup) ?? "";
		_enableProceduralAnimation = host.GetModelEnableProceduralAnimation(targetLookup);
		_proceduralAnimation = host.GetModelProceduralAnimation(targetLookup) ?? "";
		_modelPath = host.GetModelAssetKey(targetLookup) ?? "";
	}

	private void SetDefaultModelProperties()
	{
		_scale = 1.0f;
		_yOffset = 0.0f;
		_collisionCircle = 1.0f;
		_brightness = 0.5f;
		_tint = Colors.White;
		_normalizeLuminance = true;
		_ignorePlayerColor = false;
		_despillPlayerColor = false;
		_spawnShader = "";
		_deathShader = "";
		_enableProceduralAnimation = false;
		_proceduralAnimation = "";
		_modelPath = "";
	}

	private void ValidateModelPath()
	{
		if (!string.IsNullOrEmpty(_modelPath) && 
			!_modelPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && 
			!_modelPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) && 
			!_modelPath.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase))
		{
			_modelPath = "";
		}
	}

	private string DetermineVisualMode()
	{
		return (_modelPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) || 
				_modelPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || 
				_modelPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) 
				? "GroundPlane" 
				: "Mesh";
	}

private void LoadFromUnitMetadata(UnitMetadata u)
	{
		ApplyBaseMetadataProperties(u.TemplateID, u.ModelPath, u.PortraitModelPath, u.VisualMode, u.Name, u.Description, u.Scale, u.YOffset, u.CollisionCircle, u.Brightness, u.Tint, u.NormalizeLuminance, u.IgnorePlayerColor, u.DespillPlayerColor, u.SpawnShader, u.DeathShader);
	}

	private void LoadFromResourceMetadata(ResourceMetadata r)
	{
		ApplyBaseMetadataProperties(r.TemplateID, r.ModelPath, r.PortraitModelPath, r.VisualMode, r.Name, r.Description, r.Scale, r.YOffset, r.CollisionCircle, r.Brightness, r.Tint, r.NormalizeLuminance, r.IgnorePlayerColor, r.DespillPlayerColor, r.SpawnShader, r.DeathShader);
	}

	private void LoadFromPropMetadata(PropMetadata p)
	{
		ApplyBaseMetadataProperties(p.TemplateID, p.ModelPath, p.PortraitModelPath, p.VisualMode, p.Name, p.Description, p.Scale, p.YOffset, p.CollisionCircle, p.Brightness, p.Tint, p.NormalizeLuminance, p.IgnorePlayerColor, p.DespillPlayerColor, p.SpawnShader, p.DeathShader);
	}

	private void ApplyBaseMetadataProperties(string templateId, string modelPath, string portraitModelPath, string visualMode, string name, string description, float scale, float yOffset, float collisionCircle, float brightness, string tint, bool normalizeLuminance, bool ignorePlayerColor, bool despillPlayerColor, string spawnShader, string deathShader)
	{
		ApplyModelPaths(modelPath, portraitModelPath);
		ApplyBasicProperties(visualMode, name, description, scale, yOffset, collisionCircle, brightness);
		ApplyColorProperties(tint, normalizeLuminance, ignorePlayerColor, despillPlayerColor);
		ApplyShaderProperties(spawnShader, deathShader);
		ApplyProceduralAnimationProperties(templateId, modelPath);
	}

	private void ApplyModelPaths(string modelPath, string portraitModelPath)
	{
		if (!string.IsNullOrEmpty(modelPath) && (modelPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) || modelPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) || modelPath.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)))
		{
			_modelPath = modelPath;
		}
		if (!string.IsNullOrEmpty(portraitModelPath) && portraitModelPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
		{
			_portraitModelPath = portraitModelPath;
		}
	}

	private void ApplyBasicProperties(string visualMode, string name, string description, float scale, float yOffset, float collisionCircle, float brightness)
	{
		_visualMode = !string.IsNullOrEmpty(visualMode) ? visualMode : _visualMode;
		_name = name ?? _name;
		_description = description ?? _description;
		_scale = scale > 0 ? scale : _scale;
		_yOffset = yOffset;
		_collisionCircle = collisionCircle > 0 ? collisionCircle : _collisionCircle;
		_brightness = brightness;
	}

	private void ApplyColorProperties(string tint, bool normalizeLuminance, bool ignorePlayerColor, bool despillPlayerColor)
	{
		_tint = !string.IsNullOrEmpty(tint) && tint.StartsWith("#") ? Color.FromHtml(tint) : _tint;
		_normalizeLuminance = normalizeLuminance;
		_ignorePlayerColor = ignorePlayerColor;
		_despillPlayerColor = despillPlayerColor;
	}

	private void ApplyShaderProperties(string spawnShader, string deathShader)
	{
		if (!string.IsNullOrEmpty(spawnShader)) _spawnShader = spawnShader;
		if (!string.IsNullOrEmpty(deathShader)) _deathShader = deathShader;
	}

	private void ApplyProceduralAnimationProperties(string templateId, string modelPath)
	{
		string normKey = NormalizeModelAssetKey(templateId);
		string normModel = !string.IsNullOrEmpty(modelPath) ? NormalizeModelAssetKey(modelPath) : "";
		if (Realm.Client.Core.GameHost.Instance != null && (!string.IsNullOrEmpty(normKey) || !string.IsNullOrEmpty(normModel)))
		{
			string procAnim = Realm.Client.Core.GameHost.Instance.GetModelProceduralAnimation(templateId);
			if (!string.IsNullOrEmpty(procAnim)) _proceduralAnimation = procAnim;
			_enableProceduralAnimation = Realm.Client.Core.GameHost.Instance.GetModelEnableProceduralAnimation(templateId);
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
			SyncIdentityControls();
			SyncTransformControls();
			SyncAppearanceControls();
			SyncShaderControls();

			ValidateSlug();
			UpdateRiggedAnimationButtonVisibility();
		}
		finally
		{
			_isSyncing = false;
		}
	}

	private void SyncIdentityControls()
	{
		if (_lblObjectTypePrefix != null) _lblObjectTypePrefix.Text = $"{_objectType}/";
		if (_txtSlug != null) _txtSlug.Text = _slug;
		SyncVisualModeControl();
		_setModelPathValue?.Invoke(_modelPath);
		_setPortraitModelPathValue?.Invoke(_portraitModelPath);
		if (_txtName != null) _txtName.Text = _name;
		if (_txtDescription != null) _txtDescription.Text = _description;
	}

	private void SyncTransformControls()
	{
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
	}

	private void SyncAppearanceControls()
	{
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
	}

	private void SyncShaderControls()
	{
		if (_chkEnableProceduralAnim != null) _chkEnableProceduralAnim.ButtonPressed = _enableProceduralAnimation;

		var allShaders = SpawnDeathShaderManager.LoadAllCustomShaders();
		PopulateShaderDropdowns(allShaders);
		SelectActiveShaders(allShaders);

		RefreshProceduralAnimationDropdown(_proceduralAnimation);
	}

	private void PopulateShaderDropdowns(System.Collections.Generic.Dictionary<string, Realm.Client.Utils.CustomShaderConfig> allShaders)
	{
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
	}

	private void SelectActiveShaders(System.Collections.Generic.Dictionary<string, Realm.Client.Utils.CustomShaderConfig> allShaders)
	{
		int spawnIdx = 0;
		int deathIdx = 0;
		int sIdx = 1;
		
		foreach (var s in allShaders)
		{
			if (string.Equals(s.Key, _spawnShader, StringComparison.OrdinalIgnoreCase)) spawnIdx = sIdx;
			if (string.Equals(s.Key, _deathShader, StringComparison.OrdinalIgnoreCase)) deathIdx = sIdx;
			sIdx++;
		}
		
		if (_optSpawnShader != null) _optSpawnShader.Selected = spawnIdx;
		if (_optDeathShader != null) _optDeathShader.Selected = deathIdx;
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
		if (_txtSlug == null || string.IsNullOrWhiteSpace(_txtSlug.Text))
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Object slug cannot be empty."));
			return;
		}

		string animKey = GetSelectedProceduralAnimationKey();
		string spawnKey = GetSelectedSpawnShaderKey();
		string deathKey = GetSelectedDeathShaderKey();

		string newTemplateID = $"{_objectType}/{_slug}";
		string wsPath = Services.MapWorkspaceService.GetActiveWorkspacePath();

		if (!string.Equals(_originalTemplateID, newTemplateID, StringComparison.OrdinalIgnoreCase))
		{
			UpdateStaticRegistryModelAndVisualMode(_category, newTemplateID, _modelPath, _visualMode);
		}

		var currentSnapshot = CreateVisualOverridesSnapshot(spawnKey, deathKey, animKey);

		UpdateMetadata(wsPath, newTemplateID, spawnKey, deathKey);
		
		if (Realm.Client.Core.GameHost.Instance != null)
		{
			FlushAndRefreshGameHost(newTemplateID, spawnKey, deathKey, animKey, wsPath, currentSnapshot);
		}

		Hud?.RefreshEntityPalette();
		_onAppliedCallback?.Invoke(_originalTemplateID, newTemplateID);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Saved visual properties for '{0}'"), newTemplateID));
	}

	private string GetSelectedProceduralAnimationKey()
	{
		if (_optProceduralAnim != null && _optProceduralAnim.Selected > 0)
		{
			var animConfigs = ProceduralAnimationManager.LoadAllConfigs();
			if (_optProceduralAnim.Selected - 1 < animConfigs.Count)
			{
				return animConfigs.ElementAt(_optProceduralAnim.Selected - 1).Key;
			}
		}
		return "";
	}

	private string GetSelectedSpawnShaderKey()
	{
		if (_optSpawnShader != null && _optSpawnShader.Selected > 0)
		{
			var currentShaders = SpawnDeathShaderManager.LoadAllCustomShaders();
			if (_optSpawnShader.Selected - 1 < currentShaders.Count)
			{
				return currentShaders.ElementAt(_optSpawnShader.Selected - 1).Key;
			}
		}
		return "";
	}

	private string GetSelectedDeathShaderKey()
	{
		if (_optDeathShader != null && _optDeathShader.Selected > 0)
		{
			var currentShaders = SpawnDeathShaderManager.LoadAllCustomShaders();
			if (_optDeathShader.Selected - 1 < currentShaders.Count)
			{
				return currentShaders.ElementAt(_optDeathShader.Selected - 1).Key;
			}
		}
		return "";
	}

	private VisualOverridesSnapshot CreateVisualOverridesSnapshot(string spawnKey, string deathKey, string animKey)
	{
		return new VisualOverridesSnapshot
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
			SpawnShader = spawnKey,
			DeathShader = deathKey,
			EnableProceduralAnimation = _enableProceduralAnimation,
			ProceduralAnimation = animKey
		};
	}

	private void UpdateMetadata(string wsPath, string newTemplateID, string spawnKey, string deathKey)
	{
		MetadataService.Instance.UpdateMetadata(wsPath, meta =>
		{
			string tintHex = $"#{_tint.ToHtml(false)}";

			switch (_category)
			{
				case "units":
					ApplyUnitMetadata(meta, newTemplateID, tintHex, spawnKey, deathKey);
					break;
				case "buildings":
					ApplyBuildingMetadata(meta, newTemplateID, tintHex, spawnKey, deathKey);
					break;
				case "resources":
					ApplyResourceMetadata(meta, newTemplateID, tintHex, spawnKey, deathKey);
					break;
				case "props":
					ApplyPropMetadata(meta, newTemplateID, tintHex, spawnKey, deathKey);
					break;
			}
		});
	}

	private void FlushAndRefreshGameHost(string newTemplateID, string spawnKey, string deathKey, string animKey, string wsPath, VisualOverridesSnapshot currentSnapshot)
	{
		FlushModelPropertiesToGameHost(newTemplateID, spawnKey, deathKey, animKey);

		if (!string.Equals(_originalTemplateID, newTemplateID, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(_originalTemplateID))
		{
			FlushModelPropertiesToGameHost(_originalTemplateID, spawnKey, deathKey, animKey);
			Realm.Client.Core.GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
		}

		Realm.Client.Core.GameHost.Instance.LoadUnitMetadata(wsPath);

		InvalidateModelCaches(newTemplateID);

		var action = new EntityVisualEditUndoAction(newTemplateID, _category, _initialSnapshot, currentSnapshot);
		EditorHistoryManager.RecordAction(action);

		Realm.Client.Core.GameHost.Instance.RefreshAllPlacedObjectModels(newTemplateID);
		if (!string.Equals(_originalTemplateID, newTemplateID, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(_originalTemplateID))
		{
			Realm.Client.Core.GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);
		}

		RefreshCurrentSelectedObject();

		Realm.Client.Core.GameHost.Instance.FlushModelYOffsetSave();
		Realm.Client.Core.GameHost.Instance.FlushModelCollisionCircleSave();
	}

	private void InvalidateModelCaches(string newTemplateID)
	{
		Realm.Client.Prop3D.InvalidateModelPathCache(newTemplateID);
		ModelCache.InvalidateModelPath(newTemplateID);
		Realm.Client.Prop3D.InvalidateModelPathCache(_originalTemplateID);
		ModelCache.InvalidateModelPath(_originalTemplateID);
		if (!string.IsNullOrEmpty(_modelPath))
		{
			Realm.Client.Prop3D.InvalidateModelPathCache(_modelPath);
			ModelCache.InvalidateModelPath(_modelPath);
		}
	}

	private void RefreshCurrentSelectedObject()
	{
		if (_currentSelectedObject != null && GodotObject.IsInstanceValid(_currentSelectedObject))
		{
			if (_currentSelectedObject is Realm.Client.Unit3D u3d)
			{
				string fallbackModel = Realm.Client.Core.GameHost.Instance.GetFallbackModelPath(_modelPath, u3d.IsBuilding);
				u3d.LoadModel(fallbackModel);
			}
			else if (_currentSelectedObject is Realm.Client.Prop3D p3d)
			{
				p3d.RefreshPropVisual();
			}
		}
	}

	private void ApplyUnitMetadata(MapMetadata meta, string newTemplateID, string tintHex, string spawnKey, string deathKey)
	{
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
				PathingType = 8, // Fixed from 31
				MaxHp = 100,
				SpawnShader = string.IsNullOrWhiteSpace(spawnKey) ? null : spawnKey,
				DeathShader = string.IsNullOrWhiteSpace(deathKey) ? null : deathKey
			});
		}
	}
	private void ApplyBuildingMetadata(MapMetadata meta, string newTemplateID, string tintHex, string spawnKey, string deathKey)
	{
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
	}

	private void ApplyResourceMetadata(MapMetadata meta, string newTemplateID, string tintHex, string spawnKey, string deathKey)
	{
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
	}

	private void ApplyPropMetadata(MapMetadata meta, string newTemplateID, string tintHex, string spawnKey, string deathKey)
	{
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
	}

	private void FlushModelPropertiesToGameHost(string templateID, string spawnKey, string deathKey, string animKey)
	{
		Realm.Client.Core.GameHost.Instance.SetModelScale(templateID, _scale);
		Realm.Client.Core.GameHost.Instance.SetModelYOffset(templateID, _yOffset);
		Realm.Client.Core.GameHost.Instance.SetModelCollisionCircleRatio(templateID, _collisionCircle);
		Realm.Client.Core.GameHost.Instance.SetModelBrightness(templateID, _brightness);
		Realm.Client.Core.GameHost.Instance.SetModelColorTint(templateID, _tint);
		Realm.Client.Core.GameHost.Instance.SetModelNormalizeLuminance(templateID, _normalizeLuminance);
		Realm.Client.Core.GameHost.Instance.SetModelIgnorePlayerColor(templateID, _ignorePlayerColor);
		Realm.Client.Core.GameHost.Instance.SetModelDespillPlayerColor(templateID, _despillPlayerColor);
		Realm.Client.Core.GameHost.Instance.SetModelSpawnShader(templateID, spawnKey);
		Realm.Client.Core.GameHost.Instance.SetModelDeathShader(templateID, deathKey);
		Realm.Client.Core.GameHost.Instance.SetModelEnableProceduralAnimation(templateID, _enableProceduralAnimation);
		Realm.Client.Core.GameHost.Instance.SetModelProceduralAnimation(templateID, animKey);
	}


	protected override void OnCancel()
	{
		if (Realm.Client.Core.GameHost.Instance == null || string.IsNullOrEmpty(_originalTemplateID)) return;

		RestoreStaticRegistryMode();

		Realm.Client.Core.GameHost.Instance.SetModelScale(_originalTemplateID, _initialSnapshot.Scale);
		Realm.Client.Core.GameHost.Instance.SetModelYOffset(_originalTemplateID, _initialSnapshot.YOffset);
		Realm.Client.Core.GameHost.Instance.SetModelCollisionCircleRatio(_originalTemplateID, _initialSnapshot.CollisionCircleRatio);
		Realm.Client.Core.GameHost.Instance.SetModelBrightness(_originalTemplateID, _initialSnapshot.Brightness);
		Realm.Client.Core.GameHost.Instance.SetModelColorTint(_originalTemplateID, _initialSnapshot.ColorTint);
		Realm.Client.Core.GameHost.Instance.SetModelNormalizeLuminance(_originalTemplateID, _initialSnapshot.NormalizeLuminance);
		Realm.Client.Core.GameHost.Instance.SetModelIgnorePlayerColor(_originalTemplateID, _initialSnapshot.IgnorePlayerColor);
		Realm.Client.Core.GameHost.Instance.SetModelDespillPlayerColor(_originalTemplateID, _initialSnapshot.DespillPlayerColor);
		Realm.Client.Core.GameHost.Instance.SetModelSpawnShader(_originalTemplateID, _initialSnapshot.SpawnShader);
		Realm.Client.Core.GameHost.Instance.SetModelDeathShader(_originalTemplateID, _initialSnapshot.DeathShader);
		Realm.Client.Core.GameHost.Instance.SetModelEnableProceduralAnimation(_originalTemplateID, _initialSnapshot.EnableProceduralAnimation);
		Realm.Client.Core.GameHost.Instance.SetModelProceduralAnimation(_originalTemplateID, _initialSnapshot.ProceduralAnimation);

		Realm.Client.Prop3D.InvalidateModelPathCache(_originalTemplateID);
		ModelCache.InvalidateModelPath(_originalTemplateID);
		if (!string.IsNullOrEmpty(_initialSnapshot.ModelPath))
		{
			Realm.Client.Prop3D.InvalidateModelPathCache(_initialSnapshot.ModelPath);
			ModelCache.InvalidateModelPath(_initialSnapshot.ModelPath);
		}

		Realm.Client.Core.GameHost.Instance.RefreshAllPlacedObjectModels(_originalTemplateID);

		if (_currentSelectedObject != null && GodotObject.IsInstanceValid(_currentSelectedObject))
		{
			if (_currentSelectedObject is Realm.Client.Unit3D u3d)
			{
				string fallback = Realm.Client.Core.GameHost.Instance.GetFallbackModelPath(_initialSnapshot.ModelPath, u3d.IsBuilding);
				u3d.LoadModel(fallback);
			}
			else if (_currentSelectedObject is Realm.Client.Prop3D p3d)
			{
				p3d.RefreshPropVisual();
			}
		}

		Realm.Client.Core.GameHost.Instance.FlushModelYOffsetSave();
		Realm.Client.Core.GameHost.Instance.FlushModelCollisionCircleSave();
		UpdateRiggedAnimationButtonVisibility();
	}

	private void RestoreStaticRegistryMode()
	{
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

		if (_currentSelectedObject is Realm.Client.Unit3D unit && !string.IsNullOrEmpty(unit.UnitId))
		{
			UpdateStaticRegistryModelAndVisualMode(_category, unit.UnitId, _initialSnapshot.ModelPath, _initialSnapshot.VisualMode);
		}
		else if (_currentSelectedObject is Realm.Client.Prop3D prop && !string.IsNullOrEmpty(prop.PropId))
		{
			UpdateStaticRegistryModelAndVisualMode(_category, prop.PropId, _initialSnapshot.ModelPath, _initialSnapshot.VisualMode);
		}
	}


	private static float CalculateModelLocalMinY(Node selectedObject)
	{
		if (selectedObject == null || !GodotObject.IsInstanceValid(selectedObject))
			return 0f;

		Node3D visualNode = null;
		if (selectedObject is Realm.Client.Unit3D unit && unit.ModelNode != null && GodotObject.IsInstanceValid(unit.ModelNode))
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

		CollectModelMinY(visualNode, visualNode, ref minY, ref foundMesh);
		return foundMesh ? minY : 0f;
	}


	private static void CollectModelMinY(Node current, Node3D visualNode, ref float minY, ref bool foundMesh)
	{
		if (current is MeshInstance3D meshInst && meshInst.Mesh != null && meshInst.Visible)
		{
			CheckMeshBounds(meshInst, visualNode, ref minY, ref foundMesh);
		}
		
		foreach (Node child in current.GetChildren())
		{
			if (child is BoneAttachment3D) continue;
			string cName = child.Name.ToString();
			if (cName.StartsWith("PseudoSocket_", StringComparison.OrdinalIgnoreCase)) continue;
			if (cName.StartsWith("SocketAttachment_", StringComparison.OrdinalIgnoreCase)) continue;
			if (cName.StartsWith("Att_", StringComparison.OrdinalIgnoreCase)) continue;
			if (cName.StartsWith("AttVisual_", StringComparison.OrdinalIgnoreCase)) continue;
			
			CollectModelMinY(child, visualNode, ref minY, ref foundMesh);
		}
	}

	private static void CheckMeshBounds(MeshInstance3D meshInst, Node3D visualNode, ref float minY, ref bool foundMesh)
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

		if (Mathf.Abs(relXform.Basis.Determinant()) <= 0.0001f) return;

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