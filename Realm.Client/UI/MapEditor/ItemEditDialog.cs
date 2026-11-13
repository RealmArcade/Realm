using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Metadata;

namespace Realm.Client.UI.MapEditor;

/// <summary>
///     Edits one item template: the identity and icon every template has, the 3D model the drop is drawn with where the
///     map places it on the terrain, and the weapons, abilities and upgrades the item grants. The model is optional - an
///     item that states none is drawn as the drop's gem placeholder, so no map has to author a mesh to place an item.
/// </summary>
[GlobalClass]
public partial class ItemEditDialog : IconTemplateEditDialogBase
{
	public ItemEditDialog() { }

	private string _modelPath = string.Empty;
	private List<string> _grantedWeapons = new();
	private List<string> _grantedAbilities = new();
	private List<string> _grantedUpgrades = new();

	private string _initialModelPath = string.Empty;
	private List<string> _initialGrantedWeapons = new();
	private List<string> _initialGrantedAbilities = new();
	private List<string> _initialGrantedUpgrades = new();

	private Action<string> _setModelPathValue = null!;
	private VBoxContainer _vboxGrantedWeapons = null!;
	private VBoxContainer _vboxGrantedAbilities = null!;
	private VBoxContainer _vboxGrantedUpgrades = null!;

	public ItemEditDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Edit Item"), new Vector2(480, 560), "items", "item")
	{
	}

	protected override void BuildCategorySections(VBoxContainer content)
	{
		AddSectionHeader(content, "🪙 " + TranslationServer.Translate("GROUND MODEL"), new Color(0.85f, 0.72f, 0.35f));
		AddDescription(content, TranslationServer.Translate("The 3D model this item is drawn with where the map places it on the terrain. Leave it empty and the drop uses its gem placeholder."));

		(_, _setModelPathValue) = AddAssetFilterDropdown(
			content,
			TranslationServer.Translate("Ground Model (.rmesh):"),
			_modelPath,
			(all) => ScanGroundModels(all),
			(val) => _modelPath = val ?? string.Empty,
			TranslationServer.Translate("Select the model drawn on the ground..."),
			120f
		);

		AddSectionHeader(content, "⚔️ " + TranslationServer.Translate("GRANTED CAPABILITIES"), new Color(0.9f, 0.5f, 0.4f));

		AddLabel(content, TranslationServer.Translate("Granted Weapons:"), 11, UIStyle.ColorGold);
		_vboxGrantedWeapons = new VBoxContainer();
		_vboxGrantedWeapons.Name = "VboxGrantedWeapons_VBoxContainer";
		_vboxGrantedWeapons.AddThemeConstantOverride("separation", 4);
		content.AddChild(_vboxGrantedWeapons);
		AddGrantedRowButton(content, "BtnAddWeapon_Button", TranslationServer.Translate("Add Granted Weapon"), () =>
		{
			_grantedWeapons.Add(string.Empty);
			RebuildGrantedWeaponsUI();
		});

		AddLabel(content, TranslationServer.Translate("Granted Abilities:"), 11, UIStyle.ColorGold);
		_vboxGrantedAbilities = new VBoxContainer();
		_vboxGrantedAbilities.Name = "VboxGrantedAbilities_VBoxContainer";
		_vboxGrantedAbilities.AddThemeConstantOverride("separation", 4);
		content.AddChild(_vboxGrantedAbilities);
		AddGrantedRowButton(content, "BtnAddAbility_Button", TranslationServer.Translate("Add Granted Ability"), () =>
		{
			_grantedAbilities.Add(string.Empty);
			RebuildGrantedAbilitiesUI();
		});

		AddLabel(content, TranslationServer.Translate("Granted Upgrades:"), 11, UIStyle.ColorGold);
		_vboxGrantedUpgrades = new VBoxContainer();
		_vboxGrantedUpgrades.Name = "VboxGrantedUpgrades_VBoxContainer";
		_vboxGrantedUpgrades.AddThemeConstantOverride("separation", 4);
		content.AddChild(_vboxGrantedUpgrades);
		AddGrantedRowButton(content, "BtnAddUpgrade_Button", TranslationServer.Translate("Add Granted Upgrade"), () =>
		{
			_grantedUpgrades.Add(string.Empty);
			RebuildGrantedUpgradesUI();
		});
	}

	private void AddGrantedRowButton(VBoxContainer content, string buttonName, string translatedLabel, Action pressed)
	{
		var button = new Button();
		button.Name = buttonName;
		button.Text = "+ " + translatedLabel;
		button.AddThemeFontSizeOverride("font_size", 10);
		button.FocusMode = FocusModeEnum.None;
		button.Pressed += pressed;
		content.AddChild(button);
	}

	protected override void LoadTemplateProperties(MapMetadata meta, string templateId)
	{
		var item = meta.GetItem(templateId) ?? meta.GetItem(_slug);
		if (item == null) return;

		_name = string.IsNullOrEmpty(item.Name) ? _slug : item.Name;
		_iconPath = FormatIconPath(item.IconPath);
		_tooltip = item.Description ?? string.Empty;
		_modelPath = item.ModelPath ?? string.Empty;

		_grantedWeapons = item.GrantedWeapons != null ? new List<string>(item.GrantedWeapons) : new List<string>();
		_grantedAbilities = item.GrantedAbilities != null ? new List<string>(item.GrantedAbilities) : new List<string>();
		_grantedUpgrades = item.GrantedUpgrades != null ? new List<string>(item.GrantedUpgrades) : new List<string>();
	}

	protected override void ApplyTemplateProperties(MapMetadata meta, string newTemplateID)
	{
		string? modelPath = string.IsNullOrWhiteSpace(_modelPath) ? null : _modelPath;
		string[] grantedWeapons = _grantedWeapons.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
		string[] grantedAbilities = _grantedAbilities.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
		string[] grantedUpgrades = _grantedUpgrades.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();

		bool updated = meta.UpdateItem(_originalTemplateID, item =>
		{
			item.TemplateID = newTemplateID;
			item.Name = _name;
			item.Description = _tooltip;
			item.IconPath = _iconPath;
			item.ModelPath = modelPath;
			item.GrantedWeapons = grantedWeapons;
			item.GrantedAbilities = grantedAbilities;
			item.GrantedUpgrades = grantedUpgrades;
			return item;
		});

		if (!updated)
		{
			meta.AddOrUpdateItem(new ItemMetadata
			{
				TemplateID = newTemplateID,
				Name = !string.IsNullOrEmpty(_name) ? _name : _slug,
				Description = _tooltip,
				IconPath = _iconPath,
				ModelPath = modelPath,
				ItemClass = "consumable",
				GrantedWeapons = grantedWeapons,
				GrantedAbilities = grantedAbilities,
				GrantedUpgrades = grantedUpgrades
			});
		}

		if (!string.Equals(_originalTemplateID, newTemplateID, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(_originalTemplateID))
		{
			meta.RemoveItem(_originalTemplateID);
		}
	}

	protected override bool TemplateIdExists(MapMetadata meta, string fullId)
	{
		return meta.GetItem(fullId) != null;
	}

	/// <summary>
	///     Puts the edited item back on the terrain: the registry a drop reads its model from is reloaded, and every placed
	///     drop of this template is rebuilt, which is what makes a model chosen here show on the ground the author is
	///     still looking at.
	/// </summary>
	/// <param name="newTemplateID">The item template id the properties were written to.</param>
	protected override void OnTemplateApplied(string newTemplateID)
	{
		if (GameContext.Host is { } host) host.LoadUnitMetadata(MapWorkspaceService.GetActiveWorkspacePath());

		Prop3D.InvalidateModelPathCache(newTemplateID);
		ModelCache.InvalidateModelPath(newTemplateID);
		Prop3D.InvalidateModelPathCache(_originalTemplateID);
		ModelCache.InvalidateModelPath(_originalTemplateID);

		GameContext.IEditorAPI.RefreshPlacedObjectModels(newTemplateID);
		if (!string.Equals(_originalTemplateID, newTemplateID, StringComparison.OrdinalIgnoreCase))
		{
			GameContext.IEditorAPI.RefreshPlacedObjectModels(_originalTemplateID);
		}
	}

	protected override void RefreshCategoryControls()
	{
		_setModelPathValue?.Invoke(_modelPath);
		RebuildGrantedWeaponsUI();
		RebuildGrantedAbilitiesUI();
		RebuildGrantedUpgradesUI();
	}

	protected override void RestoreCategoryState()
	{
		_modelPath = _initialModelPath;
		_grantedWeapons = new List<string>(_initialGrantedWeapons);
		_grantedAbilities = new List<string>(_initialGrantedAbilities);
		_grantedUpgrades = new List<string>(_initialGrantedUpgrades);
		RefreshCategoryControls();
	}

	protected override void ResetCategoryState()
	{
		_modelPath = string.Empty;
		_grantedWeapons = new List<string>();
		_grantedAbilities = new List<string>();
		_grantedUpgrades = new List<string>();
	}

	protected override void CaptureCategoryState()
	{
		_initialModelPath = _modelPath;
		_initialGrantedWeapons = new List<string>(_grantedWeapons);
		_initialGrantedAbilities = new List<string>(_grantedAbilities);
		_initialGrantedUpgrades = new List<string>(_grantedUpgrades);
	}

	private void RebuildGrantedWeaponsUI()
	{
		RebuildGrantedList(_vboxGrantedWeapons, _grantedWeapons, TranslationServer.Translate("Weapon ID (e.g. weapon/sword)"), RebuildGrantedWeaponsUI);
	}

	private void RebuildGrantedAbilitiesUI()
	{
		RebuildGrantedList(_vboxGrantedAbilities, _grantedAbilities, TranslationServer.Translate("Ability ID (e.g. ability/fireball)"), RebuildGrantedAbilitiesUI);
	}

	private void RebuildGrantedUpgradesUI()
	{
		RebuildGrantedList(_vboxGrantedUpgrades, _grantedUpgrades, TranslationServer.Translate("Upgrade ID (e.g. upgrade/iron_forging)"), RebuildGrantedUpgradesUI);
	}

	private static void RebuildGrantedList(VBoxContainer container, List<string> values, string translatedPlaceholder, Action rebuild)
	{
		if (container == null) return;

		foreach (Node child in container.GetChildren())
		{
			child.QueueFree();
		}

		for (int index = 0; index < values.Count; index++)
		{
			int capturedIndex = index;
			var row = UIStyle.CreateRowContainer();

			var field = new LineEdit();
			field.Name = "Txt_LineEdit";
			field.Text = values[capturedIndex];
			field.PlaceholderText = translatedPlaceholder;
			field.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			field.AddThemeFontSizeOverride("font_size", 11);
			field.TextChanged += (val) => values[capturedIndex] = val ?? string.Empty;
			row.AddChild(field);

			var removeButton = new Button();
			removeButton.Name = "BtnDel_Button";
			removeButton.Text = "✕";
			removeButton.CustomMinimumSize = new Vector2(24, 24);
			removeButton.FocusMode = FocusModeEnum.None;
			removeButton.Pressed += () =>
			{
				values.RemoveAt(capturedIndex);
				rebuild();
			};
			row.AddChild(removeButton);

			container.AddChild(row);
		}
	}

	private static List<string> ScanGroundModels(bool includeAllFolders)
	{
		var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (string asset in ScanAvailableAssets("models", includeAllFolders))
		{
			if (asset.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
			{
				results.Add(Path.GetFileName(asset));
			}
		}

		string[] baseLocations =
		{
			Path.Combine(MapWorkspaceService.GetActiveWorkspacePath(), "Assets", "models"),
			Path.Combine("MapTemplate", "Assets", "models"),
			Path.Combine(ProjectSettings.GlobalizePath("res://"), "Assets", "models")
		};

		foreach (string baseLocation in baseLocations)
		{
			if (!Directory.Exists(baseLocation)) continue;

			foreach (string file in Directory.GetFiles(baseLocation, "*.rmesh", SearchOption.AllDirectories))
			{
				results.Add(Path.GetFileName(file));
			}
		}

		var list = results.ToList();
		list.Sort();
		return list;
	}
}
