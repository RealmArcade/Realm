// Scene construction: the source-file row, the reference-image row with its mask-colour picker, the
// sub-category and options, and the progress footer.

using System;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Shared;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using Realm.Client.Utils;


namespace Realm.Client.UI.MapEditor;

public partial class ConvertGlbDialog
{
	private void BuildDialogUi()
	{
		var scroll = new ScrollContainer();
		scroll.Name = "Scroll_ScrollContainer";
		scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		BodyContainer.AddChild(scroll);

		var vbox = new VBoxContainer();
		vbox.Name = "Vbox_VBoxContainer";
		vbox.AddThemeConstantOverride("separation", 10);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.AddChild(vbox);

		AddSectionHeader(vbox, TranslationServer.Translate("SOURCE 3D MODEL"));

		var fileRow = new HBoxContainer();
		fileRow.Name = "FileRow_HBoxContainer";
		fileRow.AddThemeConstantOverride("separation", 6);

		var lblFile = new Label();
		lblFile.Name = "LblFile_Label";
		lblFile.Text = TranslationServer.Translate("Model File:");
		lblFile.CustomMinimumSize = new Vector2(120, 0);
		lblFile.AddThemeFontSizeOverride("font_size", 11);
		fileRow.AddChild(lblFile);

		_txtSourceFile = new LineEdit();
		_txtSourceFile.Name = "TxtSourceFile_LineEdit";
		_txtSourceFile.PlaceholderText = TranslationServer.Translate("Select a .glb, .gltf, .fbx, or .obj file...");
		_txtSourceFile.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_txtSourceFile.AddThemeFontSizeOverride("font_size", 11);
		_txtSourceFile.TextChanged += OnSourceFileChanged;
		fileRow.AddChild(_txtSourceFile);

		_btnSelectFile = new Button();
		_btnSelectFile.Name = "BtnSelectFile_Button";
		_btnSelectFile.Set("icon_max_width", 0);
		_btnSelectFile.Text = UnicodeIcons.FOLDER_OPEN + " " + TranslationServer.Translate("Browse...");
		ApplyFontAwesomeFont(_btnSelectFile);
		_btnSelectFile.AddThemeFontSizeOverride("font_size", 11);
		_btnSelectFile.CustomMinimumSize = new Vector2(90, 24);
		_btnSelectFile.FocusMode = FocusModeEnum.None;
		_btnSelectFile.Pressed += OnSelectFilePressed;
		fileRow.AddChild(_btnSelectFile);

		vbox.AddChild(fileRow);

		AddSectionHeader(vbox, TranslationServer.Translate("SOURCE REFERENCE IMAGE"));

		var referenceImageRow = new HBoxContainer();
		referenceImageRow.Name = "ReferenceImageRow_HBoxContainer";
		referenceImageRow.AddThemeConstantOverride("separation", 6);

		var lblReferenceImage = new Label();
		lblReferenceImage.Name = "LblReferenceImage_Label";
		lblReferenceImage.Text = TranslationServer.Translate("Reference Image:");
		lblReferenceImage.CustomMinimumSize = new Vector2(120, 0);
		lblReferenceImage.AddThemeFontSizeOverride("font_size", 11);
		referenceImageRow.AddChild(lblReferenceImage);

		_txtReferenceImage = new LineEdit();
		_txtReferenceImage.Name = "TxtReferenceImage_LineEdit";
		_txtReferenceImage.PlaceholderText = TranslationServer.Translate("Optional .png, .webp, .jpg or .rtex reference image...");
		_txtReferenceImage.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_txtReferenceImage.AddThemeFontSizeOverride("font_size", 11);
		_txtReferenceImage.TooltipText = TranslationServer.Translate("Stored as lossy WebP inside the .rmesh so the original reference can be recovered for re-generation or 2D portraits");
		referenceImageRow.AddChild(_txtReferenceImage);

		_btnSelectReferenceImage = new Button();
		_btnSelectReferenceImage.Name = "BtnSelectReferenceImage_Button";
		_btnSelectReferenceImage.Set("icon_max_width", 0);
		_btnSelectReferenceImage.Text = UnicodeIcons.FOLDER_OPEN + " " + TranslationServer.Translate("Browse...");
		ApplyFontAwesomeFont(_btnSelectReferenceImage);
		_btnSelectReferenceImage.AddThemeFontSizeOverride("font_size", 11);
		_btnSelectReferenceImage.CustomMinimumSize = new Vector2(90, 24);
		_btnSelectReferenceImage.FocusMode = FocusModeEnum.None;
		_btnSelectReferenceImage.Pressed += OnSelectReferenceImagePressed;
		referenceImageRow.AddChild(_btnSelectReferenceImage);

		_btnClearReferenceImage = new Button();
		_btnClearReferenceImage.Name = "BtnClearReferenceImage_Button";
		_btnClearReferenceImage.Set("icon_max_width", 0);
		_btnClearReferenceImage.Text = UnicodeIcons.TRASH_ALT;
		_btnClearReferenceImage.TooltipText = TranslationServer.Translate("Clear the reference image so nothing is embedded");
		ApplyFontAwesomeFont(_btnClearReferenceImage);
		_btnClearReferenceImage.AddThemeFontSizeOverride("font_size", 11);
		_btnClearReferenceImage.CustomMinimumSize = new Vector2(34, 24);
		_btnClearReferenceImage.FocusMode = FocusModeEnum.None;
		_btnClearReferenceImage.Pressed += OnClearReferenceImagePressed;
		referenceImageRow.AddChild(_btnClearReferenceImage);

		vbox.AddChild(referenceImageRow);

		var lblReferenceImageHint = AddDescription(vbox, TranslationServer.Translate("Reference images are embedded automatically when the source model is an .rmesh that already carries one."));
		lblReferenceImageHint.Name = "LblReferenceImageHint_Label";

		AddSectionHeader(vbox, TranslationServer.Translate("TARGET ASSET PROPERTIES"));

		string[] subCats = new string[]
		{
			TranslationServer.Translate("Characters (models/characters)").ToString(),
			TranslationServer.Translate("Buildings (models/buildings)").ToString(),
			TranslationServer.Translate("Props (models/props)").ToString(),
			TranslationServer.Translate("Items (models/items)").ToString()
		};
		_optSubCategory = AddOptionDropdown(vbox, TranslationServer.Translate("Category:"), subCats, 2, (_) => ApplyCategoryDefaults(), 120f);
		_txtAssetName = AddTextInput(vbox, TranslationServer.Translate("Asset Name:"), "", (_) => { }, TranslationServer.Translate("e.g. orc_warrior"), 120f);

		AddSectionHeader(vbox, TranslationServer.Translate("TEAM COLOR MASKING"));

		_chkTeamColorMask = AddCheckBox(vbox, TranslationServer.Translate("Apply team color mask"), false, (enabled) =>
		{
			if (_colorPickerRow != null) _colorPickerRow.Visible = enabled;
			if (_chkAutoCorrectChromaKey != null) _chkAutoCorrectChromaKey.Visible = enabled;
		}, TranslationServer.Translate("Masks target color in textures to receive dynamic player team colors in-game"));

		_colorPickerRow = new HBoxContainer();
		_colorPickerRow.Name = "ColorPickerRow_HBoxContainer";
		_colorPickerRow.AddThemeConstantOverride("separation", 8);
		_colorPickerRow.Visible = false;

		var lblColor = new Label();
		lblColor.Name = "LblColor_Label";
		lblColor.Text = TranslationServer.Translate("Mask Color:");
		lblColor.CustomMinimumSize = new Vector2(120, 0);
		lblColor.AddThemeFontSizeOverride("font_size", 11);
		_colorPickerRow.AddChild(lblColor);

		_colorPicker = new ColorPickerButton();
		_colorPicker.CustomMinimumSize = new Vector2(40, 24);
		_colorPicker.EditAlpha = false;
		_colorPicker.Color = new Color(1.0f, 0.0f, 1.0f);
		_colorPicker.PopupClosed += OnColorPickerPopupClosed;
		_colorPickerRow.AddChild(_colorPicker);

		_btnDetectMaskColor = new Button();
		_btnDetectMaskColor.Name = "BtnDetectMaskColor_Button";
		_btnDetectMaskColor.Set("icon_max_width", 0);
		_btnDetectMaskColor.AddThemeConstantOverride("icon_max_width", 0);
		_btnDetectMaskColor.Text = "🎨 " + TranslationServer.Translate("Detect mask color");
		_btnDetectMaskColor.TooltipText = TranslationServer.Translate("Auto-detect dominant mask color from selected model texture");
		_btnDetectMaskColor.AddThemeFontSizeOverride("font_size", 11);
		_btnDetectMaskColor.CustomMinimumSize = new Vector2(0, 24);
		_btnDetectMaskColor.FocusMode = FocusModeEnum.None;
		_btnDetectMaskColor.Pressed += OnDetectMaskColorPressed;
		_colorPickerRow.AddChild(_btnDetectMaskColor);

		var lblColorHint = new Label();
		lblColorHint.Name = "LblColorHint_Label";
		lblColorHint.Text = TranslationServer.Translate("(Default: Hot Pink #FF00FF)");
		lblColorHint.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		lblColorHint.AddThemeFontSizeOverride("font_size", 10);
		_colorPickerRow.AddChild(lblColorHint);

		vbox.AddChild(_colorPickerRow);

		_chkAutoCorrectChromaKey = AddCheckBox(vbox, TranslationServer.Translate("Auto-correct mask color to texture"), true, null, TranslationServer.Translate("Automatically snaps the mask color to the closest matching color with highest luminosity in the texture"));
		_chkAutoCorrectChromaKey.Visible = false;

		AddSectionHeader(vbox, TranslationServer.Translate("SKELETAL AUTO-RIGGING"));

		_chkAutoRig = AddCheckBox(vbox, TranslationServer.Translate("Auto-Rig Humanoid"), false, (enabled) =>
		{
			if (_autoRigRow != null) _autoRigRow.Visible = enabled;
		}, TranslationServer.Translate("Automatically generates a Mixamo-compatible humanoid skeleton and skin weights"));

		_autoRigRow = new VBoxContainer();
		_autoRigRow.Name = "AutoRigRow_VBoxContainer";
		_autoRigRow.AddThemeConstantOverride("separation", 4);
		_autoRigRow.Visible = false;

		var lblRigDesc = new Label();
		lblRigDesc.Name = "LblRigDesc_Label";
		lblRigDesc.Text = TranslationServer.Translate("Attaches a humanoid bone skeleton to unrigged humanoid meshes for compatibility with Realm animations.");
		lblRigDesc.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		lblRigDesc.AddThemeFontSizeOverride("font_size", 10);
		lblRigDesc.AutowrapMode = TextServer.AutowrapMode.Word;
		lblRigDesc.CustomMinimumSize = new Vector2(530, 0);
		lblRigDesc.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_autoRigRow.AddChild(lblRigDesc);

		vbox.AddChild(_autoRigRow);

		_progressBar = UIStyle.CreateProgressBar(0f, 18f);
		_progressBar.ShowPercentage = true;
		_progressBar.Visible = false;
		BodyContainer.AddChild(_progressBar);

		_lblStatus = new Label();
		_lblStatus.Name = "LblStatus_Label";
		_lblStatus.AddThemeFontSizeOverride("font_size", 11);
		_lblStatus.AddThemeColorOverride("font_color", new Color(0.9f, 0.4f, 0.4f));
		_lblStatus.AutowrapMode = TextServer.AutowrapMode.Word;
		_lblStatus.CustomMinimumSize = new Vector2(530, 0);
		_lblStatus.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lblStatus.Visible = false;
		BodyContainer.AddChild(_lblStatus);

		ApplyButton.Text = "🔄 " + TranslationServer.Translate("Convert & Import");
	}

	/// <summary>
	/// The dialog's own buttons carry FontAwesome glyphs in their text, so they need the face that draws them; without
	/// it the glyph resolves through the default font and reads as a broken image instead of an icon.
	/// </summary>
	private void ApplyFontAwesomeFont(Button button)
	{
		var font = Hud?.GetFontAwesomeFont();
		if (font == null) return;

		button.AddThemeFontOverride("font", font);
	}

}