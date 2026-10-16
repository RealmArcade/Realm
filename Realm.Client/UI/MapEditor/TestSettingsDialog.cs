using System;
using Godot;
using Realm.Client.Services;
using Realm.Shared.Services;

namespace Realm.Client.UI.MapEditor;

public partial class TestSettingsDialog : FloatingDialogBase
{
	private SpinBox _numAiPlayers;
	private CheckBox _chkRandomizeSlots;

	public TestSettingsDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Test Settings"), new Vector2(340, 220))
	{
		SetUncompressedPanelTexture("res://Assets/UI/map_editor_usability_settings.png", 55, 15, 60, 60);
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
		contentVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		contentVBox.AddThemeConstantOverride("separation", 10);
		BodyContainer.AddChild(contentVBox);

		AddSectionHeader(contentVBox, TranslationServer.Translate("TEST SETTINGS"), UIStyle.ColorGold);

		var grid = new GridContainer();
		grid.Columns = 2;
		grid.AddThemeConstantOverride("h_separation", 12);
		grid.AddThemeConstantOverride("v_separation", 8);
		grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		contentVBox.AddChild(grid);

		var lblAi = new Label();
		lblAi.Text = TranslationServer.Translate("# AI Players:");
		lblAi.AddThemeFontSizeOverride("font_size", 11);
		lblAi.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		grid.AddChild(lblAi);

		_numAiPlayers = new SpinBox();
		_numAiPlayers.MinValue = 0;
		_numAiPlayers.MaxValue = 23;
		_numAiPlayers.Step = 1;
		_numAiPlayers.Value = 1;
		_numAiPlayers.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_numAiPlayers.ValueChanged += (val) => SaveSettings();
		grid.AddChild(_numAiPlayers);

		var lblRand = new Label();
		lblRand.Text = TranslationServer.Translate("Randomize Slots:");
		lblRand.AddThemeFontSizeOverride("font_size", 11);
		lblRand.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		grid.AddChild(lblRand);

		_chkRandomizeSlots = new CheckBox();
		_chkRandomizeSlots.Text = TranslationServer.Translate("Enabled");
		_chkRandomizeSlots.ButtonPressed = false;
		_chkRandomizeSlots.Toggled += (toggled) => SaveSettings();
		grid.AddChild(_chkRandomizeSlots);

		LoadSettings();
	}

	public void LoadSettings()
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		var metadata = MapFileService.LoadMetadata(wsPath);
		int aiCount = metadata.MapProperties?.TestAiPlayerCount ?? 1;
		bool randomize = metadata.MapProperties?.TestRandomizeSlots ?? false;

		if (_numAiPlayers != null) _numAiPlayers.Value = aiCount;
		if (_chkRandomizeSlots != null) _chkRandomizeSlots.ButtonPressed = randomize;
	}

	public void SaveSettings()
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		int aiCount = (int)(_numAiPlayers?.Value ?? 1);
		bool randomize = _chkRandomizeSlots?.ButtonPressed ?? false;

		GameContext.MetadataService.UpdateMetadata(wsPath, meta =>
		{
			meta.MapProperties.TestAiPlayerCount = aiCount;
			meta.MapProperties.TestRandomizeSlots = randomize;
		});
	}

	public override void OpenDialog()
	{
		LoadSettings();
		base.OpenDialog();
	}
}
