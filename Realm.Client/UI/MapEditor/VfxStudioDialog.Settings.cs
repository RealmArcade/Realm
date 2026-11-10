using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Client.VFX;

namespace Realm.Client.UI.MapEditor;

public partial class VfxStudioDialog
{
	private void BuildBasicSettings(VBoxContainer parent)
	{
		var rowId = BuildIdentityRow(parent, "vfx/", out _lblObjectTypePrefix);

		_txtSlug = CreateSlugField("vfx_slug");
		_txtSlug.TextChanged += (val) =>
		{
			if (_isUpdatingUI) return;
			_slug = TemplateIDHelper.ToSnakeCase(val);
			_vfxId = TemplateIDHelper.NormalizeTemplateID("vfx", _slug);
			_currentConfig.VfxId = _vfxId;
			if (_currentConfig.ParticleConfig != null)
			{
				Particle.ParticleId = _vfxId;
			}
		};
		rowId.AddChild(_txtSlug);

		_txtVfxName = AddTextInput(parent, TranslationServer.Translate("Display Name:"), _currentConfig.Name, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.Name = val;
		}, "Human-readable name", 140f);

		string[] modeNames = new[] { "Primitive", "Particle" };
		int initialModeIdx = _currentConfig.PrimitiveType == VfxPrimitiveType.ParticleSystem ? 1 : 0;
		_optMode = AddOptionDropdown(parent, TranslationServer.Translate("Mode"), modeNames, initialModeIdx, (idx) =>
		{
			if (_isUpdatingUI) return;
			if (idx == 1)
			{
				_currentConfig.PrimitiveType = VfxPrimitiveType.ParticleSystem;
				EnsureParticleConfig();
			}
			else
			{
				if (_currentConfig.PrimitiveType == VfxPrimitiveType.ParticleSystem)
				{
					int primIdx = _optPrimitive != null ? Math.Clamp(_optPrimitive.Selected, 0, _primitiveTypes.Length - 1) : 0;
					_currentConfig.PrimitiveType = _primitiveTypes[primIdx];
				}
			}

			UpdateSectionVisibilities();
			RestartPreviewVfx();
		}, 140f);
	}

	private void BuildSettings(VBoxContainer parent)
	{
		AddSectionHeader(parent, "⚙️ " + TranslationServer.Translate("Settings"));

		string[] blendModes = Enum.GetNames<VfxBlendMode>();
		_optBlendMode = AddOptionDropdown(parent, TranslationServer.Translate("Blend Mode"), blendModes, (int)_currentConfig.BlendMode, (idx) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.BlendMode = (VfxBlendMode)idx;
			if (_currentConfig.ParticleConfig != null)
			{
				Particle.BlendMode = (VfxBlendMode)idx;
			}

			RestartPreviewVfx();
		}, 140f);

		string[] placementModes = new string[_particlePlacementModes.Length];
		for (int placementIndex = 0; placementIndex < _particlePlacementModes.Length; placementIndex++)
		{
			placementModes[placementIndex] = _particlePlacementModes[placementIndex].ToString();
		}

		_optPlacementMode = AddOptionDropdown(parent, TranslationServer.Translate("Placement Mode"), placementModes, GetParticlePlacementModeIndex(_currentConfig.PlacementMode), (idx) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.PlacementMode = _particlePlacementModes[Math.Clamp(idx, 0, _particlePlacementModes.Length - 1)];
		}, 140f);

		_chkTintWithPlayerColor = AddCheckBox(
			parent,
			TranslationServer.Translate("Tint with player color"),
			_currentConfig.TintWithPlayerColor,
			(val) =>
			{
				if (_isUpdatingUI) return;
				_currentConfig.TintWithPlayerColor = val;
				RestartPreviewVfx();
			},
			TranslationServer.Translate("Tints ground-level glows with the owning player's color instead of the authored effect colors")
		);

		string[] renderModes = Enum.GetNames<SpellParticleRenderMode>();
		_optParticleRenderMode = AddOptionDropdown(parent, TranslationServer.Translate("Render Mode"), renderModes, _currentConfig.ParticleConfig != null ? (int)Particle.RenderMode : 0, (idx) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			Particle.RenderMode = (SpellParticleRenderMode)idx;
			UpdateSectionVisibilities();
			RestartPreviewVfx();
		}, 140f);
		_rowParticleRenderMode = _optParticleRenderMode.GetParent() as Control;
	}

	private void BuildTextureSettings(VBoxContainer parent)
	{
		var baseTexTuple = AddAssetFilterDropdown(
			parent,
			TranslationServer.Translate("Base Texture (.rtex)"),
			_currentConfig.BaseTexture,
			(_) => ScanTextureAssets(),
			(val) =>
			{
				if (_isUpdatingUI) return;
				_currentConfig.BaseTexture = val;
				RestartPreviewVfx();
			},
			TranslationServer.Translate("Select ribbon/decal/texture asset..."),
			140f
		);
		_setBaseTextureVal = baseTexTuple.SetValue;
		_rowBaseTexture = baseTexTuple.Input.GetParent() as Control;

		var particleTexTuple = AddAssetFilterDropdown(
			parent,
			TranslationServer.Translate("Particle Texture"),
			"",
			(_) => ScanTextureAssets(),
			(val) =>
			{
				if (_isUpdatingUI) return;
				EnsureParticleConfig();
				Particle.ParticleTexture = val;
				RestartPreviewVfx();
			},
			TranslationServer.Translate("Select billboard texture..."),
			140f
		);
		_setParticleTextureVal = particleTexTuple.SetValue;
		_rowParticleTexture = particleTexTuple.Input.GetParent() as Control;

		if (_rowParticleTexture is HBoxContainer pTexRow)
		{
			var btnEditSheet = AddButton(
				pTexRow,
				"🎞️",
				() =>
				{
					string? tex = _currentConfig.ParticleConfig?.ParticleTexture;
					if (string.IsNullOrEmpty(tex)) return;
					var meta = VfxShaderManager.GetSpritesheetMetadataSafe(tex) ?? (Columns: 4, Rows: 4, Fps: 20.0f, SubframeBlend: true);
					_spritesheetEditDialog?.OpenForSheet(
						tex,
						meta.Columns,
						meta.Rows,
						meta.Fps,
						meta.SubframeBlend,
						(cols, rows, fps, subframeBlend) =>
						{
							SaveSpritesheetGrid(tex, cols, rows, fps, subframeBlend);
							VfxShaderManager.ClearCache();
							RestartPreviewVfx();
						}
					);
				},
				"Configure Spritesheet Animation (Grid & FPS)",
				11,
				new Vector2(24, 22)
			);
		}

		var particleMeshTuple = AddAssetFilterDropdown(
			parent,
			TranslationServer.Translate("Projectile Mesh"),
			"",
			(_) => ScanProjectileMeshAssets(),
			(val) =>
			{
				if (_isUpdatingUI) return;
				EnsureParticleConfig();
				Particle.MeshAssetPath = val;
				RestartPreviewVfx();
			},
			TranslationServer.Translate("Select .rmesh asset..."),
			140f
		);
		_setParticleMeshVal = particleMeshTuple.SetValue;
		_rowParticleMesh = particleMeshTuple.Input.GetParent() as Control;
	}

	private void BuildShaderSettings(VBoxContainer parent)
	{
		_uberShaderContainer = new VBoxContainer();
		_uberShaderContainer.Name = "UberShaderContainer_VBoxContainer";
		parent.AddChild(_uberShaderContainer);

		AddSectionHeader(_uberShaderContainer, "🔳 " + TranslationServer.Translate("Mask & Distortion"));

		SetupLuminanceControls();
		SetupMaskControls();
		SetupUvControls();

		AddSectionHeader(_uberShaderContainer, "🌊 " + TranslationServer.Translate("Procedural Motion"));
		SetupProceduralMotionControls();
	}

	private void SetupProceduralMotionControls()
	{
		_optProceduralMotion = AddOptionDropdown(_uberShaderContainer, TranslationServer.Translate("Motion Profile"), new[] { TranslationServer.Translate("None").ToString() }, 0, OnProceduralMotionSelected, 140f);
		RefreshProceduralMotionOptions();
	}

	private void RefreshProceduralMotionOptions()
	{
		if (_optProceduralMotion == null) return;

		Dictionary<string, ProceduralAnimationConfig> configs = ProceduralAnimationManager.LoadAllConfigs();
		_proceduralMotionIds = new List<string> { string.Empty };

		foreach (string motionId in configs.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
		{
			_proceduralMotionIds.Add(motionId);
		}

		bool wasUpdatingUI = _isUpdatingUI;
		_isUpdatingUI = true;
		try
		{
			_optProceduralMotion.Clear();
			_optProceduralMotion.AddItem(TranslationServer.Translate("None"), 0);

			for (int index = 1; index < _proceduralMotionIds.Count; index++)
			{
				string motionLabel = configs[_proceduralMotionIds[index]].Name;
				_optProceduralMotion.AddItem(string.IsNullOrWhiteSpace(motionLabel) ? _proceduralMotionIds[index] : motionLabel, index);
			}

			UpdateUIProceduralMotionSetting();
		}
		finally
		{
			_isUpdatingUI = wasUpdatingUI;
		}
	}

	private void OnProceduralMotionSelected(int index)
	{
		if (_isUpdatingUI) return;
		if (index < 0 || index >= _proceduralMotionIds.Count) return;

		_currentConfig.ProceduralMotionId = _proceduralMotionIds[index];
		RestartPreviewVfx();
	}

	private void SetupLuminanceControls()
	{
		_chkLuminanceToAlpha = AddCheckBox(_uberShaderContainer, TranslationServer.Translate("Luminance To Alpha"), _currentConfig.LuminanceToAlpha, OnLuminanceToAlphaChanged, "Converts grayscale intensity into transparency");
		(_sliderLuminanceThreshold, _lblLuminanceThreshold) = AddSlider(_uberShaderContainer, TranslationServer.Translate("Luma Threshold"), 0.0f, 1.0f, 0.05f, 0.1f, OnLuminanceThresholdChanged, "0.00", 140f);
		(_sliderLuminanceSmoothness, _lblLuminanceSmoothness) = AddSlider(_uberShaderContainer, TranslationServer.Translate("Luma Smoothness"), 0.0f, 1.0f, 0.05f, 0.1f, OnLuminanceSmoothnessChanged, "0.00", 140f);
	}

	private void SetupMaskControls()
	{
		_chkUseGrayscale = AddCheckBox(_uberShaderContainer, TranslationServer.Translate("Use Grayscale Base"), _currentConfig.UseGrayscale, OnUseGrayscaleChanged, "Discard color and use texture purely as a mask");
		_chkInvertMask = AddCheckBox(_uberShaderContainer, TranslationServer.Translate("Invert Base Alpha"), _currentConfig.InvertMask, OnInvertMaskChanged, "Inverts the calculated alpha mask");
		(_sliderHighPassCutoff, _lblHighPassCutoff) = AddSlider(_uberShaderContainer, TranslationServer.Translate("Alpha Cutoff"), 0.0f, 1.0f, 0.05f, 0.0f, OnHighPassCutoffChanged, "0.00", 140f);
	}

	private void SetupUvControls()
	{
		(_txtBaseUvScrollX, _txtBaseUvScrollY) = AddVector2Input(_uberShaderContainer, TranslationServer.Translate("Base UV Scroll"), Vector2.Zero, (Action<Vector2>)OnBaseUvScrollChanged, 140f);
		(_txtBaseUvScaleX, _txtBaseUvScaleY) = AddVector2Input(_uberShaderContainer, TranslationServer.Translate("Base UV Scale"), Vector2.One, (Action<Vector2>)OnBaseUvScaleChanged, 140f);

		var noiseTexTuple = AddAssetFilterDropdown(
			_uberShaderContainer,
			TranslationServer.Translate("Noise Texture (.rtex)"),
			_currentConfig.NoiseTexture,
			(_) => ScanNoiseAssets(),
			OnNoiseTextureChanged,
			TranslationServer.Translate("Select noise asset..."),
			140f
		);
		_setNoiseTextureVal = noiseTexTuple.SetValue;

		if (noiseTexTuple.Input.GetParent() is HBoxContainer noiseRow)
		{
			AddButton(noiseRow, "+", OnCreateNoiseTexture, "Create new procedural noise texture", 11, new Vector2(24, 22));
		}

		(_sliderDistortionStrength, _lblDistortionStrength) = AddSlider(_uberShaderContainer, TranslationServer.Translate("Distortion Strength"), 0.0f, 2.0f, 0.02f, _currentConfig.DistortionStrength, OnDistortionStrengthChanged, "0.00", 140f);
		(_txtNoiseUvScrollX, _txtNoiseUvScrollY) = AddVector2Input(_uberShaderContainer, TranslationServer.Translate("Noise UV Scroll"), Vector2.Zero, (Action<Vector2>)OnNoiseUvScrollChanged, 140f);
		(_txtNoiseUvScaleX, _txtNoiseUvScaleY) = AddVector2Input(_uberShaderContainer, TranslationServer.Translate("Noise UV Scale"), Vector2.One, (Action<Vector2>)OnNoiseUvScaleChanged, 140f);
	}

	private void OnLuminanceToAlphaChanged(bool val)
	{
		if (!_isUpdatingUI)
		{
			_currentConfig.LuminanceToAlpha = val;
			RestartPreviewVfx();
		}
	}

	private void OnLuminanceThresholdChanged(float val)
	{
		if (!_isUpdatingUI)
		{
			_currentConfig.LuminanceThreshold = val;
			RestartPreviewVfx();
		}
	}

	private void OnLuminanceSmoothnessChanged(float val)
	{
		if (!_isUpdatingUI)
		{
			_currentConfig.LuminanceSmoothness = val;
			RestartPreviewVfx();
		}
	}

	private void OnUseGrayscaleChanged(bool val)
	{
		if (!_isUpdatingUI)
		{
			_currentConfig.UseGrayscale = val;
			RestartPreviewVfx();
		}
	}

	private void OnInvertMaskChanged(bool val)
	{
		if (!_isUpdatingUI)
		{
			_currentConfig.InvertMask = val;
			RestartPreviewVfx();
		}
	}

	private void OnHighPassCutoffChanged(float val)
	{
		if (!_isUpdatingUI)
		{
			_currentConfig.HighPassCutoff = val;
			RestartPreviewVfx();
		}
	}

	private void OnBaseUvScrollChanged(Vector2 val)
	{
		if (!_isUpdatingUI)
		{
			_currentConfig.BaseUvScroll = val.ToVector2Data();
			RestartPreviewVfx();
		}
	}

	private void OnBaseUvScaleChanged(Vector2 val)
	{
		if (!_isUpdatingUI)
		{
			_currentConfig.BaseUvScale = val.ToVector2Data();
			RestartPreviewVfx();
		}
	}

	private void OnNoiseTextureChanged(string val)
	{
		if (!_isUpdatingUI)
		{
			_currentConfig.NoiseTexture = val;
			RestartPreviewVfx();
		}
	}

	private void OnCreateNoiseTexture()
	{
		Hud?.OpenNoiseTextureDialog((createdName) =>
		{
			if (!string.IsNullOrEmpty(createdName))
			{
				_currentConfig.NoiseTexture = createdName;
				_setNoiseTextureVal?.Invoke(createdName);
				RestartPreviewVfx();
			}
		});
	}

	private void OnDistortionStrengthChanged(float val)
	{
		if (!_isUpdatingUI)
		{
			_currentConfig.DistortionStrength = val;
			RestartPreviewVfx();
		}
	}

	private void OnNoiseUvScrollChanged(Vector2 val)
	{
		if (!_isUpdatingUI)
		{
			_currentConfig.NoiseUvScroll = val.ToVector2Data();
			RestartPreviewVfx();
		}
	}

	private void OnNoiseUvScaleChanged(Vector2 val)
	{
		if (!_isUpdatingUI)
		{
			_currentConfig.NoiseUvScale = val.ToVector2Data();
			RestartPreviewVfx();
		}
	}


	private void BuildTransformSettings(VBoxContainer parent)
	{
		AddSectionHeader(parent, "📐 " + TranslationServer.Translate("TRANSFORM & SURFACE OFFSET"), new Color(0.75f, 0.65f, 0.95f));

		string[] primitiveNames = _primitiveTypes.Select(p => p.ToString()).ToArray();
		int primitiveInitialIdx = Math.Max(0, Array.IndexOf(_primitiveTypes, _currentConfig.PrimitiveType));
		_optPrimitive = AddOptionDropdown(parent, TranslationServer.Translate("Primitive Shape"), primitiveNames, primitiveInitialIdx, (idx) =>
		{
			if (_isUpdatingUI) return;
			if (idx >= 0 && idx < _primitiveTypes.Length)
			{
				_currentConfig.PrimitiveType = _primitiveTypes[idx];
				RestartPreviewVfx();
			}
		}, 140f);
		_rowPrimitive = _optPrimitive.GetParent() as Control;

		(_sliderSurfaceNormalOffset, _lblSurfaceNormalOffset) = AddSlider(parent, TranslationServer.Translate("Surface Normal Offset"), -0.2f, 0.5f, 0.005f, _currentConfig.SurfaceNormalOffset, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.SurfaceNormalOffset = val;
			RestartPreviewVfx();
		}, "0.000", 140f);

		(_txtPosOffsetX, _txtPosOffsetY, _txtPosOffsetZ) = AddVector3Input(parent, TranslationServer.Translate("Position Offset"), _currentConfig.PositionOffset, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.PositionOffset = val;
			RestartPreviewVfx();
		}, 140f);

		(_txtRotOffsetX, _txtRotOffsetY, _txtRotOffsetZ) = AddVector3Input(parent, TranslationServer.Translate("Rotation (Pitch, Yaw, Roll)"), _currentConfig.RotationOffset, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.RotationOffset = val;
			RestartPreviewVfx();
		}, 140f);

		(_txtScaleOffsetX, _txtScaleOffsetY, _txtScaleOffsetZ) = AddVector3Input(parent, TranslationServer.Translate("Non-Uniform Scale"), _currentConfig.ScaleOffset, (val) =>
		{
			if (_isUpdatingUI) return;
			_currentConfig.ScaleOffset = val;
			RestartPreviewVfx();
		}, 140f);

		CancelButton.Text = TranslationServer.Translate("CANCEL");
		ApplyButton.Text = TranslationServer.Translate("SAVE & APPLY");
	}

}
