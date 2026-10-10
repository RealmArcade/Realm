using Godot;
using Realm.Client.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Realm.Client.UI.MapEditor;

public partial class SpritesheetAssetEditDialog : Realm.Client.UI.MapEditor.FloatingDialogBase
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
		var list = ScanAvailableAssets("spritesheet_rtex", includeAllFolders);
		var rtexFiles = new HashSet<string>(list.Where(x => x.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
		return rtexFiles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private void InitializeBaseData(string fileName, int initialCols, int initialRows, float initialFps, bool initialSubframeBlend)
	{
		_sheetFileName = fileName ?? string.Empty;
		var (parsedType, parsedSlug) = TemplateIDHelper.ParseTemplateID(_sheetFileName);
		_objectType = !string.IsNullOrEmpty(parsedType) ? parsedType : "spritesheet";
		_slug = !string.IsNullOrEmpty(parsedSlug) ? parsedSlug : TemplateIDHelper.ToSnakeCase(_sheetFileName);

		_columns = Math.Max(1, initialCols);
		_rows = Math.Max(1, initialRows);
		_fps = initialFps > 0.001f ? initialFps : 20.0f;
		_subframeBlend = initialSubframeBlend;
	}

	private static string FormatRtexFilename(string texturePath)
	{
		if (texturePath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
			return Path.GetFileName(texturePath);
		
		return $"{Path.GetFileName(texturePath)}.rtex";
	}

	private string GetRtexFromMetadata(string wsPath, string sheetFileName, string slug)
	{
		if (!MetadataService.Instance.TryLoadMetadata(wsPath, out var meta))
			return string.Empty;

		if (meta.VfxSpritesheets == null)
			return string.Empty;

		if (meta.VfxSpritesheets.TryGetValue(sheetFileName, out var ssMeta))
		{
			if (ssMeta != null && !string.IsNullOrEmpty(ssMeta.TexturePath))
				return FormatRtexFilename(ssMeta.TexturePath);
		}

		if (meta.VfxSpritesheets.TryGetValue(slug, out var ssMeta2))
		{
			if (ssMeta2 != null && !string.IsNullOrEmpty(ssMeta2.TexturePath))
				return FormatRtexFilename(ssMeta2.TexturePath);
		}

		return string.Empty;
	}

	private string FindCandidateRtexAsset(string slug)
	{
		var candidates = ScanRtexAssets(true);
		string candidateMatch = candidates.FirstOrDefault(c => string.Equals(c, $"{slug}.rtex", StringComparison.OrdinalIgnoreCase))
		                        ?? candidates.FirstOrDefault(c => string.Equals(Path.GetFileNameWithoutExtension(c), slug, StringComparison.OrdinalIgnoreCase));
		return candidateMatch ?? string.Empty;
	}

	private void UpdateUIAndOpen()
	{
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

	private string ResolveRtexAsset(string providedAsset, string wsPath, string sheetFileName, string slug, bool searchCandidates)
	{
		if (!string.IsNullOrEmpty(providedAsset))
		{
			return providedAsset.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(providedAsset) : $"{Path.GetFileName(providedAsset)}.rtex";
		}

		string metadataAsset = GetRtexFromMetadata(wsPath, sheetFileName, slug);
		if (!string.IsNullOrEmpty(metadataAsset))
		{
			return metadataAsset;
		}

		if (searchCandidates)
		{
			return FindCandidateRtexAsset(slug);
		}

		return string.Empty;
	}

	public void OpenForSheet(string fullTemplateId, string rtexAsset, int initialCols, int initialRows, float initialFps, bool initialSubframeBlend, Action<string, string, int, int, float, bool> onApplied)
	{
		InitializeBaseData(fullTemplateId, initialCols, initialRows, initialFps, initialSubframeBlend);
		_onAppliedWithTemplateIdAndRtex = onApplied;
		_onApplied = null;

		_rtexAsset = ResolveRtexAsset(rtexAsset, Services.MapWorkspaceService.GetActiveWorkspacePath(), _sheetFileName, _slug, true);

		UpdateUIAndOpen();
	}

	public void OpenForSheet(string fileName, int initialCols, int initialRows, float initialFps, bool initialSubframeBlend, Action<int, int, float, bool> onApplied)
	{
		InitializeBaseData(fileName, initialCols, initialRows, initialFps, initialSubframeBlend);
		_onApplied = onApplied;
		_onAppliedWithTemplateIdAndRtex = null;

		string providedAsset = !string.IsNullOrEmpty(fileName) && fileName.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? fileName : string.Empty;
		_rtexAsset = ResolveRtexAsset(providedAsset, Services.MapWorkspaceService.GetActiveWorkspacePath(), _sheetFileName, _slug, false);

		UpdateUIAndOpen();
	}

	public void OpenForSheet(string fileName, int initialCols, int initialRows, float initialFps, Action<int, int, float> onApplied)
	{
		OpenForSheet(fileName, initialCols, initialRows, initialFps, true, (cols, rows, fps, subframeBlend) => onApplied?.Invoke(cols, rows, fps));
	}

	public void OpenForSheet(string fileName, int initialCols, int initialRows, Action<int, int> onApplied)
	{
		OpenForSheet(fileName, initialCols, initialRows, 20.0f, (cols, rows, fps) => onApplied?.Invoke(cols, rows));
	}

	private int GetSpinBoxInt(SpinBox spinBox, int fallback)
	{
		if (spinBox == null) return fallback;
		spinBox.Apply();
		if (int.TryParse(spinBox.GetLineEdit()?.Text, out int result))
			return Math.Clamp(result, (int)spinBox.MinValue, (int)spinBox.MaxValue);
		return (int)spinBox.Value;
	}

	private float GetSpinBoxFloat(SpinBox spinBox, float fallback)
	{
		if (spinBox == null) return fallback;
		spinBox.Apply();
		if (float.TryParse(spinBox.GetLineEdit()?.Text, out float result))
			return Math.Clamp(result, (float)spinBox.MinValue, (float)spinBox.MaxValue);
		return (float)spinBox.Value;
	}

	protected override void OnApply()
	{
		_columns = GetSpinBoxInt(_spinCols, _columns);
		_rows = GetSpinBoxInt(_spinRows, _rows);
		_fps = GetSpinBoxFloat(_spinFps, _fps);

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