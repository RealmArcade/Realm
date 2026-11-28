using System;
using Godot;
using Realm.Client.Services;
using Realm.Client.Services.MapGeneration;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class MapGenerationDialog : FloatingDialogBase
{
	private const int FrameBudgetMilliseconds = 8;

	private readonly HSlider[] _sliders = new HSlider[8];

	private LineEdit _seedField = null!;
	private Label _statusLabel = null!;
	private ProgressBar _progressBar = null!;
	private bool _suppressLiveUpdate;

	public MapGenerationDialog() : base()
	{
	}

	public MapGenerationDialog(MapEditorHUD hud) : base(hud, TranslationServer.Translate("Random Map Generator"), new Vector2(510f, 500f))
	{
		BuildUi();
	}

	public override void OpenDialog()
	{
		if (IsOpen && GameContext.MapGenerationService.IsPreviewActive)
		{
			BringToFront();
			return;
		}

		_suppressLiveUpdate = true;
		RollNewSeed();
		_suppressLiveUpdate = false;

		base.OpenDialog();

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

	protected override void OnCancel()
	{
		GameContext.MapGenerationService.CancelPreview();
	}

	protected override void OnApply()
	{
		GameContext.MapGenerationService.ApplyPreview();
	}

	private void BuildUi()
	{
		SetUncompressedPanelTexture("res://Assets/UI/map_editor_test.webp", 30, 24, 44, 44);

		CancelButton.TooltipText = TranslationServer.Translate("Put the map back the way it was when the dialog opened.");
		ApplyButton.TooltipText = TranslationServer.Translate("Keep the live preview as one undo step.");

		BodyContainer.AddChild(BuildSeedRow());

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
			(HSlider slider, _) = AddSlider(
				BodyContainer,
				sliderLabels[sliderIndex],
				MapGenerationParameters.MinimumSliderValue,
				MapGenerationParameters.MaximumSliderValue,
				1f,
				MapGenerationParameters.DefaultSliderValue,
				_ => ApplyLiveParameters(),
				"0",
				150f);
			slider.TooltipText = sliderTooltips[sliderIndex];
			_sliders[sliderIndex] = slider;
		}

		CreateActionButton(
			BodyContainer,
			$"{UnicodeIcons.DICE} " + TranslationServer.Translate("Randomize Sliders"),
			RandomizeSliders,
			TranslationServer.Translate("Sets every slider to a random value from one to ten."),
			new Vector2(0f, 26f));

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
		BodyContainer.AddChild(_statusLabel);

		_progressBar = new ProgressBar { Name = "GenerationProgressBar", Visible = false, ShowPercentage = false, MinValue = 0, MaxValue = 100, CustomMinimumSize = new Vector2(0f, 10f) };
		BodyContainer.AddChild(_progressBar);

		var spacer = new Control { Name = "GenerationSpacer", SizeFlagsVertical = SizeFlags.ExpandFill };
		BodyContainer.AddChild(spacer);
	}

	private HBoxContainer BuildSeedRow()
	{
		var row = new HBoxContainer { Name = "SeedRow" };
		row.AddThemeConstantOverride("separation", 6);

		var label = new Label { Name = "SeedLabel", Text = TranslationServer.Translate("Seed"), CustomMinimumSize = new Vector2(150f, 0f) };
		label.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(label);

		_seedField = new LineEdit { Name = "SeedField", SizeFlagsHorizontal = SizeFlags.ExpandFill };
		UIStyle.ApplyTextInput(_seedField, 11);
		_seedField.TooltipText = TranslationServer.Translate("Any text is accepted; pressing Enter regenerates the whole map from that seed.");
		_seedField.TextSubmitted += _ => RebuildFromSeed();
		_seedField.FocusExited += RebuildFromSeed;
		row.AddChild(_seedField);

		CreateActionButton(
			row,
			$"{UnicodeIcons.DICE} " + TranslationServer.Translate("Roll"),
			OnRollPressed,
			TranslationServer.Translate("Rolls a fresh six digit seed and regenerates the map."),
			new Vector2(74f, 26f));

		return row;
	}

	private Button CreateActionButton(Control parent, string text, Action onPressed, string tooltipText, Vector2 minSize)
	{
		Button button = CreateDialogButton();
		button.Text = text;
		button.TooltipText = tooltipText;
		button.CustomMinimumSize = minSize;
		button.FocusMode = FocusModeEnum.None;

		var fontAwesomeFont = Hud?.GetFontAwesomeFont();
		if (fontAwesomeFont != null)
		{
			button.AddThemeFontOverride("font", fontAwesomeFont);
		}

		button.AddThemeFontSizeOverride("font_size", 11);
		UIStyle.ApplyButtonStyleBoxes(button);
		button.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		button.Pressed += onPressed;
		parent.AddChild(button);
		return button;
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
			_sliders[sliderIndex].Value = MapGenerationParameters.MinimumSliderValue + (int)(GD.Randi() % MapGenerationParameters.MaximumSliderValue);
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
		if (_suppressLiveUpdate || !IsOpen || !GameContext.MapGenerationService.IsPreviewActive)
		{
			return;
		}

		GameContext.MapGenerationService.UpdateParameters(ReadParameters());
		UpdateLiveUi(true);
	}

	private void RebuildFromSeed()
	{
		if (_suppressLiveUpdate || !IsOpen || !GameContext.MapGenerationService.IsPreviewActive)
		{
			return;
		}

		GameContext.MapGenerationService.UpdateParameters(ReadParameters());
		GameContext.MapGenerationService.UpdateSeed(_seedField.Text);
		UpdateLiveUi(true);
	}

	private void UpdateLiveUi(bool running)
	{
		ApplyButton.Disabled = running;
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
			ApplyButton.Disabled = true;
			_statusLabel.Text = progress.FailureMessage ?? TranslationServer.Translate("Map generation failed.");
			return;
		}

		if (progress.IsRunning)
		{
			if (!_progressBar.Visible || ApplyButton.Disabled == false)
			{
				UpdateLiveUi(true);
			}

			_progressBar.Value = progress.Percent;
			_statusLabel.Text = string.Format(TranslationServer.Translate("{0}… {1:F0}%"), TranslateStage(progress.Stage), progress.Percent);
			return;
		}

		if (_progressBar.Visible || ApplyButton.Disabled)
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
