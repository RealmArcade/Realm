using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Shared.Services;
using Realm.Shared.Textures;

namespace Realm.Client.UI.MapEditor;

public partial class DecalSettingsDialog
{
	private void TrackOriginalDecalStates(string decalKey)
	{
		_originalDecalStates.Clear();
		if (GameContext.Host?.AllDecals == null) return;

		string baseKey = Path.GetFileNameWithoutExtension(decalKey);
		foreach (var d in GameContext.Host.AllDecals)
		{
			if (d == null || !GodotObject.IsInstanceValid(d)) continue;

			string dId = d is Decal3D d3d ? d3d.DecalId : "";
			string dBase = Path.GetFileNameWithoutExtension(dId);
			if (dId.Equals(decalKey, StringComparison.OrdinalIgnoreCase) || dBase.Equals(baseKey, StringComparison.OrdinalIgnoreCase))
			{
				_originalDecalStates[d.GetInstanceId()] = new DecalNodeState
				{
					Modulate = d.Modulate,
					AlbedoMix = d.AlbedoMix,
					TextureNormal = d.TextureNormal,
					TextureOrm = d.TextureOrm,
					TextureEmission = d.TextureEmission,
					EmissionEnergy = d.EmissionEnergy,
					Size = d.Size,
					UpperFade = d.UpperFade,
					LowerFade = d.LowerFade,
					GroundProjected = d is Decal3D groundDecal && groundDecal.GroundProjected,
					GroundOffset = d is Decal3D offsetDecal ? offsetDecal.GroundOffset : 0.05f,
					NormalFade = d.NormalFade,
					DistanceFadeEnabled = d.DistanceFadeEnabled,
					DistanceFadeBegin = d.DistanceFadeBegin,
					DistanceFadeEnd = d.DistanceFadeBegin + d.DistanceFadeLength
				};
			}
		}
	}

	private void TakeInitialSnapshot()
	{
		_initialSnapshot = new DecalSnapshot
		{
			DecalId = _decalKey,
			TexturePath = _texturePath,
			Brightness = _brightness,
			Tint = _tint,
			Contrast = _contrast,
			Saturation = _saturation,
			Opacity = _opacity,
			AlbedoMix = _albedoMix,
			NormalStrength = _normalStrength,
			Roughness = _roughness,
			Metallic = _metallic,
			BlendMode = _blendMode,
			Animation = new DecalAnimationConfig
			{
				AnimateOpacity = _animateOpacity,
				OpacityPulseSpeed = _opacityPulseSpeed,
				MinOpacity = _minOpacity,
				MaxOpacity = _maxOpacity,
				AnimateEmission = _animateEmission,
				EmissionPulseSpeed = _emissionPulseSpeed,
				MinEmission = _minEmission,
				MaxEmission = _maxEmission,
				AnimateScale = _animateScale,
				ScalePulseSpeed = _scalePulseSpeed,
				MinScaleRatio = _minScaleRatio,
				MaxScaleRatio = _maxScaleRatio
			},
			UpperFade = _upperFade,
			LowerFade = _lowerFade,
			PlacementMode = _placementMode,
			ProjectionSize = _projectionSize,
			ProjectionHeight = _projectionHeight,
			GroundOffset = _groundOffset,
			NormalFade = _normalFade,
			DistanceFadeEnabled = _distanceFadeEnabled,
			DistanceFadeBegin = _distanceFadeBegin,
			DistanceFadeEnd = _distanceFadeEnd
		};

		string targetId = !string.IsNullOrEmpty(_decalKey) ? _decalKey : $"decal/{_slug}";
		_initialTemplateJson = GameContext.EditorService.SnapshotTemplateJson(targetId);
		_templateExistedInitially = _initialTemplateJson != null;
	}

	private bool TryRestoreOriginalStates()
	{
		if (_originalDecalStates.Count == 0 || GameContext.Host?.AllDecals == null) return false;

		foreach (var d in GameContext.Host.AllDecals)
		{
			if (d == null || !GodotObject.IsInstanceValid(d) || !_originalDecalStates.TryGetValue(d.GetInstanceId(), out var orig)) continue;

			d.Modulate = orig.Modulate;
			d.AlbedoMix = orig.AlbedoMix;
			d.TextureNormal = orig.TextureNormal;
			d.TextureOrm = orig.TextureOrm;
			d.TextureEmission = orig.TextureEmission;
			d.EmissionEnergy = orig.EmissionEnergy;
			d.Size = orig.Size;
			d.UpperFade = orig.UpperFade;
			d.LowerFade = orig.LowerFade;
			d.NormalFade = orig.NormalFade;
			d.DistanceFadeEnabled = orig.DistanceFadeEnabled;
			d.DistanceFadeBegin = orig.DistanceFadeBegin;
			d.DistanceFadeLength = Mathf.Max(0.1f, orig.DistanceFadeEnd - orig.DistanceFadeBegin);

			if (d is Decal3D d3d)
			{
				d3d.SetBaseProperties(orig.Modulate, orig.EmissionEnergy, orig.Size);
				d3d.SetGroundProjection(orig.GroundProjected, orig.GroundOffset);
				d3d.UpdateProcessState();
			}
		}

		return true;
	}

	private void RestoreInitialSnapshot()
	{
		if (_initialSnapshot == null) return;

		_decalKey = _initialSnapshot.DecalId;
		var (_, initSlug) = TemplateIDHelper.ParseTemplateID(_decalKey);
		_slug = !string.IsNullOrWhiteSpace(initSlug) ? TemplateIDHelper.ToSnakeCase(initSlug) : TemplateIDHelper.ToSnakeCase(_decalKey);

		if (_txtSlug != null) _txtSlug.Text = _slug;
		if (_lblDecalName != null) _lblDecalName.Text = TranslationServer.Translate("TemplateID:") + " " + _decalKey;

		_texturePath = _initialSnapshot.TexturePath;
		_brightness = _initialSnapshot.Brightness;
		_tint = _initialSnapshot.Tint;
		_contrast = _initialSnapshot.Contrast;
		_saturation = _initialSnapshot.Saturation;
		_opacity = _initialSnapshot.Opacity;
		_albedoMix = _initialSnapshot.AlbedoMix;
		_normalStrength = _initialSnapshot.NormalStrength;
		_roughness = _initialSnapshot.Roughness;
		_metallic = _initialSnapshot.Metallic;
		_blendMode = _initialSnapshot.BlendMode;
		_animateOpacity = _initialSnapshot.Animation.AnimateOpacity;
		_opacityPulseSpeed = _initialSnapshot.Animation.OpacityPulseSpeed;
		_minOpacity = _initialSnapshot.Animation.MinOpacity;
		_maxOpacity = _initialSnapshot.Animation.MaxOpacity;
		_animateEmission = _initialSnapshot.Animation.AnimateEmission;
		_emissionPulseSpeed = _initialSnapshot.Animation.EmissionPulseSpeed;
		_minEmission = _initialSnapshot.Animation.MinEmission;
		_maxEmission = _initialSnapshot.Animation.MaxEmission;
		_animateScale = _initialSnapshot.Animation.AnimateScale;
		_scalePulseSpeed = _initialSnapshot.Animation.ScalePulseSpeed;
		_minScaleRatio = _initialSnapshot.Animation.MinScaleRatio;
		_maxScaleRatio = _initialSnapshot.Animation.MaxScaleRatio;
		_upperFade = _initialSnapshot.UpperFade;
		_lowerFade = _initialSnapshot.LowerFade;
		_placementMode = _initialSnapshot.PlacementMode;
		_projectionSize = _initialSnapshot.ProjectionSize;
		_projectionHeight = _initialSnapshot.ProjectionHeight;
		_groundOffset = _initialSnapshot.GroundOffset;
		_normalFade = _initialSnapshot.NormalFade;
		_distanceFadeEnabled = _initialSnapshot.DistanceFadeEnabled;
		_distanceFadeBegin = _initialSnapshot.DistanceFadeBegin;
		_distanceFadeEnd = _initialSnapshot.DistanceFadeEnd;

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		GameContext.MetadataService.UpdateMetadata(wsPath, m =>
		{
			m.Decals ??= new(StringComparer.OrdinalIgnoreCase);
			m.Decals[_decalKey] = new Realm.Shared.Metadata.DecalMetadata
			{
				TexturePath = _texturePath,
				Brightness = _brightness,
				Tint = $"#{_tint.ToHtml(false)}",
				Contrast = _contrast,
				Saturation = _saturation,
				Opacity = _opacity,
				AlbedoMix = _albedoMix,
				NormalStrength = _normalStrength,
				Roughness = _roughness,
				Metallic = _metallic,
				BlendMode = _blendMode,
				AnimateOpacity = _animateOpacity,
				OpacityPulseSpeed = _opacityPulseSpeed,
				MinOpacity = _minOpacity,
				MaxOpacity = _maxOpacity,
				AnimateEmission = _animateEmission,
				EmissionPulseSpeed = _emissionPulseSpeed,
				MinEmission = _minEmission,
				MaxEmission = _maxEmission,
				AnimateScale = _animateScale,
				ScalePulseSpeed = _scalePulseSpeed,
				MinScaleRatio = _minScaleRatio,
				MaxScaleRatio = _maxScaleRatio,
				UpperFade = _upperFade,
				LowerFade = _lowerFade,
				PlacementMode = _placementMode,
				ProjectionSize = _projectionSize,
				ProjectionHeight = _projectionHeight,
				GroundOffset = _groundOffset,
				NormalFade = _normalFade,
				DistanceFadeEnabled = _distanceFadeEnabled,
				DistanceFadeBegin = _distanceFadeBegin,
				DistanceFadeEnd = _distanceFadeEnd
			};
		});

		ReloadBaseTexture();
		UpdateLivePreviewAndWorld();
	}
}
