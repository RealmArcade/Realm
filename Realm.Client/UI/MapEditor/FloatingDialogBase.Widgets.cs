using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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

public partial class FloatingDialogBase
{
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
		var row = UIStyle.CreateRowContainer();

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

	public HBoxContainer BuildIdentityRow(Control parent, string prefixText, out Label prefixLabel, float labelWidth = 140.0f)
	{
		var rowId = new HBoxContainer();
		rowId.Name = "RowId_HBoxContainer";
		rowId.AddThemeConstantOverride("separation", 6);
		var lblId = new Label();
		lblId.Name = "LblId_Label";
		lblId.Text = TranslationServer.Translate("TemplateID:");
		lblId.CustomMinimumSize = new Vector2(labelWidth, 0);
		lblId.AddThemeFontSizeOverride("font_size", 11);
		rowId.AddChild(lblId);

		prefixLabel = new Label();
		prefixLabel.Name = "LblObjectTypePrefix_Label";
		prefixLabel.Text = prefixText;
		prefixLabel.AddThemeFontSizeOverride("font_size", 11);
		prefixLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		rowId.AddChild(prefixLabel);

		parent.AddChild(rowId);
		return rowId;
	}

	public (ColorPickerButton Picker, HSlider HueSlider) AddColorPicker(
		VBoxContainer parent,
		string labelText,
		Color initialColor,
		Action<Color> onChanged,
		float labelWidth = 110.0f)
	{
		var row = UIStyle.CreateRowContainer();

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
		Action<bool>? onChanged,
		string tooltip = "")
	{
		var chk = new CheckBox();
		chk.Name = "Chk_CheckBox";
		chk.Text = labelText;
		chk.ButtonPressed = initialValue;
		chk.AddThemeFontSizeOverride("font_size", 11);
		if (!string.IsNullOrEmpty(tooltip)) chk.TooltipText = tooltip;
		chk.Toggled += (pressed) => onChanged?.Invoke(pressed);
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
		var row = UIStyle.CreateRowContainer();

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

	public (ItemList, LineEdit, Action<string>) AddAssetRuleList(
		VBoxContainer contentVBox,
		string itemName,
		string assetFilterType,
		string translationKeyId,
		ItemList.ItemSelectedEventHandler onItemSelected,
		Action onAddAction,
		Action onRemoveAction)
	{
		var listRow = new HBoxContainer();
		listRow.Name = $"{itemName}ListRow_HBoxContainer";
		var list = new ItemList { CustomMinimumSize = new Vector2(240, 75), SizeFlagsHorizontal = SizeFlags.ExpandFill };
		list.ItemSelected += onItemSelected;
		listRow.AddChild(list);

		var btnBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		var filterResult = AddAssetFilterDropdown(
			btnBox, string.Empty, string.Empty, (all) => ScanAvailableAssets(assetFilterType, all), (val) => { }, TranslationServer.Translate(translationKeyId), 0f);
		
		var txtId = filterResult.Input;
		var setIdValue = filterResult.SetValue;

		var btnAdd = new Button { Text = "+ " + TranslationServer.Translate($"Add {itemName}") };
		btnAdd.Set("icon_max_width", 0);
		btnAdd.Pressed += onAddAction;
		btnBox.AddChild(btnAdd);

		var btnRemove = new Button { Text = "✕ " + TranslationServer.Translate("Remove") };
		btnRemove.Set("icon_max_width", 0);
		btnRemove.Pressed += onRemoveAction;
		btnBox.AddChild(btnRemove);

		listRow.AddChild(btnBox);
		contentVBox.AddChild(listRow);

		return (list, txtId, setIdValue);
	}

	public LineEdit AddTextInput(
		VBoxContainer parent,
		string labelText,
		string initialText,
		Action<string> onChanged,
		string placeholder = "",
		float labelWidth = 110.0f)
	{
		var row = BuildNameRow(labelText, initialText, onChanged, placeholder, labelWidth);
		parent.AddChild(row);
		return (LineEdit)row.GetChild(1);
	}

	public static HBoxContainer BuildNameRow(
		string labelText,
		string? initialText,
		Action<string> onChanged,
		string placeholder = "",
		float labelWidth = 110.0f)
	{
		var row = UIStyle.CreateRowContainer();

		var lbl = new Label();
		lbl.Name = "Lbl_Label";
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		var txt = new LineEdit();
		txt.Name = "Txt_LineEdit";
		txt.Text = initialText ?? string.Empty;
		if (!string.IsNullOrEmpty(placeholder)) txt.PlaceholderText = placeholder;
		txt.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		UIStyle.ApplyTextInput(txt, 11);
		txt.TextChanged += (newText) => onChanged?.Invoke(newText);
		row.AddChild(txt);

		return row;
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
		var row = BuildNameRow(labelText, initialValue.ToString("0.##"), (val) =>
		{
			if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
			{
				onChanged(parsed);
			}
		}, placeholder, labelWidth);

		parent.AddChild(row);
		var txt = (LineEdit)row.GetChild(1);
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
		var row = UIStyle.CreateRowContainer();

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
		UIStyle.ApplyTextInput(txtX, 11);
		UIStyle.ApplyTextInput(txtY, 11);
		UIStyle.ApplyTextInput(txtZ, 11);

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
		var row = UIStyle.CreateRowContainer();

		var lbl = new Label();
		lbl.Name = "Lbl_Label";
		lbl.Text = labelText;
		lbl.CustomMinimumSize = new Vector2(labelWidth, 0);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lbl);

		Vector2 current = initialValue;

		var txtX = new LineEdit { Text = initialValue.X.ToString("0.##"), PlaceholderText = "X", SizeFlagsHorizontal = SizeFlags.ExpandFill };
		var txtY = new LineEdit { Text = initialValue.Y.ToString("0.##"), PlaceholderText = "Y", SizeFlagsHorizontal = SizeFlags.ExpandFill };
		UIStyle.ApplyTextInput(txtX, 11);
		UIStyle.ApplyTextInput(txtY, 11);

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
		Action<string>? onPlaySound = null)
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
			Txt = new LineEdit()
		};

		state.Txt.Name = "FilterTxt_LineEdit";
		state.Txt.Text = state.CommittedValue;
		state.Txt.PlaceholderText = placeholder;
		state.Txt.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		UIStyle.ApplyTextInput(state.Txt, 11);
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

		Node dropdownOwner = Hud != null ? Hud : parent.GetViewport();
		if (dropdownOwner != null)
		{
			state.InitializeDropdown(dropdownOwner, state.CommittedValue);
		}

		VisibilityChanged += () => { if (!Visible) state.HideDropdown(); };
		TreeExiting += state.FreeDropdown;

		btnDropdown.Pressed += () => HandleAssetDropdownPressed(state);

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

	private void AddAssetFilterPlaySoundButton(HBoxContainer row, AssetFilterDropdownState state, Action<string>? onPlaySound)
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
		if (state.IsDropdownVisible && state.IsShowingAll)
		{
			state.HideDropdown();
		}
		else
		{
			state.ShowAssetPopup(showAll: true);
		}
	}

	private void HandleAssetTxtFocusExited(AssetFilterDropdownState state)
	{
		Callable.From(() =>
		{
			if (state.IsPopupOpening) return;
			if (state.IsPopupSelectionInProgress) return;
			if (string.Equals(state.Txt.Text, state.CommittedValue, StringComparison.Ordinal))
			{
				state.HideDropdown();
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

		state.SyncProgrammaticText(safeVal);
	}
}
