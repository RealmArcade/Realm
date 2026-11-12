using System;
using Godot;
using Realm.Client.Services;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the lighting card needs from the editor: the accordion chrome it shares with every other card, the slider
/// drag flag so camera input is held off while a value is being dragged, and the live apply, which pushes the tuning
/// values at the terrain material.
/// </summary>
public sealed record LightingTuningHooks(
	Node Owner,
	Action<Button> StyleAccordionHeader,
	Action<Button, Control, string> SetupAccordion,
	Action<bool> SetSliderDragging,
	Action ApplyLiveTuning);

/// <summary>
/// The lighting tuning card: three live overrides of the terrain blending, added to the left sidebar hidden and revealed
/// by the editor's debug entry. Each slider writes straight to the environment service and re-applies, so the map updates
/// as it is dragged rather than on a confirm.
/// </summary>
public sealed class MapEditorLightingTuningPanel
{
	private const string LeftVBoxPath = "LeftSlidePanel/LeftScroll/LeftVBox";

	public VBoxContainer Content { get; private set; } = null!;

	public void Build(LightingTuningHooks hooks)
	{
		var leftSlidePanel = hooks.Owner.GetNodeOrNull<Control>("LeftSlidePanel");
		if (leftSlidePanel != null)
		{
			leftSlidePanel.CustomMinimumSize = new Vector2(325, 0);
			leftSlidePanel.OffsetRight = 325.0f;
		}

		var leftVBox = hooks.Owner.GetNodeOrNull<VBoxContainer>(LeftVBoxPath);
		if (leftVBox == null) return;

		var accordion = new VBoxContainer();
		accordion.Name = "LightingTuningAccordion";

		var header = new Button();
		header.Name = "BtnHeaderLightingTuning";
		accordion.AddChild(header);

		Content = new VBoxContainer();
		Content.Name = "ContentLightingTuning";
		accordion.AddChild(Content);

		leftVBox.AddChild(accordion);

		hooks.StyleAccordionHeader(header);
		hooks.SetupAccordion(header, Content, "💡 Lighting Tuning (Live Override)");

		CreateSectionHeader(Content, "--- TERRAIN TEXTURE BLENDING ---");

		CreateSliderRow(hooks, Content, "Blend Softness", 0.001f, 0.20f, 0.002f,
			GameContext.EnvironmentService.TuneHeightBlendSoftness, val =>
			{
				GameContext.EnvironmentService.TuneHeightBlendSoftness = val;
				hooks.ApplyLiveTuning();
			}, "0.00#");

		CreateSliderRow(hooks, Content, "Blend Noise Str", 0f, 1f, 0.02f,
			GameContext.EnvironmentService.TuneBlendNoiseStrength, val =>
			{
				GameContext.EnvironmentService.TuneBlendNoiseStrength = val;
				hooks.ApplyLiveTuning();
			});

		CreateSliderRow(hooks, Content, "Blend Noise Scl", 0.01f, 0.5f, 0.005f,
			GameContext.EnvironmentService.TuneBlendNoiseScale, val =>
			{
				GameContext.EnvironmentService.TuneBlendNoiseScale = val;
				hooks.ApplyLiveTuning();
			}, "0.00#");

		accordion.Visible = false;
	}

	private static HSlider CreateSliderRow(LightingTuningHooks hooks, VBoxContainer parent, string labelText,
		float min, float max, float step, float initialVal, Action<float> onChanged, string format = "0.0#",
		float labelWidth = 70f)
		=> MapEditorStyle.SliderRow(parent, labelText, min, max, step, initialVal, onChanged,
			drag => hooks.SetSliderDragging(drag), format, labelWidth);

	private static Label CreateSectionHeader(VBoxContainer parent, string titleText)
	{
		var lbl = new Label();
		lbl.Name = "Lbl_Label";
		lbl.Text = titleText;
		lbl.AddThemeFontSizeOverride("font_size", 11);
		lbl.Modulate = new Color(0.95f, 0.85f, 0.35f);
		parent.AddChild(lbl);
		return lbl;
	}
}
