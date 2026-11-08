using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class WaterProfileDialog : FloatingDialogBase
{
	public WaterProfileDialog() { }

	private List<WaterProfileSaveData> _profiles = new();
	private int _selectedProfileIdx = 0;
	private WaterProfileSaveData _activeProfile = null!;
	private List<WaterProfileSaveData> _initialSnapshots = new();

	private OptionButton _optProfileSelect = null!;
	private Button _btnAddProfile = null!;
	private Button _btnDeleteProfile = null!;

	private LineEdit _txtName = null!;

	private ColorPickerButton _cpShallow = null!;
	private ColorPickerButton _cpDeep = null!;
	private ColorPickerButton _cpFoam = null!;
	private HSlider _sldMaxDepth = null!;
	private HSlider _sldFoamDepth = null!;
	private HSlider _sldWaveSpeed = null!;
	private HSlider _sldWaveStrength = null!;

	private CheckBox _chkUseNormal = null!;
	private LineEdit _txtNormalPath = null!;
	private Action<string> _setNormalPath = null!;
	private HSlider _sldNormalScale = null!;
	private HSlider _sldFlowDirX = null!;
	private HSlider _sldFlowDirY = null!;
	private HSlider _sldFlowSpeed = null!;
	private CheckBox _chkUseFlowMap = null!;
	private LineEdit _txtFlowMapPath = null!;
	private Action<string> _setFlowMapPath = null!;

	private HSlider _sldRefraction = null!;
	private HSlider _sldCausticStrength = null!;
	private HSlider _sldCausticScale = null!;
	private HSlider _sldCausticSpeed = null!;

	private ColorPickerButton _cpEmission = null!;
	private HSlider _sldEmissionBoost = null!;
	private ColorPickerButton _cpCore = null!;
	private HSlider _sldCoreThreshold = null!;
	private ColorPickerButton _cpSubsurface = null!;
	private HSlider _sldSubsurfaceStrength = null!;

	private CheckBox _chkUseDetail = null!;
	private LineEdit _txtDetailPath = null!;
	private Action<string> _setDetailPath = null!;
	private OptionButton _optDetailTileMode = null!;
	private HSlider _sldDetailUvScaleX = null!;
	private HSlider _sldDetailUvScaleY = null!;
	private HSlider _sldDetailUvScrollX = null!;
	private HSlider _sldDetailUvScrollY = null!;
	private HSlider _sldDetailStochasticTileSize = null!;
	private HSlider _sldDetailCrossFade = null!;
	private HSlider _sldDetailAlpha = null!;
	private OptionButton _optDetailBlend = null!;

	private CheckBox _chkPathShallow = null!;
	private CheckBox _chkPathDeep = null!;
	private CheckBox _chkPathGround = null!;
	private CheckBox _chkPathBuildable = null!;
	private CheckBox _chkPathFlying = null!;

	private ItemList _lstDecals = null!;
	private LineEdit _txtDecalId = null!;
	private Action<string> _setDecalIdValue = null!;
	private HSlider _sldDecalDensity = null!;
	private HSlider _sldDecalMinScale = null!;
	private HSlider _sldDecalMaxScale = null!;

	private ItemList _lstVfx = null!;
	private LineEdit _txtVfxId = null!;
	private Action<string> _setVfxIdValue = null!;
	private HSlider _sldVfxDensity = null!;
	private HSlider _sldVfxMinScale = null!;
	private HSlider _sldVfxMaxScale = null!;

	public WaterProfileDialog(MapEditorHUD hud) : base(hud, TranslationServer.Translate("LIQUID / WATER PROFILE UBER CONFIG"), new Vector2(620, 680))
	{
		BuildUI();
	}

	private void RandomizeAllWaterParameters()
	{
		if (_activeProfile == null) return;
		var rng = new Random();

		float baseHue = (float)rng.NextDouble();
		float shallowSat = 0.4f + (float)rng.NextDouble() * 0.5f;
		float shallowVal = 0.4f + (float)rng.NextDouble() * 0.5f;
		float shallowAlpha = 0.45f + (float)rng.NextDouble() * 0.35f;
		Color shallowColor = Color.FromHsv(baseHue, shallowSat, shallowVal, shallowAlpha);

		float deepSat = Math.Clamp(shallowSat + 0.15f, 0.0f, 1.0f);
		float deepVal = 0.05f + (float)rng.NextDouble() * 0.25f;
		float deepAlpha = 0.85f + (float)rng.NextDouble() * 0.15f;
		Color deepColor = Color.FromHsv((baseHue + 0.02f) % 1.0f, deepSat, deepVal, deepAlpha);

		float foamSat = 0.05f + (float)rng.NextDouble() * 0.2f;
		float foamVal = 0.85f + (float)rng.NextDouble() * 0.15f;
		float foamAlpha = 0.75f + (float)rng.NextDouble() * 0.2f;
		Color foamColor = Color.FromHsv(baseHue, foamSat, foamVal, foamAlpha);

		_activeProfile.ShallowColorHex = "#" + shallowColor.ToHtml(true);
		_activeProfile.DeepColorHex = "#" + deepColor.ToHtml(true);
		_activeProfile.FoamColorHex = "#" + foamColor.ToHtml(true);

		_activeProfile.MaxDepth = (float)Math.Round(0.5f + rng.NextDouble() * 3.0f, 2);
		_activeProfile.FoamDepth = (float)Math.Round(0.2f + rng.NextDouble() * 1.0f, 2);
		_activeProfile.WaveSpeed = (float)Math.Round(0.4f + rng.NextDouble() * 2.0f, 2);
		_activeProfile.WaveStrength = (float)Math.Round(0.02f + rng.NextDouble() * 0.10f, 3);

		_activeProfile.NormalScale = (float)Math.Round(0.3f + rng.NextDouble() * 1.7f, 2);
		double dirAngle = rng.NextDouble() * Math.PI * 2.0;
		_activeProfile.FlowDirectionX = (float)Math.Round(Math.Cos(dirAngle), 2);
		_activeProfile.FlowDirectionY = (float)Math.Round(Math.Sin(dirAngle), 2);
		_activeProfile.FlowSpeed = (float)Math.Round(0.2f + rng.NextDouble() * 1.5f, 2);

		bool hasRefraction = rng.NextDouble() > 0.35;
		_activeProfile.RefractionStrength = hasRefraction ? (float)Math.Round(0.1f + rng.NextDouble() * 0.5f, 2) : 0.0f;

		bool hasCaustics = rng.NextDouble() > 0.35;
		_activeProfile.CausticStrength = hasCaustics ? (float)Math.Round(0.2f + rng.NextDouble() * 1.0f, 2) : 0.0f;
		_activeProfile.CausticScale = (float)Math.Round(0.5f + rng.NextDouble() * 2.0f, 2);
		_activeProfile.CausticSpeed = (float)Math.Round(0.5f + rng.NextDouble() * 2.0f, 2);

		bool isGlowing = rng.NextDouble() > 0.65;
		if (isGlowing)
		{
			float emHue = (baseHue + (float)(rng.NextDouble() * 0.2 - 0.1) + 1.0f) % 1.0f;
			Color emColor = Color.FromHsv(emHue, 0.85f + (float)rng.NextDouble() * 0.15f, 0.9f + (float)rng.NextDouble() * 0.1f);
			_activeProfile.EmissionColorHex = "#" + emColor.ToHtml(true);
			_activeProfile.EmissionBoost = (float)Math.Round(0.3f + rng.NextDouble() * 1.7f, 2);

			Color coreColor = Color.FromHsv((emHue + 0.08f) % 1.0f, 0.2f + (float)rng.NextDouble() * 0.3f, 1.0f);
			_activeProfile.CoreColorHex = "#" + coreColor.ToHtml(true);
			_activeProfile.CoreThreshold = (float)Math.Round(0.55f + rng.NextDouble() * 0.35f, 2);

			Color sssColor = Color.FromHsv(emHue, 0.7f + (float)rng.NextDouble() * 0.3f, 0.8f + (float)rng.NextDouble() * 0.2f);
			_activeProfile.SubsurfaceColorHex = "#" + sssColor.ToHtml(true);
			_activeProfile.SubsurfaceStrength = (float)Math.Round(0.5f + rng.NextDouble() * 2.0f, 2);
		}
		else
		{
			_activeProfile.EmissionColorHex = "#000000FF";
			_activeProfile.EmissionBoost = 0.0f;
			_activeProfile.CoreColorHex = "#FFFFFFFF";
			_activeProfile.CoreThreshold = 0.8f;
			_activeProfile.SubsurfaceColorHex = "#000000FF";
			_activeProfile.SubsurfaceStrength = 0.0f;
		}

		_activeProfile.DetailUvScaleX = (float)Math.Round(0.5f + rng.NextDouble() * 2.5f, 2);
		_activeProfile.DetailUvScaleY = (float)Math.Round(0.5f + rng.NextDouble() * 2.5f, 2);
		_activeProfile.DetailStochasticTileSize = (float)Math.Round(0.8f + rng.NextDouble() * 1.5f, 2);
		_activeProfile.DetailCrossFade = (float)Math.Round(rng.NextDouble() * 4.0f, 1);

		SelectProfile(_selectedProfileIdx);
		ApplyLiveMaterialPreview();
	}

	private void RandomizeEmissiveParameters()
	{
		if (_activeProfile == null) return;
		var rng = new Random();

		float emHue = (float)rng.NextDouble();
		Color emColor = Color.FromHsv(emHue, 0.80f + (float)rng.NextDouble() * 0.20f, 0.85f + (float)rng.NextDouble() * 0.15f);
		_activeProfile.EmissionColorHex = "#" + emColor.ToHtml(true);
		_activeProfile.EmissionBoost = (float)Math.Round(0.2f + rng.NextDouble() * 1.8f, 2);

		Color coreColor = Color.FromHsv((emHue + 0.06f + (float)rng.NextDouble() * 0.06f) % 1.0f, 0.15f + (float)rng.NextDouble() * 0.35f, 1.0f);
		_activeProfile.CoreColorHex = "#" + coreColor.ToHtml(true);
		_activeProfile.CoreThreshold = (float)Math.Round(0.45f + rng.NextDouble() * 0.45f, 2);

		Color sssColor = Color.FromHsv((emHue + (float)(rng.NextDouble() * 0.1 - 0.05) + 1.0f) % 1.0f, 0.70f + (float)rng.NextDouble() * 0.30f, 0.75f + (float)rng.NextDouble() * 0.25f);
		_activeProfile.SubsurfaceColorHex = "#" + sssColor.ToHtml(true);
		_activeProfile.SubsurfaceStrength = (float)Math.Round(0.5f + rng.NextDouble() * 3.5f, 2);

		if (_cpEmission != null) _cpEmission.Color = emColor;
		if (_sldEmissionBoost != null) _sldEmissionBoost.Value = _activeProfile.EmissionBoost;
		if (_cpCore != null) _cpCore.Color = coreColor;
		if (_sldCoreThreshold != null) _sldCoreThreshold.Value = _activeProfile.CoreThreshold;
		if (_cpSubsurface != null) _cpSubsurface.Color = sssColor;
		if (_sldSubsurfaceStrength != null) _sldSubsurfaceStrength.Value = _activeProfile.SubsurfaceStrength;

		ApplyLiveMaterialPreview();
	}

	private void RandomizeNormalAndFlowParameters()
	{
		if (_activeProfile == null) return;
		var rng = new Random();

		_activeProfile.NormalScale = (float)Math.Round(0.3f + rng.NextDouble() * 2.2f, 2);
		double dirAngle = rng.NextDouble() * Math.PI * 2.0;
		_activeProfile.FlowDirectionX = (float)Math.Round(Math.Cos(dirAngle), 2);
		_activeProfile.FlowDirectionY = (float)Math.Round(Math.Sin(dirAngle), 2);
		_activeProfile.FlowSpeed = (float)Math.Round(0.2f + rng.NextDouble() * 2.0f, 2);

		if (_sldNormalScale != null) _sldNormalScale.Value = _activeProfile.NormalScale;
		if (_sldFlowDirX != null) _sldFlowDirX.Value = _activeProfile.FlowDirectionX;
		if (_sldFlowDirY != null) _sldFlowDirY.Value = _activeProfile.FlowDirectionY;
		if (_sldFlowSpeed != null) _sldFlowSpeed.Value = _activeProfile.FlowSpeed;

		ApplyLiveMaterialPreview();
	}

	private void RandomizeRefractionAndCausticsParameters()
	{
		if (_activeProfile == null) return;
		var rng = new Random();

		bool hasRefraction = rng.NextDouble() > 0.25;
		_activeProfile.RefractionStrength = hasRefraction ? (float)Math.Round(0.05f + rng.NextDouble() * 0.55f, 2) : 0.0f;

		bool hasCaustics = rng.NextDouble() > 0.25;
		_activeProfile.CausticStrength = hasCaustics ? (float)Math.Round(0.2f + rng.NextDouble() * 1.3f, 2) : 0.0f;
		_activeProfile.CausticScale = (float)Math.Round(0.5f + rng.NextDouble() * 2.5f, 2);
		_activeProfile.CausticSpeed = (float)Math.Round(0.3f + rng.NextDouble() * 2.2f, 2);

		if (_sldRefraction != null) _sldRefraction.Value = _activeProfile.RefractionStrength;
		if (_sldCausticStrength != null) _sldCausticStrength.Value = _activeProfile.CausticStrength;
		if (_sldCausticScale != null) _sldCausticScale.Value = _activeProfile.CausticScale;
		if (_sldCausticSpeed != null) _sldCausticSpeed.Value = _activeProfile.CausticSpeed;

		ApplyLiveMaterialPreview();
	}

	private void RandomizeDetailOverlayParameters()
	{
		if (_activeProfile == null) return;
		var rng = new Random();

		_activeProfile.DetailTileMode = rng.NextDouble() > 0.35 ? "Stochastic" : "Grid";
		_activeProfile.DetailUvScaleX = (float)Math.Round(0.5f + rng.NextDouble() * 3.5f, 2);
		_activeProfile.DetailUvScaleY = (float)Math.Round(0.5f + rng.NextDouble() * 3.5f, 2);

		float scrollX = (float)Math.Round((rng.NextDouble() * 2.0 - 1.0) * 0.75f, 2);
		_activeProfile.DetailUvScrollX = rng.NextDouble() > 0.4 ? scrollX : 0.0f;

		float scrollY = (float)Math.Round((rng.NextDouble() * 2.0 - 1.0) * 0.75f, 2);
		_activeProfile.DetailUvScrollY = rng.NextDouble() > 0.4 ? scrollY : 0.0f;

		_activeProfile.DetailStochasticTileSize = (float)Math.Round(0.6f + rng.NextDouble() * 1.8f, 2);
		_activeProfile.DetailCrossFade = (float)Math.Round(rng.NextDouble() * 4.0f, 1);
		_activeProfile.DetailAlpha = (float)Math.Round(0.25f + rng.NextDouble() * 0.65f, 2);
		_activeProfile.DetailBlendMode = rng.Next(0, 3);

		UpdateDetailOverlayUI();
		ApplyLiveMaterialPreview();
	}

	private void UpdateDetailOverlayUI()
	{
		UpdateDetailTileModeSelection();
		UpdateDetailTileModeVisibility();
		UpdateDetailSliders();

		if (_optDetailBlend != null) _optDetailBlend.Selected = _activeProfile.DetailBlendMode;
	}

	private void UpdateDetailTileModeSelection()
	{
		if (_optDetailTileMode == null) return;
		bool isGrid = string.Equals(_activeProfile.DetailTileMode, "Grid", StringComparison.OrdinalIgnoreCase);
		_optDetailTileMode.Selected = isGrid ? 0 : 1;
	}

	private void UpdateDetailSliders()
	{
		SetSliderValue(_sldDetailUvScaleX, _activeProfile.DetailUvScaleX);
		SetSliderValue(_sldDetailUvScaleY, _activeProfile.DetailUvScaleY);
		SetSliderValue(_sldDetailUvScrollX, _activeProfile.DetailUvScrollX);
		SetSliderValue(_sldDetailUvScrollY, _activeProfile.DetailUvScrollY);
		SetSliderValue(_sldDetailStochasticTileSize, _activeProfile.DetailStochasticTileSize);
		SetSliderValue(_sldDetailCrossFade, _activeProfile.DetailCrossFade);
		SetSliderValue(_sldDetailAlpha, _activeProfile.DetailAlpha);
	}

	private void SetSliderValue(HSlider slider, float value)
	{
		if (slider != null) slider.Value = value;
	}

	private void UpdatePathingMask()
	{
		if (_activeProfile == null) return;
		int mask = 0;
		if (_chkPathShallow.ButtonPressed) mask |= EditableTerrain.PATHING_SHALLOW_WATER;
		if (_chkPathDeep.ButtonPressed) mask |= EditableTerrain.PATHING_DEEP_WATER;
		if (_chkPathGround.ButtonPressed) mask |= EditableTerrain.PATHING_GROUND;
		if (_chkPathBuildable.ButtonPressed) mask |= EditableTerrain.PATHING_BUILDABLE;
		if (_chkPathFlying.ButtonPressed) mask |= EditableTerrain.PATHING_FLYING;
		_activeProfile.DefaultPathingCode = mask;
	}

	private void UpdateDecalListUI()
	{
		_lstDecals.Clear();
		if (_activeProfile?.DecalBombingRules == null) return;
		foreach (var r in _activeProfile.DecalBombingRules)
		{
			_lstDecals.AddItem($"{r.DecalId} ({TranslationServer.Translate("Density")}: {r.Density:0.00})");
		}
	}

	private void UpdateVfxListUI()
	{
		_lstVfx.Clear();
		if (_activeProfile?.VfxBombingRules == null) return;
		foreach (var r in _activeProfile.VfxBombingRules)
		{
			_lstVfx.AddItem($"{r.VfxId} ({TranslationServer.Translate("Density")}: {r.Density:0.00})");
		}
	}

	private float GetSliderValue(HSlider slider, float fallback)
	{
		return slider != null ? (float)slider.Value : fallback;
	}

	private void OnAddDecalRule()
	{
		string id = _txtDecalId != null && !string.IsNullOrWhiteSpace(_txtDecalId.Text) ? _txtDecalId.Text.Trim() : string.Empty;
		if (_activeProfile == null || string.IsNullOrEmpty(id)) return;

		var rule = new ProceduralBombingDecalRule
		{
			DecalId = id,
			Density = GetSliderValue(_sldDecalDensity, 0.5f),
			MinScale = GetSliderValue(_sldDecalMinScale, 0.2f),
			MaxScale = GetSliderValue(_sldDecalMaxScale, 0.5f)
		};
		_activeProfile.DecalBombingRules.Add(rule);
		UpdateDecalListUI();
	}

	private void OnRemoveDecalRule()
	{
		if (_activeProfile == null) return;
		var selected = _lstDecals.GetSelectedItems();
		if (selected.Length > 0 && selected[0] >= 0 && selected[0] < _activeProfile.DecalBombingRules.Count)
		{
			_activeProfile.DecalBombingRules.RemoveAt(selected[0]);
			UpdateDecalListUI();
		}
	}

	private void OnAddVfxRule()
	{
		string id = _txtVfxId != null && !string.IsNullOrWhiteSpace(_txtVfxId.Text) ? _txtVfxId.Text.Trim() : string.Empty;
		if (_activeProfile == null || string.IsNullOrEmpty(id)) return;

		var rule = new ProceduralBombingVfxRule
		{
			VfxId = id,
			Density = GetSliderValue(_sldVfxDensity, 0.5f),
			MinScale = GetSliderValue(_sldVfxMinScale, 0.2f),
			MaxScale = GetSliderValue(_sldVfxMaxScale, 0.5f)
		};
		_activeProfile.VfxBombingRules.Add(rule);
		UpdateVfxListUI();
	}

	private void OnRemoveVfxRule()
	{
		if (_activeProfile == null) return;
		var selected = _lstVfx.GetSelectedItems();
		if (selected.Length > 0 && selected[0] >= 0 && selected[0] < _activeProfile.VfxBombingRules.Count)
		{
			_activeProfile.VfxBombingRules.RemoveAt(selected[0]);
			UpdateVfxListUI();
		}
	}

	private void OnAddProfileClicked()
	{
		byte nextIndex = 0;
		foreach (var p in _profiles)
		{
			if (p.ProfileIndex >= nextIndex) nextIndex = (byte)(p.ProfileIndex + 1);
		}

		var newProf = new WaterProfileSaveData
		{
			Id = $"liquid_custom_{nextIndex}",
			Name = $"Custom Liquid {nextIndex}",
			ProfileIndex = nextIndex
		};
		_profiles.Add(newProf);
		UpdateProfileListUI();
		SelectProfile(_profiles.Count - 1);
	}

	private void OnDeleteProfileClicked()
	{
		if (_profiles.Count <= 1) return;
		_profiles.RemoveAt(_selectedProfileIdx);
		_selectedProfileIdx = Math.Clamp(_selectedProfileIdx, 0, _profiles.Count - 1);
		UpdateProfileListUI();
		SelectProfile(_selectedProfileIdx);
	}

	private void ApplyLiveMaterialPreview()
	{
		if (_activeProfile == null || GameContext.Terrain == null) return;
		var mat = GameContext.Terrain.GetWaterMaterial(_activeProfile.ProfileIndex);
		if (mat != null)
		{
			GameContext.Terrain.ApplyWaterProfileToMaterial(mat, _activeProfile);
		}
	}

	protected override void OnApply()
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		if (!string.IsNullOrEmpty(wsPath))
		{
			var metadataRoot = GameContext.MetadataService.LoadMetadata(wsPath);
			metadataRoot.CustomWaterProfiles = _profiles;
			string metaPath = Path.Combine(wsPath, "metadata.json");
			GameContext.MetadataService.SaveMetadata(metaPath, metadataRoot);
			Hud?.UpdateLastMetadataSyncTime(metaPath);
		}

		if (GameContext.Terrain != null)
		{
			GameContext.Terrain.SetWaterProfiles(_profiles);
		}

		if (GameContext.Host != null)
		{
			GameContext.Host.EditorHasUnsavedChanges = true;
		}

		Hud?.RefreshWaterSwatches();
	}

	protected override void OnCancel()
	{
		if (GameContext.Terrain != null)
		{
			GameContext.Terrain.SetWaterProfiles(_initialSnapshots);
		}

		Hud?.RefreshWaterSwatches();
	}

	private List<string> ScanRtexAssets(bool includeAllFolders = true)
	{
		var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string wsPath = GameContext.EditorService._editingDirectory;

		if (!string.IsNullOrEmpty(wsPath))
		{
			ScanRtexAssetsFromDirectory(wsPath, results);
			ScanRtexAssetsFromManifest(wsPath, results);
		}

		var list = new List<string>(results);
		list.Sort(StringComparer.OrdinalIgnoreCase);
		return list;
	}

	private void ScanRtexAssetsFromDirectory(string wsPath, HashSet<string> results)
	{
		string assetsDir = Path.Combine(wsPath, "Assets");
		if (!Directory.Exists(assetsDir)) return;
		try
		{
			foreach (var file in Directory.GetFiles(assetsDir, "*.rtex", SearchOption.AllDirectories))
			{
				string rel = Path.GetRelativePath(wsPath, file).Replace('\\', '/');
				results.Add(rel);
				results.Add(Path.GetFileName(file));
			}
		}
		catch
		{
		}
	}

	private void ScanRtexAssetsFromManifest(string wsPath, HashSet<string> results)
	{
		try
		{
			var assetsObj = MapAssetHelper.LoadAssets(wsPath);
			if (assetsObj == null) return;

			string[] categories = { "Terrain", "Noise", "Decal", "Ribbon", "Spritesheet" };
			foreach (var catName in categories)
			{
				var catDict = assetsObj.GetCategory(catName);
				if (catDict == null) continue;

				foreach (var kvp in catDict)
				{
					if (string.IsNullOrEmpty(kvp.Key) || !kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase)) continue;
					results.Add(kvp.Key);
					results.Add(Path.GetFileName(kvp.Key));
				}
			}
		}
		catch
		{
		}
	}
}