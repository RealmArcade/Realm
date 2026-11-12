// Scene construction, one method per section of the dialog: identity, colour, heightmap, tiling,
// pathing, decal rules, VFX rules. The decal and VFX sections also own their in-place event
// handlers, because a rule row's controls only make sense against the list it was built from.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;


namespace Realm.Client.UI.MapEditor;

public partial class TerrainTextureEditDialog
{
	private void BuildControls()
	{
		var scrollBody = CreateScrollBody(580);
		var contentVBox = CreateExpandingContentColumn();
		scrollBody.AddChild(contentVBox);

		BuildIdentitySection(contentVBox);
		BuildColorSection(contentVBox);
		BuildHeightmapSection(contentVBox);
		BuildTilingSection(contentVBox);
		BuildPathingSection(contentVBox);
		BuildDecalSection(contentVBox);
		BuildVfxSection(contentVBox);

		UpdateTileModeVisibility();
		UpdateGraphicsQualityState();
	}

	private void BuildIdentitySection(VBoxContainer contentVBox)
	{
		AddSectionHeader(contentVBox, "🆔 " + TranslationServer.Translate("IDENTITY & TEXTURE ASSET"), new Color(0.95f, 0.8f, 0.4f));

		var rowId = BuildIdentityRow(contentVBox, "terrain/", out _lblObjectTypePrefix);

		_txtSlug = new LineEdit();
		_txtSlug.Name = "TxtSlug_LineEdit";
		_txtSlug.PlaceholderText = TranslationServer.Translate("terrain_slug");
		_txtSlug.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_txtSlug.AddThemeFontSizeOverride("font_size", 11);
		_txtSlug.TextChanged += (val) => { _slug = TemplateIDHelper.ToSnakeCase(val); };
		rowId.AddChild(_txtSlug);
		

		(_txtRtexAsset, _setRtexAssetValue) = AddAssetFilterDropdown(
			contentVBox,
			TranslationServer.Translate("Terrain .rtex:"),
			_rtexAsset,
			(all) => ScanTerrainRtexAssets(all),
			(val) => _rtexAsset = val ?? string.Empty,
			TranslationServer.Translate("Select terrain .rtex asset..."),
			140f
		);
	}

	private void BuildColorSection(VBoxContainer contentVBox)
	{
		AddSectionHeader(contentVBox, "🎨 " + TranslationServer.Translate("COLOR & LIGHTING"), new Color(0.95f, 0.8f, 0.4f));

		(_sldBrightness, _lblBrightness) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Brightness:"),
			0.2f,
			2.5f,
			0.05f,
			_brightness,
			(val) =>
			{
				_brightness = val;
				ApplyLiveTerrainUpdate();
			},
			"0.00x",
			140f
		);

			HBoxContainer rowTint;
			(rowTint, _btnTint) = UIStyle.CreateColorPickerRow("Tint Color:", _tint, (newCol) =>
		{
			_tint = newCol;
			ApplyLiveTerrainUpdate();
			});
		contentVBox.AddChild(rowTint);

		(_sldRoughnessScale, _lblRoughnessScale) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Roughness Scale:"),
			0.1f,
			3.0f,
			0.05f,
			_roughnessScale,
			(val) =>
			{
				_roughnessScale = val;
				ApplyLiveTerrainUpdate();
			},
			"0.00x",
			140f
		);
	}

	private void BuildHeightmapSection(VBoxContainer contentVBox)
	{
		AddSectionHeader(contentVBox, "🏔️ " + TranslationServer.Translate("HEIGHTMAP & CREVICES"), new Color(0.4f, 0.85f, 0.5f));

		(_sldNormalScale, _lblNormalScale) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Normal Strength:"),
			0.0f,
			3.0f,
			0.05f,
			_normalScale,
			(val) =>
			{
				_normalScale = val;
				ApplyLiveTerrainUpdate();
			},
			"0.00x",
			140f
		);
		_iconHelpNormalScale = CreateHelpTooltipIcon(_sldNormalScale);

		(_sldHeightScale, _lblHeightScale) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Height Scale:"),
			0.1f,
			3.0f,
			0.05f,
			_heightScale,
			(val) =>
			{
				_heightScale = val;
				ApplyLiveTerrainUpdate();
			},
			"0.00x",
			140f
		);
		_iconHelpHeightScale = CreateHelpTooltipIcon(_sldHeightScale);

		(_sldHeightOffset, _lblHeightOffset) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Height Offset:"),
			-1.0f,
			1.0f,
			0.05f,
			_heightOffset,
			(val) =>
			{
				_heightOffset = val;
				ApplyLiveTerrainUpdate();
			},
			"0.00",
			140f
		);
		_iconHelpHeightOffset = CreateHelpTooltipIcon(_sldHeightOffset);

		(_sldCrevicePower, _lblCrevicePower) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Crevice Power:"),
			0.5f,
			4.0f,
			0.1f,
			_crevicePower,
			(val) =>
			{
				_crevicePower = val;
				ApplyLiveTerrainUpdate();
			},
			"0.00x",
			140f
		);
		_iconHelpCrevicePower = CreateHelpTooltipIcon(_sldCrevicePower);
	}

	private void BuildTilingSection(VBoxContainer contentVBox)
	{
		AddSectionHeader(contentVBox, "📐 " + TranslationServer.Translate("TILING & PROJECTION"), new Color(0.35f, 0.75f, 0.9f));

		_optTileMode = AddOptionDropdown(
			contentVBox,
			TranslationServer.Translate("Tile Mode:"),
			new[] { "Grid", "Stochastic" },
			_tileMode == "Stochastic" ? 1 : 0,
			(idx) =>
			{
				_tileMode = idx == 1 ? "Stochastic" : "Grid";
				UpdateTileModeVisibility();
				ApplyLiveTerrainUpdate();
			},
			140f
		);

		(_sldUvScale, _lblUvScale) = AddSlider(
			contentVBox,
			TranslationServer.Translate("UV Scale:"),
			0.1f,
			4.0f,
			0.05f,
			_uvScale,
			(val) =>
			{
				_uvScale = val;
				ApplyLiveTerrainUpdate();
			},
			"0.00x",
			140f
		);

		(_sldStochasticTileSize, _lblStochasticTileSize) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Stochastic Size:"),
			0.5f,
			3.0f,
			0.05f,
			_stochasticTileSize,
			(val) =>
			{
				_stochasticTileSize = val;
				ApplyLiveTerrainUpdate();
			},
			"0.00x",
			140f
		);

		(_sldCrossFade, _lblCrossFade) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Cross-Fade:"),
			0.0f,
			10.0f,
			0.25f,
			_crossFade,
			(val) =>
			{
				_crossFade = val;
				ApplyLiveTerrainUpdate();
			},
			"0.0'%'",
			140f
		);
	}

	private void BuildPathingSection(VBoxContainer contentVBox)
	{
		AddSectionHeader(contentVBox, "🚶 " + TranslationServer.Translate("DEFAULT PATHING CAPABILITIES"), new Color(0.85f, 0.65f, 0.35f));
		AddDescription(contentVBox, TranslationServer.Translate("Automatically assigned to cells when painted with this terrain swatch:"));
		_chkPathGround = AddCheckBox(contentVBox, TranslationServer.Translate("Ground"), (_defaultPathingCode & EditableTerrain.PATHING_GROUND) != 0, (v) => UpdatePathingMask());
		_chkPathBuildable = AddCheckBox(contentVBox, TranslationServer.Translate("Buildable"), (_defaultPathingCode & EditableTerrain.PATHING_BUILDABLE) != 0, (v) => UpdatePathingMask());
		_chkPathShallow = AddCheckBox(contentVBox, TranslationServer.Translate("Shallow Water"), (_defaultPathingCode & EditableTerrain.PATHING_SHALLOW_WATER) != 0, (v) => UpdatePathingMask());
		_chkPathDeep = AddCheckBox(contentVBox, TranslationServer.Translate("Deep Water"), (_defaultPathingCode & EditableTerrain.PATHING_DEEP_WATER) != 0, (v) => UpdatePathingMask());
		_chkPathFlying = AddCheckBox(contentVBox, TranslationServer.Translate("Flying"), (_defaultPathingCode & EditableTerrain.PATHING_FLYING) != 0, (v) => UpdatePathingMask());
	}

	private void BuildDecalSection(VBoxContainer contentVBox)
	{
		AddSectionHeader(contentVBox, "🎯 " + TranslationServer.Translate("PROCEDURAL DECAL BOMBING"), new Color(0.9f, 0.5f, 0.7f));
		AddDescription(contentVBox, TranslationServer.Translate("Randomly scattered decals placed at terrain elevation during texture painting:"));
		
		var result = AddAssetRuleList(contentVBox, "Decal", "decals", "decal_texture_id", OnDecalItemSelected, OnAddDecalRule, OnRemoveDecalRule);
		_lstDecals = result.Item1;
		_txtDecalId = result.Item2;
		_setDecalIdValue = result.Item3;

		_sldDecalDensity = AddSlider(contentVBox, TranslationServer.Translate("Decal Density:"), 0.0f, 1.0f, 0.01f, 0.5f, OnDecalDensityChanged).Slider;
		_sldDecalMinScale = AddSlider(contentVBox, TranslationServer.Translate("Min Decal Scale:"), 0.05f, 1.0f, 0.01f, 0.2f, OnDecalMinScaleChanged).Slider;
		_sldDecalMaxScale = AddSlider(contentVBox, TranslationServer.Translate("Max Decal Scale:"), 0.05f, 1.0f, 0.01f, 0.5f, OnDecalMaxScaleChanged).Slider;
	}

	private void OnDecalItemSelected(long idx)
	{
		if (idx < 0 || idx >= _decalRules.Count) return;
		var r = _decalRules[(int)idx];
		_setDecalIdValue?.Invoke(r.DecalId);
		if (_sldDecalDensity != null) _sldDecalDensity.Value = r.Density;
		if (_sldDecalMinScale != null) _sldDecalMinScale.Value = r.MinScale;
		if (_sldDecalMaxScale != null) _sldDecalMaxScale.Value = r.MaxScale;
	}

	private void OnDecalDensityChanged(float v)
	{
		var sel = _lstDecals.GetSelectedItems();
		int idx = sel.Length > 0 ? sel[0] : 0;
		if (idx >= 0 && idx < _decalRules.Count) _decalRules[idx].Density = v;
	}

	private void OnDecalMinScaleChanged(float v)
	{
		var sel = _lstDecals.GetSelectedItems();
		int idx = sel.Length > 0 ? sel[0] : 0;
		if (idx >= 0 && idx < _decalRules.Count) _decalRules[idx].MinScale = v;
	}

	private void OnDecalMaxScaleChanged(float v)
	{
		var sel = _lstDecals.GetSelectedItems();
		int idx = sel.Length > 0 ? sel[0] : 0;
		if (idx >= 0 && idx < _decalRules.Count) _decalRules[idx].MaxScale = v;
	}

	private void BuildVfxSection(VBoxContainer contentVBox)
	{
		AddSectionHeader(contentVBox, "✨ " + TranslationServer.Translate("PROCEDURAL VFX BOMBING"), new Color(0.4f, 0.7f, 1.0f));
		AddDescription(contentVBox, TranslationServer.Translate("Randomly scattered particle systems placed at terrain elevation during painting:"));
		
		var result = AddAssetRuleList(contentVBox, "VFX", "vfx", "vfx_template_id", OnVfxItemSelected, OnAddVfxRule, OnRemoveVfxRule);
		_lstVfx = result.Item1;
		_txtVfxId = result.Item2;
		_setVfxIdValue = result.Item3;

		_sldVfxDensity = AddSlider(contentVBox, TranslationServer.Translate("VFX Density:"), 0.0f, 1.0f, 0.01f, 0.5f, OnVfxDensityChanged).Slider;
		_sldVfxMinScale = AddSlider(contentVBox, TranslationServer.Translate("Min VFX Scale:"), 0.05f, 1.0f, 0.01f, 0.2f, OnVfxMinScaleChanged).Slider;
		_sldVfxMaxScale = AddSlider(contentVBox, TranslationServer.Translate("Max VFX Scale:"), 0.05f, 1.0f, 0.01f, 0.5f, OnVfxMaxScaleChanged).Slider;
	}

	private void OnVfxItemSelected(long idx)
	{
		if (idx < 0 || idx >= _vfxRules.Count) return;
		var r = _vfxRules[(int)idx];
		_setVfxIdValue?.Invoke(r.VfxId);
		if (_sldVfxDensity != null) _sldVfxDensity.Value = r.Density;
		if (_sldVfxMinScale != null) _sldVfxMinScale.Value = r.MinScale;
		if (_sldVfxMaxScale != null) _sldVfxMaxScale.Value = r.MaxScale;
	}

	private void OnVfxDensityChanged(float v)
	{
		var sel = _lstVfx.GetSelectedItems();
		int idx = sel.Length > 0 ? sel[0] : 0;
		if (idx >= 0 && idx < _vfxRules.Count) _vfxRules[idx].Density = v;
	}

	private void OnVfxMinScaleChanged(float v)
	{
		var sel = _lstVfx.GetSelectedItems();
		int idx = sel.Length > 0 ? sel[0] : 0;
		if (idx >= 0 && idx < _vfxRules.Count) _vfxRules[idx].MinScale = v;
	}

	private void OnVfxMaxScaleChanged(float v)
	{
		var sel = _lstVfx.GetSelectedItems();
		int idx = sel.Length > 0 ? sel[0] : 0;
		if (idx >= 0 && idx < _vfxRules.Count) _vfxRules[idx].MaxScale = v;
	}

}