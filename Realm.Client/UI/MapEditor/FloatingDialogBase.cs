using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Fastenshtein;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Distribution;
using Realm.Shared.Services;
using Environment = Godot.Environment;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class FloatingDialogBase : PanelContainer
{
	public FloatingDialogBase() { }

	private static readonly List<FloatingDialogBase> _openDialogs = new();
	public static bool HasAnyDialogOpen => _openDialogs.Count > 0;

	public static bool IsMouseOverAnyDialogOpen(Vector2 mousePos)
	{
		for (int i = _openDialogs.Count - 1; i >= 0; i--)
		{
			var dialog = _openDialogs[i];
			if (dialog != null && GodotObject.IsInstanceValid(dialog) && dialog.IsOpen && dialog.IsVisibleInTree())
			{
				if (dialog.GetGlobalRect().HasPoint(mousePos))
				{
					return true;
				}
			}
		}

		return false;
	}

	public static bool CloseTopmostDialog()
	{
		for (int i = _openDialogs.Count - 1; i >= 0; i--)
		{
			var dialog = _openDialogs[i];
			if (dialog != null && GodotObject.IsInstanceValid(dialog) && dialog.IsOpen)
			{
				dialog.CancelAndClose();
				return true;
			}
			else
			{
				_openDialogs.RemoveAt(i);
			}
		}

		return false;
	}

	public void BringToFront()
	{
		MoveToFront();
		_openDialogs.Remove(this);
		_openDialogs.Add(this);
	}

	protected readonly MapEditorHUD Hud;
	protected VBoxContainer MainVBox;
	protected HBoxContainer HeaderHBox;
	protected Label TitleLabel;
	protected Button CloseButton;
	protected VBoxContainer BodyContainer;
	protected HBoxContainer FooterHBox;
	protected Button CancelButton;
	protected Button ApplyButton;
	protected Button RandomizeAssetsButton;

	private bool _isDragging;
	private Vector2 _dragStartMousePosition;
	private Vector2 _dragStartPosition;

	public bool IsOpen => Visible && GetParent() != null;

	public FloatingDialogBase(MapEditorHUD hud, string titleText, Vector2 minSize)
	{
		Hud = hud;
		CustomMinimumSize = minSize;
		Visible = false;
		AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		GuiInput += (ev) =>
		{
			if (ev is InputEventMouseButton mb && mb.Pressed)
			{
				BringToFront();
			}
		};

		MainVBox = new VBoxContainer();
		MainVBox.Name = "MainVBox_VBoxContainer";
		MainVBox.AddThemeConstantOverride("separation", 10);
		AddChild(MainVBox);

		HeaderHBox = new HBoxContainer();
		HeaderHBox.Name = "HeaderHBox_HBoxContainer";
		HeaderHBox.AddThemeConstantOverride("separation", 8);
		HeaderHBox.GuiInput += OnHeaderGuiInput;
		HeaderHBox.MouseFilter = MouseFilterEnum.Stop;
		HeaderHBox.MouseDefaultCursorShape = CursorShape.Move;
		MainVBox.AddChild(HeaderHBox);

		TitleLabel = new Label();
		TitleLabel.Name = "TitleLabel_Label";
		TitleLabel.Text = titleText;
		TitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		TitleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		TitleLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		TitleLabel.AddThemeFontSizeOverride("font_size", 15);
		TitleLabel.MouseFilter = MouseFilterEnum.Pass;

		var titleMargin = new MarginContainer();
		titleMargin.Name = "TitleMargin_MarginContainer";
		titleMargin.AddThemeConstantOverride("margin_top", -14);
		titleMargin.AddThemeConstantOverride("margin_bottom", 0);
		titleMargin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		titleMargin.AddChild(TitleLabel);
		HeaderHBox.AddChild(titleMargin);

		CloseButton = new Button();
		CloseButton.Name = "CloseButton_Button";
		CloseButton.Set("icon_max_width", 0);
		CloseButton.Text = "✕";
		CloseButton.CustomMinimumSize = new Vector2(26, 26);
		CloseButton.FocusMode = FocusModeEnum.None;
		CloseButton.Pressed += () => CancelAndClose();

		var closeMargin = new MarginContainer();
		closeMargin.Name = "CloseMargin_MarginContainer";
		closeMargin.AddThemeConstantOverride("margin_right", -35);
		closeMargin.AddThemeConstantOverride("margin_left", 10);
		closeMargin.AddChild(CloseButton);
		HeaderHBox.AddChild(closeMargin);

		BodyContainer = new VBoxContainer();
		BodyContainer.Name = "BodyContainer_VBoxContainer";
		BodyContainer.AddThemeConstantOverride("separation", 8);
		BodyContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		MainVBox.AddChild(BodyContainer);

		FooterHBox = new HBoxContainer();
		FooterHBox.Name = "FooterHBox_HBoxContainer";
		FooterHBox.AddThemeConstantOverride("separation", 12);
		FooterHBox.Alignment = BoxContainer.AlignmentMode.End;
		MainVBox.AddChild(FooterHBox);

		CancelButton = new Button();
		CancelButton.Name = "CancelButton_Button";
		CancelButton.Set("icon_max_width", 0);
		CancelButton.Text = TranslationServer.Translate("CANCEL");
		CancelButton.CustomMinimumSize = new Vector2(90, 30);
		CancelButton.FocusMode = FocusModeEnum.None;
		CancelButton.Pressed += () => CancelAndClose();
		FooterHBox.AddChild(CancelButton);

		ApplyButton = new Button();
		ApplyButton.Name = "ApplyButton_Button";
		ApplyButton.Set("icon_max_width", 0);
		ApplyButton.Text = TranslationServer.Translate("APPLY");
		ApplyButton.CustomMinimumSize = new Vector2(90, 30);
		ApplyButton.FocusMode = FocusModeEnum.None;
		ApplyButton.Pressed += () => ApplyAndClose();
		FooterHBox.AddChild(ApplyButton);
	}

	public void SetFooterCloseOnly(string closeText = "CLOSE")
	{
		if (CancelButton != null) CancelButton.Visible = false;
		if (ApplyButton != null) ApplyButton.Visible = false;

		var btnClose = new Button();
		btnClose.Name = "BtnClose_Button";
		btnClose.Set("icon_max_width", 0);
		btnClose.Text = TranslationServer.Translate(closeText);
		btnClose.CustomMinimumSize = new Vector2(90, 30);
		btnClose.FocusMode = FocusModeEnum.None;
		btnClose.Pressed += () => CloseDialog();
		FooterHBox.AddChild(btnClose);
	}

	protected virtual string GetRandomizeTemplateSlug()
	{
		return string.Empty;
	}

	protected virtual void OnAssetsRandomized()
	{
	}

	protected readonly SessionAssetTracker SessionAssetTracker = new();

	public void CaptureBaselineManifestSnapshot()
	{
		SessionAssetTracker.CaptureBaselineManifestSnapshot();
	}

	public virtual void RandomizeAssets()
	{
		string targetSlug = GetRandomizeTemplateSlug();
		if (string.IsNullOrWhiteSpace(targetSlug))
		{
			return;
		}

		if (SessionAssetTracker.BaselineManifestAssets.Count == 0)
		{
			SessionAssetTracker.CaptureBaselineManifestSnapshot();
		}

		if (SessionAssetTracker.InitialRandomizeTemplateJson == null)
		{
			SessionAssetTracker.RandomizeTemplateSlug = targetSlug;
			SessionAssetTracker.InitialRandomizeTemplateJson = GameContext.EditorService.SnapshotTemplateJson(targetSlug);
		}

		GameContext.EditorService.SnapshotTemplateJson(targetSlug);
		GameContext.EditorService.AssignRandomAssetsToTemplate(targetSlug, overwriteExisting: true);

		SessionAssetTracker.RecordImportedAssets();

		OnAssetsRandomized();
	}

	public void PruneUnreferencedSessionAssets(bool revertSnapshot = false)
	{
		SessionAssetTracker.PruneUnreferencedSessionAssets(revertSnapshot);
	}

	public virtual void SetRandomizeAssetsAction(Action? onRandomize = null)
	{
		var actionToWire = onRandomize ?? RandomizeAssets;

		if (RandomizeAssetsButton == null)
		{
			RandomizeAssetsButton = new Button();
			RandomizeAssetsButton.Name = "RandomizeAssetsButton_Button";
			RandomizeAssetsButton.Set("icon_max_width", 0);
			RandomizeAssetsButton.Text = $"{UnicodeIcons.DICE} " + TranslationServer.Translate("Randomize Assets");
			RandomizeAssetsButton.CustomMinimumSize = new Vector2(160, 28);
			RandomizeAssetsButton.FocusMode = FocusModeEnum.None;
			RandomizeAssetsButton.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
			RandomizeAssetsButton.TooltipText = TranslationServer.Translate("Randomly assign matching indexed assets from storage to this template");

			var faFont = Hud?.GetFontAwesomeFont();
			if (faFont != null)
			{
				RandomizeAssetsButton.AddThemeFontOverride("font", faFont);
			}

			RandomizeAssetsButton.AddThemeFontSizeOverride("font_size", 11);
			RandomizeAssetsButton.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
			RandomizeAssetsButton.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
			RandomizeAssetsButton.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
			RandomizeAssetsButton.AddThemeColorOverride("font_color", UIStyle.ColorGold);

			BodyContainer.AddChild(RandomizeAssetsButton);
			BodyContainer.MoveChild(RandomizeAssetsButton, 0);
		}

		RandomizeAssetsButton.Visible = true;
		var signals = RandomizeAssetsButton.GetSignalConnectionList("pressed");
		foreach (var sig in signals)
		{
			if (sig.TryGetValue("callable", out var c) && c.VariantType == Variant.Type.Callable)
			{
				RandomizeAssetsButton.Disconnect("pressed", c.AsCallable());
			}
		}

		RandomizeAssetsButton.Pressed += () => actionToWire();
	}

	public void SetFooterApplyOnly(string applyText = "APPLY")
	{
		if (CancelButton != null) CancelButton.Visible = false;
		if (ApplyButton != null)
		{
			ApplyButton.Visible = true;
			ApplyButton.Text = TranslationServer.Translate(applyText);
		}
		FooterHBox.Alignment = BoxContainer.AlignmentMode.Center;
	}

	protected TextureRect BackgroundTextureRect;
	protected MarginContainer DialogMarginContainer;

	public void SetUncompressedPanelTexture(string texturePath, int marginTop = 30, int marginBottom = 40, int marginLeft = 50, int marginRight = 50)
	{
		AddThemeStyleboxOverride("panel", new StyleBoxEmpty());

		if (BackgroundTextureRect == null)
		{
			BackgroundTextureRect = new TextureRect();
			BackgroundTextureRect.Name = "BackgroundTextureRect_TextureRect";
			BackgroundTextureRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			BackgroundTextureRect.StretchMode = TextureRect.StretchModeEnum.Scale;
			BackgroundTextureRect.SetAnchorsPreset(LayoutPreset.FullRect);
			AddChild(BackgroundTextureRect);
			MoveChild(BackgroundTextureRect, 0);
		}

		var tex = GD.Load<Texture2D>(texturePath);
		if (tex != null)
		{
			BackgroundTextureRect.Texture = tex;
		}

		if (DialogMarginContainer == null && MainVBox != null && MainVBox.GetParent() == this)
		{
			RemoveChild(MainVBox);
			DialogMarginContainer = new MarginContainer();
			DialogMarginContainer.Name = "DialogMarginContainer_MarginContainer";
			DialogMarginContainer.SetAnchorsPreset(LayoutPreset.FullRect);
			DialogMarginContainer.AddChild(MainVBox);
			AddChild(DialogMarginContainer);
		}

		if (DialogMarginContainer != null)
		{
			DialogMarginContainer.AddThemeConstantOverride("margin_top", marginTop);
			DialogMarginContainer.AddThemeConstantOverride("margin_bottom", marginBottom);
			DialogMarginContainer.AddThemeConstantOverride("margin_left", marginLeft);
			DialogMarginContainer.AddThemeConstantOverride("margin_right", marginRight);
		}
	}

	public override void _Notification(int what)
	{
		base._Notification(what);
		if (what == NotificationPredelete || what == NotificationExitTree)
		{
			_openDialogs.Remove(this);
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (!IsOpen) return;

		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			if (keyEvent.Keycode == Key.Tab)
			{
				CycleFocusToNextInput(keyEvent.ShiftPressed);
				GetViewport().SetInputAsHandled();
			}
		}
	}

	private void CycleFocusToNextInput(bool reverse)
	{
		var inputs = new List<LineEdit>();
		CollectFocusableInputs(this, inputs);
		if (inputs.Count == 0) return;

		Control currentFocus = GetViewport().GuiGetFocusOwner();
		int currentIndex = -1;
		if (currentFocus is LineEdit currentEdit)
		{
			currentIndex = inputs.IndexOf(currentEdit);
		}

		int nextIndex;
		if (currentIndex == -1)
		{
			nextIndex = reverse ? inputs.Count - 1 : 0;
		}
		else
		{
			nextIndex = reverse ? (currentIndex - 1 + inputs.Count) % inputs.Count : (currentIndex + 1) % inputs.Count;
		}

		inputs[nextIndex].GrabFocus();
		inputs[nextIndex].SelectAll();
	}

	private void CollectFocusableInputs(Node node, List<LineEdit> inputs)
	{
		if (node is LineEdit edit && edit.Visible && edit.IsInsideTree() && edit.Editable)
		{
			inputs.Add(edit);
		}

		int count = node.GetChildCount();
		for (int i = 0; i < count; i++)
		{
			CollectFocusableInputs(node.GetChild(i), inputs);
		}
	}

	public virtual void OpenDialog()
	{
		CaptureBaselineManifestSnapshot();

		if (GetParent() == null && Hud != null)
		{
			Hud.AddChild(this);
		}

		if (RandomizeAssetsButton == null && !string.IsNullOrWhiteSpace(GetRandomizeTemplateSlug()))
		{
			SetRandomizeAssetsAction();
		}

		Visible = true;
		MoveToFront();
		_openDialogs.Remove(this);
		_openDialogs.Add(this);

		Vector2 parentSize = Hud != null ? Hud.GetViewportRect().Size : GetViewportRect().Size;
		float dialogWidth = Mathf.Max(Size.X, CustomMinimumSize.X);
		float dialogHeight = Mathf.Max(Size.Y, CustomMinimumSize.Y);
		Position = new Vector2(
			Mathf.Max(20, (parentSize.X - dialogWidth) * 0.5f),
			Mathf.Max(20, (parentSize.Y - dialogHeight) * 0.4f)
		);
	}

	public virtual void ApplyAndClose()
	{
		CommitPendingInputFocus();
		OnApply();
		PruneUnreferencedSessionAssets(revertSnapshot: false);
		CloseDialog();
	}

	protected void CommitPendingInputFocus()
	{
		try
		{
			var focusOwner = GetViewport()?.GuiGetFocusOwner();
			if (focusOwner != null && IsAncestorOf(focusOwner))
			{
				focusOwner.ReleaseFocus();
			}
		}
		catch
		{
		}

		CommitInputsRecursive(this);
	}

	private void CommitInputsRecursive(Node node)
	{
		if (node == null) return;

		if (node is SpinBox spinBox)
		{
			try
			{
				spinBox.Apply();
				var lineEdit = spinBox.GetLineEdit();
				if (lineEdit != null && double.TryParse(lineEdit.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedVal))
				{
					spinBox.Value = Math.Clamp(parsedVal, spinBox.MinValue, spinBox.MaxValue);
				}
			}
			catch
			{
			}
		}
		else if (node is LineEdit lineEdit)
		{
			try
			{
				lineEdit.ReleaseFocus();
			}
			catch
			{
			}
		}

		int count = node.GetChildCount();
		for (int i = 0; i < count; i++)
		{
			CommitInputsRecursive(node.GetChild(i));
		}
	}

	public virtual void CancelAndClose()
	{
		OnCancel();
		PruneUnreferencedSessionAssets(revertSnapshot: true);
		CloseDialog();
	}

	public event Action? DialogClosed;

	public virtual void CloseDialog()
	{
		SessionAssetTracker.Reset();

		_openDialogs.Remove(this);
		Visible = false;
		if (GetParent() != null)
		{
			GetParent().RemoveChild(this);
		}

		DialogClosed?.Invoke();
	}

	protected virtual void OnApply()
	{
	}

	protected virtual void OnCancel()
	{
	}

	private void OnHeaderGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
		{
			_isDragging = mouseButton.Pressed;
			if (_isDragging)
			{
				BringToFront();
				_dragStartMousePosition = mouseButton.GlobalPosition;
				_dragStartPosition = Position;
			}
		}
		else if (@event is InputEventMouseMotion mouseMotion && _isDragging)
		{
			Vector2 delta = mouseMotion.GlobalPosition - _dragStartMousePosition;
			Vector2 newPosition = _dragStartPosition + delta;
			Vector2 viewportSize = GetViewportRect().Size;
			newPosition.X = Mathf.Clamp(newPosition.X, 10, Mathf.Max(10, viewportSize.X - Size.X - 10));
			newPosition.Y = Mathf.Clamp(newPosition.Y, 10, Mathf.Max(10, viewportSize.Y - Size.Y - 10));
			Position = newPosition;
		}
	}

	public Label AddSectionHeader(Control parent, string titleText, Color? color = null)
	{
		var header = new Label();
		header.Name = "Header_Label";
		header.Text = titleText;
		header.AddThemeColorOverride("font_color", color ?? UIStyle.ColorGold);
		header.AddThemeFontSizeOverride("font_size", 12);
		parent.AddChild(header);
		return header;
	}

	public Label AddLabel(Control parent, string text, int fontSize = 11, Color? color = null)
	{
		var label = new Label();
		label.Name = "Label_Label";
		label.Text = text;
		label.AddThemeFontSizeOverride("font_size", fontSize);
		if (color.HasValue)
		{
			label.AddThemeColorOverride("font_color", color.Value);
		}

		parent.AddChild(label);
		return label;
	}

	public Label AddDescription(Control parent, string text)
	{
		var desc = new Label();
		desc.Name = "Desc_Label";
		desc.Text = text;
		desc.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		desc.AddThemeFontSizeOverride("font_size", 11);
		desc.AutowrapMode = TextServer.AutowrapMode.Word;
		desc.CustomMinimumSize = new Vector2(360, 0);
		desc.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		parent.AddChild(desc);
		return desc;
	}

	public (HSlider Slider, Label ValueLabel) AddSlider(
		VBoxContainer parent,
		string labelText,
		float min,
		float max,
		float step,
		float initialValue,
		Action<float> onChanged,
		string format = "0.00",
		float labelWidth = 110.0f)
	{
		var row = new HBoxContainer();
		row.Name = "Row_HBoxContainer";
		row.AddThemeConstantOverride("separation", 6);

		var lbl = new Label();
		lbl.Name = "Lbl_Label";
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		var slider = new HSlider();
		slider.Name = "Slider_HSlider";
		slider.MinValue = min;
		slider.MaxValue = max;
		slider.Step = step;
		slider.Value = initialValue;
		slider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddChild(slider);

		var valLbl = new Label();
		valLbl.Name = "ValLbl_Label";
		valLbl.Text = initialValue.ToString(format);
		valLbl.CustomMinimumSize = new Vector2(45, 0);
		valLbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(valLbl);

		slider.ValueChanged += (double val) =>
		{
			valLbl.Text = ((float)val).ToString(format);
			onChanged((float)val);
		};

		parent.AddChild(row);
		return (slider, valLbl);
	}

	public (ColorPickerButton Picker, HSlider HueSlider) AddColorPicker(
		VBoxContainer parent,
		string labelText,
		Color initialColor,
		Action<Color> onChanged,
		float labelWidth = 110.0f)
	{
		var row = new HBoxContainer();
		row.Name = "Row_HBoxContainer";
		row.AddThemeConstantOverride("separation", 6);

		var lbl = new Label();
		lbl.Name = "Lbl_Label";
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		var hueSlider = new HSlider();
		hueSlider.Name = "HueSlider_HSlider";
		hueSlider.MinValue = 0.0f;
		hueSlider.MaxValue = 1.0f;
		hueSlider.Step = 0.01f;
		hueSlider.SetValueNoSignal(initialColor.H);
		hueSlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddChild(hueSlider);

		var picker = new ColorPickerButton();
		picker.CustomMinimumSize = new Vector2(36, 22);
		picker.EditAlpha = true;
		picker.Color = initialColor;
		row.AddChild(picker);

		bool isInternalSync = false;

		hueSlider.ValueChanged += (double val) =>
		{
			if (isInternalSync) return;
			isInternalSync = true;
			try
			{
				float s = picker.Color.S > 0.01f ? picker.Color.S : 0.85f;
				float v = picker.Color.V > 0.01f ? picker.Color.V : 1.0f;
				Color tintColor = Color.FromHsv((float)val, s, v, picker.Color.A);
				picker.Color = tintColor;
				onChanged(tintColor);
			}
			finally
			{
				isInternalSync = false;
			}
		};

		picker.ColorChanged += (Color color) =>
		{
			if (isInternalSync) return;
			isInternalSync = true;
			try
			{
				hueSlider.SetValueNoSignal(color.H);
				onChanged(color);
			}
			finally
			{
				isInternalSync = false;
			}
		};

		parent.AddChild(row);
		return (picker, hueSlider);
	}

	public CheckBox AddCheckBox(
		VBoxContainer parent,
		string labelText,
		bool initialValue,
		Action<bool> onChanged,
		string tooltip = "")
	{
		var chk = new CheckBox();
		chk.Name = "Chk_CheckBox";
		chk.Text = labelText;
		chk.ButtonPressed = initialValue;
		chk.AddThemeFontSizeOverride("font_size", 11);
		if (!string.IsNullOrEmpty(tooltip)) chk.TooltipText = tooltip;
		chk.Toggled += (pressed) => onChanged(pressed);
		parent.AddChild(chk);
		return chk;
	}

	public OptionButton AddOptionDropdown(
		VBoxContainer parent,
		string labelText,
		string[] options,
		int initialIndex,
		Action<int> onChanged,
		float labelWidth = 110.0f)
	{
		var row = new HBoxContainer();
		row.Name = "Row_HBoxContainer";
		row.AddThemeConstantOverride("separation", 6);

		var lbl = new Label();
		lbl.Name = "Lbl_Label";
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		var opt = new OptionButton();
		opt.Name = "Opt_OptionButton";
		opt.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		opt.AddThemeFontSizeOverride("font_size", 11);
		for (int i = 0; i < options.Length; i++)
		{
			opt.AddItem(options[i], i);
		}

		if (initialIndex >= 0 && initialIndex < options.Length)
		{
			opt.Selected = initialIndex;
		}

		opt.ItemSelected += (long idx) => onChanged((int)idx);
		row.AddChild(opt);

		parent.AddChild(row);
		return opt;
	}

	public LineEdit AddTextInput(
		VBoxContainer parent,
		string labelText,
		string initialText,
		Action<string> onChanged,
		string placeholder = "",
		float labelWidth = 110.0f)
	{
		var row = new HBoxContainer();
		row.Name = "Row_HBoxContainer";
		row.AddThemeConstantOverride("separation", 6);

		var lbl = new Label();
		lbl.Name = "Lbl_Label";
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		var txt = new LineEdit();
		txt.Name = "Txt_LineEdit";
		txt.Text = initialText ?? string.Empty;
		txt.PlaceholderText = placeholder;
		txt.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		txt.AddThemeFontSizeOverride("font_size", 11);
		txt.TextChanged += (val) => onChanged(val);
		row.AddChild(txt);

		parent.AddChild(row);
		return txt;
	}

	public (LineEdit Edit, Action<float> SetValue) AddNumberInput(
		VBoxContainer parent,
		string labelText,
		float initialValue,
		Action<float> onChanged,
		float step = 0.5f,
		string placeholder = "",
		float labelWidth = 110.0f)
	{
		var row = new HBoxContainer();
		row.Name = "Row_HBoxContainer";
		row.AddThemeConstantOverride("separation", 6);

		var lbl = new Label();
		lbl.Name = "Lbl_Label";
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		var txt = new LineEdit();
		txt.Name = "Txt_LineEdit";
		txt.Text = initialValue.ToString("0.##");
		txt.PlaceholderText = placeholder;
		txt.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		txt.AddThemeFontSizeOverride("font_size", 11);
		txt.TextChanged += (val) =>
		{
			if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
			{
				onChanged(parsed);
			}
		};
		row.AddChild(txt);

		parent.AddChild(row);
		return (txt, (val) => txt.Text = val.ToString("0.##"));
	}

	public (LineEdit X, LineEdit Y, LineEdit Z) AddVector3Input(
		VBoxContainer parent,
		string labelText,
		Vector3Data initialValue,
		Action<Vector3Data> onChanged,
		float labelWidth = 110.0f)
	{
		return AddVector3Input(parent, labelText, initialValue.ToGodotVector3(), (v) => onChanged(v.ToVector3Data()), labelWidth);
	}

	public (LineEdit X, LineEdit Y) AddVector2Input(
		VBoxContainer parent,
		string labelText,
		Vector2Data initialValue,
		Action<Vector2Data> onChanged,
		float labelWidth = 110.0f)
	{
		return AddVector2Input(parent, labelText, initialValue.ToGodotVector2(), (v) => onChanged(v.ToVector2Data()), labelWidth);
	}

	public (LineEdit X, LineEdit Y, LineEdit Z) AddVector3Input(
		VBoxContainer parent,
		string labelText,
		Vector3 initialValue,
		Action<Vector3> onChanged,
		float labelWidth = 110.0f)
	{
		var row = new HBoxContainer();
		row.Name = "Row_HBoxContainer";
		row.AddThemeConstantOverride("separation", 6);

		var lbl = new Label();
		lbl.Name = "Lbl_Label";
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		Vector3 current = initialValue;

		var txtX = new LineEdit { Text = initialValue.X.ToString("0.##"), PlaceholderText = "X", SizeFlagsHorizontal = SizeFlags.ExpandFill };
		var txtY = new LineEdit { Text = initialValue.Y.ToString("0.##"), PlaceholderText = "Y", SizeFlagsHorizontal = SizeFlags.ExpandFill };
		var txtZ = new LineEdit { Text = initialValue.Z.ToString("0.##"), PlaceholderText = "Z", SizeFlagsHorizontal = SizeFlags.ExpandFill };
		txtX.AddThemeFontSizeOverride("font_size", 11);
		txtY.AddThemeFontSizeOverride("font_size", 11);
		txtZ.AddThemeFontSizeOverride("font_size", 11);

		void UpdateVal()
		{
			float.TryParse(txtX.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float x);
			float.TryParse(txtY.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float y);
			float.TryParse(txtZ.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float z);
			current = new Vector3(x, y, z);
			onChanged(current);
		}

		txtX.TextChanged += (_) => UpdateVal();
		txtY.TextChanged += (_) => UpdateVal();
		txtZ.TextChanged += (_) => UpdateVal();

		row.AddChild(txtX);
		row.AddChild(txtY);
		row.AddChild(txtZ);

		parent.AddChild(row);
		return (txtX, txtY, txtZ);
	}

	public (LineEdit X, LineEdit Y) AddVector2Input(
		VBoxContainer parent,
		string labelText,
		Vector2 initialValue,
		Action<Vector2> onChanged,
		float labelWidth = 110.0f)
	{
		var row = new HBoxContainer();
		row.Name = "Row_HBoxContainer";
		row.AddThemeConstantOverride("separation", 6);

		var lbl = new Label();
		lbl.Name = "Lbl_Label";
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		Vector2 current = initialValue;

		var txtX = new LineEdit { Text = initialValue.X.ToString("0.##"), PlaceholderText = "X", SizeFlagsHorizontal = SizeFlags.ExpandFill };
		var txtY = new LineEdit { Text = initialValue.Y.ToString("0.##"), PlaceholderText = "Y", SizeFlagsHorizontal = SizeFlags.ExpandFill };
		txtX.AddThemeFontSizeOverride("font_size", 11);
		txtY.AddThemeFontSizeOverride("font_size", 11);

		void UpdateVal()
		{
			float.TryParse(txtX.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float x);
			float.TryParse(txtY.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float y);
			current = new Vector2(x, y);
			onChanged(current);
		}

		txtX.TextChanged += (_) => UpdateVal();
		txtY.TextChanged += (_) => UpdateVal();

		row.AddChild(txtX);
		row.AddChild(txtY);

		parent.AddChild(row);
		return (txtX, txtY);
	}


	public (LineEdit Input, Action<string> SetValue) AddAssetFilterDropdown(
		Control parent,
		string labelText,
		string initialText,
		Func<bool, List<string>> itemsProvider,
		Action<string> onChanged,
		string placeholder = "",
		float labelWidth = 140.0f,
		bool hasAllFoldersCheckbox = false,
		Action<string> onPlaySound = null)
	{
		var row = new HBoxContainer();
		row.Name = "Row_HBoxContainer";
		row.AddThemeConstantOverride("separation", 4);

		var lbl = new Label();
		lbl.Name = "Lbl_Label";
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		var state = new AssetFilterDropdownState
		{
			CommittedValue = initialText ?? string.Empty,
			ItemsProvider = itemsProvider,
			OnChanged = onChanged,
			Txt = new LineEdit(),
			Popup = new PopupMenu()
		};

		state.Txt.Text = state.CommittedValue;
		state.Txt.PlaceholderText = placeholder;
		state.Txt.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		state.Txt.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(state.Txt);

		var btnDropdown = new Button();
		btnDropdown.Name = "BtnDropdown_Button";
		btnDropdown.Set("icon_max_width", 0);
		btnDropdown.Text = "▼";
		btnDropdown.CustomMinimumSize = new Vector2(24, 22);
		btnDropdown.AddThemeFontSizeOverride("font_size", 10);
		btnDropdown.FocusMode = FocusModeEnum.None;
		btnDropdown.TooltipText = TranslationServer.Translate("Select from imported assets");
		row.AddChild(btnDropdown);

		AddAssetFilterAllFoldersCheckbox(row, state, hasAllFoldersCheckbox);
		AddAssetFilterPlaySoundButton(row, state, onPlaySound);

		state.Popup.AddThemeFontSizeOverride("font_size", 11);
		state.Popup.Unfocusable = true;
		AddChild(state.Popup);

		btnDropdown.Pressed += () => HandleAssetDropdownPressed(state);
		state.Popup.PopupHide += () => state.IsShowingAll = false;
		state.Popup.IdPressed += (long id) => HandleAssetPopupIdPressed(state, id);

		state.Txt.FocusEntered += () => Callable.From(state.Txt.SelectAll).CallDeferred();
		state.Txt.TextChanged += (val) =>
		{
			if (!state.IsUpdatingTextProgrammatically)
				state.ShowAssetPopup(showAll: false);
		};
		state.Txt.FocusExited += () => HandleAssetTxtFocusExited(state);
		state.Txt.TextSubmitted += (newText) =>
		{
			state.ResolveClosestMatch();
			state.Txt.ReleaseFocus();
		};

		parent.AddChild(row);
		return (state.Txt, (val) => SetAssetDropdownValue(state, val));
	}

	private void AddAssetFilterAllFoldersCheckbox(HBoxContainer row, AssetFilterDropdownState state, bool hasAllFoldersCheckbox)
	{
		if (!hasAllFoldersCheckbox) return;

		var chkAll = new CheckBox();
		chkAll.Name = "ChkAll_CheckBox";
		chkAll.Text = TranslationServer.Translate("All folders");
		chkAll.AddThemeFontSizeOverride("font_size", 10);
		chkAll.TooltipText = TranslationServer.Translate("Include models outside of projectiles folder");
		chkAll.Toggled += (pressed) => state.IncludeAllFolders = pressed;
		row.AddChild(chkAll);
	}

	private void AddAssetFilterPlaySoundButton(HBoxContainer row, AssetFilterDropdownState state, Action<string> onPlaySound)
	{
		if (onPlaySound == null) return;

		var btnPlay = new Button();
		btnPlay.Name = "BtnPlay_Button";
		btnPlay.Set("icon_max_width", 0);
		btnPlay.Text = "▶";
		btnPlay.CustomMinimumSize = new Vector2(26, 22);
		btnPlay.AddThemeFontSizeOverride("font_size", 11);
		btnPlay.FocusMode = FocusModeEnum.None;
		btnPlay.TooltipText = TranslationServer.Translate("Preview Sound");
		btnPlay.Pressed += () => onPlaySound(state.Txt.Text);
		row.AddChild(btnPlay);
	}

	private void HandleAssetDropdownPressed(AssetFilterDropdownState state)
	{
		if (state.Popup.Visible && state.IsShowingAll)
		{
			state.Popup.Hide();
			state.IsShowingAll = false;
		}
		else
		{
			state.ShowAssetPopup(showAll: true);
		}
	}

	private void HandleAssetPopupIdPressed(AssetFilterDropdownState state, long id)
	{
		int idx = (int)id;
		if (idx < 0 || idx >= state.CurrentFilteredItems.Count) return;

		string selected = state.CurrentFilteredItems[idx];
		state.IsPopupSelectionInProgress = true;
		state.CommitSelection(selected);
		state.Popup.Hide();
		Callable.From(() => state.IsPopupSelectionInProgress = false).CallDeferred();
	}

	private void HandleAssetTxtFocusExited(AssetFilterDropdownState state)
	{
		if (state.IsPopupOpening) return;

		Callable.From(() =>
		{
			if (state.IsPopupSelectionInProgress) return;
			if (string.Equals(state.Txt.Text, state.CommittedValue, StringComparison.Ordinal))
			{
				state.Popup.Hide();
				return;
			}

			state.ResolveClosestMatch();
		}).CallDeferred();
	}

	private void SetAssetDropdownValue(AssetFilterDropdownState state, string val)
	{
		string safeVal = val ?? string.Empty;
		state.CommittedValue = safeVal;
		state.IsUpdatingTextProgrammatically = true;
		try
		{
			state.Txt.Text = safeVal;
		}
		finally
		{
			state.IsUpdatingTextProgrammatically = false;
		}
	}

	public static string FindClosestMatch(string query, List<string> allItems, string fallbackValue = "")
	{
		if (allItems == null || allItems.Count == 0)
		{
			return fallbackValue ?? string.Empty;
		}

		if (string.IsNullOrWhiteSpace(query))
		{
			if (!string.IsNullOrEmpty(fallbackValue) && allItems.Exists(s => s.Equals(fallbackValue, StringComparison.OrdinalIgnoreCase)))
			{
				return fallbackValue;
			}

			return allItems[0];
		}

		string cleanQuery = query.Trim();
		string bestItem = allItems[0];
		int bestScore = int.MaxValue;
		var lev = new Levenshtein(cleanQuery.ToLowerInvariant());

		for (int i = 0; i < allItems.Count; i++)
		{
			string item = allItems[i];
			int score = ComputeMatchScore(item, cleanQuery, lev);
			if (score < bestScore)
			{
				bestScore = score;
				bestItem = item;
				if (score == 0) break;
			}
		}

		return bestItem;
	}

	public static int ComputeMatchScore(string item, string query, Levenshtein? lev = null)
	{
		if (string.IsNullOrEmpty(item)) return int.MaxValue;
		if (string.IsNullOrEmpty(query)) return 0;

		string itemFull = item.Trim();
		string itemLower = itemFull.ToLowerInvariant();
		string q = query.Trim().ToLowerInvariant();

		if (itemLower == q) return 0;

		string fileName = Path.GetFileName(itemFull).ToLowerInvariant();
		if (fileName == q) return 1;

		string nameNoExt = Path.GetFileNameWithoutExtension(itemFull).ToLowerInvariant();
		if (nameNoExt == q) return 2;

		int score = CheckPrefixMatches(q, itemLower, fileName, nameNoExt);
		if (score != -1) return score;

		score = CheckWordMatches(q, itemLower, nameNoExt);
		if (score != -1) return score;

		string[] tokens = nameNoExt.Split(new[] { '_', '-', ' ', '.', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
		score = CheckTokenMatches(q, nameNoExt, tokens);
		if (score != -1) return score;

		score = CheckContainsMatches(q, itemLower, fileName, nameNoExt);
		if (score != -1) return score;

		return ComputeDistanceMatches(q, nameNoExt, tokens, lev);
	}

	private static int CheckPrefixMatches(string q, string itemLower, string fileName, string nameNoExt)
	{
		if (nameNoExt.StartsWith(q)) return 10 + (nameNoExt.Length - q.Length);
		if (fileName.StartsWith(q)) return 20 + (fileName.Length - q.Length);
		if (itemLower.StartsWith(q)) return 30 + (itemLower.Length - q.Length);
		return -1;
	}

	private static int CheckWordMatches(string q, string itemLower, string nameNoExt)
	{
		string[] queryWords = q.Split(new[] { ' ', '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
		if (queryWords.Length <= 1) return -1;

		for (int i = 0; i < queryWords.Length; i++)
		{
			if (!itemLower.Contains(queryWords[i]))
			{
				return -1;
			}
		}

		return 25 + Math.Abs(nameNoExt.Length - q.Length);
	}

	private static int CheckTokenMatches(string q, string nameNoExt, string[] tokens)
	{
		for (int i = 0; i < tokens.Length; i++)
		{
			string token = tokens[i];
			if (token == q) return 40 + (nameNoExt.Length - q.Length);
			if (token.StartsWith(q)) return 50 + (nameNoExt.Length - q.Length);
		}

		return -1;
	}

	private static int CheckContainsMatches(string q, string itemLower, string fileName, string nameNoExt)
	{
		if (nameNoExt.Contains(q)) return 100 + nameNoExt.IndexOf(q) * 5 + (nameNoExt.Length - q.Length);
		if (fileName.Contains(q)) return 200 + fileName.IndexOf(q) * 5 + (fileName.Length - q.Length);
		if (itemLower.Contains(q)) return 300 + itemLower.IndexOf(q) * 2 + (itemLower.Length - q.Length);
		return -1;
	}

	private static int ComputeDistanceMatches(string q, string nameNoExt, string[] tokens, Levenshtein? lev)
	{
		int bestTokenDist = int.MaxValue;
		for (int i = 0; i < tokens.Length; i++)
		{
			int dist = lev != null ? lev.DistanceFrom(tokens[i]) : Levenshtein.Distance(q, tokens[i]);
			if (dist < bestTokenDist) bestTokenDist = dist;
		}

		if (bestTokenDist <= 2)
		{
			return 500 + bestTokenDist * 50 + Math.Abs(nameNoExt.Length - q.Length);
		}

		int distName = lev != null ? lev.DistanceFrom(nameNoExt) : Levenshtein.Distance(q, nameNoExt);
		return 1000 + distName * 20 + Math.Abs(nameNoExt.Length - q.Length);
	}

	public static List<string> ScanAvailableAssets(string category, bool includeAllFolders = false, string subFolder = null)
	{
		var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string wsPath = !string.IsNullOrEmpty(MapWorkspaceService.GetActiveWorkspacePath())
			? MapWorkspaceService.GetActiveWorkspacePath()
			: ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath);

		try
		{
			var assetsObj = MapAssetHelper.LoadAssets(wsPath);
			string manifestPath = Path.Combine(wsPath, "manifest.json");
			MapManifest manifest = LoadManifest(manifestPath);

			if (assetsObj == null && manifest == null) return new List<string>();

			ScanCategoryAssets(category, result, assetsObj, manifest, wsPath, includeAllFolders, subFolder);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[FloatingDialogBase] ScanAvailableAssets metadata error: {ex.Message}");
		}

		var list = new List<string>(result);
		list.Sort(StringComparer.OrdinalIgnoreCase);
		return list;
	}

	private static MapManifest LoadManifest(string manifestPath)
	{
		if (!File.Exists(manifestPath)) return null;
		try
		{
			return MapManifest.LoadFromFile(manifestPath);
		}
		catch
		{
			return null;
		}
	}

	private static void ScanCategoryAssets(string category, HashSet<string> result, MapManifestAssets assetsObj, MapManifest manifest, string wsPath, bool includeAllFolders, string subFolder)
	{
		if (TryScanMediaAssets(category, result, assetsObj, manifest, wsPath)) return;
		if (TryScanVisualAssets(category, result, assetsObj, manifest, wsPath, includeAllFolders, subFolder)) return;
		if (TryScanEnvironmentAssets(category, result, assetsObj, manifest, wsPath)) return;
	}

	private static bool TryScanMediaAssets(string category, HashSet<string> result, MapManifestAssets assetsObj, MapManifest manifest, string wsPath)
	{
		if (category is "audio" or "sound" or "sfx" or "music")
		{
			ScanAudioAssets(result, assetsObj, manifest, wsPath);
			return true;
		}

		if (category is "spritesheets" or "spritesheet" or "spritesheet_rtex")
		{
			ScanSpritesheetAssets(result, assetsObj, manifest, wsPath);
			return true;
		}

		if (category is "vfx" or "vfx_spritesheets" or "vfx_radial" or "vfx_vertical")
		{
			ScanVfxAssets(category, result, assetsObj, wsPath);
			return true;
		}

		if (category is "decals" or "decal")
		{
			ScanDecalAssets(result, assetsObj, manifest, wsPath);
			return true;
		}

		return false;
	}

	private static bool TryScanVisualAssets(string category, HashSet<string> result, MapManifestAssets assetsObj, MapManifest manifest, string wsPath, bool includeAllFolders, string subFolder)
	{
		if (category is "models" or "glb" or "attachments" or "items" or "weapons" or "projectiles")
		{
			ScanModelAssets(category, result, assetsObj, manifest, wsPath, includeAllFolders, subFolder);
			return true;
		}

		if (category is "ribbons" or "ribbon_textures")
		{
			ScanRibbonAssets(result, assetsObj, manifest, wsPath);
			return true;
		}

		if (category is "animations" or "ranim" or "animation")
		{
			ScanAnimationAssets(result, assetsObj, wsPath);
			return true;
		}

		return false;
	}

	private static bool TryScanEnvironmentAssets(string category, HashSet<string> result, MapManifestAssets assetsObj, MapManifest manifest, string wsPath)
	{
		if (category is "icons" or "icon")
		{
			ScanIconAssets(result, assetsObj, manifest, wsPath);
			return true;
		}

		if (category is "noise" or "noise_textures")
		{
			ScanNoiseAssets(result, assetsObj, manifest, wsPath);
			return true;
		}

		if (category is "textures" or "terrain")
		{
			ScanTerrainAssets(result, assetsObj, manifest, wsPath);
			return true;
		}

		return false;
	}

	private static void ScanAudioAssets(HashSet<string> result, MapManifestAssets assetsObj, MapManifest manifest, string wsPath)
	{
		ScanAudioAssetsFromCategories(result, assetsObj);
		ScanAudioAssetsFromManifestFiles(result, manifest);
		ScanAudioAssetsFromDirectory(result, wsPath);
	}

	private static void ScanAudioAssetsFromCategories(HashSet<string> result, MapManifestAssets assetsObj)
	{
		foreach (var key in new[] { "SoundEffect", "Music" })
		{
			var catDict = assetsObj?.GetCategory(key);
			if (catDict == null) continue;

			foreach (var kvp in catDict)
			{
				if (IsAudioAsset(kvp.Key))
				{
					result.Add(Path.GetFileName(kvp.Key));
				}
			}
		}
	}

	private static void ScanAudioAssetsFromManifestFiles(HashSet<string> result, MapManifest manifest)
	{
		if (manifest?.Files == null) return;

		foreach (var kvp in manifest.Files)
		{
			if (IsAudioAsset(kvp.Key))
			{
				result.Add(Path.GetFileName(kvp.Key));
			}
		}
	}

	private static void ScanAudioAssetsFromDirectory(HashSet<string> result, string wsPath)
	{
		string audioDir = Path.Combine(wsPath, "Assets", "audio");
		if (!Directory.Exists(audioDir)) return;

		foreach (var file in Directory.EnumerateFiles(audioDir, "*.*", SearchOption.AllDirectories))
		{
			if (IsAudioAsset(file))
			{
				result.Add(Path.GetFileName(file));
			}
		}
	}

	private static bool IsAudioAsset(string path)
	{
		if (string.IsNullOrWhiteSpace(path)) return false;
		return path.EndsWith(".raud", StringComparison.OrdinalIgnoreCase) ||
		       path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
		       path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
	}

	private static void ScanSpritesheetAssets(HashSet<string> result, MapManifestAssets assetsObj, MapManifest manifest, string wsPath)
	{
		ScanRtexAssetsFromCategory(result, assetsObj?.GetCategory("Spritesheet") ?? assetsObj?.GetCategory("spritesheets"));
		ScanRtexAssetsFromCategory(result, manifest?.Assets?.Spritesheet);
		ScanSpritesheetAssetsFromManifestFiles(result, manifest);
		ScanSpritesheetAssetsFromMetadata(result, wsPath);
		ScanRtexAssetsFromDirectory(result, Path.Combine(wsPath, "Assets", "vfx"));
		ScanRtexAssetsFromDirectory(result, Path.Combine(ProjectSettings.GlobalizePath("res://"), "Assets", "vfx"));
	}

	private static void ScanRtexAssetsFromCategory(HashSet<string> result, IDictionary<string, string> dict)
	{
		if (dict == null) return;
		foreach (var kvp in dict)
		{
			if (IsRtexAsset(kvp.Key))
				result.Add(Path.GetFileName(kvp.Key));
		}
	}

	private static void ScanRtexAssetsFromDirectory(HashSet<string> result, string dirPath)
	{
		if (!Directory.Exists(dirPath)) return;
		foreach (var file in Directory.EnumerateFiles(dirPath, "*.rtex", SearchOption.AllDirectories))
		{
			result.Add(Path.GetFileName(file));
		}
	}

	private static bool IsRtexAsset(string path)
	{
		return !string.IsNullOrWhiteSpace(path) && path.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase);
	}

	private static void ScanSpritesheetAssetsFromManifestFiles(HashSet<string> result, MapManifest manifest)
	{
		if (manifest?.Files == null) return;

		foreach (var kvp in manifest.Files)
		{
			if (IsRtexAsset(kvp.Key) &&
			    (kvp.Key.StartsWith("Assets/vfx/spritesheets/", StringComparison.OrdinalIgnoreCase) ||
			     kvp.Key.StartsWith("Assets/spritesheets/", StringComparison.OrdinalIgnoreCase) ||
			     kvp.Key.StartsWith("Assets/vfx/", StringComparison.OrdinalIgnoreCase)))
			{
				result.Add(Path.GetFileName(kvp.Key));
			}
		}
	}

	private static void ScanSpritesheetAssetsFromMetadata(HashSet<string> result, string wsPath)
	{
		if (GameContext.MetadataService.TryLoadMetadata(wsPath, out var ssMetaRoot) && ssMetaRoot?.VfxSpritesheets != null)
		{
			foreach (var kvp in ssMetaRoot.VfxSpritesheets)
			{
				if (IsRtexAsset(kvp.Value?.TexturePath))
				{
					result.Add(Path.GetFileName(kvp.Value.TexturePath));
				}
			}
		}
	}

	private static void ScanVfxAssets(string category, HashSet<string> result, MapManifestAssets assetsObj, string wsPath)
	{
		ScanVfxAssetsFromRegistry(category, result);
		ScanVfxAssetsFromCategories(category, result, assetsObj);
		ScanVfxAssetsFromMetadata(result, wsPath);
	}

	private static void ScanVfxAssetsFromRegistry(string category, HashSet<string> result)
	{
		if (category is not ("vfx" or "vfx_spritesheets")) return;
		if (GameContext.RegistryService.VfxRegistry == null) return;

		foreach (var kvp in GameContext.RegistryService.VfxRegistry)
		{
			result.Add(kvp.Key.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase) ? kvp.Key : $"vfx:{kvp.Key}");
		}
	}

	private static void ScanVfxAssetsFromCategories(string category, HashSet<string> result, MapManifestAssets assetsObj)
	{
		string[] searchKeys = category switch
		{
			"vfx_radial" => new[] { "vfx_radial", "Spritesheet" },
			"vfx_vertical" => new[] { "vfx_vertical", "Spritesheet" },
			_ => new[] { "Spritesheet", "vfx_radial", "vfx_vertical" }
		};

		foreach (var key in searchKeys)
		{
			ScanRtexAssetsFromCategory(result, assetsObj?.GetCategory(key));
		}
	}

	private static void ScanVfxAssetsFromMetadata(HashSet<string> result, string wsPath)
	{
		if (!GameContext.MetadataService.TryLoadMetadata(wsPath, out var vfxMetaRoot)) return;
		if (vfxMetaRoot?.Templates?.Vfx == null) return;

		foreach (var cv in vfxMetaRoot.Templates.Vfx)
		{
			if (string.IsNullOrWhiteSpace(cv.VfxId)) continue;

			result.Add(cv.VfxId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase) ? cv.VfxId : $"vfx:{cv.VfxId}");
			result.Add(cv.VfxId);
		}
	}

	private static void ScanDecalAssets(HashSet<string> result, MapManifestAssets assetsObj, MapManifest manifest, string wsPath)
	{
		ScanRtexAssetsFromCategory(result, assetsObj?.GetCategory("Decal"));
		ScanRtexAssetsFromCategory(result, manifest?.Assets?.Decal);
		ScanDecalAssetsFromManifestFiles(result, manifest);
		ScanRtexAssetsFromDirectory(result, Path.Combine(wsPath, "Assets", "decals"));
		ScanRtexAssetsFromDirectory(result, Path.Combine(ProjectSettings.GlobalizePath("res://"), "Assets", "decals"));
	}

	private static void ScanDecalAssetsFromManifestFiles(HashSet<string> result, MapManifest manifest)
	{
		if (manifest?.Files == null) return;

		foreach (var kvp in manifest.Files)
		{
			if (IsRtexAsset(kvp.Key) &&
			    (kvp.Key.StartsWith("Assets/decals/", StringComparison.OrdinalIgnoreCase) ||
			     kvp.Key.StartsWith("assets/decals/", StringComparison.OrdinalIgnoreCase) ||
			     kvp.Key.StartsWith("decals/", StringComparison.OrdinalIgnoreCase)))
			{
				result.Add(Path.GetFileName(kvp.Key));
			}
		}
	}

	private static void ScanModelAssets(string category, HashSet<string> result, MapManifestAssets assetsObj, MapManifest manifest, string wsPath, bool includeAllFolders, string subFolder)
	{
		ScanModelAssetsFromRegistry(category, result, includeAllFolders);
		ScanModelAssetsFromCategories(category, result, assetsObj, includeAllFolders, subFolder);
		ScanModelAssetsFromManifestFiles(result, manifest);
		ScanModelAssetsFromDirectory(result, Path.Combine(wsPath, "Assets", "models"));
	}

	private static void ScanModelAssetsFromRegistry(string category, HashSet<string> result, bool includeAllFolders)
	{
		if (!(category is "attachments" or "items" || includeAllFolders)) return;
		if (GameContext.RegistryService.VfxRegistry == null) return;

		foreach (var kvp in GameContext.RegistryService.VfxRegistry)
		{
			result.Add(kvp.Key.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase) ? kvp.Key : $"vfx:{kvp.Key}");
		}
	}

	private static void ScanModelAssetsFromCategories(string category, HashSet<string> result, MapManifestAssets assetsObj, bool includeAllFolders, string subFolder)
	{
		string defaultFolder = !string.IsNullOrEmpty(subFolder) ? subFolder : (category is "attachments" or "items" ? "items" : "projectiles");

		foreach (var catName in new[] { "Character", "Building", "Prop", "Item" })
		{
			if (IsModelCategoryMatch(catName, defaultFolder, includeAllFolders))
			{
				ScanRmeshAssetsFromCategory(result, assetsObj?.GetCategory(catName));
			}
		}
	}

	private static bool IsModelCategoryMatch(string catName, string defaultFolder, bool includeAllFolders)
	{
		return includeAllFolders || catName.Equals(defaultFolder, StringComparison.OrdinalIgnoreCase) ||
		       (defaultFolder == "items" && catName == "Item") ||
		       (defaultFolder == "units" && catName == "Character") ||
		       (defaultFolder == "buildings" && catName == "Building") ||
		       (defaultFolder == "props" && catName == "Prop");
	}

	private static void ScanRmeshAssetsFromCategory(HashSet<string> result, IDictionary<string, string> dict)
	{
		if (dict == null) return;
		foreach (var kvp in dict)
		{
			if (IsRmeshAsset(kvp.Key))
				result.Add(Path.GetFileName(kvp.Key));
		}
	}

	private static bool IsRmeshAsset(string path)
	{
		return !string.IsNullOrWhiteSpace(path) && path.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase);
	}

	private static void ScanModelAssetsFromManifestFiles(HashSet<string> result, MapManifest manifest)
	{
		if (manifest?.Files == null) return;

		foreach (var kvp in manifest.Files)
		{
			if (IsRmeshAsset(kvp.Key))
			{
				result.Add(Path.GetFileName(kvp.Key));
			}
		}
	}

	private static void ScanModelAssetsFromDirectory(HashSet<string> result, string dirPath)
	{
		if (!Directory.Exists(dirPath)) return;

		foreach (var file in Directory.EnumerateFiles(dirPath, "*.rmesh", SearchOption.AllDirectories))
		{
			result.Add(Path.GetFileName(file));
		}
	}

	private static void ScanRibbonAssets(HashSet<string> result, MapManifestAssets assetsObj, MapManifest manifest, string wsPath)
	{
		ScanRtexAssetsFromCategory(result, assetsObj?.GetCategory("Ribbon"));
		ScanRtexAssetsFromCategory(result, manifest?.Assets?.Ribbon);
		ScanRtexAssetsFromDirectory(result, Path.Combine(wsPath, "Assets", "ribbons"));
	}

	private static void ScanAnimationAssets(HashSet<string> result, MapManifestAssets assetsObj, string wsPath)
	{
		var aDict = assetsObj?.GetCategory("Animation");
		if (aDict != null)
		{
			foreach (var kvp in aDict)
			{
				if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Key.EndsWith(".ranim", StringComparison.OrdinalIgnoreCase))
				{
					result.Add(Path.GetFileName(kvp.Key));
				}
			}
		}

		string animsDir = Path.Combine(wsPath, "Assets", "animations");
		if (Directory.Exists(animsDir))
		{
			foreach (var file in Directory.EnumerateFiles(animsDir, "*.ranim", SearchOption.AllDirectories))
			{
				result.Add(Path.GetFileName(file));
			}
		}
	}

	private static void ScanIconAssets(HashSet<string> result, MapManifestAssets assetsObj, MapManifest manifest, string wsPath)
	{
		ScanRtexAssetsFromCategory(result, assetsObj?.GetCategory("Icon") ?? assetsObj?.GetCategory("icons"));
		ScanRtexAssetsFromCategory(result, manifest?.Assets?.Icon);
		ScanIconAssetsFromManifestFiles(result, manifest);
		ScanIconAssetsFromMetadata(result, wsPath);
		ScanRtexAssetsFromDirectory(result, Path.Combine(wsPath, "Assets", "icons"));
		ScanRtexAssetsFromDirectory(result, Path.Combine(ProjectSettings.GlobalizePath("res://"), "Assets", "icons"));
	}

	private static void ScanIconAssetsFromManifestFiles(HashSet<string> result, MapManifest manifest)
	{
		if (manifest?.Files == null) return;

		foreach (var kvp in manifest.Files)
		{
			if (IsRtexAsset(kvp.Key) &&
			    (kvp.Key.StartsWith("Assets/icons/", StringComparison.OrdinalIgnoreCase) ||
			     kvp.Key.StartsWith("assets/icons/", StringComparison.OrdinalIgnoreCase) ||
			     kvp.Key.StartsWith("icons/", StringComparison.OrdinalIgnoreCase)))
			{
				result.Add(Path.GetFileName(kvp.Key));
			}
		}
	}

	private static void ScanIconAssetsFromMetadata(HashSet<string> result, string wsPath)
	{
		if (GameContext.MetadataService.TryLoadMetadata(wsPath, out var iconMetaRoot) && iconMetaRoot?.Icons != null)
		{
			foreach (var kvp in iconMetaRoot.Icons)
			{
				if (kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
				{
					result.Add(Path.GetFileName(kvp.Key));
				}
			}
		}
	}

	private static void ScanNoiseAssets(HashSet<string> result, MapManifestAssets assetsObj, MapManifest manifest, string wsPath)
	{
		ScanRtexAssetsFromCategory(result, assetsObj?.GetCategory("Noise"));
		ScanRtexAssetsFromCategory(result, manifest?.Assets?.Noise);
		ScanRtexAssetsFromDirectory(result, Path.Combine(wsPath, "Assets", "noise"));
	}

	private static void ScanTerrainAssets(HashSet<string> result, MapManifestAssets assetsObj, MapManifest manifest, string wsPath)
	{
		ScanRtexAssetsFromCategory(result, assetsObj?.GetCategory("Terrain") ?? assetsObj?.GetCategory("textures"));
		ScanRtexAssetsFromCategory(result, manifest?.Assets?.Terrain);
		ScanTerrainAssetsFromManifestFiles(result, manifest);
		ScanRtexAssetsFromDirectory(result, Path.Combine(wsPath, "Assets", "textures"));
		ScanRtexAssetsFromDirectory(result, Path.Combine(ProjectSettings.GlobalizePath("res://"), "Assets", "textures"));
	}

	private static void ScanTerrainAssetsFromManifestFiles(HashSet<string> result, MapManifest manifest)
	{
		if (manifest?.Files == null) return;

		foreach (var kvp in manifest.Files)
		{
			if (IsRtexAsset(kvp.Key) &&
			    (kvp.Key.StartsWith("Assets/textures/", StringComparison.OrdinalIgnoreCase) ||
			     kvp.Key.StartsWith("assets/textures/", StringComparison.OrdinalIgnoreCase) ||
			     kvp.Key.StartsWith("textures/", StringComparison.OrdinalIgnoreCase)))
			{
				result.Add(Path.GetFileName(kvp.Key));
			}
		}
	}


	public Button AddButton(
		Control parent,
		string text,
		Action onClick,
		string tooltip = "",
		int fontSize = 11,
		Vector2? minSize = null)
	{
		var btn = new Button();
		btn.Name = "Btn_Button";
		btn.Set("icon_max_width", 0);
		btn.Text = text;
		var font = Hud?.GetFontAwesomeFont();
		if (font != null)
		{
			btn.AddThemeFontOverride("font", font);
		}

		btn.AddThemeFontSizeOverride("font_size", fontSize);
		btn.FocusMode = FocusModeEnum.None;
		if (minSize.HasValue)
		{
			btn.CustomMinimumSize = minSize.Value;
		}

		if (!string.IsNullOrEmpty(tooltip))
		{
			btn.TooltipText = TranslationServer.Translate(tooltip);
		}

		btn.Pressed += onClick;
		parent.AddChild(btn);
		return btn;
	}

	public SubViewportContainer Add3DViewportContainer(
		Control parent,
		Vector2 minSize,
		out SubViewport subViewport,
		out Camera3D camera,
		out DirectionalLight3D light)
	{
		var container = new SubViewportContainer();
		container.CustomMinimumSize = minSize;
		container.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		container.Stretch = true;

		subViewport = new SubViewport();
		subViewport.Name = "SubViewport_SubViewport";
		subViewport.Size = new Vector2I((int)minSize.X, (int)minSize.Y);
		subViewport.TransparentBg = false;
		subViewport.OwnWorld3D = true;

		var world = new World3D();
		var env = new Environment();
		env.BackgroundMode = Environment.BGMode.Color;
		env.BackgroundColor = new Color(0.11f, 0.13f, 0.17f);
		env.AmbientLightSource = Environment.AmbientSource.Color;
		env.AmbientLightColor = new Color(0.60f, 0.60f, 0.65f);
		env.AmbientLightEnergy = 0.75f;
		world.Environment = env;
		subViewport.World3D = world;

		light = new DirectionalLight3D();
		light.Name = "Light_DirectionalLight3D";
		light.RotationDegrees = new Vector3(-30, 30, 0);
		light.LightEnergy = 0.75f;
		subViewport.AddChild(light);

		camera = new Camera3D();
		camera.Name = "Camera_Camera3D";
		camera.Position = new Vector3(0, 1.2f, 2.8f);
		subViewport.AddChild(camera);

		container.AddChild(subViewport);
		parent.AddChild(container);
		return container;
	}

	public VBoxContainer CreateScrollBody(int minHeight = 350)
	{
		var scroll = new ScrollContainer();
		scroll.Name = "Scroll_ScrollContainer";
		scroll.CustomMinimumSize = new Vector2(0, minHeight);
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

		var innerVBox = new VBoxContainer();
		innerVBox.Name = "InnerVBox_VBoxContainer";
		innerVBox.AddThemeConstantOverride("separation", 8);
		innerVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		innerVBox.SizeFlagsVertical = SizeFlags.ExpandFill;

		scroll.AddChild(innerVBox);
		BodyContainer.AddChild(scroll);
		return innerVBox;
	}
}