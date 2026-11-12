using System;
using Godot;
using Realm.Client.Services;
using Realm.Ecs.Components.Terrain;
using Realm.Shared.Metadata;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the water panel needs from the editor: its node tree and the sibling it follows, the shared accordion and
/// highlight styling, the profile dialog it opens, and the slider-drag flag. Row styling is not here: it is shared
/// chrome, so the panel calls MapEditorStyle directly.
/// </summary>
public sealed record WaterSettingsHooks(
	Node Owner,
	Node? InsertAfter,
	Action<Button> StyleAccordionHeader,
	Action<Button, Control, string> SetupAccordion,
	StyleBoxFlat HighlightStyle,
	Action OpenProfiles,
	Action<bool> SetSliderDragging);

/// <summary>
/// The water accordion in the editor's right panel: water height, the add/remove action pair, and the water
/// profile picker with its swatch list. It owns the accordion and every control in it, and reads the profile list
/// and the active profile from the terrain, so the editor only builds it and asks it to refresh.
/// </summary>
public sealed class WaterSettingsPanel
{
	public VBoxContainer Accordion { get; private set; } = null!;
	public Button Header { get; private set; } = null!;
	public VBoxContainer Content { get; private set; } = null!;
	public VBoxContainer HeightBox { get; private set; } = null!;
	public HSlider HeightSlider { get; private set; } = null!;
	public Label HeightValue { get; private set; } = null!;
	public VBoxContainer ModeBox { get; private set; } = null!;
	public OptionButton ModeOption { get; private set; } = null!;
	public Button ProfilesButton { get; private set; } = null!;
	public Button ActionAdd { get; private set; } = null!;
	public Button ActionRemove { get; private set; } = null!;

	public void Build(WaterSettingsHooks hooks)
	{
		BuildAccordion(hooks);
		BuildHeightSlider(hooks);
		BuildActionButtons(hooks);
		BuildModePicker(hooks);
		RefreshSwatches();
		BuildProfilesButton(hooks);
	}

	private void BuildAccordion(WaterSettingsHooks hooks)
	{
		Accordion = new VBoxContainer();
		Accordion.Name = "WaterAccordion";
		Header = new Button();
		Header.Name = "BtnHeaderWater";
		Content = new VBoxContainer();
		Content.Name = "ContentWater";
		Accordion.AddChild(Header);
		Accordion.AddChild(Content);

		var mainAccordionContainer = hooks.Owner.GetNodeOrNull<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer");
		if (mainAccordionContainer != null)
		{
			mainAccordionContainer.AddChild(Accordion);
			if (hooks.InsertAfter != null)
			{
				mainAccordionContainer.MoveChild(Accordion, hooks.InsertAfter.GetIndex() + 1);
			}
		}

		hooks.StyleAccordionHeader(Header);
		hooks.SetupAccordion(Header, Content, TranslationServer.Translate("Liquid / Water Config"));
	}

	private void BuildHeightSlider(WaterSettingsHooks hooks)
	{
		HeightBox = new VBoxContainer();
		HeightBox.Name = "WaterHeightBox";
		HeightBox.AddThemeConstantOverride("separation", 2);

		var headerWaterHeight = new HBoxContainer();
		headerWaterHeight.Name = "HeaderWaterHeight";
		HeightBox.AddChild(headerWaterHeight);

		var lblWaterHeightTitle = new Label();
		lblWaterHeightTitle.Name = "LblWaterHeightTitle";
		lblWaterHeightTitle.Text = TranslationServer.Translate("Water Height (Alt + Mouse_Wheel)");
		lblWaterHeightTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		lblWaterHeightTitle.AddThemeFontSizeOverride("font_size", 10);
		headerWaterHeight.AddChild(lblWaterHeightTitle);

		HeightValue = new Label();
		HeightValue.Name = "LblWaterHeightValue";
		HeightValue.Text = "0.9m";
		HeightValue.AddThemeFontSizeOverride("font_size", 11);
		headerWaterHeight.AddChild(HeightValue);

		HeightSlider = new HSlider();
		HeightSlider.Name = "SldWaterHeight";
		HeightSlider.FocusMode = Control.FocusModeEnum.None;
		HeightSlider.MinValue = 0.1;
		HeightSlider.MaxValue = 15.0;
		HeightSlider.Step = 0.1;
		HeightSlider.Value = 0.9;
		HeightSlider.ValueChanged += (val) =>
		{
			float fVal = (float)val;
			HeightValue.Text = fVal.ToString("F1") + "m";
			if (GameContext.Host != null) GameContext.EditorService.Tools.WaterHeight = fVal;
		};
		HeightSlider.DragStarted += () => hooks.SetSliderDragging(true);
		HeightSlider.DragEnded += (valueChanged) => hooks.SetSliderDragging(false);
		HeightBox.AddChild(HeightSlider);

		Content.AddChild(HeightBox);
	}

	private void BuildActionButtons(WaterSettingsHooks hooks)
	{
		var waterActionBox = new VBoxContainer();
		waterActionBox.Name = "WaterActionBox";
		waterActionBox.AddThemeConstantOverride("separation", 2);

		var lblWaterAction = new Label();
		lblWaterAction.Name = "LblWaterActionTitle";
		lblWaterAction.Text = TranslationServer.Translate("Action");
		lblWaterAction.AddThemeFontSizeOverride("font_size", 10);
		waterActionBox.AddChild(lblWaterAction);

		var waterActionButtonRow = new HBoxContainer();
		waterActionButtonRow.Name = "WaterActionButtonRow";
		waterActionButtonRow.AddThemeConstantOverride("separation", 4);
		waterActionBox.AddChild(waterActionButtonRow);

		ActionAdd = new Button();
		ActionAdd.Name = "BtnWaterActionAdd";
		ActionAdd.Set("icon_max_width", 0);
		ActionAdd.Text = "+ " + TranslationServer.Translate("Add");
		ActionAdd.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		ActionAdd.FocusMode = Control.FocusModeEnum.None;
		ActionAdd.AddThemeFontSizeOverride("font_size", 11);
		ActionAdd.TooltipText = TranslationServer.Translate("Add water to terrain");
		MapEditorStyle.RowButton(ActionAdd);
		ActionAdd.AddThemeStyleboxOverride("normal", hooks.HighlightStyle);
		waterActionButtonRow.AddChild(ActionAdd);

		ActionRemove = new Button();
		ActionRemove.Name = "BtnWaterActionRemove";
		ActionRemove.Set("icon_max_width", 0);
		ActionRemove.Text = "- " + TranslationServer.Translate("Remove");
		ActionRemove.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		ActionRemove.FocusMode = Control.FocusModeEnum.None;
		ActionRemove.AddThemeFontSizeOverride("font_size", 11);
		ActionRemove.TooltipText = TranslationServer.Translate("Remove water from terrain");
		MapEditorStyle.RowButton(ActionRemove);
		waterActionButtonRow.AddChild(ActionRemove);

		ActionAdd.Pressed += () =>
		{
			GameContext.EditorService.Tools.IsWaterRemove = false;
			ActionAdd.AddThemeStyleboxOverride("normal", hooks.HighlightStyle);
			ActionRemove.RemoveThemeStyleboxOverride("normal");
			if (HeightBox != null) HeightBox.Visible = true;
			if (ModeBox != null) ModeBox.Visible = true;
			if (ProfilesButton != null) ProfilesButton.Visible = true;
		};

		ActionRemove.Pressed += () =>
		{
			GameContext.EditorService.Tools.IsWaterRemove = true;
			ActionRemove.AddThemeStyleboxOverride("normal", hooks.HighlightStyle);
			ActionAdd.RemoveThemeStyleboxOverride("normal");
			if (HeightBox != null) HeightBox.Visible = false;
			if (ModeBox != null) ModeBox.Visible = false;
			if (ProfilesButton != null) ProfilesButton.Visible = false;
		};

		Content.AddChild(waterActionBox);
	}

	private void BuildModePicker(WaterSettingsHooks hooks)
	{
		ModeBox = new VBoxContainer();
		ModeBox.Name = "WaterModeBox";
		ModeBox.AddThemeConstantOverride("separation", 2);

		ModeOption = new OptionButton();
		ModeOption.Name = "OptWaterMode";
		ModeOption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		ModeOption.ClipText = true;
		ModeOption.CustomMinimumSize = new Vector2(0, 24);
		ModeBox.AddChild(ModeOption);

		ModeOption.ItemSelected += (idx) =>
		{
			byte profIdx = 0;
			var meta = ModeOption.GetItemMetadata((int)idx);
			if (meta.VariantType != Variant.Type.Nil)
			{
				profIdx = (byte)(int)meta;
			}

			if (GameContext.Host != null)
			{
				GameContext.Host.EditorWaterMode = WaterType.Shallow;
				GameContext.Host.ActiveWaterProfileIndex = profIdx;
			}
		};
		Content.AddChild(ModeBox);
	}

	private void BuildProfilesButton(WaterSettingsHooks hooks)
	{
		ProfilesButton = new Button();
		ProfilesButton.Name = "BtnWaterProfiles";
		ProfilesButton.Set("icon_max_width", 0);
		ProfilesButton.Text = "⚙ " + TranslationServer.Translate("Water Profiles");
		ProfilesButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		ProfilesButton.TooltipText = TranslationServer.Translate("Configure Liquid / Water Uber Profiles");
		ProfilesButton.FocusMode = Control.FocusModeEnum.None;
		ProfilesButton.AddThemeFontSizeOverride("font_size", 11);
		ProfilesButton.Pressed += () => hooks.OpenProfiles();
		Content.AddChild(ProfilesButton);
	}

	/// <summary>
	/// Rebuilds the profile list from the terrain's water profiles, keeping the profile that was selected. Falls
	/// back to the shipped defaults when no terrain is loaded, and pushes the active profile to the environment.
	/// </summary>
	public void RefreshSwatches()
	{
		if (ModeOption == null) return;

		byte currentProf = GameContext.Host != null ? GameContext.Host.ActiveWaterProfileIndex : (byte)0;
		if (ModeOption.Selected >= 0 && ModeOption.Selected < ModeOption.ItemCount)
		{
			var currentMeta = ModeOption.GetItemMetadata(ModeOption.Selected);
			if (currentMeta.VariantType != Variant.Type.Nil)
			{
				currentProf = (byte)(int)currentMeta;
			}
		}

		ModeOption.Clear();

		var profiles = GameContext.Terrain != null ? GameContext.Terrain.GetWaterProfiles() : null;
		if (profiles != null && profiles.Count > 0)
		{
			int itemIdx = 0;
			foreach (var kvp in profiles)
			{
				byte pIdx = kvp.Key;
				var prof = kvp.Value;
				string label = TranslationServer.Translate(prof.Name);
				ModeOption.AddItem(label, itemIdx);
				ModeOption.SetItemMetadata(itemIdx, pIdx);
				itemIdx++;
			}
		}
		else
		{
			var defaults = WaterProfileSaveData.CreateDefaultProfiles();
			int itemIdx = 0;
			foreach (var def in defaults)
			{
				ModeOption.AddItem(TranslationServer.Translate(def.Name), itemIdx);
				ModeOption.SetItemMetadata(itemIdx, def.ProfileIndex);
				itemIdx++;
			}
		}

		int targetSelected = 0;
		for (int i = 0; i < ModeOption.ItemCount; i++)
		{
			var meta = ModeOption.GetItemMetadata(i);
			if (meta.VariantType != Variant.Type.Nil && (byte)(int)meta == currentProf)
			{
				targetSelected = i;
				break;
			}
		}

		if (ModeOption.ItemCount > 0)
		{
			ModeOption.Selected = targetSelected;
		}

		GameContext.EnvironmentService.SetActiveWaterProfileIndex(currentProf);
	}
}
