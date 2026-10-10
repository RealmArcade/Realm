using Godot;
using Realm.Client.Services;
using System;
using System.Collections.Generic;

namespace Realm.Client.UI.MapEditor;

public class MapEditorEntityPaletteController
{
	private readonly MapEditorHUD _hud;
	private readonly VBoxContainer _palettesVBox;
	private readonly Button _btnAddObject;

	private string _currentCategory = "Units";
	private readonly List<string> _categoryFiles = new();
	private readonly Dictionary<string, string> _idToDisplayName = new(StringComparer.OrdinalIgnoreCase);
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
			if (TryGetVfxName(sub, out string subName))
			{
				return "✨ " + subName;
			}
			return "✨ " + sub;
		}
		
		if (TryGetVfxName(file, out string vfxName))
		{
			return "✨ " + vfxName;
		}
		
		if (TryGetUnitName(file, out string unitName))
		{
			return unitName;
		}
		
		if (TryGetPropName(file, out string propName))
		{
			return propName;
		}
		
		if (TryGetResourceName(file, out string resourceName))
		{
			return resourceName;
		}
		
		return System.IO.Path.GetFileNameWithoutExtension(file).Replace("_", " ");
	}

	private bool TryGetVfxName(string key, out string name)
	{
		name = null;
		if (Realm.Client.Core.GameHost.VfxRegistry == null)
		{
			return false;
		}

		if (Realm.Client.Core.GameHost.VfxRegistry.TryGetValue(key, out var meta))
		{
			if (!string.IsNullOrEmpty(meta.Name))
			{
				name = meta.Name;
				return true;
			}
		}
		return false;
	}

	private bool TryGetUnitName(string key, out string name)
	{
		name = null;
		if (Realm.Client.Core.GameHost.UnitRegistry == null)
		{
			return false;
		}

		if (Realm.Client.Core.GameHost.UnitRegistry.TryGetValue(key, out var meta))
		{
			if (!string.IsNullOrEmpty(meta.Name))
			{
				name = meta.Name;
				return true;
			}
		}
		return false;
	}

	private bool TryGetPropName(string key, out string name)
	{
		name = null;
		if (Realm.Client.Core.GameHost.PropRegistry == null)
		{
			return false;
		}

		if (Realm.Client.Core.GameHost.PropRegistry.TryGetValue(key, out var meta))
		{
			if (!string.IsNullOrEmpty(meta.Name))
			{
				name = meta.Name;
				return true;
			}
		}
		return false;
	}

	private bool TryGetResourceName(string key, out string name)
	{
		name = null;
		if (Realm.Client.Core.GameHost.ResourceRegistry == null)
		{
			return false;
		}

		if (Realm.Client.Core.GameHost.ResourceRegistry.TryGetValue(key, out var meta))
		{
			if (!string.IsNullOrEmpty(meta.Name))
			{
				name = meta.Name;
				return true;
			}
		}
		return false;
	}

	private Button _btnChars;
	private Button _btnBuilds;
	private Button _btnEnv;
	private Button _btnProps;
	private Button _btnDecals;
	private Button _btnVfx;

	public string CurrentCategory => _currentCategory;
	public List<string> CategoryFiles => _categoryFiles;
	public OptionButton OptCategoryItems => _optCategoryItems;

	public MapEditorEntityPaletteController(MapEditorHUD hud, VBoxContainer palettesVBox, Button btnAddObject)
	{
		_hud = hud;
		_palettesVBox = palettesVBox;
		_btnAddObject = btnAddObject;



		var categoryGrid = new GridContainer();
		categoryGrid.Columns = 2;
		categoryGrid.AddThemeConstantOverride("h_separation", 6);
		categoryGrid.AddThemeConstantOverride("v_separation", 6);
		_palettesVBox.AddChild(categoryGrid);
		_palettesVBox.MoveChild(categoryGrid, 0);

		_optCategoryItems = new OptionButton();
		_optCategoryItems.Name = "OptCategoryItems";
		_optCategoryItems.CustomMinimumSize = new Vector2(180, 30);
		_palettesVBox.AddChild(_optCategoryItems);
		_palettesVBox.MoveChild(_optCategoryItems, 1);

		_optCategoryItems.ItemSelected += (index) =>
		{
			SelectCategoryItem((int)index);
			TriggerAddObjectMode();
		};

		_btnChars = new Button();
		_btnChars.Set("icon_max_width", 0);
		SetupButton(_btnChars, "👥 Units", () => SelectCategory("Units"), 12, "Select Units category");
		categoryGrid.AddChild(_btnChars);

		_btnBuilds = new Button();
		_btnBuilds.Set("icon_max_width", 0);
		SetupButton(_btnBuilds, "🏢 Buildings", () => SelectCategory("Buildings"), 12, "Select Buildings category");
		categoryGrid.AddChild(_btnBuilds);

		_btnEnv = new Button();
		_btnEnv.Set("icon_max_width", 0);
		SetupButton(_btnEnv, "🪵 Resources", () => SelectCategory("Resources"), 12, "Select Resources category");
		categoryGrid.AddChild(_btnEnv);

		_btnProps = new Button();
		_btnProps.Set("icon_max_width", 0);
		SetupButton(_btnProps, "📦 Props", () => SelectCategory("Props"), 12, "Select Props category");
		categoryGrid.AddChild(_btnProps);

		_btnDecals = new Button();
		_btnDecals.Set("icon_max_width", 0);
		SetupButton(_btnDecals, "🎨 Decals", () => SelectCategory("Decals"), 12, "Select Decals category");
		categoryGrid.AddChild(_btnDecals);

		_btnVfx = new Button();
		_btnVfx.Set("icon_max_width", 0);
		SetupButton(_btnVfx, "✨ VFX", () => SelectCategory("VFX"), 12, "Select VFX category");
		categoryGrid.AddChild(_btnVfx);

		SelectCategory("Units", triggerAddObject: false);
	}

	private void SetupButton(Button btn, string text, Action onClick, int fontSize = 13, string tooltip = "")
	{
		btn.Text = TranslationServer.Translate(text);
		btn.CustomMinimumSize = new Vector2(0, 32);
		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		btn.AddThemeFontSizeOverride("font_size", fontSize);
		btn.FocusMode = Control.FocusModeEnum.None;
		if (!string.IsNullOrEmpty(tooltip))
		{
			btn.TooltipText = TranslationServer.Translate(tooltip);
		}
		btn.Pressed += () =>
		{
			Realm.Client.UI.UIManager.Instance?.PlayClickSound();
			onClick?.Invoke();
		};
	}

	public void SelectCategory(string category, bool triggerAddObject = true)
	{
		_currentCategory = category;
		UpdateCategoryButtonsStyle(category);

		string previousSelectedId = GetPreviousSelectedId();

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

	private void UpdateCategoryButtonsStyle(string category)
	{
		var activeStyle = new StyleBoxFlat();
		activeStyle.BgColor = new Color(0.15f, 0.45f, 0.7f, 0.8f);
		activeStyle.BorderColor = UIStyle.ColorCyanGlow;
		activeStyle.SetBorderWidthAll(2);
		activeStyle.CornerRadiusTopLeft = 4;
		activeStyle.CornerRadiusTopRight = 4;
		activeStyle.CornerRadiusBottomLeft = 4;
		activeStyle.CornerRadiusBottomRight = 4;

		_btnChars.RemoveThemeStyleboxOverride("normal");
		_btnBuilds.RemoveThemeStyleboxOverride("normal");
		_btnEnv.RemoveThemeStyleboxOverride("normal");
		_btnProps.RemoveThemeStyleboxOverride("normal");
		_btnDecals.RemoveThemeStyleboxOverride("normal");
		_btnVfx.RemoveThemeStyleboxOverride("normal");

		if (category == "Units" || category == "Characters") _btnChars.AddThemeStyleboxOverride("normal", activeStyle);
		else _btnChars.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());

		if (category == "Buildings") _btnBuilds.AddThemeStyleboxOverride("normal", activeStyle);
		else _btnBuilds.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());

		if (category == "Resources" || category == "Environment") _btnEnv.AddThemeStyleboxOverride("normal", activeStyle);
		else _btnEnv.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());

		if (category == "Props") _btnProps.AddThemeStyleboxOverride("normal", activeStyle);
		else _btnProps.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());

		if (category == "Decals") _btnDecals.AddThemeStyleboxOverride("normal", activeStyle);
		else _btnDecals.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());

		if (category == "VFX") _btnVfx.AddThemeStyleboxOverride("normal", activeStyle);
		else _btnVfx.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
	}

	private string GetPreviousSelectedId()
	{
		if (_optCategoryItems != null && _optCategoryItems.Selected >= 0 && _optCategoryItems.Selected < _categoryFiles.Count)
		{
			return _categoryFiles[_optCategoryItems.Selected];
		}
		return null;
	}

	private void RestorePreviousSelection(string previousSelectedId)
	{
		if (_optCategoryItems.ItemCount == 0) return;

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
		catch { }

		if (category == "VFX" && _categoryFiles.Count == 0)
		{
			LoadVfxRegistry();
		}
	}

	private void LoadMetadataForCategory(string category)
	{
		string wsPath = MapEditorHUD.TempWorkspaceGodotPath;
		string globalWs = global::Godot.ProjectSettings.GlobalizePath(wsPath);

		if (!MetadataService.Instance.TryLoadMetadata(globalWs, out var metadata)) return;

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
		}
	}

	private void AddCategoryItem(string id, string displayName)
	{
		if (string.IsNullOrEmpty(id) || _categoryFiles.Contains(id)) return;
		
		_categoryFiles.Add(id);
		if (!string.IsNullOrEmpty(displayName))
		{
			_idToDisplayName[id] = displayName;
		}
	}

	private void LoadVfxMetadata(Realm.Shared.Metadata.MapMetadata metadata)
	{
		LoadVfxRegistry();

		if (metadata.Templates?.Vfx == null) return;

		foreach (var vObj in metadata.Templates.Vfx)
		{
			if (string.IsNullOrEmpty(vObj.VfxId)) continue;
			
			string name = string.IsNullOrEmpty(vObj.Name) ? null : "✨ " + vObj.Name;
			AddCategoryItem(vObj.VfxId, name);
		}
	}

	private void LoadVfxRegistry()
	{
		if (Realm.Client.Core.GameHost.VfxRegistry == null) return;

		foreach (var kvp in Realm.Client.Core.GameHost.VfxRegistry)
		{
			string vfxKey = kvp.Key;
			string displayName = "✨ " + (!string.IsNullOrEmpty(kvp.Value.Name) ? kvp.Value.Name : kvp.Key);
			AddCategoryItem(vfxKey, displayName);
		}
	}

	private void LoadDecalsMetadata(Realm.Shared.Metadata.MapMetadata metadata, string globalWs)
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

		var unionedAssets = Realm.Client.Utils.MapAssetHelper.LoadAssets(globalWs);
		var decalsDict = unionedAssets.GetCategory("Decal");
		if (decalsDict == null) return;

		foreach (var kvp in decalsDict)
		{
			string decalFile = kvp.Key;
			string relDecalPath = System.IO.Path.Combine("Assets", "decals", decalFile);
			
			if (_categoryFiles.Contains(relDecalPath) || _categoryFiles.Contains(decalFile)) continue;

			if (System.IO.File.Exists(System.IO.Path.Combine(globalWs, relDecalPath)))
			{
				_categoryFiles.Add(relDecalPath);
			}
			else if (System.IO.File.Exists(System.IO.Path.Combine(globalWs, decalFile)))
			{
				_categoryFiles.Add(decalFile);
			}
			else
			{
				_categoryFiles.Add(relDecalPath);
			}
		}
	}

	private void LoadBuildingsMetadata(Realm.Shared.Metadata.MapMetadata metadata)
	{
		if (metadata.Templates?.Buildings != null)
		{
			foreach (var b in metadata.Templates.Buildings)
			{
				AddCategoryItem(b.TemplateID, b.Name);
			}
		}

		if (Realm.Client.Core.GameHost.BuildingRegistry != null)
		{
			foreach (var kvp in Realm.Client.Core.GameHost.BuildingRegistry)
			{
				AddCategoryItem(kvp.Key, kvp.Value.Name);
			}
		}
	}

	private void LoadUnitsMetadata(Realm.Shared.Metadata.MapMetadata metadata)
	{
		if (metadata.Templates?.Units != null)
		{
			foreach (var u in metadata.Templates.Units)
			{
				AddCategoryItem(u.TemplateID, u.Name);
			}
		}

		if (Realm.Client.Core.GameHost.UnitRegistry != null)
		{
			foreach (var kvp in Realm.Client.Core.GameHost.UnitRegistry)
			{
				AddCategoryItem(kvp.Key, kvp.Value.Name);
			}
		}
	}

	private void LoadResourcesMetadata(Realm.Shared.Metadata.MapMetadata metadata)
	{
		if (metadata.Templates?.Resources != null)
		{
			foreach (var r in metadata.Templates.Resources)
			{
				AddCategoryItem(r.TemplateID, r.Name);
			}
		}

		if (Realm.Client.Core.GameHost.ResourceRegistry != null)
		{
			foreach (var kvp in Realm.Client.Core.GameHost.ResourceRegistry)
			{
				AddCategoryItem(kvp.Key, kvp.Value.Name);
			}
		}
	}

	private void LoadPropsMetadata(Realm.Shared.Metadata.MapMetadata metadata)
	{
		if (metadata.Templates?.Props != null)
		{
			foreach (var p in metadata.Templates.Props)
			{
				AddCategoryItem(p.TemplateID, p.Name);
			}
		}

		if (Realm.Client.Core.GameHost.PropRegistry != null)
		{
			foreach (var kvp in Realm.Client.Core.GameHost.PropRegistry)
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
			if (Realm.Client.Core.GameHost.Instance != null)
			{
				Realm.Client.Core.GameHost.Instance.ActivePlaceId = selectedFile;
			}
		}
	}

	public void TriggerAddObjectMode()
	{
		if (Realm.Client.Core.GameHost.Instance == null)
		{
			return;
		}

		Realm.Client.Core.GameHost.EditorTool targetTool = GetEditorToolForCategory(_currentCategory);
		string placeId = GetSelectedCategoryPlaceId();
		
		_hud.TriggerToolSelection(targetTool, _btnAddObject, placeId);
	}

	private Realm.Client.Core.GameHost.EditorTool GetEditorToolForCategory(string category)
	{
		if (category == "Units" || category == "Characters" || category == "Buildings")
		{
			return Realm.Client.Core.GameHost.EditorTool.PlaceUnit;
		}
		
		if (category == "Decals")
		{
			return Realm.Client.Core.GameHost.EditorTool.PlaceDecal;
		}
		
		if (category == "VFX")
		{
			return Realm.Client.Core.GameHost.EditorTool.PlaceVfx;
		}
		
		return Realm.Client.Core.GameHost.EditorTool.PlaceProp;
	}

	private string GetSelectedCategoryPlaceId()
	{
		if (_categoryFiles.Count == 0)
		{
			return "";
		}

		int selectedIndex = _optCategoryItems != null ? _optCategoryItems.Selected : -1;
		
		if (selectedIndex >= 0)
		{
			if (selectedIndex < _categoryFiles.Count)
			{
				return _categoryFiles[selectedIndex];
			}
		}

		return _categoryFiles[0];
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
			searchName = System.IO.Path.GetFileName(filename);
		}

		int idx = _categoryFiles.FindIndex(f => System.IO.Path.GetFileName(f).Equals(searchName, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0)
		{
			_optCategoryItems.Selected = idx;
			SelectCategoryItem(idx);
			TriggerAddObjectMode();
		}
		else
		{
			Realm.Client.Core.GameHost.EditorTool targetTool = Realm.Client.Core.GameHost.EditorTool.PlaceProp;
			if (category == "Characters" || category == "Buildings")
			{
				targetTool = Realm.Client.Core.GameHost.EditorTool.PlaceUnit;
			}
			else if (category == "Decals")
			{
				targetTool = Realm.Client.Core.GameHost.EditorTool.PlaceDecal;
			}
			else if (category == "VFX")
			{
				targetTool = Realm.Client.Core.GameHost.EditorTool.PlaceVfx;
			}

			_hud.TriggerToolSelection(targetTool, _btnAddObject, filename);
		}
	}
}