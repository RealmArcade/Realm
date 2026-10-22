using System;
using Godot;
using Realm.Client.Services;
using Realm.Shared.Metadata;
using Realm.Shared.Services;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class TestSettingsDialog : FloatingDialogBase
{
	public TestSettingsDialog() { }

	private SpinBox _numAiPlayers;
	private CheckBox _chkRandomizeSlots;
	private SpinBox _numRandomSeed;

	public TestSettingsDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Test Settings"), new Vector2(340, 260))
	{
		SetUncompressedPanelTexture("res://Assets/UI/map_editor_usability_settings.webp", 55, 15, 60, 60);
		if (TitleLabel?.GetParent() is MarginContainer titleMargin)
		{
			titleMargin.AddThemeConstantOverride("margin_top", -42);
		}
		if (CloseButton?.GetParent() is MarginContainer closeMargin)
		{
			closeMargin.AddThemeConstantOverride("margin_top", -32);
		}
		BuildControls();
		SetFooterCloseOnly("CLOSE");
	}

	private void BuildControls()
	{
		var contentVBox = new VBoxContainer();
		contentVBox.Name = "ContentVBox_VBoxContainer";
		contentVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		contentVBox.AddThemeConstantOverride("separation", 10);
		BodyContainer.AddChild(contentVBox);

		AddSectionHeader(contentVBox, TranslationServer.Translate("TEST SETTINGS"), UIStyle.ColorGold);

		var grid = new GridContainer();
		grid.Name = "Grid_GridContainer";
		grid.Columns = 2;
		grid.AddThemeConstantOverride("h_separation", 12);
		grid.AddThemeConstantOverride("v_separation", 8);
		grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		contentVBox.AddChild(grid);

		var lblAi = new Label();
		lblAi.Name = "LblAi_Label";
		lblAi.Text = TranslationServer.Translate("# AI Players:");
		lblAi.AddThemeFontSizeOverride("font_size", 11);
		lblAi.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		grid.AddChild(lblAi);

		_numAiPlayers = new SpinBox();
		_numAiPlayers.Name = "NumAiPlayers_SpinBox";
		_numAiPlayers.MinValue = 0;
		_numAiPlayers.MaxValue = 23;
		_numAiPlayers.Step = 1;
		_numAiPlayers.Value = 1;
		_numAiPlayers.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_numAiPlayers.ValueChanged += (val) => SaveSettings();
		grid.AddChild(_numAiPlayers);

		var lblRand = new Label();
		lblRand.Name = "LblRand_Label";
		lblRand.Text = TranslationServer.Translate("Randomize Slots:");
		lblRand.AddThemeFontSizeOverride("font_size", 11);
		lblRand.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		grid.AddChild(lblRand);

		_chkRandomizeSlots = new CheckBox();
		_chkRandomizeSlots.Name = "ChkRandomizeSlots_CheckBox";
		_chkRandomizeSlots.Text = TranslationServer.Translate("Enabled");
		_chkRandomizeSlots.ButtonPressed = false;
		_chkRandomizeSlots.Toggled += (toggled) => SaveSettings();
		grid.AddChild(_chkRandomizeSlots);

		var lblSeed = new Label();
		lblSeed.Name = "LblSeed_Label";
		lblSeed.Text = TranslationServer.Translate("Random Seed:");
		lblSeed.AddThemeFontSizeOverride("font_size", 11);
		lblSeed.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		grid.AddChild(lblSeed);

		_numRandomSeed = new SpinBox();
		_numRandomSeed.Name = "NumRandomSeed_SpinBox";
		_numRandomSeed.MinValue = 0;
		_numRandomSeed.MaxValue = int.MaxValue;
		_numRandomSeed.Step = 1;
		_numRandomSeed.Value = 0;
		_numRandomSeed.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_numRandomSeed.ValueChanged += (val) => SaveSettings();
		grid.AddChild(_numRandomSeed);

		LoadSettings();
	}

	public void LoadSettings()
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		var metadata = MapFileService.LoadMetadata(wsPath);
		int playableSlots = metadata.MapProperties?.GetPlayablePlayerSlots() ?? MapInfoMetadata.DefaultPlayablePlayerSlots;
		int maxAi = Math.Max(0, playableSlots - 1);
		int aiCount = Math.Min(metadata.MapProperties?.TestAiPlayerCount ?? 1, maxAi);
		bool randomize = metadata.MapProperties?.TestRandomizeSlots ?? false;
		int seed = metadata.MapProperties?.TestRandomSeed ?? 0;

		if (_numAiPlayers != null)
		{
			_numAiPlayers.MaxValue = maxAi;
			_numAiPlayers.Value = aiCount;
		}
		if (_chkRandomizeSlots != null) _chkRandomizeSlots.ButtonPressed = randomize;
		if (_numRandomSeed != null) _numRandomSeed.Value = seed;
	}

	public void SaveSettings()
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		int aiCount = (int)(_numAiPlayers?.Value ?? 1);
		bool randomize = _chkRandomizeSlots?.ButtonPressed ?? false;
		int seedVal = (int)(_numRandomSeed?.Value ?? 0);
		int? testSeed = seedVal != 0 ? seedVal : null;

		GameContext.MetadataService.UpdateMetadata(wsPath, meta =>
		{
			meta.MapProperties.TestAiPlayerCount = aiCount;
			meta.MapProperties.TestRandomizeSlots = randomize;
			meta.MapProperties.TestRandomSeed = testSeed;
		});
	}

	public override void OpenDialog()
	{
		LoadSettings();
		base.OpenDialog();
	}
}
