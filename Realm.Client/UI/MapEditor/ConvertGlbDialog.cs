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

[GlobalClass]
public partial class ConvertGlbDialog : FloatingDialogBase
{
	public ConvertGlbDialog() { }

	private LineEdit _txtSourceFile = null!;
	private Button _btnSelectFile = null!;
	private LineEdit _txtReferenceImage = null!;
	private Button _btnSelectReferenceImage = null!;
	private Button _btnClearReferenceImage = null!;
	private OptionButton _optSubCategory = null!;
	private LineEdit _txtAssetName = null!;

	private CheckBox _chkTeamColorMask = null!;
	private HBoxContainer _colorPickerRow = null!;
	private ColorPickerButton _colorPicker = null!;
	private Button _btnDetectMaskColor = null!;
	private CheckBox _chkAutoCorrectChromaKey = null!;

	private CheckBox _chkAutoRig = null!;
	private VBoxContainer _autoRigRow = null!;

	private Label _lblStatus = null!;
	private ProgressBar _progressBar = null!;
	private bool _isConverting;
	private Action<string>? _onConvertedCallback;

	public ConvertGlbDialog(MapEditorHUD hud) : base(hud, TranslationServer.Translate("Convert 3D Model to Realm Format"), new Vector2(580, 480))
	{
		BuildDialogUi();
	}

	private void OnSelectFilePressed()
	{
		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Select 3D Model File to Convert to Realm Format"),
			PathUtils.GetProjectRoot(),
			"",
			false,
			DisplayServer.FileDialogMode.OpenFile,
			new[] { "*.glb,*.gltf,*.fbx,*.obj ; 3D Model Files (*.glb, *.gltf, *.fbx, *.obj)" },
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
			{
				if (status && selectedPaths.Length > 0)
				{
					_txtSourceFile.Text = selectedPaths[0];
					OnSourceFileChanged(selectedPaths[0]);
				}
			})
		);

		if (err != Error.Ok)
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Failed to show file dialog"));
		}
	}

	private void OnSelectReferenceImagePressed()
	{
		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Select Source Reference Image"),
			PathUtils.GetProjectRoot(),
			"",
			false,
			DisplayServer.FileDialogMode.OpenFile,
			new[] { "*.png,*.webp,*.jpg,*.jpeg,*.bmp,*.tga,*.rtex ; Image Files (*.png, *.webp, *.jpg, *.jpeg, *.bmp, *.tga, *.rtex)" },
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
			{
				if (status && selectedPaths.Length > 0)
				{
					_txtReferenceImage.Text = selectedPaths[0];
				}
			})
		);

		if (err != Error.Ok)
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Failed to show file dialog"));
		}
	}

	private void OnClearReferenceImagePressed()
	{
		_txtReferenceImage.Text = string.Empty;
	}

	private void OnDetectMaskColorPressed()
	{
		string sourcePath = _txtSourceFile?.Text?.Trim() ?? string.Empty;
		if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Please select a valid source 3D model file."));
			return;
		}

		string? detectedKey = GlbChromaKeyDetector.AutoDetectChromaKey(sourcePath);
		if (string.IsNullOrEmpty(detectedKey))
		{
			Hud?.ShowFeedback(TranslationServer.Translate("No dominant mask color detected."));
			return;
		}

		ApplyDetectedMaskColor(detectedKey);
	}

	private void ApplyDetectedMaskColor(string detectedKey)
	{
		if (_chkTeamColorMask != null && !_chkTeamColorMask.ButtonPressed)
		{
			_chkTeamColorMask.ButtonPressed = true;
			if (_colorPickerRow != null) _colorPickerRow.Visible = true;
		}

		if (_colorPicker != null)
		{
			_colorPicker.Color = Color.FromHtml(detectedKey);
		}

		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Detected mask color: {0}"), detectedKey));
	}

	private void OnColorPickerPopupClosed()
	{
		if (!ShouldAutoCorrectChromaKey()) return;

		string sourcePath = _txtSourceFile?.Text?.Trim() ?? string.Empty;
		if (!IsValidSourcePathForColorPicker(sourcePath)) return;

		string hexColor = $"#{_colorPicker!.Color.ToHtml(false)}";
		string? correctedKey = GlbChromaKeyMatcher.FindClosestMatchingChromaKey(sourcePath, hexColor);

		ApplyCorrectedChromaKey(correctedKey, hexColor);
	}

	private bool ShouldAutoCorrectChromaKey()
	{
		return _chkAutoCorrectChromaKey == null || _chkAutoCorrectChromaKey.ButtonPressed;
	}

	private bool IsValidSourcePathForColorPicker(string sourcePath)
	{
		if (string.IsNullOrEmpty(sourcePath)) return false;
		if (!File.Exists(sourcePath)) return false;
		if (_colorPicker == null) return false;
		return true;
	}

	private void ApplyCorrectedChromaKey(string? correctedKey, string hexColor)
	{
		if (string.IsNullOrEmpty(correctedKey)) return;
		if (string.Equals(correctedKey, hexColor, StringComparison.OrdinalIgnoreCase)) return;

		_colorPicker!.Color = Color.FromHtml(correctedKey);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Corrected mask color to texture: {0}"), correctedKey));
	}

	private void OnSourceFileChanged(string path)
	{
		if (string.IsNullOrWhiteSpace(path)) return;

		string resolvedName = GameContext.AssetIndexService?.ResolvePrettyFileName(path) ?? Path.GetFileName(path);
		string fileNameWithoutExt = Path.GetFileNameWithoutExtension(resolvedName);
		string cleanBase = fileNameWithoutExt.ToLowerInvariant().Replace(' ', '_');
		_txtAssetName.Text = cleanBase;

		if (_chkAutoCorrectChromaKey != null && _chkAutoCorrectChromaKey.ButtonPressed && File.Exists(path) && _colorPicker != null)
		{
			string hexColor = $"#{_colorPicker.Color.ToHtml(false)}";
			string? correctedKey = GlbChromaKeyMatcher.FindClosestMatchingChromaKey(path, hexColor);
			if (!string.IsNullOrEmpty(correctedKey) && !string.Equals(correctedKey, hexColor, StringComparison.OrdinalIgnoreCase))
			{
				_colorPicker.Color = Color.FromHtml(correctedKey);
			}
		}
	}

	public void OpenWithPreset(string? initialFilePath = null, string? initialSubCat = null, Action<string>? onConverted = null)
	{
		_onConvertedCallback = onConverted;
		_lblStatus.Visible = false;
		_progressBar.Visible = false;
		_progressBar.Value = 0;
		_txtReferenceImage.Text = string.Empty;

		if (!string.IsNullOrEmpty(initialSubCat))
		{
			_optSubCategory.Selected = initialSubCat.ToLowerInvariant() switch
			{
				"units" or "unit" or "characters" or "character" or "rmesh_characters" or "rmesh_units" => 0,
				"buildings" or "building" or "rmesh_buildings" => 1,
				"items" or "item" or "attachments" or "attachment" or "weapons" or "weapon" or "projectiles" or "projectile" or "rmesh_items" or "rmesh_attachments" or "rmesh_weapons" or "rmesh_projectiles" => 3,
				_ => 2
			};
		}

		if (!string.IsNullOrEmpty(initialFilePath))
		{
			_txtSourceFile.Text = initialFilePath;
			OnSourceFileChanged(initialFilePath);
		}
		else
		{
			_txtSourceFile.Text = string.Empty;
			_txtAssetName.Text = string.Empty;
		}

		ApplyCategoryDefaults();
		OpenDialog();
	}

	private void ApplyCategoryDefaults()
	{
		if (_optSubCategory == null) return;

		string subCat = GetSelectedSubCategoryForDefaults();
		bool teamColor = subCat is "characters" or "buildings";
		bool autoRig = subCat == "characters";

		ApplyTeamColorDefaults(teamColor);
		ApplyAutoRigDefaults(autoRig);
	}

	private string GetSelectedSubCategoryForDefaults()
	{
		return _optSubCategory!.Selected switch
		{
			0 => "characters",
			1 => "buildings",
			2 => "props",
			3 => "items",
			_ => "props"
		};
	}

	private void ApplyTeamColorDefaults(bool teamColor)
	{
		if (_chkTeamColorMask == null) return;

		_chkTeamColorMask.ButtonPressed = teamColor;
		if (_colorPickerRow != null) _colorPickerRow.Visible = teamColor;
		if (_chkAutoCorrectChromaKey != null) _chkAutoCorrectChromaKey.Visible = teamColor;
	}

	private void ApplyAutoRigDefaults(bool autoRig)
	{
		if (_chkAutoRig == null) return;

		_chkAutoRig.ButtonPressed = autoRig;
		if (_autoRigRow != null) _autoRigRow.Visible = autoRig;
	}

	private void SetProgressStatus(string message, float progressPercent, bool isError = false)
	{
		CallDeferred(nameof(ApplyProgressOnMainThread), message, progressPercent, isError);
	}

	private void ApplyProgressOnMainThread(string message, float progressPercent, bool isError)
	{
		if (_lblStatus != null)
		{
			_lblStatus.Text = message;
			_lblStatus.Visible = !string.IsNullOrEmpty(message);
			_lblStatus.AddThemeColorOverride("font_color", isError
				? new Color(0.9f, 0.35f, 0.35f)
				: new Color(0.6f, 0.9f, 0.6f));
		}

		if (_progressBar != null)
		{
			_progressBar.Value = progressPercent;
		}
	}

	private void SetConvertingState(bool converting)
	{
		_isConverting = converting;
		if (ApplyButton != null) ApplyButton.Disabled = converting;
		if (CancelButton != null) CancelButton.Disabled = converting;
		if (CloseButton != null) CloseButton.Disabled = converting;
		if (_btnSelectFile != null) _btnSelectFile.Disabled = converting;
		if (_btnSelectReferenceImage != null) _btnSelectReferenceImage.Disabled = converting;
		if (_btnClearReferenceImage != null) _btnClearReferenceImage.Disabled = converting;
		if (_btnDetectMaskColor != null) _btnDetectMaskColor.Disabled = converting;
		if (_progressBar != null) _progressBar.Visible = converting;
	}

	public override void ApplyAndClose()
	{
		CommitPendingInputFocus();
		OnApply();
	}

	protected override void OnApply()
	{
		if (_isConverting) return;

		string sourcePath = _txtSourceFile.Text?.Trim() ?? string.Empty;
		if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
		{
			ShowInvalidSourceFileError();
			return;
		}

		string assetName = GetAssetName(sourcePath);
		string cleanBase = assetName.ToLowerInvariant().Replace(' ', '_').Replace(".rmesh", "").Replace(".glb", "");
		string fileName = $"{cleanBase}.rmesh";

		string referenceImagePath = _txtReferenceImage.Text?.Trim() ?? string.Empty;
		if (!string.IsNullOrEmpty(referenceImagePath) && !File.Exists(referenceImagePath))
		{
			ShowInvalidReferenceImageError(referenceImagePath);
			return;
		}

		string subCategory = GetSubCategoryForApply();
		string wsPath = GameContext.EditorService._editingDirectory;
		string destDir = Path.Combine(wsPath, "Assets", "models", subCategory);
		Directory.CreateDirectory(destDir);
		string destPath = Path.Combine(destDir, fileName);

		bool doAutoRig = _chkAutoRig.ButtonPressed;
		bool doTeamColor = _chkTeamColorMask.ButtonPressed;
		bool autoCorrectChromaKey = _chkAutoCorrectChromaKey?.ButtonPressed ?? true;
		Color maskColor = _colorPicker.Color;

		SetConvertingState(true);
		SetProgressStatus(TranslationServer.Translate("Starting conversion..."), 2, false);

		StartConversionTask(sourcePath, cleanBase, fileName, subCategory, wsPath, destPath, doAutoRig, doTeamColor, autoCorrectChromaKey, maskColor, referenceImagePath);
	}

	private void ShowInvalidReferenceImageError(string referenceImagePath)
	{
		_lblStatus.Text = string.Format(TranslationServer.Translate("Reference image not found: {0}"), referenceImagePath);
		_lblStatus.AddThemeColorOverride("font_color", new Color(0.9f, 0.35f, 0.35f));
		_lblStatus.Visible = true;
	}

	private void ShowInvalidSourceFileError()
	{
		_lblStatus.Text = TranslationServer.Translate("Please select a valid source 3D model file.");
		_lblStatus.AddThemeColorOverride("font_color", new Color(0.9f, 0.35f, 0.35f));
		_lblStatus.Visible = true;
	}

	private string GetAssetName(string sourcePath)
	{
		string assetName = _txtAssetName.Text?.Trim() ?? string.Empty;
		if (string.IsNullOrEmpty(assetName))
		{
			string resolvedSource = GameContext.AssetIndexService?.ResolvePrettyFileName(sourcePath) ?? Path.GetFileName(sourcePath);
			return Path.GetFileNameWithoutExtension(resolvedSource).ToLowerInvariant().Replace(' ', '_');
		}

		return assetName;
	}

	private string GetSubCategoryForApply()
	{
		return _optSubCategory.Selected switch
		{
			0 => "units",
			1 => "buildings",
			2 => "props",
			3 => "attachments",
			_ => "props"
		};
	}

	private void HandleConversionError(string errorMessage)
	{
		_lblStatus.Text = errorMessage;
		_lblStatus.AddThemeColorOverride("font_color", new Color(0.9f, 0.35f, 0.35f));
		_lblStatus.Visible = true;
		_progressBar.Visible = false;
	}

}