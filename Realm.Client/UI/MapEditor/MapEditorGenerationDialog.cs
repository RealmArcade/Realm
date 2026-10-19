using System;
using Godot;
using Realm.Client.Core;

namespace Realm.Client.UI.MapEditor;

public class MapEditorGenerationDialog
{
	private readonly MapEditorHUD _hud;

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
		bgTex.Texture = UIStyle.LoadTextureWithFallback("res://Assets/UI/map_editor_random_generator.png");
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
		SetupButton(btnRoll, TranslationServer.Translate("ROLL"), () =>
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
		SetupButton(btnRandomize, TranslationServer.Translate("RANDOMIZE SLIDERS"), () =>
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

		var btnHBox = new HBoxContainer();
		btnHBox.Name = "BtnHBox_HBoxContainer";
		btnHBox.AddThemeConstantOverride("separation", 20);
		btnHBox.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		vbox.AddChild(btnHBox);

		var btnGen = new Button();
		btnGen.Name = "BtnGen_Button";
		btnGen.Set("icon_max_width", 0);
		SetupButton(btnGen, TranslationServer.Translate("GENERATE"), () =>
		{
			overlay.QueueFree();
			if (GameHost.Instance != null)
			{
				GameHost.Instance.ClearMapEntirely();
				MapGenerator.GenerateMap(
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
				_hud.RegenerateMinimap();
			}
		}, 13, "Generate the random map");
		btnGen.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		btnHBox.AddChild(btnGen);

		var btnCancel = new Button();
		btnCancel.Name = "BtnCancel_Button";
		btnCancel.Set("icon_max_width", 0);
		SetupButton(btnCancel, TranslationServer.Translate("CLOSE"), () => { overlay.QueueFree(); }, 13, "Close dialog without generating");
		btnCancel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
		btnHBox.AddChild(btnCancel);
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

	private void SetupButton(Button btn, string text, Action onClick, int fontSize = 13, string tooltip = "")
	{
		btn.Text = TranslationServer.Translate(text);
		btn.CustomMinimumSize = new Vector2(0, 32);
		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		btn.AddThemeFontSizeOverride("font_size", fontSize);
		btn.FocusMode = Control.FocusModeEnum.None;
		if (!string.IsNullOrEmpty(tooltip))
		{
			btn.TooltipText = TranslationServer.Translate(tooltip);
		}

		btn.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			onClick?.Invoke();
		};
	}
}