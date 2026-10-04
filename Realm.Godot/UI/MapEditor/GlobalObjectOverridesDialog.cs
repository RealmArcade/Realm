using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Godot.Utils;

public class GlobalObjectOverridesUndoAction : IEditorAction
{
	private readonly string _assetKey;
	private readonly GlobalObjectOverridesDialog.GlobalOverridesSnapshot _before;
	private readonly GlobalObjectOverridesDialog.GlobalOverridesSnapshot _after;

	public GlobalObjectOverridesUndoAction(string assetKey, GlobalObjectOverridesDialog.GlobalOverridesSnapshot before, GlobalObjectOverridesDialog.GlobalOverridesSnapshot after)
	{
		_assetKey = assetKey;
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

	private void ApplySnapshot(GlobalObjectOverridesDialog.GlobalOverridesSnapshot snapshot)
	{
		if (GameHost.Instance == null || string.IsNullOrEmpty(_assetKey)) return;

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

		GameHost.Instance.RefreshAllPlacedObjectModels(_assetKey);
		GameHost.Instance.FlushModelYOffsetSave();
		GameHost.Instance.FlushModelCollisionCircleSave();
	}
}

public partial class GlobalObjectOverridesDialog : FloatingDialogBase
{
	public struct GlobalOverridesSnapshot
	{
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

	private string _currentAssetKey = "";
	private Node _currentSelectedObject;
	private GlobalOverridesSnapshot _initialSnapshot;
	private bool _isUpdatingUI;
	private float _modelLocalMinY = 0f;
	private float _previousScale = 1.0f;

	private HSlider _sldScale;
	private Label _lblScaleValue;
	private HSlider _sldYOffset;
	private Label _lblYOffsetValue;
	private HSlider _sldCollisionCircle;
	private Label _lblCollisionCircleValue;
	private HSlider _sldBrightness;
	private Label _lblBrightnessValue;
	private HSlider _sldColorTint;
	private ColorPickerButton _cpkColorTint;
	private CheckBox _chkNormalizeLuminance;
	private CheckBox _chkIgnorePlayerColor;
	private CheckBox _chkDespillPlayerColor;
	private OptionButton _optSpawnShader;
	private OptionButton _optDeathShader;
	private CheckBox _chkEnableProceduralAnim;
	private OptionButton _optProceduralAnim;
	private Button _btnOpenProcAnimStudio;

	public GlobalObjectOverridesDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Global Object Overrides"), new Vector2(400, 560))
	{
		BuildControls();
	}

	private void BuildControls()
	{
		AddDescription(BodyContainer, TranslationServer.Translate("Modify visual and collision settings for all instances of this model."));

		var grid = new VBoxContainer();
		grid.AddThemeConstantOverride("separation", 6);
		BodyContainer.AddChild(grid);

		(_sldScale, _lblScaleValue) = AddSlider(grid, TranslationServer.Translate("Scale"), 0.1f, 10.0f, 0.05f, 1.0f, (val) =>
		{
			if (_isUpdatingUI || GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;
			float newScale = val;
			float deltaScale = newScale - _previousScale;
			_previousScale = newScale;

			float oldYOffset = (float)_sldYOffset.Value;
			float newYOffset = oldYOffset - deltaScale * _modelLocalMinY;

			_isUpdatingUI = true;
			if (newYOffset < _sldYOffset.MinValue) _sldYOffset.MinValue = newYOffset - 5.0f;
			if (newYOffset > _sldYOffset.MaxValue) _sldYOffset.MaxValue = newYOffset + 5.0f;
			_sldYOffset.Value = newYOffset;
			_lblYOffsetValue.Text = newYOffset.ToString("0.00");
			_isUpdatingUI = false;

			GameHost.Instance.SetModelScale(_currentAssetKey, newScale);
			GameHost.Instance.SetModelYOffset(_currentAssetKey, newYOffset);
		});

		(_sldYOffset, _lblYOffsetValue) = AddSlider(grid, TranslationServer.Translate("Y-Offset"), -10.0f, 10.0f, 0.05f, 0.0f, (val) =>
		{
			if (_isUpdatingUI || GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;
			GameHost.Instance.SetModelYOffset(_currentAssetKey, val);
		});

		(_sldCollisionCircle, _lblCollisionCircleValue) = AddSlider(grid, TranslationServer.Translate("Collision Circle"), 0.1f, 10.0f, 0.05f, 1.0f, (val) =>
		{
			if (_isUpdatingUI || GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;
			GameHost.Instance.SetModelCollisionCircleRatio(_currentAssetKey, val);
		});

		(_sldBrightness, _lblBrightnessValue) = AddSlider(grid, TranslationServer.Translate("Brightness"), 0.10f, 2.0f, 0.02f, 0.5f, (val) =>
		{
			if (_isUpdatingUI || GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;
			GameHost.Instance.SetModelBrightness(_currentAssetKey, val);
		});

		(_cpkColorTint, _sldColorTint) = AddColorPicker(grid, TranslationServer.Translate("Tint"), Colors.White, (color) =>
		{
			if (_isUpdatingUI || GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;
			GameHost.Instance.SetModelColorTint(_currentAssetKey, color);
		});

		_chkNormalizeLuminance = AddCheckBox(grid, TranslationServer.Translate("Normalize Luminosity"), true, (pressed) =>
		{
			if (_isUpdatingUI || GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;
			GameHost.Instance.SetModelNormalizeLuminance(_currentAssetKey, pressed);
		});

		_chkIgnorePlayerColor = AddCheckBox(grid, TranslationServer.Translate("Ignore Player Color"), false, (pressed) =>
		{
			if (_isUpdatingUI || GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;
			GameHost.Instance.SetModelIgnorePlayerColor(_currentAssetKey, pressed);
			GameHost.Instance.RefreshAllPlacedObjectModels(_currentAssetKey);
		});

		_chkDespillPlayerColor = AddCheckBox(grid, TranslationServer.Translate("Despill Player Color"), false, (pressed) =>
		{
			if (_isUpdatingUI || GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;
			GameHost.Instance.SetModelDespillPlayerColor(_currentAssetKey, pressed);
			GameHost.Instance.RefreshAllPlacedObjectModels(_currentAssetKey);
		});

		var shaders = SpawnDeathShaderManager.LoadAllCustomShaders();
		var shaderOptions = new List<string> { TranslationServer.Translate("(None)") };
		foreach (var s in shaders.Values)
		{
			shaderOptions.Add(s.Name);
		}

		_optSpawnShader = AddOptionDropdown(grid, TranslationServer.Translate("Spawn Shader:"), shaderOptions.ToArray(), 0, (idx) =>
		{
			if (_isUpdatingUI || GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;
			var currentShaders = SpawnDeathShaderManager.LoadAllCustomShaders();
			string selectedKey = idx > 0 && idx - 1 < currentShaders.Count ? currentShaders.ElementAt(idx - 1).Key : "";
			GameHost.Instance.SetModelSpawnShader(_currentAssetKey, selectedKey);
		});

		_optDeathShader = AddOptionDropdown(grid, TranslationServer.Translate("Death Shader:"), shaderOptions.ToArray(), 0, (idx) =>
		{
			if (_isUpdatingUI || GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;
			var currentShaders = SpawnDeathShaderManager.LoadAllCustomShaders();
			string selectedKey = idx > 0 && idx - 1 < currentShaders.Count ? currentShaders.ElementAt(idx - 1).Key : "";
			GameHost.Instance.SetModelDeathShader(_currentAssetKey, selectedKey);
		});

		_chkEnableProceduralAnim = AddCheckBox(grid, TranslationServer.Translate("Enable Procedural Sway / Wind"), false, (pressed) =>
		{
			if (_isUpdatingUI || GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;
			GameHost.Instance.SetModelEnableProceduralAnimation(_currentAssetKey, pressed);
			GameHost.Instance.RefreshAllPlacedObjectModels(_currentAssetKey);
		});

		var animConfigs = ProceduralAnimationManager.LoadAllConfigs();
		var animOptions = new List<string> { TranslationServer.Translate("(None)") };
		foreach (var a in animConfigs.Values)
		{
			animOptions.Add(a.Name);
		}

		_optProceduralAnim = AddOptionDropdown(grid, TranslationServer.Translate("Procedural Motion Profile:"), animOptions.ToArray(), 0, (idx) =>
		{
			if (_isUpdatingUI || GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;
			var currentConfigs = ProceduralAnimationManager.LoadAllConfigs();
			string selectedKey = idx > 0 && idx - 1 < currentConfigs.Count ? currentConfigs.ElementAt(idx - 1).Key : "";
			GameHost.Instance.SetModelProceduralAnimation(_currentAssetKey, selectedKey);
			GameHost.Instance.RefreshAllPlacedObjectModels(_currentAssetKey);
		});

		_btnOpenProcAnimStudio = AddButton(grid, "✨ " + TranslationServer.Translate("Procedural Animation Studio..."), () =>
		{
			var currentConfigs = ProceduralAnimationManager.LoadAllConfigs();
			string selectedKey = "";
			if (_optProceduralAnim != null && _optProceduralAnim.Selected > 0 && _optProceduralAnim.Selected - 1 < currentConfigs.Count)
			{
				selectedKey = currentConfigs.ElementAt(_optProceduralAnim.Selected - 1).Key;
			}
			if (string.IsNullOrEmpty(selectedKey))
			{
				selectedKey = GameHost.Instance?.GetModelProceduralAnimation(_currentAssetKey) ?? "";
			}

			var cfg = ProceduralAnimationManager.GetConfig(selectedKey) ?? new ProceduralAnimationConfig { Id = _currentAssetKey + "_anim", Name = _currentAssetKey + " Animation" };
			string selectedMesh = GameHost.Instance?.GetModelAssetKey(_currentSelectedObject ?? (object)_currentAssetKey) ?? _currentAssetKey;
			Hud?.OpenProceduralAnimationStudioDialog(cfg, (savedCfg) =>
			{
				if (savedCfg != null)
				{
					GameHost.Instance?.SetModelProceduralAnimation(_currentAssetKey, savedCfg.Id);
					GameHost.Instance?.SetModelEnableProceduralAnimation(_currentAssetKey, true);
					GameHost.Instance?.RefreshAllPlacedObjectModels(_currentAssetKey);
					RefreshProceduralAnimationDropdown(savedCfg.Id);
				}
			}, selectedMesh);
		}, "Open Procedural Animation Studio to edit math formulas and motion parameters", 11, new Vector2(0, 28));
	}

	public void OpenForObject(Node selectedObject)
	{
		if (selectedObject == null || !GodotObject.IsInstanceValid(selectedObject) || GameHost.Instance == null) return;

		_currentSelectedObject = selectedObject;
		_currentAssetKey = GameHost.Instance.GetSelectedEntityOrAssetKey(selectedObject);
		if (string.IsNullOrEmpty(_currentAssetKey)) return;

		_modelLocalMinY = CalculateModelLocalMinY(selectedObject);

		_initialSnapshot = new GlobalOverridesSnapshot
		{
			Scale = GameHost.Instance.GetModelScale(selectedObject),
			YOffset = GameHost.Instance.GetModelYOffset(selectedObject),
			CollisionCircleRatio = GameHost.Instance.GetModelCollisionCircleRatio(selectedObject),
			Brightness = GameHost.Instance.GetModelBrightness(selectedObject),
			ColorTint = GameHost.Instance.GetModelColorTint(selectedObject),
			NormalizeLuminance = GameHost.Instance.GetModelNormalizeLuminance(selectedObject),
			IgnorePlayerColor = GameHost.Instance.GetModelIgnorePlayerColor(selectedObject),
			DespillPlayerColor = GameHost.Instance.GetModelDespillPlayerColor(selectedObject),
			SpawnShader = GameHost.Instance.GetModelSpawnShader(selectedObject),
			DeathShader = GameHost.Instance.GetModelDeathShader(selectedObject),
			EnableProceduralAnimation = GameHost.Instance.GetModelEnableProceduralAnimation(selectedObject),
			ProceduralAnimation = GameHost.Instance.GetModelProceduralAnimation(selectedObject)
		};

		_previousScale = _initialSnapshot.Scale;

		TitleLabel.Text = $"{TranslationServer.Translate("Global Overrides")} - {_currentAssetKey}";

		_isUpdatingUI = true;
		if (_initialSnapshot.Scale < _sldScale.MinValue) _sldScale.MinValue = _initialSnapshot.Scale;
		if (_initialSnapshot.Scale > _sldScale.MaxValue) _sldScale.MaxValue = _initialSnapshot.Scale;
		_sldScale.Value = _initialSnapshot.Scale;
		_lblScaleValue.Text = _initialSnapshot.Scale.ToString("0.00");

		if (_initialSnapshot.YOffset < _sldYOffset.MinValue) _sldYOffset.MinValue = _initialSnapshot.YOffset - 5.0f;
		if (_initialSnapshot.YOffset > _sldYOffset.MaxValue) _sldYOffset.MaxValue = _initialSnapshot.YOffset + 5.0f;
		_sldYOffset.Value = _initialSnapshot.YOffset;
		_lblYOffsetValue.Text = _initialSnapshot.YOffset.ToString("0.00");

		_sldCollisionCircle.Value = _initialSnapshot.CollisionCircleRatio;
		_lblCollisionCircleValue.Text = _initialSnapshot.CollisionCircleRatio.ToString("0.00");

		_sldBrightness.Value = _initialSnapshot.Brightness;
		_lblBrightnessValue.Text = _initialSnapshot.Brightness.ToString("0.00");

		_cpkColorTint.Color = _initialSnapshot.ColorTint;
		if (Mathf.Abs(_initialSnapshot.ColorTint.R - 1.0f) < 0.001f && Mathf.Abs(_initialSnapshot.ColorTint.G - 1.0f) < 0.001f && Mathf.Abs(_initialSnapshot.ColorTint.B - 1.0f) < 0.001f)
		{
			_sldColorTint.Value = 0.0f;
		}
		else
		{
			_sldColorTint.Value = _initialSnapshot.ColorTint.H;
		}

		_chkNormalizeLuminance.ButtonPressed = _initialSnapshot.NormalizeLuminance;
		_chkIgnorePlayerColor.ButtonPressed = _initialSnapshot.IgnorePlayerColor;
		_chkDespillPlayerColor.ButtonPressed = _initialSnapshot.DespillPlayerColor;
		if (_chkEnableProceduralAnim != null) _chkEnableProceduralAnim.ButtonPressed = _initialSnapshot.EnableProceduralAnimation;

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
			if (string.Equals(s.Key, _initialSnapshot.SpawnShader, StringComparison.OrdinalIgnoreCase))
			{
				spawnIdx = sIdx;
			}
			if (string.Equals(s.Key, _initialSnapshot.DeathShader, StringComparison.OrdinalIgnoreCase))
			{
				deathIdx = sIdx;
			}
			sIdx++;
		}
		if (_optSpawnShader != null) _optSpawnShader.Selected = spawnIdx;
		if (_optDeathShader != null) _optDeathShader.Selected = deathIdx;

		RefreshProceduralAnimationDropdown(_initialSnapshot.ProceduralAnimation);

		_isUpdatingUI = false;

		OpenDialog();
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
		if (GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;

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

		var currentSnapshot = new GlobalOverridesSnapshot
		{
			Scale = (float)_sldScale.Value,
			YOffset = (float)_sldYOffset.Value,
			CollisionCircleRatio = (float)_sldCollisionCircle.Value,
			Brightness = (float)_sldBrightness.Value,
			ColorTint = _cpkColorTint.Color,
			NormalizeLuminance = _chkNormalizeLuminance.ButtonPressed,
			IgnorePlayerColor = _chkIgnorePlayerColor.ButtonPressed,
			DespillPlayerColor = _chkDespillPlayerColor.ButtonPressed,
			SpawnShader = spawnKey,
			DeathShader = deathKey,
			EnableProceduralAnimation = _chkEnableProceduralAnim != null && _chkEnableProceduralAnim.ButtonPressed,
			ProceduralAnimation = animKey
		};

		GameHost.Instance.SetModelScale(_currentAssetKey, currentSnapshot.Scale);
		GameHost.Instance.SetModelYOffset(_currentAssetKey, currentSnapshot.YOffset);
		GameHost.Instance.SetModelCollisionCircleRatio(_currentAssetKey, currentSnapshot.CollisionCircleRatio);
		GameHost.Instance.SetModelBrightness(_currentAssetKey, currentSnapshot.Brightness);
		GameHost.Instance.SetModelColorTint(_currentAssetKey, currentSnapshot.ColorTint);
		GameHost.Instance.SetModelNormalizeLuminance(_currentAssetKey, currentSnapshot.NormalizeLuminance);
		GameHost.Instance.SetModelIgnorePlayerColor(_currentAssetKey, currentSnapshot.IgnorePlayerColor);
		GameHost.Instance.SetModelDespillPlayerColor(_currentAssetKey, currentSnapshot.DespillPlayerColor);
		GameHost.Instance.SetModelSpawnShader(_currentAssetKey, spawnKey);
		GameHost.Instance.SetModelDeathShader(_currentAssetKey, deathKey);
		GameHost.Instance.SetModelEnableProceduralAnimation(_currentAssetKey, currentSnapshot.EnableProceduralAnimation);
		GameHost.Instance.SetModelProceduralAnimation(_currentAssetKey, animKey);

		var action = new GlobalObjectOverridesUndoAction(_currentAssetKey, _initialSnapshot, currentSnapshot);
		EditorHistoryManager.RecordAction(action);

		GameHost.Instance.RefreshAllPlacedObjectModels(_currentAssetKey);
		GameHost.Instance.FlushModelYOffsetSave();
		Hud?.ShowFeedback(TranslationServer.Translate("Global object overrides applied"));
	}

	protected override void OnCancel()
	{
		if (GameHost.Instance == null || string.IsNullOrEmpty(_currentAssetKey)) return;

		GameHost.Instance.SetModelScale(_currentAssetKey, _initialSnapshot.Scale);
		GameHost.Instance.SetModelYOffset(_currentAssetKey, _initialSnapshot.YOffset);
		GameHost.Instance.SetModelCollisionCircleRatio(_currentAssetKey, _initialSnapshot.CollisionCircleRatio);
		GameHost.Instance.SetModelBrightness(_currentAssetKey, _initialSnapshot.Brightness);
		GameHost.Instance.SetModelColorTint(_currentAssetKey, _initialSnapshot.ColorTint);
		GameHost.Instance.SetModelNormalizeLuminance(_currentAssetKey, _initialSnapshot.NormalizeLuminance);
		GameHost.Instance.SetModelIgnorePlayerColor(_currentAssetKey, _initialSnapshot.IgnorePlayerColor);
		GameHost.Instance.SetModelDespillPlayerColor(_currentAssetKey, _initialSnapshot.DespillPlayerColor);
		GameHost.Instance.SetModelSpawnShader(_currentAssetKey, _initialSnapshot.SpawnShader);
		GameHost.Instance.SetModelDeathShader(_currentAssetKey, _initialSnapshot.DeathShader);
		GameHost.Instance.SetModelEnableProceduralAnimation(_currentAssetKey, _initialSnapshot.EnableProceduralAnimation);
		GameHost.Instance.SetModelProceduralAnimation(_currentAssetKey, _initialSnapshot.ProceduralAnimation);

		GameHost.Instance.RefreshAllPlacedObjectModels(_currentAssetKey);
		GameHost.Instance.FlushModelYOffsetSave();
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
