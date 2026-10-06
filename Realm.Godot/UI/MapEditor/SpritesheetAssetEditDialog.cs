using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Realm.Godot.Utils;

public partial class SpritesheetAssetEditDialog : FloatingDialogBase
{
	private string _sheetFileName = "";
	private string _objectType = "spritesheet";
	private string _slug = "";
	private string _rtexAsset = "";

	private int _columns = 4;
	private int _rows = 4;
	private float _fps = 20.0f;
	private bool _subframeBlend = true;

	private Action<int, int, float, bool> _onApplied;
	private Action<string, string, int, int, float, bool> _onAppliedWithTemplateIdAndRtex;

	private Label _lblObjectTypePrefix;
	private LineEdit _txtSlug;
	private LineEdit _txtRtexAsset;
	private Action<string> _setRtexAssetValue;

	private SpinBox _spinCols;
	private SpinBox _spinRows;
	private SpinBox _spinFps;
	private CheckBox _chkSubframeBlend;

	public SpritesheetAssetEditDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Edit VFX Spritesheet"), new Vector2(380, 360))
	{
		BuildControls();
	}

	private void BuildControls()
	{
		var contentVBox = new VBoxContainer();
		contentVBox.AddThemeConstantOverride("separation", 10);
		BodyContainer.AddChild(contentVBox);

		AddSectionHeader(contentVBox, "🆔 " + TranslationServer.Translate("IDENTITY & ASSET"), new Color(0.95f, 0.8f, 0.4f));

		var rowId = new HBoxContainer();
		rowId.AddThemeConstantOverride("separation", 6);
		var lblId = new Label();
		lblId.Text = TranslationServer.Translate("TemplateID:");
		lblId.CustomMinimumSize = new Vector2(100, 0);
		lblId.AddThemeFontSizeOverride("font_size", 11);
		rowId.AddChild(lblId);

		_lblObjectTypePrefix = new Label();
		_lblObjectTypePrefix.Text = "spritesheet/";
		_lblObjectTypePrefix.AddThemeFontSizeOverride("font_size", 11);
		_lblObjectTypePrefix.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		rowId.AddChild(_lblObjectTypePrefix);

		_txtSlug = new LineEdit();
		_txtSlug.PlaceholderText = TranslationServer.Translate("spritesheet_slug");
		_txtSlug.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_txtSlug.AddThemeFontSizeOverride("font_size", 11);
		_txtSlug.TextChanged += (val) =>
		{
			_slug = TemplateIDHelper.ToSnakeCase(val);
		};
		rowId.AddChild(_txtSlug);
		contentVBox.AddChild(rowId);

		(_txtRtexAsset, _setRtexAssetValue) = AddAssetFilterDropdown(
			contentVBox,
			TranslationServer.Translate("Spritesheet .rtex:"),
			_rtexAsset,
			(all) => ScanRtexAssets(all),
			(val) => _rtexAsset = val ?? string.Empty,
			TranslationServer.Translate("Select .rtex asset..."),
			100f
		);

		AddSectionHeader(contentVBox, "✨ " + TranslationServer.Translate("SPRITESHEET GRID DIMENSIONS"), new Color(0.35f, 0.75f, 0.9f));

		var rowCols = new HBoxContainer();
		rowCols.AddThemeConstantOverride("separation", 8);
		var lblCols = new Label();
		lblCols.Text = TranslationServer.Translate("Columns:");
		lblCols.CustomMinimumSize = new Vector2(100, 0);
		lblCols.AddThemeFontSizeOverride("font_size", 11);
		rowCols.AddChild(lblCols);

		_spinCols = new SpinBox();
		_spinCols.MinValue = 1;
		_spinCols.MaxValue = 32;
		_spinCols.Step = 1;
		_spinCols.Value = 4;
		_spinCols.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_spinCols.ValueChanged += (val) => _columns = (int)val;
		_spinCols.GetLineEdit().TextChanged += (text) =>
		{
			if (int.TryParse(text, out int v))
			{
				_columns = Math.Clamp(v, (int)_spinCols.MinValue, (int)_spinCols.MaxValue);
			}
		};
		rowCols.AddChild(_spinCols);
		contentVBox.AddChild(rowCols);

		var rowRows = new HBoxContainer();
		rowRows.AddThemeConstantOverride("separation", 8);
		var lblRows = new Label();
		lblRows.Text = TranslationServer.Translate("Rows:");
		lblRows.CustomMinimumSize = new Vector2(100, 0);
		lblRows.AddThemeFontSizeOverride("font_size", 11);
		rowRows.AddChild(lblRows);

		_spinRows = new SpinBox();
		_spinRows.MinValue = 1;
		_spinRows.MaxValue = 32;
		_spinRows.Step = 1;
		_spinRows.Value = 4;
		_spinRows.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_spinRows.ValueChanged += (val) => _rows = (int)val;
		_spinRows.GetLineEdit().TextChanged += (text) =>
		{
			if (int.TryParse(text, out int v))
			{
				_rows = Math.Clamp(v, (int)_spinRows.MinValue, (int)_spinRows.MaxValue);
			}
		};
		rowRows.AddChild(_spinRows);
		contentVBox.AddChild(rowRows);

		var rowFps = new HBoxContainer();
		rowFps.AddThemeConstantOverride("separation", 8);
		var lblFps = new Label();
		lblFps.Text = TranslationServer.Translate("Animation FPS:");
		lblFps.CustomMinimumSize = new Vector2(100, 0);
		lblFps.AddThemeFontSizeOverride("font_size", 11);
		rowFps.AddChild(lblFps);

		_spinFps = new SpinBox();
		_spinFps.MinValue = 1;
		_spinFps.MaxValue = 60;
		_spinFps.Step = 1;
		_spinFps.Value = 20;
		_spinFps.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_spinFps.ValueChanged += (val) => _fps = (float)val;
		_spinFps.GetLineEdit().TextChanged += (text) =>
		{
			if (float.TryParse(text, out float v))
			{
				_fps = Math.Clamp(v, (float)_spinFps.MinValue, (float)_spinFps.MaxValue);
			}
		};
		rowFps.AddChild(_spinFps);
		contentVBox.AddChild(rowFps);

		var rowBlend = new HBoxContainer();
		rowBlend.AddThemeConstantOverride("separation", 8);
		var lblBlend = new Label();
		lblBlend.Text = TranslationServer.Translate("Sub-frame Blend:");
		lblBlend.CustomMinimumSize = new Vector2(100, 0);
		lblBlend.AddThemeFontSizeOverride("font_size", 11);
		rowBlend.AddChild(lblBlend);

		_chkSubframeBlend = new CheckBox();
		_chkSubframeBlend.ButtonPressed = _subframeBlend;
		_chkSubframeBlend.Toggled += (toggled) => _subframeBlend = toggled;
		rowBlend.AddChild(_chkSubframeBlend);
		contentVBox.AddChild(rowBlend);
	}

	private List<string> ScanRtexAssets(bool includeAllFolders)
	{
		var list = ScanAvailableAssets("spritesheets", includeAllFolders);
		var rtexFiles = new HashSet<string>(list.Where(x => x.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
		return rtexFiles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
	}

	public void OpenForSheet(string fullTemplateId, string rtexAsset, int initialCols, int initialRows, float initialFps, bool initialSubframeBlend, Action<string, string, int, int, float, bool> onApplied)
	{
		_sheetFileName = fullTemplateId ?? string.Empty;
		var (parsedType, parsedSlug) = TemplateIDHelper.ParseTemplateID(_sheetFileName);
		_objectType = !string.IsNullOrEmpty(parsedType) ? parsedType : "spritesheet";
		_slug = !string.IsNullOrEmpty(parsedSlug) ? parsedSlug : TemplateIDHelper.ToSnakeCase(_sheetFileName);
		_rtexAsset = rtexAsset ?? string.Empty;

		_columns = Math.Max(1, initialCols);
		_rows = Math.Max(1, initialRows);
		_fps = initialFps > 0.001f ? initialFps : 20.0f;
		_subframeBlend = initialSubframeBlend;
		_onAppliedWithTemplateIdAndRtex = onApplied;
		_onApplied = null;

		TitleLabel.Text = $"{TranslationServer.Translate("Edit Spritesheet")} - {_sheetFileName}";

		if (_lblObjectTypePrefix != null) _lblObjectTypePrefix.Text = $"{_objectType}/";
		if (_txtSlug != null) _txtSlug.Text = _slug;
		_setRtexAssetValue?.Invoke(_rtexAsset);
		if (_spinCols != null) _spinCols.Value = _columns;
		if (_spinRows != null) _spinRows.Value = _rows;
		if (_spinFps != null) _spinFps.Value = _fps;
		if (_chkSubframeBlend != null) _chkSubframeBlend.ButtonPressed = _subframeBlend;

		OpenDialog();
	}

	public void OpenForSheet(string fileName, int initialCols, int initialRows, float initialFps, bool initialSubframeBlend, Action<int, int, float, bool> onApplied)
	{
		_sheetFileName = fileName ?? string.Empty;
		var (parsedType, parsedSlug) = TemplateIDHelper.ParseTemplateID(_sheetFileName);
		_objectType = !string.IsNullOrEmpty(parsedType) ? parsedType : "spritesheet";
		_slug = !string.IsNullOrEmpty(parsedSlug) ? parsedSlug : TemplateIDHelper.ToSnakeCase(_sheetFileName);
		_rtexAsset = fileName ?? string.Empty;

		_columns = Math.Max(1, initialCols);
		_rows = Math.Max(1, initialRows);
		_fps = initialFps > 0.001f ? initialFps : 20.0f;
		_subframeBlend = initialSubframeBlend;
		_onApplied = onApplied;
		_onAppliedWithTemplateIdAndRtex = null;

		TitleLabel.Text = $"{TranslationServer.Translate("Edit Spritesheet")} - {_sheetFileName}";

		if (_lblObjectTypePrefix != null) _lblObjectTypePrefix.Text = $"{_objectType}/";
		if (_txtSlug != null) _txtSlug.Text = _slug;
		_setRtexAssetValue?.Invoke(_rtexAsset);
		if (_spinCols != null) _spinCols.Value = _columns;
		if (_spinRows != null) _spinRows.Value = _rows;
		if (_spinFps != null) _spinFps.Value = _fps;
		if (_chkSubframeBlend != null) _chkSubframeBlend.ButtonPressed = _subframeBlend;

		OpenDialog();
	}

	public void OpenForSheet(string fileName, int initialCols, int initialRows, float initialFps, Action<int, int, float> onApplied)
	{
		OpenForSheet(fileName, initialCols, initialRows, initialFps, true, (cols, rows, fps, subframeBlend) => onApplied?.Invoke(cols, rows, fps));
	}

	public void OpenForSheet(string fileName, int initialCols, int initialRows, Action<int, int> onApplied)
	{
		OpenForSheet(fileName, initialCols, initialRows, 20.0f, (cols, rows, fps) => onApplied?.Invoke(cols, rows));
	}

	protected override void OnApply()
	{
		if (_spinCols != null)
		{
			_spinCols.Apply();
			if (int.TryParse(_spinCols.GetLineEdit()?.Text, out int c))
				_columns = Math.Clamp(c, (int)_spinCols.MinValue, (int)_spinCols.MaxValue);
			else
				_columns = (int)_spinCols.Value;
		}

		if (_spinRows != null)
		{
			_spinRows.Apply();
			if (int.TryParse(_spinRows.GetLineEdit()?.Text, out int r))
				_rows = Math.Clamp(r, (int)_spinRows.MinValue, (int)_spinRows.MaxValue);
			else
				_rows = (int)_spinRows.Value;
		}

		if (_spinFps != null)
		{
			_spinFps.Apply();
			if (float.TryParse(_spinFps.GetLineEdit()?.Text, out float f))
				_fps = Math.Clamp(f, (float)_spinFps.MinValue, (float)_spinFps.MaxValue);
			else
				_fps = (float)_spinFps.Value;
		}

		if (_chkSubframeBlend != null)
		{
			_subframeBlend = _chkSubframeBlend.ButtonPressed;
		}

		if (_txtRtexAsset != null)
		{
			_rtexAsset = _txtRtexAsset.Text?.Trim() ?? string.Empty;
		}

		string newTemplateID = string.IsNullOrEmpty(_objectType) ? _slug : $"{_objectType}/{_slug}";

		_onAppliedWithTemplateIdAndRtex?.Invoke(newTemplateID, _rtexAsset, _columns, _rows, _fps, _subframeBlend);
		_onApplied?.Invoke(_columns, _rows, _fps, _subframeBlend);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Spritesheet {0} updated to {1}x{2} @ {3} FPS."), newTemplateID, _columns, _rows, _fps));
	}
}
