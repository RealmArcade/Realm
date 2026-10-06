using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Realm.Godot.Services;
using Realm.Godot.Utils;
using Realm.Godot.VFX;

public partial class AbilityVfxDialog : FloatingPreview3DDialogBase
{
	private ProceduralVfxInstance3D _vfxInstance;
	private MeshInstance3D _aoeRingMesh;
	private MeshInstance3D _aoeDiskMesh;
	private MeshInstance3D _groundGrid;
	private AudioStreamPlayer _sfxPlayer;

	private TextureRect _iconPreviewRect;
	private LineEdit _txtIconPath;
	private Action<string> _setIconPathValue;

	private LineEdit _txtVisualEffect;
	private Action<string> _setVisualEffectValue;
	private HSlider _sldAoeRadius;
	private Label _lblAoeRadiusVal;
	private LineEdit _txtCastSound;
	private Action<string> _setCastSoundValue;

	private string _abilityId = "";
	private string _slug = "";
	private Label _lblObjectTypePrefix;
	private LineEdit _txtSlug;
	private bool _isUpdatingUI = false;
	private string _abilityName = "";
	private string _initialVisualEffect = "";
	private string _initialCastSound = "";
	private string _initialIconPath = "";
	private float _initialAoeRadius = 0.0f;

	private string _currentVisualEffect = "";
	private string _currentCastSound = "";
	private string _currentIconPath = "";
	private float _currentAoeRadius = 4.0f;
	private float _playbackSpeed = 1.0f;
	private Action<JsonObject> _onApplied;

	public AbilityVfxDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Ability VFX & Audio Studio"), new Vector2(500, 720))
	{
		_sfxPlayer = new AudioStreamPlayer();
		AddChild(_sfxPlayer);

		DefaultDistance = 8.0f;
		CameraDistance = 8.0f;
		DefaultYaw = Mathf.DegToRad(45.0f);
		DefaultPitch = Mathf.DegToRad(30.0f);
		CameraYaw = Mathf.DegToRad(45.0f);
		CameraPitch = Mathf.DegToRad(30.0f);
		DefaultTargetPosition = new Vector3(0, 0.5f, 0);
		TargetPosition = DefaultTargetPosition;

		BuildControls();
	}

	private void BuildControls()
	{
		Add3DPreviewViewport(BodyContainer, new Vector2(480, 230));

		Setup3DEnvironment();

		var topControlsVBox = new VBoxContainer();
		topControlsVBox.AddThemeConstantOverride("separation", 6);
		BodyContainer.AddChild(topControlsVBox);

		var presetRow = AddCameraPresetToolbar(topControlsVBox, includeBack: false);

		var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		presetRow.AddChild(spacer);

		AddButton(presetRow, "🔥 " + TranslationServer.Translate("Cast Test"), () => TriggerCastTest(), "Simulate casting ability VFX & sound", 10, new Vector2(90, 22));

		var playbackRow = new HBoxContainer();
		playbackRow.AddThemeConstantOverride("separation", 6);

		AddButton(playbackRow, "▶ " + TranslationServer.Translate("Play"), () => PlayVfxAnimation(), "Play VFX", 10, new Vector2(50, 22));
		AddButton(playbackRow, "⏸ " + TranslationServer.Translate("Pause"), () => PauseVfxAnimation(), "Pause VFX", 10, new Vector2(50, 22));
		AddButton(playbackRow, "⏹ " + TranslationServer.Translate("Stop"), () => StopVfxAnimation(), "Stop VFX", 10, new Vector2(50, 22));

		var lblSpeed = new Label();
		lblSpeed.Text = TranslationServer.Translate("Speed:");
		lblSpeed.AddThemeFontSizeOverride("font_size", 10);
		lblSpeed.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		playbackRow.AddChild(lblSpeed);

		var sldSpeed = new HSlider();
		sldSpeed.MinValue = 0.25;
		sldSpeed.MaxValue = 2.50;
		sldSpeed.Step = 0.05;
		sldSpeed.Value = 1.0;
		sldSpeed.CustomMinimumSize = new Vector2(90, 0);
		sldSpeed.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		sldSpeed.ValueChanged += (val) =>
		{
			_playbackSpeed = (float)val;
			if (_vfxInstance != null && GodotObject.IsInstanceValid(_vfxInstance))
			{
				_vfxInstance.SetSpeedScale(_playbackSpeed);
			}
		};
		playbackRow.AddChild(sldSpeed);

		topControlsVBox.AddChild(playbackRow);

		var scrollBody = CreateScrollBody(340);
		var configVBox = new VBoxContainer();
		configVBox.AddThemeConstantOverride("separation", 10);
		configVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scrollBody.AddChild(configVBox);

		// SECTION 0: IDENTITY
		AddSectionHeader(configVBox, "🆔 " + TranslationServer.Translate("IDENTITY"), new Color(0.95f, 0.8f, 0.4f));

		var rowId = new HBoxContainer();
		rowId.AddThemeConstantOverride("separation", 6);
		var lblId = new Label();
		lblId.Text = TranslationServer.Translate("TemplateID:");
		lblId.CustomMinimumSize = new Vector2(140, 0);
		lblId.AddThemeFontSizeOverride("font_size", 11);
		rowId.AddChild(lblId);

		_lblObjectTypePrefix = new Label();
		_lblObjectTypePrefix.Text = "ability/";
		_lblObjectTypePrefix.AddThemeFontSizeOverride("font_size", 11);
		_lblObjectTypePrefix.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		rowId.AddChild(_lblObjectTypePrefix);

		_txtSlug = new LineEdit();
		_txtSlug.PlaceholderText = TranslationServer.Translate("ability_slug");
		_txtSlug.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_txtSlug.AddThemeFontSizeOverride("font_size", 11);
		_txtSlug.TextChanged += (val) =>
		{
			if (_isUpdatingUI) return;
			_slug = TemplateIDHelper.ToSnakeCase(val);
			_abilityId = TemplateIDHelper.NormalizeTemplateID("ability", _slug);
		};
		rowId.AddChild(_txtSlug);
		configVBox.AddChild(rowId);

		AddSectionHeader(configVBox, "🎨 " + TranslationServer.Translate("ABILITY ICON"), new Color(0.95f, 0.8f, 0.4f));

		var iconRow = new HBoxContainer();
		iconRow.AddThemeConstantOverride("separation", 8);

		var iconPanel = new PanelContainer();
		iconPanel.CustomMinimumSize = new Vector2(48, 48);
		iconPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());

		_iconPreviewRect = new TextureRect();
		_iconPreviewRect.CustomMinimumSize = new Vector2(44, 44);
		_iconPreviewRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_iconPreviewRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		iconPanel.AddChild(_iconPreviewRect);
		iconRow.AddChild(iconPanel);

		var iconDropdownVBox = new VBoxContainer();
		iconDropdownVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

		(_txtIconPath, _setIconPathValue) = AddAssetFilterDropdown(
			iconDropdownVBox,
			TranslationServer.Translate("Icon Texture:"),
			_currentIconPath,
			(all) => ScanAvailableAssets("icons", all),
			(val) =>
			{
				_currentIconPath = val ?? string.Empty;
				UpdateIconPreview(_currentIconPath);
			},
			TranslationServer.Translate("Select or search icon from metadata..."),
			100f
		);

		iconRow.AddChild(iconDropdownVBox);
		configVBox.AddChild(iconRow);

		AddSectionHeader(configVBox, "✨ " + TranslationServer.Translate("VISUAL EFFECT (VFX)"), new Color(0.35f, 0.75f, 0.9f));

		(_txtVisualEffect, _setVisualEffectValue) = AddAssetFilterDropdown(
			configVBox,
			TranslationServer.Translate("Visual Effect (VFX):"),
			_currentVisualEffect,
			(all) => ScanAvailableAssets("vfx", all),
			(val) =>
			{
				_currentVisualEffect = val ?? string.Empty;
				ReloadVfx();
			},
			TranslationServer.Translate("Select or search VFX preset, custom VFX, or texture..."),
			140f
		);

		var vfxButtonsRow = new HBoxContainer();
		vfxButtonsRow.AddThemeConstantOverride("separation", 6);

		var vfxSpacer = new Control { CustomMinimumSize = new Vector2(140f, 0) };
		vfxButtonsRow.AddChild(vfxSpacer);

		AddButton(vfxButtonsRow, "✨ " + TranslationServer.Translate("Edit in VFX Studio..."), () => OpenVfxStudioForCurrentAbility(), "Open Procedural VFX Studio to edit this VFX preset or create custom visuals", 10, new Vector2(160, 24));
		AddButton(vfxButtonsRow, "➕ " + TranslationServer.Translate("New VFX Preset..."), () => CreateNewVfxForAbility(), "Create a new custom procedural VFX preset for this ability", 10, new Vector2(140, 24));

		configVBox.AddChild(vfxButtonsRow);

		AddSectionHeader(configVBox, "🎯 " + TranslationServer.Translate("AREA OF EFFECT (AOE)"), new Color(0.4f, 0.85f, 0.5f));

		(_sldAoeRadius, _lblAoeRadiusVal) = AddSlider(
			configVBox,
			TranslationServer.Translate("AoE Radius:"),
			0.0f,
			20.0f,
			0.25f,
			_currentAoeRadius,
			(val) =>
			{
				_currentAoeRadius = val;
				UpdateAoEIndicator(_currentAoeRadius);
			},
			"0.00m",
			140f
		);

		AddSectionHeader(configVBox, "🔊 " + TranslationServer.Translate("AUDIO & CAST SOUND"), new Color(0.9f, 0.6f, 0.35f));

		(_txtCastSound, _setCastSoundValue) = AddAssetFilterDropdown(
			configVBox,
			TranslationServer.Translate("Cast Sound:"),
			_currentCastSound,
			(all) => ScanAvailableAssets("audio", all),
			(val) =>
			{
				_currentCastSound = val ?? string.Empty;
			},
			TranslationServer.Translate("Select or search audio event..."),
			140f,
			false,
			(soundVal) => PlaySoundFile(soundVal)
		);
	}

	private void Setup3DEnvironment()
	{
		if (PreviewSceneRoot == null) return;

		_groundGrid = new MeshInstance3D();
		_groundGrid.Name = "GroundGrid";
		var planeMesh = new PlaneMesh { Size = new Vector2(24f, 24f), SubdivideWidth = 24, SubdivideDepth = 24 };
		_groundGrid.Mesh = planeMesh;
		var gridMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.12f, 0.14f, 0.18f, 0.85f),
			Roughness = 0.8f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha
		};
		_groundGrid.MaterialOverride = gridMat;
		_groundGrid.Position = new Vector3(0, -0.01f, 0);
		PreviewSceneRoot.AddChild(_groundGrid);

		var diskMesh = new CylinderMesh
		{
			TopRadius = 1.0f,
			BottomRadius = 1.0f,
			Height = 0.005f
		};
		var diskMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.2f, 0.7f, 1.0f, 0.15f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
		};
		_aoeDiskMesh = new MeshInstance3D { Mesh = diskMesh, MaterialOverride = diskMat, Position = new Vector3(0, 0.01f, 0) };
		PreviewSceneRoot.AddChild(_aoeDiskMesh);

		var ringMesh = new TorusMesh
		{
			InnerRadius = 0.97f,
			OuterRadius = 1.0f,
			Rings = 48,
			RingSegments = 8
		};
		var ringMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.25f, 0.85f, 1.0f, 0.85f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
		};
		_aoeRingMesh = new MeshInstance3D { Mesh = ringMesh, MaterialOverride = ringMat, Position = new Vector3(0, 0.015f, 0) };
		PreviewSceneRoot.AddChild(_aoeRingMesh);

		UpdateAoEIndicator(_currentAoeRadius);
	}

	private void UpdateAoEIndicator(float radius)
	{
		if (_aoeRingMesh == null || _aoeDiskMesh == null) return;

		if (radius <= 0.05f)
		{
			_aoeRingMesh.Visible = false;
			_aoeDiskMesh.Visible = false;
			return;
		}

		_aoeRingMesh.Visible = true;
		_aoeDiskMesh.Visible = true;

		_aoeRingMesh.Scale = new Vector3(radius, 1.0f, radius);
		_aoeDiskMesh.Scale = new Vector3(radius, 1.0f, radius);
	}

	private void UpdateIconPreview(string iconPath)
	{
		if (_iconPreviewRect == null) return;
		var tex = ResolveTexture(iconPath);
		_iconPreviewRect.Texture = tex;
	}

	private VfxAttachmentConfig ResolveVfxConfig(string visualEffect)
	{
		if (string.IsNullOrWhiteSpace(visualEffect))
		{
			return new VfxAttachmentConfig { VfxId = "vfx_none", PrimitiveType = VfxPrimitiveType.CrossQuad };
		}

		string vfxKey = visualEffect.Trim();
		if (vfxKey.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase))
		{
			vfxKey = vfxKey.Substring(4);
		}

		if (GameHost.VfxRegistry.TryGetValue(vfxKey, out var regCfg))
		{
			return regCfg.Clone();
		}

		if (Enum.TryParse<VfxPrimitiveType>(vfxKey, true, out var primType))
		{
			return new VfxAttachmentConfig { VfxId = vfxKey, PrimitiveType = primType };
		}

		if (vfxKey.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ||
			vfxKey.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
			vfxKey.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
			vfxKey.Contains('/'))
		{
			var pConfig = new SpellParticleConfig
			{
				ParticleId = vfxKey,
				Name = System.IO.Path.GetFileNameWithoutExtension(vfxKey),
				RenderMode = SpellParticleRenderMode.BillboardQuad,
				ParticleTexture = vfxKey,
				Amount = 24,
				Lifetime = 1.2f,
				ColorStart = "#FFE066",
				ColorMid = "#FF6600",
				ColorEnd = "#990000",
				EmissionEnergy = 3.0f,
				InitialScaleMin = 0.4f,
				InitialScaleMax = 0.8f
			};
			return new VfxAttachmentConfig
			{
				VfxId = vfxKey,
				Name = System.IO.Path.GetFileNameWithoutExtension(vfxKey),
				PrimitiveType = VfxPrimitiveType.ParticleSystem,
				ParticleConfig = pConfig
			};
		}

		return new VfxAttachmentConfig { VfxId = vfxKey, Name = vfxKey };
	}

	private void ReloadVfx()
	{
		if (PreviewSceneRoot == null) return;

		if (_vfxInstance != null && GodotObject.IsInstanceValid(_vfxInstance))
		{
			_vfxInstance.QueueFree();
			_vfxInstance = null;
		}

		if (string.IsNullOrWhiteSpace(_currentVisualEffect))
		{
			return;
		}

		var config = ResolveVfxConfig(_currentVisualEffect);
		_vfxInstance = new ProceduralVfxInstance3D(config);
		_vfxInstance.Name = "AbilityVfxPreview";
		PreviewSceneRoot.AddChild(_vfxInstance);
		_vfxInstance.Position = new Vector3(0, 0.5f, 0);
		_vfxInstance.SetSpeedScale(_playbackSpeed);
	}

	private void OpenVfxStudioForCurrentAbility()
	{
		VfxAttachmentConfig targetConfig = null;
		if (!string.IsNullOrWhiteSpace(_currentVisualEffect))
		{
			targetConfig = ResolveVfxConfig(_currentVisualEffect);
		}

		if (targetConfig == null || string.IsNullOrWhiteSpace(targetConfig.VfxId) || targetConfig.VfxId == "vfx_none")
		{
			targetConfig = new VfxAttachmentConfig
			{
				VfxId = !string.IsNullOrWhiteSpace(_abilityId) ? $"vfx_{_abilityId}" : "vfx_custom",
				Name = !string.IsNullOrWhiteSpace(_abilityName) ? $"{_abilityName} VFX" : "Ability VFX",
				PrimitiveType = VfxPrimitiveType.ParticleSystem,
				ParticleConfig = new SpellParticleConfig
				{
					ParticleId = !string.IsNullOrWhiteSpace(_abilityId) ? $"vfx_{_abilityId}" : "vfx_custom",
					Name = !string.IsNullOrWhiteSpace(_abilityName) ? $"{_abilityName} Particles" : "Ability Particles",
					RenderMode = SpellParticleRenderMode.BillboardQuad,
					Amount = 32,
					Lifetime = 1.0f
				}
			};
		}

		Hud?.OpenVfxStudioDialog(targetConfig, (savedCfg) =>
		{
			string key = $"vfx:{savedCfg.VfxId}";
			_currentVisualEffect = key;
			_setVisualEffectValue?.Invoke(key);
			ReloadVfx();
		});
	}

	private void CreateNewVfxForAbility()
	{
		var newConfig = new VfxAttachmentConfig
		{
			VfxId = !string.IsNullOrWhiteSpace(_abilityId) ? $"vfx_{_abilityId}" : $"vfx_spell_{Random.Shared.Next(100, 999)}",
			Name = !string.IsNullOrWhiteSpace(_abilityName) ? $"{_abilityName} VFX" : "Spell VFX",
			PrimitiveType = VfxPrimitiveType.ParticleSystem,
			ParticleConfig = new SpellParticleConfig
			{
				ParticleId = !string.IsNullOrWhiteSpace(_abilityId) ? $"vfx_{_abilityId}" : "vfx_spell",
				Name = !string.IsNullOrWhiteSpace(_abilityName) ? $"{_abilityName} VFX" : "Spell VFX",
				RenderMode = SpellParticleRenderMode.BillboardQuad,
				Amount = 32,
				Lifetime = 1.2f,
				ColorStart = "#FFE066",
				ColorMid = "#FF6600",
				ColorEnd = "#990000",
				EmissionEnergy = 3.5f
			}
		};

		Hud?.OpenVfxStudioDialog(newConfig, (savedCfg) =>
		{
			string key = $"vfx:{savedCfg.VfxId}";
			_currentVisualEffect = key;
			_setVisualEffectValue?.Invoke(key);
			ReloadVfx();
		});
	}

	private void TriggerCastTest()
	{
		if (_vfxInstance != null && GodotObject.IsInstanceValid(_vfxInstance))
		{
			_vfxInstance.Restart();
		}
		else
		{
			ReloadVfx();
		}

		PlaySoundFile(_currentCastSound);
	}

	private void PlayVfxAnimation()
	{
		if (_vfxInstance != null && GodotObject.IsInstanceValid(_vfxInstance))
		{
			_vfxInstance.Play();
		}
		else
		{
			ReloadVfx();
		}
	}

	private void PauseVfxAnimation()
	{
		if (_vfxInstance != null && GodotObject.IsInstanceValid(_vfxInstance))
		{
			_vfxInstance.Stop();
		}
	}

	private void StopVfxAnimation()
	{
		if (_vfxInstance != null && GodotObject.IsInstanceValid(_vfxInstance))
		{
			_vfxInstance.Stop();
		}
	}

	private void PlaySoundFile(string soundPath)
	{
		if (string.IsNullOrWhiteSpace(soundPath) || _sfxPlayer == null) return;

		try
		{
			if (soundPath.StartsWith("res://"))
			{
				if (ResourceLoader.Exists(soundPath))
				{
					_sfxPlayer.Stream = GD.Load<AudioStream>(soundPath);
					_sfxPlayer.Play();
					return;
				}
			}

			string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
			string cleanPath = soundPath.Trim().TrimStart('/', '\\').Replace('\\', '/');
			string fileName = System.IO.Path.GetFileName(cleanPath);

			var candidatePaths = new List<string>
			{
				soundPath,
				System.IO.Path.Combine(wsPath, cleanPath),
				System.IO.Path.Combine(wsPath, "Assets", cleanPath),
				System.IO.Path.Combine(wsPath, "Assets", "audio", "sfx", fileName),
				System.IO.Path.Combine(wsPath, "Assets", "audio", "music", fileName),
				System.IO.Path.Combine(wsPath, "Assets", "audio", fileName),
				System.IO.Path.Combine(wsPath, "Assets", "sounds", fileName),
			};

			AudioStream stream = null;
			foreach (var candidate in candidatePaths)
			{
				if (!string.IsNullOrWhiteSpace(candidate) && System.IO.File.Exists(candidate))
				{
					if (candidate.EndsWith(".raud", StringComparison.OrdinalIgnoreCase))
					{
						byte[] raudBytes = System.IO.File.ReadAllBytes(candidate);
						byte[]? oggBytes = Realm.Shared.Audio.RaudFile.GetTrack(raudBytes, 0);
						if (oggBytes != null && oggBytes.Length > 0)
						{
							stream = AudioStreamOggVorbis.LoadFromBuffer(oggBytes);
						}
					}
					else if (candidate.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
					{
						stream = AudioStreamOggVorbis.LoadFromFile(candidate);
					}
					else
					{
						stream = GD.Load<AudioStream>(candidate);
					}
					if (stream != null) break;
				}
			}

			if (stream == null)
			{
				var resCandidates = new[]
				{
					$"res://Assets/audio/sfx/{fileName}",
					$"res://Assets/audio/music/{fileName}",
					$"res://Assets/audio/{fileName}",
					$"res://Assets/sounds/{fileName}",
					$"res://{cleanPath}"
				};
				foreach (var resPath in resCandidates)
				{
					if (ResourceLoader.Exists(resPath))
					{
						stream = GD.Load<AudioStream>(resPath);
						if (stream != null) break;
					}
				}
			}

			if (stream != null)
			{
				_sfxPlayer.Stream = stream;
				_sfxPlayer.Play();
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AbilityVfxDialog] PlaySoundFile error: {ex.Message}");
		}
	}

	private Texture2D ResolveTexture(string path)
	{
		if (string.IsNullOrWhiteSpace(path)) return null;

		try
		{
			if (path.StartsWith("res://"))
			{
				if (ResourceLoader.Exists(path))
				{
					return GD.Load<Texture2D>(path);
				}
			}

			string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
			string cleanPath = path.Trim().TrimStart('/', '\\').Replace('\\', '/');
			string fileName = System.IO.Path.GetFileName(cleanPath);

			var candidatePaths = new List<string>
			{
				path,
				System.IO.Path.Combine(wsPath, cleanPath),
				System.IO.Path.Combine(wsPath, "Assets", cleanPath),
				System.IO.Path.Combine(wsPath, "Assets", "vfx", fileName),
				System.IO.Path.Combine(wsPath, "Assets", "icons", fileName),
				System.IO.Path.Combine(wsPath, "Assets", "decals", fileName),
				System.IO.Path.Combine(wsPath, "Assets", "textures", fileName),
				System.IO.Path.Combine(wsPath, "Assets", "ribbons", fileName),
				System.IO.Path.Combine(wsPath, "Assets", "noise", fileName),
				System.IO.Path.Combine(wsPath, "Assets", "skyboxes", fileName),
				System.IO.Path.Combine(wsPath, "Assets", "UI", fileName),
			};

			foreach (var candidate in candidatePaths)
			{
				if (!string.IsNullOrWhiteSpace(candidate) && System.IO.File.Exists(candidate))
				{
					Image? img = null;
					if (candidate.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
					{
						byte[] rtexBytes = System.IO.File.ReadAllBytes(candidate);
						byte[]? webpBytes = Realm.Shared.Textures.RtexFile.GetLayer(rtexBytes, 0);
						if (webpBytes != null && webpBytes.Length > 0)
						{
							img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
							if (img.LoadWebpFromBuffer(webpBytes) != Error.Ok)
							{
								img.LoadPngFromBuffer(webpBytes);
							}
						}
					}
					else
					{
						img = Image.LoadFromFile(candidate);
					}

					if (img != null)
					{
						if (!img.HasMipmaps())
						{
							img.GenerateMipmaps();
						}
						return ImageTexture.CreateFromImage(img);
					}
				}
			}

			var resCandidates = new[]
			{
				$"res://Assets/vfx/{fileName}",
				$"res://Assets/icons/{fileName}",
				$"res://Assets/decals/{fileName}",
				$"res://Assets/textures/{fileName}",
				$"res://Assets/UI/{fileName}",
				$"res://{cleanPath}"
			};

			foreach (var resPath in resCandidates)
			{
				if (ResourceLoader.Exists(resPath))
				{
					return GD.Load<Texture2D>(resPath);
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AbilityVfxDialog] ResolveTexture error: {ex.Message}");
		}

		return null;
	}

	public void OpenForAbility(string abilityId, JsonObject abilityData, Action<JsonObject> onApplied = null)
	{
		string effectiveId = abilityData?.TryGetPropertyValue("TemplateID", out var tidNode) == true && !string.IsNullOrWhiteSpace(tidNode?.ToString())
			? tidNode.ToString()
			: (abilityData?.TryGetPropertyValue("AbilityId", out var aidNode) == true && !string.IsNullOrWhiteSpace(aidNode?.ToString())
				? aidNode.ToString()
				: abilityId);

		var (_, parsedSlug) = TemplateIDHelper.ParseTemplateID(effectiveId);
		_slug = !string.IsNullOrWhiteSpace(parsedSlug) ? TemplateIDHelper.ToSnakeCase(parsedSlug) : TemplateIDHelper.ToSnakeCase(effectiveId);
		_abilityId = TemplateIDHelper.NormalizeTemplateID("ability", _slug);
		_abilityName = abilityData?["Name"]?.ToString() ?? _abilityId;
		_onApplied = onApplied;

		TitleLabel.Text = $"{TranslationServer.Translate("Ability VFX Studio")} - {_abilityName}";

		_currentVisualEffect = abilityData?["VisualEffect"]?.ToString() ?? string.Empty;
		_currentCastSound = abilityData?["CastSound"]?.ToString() ?? string.Empty;
		string rawIcon = abilityData?["IconPath"]?.ToString() ?? string.Empty;
		if (!string.IsNullOrEmpty(rawIcon) && rawIcon.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			_currentIconPath = Path.GetFileName(rawIcon);
		}
		else
		{
			_currentIconPath = string.Empty;
		}
		_currentAoeRadius = abilityData?["AreaOfEffectRadius"] != null ? (float)abilityData["AreaOfEffectRadius"] : 4.0f;

		_initialVisualEffect = _currentVisualEffect;
		_initialCastSound = _currentCastSound;
		_initialIconPath = _currentIconPath;
		_initialAoeRadius = _currentAoeRadius;

		_isUpdatingUI = true;
		if (_txtSlug != null) _txtSlug.Text = _slug;
		_setVisualEffectValue?.Invoke(_currentVisualEffect);
		_setCastSoundValue?.Invoke(_currentCastSound);
		_setIconPathValue?.Invoke(_currentIconPath);
		if (_sldAoeRadius != null) _sldAoeRadius.Value = _currentAoeRadius;
		_isUpdatingUI = false;

		UpdateIconPreview(_currentIconPath);
		UpdateAoEIndicator(_currentAoeRadius);
		ReloadVfx();

		OpenDialog();
		ResetCameraDefault();
	}

	protected override void OnApply()
	{
		string finalSlug = !string.IsNullOrWhiteSpace(_txtSlug?.Text) ? TemplateIDHelper.ToSnakeCase(_txtSlug.Text) : _slug;
		if (string.IsNullOrWhiteSpace(finalSlug)) finalSlug = _slug;
		_slug = finalSlug;
		_abilityId = TemplateIDHelper.NormalizeTemplateID("ability", _slug);

		if (!string.IsNullOrEmpty(_abilityId))
		{
			Hud?.SaveCustomAbilityVfxToMetadata(
				_abilityId,
				_currentVisualEffect,
				_currentCastSound,
				_currentIconPath,
				_currentAoeRadius
			);

			var updatedData = new JsonObject
			{
				["TemplateID"] = _abilityId,
				["template_id"] = _abilityId,
				["AbilityId"] = _abilityId,
				["Name"] = _abilityName,
				["VisualEffect"] = _currentVisualEffect,
				["CastSound"] = _currentCastSound,
				["IconPath"] = _currentIconPath,
				["AreaOfEffectRadius"] = _currentAoeRadius
			};

			_onApplied?.Invoke(updatedData);
			Hud?.ShowFeedback(TranslationServer.Translate("Ability VFX and audio updated successfully."));
		}

		StopVfxAnimation();
	}

	protected override void OnCancel()
	{
		StopVfxAnimation();
	}

	public override void CloseDialog()
	{
		StopVfxAnimation();
		base.CloseDialog();
	}
}
