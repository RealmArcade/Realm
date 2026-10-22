using System;
using Godot;
using Realm.Client.Core;

namespace Realm.Client.UI.MapEditor;

public class MapEditorGenerationDialog
{
	private readonly MapEditorHUD _hud;

	private ColorRect _overlay;
	private Button _btnGen;
	private Button _btnCancel;
	private Label _statusLabel;
	private ProgressBar _progressBar;
	private Timer _progressTimer;
	private MapGenerator.MapGenerationJob _activeJob;
	private bool _generationRunning;

	private int _genHillsDensity = 5;
	private int _genTerrainRoughness = 5;
	private int _genMountainHeight = 5;
	private int _genChokeWidth = 5;
	private int _genWaterLevel = 5;
	private int _genTreeDensity = 5;
	private int _genResourceAbundance = 5;
	private int _genDecoDensity = 5;
	private string _genSeed = "";

	public MapEditorGenerationDialog(MapEditorHUD hud)
	{
		_hud = hud;
		_genSeed = new Random().Next(100000, 999999).ToString();
	}

	public void Show()
	{
		var overlay = new ColorRect();
		overlay.Name = "GenerationOverlay";
		overlay.Color = new Color(0, 0, 0, 0.65f);
		overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		overlay.MouseFilter = Control.MouseFilterEnum.Stop;
		overlay.ZIndex = 1000;
		_hud.AddChild(overlay);
		_overlay = overlay;

		var panel = new PanelContainer();
		panel.Name = "Panel_PanelContainer";
		panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		panel.CustomMinimumSize = new Vector2(510, 490);
		panel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		panel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

		var center = new CenterContainer();
		center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		overlay.AddChild(center);
		center.AddChild(panel);

		var bgTex = new TextureRect();
		bgTex.Name = "BgTex_TextureRect";
		bgTex.Texture = UIStyle.LoadTextureWithFallback("res://Assets/UI/map_editor_random_generator.webp");
		bgTex.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		bgTex.StretchMode = TextureRect.StretchModeEnum.Scale;
		bgTex.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		panel.AddChild(bgTex);

		var margin = new MarginContainer();
		margin.Name = "Margin_MarginContainer";
		margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_top", 55);
		margin.AddThemeConstantOverride("margin_bottom", 25);
		margin.AddThemeConstantOverride("margin_left", 70);
		margin.AddThemeConstantOverride("margin_right", 70);
		panel.AddChild(margin);

		var vbox = new VBoxContainer();
		vbox.Name = "Vbox_VBoxContainer";
		vbox.AddThemeConstantOverride("separation", 10);
		margin.AddChild(vbox);

		var lblTitle = new Label();
		lblTitle.Name = "LblTitle_Label";
		UIStyle.ApplyTitle(lblTitle, TranslationServer.Translate("RANDOM MAP GENERATOR"), 18);
		lblTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		vbox.AddChild(lblTitle);

		var titleSpacer = new Control();
		titleSpacer.Name = "TitleSpacer_Control";
		titleSpacer.CustomMinimumSize = new Vector2(0, 18);
		vbox.AddChild(titleSpacer);

		var seedHBox = new HBoxContainer();
		seedHBox.Name = "SeedHBox_HBoxContainer";
		seedHBox.AddThemeConstantOverride("separation", 10);
		vbox.AddChild(seedHBox);

		var lblSeed = new Label();
		lblSeed.Name = "LblSeed_Label";
		lblSeed.Text = TranslationServer.Translate("Map Seed:");
		lblSeed.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		lblSeed.AddThemeFontSizeOverride("font_size", 12);
		seedHBox.AddChild(lblSeed);

		var txtSeed = new LineEdit();
		txtSeed.Name = "TxtSeed_LineEdit";
		txtSeed.Text = _genSeed;
		txtSeed.CustomMinimumSize = new Vector2(150, 30);
		txtSeed.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		txtSeed.AddThemeStyleboxOverride("normal", UIStyle.CreateTextInput(false));
		txtSeed.AddThemeStyleboxOverride("focus", UIStyle.CreateTextInput(true));
		txtSeed.AddThemeFontSizeOverride("font_size", 12);
		txtSeed.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		txtSeed.TextChanged += (newText) => { _genSeed = newText; };
		seedHBox.AddChild(txtSeed);

		var btnRoll = new Button();
		btnRoll.Name = "BtnRoll_Button";
		btnRoll.Set("icon_max_width", 0);
		UIStyle.SetupButton(btnRoll, TranslationServer.Translate("ROLL"), () =>
		{
			_genSeed = new Random().Next(100000, 999999).ToString();
			txtSeed.Text = _genSeed;
		}, 11, "Generate a new random seed");
		btnRoll.CustomMinimumSize = new Vector2(70, 30);
		seedHBox.AddChild(btnRoll);

		var grid = new GridContainer();
		grid.Name = "Grid_GridContainer";
		grid.Columns = 3;
		grid.AddThemeConstantOverride("h_separation", 10);
		grid.AddThemeConstantOverride("v_separation", 8);
		vbox.AddChild(grid);

		var sldHillsDensity = AddSliderRow(grid, "Hills Density", _genHillsDensity, (val) => _genHillsDensity = val);
		var sldTerrainRoughness = AddSliderRow(grid, "Terrain Roughness", _genTerrainRoughness, (val) => _genTerrainRoughness = val);
		var sldMountainHeight = AddSliderRow(grid, "Mountain Height", _genMountainHeight, (val) => _genMountainHeight = val);
		var sldChokeWidth = AddSliderRow(grid, "Choke Point Width", _genChokeWidth, (val) => _genChokeWidth = val);
		var sldWaterLevel = AddSliderRow(grid, "Water Level", _genWaterLevel, (val) => _genWaterLevel = val);
		var sldTreeDensity = AddSliderRow(grid, "Tree Clump Density", _genTreeDensity, (val) => _genTreeDensity = val);
		var sldResourceAbundance = AddSliderRow(grid, "Resource Abundance", _genResourceAbundance, (val) => _genResourceAbundance = val);
		var sldDecoDensity = AddSliderRow(grid, "Decorative Prop Density", _genDecoDensity, (val) => _genDecoDensity = val);
		var btnRandomize = new Button();
		btnRandomize.Name = "BtnRandomize_Button";
		btnRandomize.Set("icon_max_width", 0);
		UIStyle.SetupButton(btnRandomize, TranslationServer.Translate("RANDOMIZE SLIDERS"), () =>
		{
			var rnd = new Random();
			sldHillsDensity.Value = rnd.Next(1, 11);
			sldTerrainRoughness.Value = rnd.Next(1, 11);
			sldMountainHeight.Value = rnd.Next(1, 11);
			sldChokeWidth.Value = rnd.Next(1, 11);
			sldWaterLevel.Value = rnd.Next(1, 11);
			sldTreeDensity.Value = rnd.Next(1, 11);
			sldResourceAbundance.Value = rnd.Next(1, 11);
			sldDecoDensity.Value = rnd.Next(1, 11);
		}, 11, "Randomize all slider values");
		vbox.AddChild(btnRandomize);

		_statusLabel = new Label();
		_statusLabel.Name = "GenerationStatusLabel";
		_statusLabel.Visible = false;
		_statusLabel.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_statusLabel.AddThemeFontSizeOverride("font_size", 11);
		_statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_statusLabel.CustomMinimumSize = new Vector2(300, 0);
		_statusLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		vbox.AddChild(_statusLabel);

		_progressBar = new ProgressBar();
		_progressBar.Name = "GenerationProgressBar";
		_progressBar.Visible = false;
		_progressBar.MinValue = 0;
		_progressBar.MaxValue = 100;
		_progressBar.Value = 0;
		_progressBar.CustomMinimumSize = new Vector2(0, 18);
		vbox.AddChild(_progressBar);

		var btnHBox = new HBoxContainer();
		btnHBox.Name = "BtnHBox_HBoxContainer";
		btnHBox.AddThemeConstantOverride("separation", 20);
		btnHBox.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		vbox.AddChild(btnHBox);

		_btnGen = new Button();
		_btnGen.Name = "BtnGen_Button";
		_btnGen.Set("icon_max_width", 0);
		UIStyle.SetupButton(_btnGen, TranslationServer.Translate("GENERATE"), StartGeneration, 13, "Generate the random map");
		_btnGen.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		btnHBox.AddChild(_btnGen);

		_btnCancel = new Button();
		_btnCancel.Name = "BtnCancel_Button";
		_btnCancel.Set("icon_max_width", 0);
		UIStyle.SetupButton(_btnCancel, TranslationServer.Translate("CLOSE"), HandleCancelPressed, 13, "Close dialog without generating");
		_btnCancel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
		btnHBox.AddChild(_btnCancel);

		_progressTimer = new Timer();
		_progressTimer.Name = "MapGenerationProgressTimer";
		_progressTimer.WaitTime = 0.1;
		_progressTimer.OneShot = false;
		_progressTimer.Autostart = false;
		_progressTimer.Timeout += UpdateGenerationProgress;
		_hud.AddChild(_progressTimer);
	}

	private void StartGeneration()
	{
		if (_generationRunning || GameHost.Instance == null) return;

		_generationRunning = true;
		_btnGen.Disabled = true;
		_btnCancel.Text = TranslationServer.Translate("CANCEL");
		_statusLabel.Visible = true;
		_progressBar.Visible = true;
		_progressBar.Value = 0;
		_statusLabel.Text = TranslationServer.Translate("Preparing generation...");

		_activeJob = MapGenerator.StartGeneration(
			GameHost.Instance,
			_genHillsDensity,
			_genTerrainRoughness,
			_genMountainHeight,
			_genChokeWidth,
			_genWaterLevel,
			_genTreeDensity,
			_genResourceAbundance,
			_genDecoDensity,
			_genSeed
		);

		if (_activeJob == null)
		{
			FinishGeneration();
			return;
		}

		_progressTimer.Start();
	}

	private void HandleCancelPressed()
	{
		if (_generationRunning)
		{
			MapGenerator.CancelActiveGeneration();
			FinishGeneration();
			return;
		}

		CloseOverlay();
	}

	private void UpdateGenerationProgress()
	{
		if (!_generationRunning) return;

		if (_activeJob != null && !_activeJob.IsCancelled)
		{
			_progressBar.Value = _activeJob.Progress * 100.0;
			if (!string.IsNullOrEmpty(_activeJob.StageDescription))
			{
				_statusLabel.Text = _activeJob.StageDescription;
			}
		}

		if (_activeJob == null || _activeJob.IsComplete || _activeJob.IsCancelled)
		{
			FinishGeneration();
		}
	}

	private void FinishGeneration()
	{
		_generationRunning = false;
		_activeJob = null;
		_progressTimer?.Stop();

		_hud.RegenerateMinimap();
		CloseOverlay();
	}

	private void CloseOverlay()
	{
		if (_progressTimer != null && GodotObject.IsInstanceValid(_progressTimer))
		{
			_progressTimer.Stop();
			_progressTimer.QueueFree();
			_progressTimer = null;
		}

		_overlay?.QueueFree();
		_overlay = null;
	}

	private HSlider AddSliderRow(GridContainer grid, string title, int initialValue, Action<int> onValueChanged)
	{
		var lblName = new Label();
		lblName.Name = "LblName_Label";
		lblName.Text = TranslationServer.Translate(title);
		lblName.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		lblName.AddThemeFontSizeOverride("font_size", 12);
		grid.AddChild(lblName);

		var sld = new HSlider();
		sld.Name = "Sld_HSlider";
		sld.MinValue = 1;
		sld.MaxValue = 10;
		sld.Step = 1;
		sld.Value = initialValue;
		sld.CustomMinimumSize = new Vector2(130, 0);
		sld.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		grid.AddChild(sld);

		var lblVal = new Label();
		lblVal.Name = "LblVal_Label";
		lblVal.Text = initialValue.ToString();
		lblVal.CustomMinimumSize = new Vector2(30, 0);
		lblVal.HorizontalAlignment = HorizontalAlignment.Right;
		lblVal.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		lblVal.AddThemeFontSizeOverride("font_size", 12);
		grid.AddChild(lblVal);

		sld.ValueChanged += (double value) =>
		{
			int val = (int)value;
			lblVal.Text = val.ToString();
			onValueChanged(val);
		};

		return sld;
	}
}