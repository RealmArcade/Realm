using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Terrain;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class ProceduralAnimationStudioDialog : FloatingPreview3DDialogBase
{
	public ProceduralAnimationStudioDialog() { }

	private Node3D _simRoot = null!;
	private Node3D _currentModelRoot = null!;

	private LineEdit _txtAnimId = null!;
	private LineEdit _txtAnimName = null!;
	private OptionButton _optPreset = null!;
	private OptionButton _optTemplatePicker = null!;
	private OptionButton _optMotionType = null!;
	private OptionButton _optMaskMode = null!;

	private HSlider _sldMaskMin = null!;
	private Label _lblMaskMin = null!;
	private HSlider _sldMaskMax = null!;
	private Label _lblMaskMax = null!;
	private HSlider _sldMaskPower = null!;
	private Label _lblMaskPower = null!;
	private CheckBox _chkMaskInvert = null!;
	private CheckBox _chkShowHeatmap = null!;

	private readonly HSlider[] _sldRegionMin = new HSlider[3];
	private readonly Label[] _lblRegionMin = new Label[3];
	private readonly HSlider[] _sldRegionMax = new HSlider[3];
	private readonly Label[] _lblRegionMax = new Label[3];
	private static readonly string[] _regionAxisNames = { "X", "Y", "Z" };
	private const float RegionAutoDetectCreaseAngleDegrees = 50.0f;

	private Button _btnRegionAutoDetect = null!;
	private Label _lblRegionAutoDetectStatus = null!;
	private bool _isRegionAutoDetectActive;

	private HSlider _sldSwayFrequency = null!;
	private Label _lblSwayFrequency = null!;
	private HSlider _sldSwayAmplitude = null!;
	private Label _lblSwayAmplitude = null!;
	private HSlider _sldFlutterFrequency = null!;
	private Label _lblFlutterFrequency = null!;
	private HSlider _sldFlutterAmplitude = null!;
	private Label _lblFlutterAmplitude = null!;
	private HSlider _sldWindInfluence = null!;
	private Label _lblWindInfluence = null!;
	private HSlider _sldVelocityDragInfluence = null!;
	private Label _lblVelocityDragInfluence = null!;
	private HSlider _sldWaveTurbulence = null!;
	private Label _lblWaveTurbulence = null!;

	private HSlider _sldImpulseAmplitude = null!;
	private Label _lblImpulseAmplitude = null!;
	private HSlider _sldImpulseFrequency = null!;
	private Label _lblImpulseFrequency = null!;
	private HSlider _sldImpulseDecay = null!;
	private Label _lblImpulseDecay = null!;
	private HSlider _sldImpulseDuration = null!;
	private Label _lblImpulseDuration = null!;

	private HSlider _sldSimVelocity = null!;
	private Label _lblSimVelocity = null!;

	private Label _lblRegionResolution = null!;
	private LineEdit _txtRegionName = null!;
	private HSlider _sldRegionLowerEdge = null!;
	private Label _lblRegionLowerEdge = null!;
	private HSlider _sldRegionUpperEdge = null!;
	private Label _lblRegionUpperEdge = null!;
	private HSlider _sldRegionFeather = null!;
	private Label _lblRegionFeather = null!;
	private bool _suppressRegionSync;

	private ProceduralAnimationConfig _config = new();
	private ProceduralAnimationConfig _snapshot = new();
	private string _selectedTemplateKey = "";
	private List<TemplateItemInfo> _availableTemplates = new();

	private float _impulseStrength = 0.0f;
	private float _impulseTime = 0.0f;
	private float _simVelocityX = 0.0f;

	private Action<ProceduralAnimationConfig>? _onSaved;

	public ProceduralAnimationStudioDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Procedural Animation & Sway Studio"), new Vector2(560, 780))
	{
		BuildControls();
	}

	public override void _Process(double delta)
	{
		base._Process(delta);

		if (_impulseStrength > 0.001f && Visible)
		{
			_impulseTime += (float)delta;
			float dur = _config.ImpulseDuration > 0.05f ? _config.ImpulseDuration : 0.6f;
			if (_impulseTime >= dur)
			{
				_impulseStrength = 0.0f;
				_impulseTime = 0.0f;
			}

			UpdateShaderParameters();
		}
	}

	private void TriggerTestImpulse()
	{
		_impulseStrength = _config.ImpulseAmplitude > 0f ? _config.ImpulseAmplitude : 0.35f;
		_impulseTime = 0.0f;
		UpdateShaderParameters();
	}

	private void PopulatePresetList()
	{
		if (_optPreset == null) return;
		_optPreset.Clear();
		int pIdx = 0;
		foreach (var kvp in ProceduralAnimationManager.LoadAllConfigs())
		{
			_optPreset.AddItem(kvp.Value.Name, pIdx);
			_optPreset.SetItemMetadata(pIdx, kvp.Key);
			pIdx++;
		}
	}

	public void OpenForConfig(ProceduralAnimationConfig? config, Action<ProceduralAnimationConfig>? onSaved = null, string? previewModelKey = null)
	{
		_onSaved = onSaved;
		if (!string.IsNullOrWhiteSpace(previewModelKey))
			_selectedTemplateKey = previewModelKey;

		PopulatePresetList();
		PopulateTemplateList();
		InitializeConfig(config);

		_snapshot = _config.Clone();
		SyncControlsFromConfig();
		SelectPresetOption();
		SelectInitialTemplate();

		OpenDialog();
	}

	private void InitializeConfig(ProceduralAnimationConfig? config)
	{
		if (config != null && !string.IsNullOrWhiteSpace(config.Id))
		{
			var existing = ProceduralAnimationManager.GetConfig(config.Id);
			_config = existing != null ? existing.Clone() : config.Clone();
			return;
		}

		_config = new ProceduralAnimationConfig
		{
			Id = "new_procedural_anim",
			Name = "New Procedural Animation"
		};
	}

	private void SelectPresetOption()
	{
		if (_optPreset == null || string.IsNullOrEmpty(_config.Id)) return;
		for (int i = 0; i < _optPreset.ItemCount; i++)
		{
			string metaKey = _optPreset.GetItemMetadata(i).AsString();
			if (string.Equals(metaKey, _config.Id, StringComparison.OrdinalIgnoreCase))
			{
				_optPreset.Selected = i;
				return;
			}
		}
	}

	private int FindSelectedTemplateIndex()
	{
		if (string.IsNullOrWhiteSpace(_selectedTemplateKey)) return -1;

		string targetName = Path.GetFileName(_selectedTemplateKey);
		string targetWithoutExt = Path.GetFileNameWithoutExtension(_selectedTemplateKey);

		int idx = _availableTemplates.FindIndex(t => string.Equals(t.Id, _selectedTemplateKey, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) return idx;

		idx = _availableTemplates.FindIndex(t => string.Equals(t.ModelPath, _selectedTemplateKey, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) return idx;

		idx = _availableTemplates.FindIndex(t => string.Equals(Path.GetFileName(t.ModelPath), targetName, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) return idx;

		idx = _availableTemplates.FindIndex(t => string.Equals(Path.GetFileNameWithoutExtension(t.Id), targetWithoutExt, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) return idx;

		return _availableTemplates.FindIndex(t => string.Equals(Path.GetFileNameWithoutExtension(t.ModelPath), targetWithoutExt, StringComparison.OrdinalIgnoreCase));
	}

	private void SyncControlsFromConfig()
	{
		UpdateTextControls();
		UpdateMaskControls();
		UpdateSwayAndFlutterControls();
		UpdateImpulseControls();
		UpdateRegionControls();
		UpdateRegionResolutionLabel();
	}

	private void UpdateTextControls()
	{
		if (_txtAnimId != null) _txtAnimId.Text = _config.Id;
		if (_txtAnimName != null) _txtAnimName.Text = _config.Name;
		if (_optMotionType != null) _optMotionType.Selected = (int)_config.MotionType - 1;
		if (_optMaskMode != null) _optMaskMode.Selected = (int)_config.MaskMode;
	}

	private void BuildRegionBoxControls(VBoxContainer parent)
	{
		for (int axis = 0; axis < _regionAxisNames.Length; axis++)
		{			int axisIndex = axis;
			string axisName = _regionAxisNames[axisIndex];

			(_sldRegionMin[axisIndex], _lblRegionMin[axisIndex]) = AddSlider(
				parent,
				string.Format(TranslationServer.Translate("Region Min {0}:"), axisName),
				-10.0f, 20.0f, 0.05f, GetRegionComponent(_config.MaskRegionMin, axisIndex),
				(val) =>
				{
					_config.MaskRegionMin = WithRegionComponent(_config.MaskRegionMin, axisIndex, val);
					UpdateShaderParameters();
				}, "0.00", 150f);

			(_sldRegionMax[axisIndex], _lblRegionMax[axisIndex]) = AddSlider(
				parent,
				string.Format(TranslationServer.Translate("Region Max {0}:"), axisName),
				-10.0f, 30.0f, 0.05f, GetRegionComponent(_config.MaskRegionMax, axisIndex),
				(val) =>
				{
					_config.MaskRegionMax = WithRegionComponent(_config.MaskRegionMax, axisIndex, val);
					UpdateShaderParameters();
				}, "0.00", 150f);
		}

		_btnRegionAutoDetect = AddButton(
			parent,
			"🎯 " + TranslationServer.Translate("Auto-Detect Region From Click"),
			ToggleRegionAutoDetect,
			"Click a part in the preview to raycast it and grow a connected region across its surface edges",
			11,
			new Vector2(0, 26));

		_lblRegionAutoDetectStatus = new Label();
		_lblRegionAutoDetectStatus.Name = "LblRegionAutoDetectStatus_Label";
		_lblRegionAutoDetectStatus.Text = TranslationServer.Translate("Auto-detect is idle. Press the button, then click the part you want to animate.");
		_lblRegionAutoDetectStatus.AddThemeFontSizeOverride("font_size", 10);
		_lblRegionAutoDetectStatus.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_lblRegionAutoDetectStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_lblRegionAutoDetectStatus.CustomMinimumSize = new Vector2(300, 0);
		_lblRegionAutoDetectStatus.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		parent.AddChild(_lblRegionAutoDetectStatus);

		var hintLabel = new Label();
		hintLabel.Name = "LblRegionBoxHint_Label";
		hintLabel.Text = TranslationServer.Translate("Region Box affects only vertices inside this local-space volume (e.g. the cape). Mask Lower Bound acts as the edge feather width and a negative Falloff Power inverts the selection.");
		hintLabel.AddThemeFontSizeOverride("font_size", 10);
		hintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		hintLabel.CustomMinimumSize = new Vector2(300, 0);
		hintLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		parent.AddChild(hintLabel);
	}

	protected override void OnViewportGuiInput(InputEvent @event)
	{
		if (_isRegionAutoDetectActive
		    && @event is InputEventMouseButton regionPickMouseButton
		    && regionPickMouseButton.ButtonIndex == MouseButton.Left
		    && regionPickMouseButton.Pressed)
		{
			TryDetectRegionFromPreviewClick(regionPickMouseButton.Position);
			return;
		}

		base.OnViewportGuiInput(@event);
	}

	private void ToggleRegionAutoDetect()
	{
		SetRegionAutoDetectActive(!_isRegionAutoDetectActive);
	}

	private void SetRegionAutoDetectActive(bool isActive)
	{
		_isRegionAutoDetectActive = isActive;

		if (_btnRegionAutoDetect != null)
		{
			_btnRegionAutoDetect.Text = isActive
				? "🎯 " + TranslationServer.Translate("Click The Part In The Preview...")
				: "🎯 " + TranslationServer.Translate("Auto-Detect Region From Click");
		}

		if (_lblRegionAutoDetectStatus != null)
		{
			_lblRegionAutoDetectStatus.Text = isActive
				? TranslationServer.Translate("Auto-detect is armed. Click the center of the part you want and the region box will grow to its connected surface.")
				: TranslationServer.Translate("Auto-detect is idle. Press the button, then click the part you want to animate.");
		}

		if (isActive && _chkShowHeatmap != null && !_chkShowHeatmap.ButtonPressed)
		{
			_chkShowHeatmap.ButtonPressed = true;
			UpdateShaderParameters();
		}
	}

	private void TryDetectRegionFromPreviewClick(Vector2 viewportPoint)
	{
		if (_currentModelRoot == null || !GodotObject.IsInstanceValid(_currentModelRoot)) return;

		if (!SpatialMeshRegionPicker.TryPickRegion(PreviewCamera, _currentModelRoot, viewportPoint, RegionAutoDetectCreaseAngleDegrees, out SpatialMeshRegionPick pick))
		{
			if (_lblRegionAutoDetectStatus != null)
			{
				_lblRegionAutoDetectStatus.Text = TranslationServer.Translate("Nothing was hit. Click directly on the mesh surface of the part you want to animate.");
			}

			return;
		}

		_config.MaskMode = SpatialMaskMode.RegionBox;
		_config.MaskRegionMin = new Vector3Data(pick.Min.X, pick.Min.Y, pick.Min.Z);
		_config.MaskRegionMax = new Vector3Data(pick.Max.X, pick.Max.Y, pick.Max.Z);

		SetRegionAutoDetectActive(false);
		SyncControlsFromConfig();
		UpdateShaderParameters();

		if (_lblRegionAutoDetectStatus != null)
		{
			_lblRegionAutoDetectStatus.Text = string.Format(
				TranslationServer.Translate("Detected region on {0}: {1} connected triangles, X {2:F2} to {3:F2}, Y {4:F2} to {5:F2}, Z {6:F2} to {7:F2}."),
				pick.MeshName,
				pick.TriangleCount,
				pick.Min.X,
				pick.Max.X,
				pick.Min.Y,
				pick.Max.Y,
				pick.Min.Z,
				pick.Max.Z);
		}

		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Auto-detected the {0} region box from {1} connected triangles."), pick.MeshName, pick.TriangleCount));
	}

	private void BuildSwayRegionControls(VBoxContainer parent)
	{
		AddSectionHeader(parent, "🌳 " + TranslationServer.Translate("PER-MODEL SWAY REGION"), new Color(0.45f, 0.85f, 0.55f));

		_lblRegionResolution = new Label();
		_lblRegionResolution.Name = "LblRegionResolution_Label";
		_lblRegionResolution.AddThemeFontSizeOverride("font_size", 10);
		_lblRegionResolution.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_lblRegionResolution.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_lblRegionResolution.CustomMinimumSize = new Vector2(300, 0);
		_lblRegionResolution.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		parent.AddChild(_lblRegionResolution);

		ProceduralSwayRegionConfig? currentRegion = GetCurrentRegion();
		ProceduralSwayRegionResolution resolvedRegion = ProceduralSwayRegionResolver.Resolve(_config, GetCurrentModelKey());

		_txtRegionName = AddTextInput(parent, TranslationServer.Translate("Region Name:"), currentRegion?.RegionName ?? resolvedRegion.RegionName,
			(value) =>
			{
				if (_suppressRegionSync) return;
				EnsureCurrentRegion().RegionName = value.Trim();
				UpdateRegionResolutionLabel();
			}, TranslationServer.Translate("Canopy").ToString(), 150f);

		(_sldRegionLowerEdge, _lblRegionLowerEdge) = AddSlider(parent, TranslationServer.Translate("Region Lower Edge (%):"), 0.0f, 100.0f, 1.0f,
			(currentRegion?.NormalizedMin.Y ?? resolvedRegion.NormalizedMin.Y) * 100.0f,
			(value) =>
			{
				if (_suppressRegionSync) return;
				EnsureCurrentRegion().NormalizedMin = new Vector3Data(0.0f, value / 100.0f, 0.0f);
				UpdateShaderParameters();
			}, "0", 150f);

		(_sldRegionUpperEdge, _lblRegionUpperEdge) = AddSlider(parent, TranslationServer.Translate("Region Upper Edge (%):"), 0.0f, 100.0f, 1.0f,
			(currentRegion?.NormalizedMax.Y ?? resolvedRegion.NormalizedMax.Y) * 100.0f,
			(value) =>
			{
				if (_suppressRegionSync) return;
				EnsureCurrentRegion().NormalizedMax = new Vector3Data(1.0f, value / 100.0f, 1.0f);
				UpdateShaderParameters();
			}, "0", 150f);

		(_sldRegionFeather, _lblRegionFeather) = AddSlider(parent, TranslationServer.Translate("Region Feather (%):"), 0.0f, 50.0f, 1.0f,
			(currentRegion?.Feather ?? resolvedRegion.Feather) * 100.0f,
			(value) =>
			{
				if (_suppressRegionSync) return;
				EnsureCurrentRegion().Feather = value / 100.0f;
				UpdateShaderParameters();
			}, "0", 150f);

		var hintLabel = new Label();
		hintLabel.Name = "LblRegionHint_Label";
		hintLabel.Text = TranslationServer.Translate("The region is a percentage of the model bounds, so it follows any model scale - adjust the edges and confirm the motion in the preview.");
		hintLabel.AddThemeFontSizeOverride("font_size", 10);
		hintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		hintLabel.CustomMinimumSize = new Vector2(300, 0);
		hintLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		parent.AddChild(hintLabel);
	}

	private string GetCurrentModelKey()
	{
		if (string.IsNullOrWhiteSpace(_selectedTemplateKey)) return string.Empty;
		return _selectedTemplateKey.StartsWith("(", StringComparison.Ordinal) ? string.Empty : _selectedTemplateKey;
	}

	private ProceduralSwayRegionConfig? GetCurrentRegion()
	{
		if (_config.SwayRegions == null || _config.SwayRegions.Count == 0) return null;

		string modelKey = GetCurrentModelKey();
		if (string.IsNullOrWhiteSpace(modelKey)) return null;

		for (int i = 0; i < _config.SwayRegions.Count; i++)
		{
			ProceduralSwayRegionConfig region = _config.SwayRegions[i];
			if (region == null || string.IsNullOrWhiteSpace(region.ModelKey)) continue;
			if (ProceduralSwayRegionResolver.MatchesModelKey(region.ModelKey, modelKey)) return region;
		}

		return null;
	}

	private ProceduralSwayRegionConfig EnsureCurrentRegion()
	{
		ProceduralSwayRegionConfig? currentRegion = GetCurrentRegion();
		if (currentRegion != null) return currentRegion;

		string modelKey = GetCurrentModelKey();
		ProceduralSwayRegionResolution resolvedRegion = ProceduralSwayRegionResolver.Resolve(_config, modelKey);
		var createdRegion = new ProceduralSwayRegionConfig
		{
			ModelKey = modelKey,
			RegionName = resolvedRegion.RegionName,
			NormalizedMin = resolvedRegion.NormalizedMin,
			NormalizedMax = resolvedRegion.NormalizedMax,
			Feather = resolvedRegion.Feather,
			MaskPower = resolvedRegion.MaskPower,
			MaskInvert = resolvedRegion.MaskInvert
		};

		_config.SwayRegions ??= new List<ProceduralSwayRegionConfig>();
		_config.SwayRegions.Add(createdRegion);
		return createdRegion;
	}

	private void UpdateRegionControls()
	{
		ProceduralSwayRegionConfig? currentRegion = GetCurrentRegion();
		ProceduralSwayRegionResolution resolvedRegion = ProceduralSwayRegionResolver.Resolve(_config, GetCurrentModelKey());

		_suppressRegionSync = true;
		try
		{
			if (_txtRegionName != null) _txtRegionName.Text = currentRegion?.RegionName ?? resolvedRegion.RegionName;

			SetSlider(_sldRegionLowerEdge, _lblRegionLowerEdge, (currentRegion?.NormalizedMin.Y ?? resolvedRegion.NormalizedMin.Y) * 100.0f, "0");
			SetSlider(_sldRegionUpperEdge, _lblRegionUpperEdge, (currentRegion?.NormalizedMax.Y ?? resolvedRegion.NormalizedMax.Y) * 100.0f, "0");
			SetSlider(_sldRegionFeather, _lblRegionFeather, (currentRegion?.Feather ?? resolvedRegion.Feather) * 100.0f, "0");
		}
		finally
		{
			_suppressRegionSync = false;
		}
	}

	private void UpdateRegionResolutionLabel()
	{
		if (_lblRegionResolution == null) return;

		string modelKey = GetCurrentModelKey();
		ProceduralSwayRegionResolution resolution = ProceduralSwayRegionResolver.Resolve(_config, modelKey);

		string sourceText = resolution.Source switch
		{
			ProceduralSwayRegionSource.DeclaredModelRegion => TranslationServer.Translate("declared for this model").ToString(),
			ProceduralSwayRegionSource.MotionTypeFallback => TranslationServer.Translate("automatic foliage fallback").ToString(),
			_ => TranslationServer.Translate("legacy preset mask").ToString()
		};

		string regionName = string.IsNullOrEmpty(resolution.RegionName) ? TranslationServer.Translate("Preset Mask").ToString() : resolution.RegionName;
		string heightText = resolution.HasRegionBox
			? string.Format(TranslationServer.Translate("height {0}% to {1}%"), (int)MathF.Round(resolution.NormalizedMin.Y * 100.0f), (int)MathF.Round(resolution.NormalizedMax.Y * 100.0f))
			: string.Empty;

		_lblRegionResolution.Text = string.Format(TranslationServer.Translate("Resolved: {0} ({1}) - {2}"), regionName, sourceText, heightText).TrimEnd(' ', '-');
	}

	private static float GetRegionComponent(Vector3Data region, int axis)
	{
		return axis switch
		{
			0 => region.X,
			1 => region.Y,
			_ => region.Z
		};
	}

	private static Vector3Data WithRegionComponent(Vector3Data region, int axis, float value)
	{
		return axis switch
		{
			0 => new Vector3Data(value, region.Y, region.Z),
			1 => new Vector3Data(region.X, value, region.Z),
			_ => new Vector3Data(region.X, region.Y, value)
		};
	}

	private void UpdateMaskControls()
	{
		SetSlider(_sldMaskMin, _lblMaskMin, _config.MaskMin, "F2");
		SetSlider(_sldMaskMax, _lblMaskMax, _config.MaskMax, "F2");
		SetSlider(_sldMaskPower, _lblMaskPower, _config.MaskPower, "F1");
		if (_chkMaskInvert != null) _chkMaskInvert.ButtonPressed = _config.MaskInvert;

		for (int axis = 0; axis < _regionAxisNames.Length; axis++)
		{
			SetSlider(_sldRegionMin[axis], _lblRegionMin[axis], GetRegionComponent(_config.MaskRegionMin, axis), "F2");
			SetSlider(_sldRegionMax[axis], _lblRegionMax[axis], GetRegionComponent(_config.MaskRegionMax, axis), "F2");
		}
	}

	private void UpdateSwayAndFlutterControls()
	{
		SetSlider(_sldSwayFrequency, _lblSwayFrequency, _config.SwayFrequency, "F1", " Hz");
		SetSlider(_sldSwayAmplitude, _lblSwayAmplitude, _config.SwayAmplitude, "F2", " m");
		SetSlider(_sldFlutterFrequency, _lblFlutterFrequency, _config.FlutterFrequency, "F1", " Hz");
		SetSlider(_sldFlutterAmplitude, _lblFlutterAmplitude, _config.FlutterAmplitude, "F3", " m");
		SetSlider(_sldWindInfluence, _lblWindInfluence, _config.WindInfluence, "F2");
		SetSlider(_sldVelocityDragInfluence, _lblVelocityDragInfluence, _config.VelocityDragInfluence, "F2");
		SetSlider(_sldWaveTurbulence, _lblWaveTurbulence, _config.WaveTurbulence, "F2");
	}

	private void UpdateImpulseControls()
	{
		SetSlider(_sldImpulseAmplitude, _lblImpulseAmplitude, _config.ImpulseAmplitude, "F2", " m");
		SetSlider(_sldImpulseFrequency, _lblImpulseFrequency, _config.ImpulseFrequency, "F1", " Hz");
		SetSlider(_sldImpulseDecay, _lblImpulseDecay, _config.ImpulseDecay, "F1");
		SetSlider(_sldImpulseDuration, _lblImpulseDuration, _config.ImpulseDuration, "F2", " s");
	}

	private void SetSlider(Slider sld, Label lbl, float val, string format, string suffix = "")
	{
		if (sld != null) sld.Value = val;
		if (lbl != null) lbl.Text = $"{val.ToString(format)}{suffix}";
	}

	private void RandomizeParameters()
	{
		_config.SwayFrequency = (float)Math.Round(Random.Shared.NextDouble() * 3.0 + 0.5, 1);
		_config.SwayAmplitude = (float)Math.Round(Random.Shared.NextDouble() * 0.35 + 0.05, 2);
		_config.FlutterFrequency = (float)Math.Round(Random.Shared.NextDouble() * 8.0 + 2.0, 1);
		_config.FlutterAmplitude = (float)Math.Round(Random.Shared.NextDouble() * 0.08 + 0.01, 3);
		_config.WaveTurbulence = (float)Math.Round(Random.Shared.NextDouble() * 0.8 + 0.2, 2);

		SyncControlsFromConfig();
		UpdateShaderParameters();
	}

	private void UpdateShaderParameters()
	{
		if (_currentModelRoot != null && GodotObject.IsInstanceValid(_currentModelRoot))
		{
			bool showHeatmap = _chkShowHeatmap != null && _chkShowHeatmap.ButtonPressed;
			Vector3 vel = new Vector3(_simVelocityX, 0, 0);
			ProceduralAnimationManager.ApplyPreviewToNode(_currentModelRoot, _config, vel, _impulseStrength, _impulseTime, showHeatmap, GetCurrentModelKey());
		}

		UpdateRegionResolutionLabel();
	}

	protected override void OnApply()
	{
		if (string.IsNullOrWhiteSpace(_config.Id))
		{
			_config.Id = "custom_procedural_anim";
		}

		if (string.IsNullOrWhiteSpace(_config.Name))
		{
			_config.Name = _config.Id;
		}

		ProceduralAnimationManager.SaveConfig(_config);
		_onSaved?.Invoke(_config);

		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Saved procedural animation {0} to metadata.json"), _config.Name));
		CloseDialog();
	}

	protected override void OnCancel()
	{
		_config = _snapshot.Clone();
		CloseDialog();
	}

	public override void CloseDialog()
	{
		SetRegionAutoDetectActive(false);
		ClearCurrentModelRoot();
		base.CloseDialog();
	}
}