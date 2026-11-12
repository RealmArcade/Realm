using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;

namespace Realm.Client.UI.MapEditor;

public partial class ObjectAttachmentDialog
{
	private void BuildControls()
	{
		Add3DPreviewViewport(BodyContainer, new Vector2(530, 230));

		var topControlsVBox = CreateTopControlsColumn();

		AddCameraPresetToolbar(topControlsVBox, includeBack: true);
		BuildTopInfoRow(topControlsVBox);
		BuildSocketRow(topControlsVBox);
		BuildPseudoSocketAuthoringRow(topControlsVBox);
		BuildAttachmentPickerRow(topControlsVBox);
		BuildTransformSliders();

		AddSectionHeader(BodyContainer, "📎 " + TranslationServer.Translate("CONFIGURED ATTACHMENTS & VFX"), new Color(0.85f, 0.75f, 0.4f));
		_configuredAttachmentsContainer = CreateScrollBody(150);

		CancelButton.Text = TranslationServer.Translate("CANCEL");
		ApplyButton.Text = TranslationServer.Translate("APPLY");
	}

	private void BuildTopInfoRow(Container parent)
	{
		var infoRow = new HBoxContainer();
		infoRow.Name = "InfoRow_HBoxContainer";
		infoRow.AddThemeConstantOverride("separation", 8);

		_lblUnitTitle = new Label { Text = TranslationServer.Translate("Unit:") };
		_lblUnitTitle.AddThemeFontSizeOverride("font_size", 11);
		_lblUnitTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		infoRow.AddChild(_lblUnitTitle);

		_lblUnitValue = new Label { Text = "-" };
		_lblUnitValue.AddThemeFontSizeOverride("font_size", 11);
		_lblUnitValue.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		infoRow.AddChild(_lblUnitValue);

		var infoSpacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		infoRow.AddChild(infoSpacer);

		AddButton(infoRow, $"{UnicodeIcons.COPY} " + TranslationServer.Translate("Copy Code"), () =>
		{
			string code = $"api.SetUnitHandAttachment(unit, \"{_currentSocketId}\", \"{_currentAttachmentId}\");";
			DisplayServer.ClipboardSet(code);
			Hud?.ShowFeedback(TranslationServer.Translate("Copied script code to clipboard"));
		}, "Copy C# MapScript code reference to clipboard", 10, new Vector2(0, 20));

		parent.AddChild(infoRow);
	}

	private void BuildSocketRow(Container parent)
	{
		var socketRow = new HBoxContainer();
		socketRow.Name = "SocketRow_HBoxContainer";
		socketRow.AddThemeConstantOverride("separation", 6);

		var lblSocketTitle = new Label { Text = TranslationServer.Translate("Socket:") };
		lblSocketTitle.AddThemeFontSizeOverride("font_size", 11);
		lblSocketTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		socketRow.AddChild(lblSocketTitle);

		_optSocket = new OptionButton();
		_optSocket.Name = "OptSocket_OptionButton";
		_optSocket.AddThemeFontSizeOverride("font_size", 11);
		_optSocket.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_optSocket.CustomMinimumSize = new Vector2(160, 24);
		_optSocket.ItemSelected += (idx) =>
		{
			if (_isUpdatingUI) return;
			if (idx >= 0 && idx < _currentAvailableSockets.Count)
			{
				_currentSocketId = _currentAvailableSockets[(int)idx].SocketId;
				_currentParentAttachmentId = null;
				UpdateParentDropdown();
				PreviewCurrentAttachment();
			}
		};
		socketRow.AddChild(_optSocket);

		var lblParentTitle = new Label { Text = TranslationServer.Translate("Attach To:") };
		lblParentTitle.AddThemeFontSizeOverride("font_size", 11);
		lblParentTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		socketRow.AddChild(lblParentTitle);

		_optParent = new OptionButton();
		_optParent.Name = "OptParent_OptionButton";
		_optParent.AddThemeFontSizeOverride("font_size", 11);
		_optParent.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_optParent.CustomMinimumSize = new Vector2(160, 24);
		_optParent.ItemSelected += (idx) =>
		{
			if (_isUpdatingUI) return;
			string? parentId = null;
			if (idx > 0 && idx < _availableParents.Count)
			{
				parentId = _availableParents[(int)idx];
			}

			_currentParentAttachmentId = parentId;
			PreviewCurrentAttachment();
		};
		socketRow.AddChild(_optParent);

		parent.AddChild(socketRow);
	}

	private void BuildPseudoSocketAuthoringRow(Container parent)
	{
		var anchorRow = new HBoxContainer();
		anchorRow.Name = "PseudoSocketAuthoring_HBoxContainer";
		anchorRow.AddThemeConstantOverride("separation", 6);

		var lblAnchorTitle = new Label { Text = TranslationServer.Translate("Custom Anchor:") };
		lblAnchorTitle.AddThemeFontSizeOverride("font_size", 11);
		lblAnchorTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		anchorRow.AddChild(lblAnchorTitle);

		_txtPseudoSocketName = new LineEdit
		{
			Name = "TxtPseudoSocketName_LineEdit",
			PlaceholderText = TranslationServer.Translate("Anchor name"),
			CustomMinimumSize = new Vector2(120, 24),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			TooltipText = TranslationServer.Translate("Name of a custom anchor placed at the selected socket plus the current position offsets")
		};
		_txtPseudoSocketName.AddThemeFontSizeOverride("font_size", 11);
		anchorRow.AddChild(_txtPseudoSocketName);

		AddButton(anchorRow, $"{UnicodeIcons.PLUS} " + TranslationServer.Translate("Save Anchor"), AddOrUpdatePseudoSocketAnchor, "Add or override an anchor for this asset at the selected socket position", 10, new Vector2(110, 24));
		AddButton(anchorRow, $"{UnicodeIcons.TRASH} " + TranslationServer.Translate("Delete"), RemovePseudoSocketAnchor, "Remove a custom anchor from this asset", 10, new Vector2(80, 24));

		parent.AddChild(anchorRow);
	}

	private void BuildAttachmentPickerRow(Container parent)
	{
		var pickerRow = new HBoxContainer();
		pickerRow.Name = "PickerRow_HBoxContainer";
		pickerRow.AddThemeConstantOverride("separation", 6);

		var lblAttTitle = new Label { Text = TranslationServer.Translate("Attachment / VFX:") };
		lblAttTitle.AddThemeFontSizeOverride("font_size", 11);
		lblAttTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		pickerRow.AddChild(lblAttTitle);

		_optAttachmentPicker = new OptionButton();
		_optAttachmentPicker.Name = "OptAttachmentPicker_OptionButton";
		_optAttachmentPicker.AddThemeFontSizeOverride("font_size", 11);
		_optAttachmentPicker.CustomMinimumSize = new Vector2(220, 24);
		_optAttachmentPicker.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_optAttachmentPicker.ItemSelected += (idx) =>
		{
			if (_isUpdatingUI) return;
			if (idx >= 0 && idx < _availableAttachments.Count)
			{
				_currentAttachmentId = _availableAttachments[(int)idx];
				UpdateParentDropdown();
				PreviewCurrentAttachment();
			}
		};
		pickerRow.AddChild(_optAttachmentPicker);

		parent.AddChild(pickerRow);
	}

	private void BuildTransformSliders()
	{
		AddSectionHeader(BodyContainer, "🗡️ " + TranslationServer.Translate("TRANSFORM & NORMAL OFFSET"));

		var transformCols = new HBoxContainer();
		transformCols.Name = "TransformCols_HBoxContainer";
		transformCols.AddThemeConstantOverride("separation", 12);

		var leftCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		leftCol.AddThemeConstantOverride("separation", 4);
		var rightCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		rightCol.AddThemeConstantOverride("separation", 4);

		var posResultX = AddSlider(leftCol, TranslationServer.Translate("Position X:"), -1.5f, 1.5f, 0.005f, 0f, (v) =>
		{
			_currentPosOffset.X = v;
			UpdateActiveAttachmentTransform();
			SyncWorkingAttachmentIfConfigured();
		}, "0.000", 90f);
		_sliderPosX = posResultX.Slider;
		_lblPosX = posResultX.ValueLabel;

		var posResultY = AddSlider(leftCol, TranslationServer.Translate("Position Y:"), -1.5f, 1.5f, 0.005f, 0f, (v) =>
		{
			_currentPosOffset.Y = v;
			UpdateActiveAttachmentTransform();
			SyncWorkingAttachmentIfConfigured();
		}, "0.000", 90f);
		_sliderPosY = posResultY.Slider;
		_lblPosY = posResultY.ValueLabel;

		var posResultZ = AddSlider(leftCol, TranslationServer.Translate("Position Z:"), -1.5f, 1.5f, 0.005f, 0f, (v) =>
		{
			_currentPosOffset.Z = v;
			UpdateActiveAttachmentTransform();
			SyncWorkingAttachmentIfConfigured();
		}, "0.000", 90f);
		_sliderPosZ = posResultZ.Slider;
		_lblPosZ = posResultZ.ValueLabel;

		var scaleResultX = AddSlider(leftCol, TranslationServer.Translate("Scale X:"), 0.05f, 5.0f, 0.05f, 1.0f, (v) =>
		{
			_currentScaleOffset.X = v;
			UpdateActiveAttachmentTransform();
			SyncWorkingAttachmentIfConfigured();
		}, "0.00", 90f);
		_sliderScaleX = scaleResultX.Slider;
		_lblScaleX = scaleResultX.ValueLabel;

		var scaleResultY = AddSlider(leftCol, TranslationServer.Translate("Scale Y:"), 0.05f, 5.0f, 0.05f, 1.0f, (v) =>
		{
			_currentScaleOffset.Y = v;
			UpdateActiveAttachmentTransform();
			SyncWorkingAttachmentIfConfigured();
		}, "0.00", 90f);
		_sliderScaleY = scaleResultY.Slider;
		_lblScaleY = scaleResultY.ValueLabel;

		var scaleResultZ = AddSlider(leftCol, TranslationServer.Translate("Scale Z:"), 0.05f, 5.0f, 0.05f, 1.0f, (v) =>
		{
			_currentScaleOffset.Z = v;
			UpdateActiveAttachmentTransform();
			SyncWorkingAttachmentIfConfigured();
		}, "0.00", 90f);
		_sliderScaleZ = scaleResultZ.Slider;
		_lblScaleZ = scaleResultZ.ValueLabel;

		var rotResultX = AddSlider(rightCol, TranslationServer.Translate("Pitch X (deg):"), -180f, 180f, 1f, 0f, (v) =>
		{
			_currentRotOffset.X = v;
			UpdateActiveAttachmentTransform();
			SyncWorkingAttachmentIfConfigured();
		}, "0", 95f);
		_sliderRotX = rotResultX.Slider;
		_lblRotX = rotResultX.ValueLabel;

		var rotResultY = AddSlider(rightCol, TranslationServer.Translate("Yaw Y (deg):"), -180f, 180f, 1f, 0f, (v) =>
		{
			_currentRotOffset.Y = v;
			UpdateActiveAttachmentTransform();
			SyncWorkingAttachmentIfConfigured();
		}, "0", 95f);
		_sliderRotY = rotResultY.Slider;
		_lblRotY = rotResultY.ValueLabel;

		var rotResultZ = AddSlider(rightCol, TranslationServer.Translate("Roll Z (deg):"), -180f, 180f, 1f, 0f, (v) =>
		{
			_currentRotOffset.Z = v;
			UpdateActiveAttachmentTransform();
			SyncWorkingAttachmentIfConfigured();
		}, "0", 95f);
		_sliderRotZ = rotResultZ.Slider;
		_lblRotZ = rotResultZ.ValueLabel;

		var normalResult = AddSlider(rightCol, TranslationServer.Translate("Normal Offset:"), -0.5f, 0.5f, 0.005f, 0.0f, (v) =>
		{
			_currentNormalOffset = v;
			UpdateActiveAttachmentTransform();
			SyncWorkingAttachmentIfConfigured();
		}, "0.000", 95f);
		_sliderNormalOffset = normalResult.Slider;
		_lblNormalOffset = normalResult.ValueLabel;

		var btnRow = new HBoxContainer();
		btnRow.Name = "BtnRow_HBoxContainer";
		btnRow.AddThemeConstantOverride("separation", 8);
		AddButton(btnRow, "⚡ " + TranslationServer.Translate("Auto Calculate"), () => AutoCalculateAttachmentOrientation(), "Automatically calculate orientation and position based on attachment bounds and camera view", 11, new Vector2(130, 24));
		AddButton(btnRow, "⟲ " + TranslationServer.Translate("Reset"), () => ResetTransformValues(), "Reset transform to defaults", 11, new Vector2(80, 24));
		var btnAdd = AddButton(btnRow, $"{UnicodeIcons.PLUS} " + TranslationServer.Translate("Add"), () => AddOrUpdateCurrentAttachment(), "Add or update this attachment on the selected socket", 11, new Vector2(80, 24));
		btnAdd.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		rightCol.AddChild(btnRow);

		transformCols.AddChild(leftCol);
		transformCols.AddChild(rightCol);
		BodyContainer.AddChild(transformCols);
	}
}
