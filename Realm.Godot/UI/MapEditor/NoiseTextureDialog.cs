using Godot;
using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Realm.Godot.Services;

public partial class NoiseTextureDialog : FloatingDialogBase
{
	[GeneratedRegex(@"^(.*)_(\d+)$")]
	private static partial Regex TrailingIndexRegex();

	private LineEdit _txtName;
	private OptionButton _optResolution;
	private OptionButton _optNoiseType;
	private LineEdit _txtSeed;
	private Button _btnRandomizeSeed;
	private HSlider _sliderFrequency;
	private Label _lblFrequencyVal;
	private OptionButton _optFractalType;
	private HSlider _sliderOctaves;
	private Label _lblOctavesVal;
	private HSlider _sliderLacunarity;
	private Label _lblLacunarityVal;
	private HSlider _sliderGain;
	private Label _lblGainVal;
	private HSlider _sliderWeightedStrength;
	private Label _lblWeightedStrengthVal;

	private VBoxContainer _cellularGroup;
	private OptionButton _optCellularDistFunc;
	private OptionButton _optCellularReturnType;
	private HSlider _sliderCellularJitter;
	private Label _lblCellularJitterVal;

	private VBoxContainer _domainWarpGroup;
	private CheckBox _chkDomainWarpEnabled;
	private OptionButton _optDomainWarpType;
	private HSlider _sliderDomainWarpAmplitude;
	private Label _lblDomainWarpAmplitudeVal;
	private HSlider _sliderDomainWarpFrequency;
	private Label _lblDomainWarpFrequencyVal;
	private HSlider _sliderDomainWarpOctaves;
	private Label _lblDomainWarpOctavesVal;

	private CheckBox _chkInvert;
	private CheckBox _chkNormalize;

	private OptionButton _optColorMode;
	private HBoxContainer _colorRampRow;
	private ColorPickerButton _pickerColorA;
	private ColorPickerButton _pickerColorB;

	private TextureRect _previewTextureRect;
	private Label _lblPreviewInfo;

	private Action<string> _onSavedCallback;
	private bool _isUpdatingPreview;

	public NoiseTextureDialog(MapEditorHUD hud) : base(hud, TranslationServer.Translate("Procedural Noise Texture Generator"), new Vector2(740, 560))
	{
		BuildDialogUi();
	}

	private void BuildDialogUi()
	{
		var contentHBox = new HBoxContainer();
		contentHBox.AddThemeConstantOverride("separation", 14);
		contentHBox.SizeFlagsVertical = SizeFlags.ExpandFill;
		BodyContainer.AddChild(contentHBox);

		var scrollLeft = new ScrollContainer();
		scrollLeft.CustomMinimumSize = new Vector2(400, 0);
		scrollLeft.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scrollLeft.SizeFlagsVertical = SizeFlags.ExpandFill;
		contentHBox.AddChild(scrollLeft);

		var leftVBox = new VBoxContainer();
		leftVBox.AddThemeConstantOverride("separation", 8);
		leftVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scrollLeft.AddChild(leftVBox);

		AddSectionHeader(leftVBox, TranslationServer.Translate("TEXTURE ASSET DETAILS"));

		_txtName = AddTextInput(leftVBox, TranslationServer.Translate("Asset Name:"), "procedural_noise_1", (_) => SchedulePreviewUpdate(), TranslationServer.Translate("e.g. magic_swirl_noise"), 120f);

		_optResolution = AddOptionDropdown(leftVBox, TranslationServer.Translate("Resolution:"), new[] { "128x128", "256x256", "512x512", "1024x1024" }, 2, (_) => SchedulePreviewUpdate(), 120f);

		string[] colorModes = new[] { "Grayscale", "ColorRamp" };
		_optColorMode = AddOptionDropdown(leftVBox, TranslationServer.Translate("Color Mode:"), colorModes, 0, (idx) =>
		{
			UpdateVisibility();
			SchedulePreviewUpdate();
		}, 120f);

		_colorRampRow = new HBoxContainer();
		_colorRampRow.AddThemeConstantOverride("separation", 8);
		_colorRampRow.Visible = false;

		var lblColorRamp = new Label();
		lblColorRamp.Text = TranslationServer.Translate("Ramp Colors:");
		lblColorRamp.CustomMinimumSize = new Vector2(120f, 0);
		lblColorRamp.AddThemeFontSizeOverride("font_size", 11);
		_colorRampRow.AddChild(lblColorRamp);

		var lblA = new Label { Text = "A:" };
		lblA.AddThemeFontSizeOverride("font_size", 10);
		_colorRampRow.AddChild(lblA);
		_pickerColorA = new ColorPickerButton { CustomMinimumSize = new Vector2(40, 24), Color = Colors.Black, EditAlpha = false };
		_pickerColorA.ColorChanged += (_) => SchedulePreviewUpdate();
		_colorRampRow.AddChild(_pickerColorA);

		var lblB = new Label { Text = "B:" };
		lblB.AddThemeFontSizeOverride("font_size", 10);
		_colorRampRow.AddChild(lblB);
		_pickerColorB = new ColorPickerButton { CustomMinimumSize = new Vector2(40, 24), Color = Colors.White, EditAlpha = false };
		_pickerColorB.ColorChanged += (_) => SchedulePreviewUpdate();
		_colorRampRow.AddChild(_pickerColorB);

		leftVBox.AddChild(_colorRampRow);

		_chkInvert = AddCheckBox(leftVBox, TranslationServer.Translate("Invert Colors"), false, (_) => SchedulePreviewUpdate());
		_chkNormalize = AddCheckBox(leftVBox, TranslationServer.Translate("Normalize (Full Contrast)"), true, (_) => SchedulePreviewUpdate());

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
		btnRandomizeAll.Pressed += () => RandomizeAllNoiseParameters();
		leftVBox.AddChild(btnRandomizeAll);

		AddSectionHeader(leftVBox, TranslationServer.Translate("NOISE PARAMETERS"));

		string[] noiseTypes = new[] { "Perlin", "Simplex", "SimplexSmooth", "Cellular", "ValueCubic", "Value" };
		_optNoiseType = AddOptionDropdown(leftVBox, TranslationServer.Translate("Noise Type:"), noiseTypes, 0, (idx) =>
		{
			UpdateVisibility();
			SchedulePreviewUpdate();
		}, 120f);

		var seedRow = new HBoxContainer();
		seedRow.AddThemeConstantOverride("separation", 6);
		var lblSeed = new Label();
		lblSeed.Text = TranslationServer.Translate("Seed:");
		lblSeed.CustomMinimumSize = new Vector2(120f, 0);
		lblSeed.AddThemeFontSizeOverride("font_size", 11);
		seedRow.AddChild(lblSeed);

		_txtSeed = new LineEdit();
		_txtSeed.Text = "1337";
		_txtSeed.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_txtSeed.AddThemeFontSizeOverride("font_size", 11);
		_txtSeed.TextChanged += (_) => SchedulePreviewUpdate();
		seedRow.AddChild(_txtSeed);

		_btnRandomizeSeed = new Button();
		_btnRandomizeSeed.Set("icon_max_width", 0);
		_btnRandomizeSeed.Text = "🎲 " + TranslationServer.Translate("Random");
		_btnRandomizeSeed.CustomMinimumSize = new Vector2(70, 22);
		_btnRandomizeSeed.AddThemeFontSizeOverride("font_size", 10);
		_btnRandomizeSeed.Pressed += () =>
		{
			_txtSeed.Text = Random.Shared.Next(1, 999999).ToString();
			SchedulePreviewUpdate();
		};
		seedRow.AddChild(_btnRandomizeSeed);
		leftVBox.AddChild(seedRow);

		(_sliderFrequency, _lblFrequencyVal) = AddSlider(leftVBox, TranslationServer.Translate("Frequency:"), 0.001f, 0.1f, 0.001f, 0.015f, (_) => SchedulePreviewUpdate(), "0.000", 120f);

		string[] fractalTypes = new[] { "Fbm", "Ridged", "PingPong", "None" };
		_optFractalType = AddOptionDropdown(leftVBox, TranslationServer.Translate("Fractal Type:"), fractalTypes, 0, (_) => SchedulePreviewUpdate(), 120f);

		(_sliderOctaves, _lblOctavesVal) = AddSlider(leftVBox, TranslationServer.Translate("Octaves:"), 1f, 10f, 1f, 5f, (_) => SchedulePreviewUpdate(), "0", 120f);
		(_sliderLacunarity, _lblLacunarityVal) = AddSlider(leftVBox, TranslationServer.Translate("Lacunarity:"), 1.0f, 4.0f, 0.1f, 2.0f, (_) => SchedulePreviewUpdate(), "0.0", 120f);
		(_sliderGain, _lblGainVal) = AddSlider(leftVBox, TranslationServer.Translate("Gain:"), 0.05f, 1.0f, 0.05f, 0.5f, (_) => SchedulePreviewUpdate(), "0.00", 120f);
		(_sliderWeightedStrength, _lblWeightedStrengthVal) = AddSlider(leftVBox, TranslationServer.Translate("Weighted Strength:"), 0.0f, 1.0f, 0.05f, 0.0f, (_) => SchedulePreviewUpdate(), "0.00", 120f);

		_cellularGroup = new VBoxContainer();
		_cellularGroup.AddThemeConstantOverride("separation", 6);
		_cellularGroup.Visible = false;
		AddSectionHeader(_cellularGroup, TranslationServer.Translate("CELLULAR SETTINGS"));

		string[] distFuncs = new[] { "Euclidean", "EuclideanSquared", "Manhattan", "Hybrid" };
		_optCellularDistFunc = AddOptionDropdown(_cellularGroup, TranslationServer.Translate("Dist Function:"), distFuncs, 0, (_) => SchedulePreviewUpdate(), 120f);

		string[] retTypes = new[] { "CellValue", "Distance", "Distance2", "Distance2Add", "Distance2Sub", "Distance2Mul", "Distance2Div" };
		_optCellularReturnType = AddOptionDropdown(_cellularGroup, TranslationServer.Translate("Return Type:"), retTypes, 1, (_) => SchedulePreviewUpdate(), 120f);

		(_sliderCellularJitter, _lblCellularJitterVal) = AddSlider(_cellularGroup, TranslationServer.Translate("Jitter:"), 0.0f, 2.0f, 0.05f, 1.0f, (_) => SchedulePreviewUpdate(), "0.00", 120f);
		leftVBox.AddChild(_cellularGroup);

		_domainWarpGroup = new VBoxContainer();
		_domainWarpGroup.AddThemeConstantOverride("separation", 6);
		AddSectionHeader(_domainWarpGroup, TranslationServer.Translate("DOMAIN WARP"));

		_chkDomainWarpEnabled = AddCheckBox(_domainWarpGroup, TranslationServer.Translate("Enable Domain Warp"), false, (_) =>
		{
			UpdateVisibility();
			SchedulePreviewUpdate();
		});

		string[] warpTypes = new[] { "Simplex", "SimplexReduced", "BasicGrid" };
		_optDomainWarpType = AddOptionDropdown(_domainWarpGroup, TranslationServer.Translate("Warp Type:"), warpTypes, 0, (_) => SchedulePreviewUpdate(), 120f);

		(_sliderDomainWarpAmplitude, _lblDomainWarpAmplitudeVal) = AddSlider(_domainWarpGroup, TranslationServer.Translate("Amplitude:"), 1f, 100f, 1f, 30f, (_) => SchedulePreviewUpdate(), "0", 120f);
		(_sliderDomainWarpFrequency, _lblDomainWarpFrequencyVal) = AddSlider(_domainWarpGroup, TranslationServer.Translate("Warp Frequency:"), 0.001f, 0.2f, 0.005f, 0.05f, (_) => SchedulePreviewUpdate(), "0.000", 120f);
		(_sliderDomainWarpOctaves, _lblDomainWarpOctavesVal) = AddSlider(_domainWarpGroup, TranslationServer.Translate("Warp Octaves:"), 1f, 10f, 1f, 5f, (_) => SchedulePreviewUpdate(), "0", 120f);
		leftVBox.AddChild(_domainWarpGroup);

		var rightVBox = new VBoxContainer();
		rightVBox.CustomMinimumSize = new Vector2(280, 0);
		rightVBox.AddThemeConstantOverride("separation", 10);
		rightVBox.Alignment = BoxContainer.AlignmentMode.Center;
		contentHBox.AddChild(rightVBox);

		AddSectionHeader(rightVBox, TranslationServer.Translate("LIVE PREVIEW"));

		var previewPanel = new PanelContainer();
		previewPanel.CustomMinimumSize = new Vector2(256, 256);
		previewPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(false));
		previewPanel.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;

		_previewTextureRect = new TextureRect();
		_previewTextureRect.CustomMinimumSize = new Vector2(256, 256);
		_previewTextureRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_previewTextureRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		_previewTextureRect.TextureFilter = CanvasItem.TextureFilterEnum.Linear;
		previewPanel.AddChild(_previewTextureRect);
		rightVBox.AddChild(previewPanel);

		_lblPreviewInfo = new Label();
		_lblPreviewInfo.Text = "512 x 512 | FastNoiseLite";
		_lblPreviewInfo.HorizontalAlignment = HorizontalAlignment.Center;
		_lblPreviewInfo.AddThemeFontSizeOverride("font_size", 11);
		_lblPreviewInfo.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		rightVBox.AddChild(_lblPreviewInfo);

		ApplyButton.Text = "💾 " + TranslationServer.Translate("Generate & Save (.rtex)");
		ApplyButton.CustomMinimumSize = new Vector2(180, 30);

		UpdateVisibility();
		SchedulePreviewUpdate();
	}

	private void UpdateVisibility()
	{
		bool isCellular = _optNoiseType.GetItemText(_optNoiseType.Selected) == "Cellular";
		_cellularGroup.Visible = isCellular;

		bool warpEnabled = _chkDomainWarpEnabled.ButtonPressed;
		_optDomainWarpType.GetParent<Control>().Visible = warpEnabled;
		_sliderDomainWarpAmplitude.GetParent<Control>().Visible = warpEnabled;
		_sliderDomainWarpFrequency.GetParent<Control>().Visible = warpEnabled;
		_sliderDomainWarpOctaves.GetParent<Control>().Visible = warpEnabled;

		bool isColorRamp = _optColorMode.GetItemText(_optColorMode.Selected) == "ColorRamp";
		_colorRampRow.Visible = isColorRamp;
	}

	private void RandomizeAllNoiseParameters()
	{
		_optNoiseType.Selected = Random.Shared.Next(0, _optNoiseType.ItemCount);
		_txtSeed.Text = Random.Shared.Next(1, 999999).ToString();
		_sliderFrequency.Value = Math.Round(Random.Shared.NextDouble() * (0.05 - 0.003) + 0.003, 3);
		_optFractalType.Selected = Random.Shared.Next(0, _optFractalType.ItemCount);
		_sliderOctaves.Value = Random.Shared.Next(1, 9);
		_sliderLacunarity.Value = Math.Round(Random.Shared.NextDouble() * (3.5 - 1.2) + 1.2, 1);
		_sliderGain.Value = Math.Round(Random.Shared.NextDouble() * (0.9 - 0.1) + 0.1, 2);
		_sliderWeightedStrength.Value = Math.Round(Random.Shared.NextDouble() * 0.8, 2);

		_optCellularDistFunc.Selected = Random.Shared.Next(0, _optCellularDistFunc.ItemCount);
		_optCellularReturnType.Selected = Random.Shared.Next(0, _optCellularReturnType.ItemCount);
		_sliderCellularJitter.Value = Math.Round(Random.Shared.NextDouble() * 1.5, 2);

		_chkDomainWarpEnabled.ButtonPressed = Random.Shared.NextDouble() > 0.6;
		_optDomainWarpType.Selected = Random.Shared.Next(0, _optDomainWarpType.ItemCount);
		_sliderDomainWarpAmplitude.Value = Random.Shared.Next(5, 75);
		_sliderDomainWarpFrequency.Value = Math.Round(Random.Shared.NextDouble() * (0.15 - 0.005) + 0.005, 3);
		_sliderDomainWarpOctaves.Value = Random.Shared.Next(1, 8);

		_chkInvert.ButtonPressed = Random.Shared.NextDouble() > 0.7;
		_chkNormalize.ButtonPressed = Random.Shared.NextDouble() > 0.15;

		UpdateVisibility();
		SchedulePreviewUpdate();
	}

	private int GetSelectedResolution()
	{
		return _optResolution.Selected switch
		{
			0 => 128,
			1 => 256,
			2 => 512,
			3 => 1024,
			_ => 512
		};
	}

	private JsonObject BuildConfigObject()
	{
		int res = GetSelectedResolution();
		int.TryParse(_txtSeed.Text, out int seed);

		var config = new JsonObject
		{
			["generator"] = "FastNoiseLite",
			["noise_type"] = _optNoiseType.GetItemText(_optNoiseType.Selected),
			["seed"] = seed,
			["frequency"] = (float)_sliderFrequency.Value,
			["fractal_type"] = _optFractalType.GetItemText(_optFractalType.Selected),
			["fractal_octaves"] = (int)_sliderOctaves.Value,
			["fractal_lacunarity"] = (float)_sliderLacunarity.Value,
			["fractal_gain"] = (float)_sliderGain.Value,
			["fractal_weighted_strength"] = (float)_sliderWeightedStrength.Value,
			["invert"] = _chkInvert.ButtonPressed,
			["normalize"] = _chkNormalize.ButtonPressed,
			["width"] = res,
			["height"] = res
		};

		if (_optNoiseType.GetItemText(_optNoiseType.Selected) == "Cellular")
		{
			config["cellular_distance_function"] = _optCellularDistFunc.GetItemText(_optCellularDistFunc.Selected);
			config["cellular_return_type"] = _optCellularReturnType.GetItemText(_optCellularReturnType.Selected);
			config["cellular_jitter"] = (float)_sliderCellularJitter.Value;
		}

		if (_chkDomainWarpEnabled.ButtonPressed)
		{
			config["domain_warp_enabled"] = true;
			config["domain_warp_type"] = _optDomainWarpType.GetItemText(_optDomainWarpType.Selected);
			config["domain_warp_amplitude"] = (float)_sliderDomainWarpAmplitude.Value;
			config["domain_warp_frequency"] = (float)_sliderDomainWarpFrequency.Value;
			config["domain_warp_fractal_octaves"] = (int)_sliderDomainWarpOctaves.Value;
		}

		if (_optColorMode.GetItemText(_optColorMode.Selected) == "ColorRamp")
		{
			config["color_mode"] = "ColorRamp";
			config["color_a"] = $"#{_pickerColorA.Color.ToHtml(false)}";
			config["color_b"] = $"#{_pickerColorB.Color.ToHtml(false)}";
		}
		else
		{
			config["color_mode"] = "Grayscale";
		}

		return config;
	}

	private void SchedulePreviewUpdate()
	{
		if (_isUpdatingPreview) return;
		_isUpdatingPreview = true;

		Callable.From(() =>
		{
			_isUpdatingPreview = false;
			UpdatePreview();
		}).CallDeferred();
	}

	private void UpdatePreview()
	{
		try
		{
			var config = BuildConfigObject();
			var img = NoiseTextureGenerator.GenerateNoiseImage(config, 256, 256);
			if (img != null)
			{
				var tex = ImageTexture.CreateFromImage(img);
				_previewTextureRect.Texture = tex;
				int res = GetSelectedResolution();
				_lblPreviewInfo.Text = $"{res} x {res} | {config["noise_type"]} ({config["fractal_type"]})";
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[NoiseTextureDialog] Preview error: {ex.Message}");
		}
	}

	public override void OpenDialog()
	{
		base.OpenDialog();
		if (_txtName != null)
		{
			_txtName.Text = GetUniqueDefaultAssetName("procedural_noise_1");
		}
	}

	public void OpenWithCallback(Action<string> onSaved = null)
	{
		_onSavedCallback = onSaved;
		OpenDialog();
		SchedulePreviewUpdate();
	}

	private string GetUniqueDefaultAssetName(string baseName = "procedural_noise_1")
	{
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		var assetsObj = Realm.Godot.Utils.MapAssetHelper.LoadUnionedAssets(wsPath) ?? new JsonObject();
		var noiseObj = assetsObj["noise_textures"] as JsonObject;

		string cleanBase = baseName.ToLowerInvariant().Replace(" ", "_").Replace(".rtex", "");
		var match = TrailingIndexRegex().Match(cleanBase);
		string prefix = match.Success ? match.Groups[1].Value : cleanBase;
		int counter = match.Success && int.TryParse(match.Groups[2].Value, out int idx) ? idx : 1;

		string candidate = $"{prefix}_{counter}";
		string candidateFileName = $"{candidate}.rtex";
		string candidatePath = Path.Combine(wsPath, "Assets", "noise", candidateFileName);

		while (File.Exists(candidatePath) || (noiseObj != null && noiseObj.ContainsKey(candidateFileName)))
		{
			counter++;
			candidate = $"{prefix}_{counter}";
			candidateFileName = $"{candidate}.rtex";
			candidatePath = Path.Combine(wsPath, "Assets", "noise", candidateFileName);
		}

		return candidate;
	}

	public override void ApplyAndClose()
	{
		CommitPendingInputFocus();
		OnApply();
	}

	protected override void OnApply()
	{
		string rawName = _txtName.Text?.Trim();
		if (string.IsNullOrEmpty(rawName))
		{
			rawName = GetUniqueDefaultAssetName("procedural_noise_1");
		}

		string cleanBase = rawName.ToLowerInvariant().Replace(" ", "_").Replace(".rtex", "");
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);

		var assetsObj = Realm.Godot.Utils.MapAssetHelper.LoadUnionedAssets(wsPath) ?? new JsonObject();
		if (!assetsObj.ContainsKey("noise_textures") || assetsObj["noise_textures"] == null) assetsObj["noise_textures"] = new JsonObject();
		var noiseObj = assetsObj["noise_textures"].AsObject();

		string fileName = $"{cleanBase}.rtex";
		string outputRtex = Path.Combine(wsPath, "Assets", "noise", fileName);

		if (File.Exists(outputRtex) || noiseObj.ContainsKey(fileName))
		{
			string msg = string.Format(TranslationServer.Translate("An asset named '{0}' already exists.\nOverwriting will replace the existing file. Do you want to continue?"), fileName);
			Hud?.ShowConfirmationDialog(
				msg,
				() =>
				{
					ExecuteSave(fileName, outputRtex, wsPath, assetsObj, noiseObj);
				},
				confirmText: "SAVE",
				cancelText: "CANCEL"
			);
			return;
		}

		ExecuteSave(fileName, outputRtex, wsPath, assetsObj, noiseObj);
	}

	private void ExecuteSave(string fileName, string outputRtex, string wsPath, JsonObject assetsObj, JsonObject noiseObj)
	{
		try
		{
			var config = BuildConfigObject();
			string blake3Hash = NoiseTextureGenerator.GenerateAndSaveRtex(config, outputRtex);
			config["hash"] = blake3Hash;

			noiseObj[fileName] = config;

			Realm.Godot.Utils.MapAssetHelper.SaveAssetsToManifest(wsPath, assetsObj, removeFromMetadata: true);
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Generated noise texture '{0}' ({1}x{1})!"), fileName, config["width"]));

			Hud?.ReadMetadataAndRefreshTextures();
			_onSavedCallback?.Invoke(fileName);
			CloseDialog();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[NoiseTextureDialog] Failed to save noise texture: {ex.Message}");
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Failed to save noise texture: {0}"), ex.Message));
		}
	}
}
