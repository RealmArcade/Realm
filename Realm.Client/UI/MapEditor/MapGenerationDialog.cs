using System;
using Godot;
using Realm.Client.Services;
using Realm.Client.Services.MapGeneration;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class MapGenerationDialog : Control
{
	private const int FrameBudgetMilliseconds = 8;

	private MapEditorHUD _hud = null!;
	private readonly HSlider[] _sliders = new HSlider[8];
	private readonly Label[] _sliderValueLabels = new Label[8];

	private LineEdit _seedField = null!;
	private Button _rollButton = null!;
	private Button _randomizeSlidersButton = null!;
	private Button _applyButton = null!;
	private Button _cancelButton = null!;
	private Label _statusLabel = null!;
	private ProgressBar _progressBar = null!;
	private bool _suppressLiveUpdate;

	public MapGenerationDialog()
	{
		Name = "MapGenerationDialog";
		SetAnchorsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		ZIndex = 1150;
		Visible = false;
	}

	public MapGenerationDialog(MapEditorHUD hud) : this()
	{
		_hud = hud;
		BuildUi();
	}

	public void OpenDialog()
	{
		if (GetParent() == null)
		{
			_hud.AddChild(this);
		}

		if (Visible && GameContext.MapGenerationService.IsPreviewActive)
		{
			MoveToFront();
			return;
		}

		_suppressLiveUpdate = true;
		RollNewSeed();
		_suppressLiveUpdate = false;

		Visible = true;
		MoveToFront();

		if (!GameContext.MapGenerationService.StartPreview(_seedField.Text, ReadParameters()))
		{
			_statusLabel.Visible = true;
			_progressBar.Visible = false;
			_statusLabel.Text = GameContext.MapGenerationService.Progress.FailureMessage
				?? TranslationServer.Translate("Map generation failed.");
			return;
		}

		_statusLabel.Visible = true;
		_progressBar.Visible = true;
		_progressBar.Value = 0;
		UpdateLiveUi(true);
	}

	private void CloseDialog()
	{
		Visible = false;
	}

	private void BuildUi()
	{
		var scrim = new ColorRect { Name = "GenerationScrim", Color = new Color(0f, 0f, 0f, 0.65f), MouseFilter = MouseFilterEnum.Stop };
		scrim.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(scrim);

		var center = new CenterContainer { Name = "GenerationCenterContainer" };
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(center);

		var panel = new PanelContainer { Name = "GenerationPanel", CustomMinimumSize = new Vector2(510f, 490f) };
		UIStyle.ClearPanelBackground(panel);
		center.AddChild(panel);

		var background = new TextureRect
		{
			Name = "GenerationBackgroundTexture",
			Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/map_editor_test.webp"),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale
		};
		background.SetAnchorsPreset(LayoutPreset.FullRect);
		panel.AddChild(background);

		var margin = new MarginContainer { Name = "GenerationMargin" };
		margin.SetAnchorsPreset(LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_top", 30);
		margin.AddThemeConstantOverride("margin_bottom", 24);
		margin.AddThemeConstantOverride("margin_left", 44);
		margin.AddThemeConstantOverride("margin_right", 44);
		panel.AddChild(margin);

		var content = new VBoxContainer { Name = "GenerationContent" };
		content.AddThemeConstantOverride("separation", 4);
		margin.AddChild(content);

		var title = new Label { Name = "GenerationTitle", HorizontalAlignment = HorizontalAlignment.Center };
		UIStyle.ApplyTitle(title, TranslationServer.Translate("Random Map Generator"), 18);
		title.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		content.AddChild(title);

		content.AddChild(BuildSeedRow());

		var sliderLabels = new[]
		{
			TranslationServer.Translate("Hills Density"),
			TranslationServer.Translate("Terrain Roughness"),
			TranslationServer.Translate("Mountain Height"),
			TranslationServer.Translate("Choke Point Width"),
			TranslationServer.Translate("Water Level"),
			TranslationServer.Translate("Tree Clump Density"),
			TranslationServer.Translate("Resource Abundance"),
			TranslationServer.Translate("Decorative Prop Density")
		};

		var sliderTooltips = new[]
		{
			TranslationServer.Translate("Controls how much of the map starts as land."),
			TranslationServer.Translate("Controls coastline noise detail and terrain jaggedness."),
			TranslationServer.Translate("Controls how many height tiers mountains can rise."),
			TranslationServer.Translate("Controls the width of carved paths and choke points."),
			TranslationServer.Translate("Controls how much open ground becomes water."),
			TranslationServer.Translate("Controls how densely trees are scattered across the map."),
			TranslationServer.Translate("Controls vegetation and resource density; higher values scatter more tree resources."),
			TranslationServer.Translate("Controls how many decorative props are scattered away from paths.")
		};

		for (int sliderIndex = 0; sliderIndex < _sliders.Length; sliderIndex++)
		{
			content.AddChild(BuildSliderRow(sliderIndex, sliderLabels[sliderIndex], sliderTooltips[sliderIndex]));
		}

		_randomizeSlidersButton = new Button { Name = "RandomizeSlidersButton", Text = $"{UnicodeIcons.DICE} " + TranslationServer.Translate("Randomize Sliders") };
		_randomizeSlidersButton.Set("icon_max_width", 0);
		_randomizeSlidersButton.CustomMinimumSize = new Vector2(0f, 26f);
		_randomizeSlidersButton.FocusMode = FocusModeEnum.None;
		_randomizeSlidersButton.TooltipText = TranslationServer.Translate("Sets every slider to a random value from one to ten.");
		StyleActionButton(_randomizeSlidersButton);
		_randomizeSlidersButton.Pressed += RandomizeSliders;
		content.AddChild(_randomizeSlidersButton);

		_statusLabel = new Label
		{
			Name = "GenerationStatusLabel",
			Visible = false,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(412f, 18f),
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		_statusLabel.AddThemeFontSizeOverride("font_size", 11);
		_statusLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		content.AddChild(_statusLabel);

		_progressBar = new ProgressBar { Name = "GenerationProgressBar", Visible = false, ShowPercentage = false, MinValue = 0, MaxValue = 100, CustomMinimumSize = new Vector2(0f, 10f) };
		content.AddChild(_progressBar);

		var spacer = new Control { Name = "GenerationSpacer", SizeFlagsVertical = SizeFlags.ExpandFill };
		content.AddChild(spacer);

		var actions = new HBoxContainer { Name = "GenerationActions", Alignment = BoxContainer.AlignmentMode.Center };
		actions.AddThemeConstantOverride("separation", 18);
		content.AddChild(actions);

		_applyButton = new Button { Name = "ApplyButton", Text = TranslationServer.Translate("Apply"), CustomMinimumSize = new Vector2(120f, 32f), FocusMode = FocusModeEnum.None };
		_applyButton.Set("icon_max_width", 0);
		_applyButton.TooltipText = TranslationServer.Translate("Keep the live preview as one undo step.");
		StyleActionButton(_applyButton);
		_applyButton.Pressed += OnApplyPressed;
		actions.AddChild(_applyButton);

		_cancelButton = new Button { Name = "CancelButton", Text = TranslationServer.Translate("Cancel"), CustomMinimumSize = new Vector2(120f, 32f), FocusMode = FocusModeEnum.None };
		_cancelButton.Set("icon_max_width", 0);
		_cancelButton.TooltipText = TranslationServer.Translate("Put the map back the way it was when the dialog opened.");
		StyleActionButton(_cancelButton);
		_cancelButton.Pressed += OnCancelPressed;
		actions.AddChild(_cancelButton);
	}

	private HBoxContainer BuildSeedRow()
	{
		var row = new HBoxContainer { Name = "SeedRow" };
		row.AddThemeConstantOverride("separation", 6);

		var label = new Label { Name = "SeedLabel", Text = TranslationServer.Translate("Seed"), CustomMinimumSize = new Vector2(110f, 0f) };
		label.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(label);

		_seedField = new LineEdit { Name = "SeedField", SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_seedField.AddThemeFontSizeOverride("font_size", 11);
		_seedField.TooltipText = TranslationServer.Translate("Any text is accepted; pressing Enter regenerates the whole map from that seed.");
		_seedField.TextSubmitted += _ => RebuildFromSeed();
		_seedField.FocusExited += RebuildFromSeed;
		row.AddChild(_seedField);

		_rollButton = new Button { Name = "RollSeedButton", Text = $"{UnicodeIcons.DICE} " + TranslationServer.Translate("Roll"), CustomMinimumSize = new Vector2(74f, 26f), FocusMode = FocusModeEnum.None };
		_rollButton.Set("icon_max_width", 0);
		_rollButton.TooltipText = TranslationServer.Translate("Rolls a fresh six digit seed and regenerates the map.");
		StyleActionButton(_rollButton);
		_rollButton.Pressed += OnRollPressed;
		row.AddChild(_rollButton);

		return row;
	}

	private HBoxContainer BuildSliderRow(int sliderIndex, string labelText, string tooltipText)
	{
		var row = new HBoxContainer { Name = $"SliderRow{sliderIndex}" };
		row.AddThemeConstantOverride("separation", 6);

		var label = new Label { Name = "SliderLabel", Text = labelText, CustomMinimumSize = new Vector2(150f, 0f) };
		label.AddThemeFontSizeOverride("font_size", 11);
		label.TooltipText = tooltipText;
		row.AddChild(label);

		var slider = new HSlider
		{
			Name = "Slider",
			MinValue = MapGenerationParameters.MinimumSliderValue,
			MaxValue = MapGenerationParameters.MaximumSliderValue,
			Step = 1,
			Value = MapGenerationParameters.DefaultSliderValue,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0f, 18f)
		};
		slider.TooltipText = tooltipText;
		row.AddChild(slider);

		var valueLabel = new Label { Name = "SliderValue", Text = MapGenerationParameters.DefaultSliderValue.ToString(), CustomMinimumSize = new Vector2(24f, 0f), HorizontalAlignment = HorizontalAlignment.Right };
		valueLabel.AddThemeFontSizeOverride("font_size", 11);
		valueLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		row.AddChild(valueLabel);

		slider.ValueChanged += value =>
		{
			valueLabel.Text = ((int)value).ToString();
			ApplyLiveParameters();
		};

		_sliders[sliderIndex] = slider;
		_sliderValueLabels[sliderIndex] = valueLabel;
		return row;
	}

	private void StyleActionButton(Button button)
	{
		var fontAwesomeFont = _hud?.GetFontAwesomeFont();
		if (fontAwesomeFont != null)
		{
			button.AddThemeFontOverride("font", fontAwesomeFont);
		}

		button.AddThemeFontSizeOverride("font_size", 12);
		UIStyle.ApplyButtonStyleBoxes(button);
		button.AddThemeColorOverride("font_color", UIStyle.ColorGold);
	}

	private void RollNewSeed()
	{
		_seedField.Text = (100000 + (int)(GD.Randi() % 900000)).ToString();
	}

	private void OnRollPressed()
	{
		RollNewSeed();
		RebuildFromSeed();
	}

	private void RandomizeSliders()
	{
		_suppressLiveUpdate = true;
		for (int sliderIndex = 0; sliderIndex < _sliders.Length; sliderIndex++)
		{
			int value = MapGenerationParameters.MinimumSliderValue + (int)(GD.Randi() % MapGenerationParameters.MaximumSliderValue);
			_sliders[sliderIndex].Value = value;
			_sliderValueLabels[sliderIndex].Text = value.ToString();
		}

		_suppressLiveUpdate = false;
		ApplyLiveParameters();
	}

	private MapGenerationParameters ReadParameters()
	{
		return new MapGenerationParameters(
			(int)_sliders[0].Value,
			(int)_sliders[1].Value,
			(int)_sliders[2].Value,
			(int)_sliders[3].Value,
			(int)_sliders[4].Value,
			(int)_sliders[5].Value,
			(int)_sliders[6].Value,
			(int)_sliders[7].Value);
	}

	private void ApplyLiveParameters()
	{
		if (_suppressLiveUpdate || !Visible || !GameContext.MapGenerationService.IsPreviewActive)
		{
			return;
		}

		GameContext.MapGenerationService.UpdateParameters(ReadParameters());
		UpdateLiveUi(true);
	}

	private void RebuildFromSeed()
	{
		if (_suppressLiveUpdate || !Visible || !GameContext.MapGenerationService.IsPreviewActive)
		{
			return;
		}

		GameContext.MapGenerationService.UpdateParameters(ReadParameters());
		GameContext.MapGenerationService.UpdateSeed(_seedField.Text);
		UpdateLiveUi(true);
	}

	private void OnApplyPressed()
	{
		if (GameContext.MapGenerationService.IsRunning)
		{
			return;
		}

		GameContext.MapGenerationService.ApplyPreview();
		CloseDialog();
	}

	private void OnCancelPressed()
	{
		GameContext.MapGenerationService.CancelPreview();
		CloseDialog();
	}

	private void UpdateLiveUi(bool running)
	{
		_applyButton.Disabled = running;
		_progressBar.Visible = running;
		_statusLabel.Visible = true;
		_statusLabel.Text = running
			? string.Format(TranslationServer.Translate("{0}… {1:F0}%"), TranslateStage(GameContext.MapGenerationService.Progress.Stage), GameContext.MapGenerationService.Progress.Percent)
			: TranslationServer.Translate("Live preview. Adjust the seed or sliders, then Apply or Cancel.");
	}

	public override void _Process(double delta)
	{
		if (!Visible || !GameContext.MapGenerationService.IsPreviewActive)
		{
			return;
		}

		MapGenerationProgress progress = GameContext.MapGenerationService.Advance(FrameBudgetMilliseconds);
		if (progress.HasFailed)
		{
			_progressBar.Visible = false;
			_applyButton.Disabled = true;
			_statusLabel.Text = progress.FailureMessage ?? TranslationServer.Translate("Map generation failed.");
			return;
		}

		if (progress.IsRunning)
		{
			if (!_progressBar.Visible || _applyButton.Disabled == false)
			{
				UpdateLiveUi(true);
			}

			_progressBar.Value = progress.Percent;
			_statusLabel.Text = string.Format(TranslationServer.Translate("{0}… {1:F0}%"), TranslateStage(progress.Stage), progress.Percent);
			return;
		}

		if (_progressBar.Visible || _applyButton.Disabled)
		{
			UpdateLiveUi(false);
		}
	}

	private static string TranslateStage(MapGenerationStage stage)
	{
		return stage switch
		{
			MapGenerationStage.Preparing => TranslationServer.Translate("Preparing"),
			MapGenerationStage.ShapingLandMasses => TranslationServer.Translate("Shaping land masses"),
			MapGenerationStage.ClearingStartAreas => TranslationServer.Translate("Clearing start areas"),
			MapGenerationStage.CarvingPaths => TranslationServer.Translate("Carving paths"),
			MapGenerationStage.MeasuringDensity => TranslationServer.Translate("Measuring density"),
			MapGenerationStage.RaisingMountainsAndWater => TranslationServer.Translate("Raising mountains and water"),
			MapGenerationStage.SmoothingTiers => TranslationServer.Translate("Smoothing tiers"),
			MapGenerationStage.BuildingHeights => TranslationServer.Translate("Building heights"),
			MapGenerationStage.PaintingMaterials => TranslationServer.Translate("Painting materials"),
			MapGenerationStage.UploadingTerrain => TranslationServer.Translate("Uploading terrain"),
			MapGenerationStage.FillingWater => TranslationServer.Translate("Filling water basins"),
			MapGenerationStage.ApplyingPathing => TranslationServer.Translate("Applying pathing"),
			MapGenerationStage.MeasuringDistanceToPaths => TranslationServer.Translate("Measuring distance to paths"),
			MapGenerationStage.ScatteringTrees => TranslationServer.Translate("Scattering trees"),
			MapGenerationStage.PlantingTrees => TranslationServer.Translate("Planting trees"),
			MapGenerationStage.ScatteringDecorations => TranslationServer.Translate("Scattering decorations"),
			MapGenerationStage.PlacingDecorations => TranslationServer.Translate("Placing decorations"),
			MapGenerationStage.Finishing => TranslationServer.Translate("Finishing"),
			_ => TranslationServer.Translate("Preparing")
		};
	}
}
