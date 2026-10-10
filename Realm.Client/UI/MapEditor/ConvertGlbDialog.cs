using Godot;
using Realm.Client.Services;
using Realm.Shared;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Realm.Client.UI.MapEditor;

public partial class ConvertGlbDialog : FloatingDialogBase
{
	private LineEdit _txtSourceFile;
	private Button _btnSelectFile;
	private OptionButton _optSubCategory;
	private LineEdit _txtAssetName;

	private CheckBox _chkTeamColorMask;
	private HBoxContainer _colorPickerRow;
	private ColorPickerButton _colorPicker;
	private Button _btnDetectMaskColor;
	private CheckBox _chkAutoCorrectChromaKey;

	private CheckBox _chkAutoRig;
	private VBoxContainer _autoRigRow;

	private Label _lblStatus;
	private ProgressBar _progressBar;
	private bool _isConverting;
	private Action<string>? _onConvertedCallback;

	public ConvertGlbDialog(MapEditorHUD hud) : base(hud, TranslationServer.Translate("Convert 3D Model to Realm Format"), new Vector2(580, 480))
	{
		BuildDialogUi();
	}

	private void BuildDialogUi()
	{
		var btnWebAi = new Button();
		btnWebAi.Set("icon_max_width", 16);
		btnWebAi.AddThemeConstantOverride("icon_max_width", 16);
		btnWebAi.ExpandIcon = false;
		btnWebAi.IconAlignment = HorizontalAlignment.Center;
		btnWebAi.VerticalIconAlignment = VerticalAlignment.Center;
		if (ResourceLoader.Exists("res://Assets/UI/globe_icon.png"))
		{
			btnWebAi.Icon = GD.Load<Texture2D>("res://Assets/UI/globe_icon.png");
		}
		else
		{
			btnWebAi.Text = "🌐";
		}
		btnWebAi.TooltipText = TranslationServer.Translate("Generate 3D Model with AI Online");
		btnWebAi.CustomMinimumSize = new Vector2(24, 24);
		btnWebAi.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		btnWebAi.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		btnWebAi.FocusMode = FocusModeEnum.None;
		btnWebAi.Pressed += () => OS.ShellOpen("https://3d.hunyuanglobal.com");
		HeaderHBox.AddChild(btnWebAi);
		HeaderHBox.MoveChild(btnWebAi, HeaderHBox.GetChildCount() - 2);

		var scroll = new ScrollContainer();
		scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		BodyContainer.AddChild(scroll);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 10);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.AddChild(vbox);

		AddSectionHeader(vbox, TranslationServer.Translate("SOURCE 3D MODEL"));

		var fileRow = new HBoxContainer();
		fileRow.AddThemeConstantOverride("separation", 6);

		var lblFile = new Label();
		lblFile.Text = TranslationServer.Translate("Model File:");
		lblFile.CustomMinimumSize = new Vector2(120, 0);
		lblFile.AddThemeFontSizeOverride("font_size", 11);
		fileRow.AddChild(lblFile);

		_txtSourceFile = new LineEdit();
		_txtSourceFile.PlaceholderText = TranslationServer.Translate("Select a .glb, .gltf, .fbx, or .obj file...");
		_txtSourceFile.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_txtSourceFile.AddThemeFontSizeOverride("font_size", 11);
		_txtSourceFile.TextChanged += OnSourceFileChanged;
		fileRow.AddChild(_txtSourceFile);

		_btnSelectFile = new Button();
		_btnSelectFile.Set("icon_max_width", 0);
		_btnSelectFile.Text = "📂 " + TranslationServer.Translate("Browse...");
		_btnSelectFile.AddThemeFontSizeOverride("font_size", 11);
		_btnSelectFile.CustomMinimumSize = new Vector2(90, 24);
		_btnSelectFile.FocusMode = FocusModeEnum.None;
		_btnSelectFile.Pressed += OnSelectFilePressed;
		fileRow.AddChild(_btnSelectFile);

		vbox.AddChild(fileRow);

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
		_colorPickerRow.AddThemeConstantOverride("separation", 8);
		_colorPickerRow.Visible = false;

		var lblColor = new Label();
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
		_autoRigRow.AddThemeConstantOverride("separation", 4);
		_autoRigRow.Visible = false;

		var lblRigDesc = new Label();
		lblRigDesc.Text = TranslationServer.Translate("Attaches a humanoid bone skeleton to unrigged humanoid meshes for compatibility with Realm animations.");
		lblRigDesc.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		lblRigDesc.AddThemeFontSizeOverride("font_size", 10);
		lblRigDesc.AutowrapMode = TextServer.AutowrapMode.Word;
		_autoRigRow.AddChild(lblRigDesc);

		vbox.AddChild(_autoRigRow);

		_progressBar = new ProgressBar();
		_progressBar.MinValue = 0;
		_progressBar.MaxValue = 100;
		_progressBar.Value = 0;
		_progressBar.ShowPercentage = true;
		_progressBar.CustomMinimumSize = new Vector2(0, 18);
		_progressBar.Visible = false;
		BodyContainer.AddChild(_progressBar);

		_lblStatus = new Label();
		_lblStatus.AddThemeFontSizeOverride("font_size", 11);
		_lblStatus.AddThemeColorOverride("font_color", new Color(0.9f, 0.4f, 0.4f));
		_lblStatus.AutowrapMode = TextServer.AutowrapMode.Word;
		_lblStatus.Visible = false;
		BodyContainer.AddChild(_lblStatus);

		ApplyButton.Text = "🔄 " + TranslationServer.Translate("Convert & Import");
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

	private void OnDetectMaskColorPressed()
	{
		string sourcePath = _txtSourceFile?.Text?.Trim() ?? string.Empty;
		if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Please select a valid source 3D model file."));
			return;
		}

		string? detectedKey = GlbPlayerColorProcessor.AutoDetectChromaKey(sourcePath);
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
		string? correctedKey = GlbPlayerColorProcessor.FindClosestMatchingChromaKey(sourcePath, hexColor);
		
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

		string resolvedName = AssetIndexService.Instance?.ResolvePrettyFileName(path) ?? Path.GetFileName(path);
		string fileNameWithoutExt = Path.GetFileNameWithoutExtension(resolvedName);
		string cleanBase = fileNameWithoutExt.ToLowerInvariant().Replace(' ', '_');
		_txtAssetName.Text = cleanBase;

		if (_chkAutoCorrectChromaKey != null && _chkAutoCorrectChromaKey.ButtonPressed && File.Exists(path) && _colorPicker != null)
		{
			string hexColor = $"#{_colorPicker.Color.ToHtml(false)}";
			string? correctedKey = GlbPlayerColorProcessor.FindClosestMatchingChromaKey(path, hexColor);
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
		if (_btnDetectMaskColor != null) _btnDetectMaskColor.Disabled = converting;
		if (_progressBar != null) _progressBar.Visible = converting;
	}

	public override void ApplyAndClose()
	{
		CommitPendingInputFocus();
		OnApply();
		// Do NOT call CloseDialog() here — the dialog closes itself via
		// OnConversionFinished once the background conversion task is done.
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

		string subCategory = GetSubCategoryForApply();
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string destDir = Path.Combine(wsPath, "Assets", "models", subCategory);
		Directory.CreateDirectory(destDir);
		string destPath = Path.Combine(destDir, fileName);

		bool doAutoRig = _chkAutoRig.ButtonPressed;
		bool doTeamColor = _chkTeamColorMask.ButtonPressed;
		bool autoCorrectChromaKey = _chkAutoCorrectChromaKey?.ButtonPressed ?? true;
		Color maskColor = _colorPicker.Color;

		SetConvertingState(true);
		SetProgressStatus(TranslationServer.Translate("Starting conversion..."), 2, false);

		StartConversionTask(sourcePath, cleanBase, fileName, subCategory, wsPath, destPath, doAutoRig, doTeamColor, autoCorrectChromaKey, maskColor);
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
			string resolvedSource = AssetIndexService.Instance?.ResolvePrettyFileName(sourcePath) ?? Path.GetFileName(sourcePath);
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

	private void StartConversionTask(string sourcePath, string cleanBase, string fileName, string subCategory, string wsPath, string destPath, bool doAutoRig, bool doTeamColor, bool autoCorrectChromaKey, Color maskColor)
	{
		Task.Run(() =>
		{
			string tempWorkingDir = Path.Combine(Path.GetTempPath(), $"realm_glb_conv_{Guid.NewGuid():N}");
			Directory.CreateDirectory(tempWorkingDir);
			string? errorMessage = null;
			string? resultPath = null;

			try
			{
				string currentPath = Path.Combine(tempWorkingDir, Path.GetFileName(sourcePath));
				File.Copy(sourcePath, currentPath, true);

				if (doAutoRig)
				{
					if (!PerformAutoRigging(ref currentPath, tempWorkingDir, cleanBase, out errorMessage)) return;
				}

				string? chromaKeyHex = null;
				if (doTeamColor)
				{
					if (!PerformTeamColorMasking(ref currentPath, tempWorkingDir, cleanBase, maskColor, autoCorrectChromaKey, doAutoRig, ref chromaKeyHex, out errorMessage)) return;
				}

				if (!PerformModelConversion(currentPath, destPath, subCategory, chromaKeyHex, out errorMessage, out byte[]? outputBytes)) return;

				PerformFinalMetadataSave(destPath, fileName, subCategory, wsPath, outputBytes, out resultPath);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ConvertGlbDialog] Conversion error: {ex.Message}");
				errorMessage = string.Format(TranslationServer.Translate("Conversion error: {0}"), ex.Message);
			}
			finally
			{
				CleanupTempDir(tempWorkingDir);
			}

			CallDeferred(nameof(OnConversionFinished), resultPath ?? string.Empty, errorMessage ?? string.Empty);
		});
	}

	private bool PerformAutoRigging(ref string currentPath, string tempWorkingDir, string cleanBase, out string? errorMessage)
	{
		errorMessage = null;
		SetProgressStatus(TranslationServer.Translate("Step 1/4: Auto-rigging skeleton..."), 10, false);
		string riggedPath = Path.Combine(tempWorkingDir, $"{cleanBase}_rigged.glb");
		var rigResult = GlbAutoRigger.RigHumanoid(currentPath, riggedPath, new GlbAutoRiggerOptions
		{
			LogCallback = (msg) => GD.Print($"[ConvertGlb] {msg}")
		});

		if (!rigResult.Success)
		{
			errorMessage = string.Format(TranslationServer.Translate("Auto-rigging failed: {0}"), rigResult.ErrorMessage);
			return false;
		}
		currentPath = riggedPath;
		return true;
	}

	private bool PerformTeamColorMasking(ref string currentPath, string tempWorkingDir, string cleanBase, Color maskColor, bool autoCorrectChromaKey, bool doAutoRig, ref string? chromaKeyHex, out string? errorMessage)
	{
		errorMessage = null;
		SetProgressStatus(TranslationServer.Translate("Step 2/4: Applying team color mask..."), doAutoRig ? 25 : 15, false);
		string maskedPath = Path.Combine(tempWorkingDir, $"{cleanBase}_masked.glb");
		string hexColor = $"#{maskColor.ToHtml(false)}";
		chromaKeyHex = hexColor;
		var maskResult = GlbPlayerColorProcessor.ProcessFile(currentPath, maskedPath, new GlbPlayerColorOptions
		{
			ChromaKey = hexColor,
			AutoCorrectChromaKey = autoCorrectChromaKey
		});

		if (!maskResult.Success)
		{
			errorMessage = string.Format(TranslationServer.Translate("Team color mask failed: {0}"), maskResult.ErrorMessage);
			return false;
		}
		if (!string.IsNullOrEmpty(maskResult.DetectedChromaKey))
		{
			chromaKeyHex = maskResult.DetectedChromaKey;
		}
		currentPath = maskedPath;
		return true;
	}

	private bool PerformModelConversion(string currentPath, string destPath, string subCategory, string? chromaKeyHex, out string? errorMessage, out byte[]? outputBytes)
	{
		errorMessage = null;
		outputBytes = null;
		SetProgressStatus(TranslationServer.Translate("Step 3/4: Optimizing geometry & packaging RMESH..."), 40, false);
		int maxRes = subCategory is "attachments" or "items" ? 512 : 1024;
		string canonicalAssetType = GetCanonicalAssetType(subCategory);

		var convRes = Realm.Shared.ModelOptimization.ModelConverter.ConvertToRmesh(
			currentPath,
			destPath,
			canonicalAssetType,
			force: true,
			options: new Realm.Shared.OptimizationOptions
			{
				SimplificationRatio = 0.5f,
				MaxTextureResolution = maxRes,
				ForceReDecimate = true
			},
			chromaKey: chromaKeyHex);

		if (!convRes.Success)
		{
			errorMessage = string.Format(TranslationServer.Translate("Conversion failed: {0}"), convRes.ErrorMessage);
			return false;
		}
		
		outputBytes = convRes.OutputBytes;
		return true;
	}

	private string GetCanonicalAssetType(string subCategory)
	{
		return subCategory switch
		{
			"units" => "Character",
			"buildings" => "Building",
			"attachments" or "items" => "Item",
			_ => "Prop"
		};
	}

	private void PerformFinalMetadataSave(string destPath, string fileName, string subCategory, string wsPath, byte[]? outputBytes, out string? resultPath)
	{
		SetProgressStatus(TranslationServer.Translate("Step 4/4: Computing bounds & saving metadata..."), 80, false);

		string hash = outputBytes != null
			? RealmMetadataHelper.ComputeBlake3(outputBytes, ".rmesh")
			: RealmMetadataHelper.ComputeBlake3(destPath);

		string canonicalCat = subCategory switch
		{
			"units" => "Character",
			"buildings" => "Building",
			"attachments" or "items" or "weapons" or "projectiles" => "Item",
			_ => "Prop"
		};
		MapAssetHelper.UpdateManifestAsset(wsPath, canonicalCat, fileName, hash);

		resultPath = destPath;
	}

	private void CleanupTempDir(string tempWorkingDir)
	{
		try
		{
			if (Directory.Exists(tempWorkingDir))
				Directory.Delete(tempWorkingDir, true);
		}
		catch { }
	}
	private void OnConversionFinished(string resultPath, string errorMessage)
	{
		SetConvertingState(false);

		if (!string.IsNullOrEmpty(errorMessage))
		{
			HandleConversionError(errorMessage);
			return;
		}

		_progressBar.Value = 100;

		string fileName = Path.GetFileName(resultPath);
		string subCategory = GetSubCategoryForApply();
		float defaultScale = GetDefaultScaleForSubCategory(subCategory);

		try
		{
			CalculateAndSaveMetadata(resultPath, fileName, subCategory, defaultScale);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ConvertGlbDialog] Bounds calculation error: {ex.Message}");
		}

		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Converted and imported 3D model '{0}'!"), fileName));
		AssetIndexService.Instance.RescanAllDirectories();
		_onConvertedCallback?.Invoke(resultPath);
		CloseDialog();
	}

	private void HandleConversionError(string errorMessage)
	{
		_lblStatus.Text = errorMessage;
		_lblStatus.AddThemeColorOverride("font_color", new Color(0.9f, 0.35f, 0.35f));
		_lblStatus.Visible = true;
		_progressBar.Visible = false;
	}

	private float GetDefaultScaleForSubCategory(string subCategory)
	{
		return subCategory switch
		{
			"resources" => 2.75f,
			"buildings" => 1.5f,
			"props" => 1.25f,
			"units" => 1.0f,
			"attachments" or "items" => 1.0f,
			_ => 1.0f
		};
	}

	private void CalculateAndSaveMetadata(string resultPath, string fileName, string subCategory, float defaultScale)
	{
		var (minY, autoYOffset) = ModelCache.CalculateModelBounds(resultPath, defaultScale);
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string unitId = Path.GetFileNameWithoutExtension(fileName);

		MetadataService.Instance.UpdateMetadata(wsPath, meta =>
		{
			meta.SetModelYOffset(fileName, autoYOffset);
			meta.SetModelScale(fileName, defaultScale);

			UpdateSpecificMetadata(meta, subCategory, unitId, fileName, defaultScale, autoYOffset);
		});

		MetadataService.Instance.CleanMetadata(wsPath);
		Realm.Client.Core.GameHost.Instance?.SetModelYOffset(fileName, autoYOffset);
		Realm.Client.Core.GameHost.Instance?.SetModelScale(fileName, defaultScale);
		Realm.Client.Core.GameHost.Instance?.FlushModelYOffsetSave();
		Realm.Client.Core.GameHost.Instance?.LoadUnitMetadata(wsPath);
	}

	private void UpdateSpecificMetadata(MapMetadata meta, string subCategory, string unitId, string fileName, float defaultScale, float autoYOffset)
	{
		switch (subCategory)
		{
			case "units" or "characters":
				UpdateUnitMetadata(meta, unitId, fileName, defaultScale, autoYOffset);
				break;
			case "buildings":
				UpdateBuildingMetadata(meta, unitId, fileName, defaultScale, autoYOffset);
				break;
			case "resources":
				UpdateResourceMetadata(meta, unitId, fileName, defaultScale, autoYOffset);
				break;
			case "props":
				UpdatePropMetadata(meta, unitId, fileName, defaultScale, autoYOffset);
				break;
			case "attachments" or "items":
				UpdateItemMetadata(meta, unitId);
				break;
		}
	}

	private void UpdateUnitMetadata(MapMetadata meta, string unitId, string fileName, float defaultScale, float autoYOffset)
	{
		string unitTemplateId = TemplateIDHelper.NormalizeTemplateID("unit", unitId);
		bool updatedU = meta.UpdateUnit(unitTemplateId, u =>
		{
			if (autoYOffset != 0f) u.YOffset = autoYOffset;
			return u;
		});
		if (!updatedU)
		{
			meta.AddOrUpdateUnit(new UnitMetadata
			{
				TemplateID = unitTemplateId,
				Name = unitId,
				Description = "",
				ModelPath = fileName,
				Scale = defaultScale,
				YOffset = autoYOffset,
				PathingType = 9,
				DespillPlayerColor = false,
				NormalizeLuminance = true
			});
		}
	}

	private void UpdateBuildingMetadata(MapMetadata meta, string unitId, string fileName, float defaultScale, float autoYOffset)
	{
		string buildingTemplateId = TemplateIDHelper.NormalizeTemplateID("building", unitId);
		bool updatedB = meta.UpdateBuilding(buildingTemplateId, b =>
		{
			if (autoYOffset != 0f) b.YOffset = autoYOffset;
			return b;
		});
		if (!updatedB)
		{
			meta.AddOrUpdateBuilding(new UnitMetadata
			{
				TemplateID = buildingTemplateId,
				Name = unitId,
				Description = "",
				ModelPath = fileName,
				Scale = defaultScale,
				YOffset = autoYOffset,
				PathingType = 32,
				DespillPlayerColor = false,
				NormalizeLuminance = true
			});
		}
	}

	private void UpdateResourceMetadata(MapMetadata meta, string unitId, string fileName, float defaultScale, float autoYOffset)
	{
		string resourceTemplateId = TemplateIDHelper.NormalizeTemplateID("resource", unitId);
		bool updatedR = meta.UpdateResource(resourceTemplateId, r =>
		{
			if (autoYOffset != 0f) r.YOffset = autoYOffset;
			return r;
		});
		if (!updatedR)
		{
			meta.AddOrUpdateResource(new ResourceMetadata
			{
				TemplateID = resourceTemplateId,
				Name = unitId,
				Description = "",
				ModelPath = fileName,
				Scale = defaultScale,
				YOffset = autoYOffset,
				PathingType = 255,
				DespillPlayerColor = false,
				NormalizeLuminance = true,
				IgnorePlayerColor = true
			});
		}
	}

	private void UpdatePropMetadata(MapMetadata meta, string unitId, string fileName, float defaultScale, float autoYOffset)
	{
		string propTemplateId = TemplateIDHelper.NormalizeTemplateID("prop", unitId);
		bool updatedP = meta.UpdateProp(propTemplateId, p =>
		{
			if (autoYOffset != 0f) p.YOffset = autoYOffset;
			return p;
		});
		if (!updatedP)
		{
			meta.AddOrUpdateProp(new PropMetadata
			{
				TemplateID = propTemplateId,
				Name = unitId,
				Description = "",
				ModelPath = fileName,
				Scale = defaultScale,
				YOffset = autoYOffset,
				PathingType = 255,
				DespillPlayerColor = false,
				NormalizeLuminance = true,
				IgnorePlayerColor = true
			});
		}
	}

	private void UpdateItemMetadata(MapMetadata meta, string unitId)
	{
		string itemTemplateId = TemplateIDHelper.NormalizeTemplateID("item", unitId);
		bool updatedI = meta.UpdateItem(itemTemplateId, i => i);
		if (!updatedI)
		{
			meta.AddOrUpdateItem(new ItemMetadata
			{
				TemplateID = itemTemplateId,
				Name = unitId,
				Description = "",
				ItemClass = "consumable",
				CanDrop = true
			});
		}
	}
}