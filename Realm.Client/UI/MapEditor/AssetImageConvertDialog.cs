using System;
using Godot;
using static Godot.Control;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// The chooser shown before an external image is converted. Terrain and decal conversion derives PBR layers from the
/// source image, every other type stores it as a single layer, so the kind is asked for instead of inferred from the
/// asset filter that happened to be selected.
/// </summary>
public sealed class AssetImageConvertDialog
{
	private static readonly string[] ImageCategories =
	{
		"Terrain", "Decal", "Icon", "Skybox", "Spritesheet", "Ribbon", "Noise", "vfx_radial", "vfx_vertical"
	};

	private readonly Node _owner;
	private readonly string _fileName;
	private readonly string _defaultCategory;
	private readonly Action<string> _onChosen;

	public AssetImageConvertDialog(Node owner, string fileName, string defaultCategory, Action<string> onChosen)
	{
		_owner = owner;
		_fileName = fileName;
		_defaultCategory = defaultCategory;
		_onChosen = onChosen;
	}

	public void Show()
	{
		var overlay = new ColorRect();
		overlay.Name = "ImageConvertOverlay";
		overlay.Color = new Color(0, 0, 0, 0.65f);
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.MouseFilter = MouseFilterEnum.Stop;
		overlay.ZIndex = 1150;
		_owner.AddChild(overlay);

		var center = UIStyle.CreateCenteredOverlay();
		overlay.AddChild(center);

		var panel = new PanelContainer();
		panel.Name = "ImageConvertPanel_PanelContainer";
		panel.CustomMinimumSize = new Vector2(520, 0);
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreatePopoverPanelPlate());
		center.AddChild(panel);

		var margin = new MarginContainer();
		margin.Name = "ImageConvertMargin_MarginContainer";
		margin.AddThemeConstantOverride("margin_left", 28);
		margin.AddThemeConstantOverride("margin_right", 28);
		margin.AddThemeConstantOverride("margin_top", 24);
		margin.AddThemeConstantOverride("margin_bottom", 22);
		panel.AddChild(margin);

		var vbox = new VBoxContainer();
		vbox.Name = "ImageConvertVBox_VBoxContainer";
		vbox.AddThemeConstantOverride("separation", 12);
		margin.AddChild(vbox);

		BuildHeading(vbox);

		OptionButton kindOption = BuildKindOption(vbox);

		BuildHint(vbox);

		BuildButtons(vbox, overlay, kindOption);
	}

	private void BuildHeading(VBoxContainer vbox)
	{
		var title = new Label();
		title.Name = "ImageConvertTitle_Label";
		UIStyle.ApplyTitle(title, TranslationServer.Translate("CONVERT IMAGE"), 16);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		vbox.AddChild(title);

		var message = new Label();
		message.Name = "ImageConvertMessage_Label";
		message.Text = string.Format(TranslationServer.Translate("Choose what this image converts to: {0}"), _fileName);
		message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		message.HorizontalAlignment = HorizontalAlignment.Center;
		message.CustomMinimumSize = new Vector2(440, 0);
		message.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		message.AddThemeFontSizeOverride("font_size", 12);
		message.AddThemeColorOverride("font_color", new Color(0.86f, 0.88f, 0.92f));
		vbox.AddChild(message);
	}

	private OptionButton BuildKindOption(VBoxContainer vbox)
	{
		var kindOption = new OptionButton();
		kindOption.Name = "ImageConvertKind_OptionButton";
		kindOption.Flat = false;
		kindOption.Set("icon_max_width", 0);
		kindOption.CustomMinimumSize = new Vector2(0, 34);
		kindOption.AddThemeFontSizeOverride("font_size", 13);
		UIStyle.ApplyLightDropdown(kindOption);
		kindOption.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.75f));
		kindOption.AddThemeColorOverride("font_hover_color", new Color(1.0f, 0.95f, 0.8f));
		kindOption.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);

		AddKind(kindOption, "Terrain", TranslationServer.Translate("Terrain Texture (PBR)"));
		AddKind(kindOption, "Decal", TranslationServer.Translate("Decal (PBR)"));
		AddKind(kindOption, "Icon", TranslationServer.Translate("Icon"));
		AddKind(kindOption, "Skybox", TranslationServer.Translate("Skybox"));
		AddKind(kindOption, "Spritesheet", TranslationServer.Translate("Spritesheet"));
		AddKind(kindOption, "Ribbon", TranslationServer.Translate("Ribbon"));
		AddKind(kindOption, "Noise", TranslationServer.Translate("Noise"));
		AddKind(kindOption, "vfx_radial", TranslationServer.Translate("VFX Radial"));
		AddKind(kindOption, "vfx_vertical", TranslationServer.Translate("VFX Vertical"));

		vbox.AddChild(kindOption);
		return kindOption;
	}

	private void AddKind(OptionButton kindOption, string category, string label)
	{
		kindOption.AddItem(label);
		if (string.Equals(category, _defaultCategory, StringComparison.OrdinalIgnoreCase))
		{
			kindOption.Selected = kindOption.ItemCount - 1;
		}
	}

	private static void BuildHint(VBoxContainer vbox)
	{
		var hint = new Label();
		hint.Name = "ImageConvertHint_Label";
		hint.Text = TranslationServer.Translate("Terrain and Decal derive PBR layers from the image. Other kinds store it as a single layer, so they keep the source colours unchanged.");
		hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		hint.CustomMinimumSize = new Vector2(440, 0);
		hint.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		hint.AddThemeFontSizeOverride("font_size", 11);
		hint.AddThemeColorOverride("font_color", new Color(0.7f, 0.72f, 0.78f));
		vbox.AddChild(hint);
	}

	private void BuildButtons(VBoxContainer vbox, ColorRect overlay, OptionButton kindOption)
	{
		var buttons = new HBoxContainer();
		buttons.Name = "ImageConvertButtons_HBoxContainer";
		buttons.AddThemeConstantOverride("separation", 24);
		buttons.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		vbox.AddChild(buttons);

		var btnConfirm = UIStyle.CreateActionButton("BtnConfirmImageConvert_Button", TranslationServer.Translate("CONVERT"), 13);
		btnConfirm.CustomMinimumSize = new Vector2(120, 34);
		btnConfirm.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		btnConfirm.Pressed += () =>
		{
			int index = Mathf.Clamp(kindOption.Selected, 0, ImageCategories.Length - 1);
			overlay.QueueFree();
			_onChosen?.Invoke(ImageCategories[index]);
		};
		buttons.AddChild(btnConfirm);

		var btnCancel = UIStyle.CreateActionButton("BtnCancelImageConvert_Button", TranslationServer.Translate("CANCEL"), 13);
		btnCancel.CustomMinimumSize = new Vector2(120, 34);
		btnCancel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
		btnCancel.Pressed += () => overlay.QueueFree();
		buttons.AddChild(btnCancel);
	}
}
