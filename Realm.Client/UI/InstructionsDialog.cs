using System.Collections.Generic;
using Godot;
using Realm.Client.Services;
using Realm.Client.UI.InGame;

namespace Realm.Client.UI;

/// <summary>
///     The in-match Instructions menu. It only shows what the authority published for this map: the selector lists the
///     page titles in the order the map registered them, and choosing one swaps the text below it. A screen with no pages
///     is never opened, so the menu cannot offer a blank entry.
/// </summary>
[GlobalClass]
public partial class InstructionsDialog : Control
{
	private const int CardWidth = 860;
	private const int CardHeight = 620;
	private const int BodyWidth = CardWidth - 52;

	private readonly List<InstructionsSection> _sections = new();

	private OptionButton _sectionSelect;
	private RichTextLabel _sectionBody;

	public override void _Ready()
	{
		Name = "InstructionsDialog";
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;

		BuildLayout();
	}

	public void Open(IReadOnlyList<InstructionsSection> sections)
	{
		_sections.Clear();
		for (int index = 0; index < sections.Count; index++)
		{
			_sections.Add(sections[index]);
		}

		RebuildSelector();
		Visible = true;
	}

	public void Refresh(IReadOnlyList<InstructionsSection> sections)
	{
		if (!GodotObject.IsInstanceValid(this)) return;

		int previouslySelected = _sectionSelect.Selected;
		Open(sections);

		if (previouslySelected > 0 && previouslySelected < _sectionSelect.ItemCount)
		{
			_sectionSelect.Selected = previouslySelected;
		}

		ShowSelectedSection();
	}

	private void BuildLayout()
	{
		var backdrop = new ColorRect { Name = "InstructionsDialogBackdrop", Color = new Color(0.02f, 0.02f, 0.04f, 0.78f) };
		backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		backdrop.MouseFilter = MouseFilterEnum.Stop;
		backdrop.GuiInput += OnBackdropInput;
		AddChild(backdrop);

		var center = new CenterContainer { Name = "InstructionsDialogCenter" };
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		center.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(center);

		var card = new PanelContainer { Name = "InstructionsDialogCard" };
		card.CustomMinimumSize = new Vector2(CardWidth, CardHeight);
		card.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		center.AddChild(card);

		var margin = new MarginContainer { Name = "InstructionsDialogMargin" };
		margin.AddThemeConstantOverride("margin_left", 26);
		margin.AddThemeConstantOverride("margin_right", 26);
		margin.AddThemeConstantOverride("margin_top", 20);
		margin.AddThemeConstantOverride("margin_bottom", 20);
		card.AddChild(margin);

		var column = new VBoxContainer { Name = "InstructionsDialogColumn" };
		column.AddThemeConstantOverride("separation", 12);
		margin.AddChild(column);

		var titleLabel = new Label { Name = "InstructionsDialogTitle" };
		UIStyle.ApplyTitle(titleLabel, TranslationServer.Translate("INSTRUCTIONS"), 26);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		column.AddChild(titleLabel);

		column.AddChild(BuildSectionSelector());
		column.AddChild(BuildBody());
		column.AddChild(BuildFooter());
	}

	private OptionButton BuildSectionSelector()
	{
		_sectionSelect = new OptionButton { Name = "InstructionsSectionSelect" };
		_sectionSelect.Set("icon_max_width", 0);
		_sectionSelect.CustomMinimumSize = new Vector2(0, 34);
		_sectionSelect.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_sectionSelect.FocusMode = FocusModeEnum.All;
		_sectionSelect.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_sectionSelect.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_sectionSelect.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_sectionSelect.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		_sectionSelect.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_sectionSelect.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		_sectionSelect.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
		_sectionSelect.AddThemeColorOverride("font_focus_color", UIStyle.ColorGoldDull);
		_sectionSelect.AddThemeFontSizeOverride("font_size", 15);
		_sectionSelect.ItemSelected += _ => ShowSelectedSection();
		_sectionSelect.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		return _sectionSelect;
	}

	private ScrollContainer BuildBody()
	{
		var scroll = new ScrollContainer { Name = "InstructionsBodyScroll" };
		scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

		_sectionBody = new RichTextLabel { Name = "InstructionsSectionBody" };
		_sectionBody.BbcodeEnabled = true;
		_sectionBody.SelectionEnabled = true;
		_sectionBody.FitContent = true;
		_sectionBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_sectionBody.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_sectionBody.SizeFlagsVertical = SizeFlags.ShrinkBegin;
		_sectionBody.CustomMinimumSize = new Vector2(BodyWidth, 0);
		_sectionBody.AddThemeFontSizeOverride("normal_font_size", 15);
		_sectionBody.AddThemeFontSizeOverride("bold_font_size", 15);
		_sectionBody.AddThemeFontSizeOverride("italics_font_size", 15);
		_sectionBody.AddThemeColorOverride("default_color", new Color(0.9f, 0.9f, 0.95f));
		scroll.AddChild(_sectionBody);

		return scroll;
	}

	private HBoxContainer BuildFooter()
	{
		var footer = new HBoxContainer { Name = "InstructionsDialogFooter" };
		footer.Alignment = BoxContainer.AlignmentMode.End;

		var closeButton = new Button { Name = "InstructionsCloseButton" };
		closeButton.Set("icon_max_width", 0);
		closeButton.CustomMinimumSize = new Vector2(140, 34);
		closeButton.FocusMode = FocusModeEnum.None;
		closeButton.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		closeButton.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		closeButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		closeButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(closeButton, TranslationServer.Translate("CLOSE"), 14);
		closeButton.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		closeButton.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		closeButton.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
		closeButton.Pressed += Close;
		closeButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		footer.AddChild(closeButton);

		return footer;
	}

	private void RebuildSelector()
	{
		_sectionSelect.Clear();

		for (int index = 0; index < _sections.Count; index++)
		{
			_sectionSelect.AddItem(_sections[index].Title, index);
		}

		if (_sections.Count > 0) _sectionSelect.Selected = 0;

		ShowSelectedSection();
	}

	private void ShowSelectedSection()
	{
		if (_sectionBody == null || !GodotObject.IsInstanceValid(_sectionBody)) return;

		int selected = _sectionSelect.Selected;
		_sectionBody.Text = selected >= 0 && selected < _sections.Count ? _sections[selected].Body : string.Empty;
	}

	private void OnBackdropInput(InputEvent inputEvent)
	{
		if (inputEvent is InputEventMouseButton mouseButton && mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.Left)
		{
			Close();
		}
	}

	private void Close()
	{
		GameContext.UserInterface?.PlayClickSound();
		QueueFree();
	}
}
