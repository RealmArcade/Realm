using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Services;

namespace Realm.Client.UI.MapEditor;

public partial class ItemUpgradeEditDialog : FloatingDialogBase
{
	private string _category = "items";
	private string _objectType = "item";
	private string _slug = "";
	private string _originalTemplateID = "";
	private string _name = "";
	private string _iconPath = "";
	private string _tooltip = "";
	private List<string> _grantedWeapons = new();
	private List<string> _grantedAbilities = new();
	private List<string> _grantedUpgrades = new();

	private string _initialName = "";
	private string _initialIconPath = "";
	private string _initialTooltip = "";
	private List<string> _initialGrantedWeapons = new();
	private List<string> _initialGrantedAbilities = new();
	private List<string> _initialGrantedUpgrades = new();
	private string? _initialTemplateJson = null;
	private bool _templateExistedInitially = false;

	private Action<string, string> _onAppliedCallback;

	private Label _lblObjectTypePrefix;
	private LineEdit _txtSlug;
	private Label _lblSlugValidation;
	private LineEdit _txtName;
	private LineEdit _txtIconPath;
	private Action<string> _setIconPathValue;
	private LineEdit _txtTooltip;
	private VBoxContainer _previewContainer;
	private VBoxContainer _grantedCapabilitiesSection;
	private VBoxContainer _vboxGrantedWeapons;
	private VBoxContainer _vboxGrantedAbilities;
	private VBoxContainer _vboxGrantedUpgrades;

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
		contentVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
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
		_txtSlug.SizeFlagsHorizontal = SizeFlags.ExpandFill;
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
		AddDescription(contentVBox, TranslationServer.Translate("Custom syntax supported: <b>Bold</b>, <i>Italics</i>, <color=#RRGGBB>Colors</color>, and standard BBCode tags."));

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

		_grantedCapabilitiesSection = new VBoxContainer();
		_grantedCapabilitiesSection.AddThemeConstantOverride("separation", 10);
		contentVBox.AddChild(_grantedCapabilitiesSection);

		AddSectionHeader(_grantedCapabilitiesSection, "⚔️ " + TranslationServer.Translate("GRANTED CAPABILITIES"), new Color(0.9f, 0.5f, 0.4f));

		AddLabel(_grantedCapabilitiesSection, TranslationServer.Translate("Granted Weapons:"), 11, UIStyle.ColorGold);
		_vboxGrantedWeapons = new VBoxContainer();
		_vboxGrantedWeapons.AddThemeConstantOverride("separation", 4);
		_grantedCapabilitiesSection.AddChild(_vboxGrantedWeapons);

		var btnAddWeapon = new Button();
		btnAddWeapon.Text = "+ " + TranslationServer.Translate("Add Granted Weapon");
		btnAddWeapon.AddThemeFontSizeOverride("font_size", 10);
		btnAddWeapon.FocusMode = FocusModeEnum.None;
		btnAddWeapon.Pressed += () =>
		{
			_grantedWeapons.Add("");
			RebuildGrantedWeaponsUI();
		};
		_grantedCapabilitiesSection.AddChild(btnAddWeapon);

		AddLabel(_grantedCapabilitiesSection, TranslationServer.Translate("Granted Abilities:"), 11, UIStyle.ColorGold);
		_vboxGrantedAbilities = new VBoxContainer();
		_vboxGrantedAbilities.AddThemeConstantOverride("separation", 4);
		_grantedCapabilitiesSection.AddChild(_vboxGrantedAbilities);

		var btnAddAbility = new Button();
		btnAddAbility.Text = "+ " + TranslationServer.Translate("Add Granted Ability");
		btnAddAbility.AddThemeFontSizeOverride("font_size", 10);
		btnAddAbility.FocusMode = FocusModeEnum.None;
		btnAddAbility.Pressed += () =>
		{
			_grantedAbilities.Add("");
			RebuildGrantedAbilitiesUI();
		};
		_grantedCapabilitiesSection.AddChild(btnAddAbility);

		AddLabel(_grantedCapabilitiesSection, TranslationServer.Translate("Granted Upgrades:"), 11, UIStyle.ColorGold);
		_vboxGrantedUpgrades = new VBoxContainer();
		_vboxGrantedUpgrades.AddThemeConstantOverride("separation", 4);
		_grantedCapabilitiesSection.AddChild(_vboxGrantedUpgrades);

		var btnAddUpgrade = new Button();
		btnAddUpgrade.Text = "+ " + TranslationServer.Translate("Add Granted Upgrade");
		btnAddUpgrade.AddThemeFontSizeOverride("font_size", 10);
		btnAddUpgrade.FocusMode = FocusModeEnum.None;
		btnAddUpgrade.Pressed += () =>
		{
			_grantedUpgrades.Add("");
			RebuildGrantedUpgradesUI();
		};
		_grantedCapabilitiesSection.AddChild(btnAddUpgrade);
	}

	private void RebuildGrantedWeaponsUI()
	{
		if (_vboxGrantedWeapons == null) return;
		foreach (Node child in _vboxGrantedWeapons.GetChildren())
		{
			child.QueueFree();
		}

		for (int i = 0; i < _grantedWeapons.Count; i++)
		{
			int index = i;
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 6);

			var txt = new LineEdit();
			txt.Text = _grantedWeapons[index];
			txt.PlaceholderText = TranslationServer.Translate("Weapon ID (e.g. weapon/sword)");
			txt.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			txt.AddThemeFontSizeOverride("font_size", 11);
			txt.TextChanged += (val) => _grantedWeapons[index] = val ?? string.Empty;
			row.AddChild(txt);

			var btnDel = new Button();
			btnDel.Text = "✕";
			btnDel.CustomMinimumSize = new Vector2(24, 24);
			btnDel.FocusMode = FocusModeEnum.None;
			btnDel.Pressed += () =>
			{
				_grantedWeapons.RemoveAt(index);
				RebuildGrantedWeaponsUI();
			};
			row.AddChild(btnDel);

			_vboxGrantedWeapons.AddChild(row);
		}
	}

	private void RebuildGrantedAbilitiesUI()
	{
		if (_vboxGrantedAbilities == null) return;
		foreach (Node child in _vboxGrantedAbilities.GetChildren())
		{
			child.QueueFree();
		}

		for (int i = 0; i < _grantedAbilities.Count; i++)
		{
			int index = i;
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 6);

			var txt = new LineEdit();
			txt.Text = _grantedAbilities[index];
			txt.PlaceholderText = TranslationServer.Translate("Ability ID (e.g. ability/fireball)");
			txt.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			txt.AddThemeFontSizeOverride("font_size", 11);
			txt.TextChanged += (val) => _grantedAbilities[index] = val ?? string.Empty;
			row.AddChild(txt);

			var btnDel = new Button();
			btnDel.Text = "✕";
			btnDel.CustomMinimumSize = new Vector2(24, 24);
			btnDel.FocusMode = FocusModeEnum.None;
			btnDel.Pressed += () =>
			{
				_grantedAbilities.RemoveAt(index);
				RebuildGrantedAbilitiesUI();
			};
			row.AddChild(btnDel);

			_vboxGrantedAbilities.AddChild(row);
		}
	}

	private void RebuildGrantedUpgradesUI()
	{
		if (_vboxGrantedUpgrades == null) return;
		foreach (Node child in _vboxGrantedUpgrades.GetChildren())
		{
			child.QueueFree();
		}

		for (int i = 0; i < _grantedUpgrades.Count; i++)
		{
			int index = i;
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 6);

			var txt = new LineEdit();
			txt.Text = _grantedUpgrades[index];
			txt.PlaceholderText = TranslationServer.Translate("Upgrade ID (e.g. upgrade/iron_forging)");
			txt.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			txt.AddThemeFontSizeOverride("font_size", 11);
			txt.TextChanged += (val) => _grantedUpgrades[index] = val ?? string.Empty;
			row.AddChild(txt);

			var btnDel = new Button();
			btnDel.Text = "✕";
			btnDel.CustomMinimumSize = new Vector2(24, 24);
			btnDel.FocusMode = FocusModeEnum.None;
			btnDel.Pressed += () =>
			{
				_grantedUpgrades.RemoveAt(index);
				RebuildGrantedUpgradesUI();
			};
			row.AddChild(btnDel);

			_vboxGrantedUpgrades.AddChild(row);
		}
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
			SetSlugValidationMessage("Slug cannot be empty.");
			return;
		}

		if (fullId.Equals(_originalTemplateID, StringComparison.OrdinalIgnoreCase))
		{
			_lblSlugValidation.Visible = false;
			return;
		}

		if (CheckSlugExists(fullId))
		{
			SetSlugValidationMessage("Slug already exists in this category.");
			return;
		}

		_lblSlugValidation.Visible = false;
	}

	private void SetSlugValidationMessage(string message)
	{
		_lblSlugValidation.Text = TranslationServer.Translate(message);
		_lblSlugValidation.Visible = true;
	}

	private bool CheckSlugExists(string fullId)
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		if (!GameContext.MetadataService.TryLoadMetadata(wsPath, out var meta) || meta == null)
			return false;

		if (_category == "items")
			return CheckItemSlugExists(meta, fullId);

		if (_category == "upgrades")
			return CheckUpgradeSlugExists(meta, fullId);

		return false;
	}

	private bool CheckItemSlugExists(MapMetadata meta, string fullId)
	{
		if (meta.Templates == null) return false;
		if (meta.Templates.Items == null) return false;

		foreach (var i in meta.Templates.Items)
		{
			if (string.Equals(i.TemplateID, fullId, StringComparison.OrdinalIgnoreCase))
				return true;
		}

		return false;
	}

	private bool CheckUpgradeSlugExists(MapMetadata meta, string fullId)
	{
		if (meta.Templates == null) return false;
		if (meta.Templates.Upgrades == null) return false;

		foreach (var u in meta.Templates.Upgrades)
		{
			if (string.Equals(u.TemplateID, fullId, StringComparison.OrdinalIgnoreCase))
				return true;
		}

		return false;
	}

	public void OpenForObject(string category, string templateId, Action<string, string> onApplied = null)
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
		_initialGrantedWeapons = new List<string>(_grantedWeapons);
		_initialGrantedAbilities = new List<string>(_grantedAbilities);
		_initialGrantedUpgrades = new List<string>(_grantedUpgrades);

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
		_grantedWeapons = new List<string>(_initialGrantedWeapons);
		_grantedAbilities = new List<string>(_initialGrantedAbilities);
		_grantedUpgrades = new List<string>(_initialGrantedUpgrades);

		string targetId = !string.IsNullOrEmpty(_originalTemplateID) ? _originalTemplateID : $"{_objectType}/{_slug}";
		if (!string.IsNullOrEmpty(targetId))
		{
			if (_templateExistedInitially)
			{
				GameContext.EditorService.RevertTemplateJson(targetId, _initialTemplateJson);
			}
			else
			{
				GameContext.EditorService.RevertTemplateJson(targetId, null);
			}
		}
	}

	private void InitializeState(string category, string templateId, Action<string, string> onApplied)
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
		_grantedWeapons = new List<string>();
		_grantedAbilities = new List<string>();
		_grantedUpgrades = new List<string>();
	}

	private void LoadMetadataProperties()
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		if (!GameContext.MetadataService.TryLoadMetadata(wsPath, out var meta) || meta == null)
			return;

		if (_category == "upgrades")
		{
			LoadUpgradeProperties(meta);
			return;
		}

		LoadItemProperties(meta);
	}

	private void LoadUpgradeProperties(MapMetadata meta)
	{
		var u = meta.GetUpgrade(_originalTemplateID);
		if (u == null)
			u = meta.GetUpgrade(_slug);

		if (u == null)
			return;

		_name = u.Name;
		if (string.IsNullOrEmpty(_name))
			_name = _slug;

		_iconPath = FormatIconPath(u.IconPath);

		_tooltip = u.Description;
		if (_tooltip == null)
			_tooltip = "";
	}

	private void LoadItemProperties(MapMetadata meta)
	{
		var i = meta.GetItem(_originalTemplateID);
		if (i == null)
			i = meta.GetItem(_slug);

		if (i == null)
			return;

		_name = i.Name;
		if (string.IsNullOrEmpty(_name))
			_name = _slug;

		_iconPath = FormatIconPath(i.IconPath);

		_tooltip = i.Description;
		if (_tooltip == null)
			_tooltip = "";

		_grantedWeapons = i.GrantedWeapons != null ? new List<string>(i.GrantedWeapons) : new List<string>();
		_grantedAbilities = i.GrantedAbilities != null ? new List<string>(i.GrantedAbilities) : new List<string>();
		_grantedUpgrades = i.GrantedUpgrades != null ? new List<string>(i.GrantedUpgrades) : new List<string>();
	}

	private string FormatIconPath(string path)
	{
		if (string.IsNullOrEmpty(path)) return "";
		return path.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(path) : $"{Path.GetFileName(path)}.rtex";
	}

	private void EnsureIconPath()
	{
		if (!string.IsNullOrEmpty(_iconPath))
			return;

		var candidates = ScanIconRtexAssets(true);
		string candidateMatch = candidates.FirstOrDefault(c => string.Equals(c, $"{_slug}.rtex", StringComparison.OrdinalIgnoreCase))
		                        ?? candidates.FirstOrDefault(c => string.Equals(Path.GetFileNameWithoutExtension(c), _slug, StringComparison.OrdinalIgnoreCase));

		if (!string.IsNullOrEmpty(candidateMatch))
		{
			_iconPath = candidateMatch;
		}
	}

	private void UpdateUI()
	{
		TitleLabel.Text = $"{TranslationServer.Translate("Edit Properties")} - {_originalTemplateID}";

		if (_lblObjectTypePrefix != null) _lblObjectTypePrefix.Text = $"{_objectType}/";
		if (_txtSlug != null) _txtSlug.Text = _slug;
		if (_txtName != null) _txtName.Text = _name;
		_setIconPathValue?.Invoke(_iconPath);
		if (_txtTooltip != null) _txtTooltip.Text = _tooltip;

		if (_grantedCapabilitiesSection != null)
		{
			_grantedCapabilitiesSection.Visible = (_category == "items");
			if (_category == "items")
			{
				RebuildGrantedWeaponsUI();
				RebuildGrantedAbilitiesUI();
				RebuildGrantedUpgradesUI();
			}
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
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();

		GameContext.MetadataService.UpdateMetadata(wsPath, meta =>
		{
			if (_category == "upgrades")
			{
				ApplyUpgradeMetadata(meta, newTemplateID);
			}
			else
			{
				ApplyItemMetadata(meta, newTemplateID);
			}
		});

		_onAppliedCallback?.Invoke(_originalTemplateID, newTemplateID);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Saved properties for '{0}'"), newTemplateID));
	}

	private void ApplyUpgradeMetadata(MapMetadata meta, string newTemplateID)
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

	private void ApplyItemMetadata(MapMetadata meta, string newTemplateID)
	{
		bool updated = meta.UpdateItem(_originalTemplateID, i =>
		{
			i.TemplateID = newTemplateID;
			i.Name = _name;
			i.Description = _tooltip;
			i.IconPath = _iconPath;
			i.GrantedWeapons = _grantedWeapons.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
			i.GrantedAbilities = _grantedAbilities.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
			i.GrantedUpgrades = _grantedUpgrades.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
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
				ItemClass = "consumable",
				GrantedWeapons = _grantedWeapons.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray(),
				GrantedAbilities = _grantedAbilities.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray(),
				GrantedUpgrades = _grantedUpgrades.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
			});
		}

		if (!string.Equals(_originalTemplateID, newTemplateID, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(_originalTemplateID))
		{
			meta.RemoveItem(_originalTemplateID);
		}
	}
}