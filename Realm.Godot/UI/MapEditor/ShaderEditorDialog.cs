using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using Realm.Godot.Utils;

public partial class ShaderEditorDialog : FloatingPreview3DDialogBase
{
	private Node3D _simRoot;
	private Node3D _currentModelRoot;

	private LineEdit _txtShaderKey;
	private LineEdit _txtShaderName;
	private OptionButton _optModelPicker;
	private OptionButton _optTransitionMode;
	private OptionButton _optDirection;

	private ColorPickerButton _cpkEdgeColor;
	private HSlider _sldEdgeWidth;
	private Label _lblEdgeWidth;
	private HSlider _sldEdgeEmission;
	private Label _lblEdgeEmission;
	private HSlider _sldNoiseScale;
	private Label _lblNoiseScale;
	private HSlider _sldNoiseRoughness;
	private Label _lblNoiseRoughness;
	private HSlider _sldFresnelPower;
	private Label _lblFresnelPower;
	private HSlider _sldVertexDisplacement;
	private Label _lblVertexDisplacement;
	private HSlider _sldAlphaFade;
	private Label _lblAlphaFade;
	private HSlider _sldDuration;
	private Label _lblDuration;

	private HSlider _sldProgress;
	private Label _lblProgress;
	private CheckBox _chkLoop;

	private CustomShaderConfig _config = new CustomShaderConfig();
	private CustomShaderConfig _snapshot = new CustomShaderConfig();
	private string _selectedModelKey = "";
	private List<string> _availableModels = new();

	private bool _isPlaying = false;
	private bool _isPlayingForward = true;
	private float _currentAnimTime = 0.0f;
	private Action<CustomShaderConfig> _onSaved;

	public ShaderEditorDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Custom Shader & Dissolve Studio"), new Vector2(560, 780))
	{
		BuildControls();
	}

	private void BuildControls()
	{
		// 1. TOP LIVE PREVIEW (3D VIEWPORT)
		var previewContainer = new PanelContainer();
		previewContainer.CustomMinimumSize = new Vector2(0, 220);
		previewContainer.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		BodyContainer.AddChild(previewContainer);

		Add3DPreviewViewport(previewContainer, new Vector2(0, 220));

		Setup3DEnvironment();

		// CAMERA TOOLBAR WITH EMBEDDED CONTROLS
		AddCameraPresetToolbar(BodyContainer, includeBack: true, includeLightingToggle: true);

		// ROW 1: MODEL PICKER
		var modelRow = new HBoxContainer();
		modelRow.AddThemeConstantOverride("separation", 6);

		var lblModel = new Label();
		lblModel.Text = TranslationServer.Translate("Preview Model:");
		lblModel.AddThemeFontSizeOverride("font_size", 11);
		lblModel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		modelRow.AddChild(lblModel);

		_optModelPicker = new OptionButton();
		_optModelPicker.AddThemeFontSizeOverride("font_size", 11);
		_optModelPicker.CustomMinimumSize = new Vector2(160, 24);
		_optModelPicker.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_optModelPicker.ItemSelected += (idx) =>
		{
			if (idx >= 0 && idx < _availableModels.Count)
			{
				_selectedModelKey = _availableModels[(int)idx];
				LoadPreviewModel(_selectedModelKey);
			}
		};
		modelRow.AddChild(_optModelPicker);

		BodyContainer.AddChild(modelRow);

		// ROW 2: PLAYBACK & SCRUBBING
		var playRow = new HBoxContainer();
		playRow.AddThemeConstantOverride("separation", 6);

		AddButton(playRow, "▶ " + TranslationServer.Translate("Spawn"), () => PlayAnimation(true), "Simulate Spawn animation", 10, new Vector2(65, 24));
		AddButton(playRow, "▶ " + TranslationServer.Translate("Death"), () => PlayAnimation(false), "Simulate Death animation", 10, new Vector2(65, 24));
		AddButton(playRow, "⏸", () => _isPlaying = false, "Pause animation", 10, new Vector2(30, 24));

		_chkLoop = new CheckBox();
		_chkLoop.Text = TranslationServer.Translate("Loop");
		_chkLoop.ButtonPressed = true;
		_chkLoop.AddThemeFontSizeOverride("font_size", 10);
		playRow.AddChild(_chkLoop);

		var lblProg = new Label();
		lblProg.Text = TranslationServer.Translate("Progress:");
		lblProg.AddThemeFontSizeOverride("font_size", 10);
		lblProg.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		playRow.AddChild(lblProg);

		_sldProgress = new HSlider();
		_sldProgress.MinValue = 0.0;
		_sldProgress.MaxValue = 1.0;
		_sldProgress.Step = 0.01;
		_sldProgress.Value = 0.5;
		_sldProgress.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_sldProgress.ValueChanged += (val) =>
		{
			_isPlaying = false;
			_currentAnimTime = (float)val * _config.Duration;
			if (_lblProgress != null) _lblProgress.Text = $"{val:F2}";
			UpdateShaderParameters();
		};
		playRow.AddChild(_sldProgress);

		_lblProgress = new Label();
		_lblProgress.Text = "0.50";
		_lblProgress.CustomMinimumSize = new Vector2(35, 0);
		_lblProgress.AddThemeFontSizeOverride("font_size", 10);
		playRow.AddChild(_lblProgress);

		BodyContainer.AddChild(playRow);

		// 2. CONFIGURATION CONTROLS
		var scroll = new ScrollContainer();
		scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;

		var configVBox = new VBoxContainer();
		configVBox.AddThemeConstantOverride("separation", 6);
		configVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scroll.AddChild(configVBox);
		BodyContainer.AddChild(scroll);

		// IDENTIFIERS
		_txtShaderKey = AddTextInput(configVBox, TranslationServer.Translate("Shader Key / ID:"), _config.Key, (val) =>
		{
			_config.Key = val.Trim().ToLowerInvariant().Replace(" ", "_");
		}, "", 140f);

		_txtShaderName = AddTextInput(configVBox, TranslationServer.Translate("Display Name:"), _config.Name, (val) =>
		{
			_config.Name = val;
		}, "", 140f);

		var btnRandomizeAll = new Button();
		btnRandomizeAll.Set("icon_max_width", 0);
		btnRandomizeAll.Text = "🎲 " + TranslationServer.Translate("Randomize All");
		btnRandomizeAll.CustomMinimumSize = new Vector2(0, 28);
		btnRandomizeAll.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
		btnRandomizeAll.AddThemeFontSizeOverride("font_size", 11);
		{
			var mapEdBtnTex = GD.Load<Texture2D>("res://Assets/UI/map_editor_button.png");
			if (mapEdBtnTex != null)
			{
				var normalSb = new StyleBoxTexture { Texture = mapEdBtnTex, ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 4, ContentMarginBottom = 4 };
				var hoverSb = new StyleBoxTexture { Texture = mapEdBtnTex, ModulateColor = new Color(1.25f, 1.2f, 1.0f, 1.0f), ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 4, ContentMarginBottom = 4 };
				var pressedSb = new StyleBoxTexture { Texture = mapEdBtnTex, ModulateColor = new Color(0.85f, 0.8f, 0.7f, 1.0f), ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 4, ContentMarginBottom = 4 };
				btnRandomizeAll.AddThemeStyleboxOverride("normal", normalSb);
				btnRandomizeAll.AddThemeStyleboxOverride("hover", hoverSb);
				btnRandomizeAll.AddThemeStyleboxOverride("pressed", pressedSb);
				btnRandomizeAll.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
			}
			else
			{
				btnRandomizeAll.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
				btnRandomizeAll.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
				btnRandomizeAll.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
			}
		}
		btnRandomizeAll.Pressed += () => RandomizeAllParameters();
		configVBox.AddChild(btnRandomizeAll);

		// TRANSITION MODE & DIRECTION
		string[] modes = new[]
		{
			TranslationServer.Translate("0: Vertical Slice / Wipe").ToString(),
			TranslationServer.Translate("1: Noise Burn & Dissolve").ToString(),
			TranslationServer.Translate("2: Hologram Scanlines").ToString(),
			TranslationServer.Translate("3: Ground Sink & Crumble").ToString(),
			TranslationServer.Translate("4: Radial Pulse / Burn").ToString(),
			TranslationServer.Translate("5: Glitch Pixelate").ToString(),
			TranslationServer.Translate("6: Alpha Fade & Fresnel").ToString()
		};

		_optTransitionMode = AddOptionDropdown(configVBox, TranslationServer.Translate("Transition Pattern:"), modes, _config.TransitionMode, (idx) =>
		{
			_config.TransitionMode = idx;
			UpdateShaderParameters();
		}, 140f);

		string[] dirs = new[]
		{
			TranslationServer.Translate("Bottom to Top (Y+)").ToString(),
			TranslationServer.Translate("Top to Bottom (Y-)").ToString(),
			TranslationServer.Translate("Radial Outward (XZ)").ToString(),
			TranslationServer.Translate("Radial Inward (XZ)").ToString()
		};

		_optDirection = AddOptionDropdown(configVBox, TranslationServer.Translate("Wipe Direction:"), dirs, _config.Direction, (idx) =>
		{
			_config.Direction = idx;
			UpdateShaderParameters();
		}, 140f);

		// COLOR & GLOW
		(_cpkEdgeColor, _) = AddColorPicker(configVBox, TranslationServer.Translate("Edge / Glow Color:"), _config.EdgeColor, (col) =>
		{
			_config.EdgeColor = col;
			UpdateShaderParameters();
		}, 140f);

		(_sldEdgeWidth, _lblEdgeWidth) = AddSlider(configVBox, TranslationServer.Translate("Edge Width:"), 0.001f, 0.30f, 0.005f, _config.EdgeWidth, (val) =>
		{
			_config.EdgeWidth = val;
			UpdateShaderParameters();
		}, "0.000", 140f);

		(_sldEdgeEmission, _lblEdgeEmission) = AddSlider(configVBox, TranslationServer.Translate("Glow Intensity:"), 0.0f, 15.0f, 0.25f, _config.EdgeEmission, (val) =>
		{
			_config.EdgeEmission = val;
			UpdateShaderParameters();
		}, "0.0", 140f);

		// NOISE & DISTORTION
		(_sldNoiseScale, _lblNoiseScale) = AddSlider(configVBox, TranslationServer.Translate("Noise Scale:"), 1.0f, 50.0f, 0.5f, _config.NoiseScale, (val) =>
		{
			_config.NoiseScale = val;
			UpdateShaderParameters();
		}, "0.0", 140f);

		(_sldNoiseRoughness, _lblNoiseRoughness) = AddSlider(configVBox, TranslationServer.Translate("Noise Roughness:"), 0.0f, 1.0f, 0.05f, _config.NoiseRoughness, (val) =>
		{
			_config.NoiseRoughness = val;
			UpdateShaderParameters();
		}, "0.00", 140f);

		(_sldFresnelPower, _lblFresnelPower) = AddSlider(configVBox, TranslationServer.Translate("Fresnel Rim Power:"), 0.5f, 8.0f, 0.25f, _config.FresnelPower, (val) =>
		{
			_config.FresnelPower = val;
			UpdateShaderParameters();
		}, "0.0", 140f);

		(_sldVertexDisplacement, _lblVertexDisplacement) = AddSlider(configVBox, TranslationServer.Translate("Crumble Jitter:"), 0.0f, 0.5f, 0.02f, _config.VertexDisplacement, (val) =>
		{
			_config.VertexDisplacement = val;
			UpdateShaderParameters();
		}, "0.00", 140f);

		(_sldAlphaFade, _lblAlphaFade) = AddSlider(configVBox, TranslationServer.Translate("Alpha Fade:"), 0.1f, 1.0f, 0.05f, _config.AlphaFade, (val) =>
		{
			_config.AlphaFade = val;
			UpdateShaderParameters();
		}, "0.00", 140f);

		(_sldDuration, _lblDuration) = AddSlider(configVBox, TranslationServer.Translate("Default Duration (s):"), 0.2f, 5.0f, 0.1f, _config.Duration, (val) =>
		{
			_config.Duration = val;
		}, "0.0s", 140f);
	}

	public override void _Process(double delta)
	{
		base._Process(delta);

		if (_isPlaying && Visible)
		{
			float speed = (float)delta;
			float dur = _config.Duration > 0.05f ? _config.Duration : 1.0f;

			if (_isPlayingForward)
			{
				_currentAnimTime += speed;
				if (_currentAnimTime >= dur)
				{
					if (_chkLoop != null && _chkLoop.ButtonPressed)
					{
						_currentAnimTime = dur;
						_isPlayingForward = false;
					}
					else
					{
						_currentAnimTime = dur;
						_isPlaying = false;
					}
				}
			}
			else
			{
				_currentAnimTime -= speed;
				if (_currentAnimTime <= 0.0f)
				{
					if (_chkLoop != null && _chkLoop.ButtonPressed)
					{
						_currentAnimTime = 0.0f;
						_isPlayingForward = true;
					}
					else
					{
						_currentAnimTime = 0.0f;
						_isPlaying = false;
					}
				}
			}

			float prog = Mathf.Clamp(_currentAnimTime / dur, 0.0f, 1.0f);
			if (_sldProgress != null)
			{
				_sldProgress.SetValueNoSignal(prog);
			}
			if (_lblProgress != null)
			{
				_lblProgress.Text = $"{prog:F2}";
			}

			UpdateShaderParameters();
		}
	}

	private void PlayAnimation(bool forward)
	{
		_isPlaying = true;
		_isPlayingForward = forward;
		_currentAnimTime = forward ? 0.0f : _config.Duration;
		if (_sldProgress != null) _sldProgress.SetValueNoSignal(forward ? 0.0 : 1.0);
		UpdateShaderParameters();
	}

	public void OpenForShader(string shaderKey, Action<CustomShaderConfig> onSaved = null)
	{
		_onSaved = onSaved;

		PopulateModelList();

		var existing = SpawnDeathShaderManager.GetShaderConfig(shaderKey);
		if (existing != null)
		{
			_config = existing.Clone();
		}
		else
		{
			_config = new CustomShaderConfig
			{
				Key = !string.IsNullOrEmpty(shaderKey) ? shaderKey : "new_shader",
				Name = !string.IsNullOrEmpty(shaderKey) ? shaderKey : "New Custom Shader"
			};
		}

		_snapshot = _config.Clone();
		SyncControlsFromConfig();

		if (_availableModels.Count > 0 && string.IsNullOrEmpty(_selectedModelKey))
		{
			_selectedModelKey = _availableModels[0];
		}
		LoadPreviewModel(_selectedModelKey);

		PlayAnimation(true);
		OpenDialog();
	}

	private void PopulateModelList()
	{
		_availableModels.Clear();
		if (_optModelPicker == null) return;
		_optModelPicker.Clear();

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string modelsDir = Path.Combine(wsPath, "Assets", "models");
		if (Directory.Exists(modelsDir))
		{
			var files = Directory.GetFiles(modelsDir, "*.rmesh", SearchOption.AllDirectories);
			foreach (var f in files)
			{
				string name = Path.GetFileName(f);
				if (!_availableModels.Contains(name))
				{
					_availableModels.Add(name);
				}
			}
		}

		try
		{
			var assets = MapAssetHelper.LoadAssets(wsPath);
			foreach (var categoryName in new[] { "Character", "Building", "Prop", "Item" })
			{
				var catDict = assets.GetCategory(categoryName);
				if (catDict != null)
				{
					foreach (var model in catDict)
					{
						if (!string.IsNullOrEmpty(model.Key) && model.Key.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
						{
							string name = Path.GetFileName(model.Key);
							if (!_availableModels.Contains(name))
							{
								_availableModels.Add(name);
							}
						}
					}
				}
			}
		}
		catch { }

		if (_availableModels.Count == 0)
		{
			_availableModels.Add("(Sample Building Cube)");
			_availableModels.Add("(Sample Unit Capsule)");
		}

		int idx = 0;
		foreach (var m in _availableModels)
		{
			_optModelPicker.AddItem(m, idx++);
		}

		int selectedIdx = !string.IsNullOrEmpty(_selectedModelKey) ? _availableModels.IndexOf(_selectedModelKey) : -1;
		if (selectedIdx >= 0)
		{
			_optModelPicker.Selected = selectedIdx;
		}
		else if (_availableModels.Count > 0)
		{
			_selectedModelKey = _availableModels[0];
			_optModelPicker.Selected = 0;
		}
	}

	private void SyncControlsFromConfig()
	{
		if (_txtShaderKey != null) _txtShaderKey.Text = _config.Key;
		if (_txtShaderName != null) _txtShaderName.Text = _config.Name;
		if (_optTransitionMode != null) _optTransitionMode.Selected = _config.TransitionMode;
		if (_optDirection != null) _optDirection.Selected = _config.Direction;
		if (_cpkEdgeColor != null) _cpkEdgeColor.Color = _config.EdgeColor;
		if (_sldEdgeWidth != null) { _sldEdgeWidth.Value = _config.EdgeWidth; _lblEdgeWidth.Text = $"{_config.EdgeWidth:F3}"; }
		if (_sldEdgeEmission != null) { _sldEdgeEmission.Value = _config.EdgeEmission; _lblEdgeEmission.Text = $"{_config.EdgeEmission:F1}"; }
		if (_sldNoiseScale != null) { _sldNoiseScale.Value = _config.NoiseScale; _lblNoiseScale.Text = $"{_config.NoiseScale:F1}"; }
		if (_sldNoiseRoughness != null) { _sldNoiseRoughness.Value = _config.NoiseRoughness; _lblNoiseRoughness.Text = $"{_config.NoiseRoughness:F2}"; }
		if (_sldFresnelPower != null) { _sldFresnelPower.Value = _config.FresnelPower; _lblFresnelPower.Text = $"{_config.FresnelPower:F1}"; }
		if (_sldVertexDisplacement != null) { _sldVertexDisplacement.Value = _config.VertexDisplacement; _lblVertexDisplacement.Text = $"{_config.VertexDisplacement:F2}"; }
		if (_sldAlphaFade != null) { _sldAlphaFade.Value = _config.AlphaFade; _lblAlphaFade.Text = $"{_config.AlphaFade:F2}"; }
		if (_sldDuration != null) { _sldDuration.Value = _config.Duration; _lblDuration.Text = $"{_config.Duration:F1}s"; }
	}

	private void RandomizeAllParameters()
	{
		_config.TransitionMode = Random.Shared.Next(0, 7);
		_config.Direction = Random.Shared.Next(0, 4);

		float h = (float)Random.Shared.NextDouble();
		float s = (float)(Random.Shared.NextDouble() * 0.5 + 0.5);
		float v = (float)(Random.Shared.NextDouble() * 0.3 + 0.7);
		_config.EdgeColor = Color.FromHsv(h, s, v);

		_config.EdgeWidth = (float)Math.Round(Random.Shared.NextDouble() * (0.20 - 0.01) + 0.01, 3);
		_config.EdgeEmission = (float)Math.Round(Random.Shared.NextDouble() * 10.0 + 1.0, 1);
		_config.NoiseScale = (float)Math.Round(Random.Shared.NextDouble() * 40.0 + 5.0, 1);
		_config.NoiseRoughness = (float)Math.Round(Random.Shared.NextDouble(), 2);
		_config.FresnelPower = (float)Math.Round(Random.Shared.NextDouble() * 5.0 + 1.0, 1);
		_config.VertexDisplacement = (float)Math.Round(Random.Shared.NextDouble() * 0.35, 2);
		_config.AlphaFade = (float)Math.Round(Random.Shared.NextDouble() * 0.8 + 0.2, 2);
		_config.Duration = (float)Math.Round(Random.Shared.NextDouble() * 2.5 + 0.5, 1);

		SyncControlsFromConfig();
		UpdateShaderParameters();
	}

	private void UpdateShaderParameters()
	{
		if (_currentModelRoot != null && GodotObject.IsInstanceValid(_currentModelRoot))
		{
			float prog = _sldProgress != null ? (float)_sldProgress.Value : 0.5f;
			SpawnDeathShaderManager.ApplyShaderPreview(_currentModelRoot, _config, prog);
		}
	}

	private void Setup3DEnvironment()
	{
		_simRoot = new Node3D();
		_simRoot.Name = "SimRoot";
		PreviewSubViewport.AddChild(_simRoot);

		_currentModelRoot = new Node3D();
		_currentModelRoot.Name = "ModelRoot";
		_simRoot.AddChild(_currentModelRoot);

		// Grid floor
		var floor = new MeshInstance3D();
		var planeMesh = new PlaneMesh { Size = new Vector2(10, 10) };
		var mat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.12f, 0.12f, 0.14f),
			Roughness = 0.8f
		};
		floor.Mesh = planeMesh;
		floor.MaterialOverride = mat;
		_simRoot.AddChild(floor);

		UpdateCameraTransform();
	}

	private void LoadPreviewModel(string key)
	{
		if (_currentModelRoot == null) return;
		foreach (Node child in _currentModelRoot.GetChildren())
		{
			child.QueueFree();
		}

		if (string.IsNullOrEmpty(key)) return;

		if (key.StartsWith("("))
		{
			var meshInst = new MeshInstance3D();
			if (key.Contains("Capsule"))
			{
				meshInst.Mesh = new CapsuleMesh { Radius = 0.5f, Height = 1.8f };
				meshInst.Position = new Vector3(0, 0.9f, 0);
			}
			else
			{
				meshInst.Mesh = new BoxMesh { Size = new Vector3(2f, 2f, 2f) };
				meshInst.Position = new Vector3(0, 1.0f, 0);
			}
			var mat = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.7f, 0.5f) };
			meshInst.MaterialOverride = mat;
			_currentModelRoot.AddChild(meshInst);
			CenterAndFrameNode(_currentModelRoot);
			UpdateShaderParameters();
			return;
		}

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string modelPath = null;
		foreach (var sub in new[] { "units", "buildings", "resources", "props", "projectiles", "characters", "items", "attachments", "weapons" })
		{
			string p = Path.Combine(wsPath, "Assets", "models", sub, key);
			if (File.Exists(p)) { modelPath = p; break; }
		}

		if (!File.Exists(modelPath))
		{
			string modelsDir = Path.Combine(wsPath, "Assets", "models");
			if (Directory.Exists(modelsDir))
			{
				var files = Directory.GetFiles(modelsDir, key, SearchOption.AllDirectories);
				if (files.Length > 0 && files[0].EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
				{
					modelPath = files[0];
				}
			}
		}

		Node3D? loadedNode3D = null;
		if (modelPath != null && modelPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && File.Exists(modelPath))
		{
			var loaded = ModelCache.GetModel(modelPath) ?? ModelCache.GetModel(key);
			if (loaded is Node3D n)
			{
				loadedNode3D = n;
			}
			else
			{
				var gltfDoc = new GltfDocument();
				var gltfState = new GltfState();
				byte[] rmeshBytes = File.ReadAllBytes(modelPath);
				byte[] glbBytes = Realm.Shared.ModelOptimization.RmeshFile.GetGlbBytes(rmeshBytes) ?? rmeshBytes;
				if (gltfDoc.AppendFromBuffer(glbBytes, "", gltfState) == Error.Ok)
				{
					var node = gltfDoc.GenerateScene(gltfState);
					if (node is Node3D n3d) loadedNode3D = n3d;
				}
			}
		}

		if (loadedNode3D != null)
		{
			_currentModelRoot.AddChild(loadedNode3D);
			Realm.Godot.Animation.AnimationRetargetingService.TryApplyRiggedIdlePose(loadedNode3D, modelPath ?? key);
			CenterAndFrameNode(loadedNode3D);
		}

		UpdateShaderParameters();
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

	protected override void OnApply()
	{
		if (_txtShaderKey != null && !string.IsNullOrWhiteSpace(_txtShaderKey.Text))
		{
			_config.Key = _txtShaderKey.Text.Trim().ToLowerInvariant().Replace(" ", "_");
		}
		if (_txtShaderName != null && !string.IsNullOrWhiteSpace(_txtShaderName.Text))
		{
			_config.Name = _txtShaderName.Text.Trim();
		}
		if (string.IsNullOrWhiteSpace(_config.Key))
		{
			_config.Key = "shader/custom_shader";
		}
		if (string.IsNullOrWhiteSpace(_config.Name))
		{
			_config.Name = _config.Key;
		}

		SpawnDeathShaderManager.SaveCustomShader(_config);
		_onSaved?.Invoke(_config);

		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Saved shader {0} to metadata.json"), _config.Name));
		CloseDialog();
	}

	protected override void OnCancel()
	{
		_config = _snapshot.Clone();
		CloseDialog();
	}
}
