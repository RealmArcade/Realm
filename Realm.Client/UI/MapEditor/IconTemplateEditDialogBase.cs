using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Services;
using Realm.Shared.Metadata;

namespace Realm.Client.UI.MapEditor;

/// <summary>
///     The part of a template edit dialog that every icon-bearing template shares: the template id it edits, the display
///     name, the icon asset and the rich tooltip, plus the flow that snapshots the template and puts it back on cancel.
///     What the template actually owns - an item's ground model and granted capabilities, an upgrade's text - is left to
///     the subclass, which is why an item and an upgrade no longer share one screen.
/// </summary>
public abstract partial class IconTemplateEditDialogBase : FloatingDialogBase
{
	protected IconTemplateEditDialogBase() { }

	protected IconTemplateEditDialogBase(MapEditorHUD hud, string title, Vector2 size, string category, string objectType)
		: base(hud, title, size)
	{
		_category = category;
		_objectType = objectType;
		BuildControls();
	}

	protected readonly string _category = string.Empty;
	protected readonly string _objectType = string.Empty;
	protected string _slug = string.Empty;
	protected string _originalTemplateID = string.Empty;
	protected string _name = string.Empty;
	protected string _iconPath = string.Empty;
	protected string _tooltip = string.Empty;
	protected Action<string, string>? _onAppliedCallback;

	protected VBoxContainer ContentVBox = null!;

	private string _initialName = string.Empty;
	private string _initialIconPath = string.Empty;
	private string _initialTooltip = string.Empty;
	private string? _initialTemplateJson;
	private bool _templateExistedInitially;

	private Label _lblObjectTypePrefix = null!;
	private LineEdit _txtSlug = null!;
	private Label _lblSlugValidation = null!;
	private LineEdit _txtName = null!;
	private Action<string> _setIconPathValue = null!;
	private LineEdit _txtTooltip = null!;
	private VBoxContainer _previewContainer = null!;

	private void BuildControls()
	{
		AddDescription(BodyContainer, TranslationServer.Translate("Modify the identity, icon asset and rich formatted tooltip of this template."));

		var scrollBody = CreateScrollBody(400);
		ContentVBox = CreateExpandingContentColumn();
		scrollBody.AddChild(ContentVBox);

		BuildIdentitySection();
		BuildTooltipSection();
		BuildTooltipPreviewSection();
		BuildCategorySections(ContentVBox);
	}

	private void BuildIdentitySection()
	{
		AddSectionHeader(ContentVBox, "🆔 " + TranslationServer.Translate("IDENTITY & ICON"), new Color(0.95f, 0.8f, 0.4f));

		var idRow = new HBoxContainer();
		idRow.Name = "IdRow_HBoxContainer";
		idRow.AddThemeConstantOverride("separation", 6);

		var lblId = new Label();
		lblId.Name = "LblId_Label";
		lblId.Text = TranslationServer.Translate("TemplateID:");
		lblId.CustomMinimumSize = new Vector2(120, 0);
		lblId.AddThemeFontSizeOverride("font_size", 11);
		idRow.AddChild(lblId);

		_lblObjectTypePrefix = new Label();
		_lblObjectTypePrefix.Name = "LblObjectTypePrefix_Label";
		_lblObjectTypePrefix.Text = $"{_objectType}/";
		_lblObjectTypePrefix.AddThemeFontSizeOverride("font_size", 11);
		_lblObjectTypePrefix.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		idRow.AddChild(_lblObjectTypePrefix);

		_txtSlug = CreateSlugField("snake_case_slug");
		_txtSlug.TextChanged += (val) =>
		{
			_slug = TemplateIDHelper.ToSnakeCase(val);
			ValidateSlug();
		};
		idRow.AddChild(_txtSlug);
		ContentVBox.AddChild(idRow);

		_lblSlugValidation = new Label();
		_lblSlugValidation.Name = "LblSlugValidation_Label";
		_lblSlugValidation.AddThemeFontSizeOverride("font_size", 10);
		_lblSlugValidation.AddThemeColorOverride("font_color", new Color(0.95f, 0.4f, 0.4f));
		_lblSlugValidation.Visible = false;
		ContentVBox.AddChild(_lblSlugValidation);

		_txtName = AddTextInput(
			ContentVBox,
			TranslationServer.Translate("Display Name:"),
			_name,
			(val) => _name = val ?? string.Empty,
			TranslationServer.Translate("Display name..."),
			120f
		);

		(_, _setIconPathValue) = AddAssetFilterDropdown(
			ContentVBox,
			TranslationServer.Translate("Icon (.rtex):"),
			_iconPath,
			(all) => ScanIconAssets(all),
			(val) =>
			{
				_iconPath = val ?? string.Empty;
				UpdateTooltipPreview();
			},
			TranslationServer.Translate("Select icon .rtex..."),
			120f
		);
	}

	private void BuildTooltipSection()
	{
		AddSectionHeader(ContentVBox, "💬 " + TranslationServer.Translate("TOOLTIP & DESCRIPTION"), new Color(0.4f, 0.85f, 0.5f));

		_txtTooltip = AddTextInput(
			ContentVBox,
			TranslationServer.Translate("Tooltip Text:"),
			_tooltip,
			(val) =>
			{
				_tooltip = val ?? string.Empty;
				UpdateTooltipPreview();
			},
			TranslationServer.Translate("Tooltip text, e.g. <b>Bold</b> or <color=#FFD700>Gold</color>"),
			120f
		);
	}

	private void BuildTooltipPreviewSection()
	{
		AddSectionHeader(ContentVBox, "👁️ " + TranslationServer.Translate("TOOLTIP PREVIEW"), new Color(0.35f, 0.75f, 0.9f));
		AddDescription(ContentVBox, TranslationServer.Translate("Custom syntax supported: <b>Bold</b>, <i>Italics</i>, <color=#RRGGBB>Colors</color>, and standard BBCode tags."));

		var previewPanel = new PanelContainer();
		previewPanel.Name = "PreviewPanel_PanelContainer";
		previewPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		previewPanel.CustomMinimumSize = new Vector2(0, 80);

		var previewMargin = new MarginContainer();
		previewMargin.Name = "PreviewMargin_MarginContainer";
		previewMargin.AddThemeConstantOverride("margin_left", 8);
		previewMargin.AddThemeConstantOverride("margin_right", 8);
		previewMargin.AddThemeConstantOverride("margin_top", 8);
		previewMargin.AddThemeConstantOverride("margin_bottom", 8);

		_previewContainer = new VBoxContainer();
		_previewContainer.Name = "PreviewContainer_VBoxContainer";
		_previewContainer.AddThemeConstantOverride("separation", 6);
		previewMargin.AddChild(_previewContainer);
		previewPanel.AddChild(previewMargin);

		ContentVBox.AddChild(previewPanel);
	}

	protected abstract void BuildCategorySections(VBoxContainer content);

	protected abstract void LoadTemplateProperties(MapMetadata meta, string templateId);

	protected abstract void ApplyTemplateProperties(MapMetadata meta, string newTemplateID);

	protected abstract bool TemplateIdExists(MapMetadata meta, string fullId);

	protected virtual void RefreshCategoryControls() { }

	protected virtual void RestoreCategoryState() { }

	protected virtual void CaptureCategoryState() { }

	protected void UpdateTooltipPreview()
	{
		if (_previewContainer == null) return;

		foreach (Node child in _previewContainer.GetChildren())
		{
			child.QueueFree();
		}

		Control tooltipWidget = RichTooltip.Create(_tooltip, useStonePanel: false);
		_previewContainer.AddChild(tooltipWidget);
	}

	protected static string FormatIconPath(string? path)
	{
		if (string.IsNullOrEmpty(path)) return string.Empty;
		return path.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(path) : $"{Path.GetFileName(path)}.rtex";
	}

	protected List<string> ScanIconAssets(bool includeAllFolders)
	{
		var results = new HashSet<string>(ScanAvailableAssets("icons", includeAllFolders), StringComparer.OrdinalIgnoreCase);

		string searchDir = Path.Combine(MapWorkspaceService.GetActiveWorkspacePath(), "Assets", "icons");
		if (Directory.Exists(searchDir))
		{
			foreach (var file in Directory.GetFiles(searchDir, "*.rtex", SearchOption.AllDirectories))
			{
				results.Add(Path.GetFileName(file));
			}
		}

		return results.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
	}

	public void OpenForObject(string category, string templateId, Action<string, string>? onApplied = null)
	{
		InitializeState(category, templateId, onApplied);
		LoadMetadataProperties();
		EnsureIconPath();
		UpdateUI();

		ValidateSlug();
		UpdateTooltipPreview();

		_initialName = _name;
		_initialIconPath = _iconPath;
		_initialTooltip = _tooltip;
		CaptureCategoryState();

		string targetId = !string.IsNullOrEmpty(_originalTemplateID) ? _originalTemplateID : $"{_objectType}/{_slug}";
		_initialTemplateJson = GameContext.EditorService.SnapshotTemplateJson(targetId);
		_templateExistedInitially = _initialTemplateJson != null;

		OpenDialog();
	}

	protected override string GetRandomizeTemplateSlug()
	{
		return !string.IsNullOrEmpty(_originalTemplateID) ? _originalTemplateID : $"{_objectType}/{_slug}";
	}

	protected override void OnAssetsRandomized()
	{
		LoadMetadataProperties();
		UpdateUI();
		UpdateTooltipPreview();
	}

	protected override void OnCancel()
	{
		_name = _initialName;
		_iconPath = _initialIconPath;
		_tooltip = _initialTooltip;
		RestoreCategoryState();

		string targetId = !string.IsNullOrEmpty(_originalTemplateID) ? _originalTemplateID : $"{_objectType}/{_slug}";
		if (!string.IsNullOrEmpty(targetId))
		{
			GameContext.EditorService.RevertTemplateJson(targetId, _templateExistedInitially ? _initialTemplateJson : null);
		}
	}

	protected override void OnApply()
	{
		if (string.IsNullOrWhiteSpace(_slug))
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Cannot save: Slug is required."));
			return;
		}

		string newTemplateID = $"{_objectType}/{_slug}";
		string workspacePath = MapWorkspaceService.GetActiveWorkspacePath();

		GameContext.MetadataService.UpdateMetadata(workspacePath, meta => ApplyTemplateProperties(meta, newTemplateID));

		_onAppliedCallback?.Invoke(_originalTemplateID, newTemplateID);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Saved properties for '{0}'"), newTemplateID));
	}

	private void InitializeState(string category, string templateId, Action<string, string>? onApplied)
	{
		_originalTemplateID = templateId ?? string.Empty;
		_onAppliedCallback = onApplied;

		var (_, parsedSlug) = TemplateIDHelper.ParseTemplateID(_originalTemplateID);
		_slug = !string.IsNullOrEmpty(parsedSlug) ? parsedSlug : TemplateIDHelper.ToSnakeCase(_originalTemplateID);
		_name = _slug;
		_iconPath = string.Empty;
		_tooltip = string.Empty;
		ResetCategoryState();
	}

	protected virtual void ResetCategoryState() { }

	private void LoadMetadataProperties()
	{
		string workspacePath = MapWorkspaceService.GetActiveWorkspacePath();
		if (!GameContext.MetadataService.TryLoadMetadata(workspacePath, out var meta) || meta == null)
			return;

		LoadTemplateProperties(meta, _originalTemplateID);
	}

	private void EnsureIconPath()
	{
		if (!string.IsNullOrEmpty(_iconPath))
			return;

		var candidates = ScanIconAssets(true);
		string? candidateMatch = candidates.FirstOrDefault(c => string.Equals(c, $"{_slug}.rtex", StringComparison.OrdinalIgnoreCase))
		                        ?? candidates.FirstOrDefault(c => string.Equals(Path.GetFileNameWithoutExtension(c), _slug, StringComparison.OrdinalIgnoreCase));

		if (!string.IsNullOrEmpty(candidateMatch))
		{
			_iconPath = candidateMatch;
		}
	}

	protected void UpdateUI()
	{
		TitleLabel.Text = $"{TranslationServer.Translate("Edit Properties")} - {_originalTemplateID}";

		if (_lblObjectTypePrefix != null) _lblObjectTypePrefix.Text = $"{_objectType}/";
		if (_txtSlug != null) _txtSlug.Text = _slug;
		if (_txtName != null) _txtName.Text = _name;
		_setIconPathValue?.Invoke(_iconPath);
		if (_txtTooltip != null) _txtTooltip.Text = _tooltip;

		RefreshCategoryControls();
	}

	private void ValidateSlug()
	{
		string fullId = $"{_objectType}/{_slug}";
		if (string.IsNullOrWhiteSpace(_slug))
		{
			SetSlugValidationMessage(TranslationServer.Translate("Slug cannot be empty."));
			return;
		}

		if (fullId.Equals(_originalTemplateID, StringComparison.OrdinalIgnoreCase))
		{
			_lblSlugValidation.Visible = false;
			return;
		}

		string workspacePath = MapWorkspaceService.GetActiveWorkspacePath();
		if (GameContext.MetadataService.TryLoadMetadata(workspacePath, out var meta) && meta != null && TemplateIdExists(meta, fullId))
		{
			SetSlugValidationMessage(TranslationServer.Translate("Slug already exists in this category."));
			return;
		}

		_lblSlugValidation.Visible = false;
	}

	private void SetSlugValidationMessage(string translatedMessage)
	{
		_lblSlugValidation.Text = translatedMessage;
		_lblSlugValidation.Visible = true;
	}
}
