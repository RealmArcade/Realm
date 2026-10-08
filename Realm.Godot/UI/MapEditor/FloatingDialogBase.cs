using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using Realm.Godot.Services;
using Realm.Godot.VFX;

public partial class FloatingDialogBase : PanelContainer
{
	private static readonly List<FloatingDialogBase> _openDialogs = new();
	public static bool HasAnyDialogOpen => _openDialogs.Count > 0;

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
		MainVBox.AddThemeConstantOverride("separation", 10);
		AddChild(MainVBox);

		HeaderHBox = new HBoxContainer();
		HeaderHBox.AddThemeConstantOverride("separation", 8);
		HeaderHBox.GuiInput += OnHeaderGuiInput;
		HeaderHBox.MouseFilter = MouseFilterEnum.Stop;
		HeaderHBox.MouseDefaultCursorShape = CursorShape.Move;
		MainVBox.AddChild(HeaderHBox);

		TitleLabel = new Label();
		TitleLabel.Text = titleText;
		TitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		TitleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		TitleLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		TitleLabel.AddThemeFontSizeOverride("font_size", 15);
		TitleLabel.MouseFilter = MouseFilterEnum.Pass;

		var titleMargin = new MarginContainer();
		titleMargin.AddThemeConstantOverride("margin_top", -14);
		titleMargin.AddThemeConstantOverride("margin_bottom", 0);
		titleMargin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		titleMargin.AddChild(TitleLabel);
		HeaderHBox.AddChild(titleMargin);

		CloseButton = new Button();
		CloseButton.Set("icon_max_width", 0);
		CloseButton.Text = "\uf00d";
		CloseButton.CustomMinimumSize = new Vector2(26, 26);
		CloseButton.FocusMode = FocusModeEnum.None;
		var faFontClose = Hud?.GetFontAwesomeFont();
		if (faFontClose != null)
		{
			CloseButton.AddThemeFontOverride("font", faFontClose);
		}
		CloseButton.Pressed += () => CancelAndClose();

		var closeMargin = new MarginContainer();
		closeMargin.AddThemeConstantOverride("margin_right", -35);
		closeMargin.AddThemeConstantOverride("margin_left", 10);
		closeMargin.AddChild(CloseButton);
		HeaderHBox.AddChild(closeMargin);

		BodyContainer = new VBoxContainer();
		BodyContainer.AddThemeConstantOverride("separation", 8);
		BodyContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		MainVBox.AddChild(BodyContainer);

		var footerMargin = new MarginContainer();
		footerMargin.AddThemeConstantOverride("margin_bottom", 12);
		FooterHBox = new HBoxContainer();
		FooterHBox.AddThemeConstantOverride("separation", 12);
		FooterHBox.Alignment = BoxContainer.AlignmentMode.End;
		footerMargin.AddChild(FooterHBox);
		MainVBox.AddChild(footerMargin);

		CancelButton = new Button();
		CancelButton.Set("icon_max_width", 0);
		CancelButton.Text = TranslationServer.Translate("CANCEL");
		CancelButton.CustomMinimumSize = new Vector2(90, 30);
		CancelButton.FocusMode = FocusModeEnum.None;
		CancelButton.Pressed += () => CancelAndClose();
		FooterHBox.AddChild(CancelButton);

		ApplyButton = new Button();
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
		btnClose.Set("icon_max_width", 0);
		btnClose.Text = TranslationServer.Translate(closeText);
		btnClose.CustomMinimumSize = new Vector2(90, 30);
		btnClose.FocusMode = FocusModeEnum.None;
		btnClose.Pressed += () => CloseDialog();
		FooterHBox.AddChild(btnClose);
		FooterHBox.Alignment = BoxContainer.AlignmentMode.Center;
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
			BackgroundTextureRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			BackgroundTextureRect.StretchMode = TextureRect.StretchModeEnum.Scale;
			BackgroundTextureRect.SetAnchorsPreset(LayoutPreset.FullRect);
			AddChild(BackgroundTextureRect);
			MoveChild(BackgroundTextureRect, 0);
		}
		
		Texture2D tex = null;
		try
		{
			tex = GD.Load<Texture2D>(texturePath);
		}
		catch { }

		if (tex == null && !string.IsNullOrEmpty(texturePath))
		{
			string globalPath = ProjectSettings.GlobalizePath(texturePath);
			if (System.IO.File.Exists(globalPath))
			{
				var img = Image.LoadFromFile(globalPath);
				if (img != null)
				{
					tex = ImageTexture.CreateFromImage(img);
				}
			}
		}

		if (tex != null)
		{
			BackgroundTextureRect.Texture = tex;
		}

		if (DialogMarginContainer == null && MainVBox != null && MainVBox.GetParent() == this)
		{
			RemoveChild(MainVBox);
			DialogMarginContainer = new MarginContainer();
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
		if (GetParent() == null && Hud != null)
		{
			Hud.AddChild(this);
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
		catch { }

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
				if (lineEdit != null && double.TryParse(lineEdit.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsedVal))
				{
					spinBox.Value = Math.Clamp(parsedVal, spinBox.MinValue, spinBox.MaxValue);
				}
			}
			catch { }
		}
		else if (node is LineEdit lineEdit)
		{
			try
			{
				lineEdit.ReleaseFocus();
			}
			catch { }
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
		CloseDialog();
	}

	public event Action? DialogClosed;

	public virtual void CloseDialog()
	{
		_openDialogs.Remove(this);
		Visible = false;
		if (GetParent() != null)
		{
			GetParent().RemoveChild(this);
		}
		DialogClosed?.Invoke();
	}

	protected virtual void OnApply() { }
	protected virtual void OnCancel() { }

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
		header.Text = titleText;
		header.AddThemeColorOverride("font_color", color ?? UIStyle.ColorGold);
		header.AddThemeFontSizeOverride("font_size", 12);
		var faFont = Hud?.GetFontAwesomeFont();
		if (faFont != null)
		{
			header.AddThemeFontOverride("font", faFont);
		}
		parent.AddChild(header);
		return header;
	}

	public Label AddLabel(Control parent, string text, int fontSize = 11, Color? color = null)
	{
		var label = new Label();
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
		row.AddThemeConstantOverride("separation", 6);

		var lbl = new Label();
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		var slider = new HSlider();
		slider.MinValue = min;
		slider.MaxValue = max;
		slider.Step = step;
		slider.Value = initialValue;
		slider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		row.AddChild(slider);

		var valLbl = new Label();
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
		row.AddThemeConstantOverride("separation", 6);

		var lbl = new Label();
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		var hueSlider = new HSlider();
		hueSlider.MinValue = 0.0f;
		hueSlider.MaxValue = 1.0f;
		hueSlider.Step = 0.01f;
		hueSlider.SetValueNoSignal(initialColor.H);
		hueSlider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
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
		row.AddThemeConstantOverride("separation", 6);

		var lbl = new Label();
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		var opt = new OptionButton();
		opt.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
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
		row.AddThemeConstantOverride("separation", 6);

		var lbl = new Label();
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		var txt = new LineEdit();
		txt.Text = initialText ?? string.Empty;
		txt.PlaceholderText = placeholder;
		txt.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
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
		row.AddThemeConstantOverride("separation", 6);

		var lbl = new Label();
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		var txt = new LineEdit();
		txt.Text = initialValue.ToString("0.##");
		txt.PlaceholderText = placeholder;
		txt.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		txt.AddThemeFontSizeOverride("font_size", 11);
		txt.TextChanged += (val) =>
		{
			if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed))
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
		row.AddThemeConstantOverride("separation", 6);

		var lbl = new Label();
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		Vector3 current = initialValue;

		var txtX = new LineEdit { Text = initialValue.X.ToString("0.##"), PlaceholderText = "X", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		var txtY = new LineEdit { Text = initialValue.Y.ToString("0.##"), PlaceholderText = "Y", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		var txtZ = new LineEdit { Text = initialValue.Z.ToString("0.##"), PlaceholderText = "Z", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		txtX.AddThemeFontSizeOverride("font_size", 11);
		txtY.AddThemeFontSizeOverride("font_size", 11);
		txtZ.AddThemeFontSizeOverride("font_size", 11);

		void UpdateVal()
		{
			float.TryParse(txtX.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x);
			float.TryParse(txtY.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y);
			float.TryParse(txtZ.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z);
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
		row.AddThemeConstantOverride("separation", 6);

		var lbl = new Label();
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		Vector2 current = initialValue;

		var txtX = new LineEdit { Text = initialValue.X.ToString("0.##"), PlaceholderText = "X", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		var txtY = new LineEdit { Text = initialValue.Y.ToString("0.##"), PlaceholderText = "Y", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		txtX.AddThemeFontSizeOverride("font_size", 11);
		txtY.AddThemeFontSizeOverride("font_size", 11);

		void UpdateVal()
		{
			float.TryParse(txtX.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x);
			float.TryParse(txtY.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y);
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
		row.AddThemeConstantOverride("separation", 4);

		var lbl = new Label();
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		string committedValue = initialText ?? string.Empty;
		bool isUpdatingTextProgrammatically = false;
		bool isPopupOpening = false;
		bool isPopupSelectionInProgress = false;
		bool isShowingAll = false;

		var txt = new LineEdit();
		txt.Text = committedValue;
		txt.PlaceholderText = placeholder;
		txt.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		txt.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(txt);

		var btnDropdown = new Button();
		btnDropdown.Set("icon_max_width", 0);
		btnDropdown.Text = "▼";
		btnDropdown.CustomMinimumSize = new Vector2(24, 22);
		btnDropdown.AddThemeFontSizeOverride("font_size", 10);
		btnDropdown.FocusMode = FocusModeEnum.None;
		btnDropdown.TooltipText = TranslationServer.Translate("Select from imported assets");
		row.AddChild(btnDropdown);

		bool includeAllFolders = false;
		if (hasAllFoldersCheckbox)
		{
			var chkAll = new CheckBox();
			chkAll.Text = TranslationServer.Translate("All folders");
			chkAll.AddThemeFontSizeOverride("font_size", 10);
			chkAll.TooltipText = TranslationServer.Translate("Include models outside of projectiles folder");
			chkAll.Toggled += (pressed) =>
			{
				includeAllFolders = pressed;
			};
			row.AddChild(chkAll);
		}

		if (onPlaySound != null)
		{
			var btnPlay = new Button();
			btnPlay.Set("icon_max_width", 0);
			btnPlay.Text = "▶";
			btnPlay.CustomMinimumSize = new Vector2(26, 22);
			btnPlay.AddThemeFontSizeOverride("font_size", 11);
			btnPlay.FocusMode = FocusModeEnum.None;
			btnPlay.TooltipText = TranslationServer.Translate("Preview Sound");
			btnPlay.Pressed += () => onPlaySound(txt.Text);
			row.AddChild(btnPlay);
		}

		var popup = new PopupMenu();
		popup.AddThemeFontSizeOverride("font_size", 11);
		popup.Unfocusable = true;
		AddChild(popup);

		var currentFilteredItems = new List<string>();

		void CommitSelection(string selected)
		{
			string safeSelected = selected ?? string.Empty;
			isUpdatingTextProgrammatically = true;
			try
			{
				txt.Text = safeSelected;
			}
			finally
			{
				isUpdatingTextProgrammatically = false;
			}

			if (!string.Equals(committedValue, safeSelected, StringComparison.Ordinal))
			{
				committedValue = safeSelected;
				onChanged(safeSelected);
			}
		}

		void PositionAndShowPopup()
		{
			if (!txt.IsInsideTree()) return;

			isPopupOpening = true;
			try
			{
				Vector2 globalPos = txt.GlobalPosition;
				Vector2 txtSize = txt.Size;
				popup.Position = new Vector2I((int)globalPos.X, (int)(globalPos.Y + txtSize.Y + 2));
				popup.ResetSize();
				if (popup.Size.X < (int)txtSize.X)
				{
					popup.Size = new Vector2I((int)txtSize.X, popup.Size.Y);
				}
				popup.Popup();
			}
			finally
			{
				Callable.From(() =>
				{
					isPopupOpening = false;
					if (GodotObject.IsInstanceValid(txt) && !txt.HasFocus())
					{
						txt.GrabFocus();
					}
				}).CallDeferred();
			}
		}

		void ShowAssetPopup(bool showAll = false)
		{
			isShowingAll = showAll;
			popup.Clear();
			currentFilteredItems.Clear();

			var allItems = itemsProvider(includeAllFolders);
			if (allItems == null || allItems.Count == 0)
			{
				popup.AddItem(TranslationServer.Translate("No matching assets found"), 0);
				popup.SetItemDisabled(0, true);
				PositionAndShowPopup();
				return;
			}

			string currentText = txt.Text?.Trim() ?? string.Empty;
			string query = (showAll || (string.IsNullOrEmpty(currentText) && !showAll))
				? string.Empty
				: currentText;

			List<string> displayItems;
			if (showAll || string.IsNullOrEmpty(query))
			{
				displayItems = allItems;
			}
			else
			{
				var lev = new Fastenshtein.Levenshtein(query.ToLowerInvariant());
				var scoredItems = new List<(string Item, int Score)>();
				for (int i = 0; i < allItems.Count; i++)
				{
					string item = allItems[i];
					int score = ComputeMatchScore(item, query, lev);
					scoredItems.Add((item, score));
				}

				scoredItems.Sort((a, b) =>
				{
					int cmp = a.Score.CompareTo(b.Score);
					return cmp != 0 ? cmp : string.Compare(a.Item, b.Item, StringComparison.OrdinalIgnoreCase);
				});

				var genuineMatches = new List<string>();
				for (int i = 0; i < scoredItems.Count; i++)
				{
					if (scoredItems[i].Score < 1000)
					{
						genuineMatches.Add(scoredItems[i].Item);
					}
				}

				if (genuineMatches.Count > 0)
				{
					displayItems = genuineMatches;
				}
				else
				{
					displayItems = new List<string>();
					int takeCount = Math.Min(5, scoredItems.Count);
					for (int i = 0; i < takeCount; i++)
					{
						displayItems.Add(scoredItems[i].Item);
					}
				}
			}

			if (displayItems.Count == 0)
			{
				popup.AddItem(TranslationServer.Translate("No matching assets found"), 0);
				popup.SetItemDisabled(0, true);
			}
			else
			{
				int maxItems = Math.Min(displayItems.Count, 35);
				for (int i = 0; i < maxItems; i++)
				{
					currentFilteredItems.Add(displayItems[i]);
					popup.AddItem(displayItems[i], i);
				}
				if (displayItems.Count > 35)
				{
					popup.AddItem($"... ({displayItems.Count - 35} more)", 35);
					popup.SetItemDisabled(35, true);
				}
			}

			PositionAndShowPopup();
		}

		void ResolveClosestMatch()
		{
			var allItems = itemsProvider(includeAllFolders);
			string closest = FindClosestMatch(txt.Text, allItems, committedValue);
			CommitSelection(closest);
			popup.Hide();
		}

		btnDropdown.Pressed += () =>
		{
			if (popup.Visible && isShowingAll)
			{
				popup.Hide();
				isShowingAll = false;
			}
			else
			{
				ShowAssetPopup(showAll: true);
			}
		};

		popup.PopupHide += () =>
		{
			isShowingAll = false;
		};

		popup.IdPressed += (long id) =>
		{
			int idx = (int)id;
			if (idx >= 0 && idx < currentFilteredItems.Count)
			{
				string selected = currentFilteredItems[idx];
				isPopupSelectionInProgress = true;
				CommitSelection(selected);
				popup.Hide();
				Callable.From(() => isPopupSelectionInProgress = false).CallDeferred();
			}
		};

		txt.FocusEntered += () =>
		{
			Callable.From(txt.SelectAll).CallDeferred();
		};

		txt.TextChanged += (val) =>
		{
			if (isUpdatingTextProgrammatically) return;
			ShowAssetPopup(showAll: false);
		};

		txt.FocusExited += () =>
		{
			if (isPopupOpening) return;

			Callable.From(() =>
			{
				if (isPopupSelectionInProgress) return;
				if (string.Equals(txt.Text, committedValue, StringComparison.Ordinal))
				{
					popup.Hide();
					return;
				}

				ResolveClosestMatch();
			}).CallDeferred();
		};

		txt.TextSubmitted += (newText) =>
		{
			ResolveClosestMatch();
			txt.ReleaseFocus();
		};

		parent.AddChild(row);
		return (txt, (val) =>
		{
			string safeVal = val ?? string.Empty;
			committedValue = safeVal;
			isUpdatingTextProgrammatically = true;
			try
			{
				txt.Text = safeVal;
			}
			finally
			{
				isUpdatingTextProgrammatically = false;
			}
		});
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
		var lev = new Fastenshtein.Levenshtein(cleanQuery.ToLowerInvariant());

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

	public static int ComputeMatchScore(string item, string query, Fastenshtein.Levenshtein? lev = null)
	{
		if (string.IsNullOrEmpty(item)) return int.MaxValue;
		if (string.IsNullOrEmpty(query)) return 0;

		string itemFull = item.Trim();
		string itemLower = itemFull.ToLowerInvariant();
		string q = query.Trim().ToLowerInvariant();

		if (itemLower == q) return 0;

		string fileName = System.IO.Path.GetFileName(itemFull).ToLowerInvariant();
		if (fileName == q) return 1;

		string nameNoExt = System.IO.Path.GetFileNameWithoutExtension(itemFull).ToLowerInvariant();
		if (nameNoExt == q) return 2;

		if (nameNoExt.StartsWith(q)) return 10 + (nameNoExt.Length - q.Length);
		if (fileName.StartsWith(q)) return 20 + (fileName.Length - q.Length);
		if (itemLower.StartsWith(q)) return 30 + (itemLower.Length - q.Length);

		string[] queryWords = q.Split(new[] { ' ', '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
		if (queryWords.Length > 1)
		{
			bool allWordsMatch = true;
			for (int i = 0; i < queryWords.Length; i++)
			{
				if (!itemLower.Contains(queryWords[i]))
				{
					allWordsMatch = false;
					break;
				}
			}
			if (allWordsMatch)
			{
				return 25 + Math.Abs(nameNoExt.Length - q.Length);
			}
		}

		string[] tokens = nameNoExt.Split(new[] { '_', '-', ' ', '.', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < tokens.Length; i++)
		{
			string token = tokens[i];
			if (token == q) return 40 + (nameNoExt.Length - q.Length);
			if (token.StartsWith(q)) return 50 + (nameNoExt.Length - q.Length);
		}

		if (nameNoExt.Contains(q))
		{
			int idx = nameNoExt.IndexOf(q);
			return 100 + idx * 5 + (nameNoExt.Length - q.Length);
		}

		if (fileName.Contains(q))
		{
			int idx = fileName.IndexOf(q);
			return 200 + idx * 5 + (fileName.Length - q.Length);
		}

		if (itemLower.Contains(q))
		{
			int idx = itemLower.IndexOf(q);
			return 300 + idx * 2 + (itemLower.Length - q.Length);
		}

		int bestTokenDist = int.MaxValue;
		for (int i = 0; i < tokens.Length; i++)
		{
			int dist = lev != null ? lev.DistanceFrom(tokens[i]) : Fastenshtein.Levenshtein.Distance(q, tokens[i]);
			if (dist < bestTokenDist) bestTokenDist = dist;
		}
		if (bestTokenDist <= 2)
		{
			return 500 + bestTokenDist * 50 + Math.Abs(nameNoExt.Length - q.Length);
		}

		int distName = lev != null ? lev.DistanceFrom(nameNoExt) : Fastenshtein.Levenshtein.Distance(q, nameNoExt);
		return 1000 + distName * 20 + Math.Abs(nameNoExt.Length - q.Length);
	}

	public static List<string> ScanAvailableAssets(string category, bool includeAllFolders = false, string subFolder = null)
	{
		var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string wsPath = !string.IsNullOrEmpty(MapWorkspaceService.GetActiveWorkspacePath())
			? MapWorkspaceService.GetActiveWorkspacePath()
			: ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);

		try
		{
			var assetsObj = Realm.Godot.Utils.MapAssetHelper.LoadAssets(wsPath);
			string manifestPath = Path.Combine(wsPath, "manifest.json");
			Realm.Shared.Distribution.MapManifest manifest = null;
			if (File.Exists(manifestPath))
			{
				try
				{
					manifest = Realm.Shared.Distribution.MapManifest.LoadFromFile(manifestPath);
				}
				catch { }
			}

			if (assetsObj != null || manifest != null)
			{
					if (category == "audio" || category == "sound" || category == "sfx" || category == "music")
					{
						foreach (var key in new[] { "SoundEffect", "Music" })
						{
							var catDict = assetsObj?.GetCategory(key);
							if (catDict != null)
							{
								foreach (var kvp in catDict)
								{
									if (!string.IsNullOrWhiteSpace(kvp.Key) &&
										(kvp.Key.EndsWith(".raud", StringComparison.OrdinalIgnoreCase) ||
										 kvp.Key.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
										 kvp.Key.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)))
									{
										result.Add(Path.GetFileName(kvp.Key));
									}
								}
							}
						}

						if (manifest?.Files != null)
						{
							foreach (var kvp in manifest.Files)
							{
								if (!string.IsNullOrWhiteSpace(kvp.Key) &&
									(kvp.Key.EndsWith(".raud", StringComparison.OrdinalIgnoreCase) ||
									 kvp.Key.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
									 kvp.Key.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)))
								{
									result.Add(Path.GetFileName(kvp.Key));
								}
							}
						}

						string audioDir = Path.Combine(wsPath, "Assets", "audio");
						if (Directory.Exists(audioDir))
						{
							foreach (var file in Directory.EnumerateFiles(audioDir, "*.*", SearchOption.AllDirectories))
							{
								string ext = Path.GetExtension(file);
								if (ext.Equals(".raud", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".wav", StringComparison.OrdinalIgnoreCase))
								{
									result.Add(Path.GetFileName(file));
								}
							}
						}
					}
					else if (category == "spritesheets" || category == "spritesheet" || category == "spritesheet_rtex")
					{
						var sDict = assetsObj?.GetCategory("Spritesheet") ?? assetsObj?.GetCategory("spritesheets");
						if (sDict != null)
						{
							foreach (var kvp in sDict)
							{
								if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
								{
									result.Add(Path.GetFileName(kvp.Key));
								}
							}
						}

						if (manifest?.Assets?.Spritesheet != null)
						{
							foreach (var kvp in manifest.Assets.Spritesheet)
							{
								if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
								{
									result.Add(Path.GetFileName(kvp.Key));
								}
							}
						}

						if (manifest?.Files != null)
						{
							foreach (var kvp in manifest.Files)
							{
								if (!string.IsNullOrWhiteSpace(kvp.Key) &&
									kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) &&
									(kvp.Key.StartsWith("Assets/vfx/spritesheets/", StringComparison.OrdinalIgnoreCase) ||
									 kvp.Key.StartsWith("Assets/spritesheets/", StringComparison.OrdinalIgnoreCase) ||
									 kvp.Key.StartsWith("Assets/vfx/", StringComparison.OrdinalIgnoreCase)))
								{
									result.Add(Path.GetFileName(kvp.Key));
								}
							}
						}

						if (MetadataService.Instance.TryLoadMetadata(wsPath, out var ssMetaRoot) && ssMetaRoot?.VfxSpritesheets != null)
						{
							foreach (var kvp in ssMetaRoot.VfxSpritesheets)
							{
								if (!string.IsNullOrWhiteSpace(kvp.Value?.TexturePath) && kvp.Value.TexturePath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
								{
									result.Add(Path.GetFileName(kvp.Value.TexturePath));
								}
							}
						}

						string vfxDir = Path.Combine(wsPath, "Assets", "vfx");
						if (Directory.Exists(vfxDir))
						{
							foreach (var file in Directory.EnumerateFiles(vfxDir, "*.rtex", SearchOption.AllDirectories))
							{
								result.Add(Path.GetFileName(file));
							}
						}

						string templateVfxDir = Path.Combine(ProjectSettings.GlobalizePath("res://"), "Assets", "vfx");
						if (Directory.Exists(templateVfxDir))
						{
							foreach (var file in Directory.EnumerateFiles(templateVfxDir, "*.rtex", SearchOption.AllDirectories))
							{
								result.Add(Path.GetFileName(file));
							}
						}
					}
					else if (category == "vfx" || category == "vfx_spritesheets" || category == "vfx_radial" || category == "vfx_vertical")
					{
						if (category is "vfx" or "vfx_spritesheets")
						{
							foreach (var prim in Enum.GetValues<VfxPrimitiveType>())
							{
								result.Add($"vfx:{prim}");
							}
							if (GameHost.VfxRegistry != null)
							{
								foreach (var kvp in GameHost.VfxRegistry)
								{
									result.Add(kvp.Key.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase) ? kvp.Key : $"vfx:{kvp.Key}");
								}
							}
						}

						string[] searchKeys = category switch
						{
							"vfx_radial" => new[] { "vfx_radial", "Spritesheet" },
							"vfx_vertical" => new[] { "vfx_vertical", "Spritesheet" },
							_ => new[] { "Spritesheet", "vfx_radial", "vfx_vertical" }
						};

						foreach (var key in searchKeys)
						{
							var catDict = assetsObj?.GetCategory(key);
							if (catDict != null)
							{
								foreach (var kvp in catDict)
								{
									if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
									{
										result.Add(Path.GetFileName(kvp.Key));
									}
								}
							}
						}

						if (MetadataService.Instance.TryLoadMetadata(wsPath, out var vfxMetaRoot) && vfxMetaRoot != null)
						{
							if (vfxMetaRoot.Templates?.Vfx != null)
							{
								foreach (var cv in vfxMetaRoot.Templates.Vfx)
								{
									if (!string.IsNullOrWhiteSpace(cv.VfxId))
									{
										result.Add(cv.VfxId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase) ? cv.VfxId : $"vfx:{cv.VfxId}");
										result.Add(cv.VfxId);
									}
								}
							}
						}
					}
					else if (category == "decals" || category == "decal")
					{
						var catDict = assetsObj?.GetCategory("Decal");
						if (catDict != null)
						{
							foreach (var kvp in catDict)
							{
								if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
								{
									result.Add(Path.GetFileName(kvp.Key));
								}
							}
						}

						if (manifest?.Assets?.Decal != null)
						{
							foreach (var kvp in manifest.Assets.Decal)
							{
								if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
								{
									result.Add(Path.GetFileName(kvp.Key));
								}
							}
						}
						if (manifest?.Files != null)
						{
							foreach (var kvp in manifest.Files)
							{
								if (!string.IsNullOrWhiteSpace(kvp.Key) &&
									kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) &&
									(kvp.Key.StartsWith("Assets/decals/", StringComparison.OrdinalIgnoreCase) ||
									 kvp.Key.StartsWith("assets/decals/", StringComparison.OrdinalIgnoreCase) ||
									 kvp.Key.StartsWith("decals/", StringComparison.OrdinalIgnoreCase)))
								{
									result.Add(Path.GetFileName(kvp.Key));
								}
							}
						}

						string decalsDir = Path.Combine(wsPath, "Assets", "decals");
						if (Directory.Exists(decalsDir))
						{
							foreach (var file in Directory.EnumerateFiles(decalsDir, "*.rtex", SearchOption.AllDirectories))
							{
								result.Add(Path.GetFileName(file));
							}
						}

						string templateDecalsDir = Path.Combine(ProjectSettings.GlobalizePath("res://"), "Assets", "decals");
						if (Directory.Exists(templateDecalsDir))
						{
							foreach (var file in Directory.EnumerateFiles(templateDecalsDir, "*.rtex", SearchOption.AllDirectories))
							{
								result.Add(Path.GetFileName(file));
							}
						}
					}
					else if (category == "models" || category == "glb" || category == "attachments" || category == "items" || category == "weapons" || category == "projectiles")
					{
						if (category is "attachments" or "items" || includeAllFolders)
						{
							foreach (var prim in Enum.GetValues<VfxPrimitiveType>())
							{
								result.Add($"vfx:{prim}");
							}
							if (GameHost.VfxRegistry != null)
							{
								foreach (var kvp in GameHost.VfxRegistry)
								{
									result.Add(kvp.Key.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase) ? kvp.Key : $"vfx:{kvp.Key}");
								}
							}
						}

						string defaultFolder = !string.IsNullOrEmpty(subFolder) ? subFolder : (category is "attachments" or "items" ? "items" : "projectiles");
						foreach (var catName in new[] { "Character", "Building", "Prop", "Item" })
						{
							bool matches = includeAllFolders || catName.Equals(defaultFolder, StringComparison.OrdinalIgnoreCase) ||
								(defaultFolder == "items" && catName == "Item") ||
								(defaultFolder == "units" && catName == "Character") ||
								(defaultFolder == "buildings" && catName == "Building") ||
								(defaultFolder == "props" && catName == "Prop");

							if (matches)
							{
								var cDict = assetsObj?.GetCategory(catName);
								if (cDict != null)
								{
									foreach (var kvp in cDict)
									{
										if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Key.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
										{
											result.Add(Path.GetFileName(kvp.Key));
										}
									}
								}
							}
						}

						if (manifest?.Files != null)
						{
							foreach (var kvp in manifest.Files)
							{
								if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Key.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
								{
									result.Add(Path.GetFileName(kvp.Key));
								}
							}
						}

						string modelsDir = Path.Combine(wsPath, "Assets", "models");
						if (Directory.Exists(modelsDir))
						{
							foreach (var file in Directory.EnumerateFiles(modelsDir, "*.rmesh", SearchOption.AllDirectories))
							{
								result.Add(Path.GetFileName(file));
							}
						}
					}
					else if (category == "ribbons" || category == "ribbon_textures")
					{
						var rDict = assetsObj?.GetCategory("Ribbon");
						if (rDict != null)
						{
							foreach (var kvp in rDict)
							{
								if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
								{
									result.Add(Path.GetFileName(kvp.Key));
								}
							}
						}

						if (manifest?.Assets?.Ribbon != null)
						{
							foreach (var kvp in manifest.Assets.Ribbon)
							{
								if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
								{
									result.Add(Path.GetFileName(kvp.Key));
								}
							}
						}

						string ribbonsDir = Path.Combine(wsPath, "Assets", "ribbons");
						if (Directory.Exists(ribbonsDir))
						{
							foreach (var file in Directory.EnumerateFiles(ribbonsDir, "*.rtex", SearchOption.AllDirectories))
							{
								result.Add(Path.GetFileName(file));
							}
						}
					}
					else if (category == "animations" || category == "ranim" || category == "animation")
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
					else if (category == "icons" || category == "icon")
					{
						var iDict = assetsObj?.GetCategory("Icon") ?? assetsObj?.GetCategory("icons");
						if (iDict != null)
						{
							foreach (var kvp in iDict)
							{
								if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
								{
									result.Add(Path.GetFileName(kvp.Key));
								}
							}
						}

						if (manifest?.Assets?.Icon != null)
						{
							foreach (var kvp in manifest.Assets.Icon)
							{
								if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
								{
									result.Add(Path.GetFileName(kvp.Key));
								}
							}
						}

						if (manifest?.Files != null)
						{
							foreach (var kvp in manifest.Files)
							{
								if (!string.IsNullOrWhiteSpace(kvp.Key) &&
									kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) &&
									(kvp.Key.StartsWith("Assets/icons/", StringComparison.OrdinalIgnoreCase) ||
									 kvp.Key.StartsWith("assets/icons/", StringComparison.OrdinalIgnoreCase) ||
									 kvp.Key.StartsWith("icons/", StringComparison.OrdinalIgnoreCase)))
								{
									result.Add(Path.GetFileName(kvp.Key));
								}
							}
						}

						if (MetadataService.Instance.TryLoadMetadata(wsPath, out var iconMetaRoot) && iconMetaRoot?.Icons != null)
						{
							foreach (var kvp in iconMetaRoot.Icons)
							{
								if (kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
								{
									result.Add(Path.GetFileName(kvp.Key));
								}
							}
						}

						string iconsDir = Path.Combine(wsPath, "Assets", "icons");
						if (Directory.Exists(iconsDir))
						{
							foreach (var file in Directory.EnumerateFiles(iconsDir, "*.rtex", SearchOption.AllDirectories))
							{
								result.Add(Path.GetFileName(file));
							}
						}

						string templateIconsDir = Path.Combine(ProjectSettings.GlobalizePath("res://"), "Assets", "icons");
						if (Directory.Exists(templateIconsDir))
						{
							foreach (var file in Directory.EnumerateFiles(templateIconsDir, "*.rtex", SearchOption.AllDirectories))
							{
								result.Add(Path.GetFileName(file));
							}
						}
					}
					else if (category == "noise" || category == "noise_textures")
					{
						var nDict = assetsObj?.GetCategory("Noise");
						if (nDict != null)
						{
							foreach (var kvp in nDict)
							{
								if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
								{
									result.Add(Path.GetFileName(kvp.Key));
								}
							}
						}

						if (manifest?.Assets?.Noise != null)
						{
							foreach (var kvp in manifest.Assets.Noise)
							{
								if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
								{
									result.Add(Path.GetFileName(kvp.Key));
								}
							}
						}

						string noiseDir = Path.Combine(wsPath, "Assets", "noise");
						if (Directory.Exists(noiseDir))
						{
							foreach (var file in Directory.EnumerateFiles(noiseDir, "*.rtex", SearchOption.AllDirectories))
							{
								result.Add(Path.GetFileName(file));
							}
						}
					}
					else if (category == "textures" || category == "terrain")
					{
						var tDict = assetsObj?.GetCategory("Terrain") ?? assetsObj?.GetCategory("textures");
						if (tDict != null)
						{
							foreach (var kvp in tDict)
							{
								if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
								{
									result.Add(Path.GetFileName(kvp.Key));
								}
							}
						}

						if (manifest?.Assets?.Terrain != null)
						{
							foreach (var kvp in manifest.Assets.Terrain)
							{
								if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
								{
									result.Add(Path.GetFileName(kvp.Key));
								}
							}
						}

						if (manifest?.Files != null)
						{
							foreach (var kvp in manifest.Files)
							{
								if (!string.IsNullOrWhiteSpace(kvp.Key) &&
									kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) &&
									(kvp.Key.StartsWith("Assets/textures/", StringComparison.OrdinalIgnoreCase) ||
									 kvp.Key.StartsWith("assets/textures/", StringComparison.OrdinalIgnoreCase) ||
									 kvp.Key.StartsWith("textures/", StringComparison.OrdinalIgnoreCase)))
								{
									result.Add(Path.GetFileName(kvp.Key));
								}
							}
						}

						string texturesDir = Path.Combine(wsPath, "Assets", "textures");
						if (Directory.Exists(texturesDir))
						{
							foreach (var file in Directory.EnumerateFiles(texturesDir, "*.rtex", SearchOption.AllDirectories))
							{
								result.Add(Path.GetFileName(file));
							}
						}

						string templateTexturesDir = Path.Combine(ProjectSettings.GlobalizePath("res://"), "Assets", "textures");
						if (Directory.Exists(templateTexturesDir))
						{
							foreach (var file in Directory.EnumerateFiles(templateTexturesDir, "*.rtex", SearchOption.AllDirectories))
							{
								result.Add(Path.GetFileName(file));
							}
						}
					}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[FloatingDialogBase] ScanAvailableAssets metadata error: {ex.Message}");
		}

		var list = new List<string>(result);
		list.Sort(StringComparer.OrdinalIgnoreCase);
		return list;
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
		container.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		container.Stretch = true;

		subViewport = new SubViewport();
		subViewport.Size = new Vector2I((int)minSize.X, (int)minSize.Y);
		subViewport.TransparentBg = false;
		subViewport.OwnWorld3D = true;

		var world = new World3D();
		var env = new Godot.Environment();
		env.BackgroundMode = Godot.Environment.BGMode.Color;
		env.BackgroundColor = new Color(0.11f, 0.13f, 0.17f);
		env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
		env.AmbientLightColor = new Color(0.60f, 0.60f, 0.65f);
		env.AmbientLightEnergy = 0.75f;
		world.Environment = env;
		subViewport.World3D = world;

		light = new DirectionalLight3D();
		light.RotationDegrees = new Vector3(-30, 30, 0);
		light.LightEnergy = 0.75f;
		subViewport.AddChild(light);

		camera = new Camera3D();
		camera.Position = new Vector3(0, 1.2f, 2.8f);
		subViewport.AddChild(camera);

		container.AddChild(subViewport);
		parent.AddChild(container);
		return container;
	}

	public VBoxContainer CreateScrollBody(int minHeight = 350)
	{
		var scroll = new ScrollContainer();
		scroll.CustomMinimumSize = new Vector2(0, minHeight);
		scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

		var innerVBox = new VBoxContainer();
		innerVBox.AddThemeConstantOverride("separation", 8);
		innerVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		innerVBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

		scroll.AddChild(innerVBox);
		BodyContainer.AddChild(scroll);
		return innerVBox;
	}
}
