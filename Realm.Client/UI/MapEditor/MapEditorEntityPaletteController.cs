using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;

namespace Realm.Client.UI.MapEditor;

public class MapEditorEntityPaletteController
{
	private readonly MapEditorHUD _hud;
	private readonly VBoxContainer _palettesVBox;
	private readonly Button _btnAddObject;

	private static readonly string[] Categories =
	{
		"Units",
		"Buildings",
		"Resources",
		"Props",
		"Decals",
		"VFX",
		"Items"
	};

	private string _currentCategory = "Units";
	private readonly List<string> _categoryFiles = new();
	private readonly Dictionary<string, string> _idToDisplayName = new(StringComparer.OrdinalIgnoreCase);
	private OptionButton _optCategory;
	private OptionButton _optCategoryItems;

	private string GetDisplayNameForId(string file)
	{
		if (_idToDisplayName.TryGetValue(file, out var name))
		{
			if (!string.IsNullOrEmpty(name))
			{
				return name;
			}
		}

		if (file.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase))
		{
			string sub = file.Substring(4);
			if (TryGetVfxName(sub, out string? subName))
			{
				return "✨ " + subName;
			}

			return "✨ " + sub;
		}

		if (TryGetVfxName(file, out string? vfxName))
		{
			return "✨ " + vfxName;
		}

		if (TryGetUnitName(file, out string? unitName))
		{
			return unitName;
		}

		if (TryGetPropName(file, out string? propName))
		{
			return propName;
		}

		if (TryGetResourceName(file, out string? resourceName))
		{
			return resourceName;
		}

		if (TryGetItemName(file, out string? itemName))
		{
			return itemName;
		}

		return Path.GetFileNameWithoutExtension(file).Replace("_", " ");
	}

	private bool TryGetVfxName(string key, [NotNullWhen(true)] out string? name)
	{
		name = null;
		if (GameContext.RegistryService.VfxRegistry == null)
		{
			return false;
		}

		if (GameContext.RegistryService.TryGetVfxConfig(key, out var meta))
		{
			if (!string.IsNullOrEmpty(meta.Name))
			{
				name = meta.Name;
				return true;
			}
		}

		return false;
	}

	private bool TryGetUnitName(string key, [NotNullWhen(true)] out string? name)
	{
		name = null;
		if (GameHost.UnitRegistry == null)
		{
			return false;
		}

		if (GameHost.UnitRegistry.TryGetValue(key, out var meta))
		{
			if (!string.IsNullOrEmpty(meta.Name))
			{
				name = meta.Name;
				return true;
			}
		}

		return false;
	}

	private bool TryGetPropName(string key, [NotNullWhen(true)] out string? name)
	{
		name = null;
		if (GameHost.PropRegistry == null)
		{
			return false;
		}

		if (GameHost.PropRegistry.TryGetValue(key, out var meta))
		{
			if (!string.IsNullOrEmpty(meta.Name))
			{
				name = meta.Name;
				return true;
			}
		}

		return false;
	}

	private bool TryGetResourceName(string key, [NotNullWhen(true)] out string? name)
	{
		name = null;
		if (GameHost.ResourceRegistry == null)
		{
			return false;
		}

		if (GameHost.ResourceRegistry.TryGetValue(key, out var meta))
		{
			if (!string.IsNullOrEmpty(meta.Name))
			{
				name = meta.Name;
				return true;
			}
		}

		return false;
	}

	private bool TryGetItemName(string key, [NotNullWhen(true)] out string? name)
	{
		name = null;
		if (GameHost.ItemRegistry == null)
		{
			return false;
		}

		if (GameHost.ItemRegistry.TryGetValue(key, out var meta))
		{
			if (!string.IsNullOrEmpty(meta.Name))
			{
				name = meta.Name;
				return true;
			}
		}

		return false;
	}

	public string CurrentCategory => _currentCategory;
	public List<string> CategoryFiles => _categoryFiles;
	public OptionButton OptCategory => _optCategory;
	public OptionButton OptCategoryItems => _optCategoryItems;

	public MapEditorEntityPaletteController(MapEditorHUD hud, VBoxContainer palettesVBox, Button btnAddObject)
	{
		_hud = hud;
		_palettesVBox = palettesVBox;
		_btnAddObject = btnAddObject;

		_optCategory = CreateRow(TranslationServer.Translate("Category"), TranslationServer.Translate("Select which kind of object to place"), "OptCategory");
		foreach (string category in Categories)
		{
			_optCategory.AddItem(GetCategoryLabel(category));
		}

		_optCategory.ItemSelected += (index) =>
		{
			if (index < 0 || index >= Categories.Length) return;
			SelectCategory(Categories[(int)index]);
		};

		_optCategoryItems = CreateRow(TranslationServer.Translate("Template"), TranslationServer.Translate("Select the template to place"), "OptCategoryItems");

		_optCategoryItems.ItemSelected += (index) =>
		{
			SelectCategoryItem((int)index);
			TriggerAddObjectMode();
		};

		SelectCategory("Units", triggerAddObject: false);
	}

	/// <summary>
	///     Names a category the way the list shows it. Each label is a literal translation call site, which is what keeps the
	///     category names in the catalogs rather than only in the code that builds the list.
	/// </summary>
	/// <param name="category">The category whose label is wanted.</param>
	/// <returns>The translated label, or the category id itself when the category has no label of its own.</returns>
	private static string GetCategoryLabel(string category)
	{
		return category switch
		{
			"Units" => TranslationServer.Translate("👥 Units"),
			"Buildings" => TranslationServer.Translate("🏢 Buildings"),
			"Resources" => TranslationServer.Translate("🪵 Resources"),
			"Props" => TranslationServer.Translate("📦 Props"),
			"Decals" => TranslationServer.Translate("🎨 Decals"),
			"VFX" => TranslationServer.Translate("✨ VFX"),
			"Items" => TranslationServer.Translate("🎒 Items"),
			_ => category
		};
	}

	/// <summary>
	///     Builds one labelled row of the selector: the name of what the row chooses, then the list itself. The two rows
	///     read as one control, so the object palette is a category and a template rather than a wall of category buttons.
	/// </summary>
	/// <param name="labelText">The text naming what the row chooses.</param>
	/// <param name="labelTooltip">What the row's list is for.</param>
	/// <param name="optionName">The node name the list is registered under, which names the row after it.</param>
	/// <returns>The list the row holds.</returns>
	private OptionButton CreateRow(string labelText, string labelTooltip, string optionName)
	{
		var row = new HBoxContainer();
		row.Name = $"{optionName}Row_HBoxContainer";
		row.AddThemeConstantOverride("separation", 6);
		_palettesVBox.AddChild(row);

		var label = new Label();
		label.Name = "Label_Label";
		label.Text = labelText;
		label.TooltipText = labelTooltip;
		label.CustomMinimumSize = new Vector2(76, 0);
		label.VerticalAlignment = VerticalAlignment.Center;
		row.AddChild(label);

		var option = new OptionButton();
		option.Name = optionName;
		option.CustomMinimumSize = new Vector2(140, 30);
		option.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		option.TooltipText = labelTooltip;
		row.AddChild(option);

		return option;
	}

	public void RefreshCurrentCategoryItems()
	{
		SelectCategory(_currentCategory, triggerAddObject: false);
	}

	public void SelectCategory(string category, bool triggerAddObject = true)
	{
		_currentCategory = category;
		SyncCategorySelection(category);

		string? previousSelectedId = GetPreviousSelectedId();

		_categoryFiles.Clear();
		_idToDisplayName.Clear();
		_optCategoryItems.Clear();

		LoadCategoryMetadata(category);

		_categoryFiles.Sort((a, b) => string.Compare(GetDisplayNameForId(a), GetDisplayNameForId(b), StringComparison.OrdinalIgnoreCase));

		foreach (var file in _categoryFiles)
		{
			string displayName = GetDisplayNameForId(file);
			_optCategoryItems.AddItem(TranslationServer.Translate(displayName));
		}

		RestorePreviousSelection(previousSelectedId);

		if (triggerAddObject)
		{
			TriggerAddObjectMode();
		}
	}

	/// <summary>
	///     Points the category list at the category the editor is placing from, so a category chosen anywhere else - an
	///     eyedropper pick or a restored session - reads in the list rather than leaving it on a category nothing uses.
	/// </summary>
	/// <param name="category">The category the palette is showing.</param>
	private void SyncCategorySelection(string category)
	{
		if (_optCategory == null) return;

		for (int index = 0; index < Categories.Length; index++)
		{
			if (!string.Equals(Categories[index], category, StringComparison.OrdinalIgnoreCase) &&
			    !(category is "Characters" && Categories[index] == "Units") &&
			    !(category is "Environment" && Categories[index] == "Resources"))
			{
				continue;
			}

			_optCategory.Selected = index;
			return;
		}
	}

	private string? GetPreviousSelectedId()
	{
		if (_optCategoryItems != null && _optCategoryItems.Selected >= 0 && _optCategoryItems.Selected < _categoryFiles.Count)
		{
			return _categoryFiles[_optCategoryItems.Selected];
		}

		return null;
	}

	private void RestorePreviousSelection(string? previousSelectedId)
	{
		if (_optCategoryItems.ItemCount == 0)
		{
			if (GameContext.Host != null)
			{
				GameContext.Host.ActivePlaceId = "";
			}

			return;
		}

		int targetIndex = 0;
		if (!string.IsNullOrEmpty(previousSelectedId))
		{
			int found = _categoryFiles.FindIndex(f => f.Equals(previousSelectedId, StringComparison.OrdinalIgnoreCase));
			if (found >= 0) targetIndex = found;
		}

		_optCategoryItems.Selected = targetIndex;
		SelectCategoryItem(targetIndex);
	}

	private void LoadCategoryMetadata(string category)
	{
		try
		{
			LoadMetadataForCategory(category);
		}
		catch
		{
		}

		if (category == "VFX" && _categoryFiles.Count == 0)
		{
			LoadVfxRegistry();
		}
	}

	private void LoadMetadataForCategory(string category)
	{
		string wsPath = GameContext.EditorService._editingDirectory;
		string globalWs = ProjectSettings.GlobalizePath(wsPath);

		if (!GameContext.MetadataService.TryLoadMetadata(globalWs, out var metadata)) return;

		switch (category)
		{
			case "VFX": LoadVfxMetadata(metadata); break;
			case "Decals": LoadDecalsMetadata(metadata, globalWs); break;
			case "Buildings": LoadBuildingsMetadata(metadata); break;
			case "Units":
			case "Characters": LoadUnitsMetadata(metadata); break;
			case "Resources":
			case "Environment": LoadResourcesMetadata(metadata); break;
			case "Props": LoadPropsMetadata(metadata); break;
			case "Items": LoadItemsMetadata(metadata); break;
		}
	}

	private void AddCategoryItem(string id, string? displayName)
	{
		var (prefix, slug) = TemplateIDHelper.ParseTemplateID(id);

		if (string.IsNullOrEmpty(id) || _categoryFiles.Contains(slug)) return;

		_categoryFiles.Add(slug);
		if (!string.IsNullOrEmpty(displayName))
		{
			_idToDisplayName[slug] = displayName;
		}
	}

	private void LoadVfxMetadata(MapMetadata metadata)
	{
		LoadVfxRegistry();

		if (metadata.Templates?.Vfx == null) return;

		foreach (var vObj in metadata.Templates.Vfx)
		{
			if (string.IsNullOrEmpty(vObj.VfxId)) continue;

			string? name = string.IsNullOrEmpty(vObj.Name) ? null : "✨ " + vObj.Name;
			AddCategoryItem(vObj.VfxId, name);
		}
	}

	private void LoadVfxRegistry()
	{
		if (GameContext.RegistryService.VfxRegistry == null) return;

		foreach (var kvp in GameContext.RegistryService.VfxRegistry)
		{
			string vfxKey = kvp.Key;
			string displayName = "✨ " + (!string.IsNullOrEmpty(kvp.Value.Name) ? kvp.Value.Name : kvp.Key);
			AddCategoryItem(vfxKey, displayName);
		}
	}

	private void LoadDecalsMetadata(MapMetadata metadata, string globalWs)
	{
		if (metadata.Decals != null)
		{
			foreach (var kvp in metadata.Decals)
			{
				string decalKey = kvp.Key;
				string name = TemplateIDHelper.ParseTemplateID(decalKey).Slug.Replace("_", " ");
				AddCategoryItem(decalKey, name);
			}
		}
	}

	private void LoadBuildingsMetadata(MapMetadata metadata)
	{
		if (metadata.Templates?.Buildings != null)
		{
			foreach (var b in metadata.Templates.Buildings)
			{
				AddCategoryItem(b.TemplateID, b.Name);
			}
		}

		if (GameHost.BuildingRegistry != null)
		{
			foreach (var kvp in GameHost.BuildingRegistry)
			{
				AddCategoryItem(kvp.Key, kvp.Value.Name);
			}
		}
	}

	private void LoadUnitsMetadata(MapMetadata metadata)
	{
		if (metadata.Templates?.Units != null)
		{
			foreach (var u in metadata.Templates.Units)
			{
				AddCategoryItem(u.TemplateID, u.Name);
			}
		}

		if (GameHost.UnitRegistry != null)
		{
			foreach (var kvp in GameHost.UnitRegistry)
			{
				AddCategoryItem(kvp.Key, kvp.Value.Name);
			}
		}
	}

	private void LoadResourcesMetadata(MapMetadata metadata)
	{
		if (metadata.Templates?.Resources != null)
		{
			foreach (var r in metadata.Templates.Resources)
			{
				AddCategoryItem(r.TemplateID, r.Name);
			}
		}

		if (GameHost.ResourceRegistry != null)
		{
			foreach (var kvp in GameHost.ResourceRegistry)
			{
				AddCategoryItem(kvp.Key, kvp.Value.Name);
			}
		}
	}

	private void LoadPropsMetadata(MapMetadata metadata)
	{
		if (metadata.Templates?.Props != null)
		{
			foreach (var p in metadata.Templates.Props)
			{
				AddCategoryItem(p.TemplateID, p.Name);
			}
		}

		if (GameHost.PropRegistry != null)
		{
			foreach (var kvp in GameHost.PropRegistry)
			{
				AddCategoryItem(kvp.Key, kvp.Value.Name);
			}
		}
	}

	private void LoadItemsMetadata(MapMetadata metadata)
	{
		if (metadata.Templates?.Items != null)
		{
			foreach (var i in metadata.Templates.Items)
			{
				AddCategoryItem(i.TemplateID, i.Name);
			}
		}

		if (GameContext.RegistryService?.ItemRegistry != null)
		{
			foreach (var kvp in GameContext.RegistryService.ItemRegistry)
			{
				AddCategoryItem(kvp.Key, kvp.Value.Name);
			}
		}
	}

	public void SelectCategoryItem(int index)
	{
		if (index >= 0 && index < _categoryFiles.Count)
		{
			string selectedFile = _categoryFiles[index];
			if (GameContext.Host != null)
			{
				GameContext.Host.ActivePlaceId = selectedFile;
			}
		}
	}

	public void TriggerAddObjectMode()
	{
		if (GameContext.Host == null)
		{
			return;
		}

		string placeId = GetSelectedCategoryPlaceId();
		EditorTool targetTool = GetEditorToolForCategory(_currentCategory);
		_hud.TriggerToolSelection(targetTool, _btnAddObject, placeId);
	}

	private EditorTool GetEditorToolForCategory(string category)
	{
		if (category == "Units" || category == "Characters" || category == "Buildings")
		{
			return EditorTool.PlaceUnit;
		}

		if (category == "Decals")
		{
			return EditorTool.PlaceDecal;
		}

		if (category == "VFX")
		{
			return EditorTool.PlaceVfx;
		}

		if (category == "Items")
		{
			return EditorTool.PlaceItem;
		}

		return EditorTool.PlaceProp;
	}

	private string GetSelectedCategoryPlaceId()
	{
		if (_categoryFiles.Count == 0 || _optCategoryItems == null)
		{
			return "";
		}

		int selectedIndex = _optCategoryItems.Selected;
		if (selectedIndex < 0 || selectedIndex >= _categoryFiles.Count)
		{
			return "";
		}

		return _categoryFiles[selectedIndex];
	}

	public void SelectCategoryItemExternal(string category, string filename)
	{
		if (category != _currentCategory)
		{
			SelectCategory(category);
		}

		string searchName = filename;
		if (filename.StartsWith("res://") || filename.Contains('/') || filename.Contains('\\'))
		{
			searchName = Path.GetFileName(filename);
		}

		int idx = _categoryFiles.FindIndex(f => Path.GetFileName(f).Equals(searchName, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0)
		{
			_optCategoryItems.Selected = idx;
			SelectCategoryItem(idx);
			TriggerAddObjectMode();
		}
		else
		{
			EditorTool targetTool = EditorTool.PlaceProp;
			if (category == "Characters" || category == "Buildings")
			{
				targetTool = EditorTool.PlaceUnit;
			}
			else if (category == "Decals")
			{
				targetTool = EditorTool.PlaceDecal;
			}
			else if (category == "VFX")
			{
				targetTool = EditorTool.PlaceVfx;
			}
			else if (category == "Items")
			{
				targetTool = EditorTool.PlaceItem;
			}

			_hud.TriggerToolSelection(targetTool, _btnAddObject, filename);
		}
	}
}