using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Realm.Godot.Services;
using Realm.Godot.UI;
using Realm.Godot.Utils;
using Realm.Shared.Metadata;

public partial class ItemUpgradeEditDialog : FloatingDialogBase
{
	private string _category = "items";
	private string _objectType = "item";
	private string _slug = "";
	private string _originalTemplateID = "";
	private string _name = "";
	private string _iconPath = "";
	private string _tooltip = "";

	private Action<string, string> _onAppliedCallback;

	private Label _lblObjectTypePrefix;
	private LineEdit _txtSlug;
	private Label _lblSlugValidation;
	private LineEdit _txtName;
	private LineEdit _txtIconPath;
	private Action<string> _setIconPathValue;
	private LineEdit _txtTooltip;
	private VBoxContainer _previewContainer;

	public ItemUpgradeEditDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Edit Item / Upgrade"), new Vector2(480, 480))
	{
		BuildControls();
	}

	private void BuildControls()
	{
		AddDescription(BodyContainer, TranslationServer.Translate("Modify metadata, icon asset, and rich formatted tooltip for this item or upgrade template."));

		var scrollBody = CreateScrollBody(400);
		var contentVBox = new VBoxContainer();
		contentVBox.AddThemeConstantOverride("separation", 10);
		contentVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scrollBody.AddChild(contentVBox);

		AddSectionHeader(contentVBox, "🆔 " + TranslationServer.Translate("IDENTITY & ICON"), new Color(0.95f, 0.8f, 0.4f));

		var idRow = new HBoxContainer();
		idRow.AddThemeConstantOverride("separation", 6);

		var lblId = new Label();
		lblId.Text = TranslationServer.Translate("TemplateID:");
		lblId.CustomMinimumSize = new Vector2(120, 0);
		lblId.AddThemeFontSizeOverride("font_size", 11);
		idRow.AddChild(lblId);

		_lblObjectTypePrefix = new Label();
		_lblObjectTypePrefix.Text = "item/";
		_lblObjectTypePrefix.AddThemeFontSizeOverride("font_size", 11);
		_lblObjectTypePrefix.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		idRow.AddChild(_lblObjectTypePrefix);

		_txtSlug = new LineEdit();
		_txtSlug.PlaceholderText = TranslationServer.Translate("snake_case_slug");
		_txtSlug.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_txtSlug.AddThemeFontSizeOverride("font_size", 11);
		_txtSlug.TextChanged += (val) =>
		{
			_slug = TemplateIDHelper.ToSnakeCase(val);
			ValidateSlug();
		};
		idRow.AddChild(_txtSlug);
		contentVBox.AddChild(idRow);

		_lblSlugValidation = new Label();
		_lblSlugValidation.AddThemeFontSizeOverride("font_size", 10);
		_lblSlugValidation.AddThemeColorOverride("font_color", new Color(0.95f, 0.4f, 0.4f));
		_lblSlugValidation.Visible = false;
		contentVBox.AddChild(_lblSlugValidation);

		_txtName = AddTextInput(
			contentVBox,
			TranslationServer.Translate("Display Name:"),
			_name,
			(val) => _name = val ?? string.Empty,
			TranslationServer.Translate("Display name..."),
			120f
		);

		(_txtIconPath, _setIconPathValue) = AddAssetFilterDropdown(
			contentVBox,
			TranslationServer.Translate("Icon (.rtex):"),
			_iconPath,
			(all) => ScanIconRtexAssets(all),
			(val) =>
			{
				_iconPath = val ?? string.Empty;
				UpdateTooltipPreview();
			},
			TranslationServer.Translate("Select icon .rtex..."),
			120f
		);

		AddSectionHeader(contentVBox, "💬 " + TranslationServer.Translate("TOOLTIP & DESCRIPTION"), new Color(0.4f, 0.85f, 0.5f));

		_txtTooltip = AddTextInput(
			contentVBox,
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

		AddSectionHeader(contentVBox, "👁️ " + TranslationServer.Translate("TOOLTIP PREVIEW"), new Color(0.35f, 0.75f, 0.9f));

		var previewPanel = new PanelContainer();
		previewPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		previewPanel.CustomMinimumSize = new Vector2(0, 80);

		var previewMargin = new MarginContainer();
		previewMargin.AddThemeConstantOverride("margin_left", 8);
		previewMargin.AddThemeConstantOverride("margin_right", 8);
		previewMargin.AddThemeConstantOverride("margin_top", 8);
		previewMargin.AddThemeConstantOverride("margin_bottom", 8);

		_previewContainer = new VBoxContainer();
		_previewContainer.AddThemeConstantOverride("separation", 6);
		previewMargin.AddChild(_previewContainer);
		previewPanel.AddChild(previewMargin);

		contentVBox.AddChild(previewPanel);
	}

	private List<string> ScanIconRtexAssets(bool includeAllFolders)
	{
		var list = ScanAvailableAssets("icons", includeAllFolders);
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		var results = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);

		string searchDir = Path.Combine(wsPath, "Assets", "icons");
		if (Directory.Exists(searchDir))
		{
			foreach (var file in Directory.GetFiles(searchDir, "*.rtex", SearchOption.AllDirectories))
			{
				results.Add(Path.GetFileName(file));
			}
		}

		return results.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private void UpdateTooltipPreview()
	{
		if (_previewContainer == null) return;

		foreach (Node child in _previewContainer.GetChildren())
		{
			child.QueueFree();
		}

		Control tooltipWidget = RichTooltip.Create(_tooltip);
		_previewContainer.AddChild(tooltipWidget);
	}

	private void ValidateSlug()
	{
		string fullId = $"{_objectType}/{_slug}";
		if (string.IsNullOrWhiteSpace(_slug))
		{
			_lblSlugValidation.Text = TranslationServer.Translate("Slug cannot be empty.");
			_lblSlugValidation.Visible = true;
			return;
		}

		if (!fullId.Equals(_originalTemplateID, StringComparison.OrdinalIgnoreCase))
		{
			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
			if (MetadataService.Instance.TryLoadMetadata(wsPath, out var meta) && meta != null)
			{
				bool exists = _category switch
				{
					"items" => meta.Templates?.Items?.Any(i => string.Equals(i.TemplateID, fullId, StringComparison.OrdinalIgnoreCase)) ?? false,
					"upgrades" => meta.Templates?.Upgrades?.Any(u => string.Equals(u.TemplateID, fullId, StringComparison.OrdinalIgnoreCase)) ?? false,
					_ => false
				};

				if (exists)
				{
					_lblSlugValidation.Text = TranslationServer.Translate("Slug already exists in this category.");
					_lblSlugValidation.Visible = true;
					return;
				}
			}
		}

		_lblSlugValidation.Visible = false;
	}

	public void OpenForObject(string category, string templateId, Action<string, string> onApplied = null)
	{
		_category = (category ?? "items").ToLowerInvariant();
		_objectType = _category == "upgrades" ? "upgrade" : "item";
		_originalTemplateID = templateId ?? string.Empty;
		_onAppliedCallback = onApplied;

		var (parsedType, parsedSlug) = TemplateIDHelper.ParseTemplateID(_originalTemplateID);
		_slug = !string.IsNullOrEmpty(parsedSlug) ? parsedSlug : TemplateIDHelper.ToSnakeCase(_originalTemplateID);
		_name = _slug;
		_iconPath = "";
		_tooltip = "";

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		if (MetadataService.Instance.TryLoadMetadata(wsPath, out var meta) && meta != null)
		{
			if (_category == "upgrades")
			{
				var u = meta.GetUpgrade(_originalTemplateID);
				if (u != null)
				{
					_name = !string.IsNullOrEmpty(u.Name) ? u.Name : _slug;
					_iconPath = u.IconPath ?? "";
					_tooltip = u.Description ?? "";
				}
			}
			else
			{
				var i = meta.GetItem(_originalTemplateID);
				if (i != null)
				{
					_name = !string.IsNullOrEmpty(i.Name) ? i.Name : _slug;
					_iconPath = i.IconPath ?? "";
					_tooltip = i.Description ?? "";
				}
			}
		}

		TitleLabel.Text = $"{TranslationServer.Translate("Edit Properties")} - {_originalTemplateID}";

		if (_lblObjectTypePrefix != null) _lblObjectTypePrefix.Text = $"{_objectType}/";
		if (_txtSlug != null) _txtSlug.Text = _slug;
		if (_txtName != null) _txtName.Text = _name;
		_setIconPathValue?.Invoke(_iconPath);
		if (_txtTooltip != null) _txtTooltip.Text = _tooltip;

		ValidateSlug();
		UpdateTooltipPreview();
		OpenDialog();
	}

	protected override void OnApply()
	{
		if (string.IsNullOrWhiteSpace(_slug))
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Cannot save: Slug is required."));
			return;
		}

		string newTemplateID = $"{_objectType}/{_slug}";
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();

		MetadataService.Instance.UpdateMetadata(wsPath, meta =>
		{
			if (_category == "upgrades")
			{
				bool updated = meta.UpdateUpgrade(_originalTemplateID, u =>
				{
					u.TemplateID = newTemplateID;
					u.Name = _name;
					u.Description = _tooltip;
					u.IconPath = _iconPath;
					return u;
				});

				if (!updated)
				{
					meta.AddOrUpdateUpgrade(new UpgradeMetadata
					{
						TemplateID = newTemplateID,
						Name = !string.IsNullOrEmpty(_name) ? _name : _slug,
						Description = _tooltip,
						IconPath = _iconPath
					});
				}

				if (!string.Equals(_originalTemplateID, newTemplateID, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(_originalTemplateID))
				{
					meta.RemoveUpgrade(_originalTemplateID);
				}
			}
			else
			{
				bool updated = meta.UpdateItem(_originalTemplateID, i =>
				{
					i.TemplateID = newTemplateID;
					i.Name = _name;
					i.Description = _tooltip;
					i.IconPath = _iconPath;
					return i;
				});

				if (!updated)
				{
					meta.AddOrUpdateItem(new ItemMetadata
					{
						TemplateID = newTemplateID,
						Name = !string.IsNullOrEmpty(_name) ? _name : _slug,
						Description = _tooltip,
						IconPath = _iconPath,
						ItemClass = "consumable"
					});
				}

				if (!string.Equals(_originalTemplateID, newTemplateID, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(_originalTemplateID))
				{
					meta.RemoveItem(_originalTemplateID);
				}
			}
		});

		_onAppliedCallback?.Invoke(_originalTemplateID, newTemplateID);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Saved properties for '{0}'"), newTemplateID));
	}
}
