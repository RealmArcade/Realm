// The profile list and the form it drives: open with a set of profiles, keep the list selection in
// step, and copy the selected profile into the tab widgets.

using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;


namespace Realm.Client.UI.MapEditor;

public partial class WaterProfileDialog
{
	public void OpenWithProfiles(List<WaterProfileSaveData> profiles, int selectedIdx = 0)
	{
		_profiles.Clear();
		_initialSnapshots.Clear();
		foreach (var p in profiles)
		{
			_profiles.Add(p.Clone());
			_initialSnapshots.Add(p.Clone());
		}

		if (_profiles.Count == 0)
		{
			_profiles = WaterProfileSaveData.CreateDefaultProfiles();
			foreach (var p in _profiles) _initialSnapshots.Add(p.Clone());
		}

		_selectedProfileIdx = Math.Clamp(selectedIdx, 0, _profiles.Count - 1);
		UpdateProfileListUI();
		SelectProfile(_selectedProfileIdx);
		OpenDialog();
	}

	private void UpdateProfileListUI()
	{
		if (_optProfileSelect == null) return;
		_optProfileSelect.Clear();
		for (int i = 0; i < _profiles.Count; i++)
		{
			_optProfileSelect.AddItem($"[{_profiles[i].ProfileIndex}] {_profiles[i].Name} ({_profiles[i].Id})", i);
		}

		if (_selectedProfileIdx >= 0 && _selectedProfileIdx < _profiles.Count)
		{
			_optProfileSelect.Selected = _selectedProfileIdx;
		}
	}

	private void SelectProfile(int idx)
	{
		if (idx < 0 || idx >= _profiles.Count) return;
		_selectedProfileIdx = idx;
		_activeProfile = _profiles[idx];
		if (_optProfileSelect != null && _optProfileSelect.Selected != idx)
		{
			_optProfileSelect.Selected = idx;
		}

		PopulateFormFromActiveProfile();
		ApplyLiveMaterialPreview();
	}

	private void PopulateFormFromActiveProfile()
	{
		if (_activeProfile == null) return;
		_txtName.Text = _activeProfile.Name;

		PopulateGeneralAndColors();
		PopulateFlowAndOptics();
		PopulateEmissiveAndSurface();
		PopulatePathingAndBombing();
	}

	private void PopulateGeneralAndColors()
	{
		_cpShallow.Color = ParseColor(_activeProfile.ShallowColorHex, Colors.Teal);
		_cpDeep.Color = ParseColor(_activeProfile.DeepColorHex, Colors.NavyBlue);
		_cpFoam.Color = ParseColor(_activeProfile.FoamColorHex, Colors.White);

		_sldMaxDepth.Value = _activeProfile.MaxDepth;
		_sldFoamDepth.Value = _activeProfile.FoamDepth;
		_sldWaveSpeed.Value = _activeProfile.WaveSpeed;
		_sldWaveStrength.Value = _activeProfile.WaveStrength;
	}

	private void PopulateFlowAndOptics()
	{
		_chkUseNormal.ButtonPressed = _activeProfile.UseNormalTexture;
		_setNormalPath?.Invoke(_activeProfile.NormalTexturePath ?? "");
		_sldNormalScale.Value = _activeProfile.NormalScale;
		_sldFlowDirX.Value = _activeProfile.FlowDirectionX;
		_sldFlowDirY.Value = _activeProfile.FlowDirectionY;
		_sldFlowSpeed.Value = _activeProfile.FlowSpeed;
		_chkUseFlowMap.ButtonPressed = _activeProfile.UseFlowMap;
		_setFlowMapPath?.Invoke(_activeProfile.FlowMapPath ?? "");

		_sldRefraction.Value = _activeProfile.RefractionStrength;
		_sldCausticStrength.Value = _activeProfile.CausticStrength;
		_sldCausticScale.Value = _activeProfile.CausticScale;
		_sldCausticSpeed.Value = _activeProfile.CausticSpeed;
	}

	private void PopulateEmissiveAndSurface()
	{
		_cpEmission.Color = ParseColor(_activeProfile.EmissionColorHex, Colors.Black);
		_sldEmissionBoost.Value = _activeProfile.EmissionBoost;
		_cpCore.Color = ParseColor(_activeProfile.CoreColorHex, Colors.White);
		_sldCoreThreshold.Value = _activeProfile.CoreThreshold;
		_cpSubsurface.Color = ParseColor(_activeProfile.SubsurfaceColorHex, Colors.Black);
		_sldSubsurfaceStrength.Value = _activeProfile.SubsurfaceStrength;

		_chkUseDetail.ButtonPressed = _activeProfile.UseDetailTexture;
		_setDetailPath?.Invoke(_activeProfile.DetailTexturePath ?? "");

		UpdateDetailOverlayUI();

		if (_sldDetailStochasticTileSize != null)
		{
			_sldDetailStochasticTileSize.Value = _activeProfile.DetailStochasticTileSize > 0.001f ? _activeProfile.DetailStochasticTileSize : 1.0f;
		}
	}

	private void PopulatePathingAndBombing()
	{
		int code = _activeProfile.DefaultPathingCode;
		_chkPathShallow.ButtonPressed = (code & EditableTerrain.PATHING_SHALLOW_WATER) != 0;
		_chkPathDeep.ButtonPressed = (code & EditableTerrain.PATHING_DEEP_WATER) != 0;
		_chkPathGround.ButtonPressed = (code & EditableTerrain.PATHING_GROUND) != 0;
		_chkPathBuildable.ButtonPressed = (code & EditableTerrain.PATHING_BUILDABLE) != 0;
		_chkPathFlying.ButtonPressed = (code & EditableTerrain.PATHING_FLYING) != 0;

		_setDecalIdValue?.Invoke(string.Empty);
		_setVfxIdValue?.Invoke(string.Empty);
		UpdateDecalListUI();
		UpdateVfxListUI();
	}

	private Color ParseColor(string hex, Color fallback)
	{
		return Color.HtmlIsValid(hex) ? Color.FromHtml(hex) : fallback;
	}

	private void UpdateDetailTileModeVisibility()
	{
		bool isStochastic = _activeProfile == null || !string.Equals(_activeProfile.DetailTileMode, "Grid", StringComparison.OrdinalIgnoreCase);
		if (_sldDetailStochasticTileSize?.GetParent() is Control stochRow)
		{
			stochRow.Visible = isStochastic;
		}

		if (_sldDetailCrossFade?.GetParent() is Control crossFadeRow)
		{
			crossFadeRow.Visible = !isStochastic;
		}
	}

}