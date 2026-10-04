using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Godot.VFX;
using Realm.Godot.Services;
using Realm.Godot.Utils;

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
		if (_idToDisplayName.TryGetValue(file, out var name) && !string.IsNullOrEmpty(name))
			return name;
		if (file.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase))
		{
			string sub = file.Substring(4);
			if (GameHost.VfxRegistry != null && GameHost.VfxRegistry.TryGetValue(sub, out var vReg) && !string.IsNullOrEmpty(vReg.Name))
				return "✨ " + vReg.Name;
			return "✨ " + sub;
		}
		if (GameHost.VfxRegistry != null && GameHost.VfxRegistry.TryGetValue(file, out var vMeta) && !string.IsNullOrEmpty(vMeta.Name))
			return "✨ " + vMeta.Name;
		if (GameHost.UnitRegistry != null && GameHost.UnitRegistry.TryGetValue(file, out var uMeta) && !string.IsNullOrEmpty(uMeta.Name))
			return uMeta.Name;
		if (GameHost.PropRegistry != null && GameHost.PropRegistry.TryGetValue(file, out var pMeta) && !string.IsNullOrEmpty(pMeta.Name))
			return pMeta.Name;
		if (GameHost.ResourceRegistry != null && GameHost.ResourceRegistry.TryGetValue(file, out var rMeta) && !string.IsNullOrEmpty(rMeta.Name))
			return rMeta.Name;
		return System.IO.Path.GetFileNameWithoutExtension(file).Replace("_", " ");
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
			UIManager.Instance?.PlayClickSound();
			onClick?.Invoke();
		};
	}

	public void SelectCategory(string category, bool triggerAddObject = true)
	{
		_currentCategory = category;

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

		string previousSelectedId = null;
		if (_optCategoryItems != null && _optCategoryItems.Selected >= 0 && _optCategoryItems.Selected < _categoryFiles.Count)
		{
			previousSelectedId = _categoryFiles[_optCategoryItems.Selected];
		}

		_categoryFiles.Clear();
		_idToDisplayName.Clear();
		_optCategoryItems.Clear();

		try
		{
			string wsPath = MapEditorHUD.TempWorkspaceGodotPath;
			string globalWs = Godot.ProjectSettings.GlobalizePath(wsPath);

			if (MetadataService.Instance.TryLoadMetadata(globalWs, out var metadata))
			{
				if (category == "VFX")
				{
					foreach (var prim in Enum.GetValues<VfxPrimitiveType>())
					{
						string primKey = $"vfx:{prim}";
						if (!_categoryFiles.Contains(primKey))
						{
							_categoryFiles.Add(primKey);
							_idToDisplayName[primKey] = "✨ " + prim.ToString();
						}
					}

					if (GameHost.VfxRegistry != null)
					{
						foreach (var kvp in GameHost.VfxRegistry)
						{
							string vfxKey = kvp.Key;
							if (!_categoryFiles.Contains(vfxKey))
							{
								_categoryFiles.Add(vfxKey);
								_idToDisplayName[vfxKey] = "✨ " + (!string.IsNullOrEmpty(kvp.Value.Name) ? kvp.Value.Name : kvp.Key);
							}
						}
					}

					if (metadata.CustomVfx != null)
					{
						foreach (var vObj in metadata.CustomVfx)
						{
							string vId = vObj.VfxId ?? "";
							string name = vObj.Name ?? "";
							if (!string.IsNullOrEmpty(vId) && !_categoryFiles.Contains(vId))
							{
								_categoryFiles.Add(vId);
								if (!string.IsNullOrEmpty(name))
								{
									_idToDisplayName[vId] = "✨ " + name;
								}
							}
						}
					}
				}
				else if (category == "Decals")
				{
					if (metadata.Decals != null)
					{
						foreach (var kvp in metadata.Decals)
						{
							string decalKey = kvp.Key;
							if (!_categoryFiles.Contains(decalKey))
							{
								_categoryFiles.Add(decalKey);
								_idToDisplayName[decalKey] = TemplateIDHelper.ParseTemplateID(decalKey).Slug.Replace("_", " ");
							}
						}
					}

					var unionedAssets = Realm.Godot.Utils.MapAssetHelper.LoadAssets(globalWs);
					if (unionedAssets.ContainsKey("decals") && unionedAssets["decals"] is System.Text.Json.Nodes.JsonObject decalsObj)
					{
						foreach (var kvp in decalsObj)
						{
							string decalFile = kvp.Key;
							string relDecalPath = System.IO.Path.Combine("Assets", "decals", decalFile);
							if (!_categoryFiles.Contains(relDecalPath) && !_categoryFiles.Contains(decalFile))
							{
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
					}
				}
				else
				{
					if (category == "Buildings")
					{
						if (metadata.CustomBuildings != null)
						{
							foreach (var b in metadata.CustomBuildings)
							{
								if (!string.IsNullOrEmpty(b.TemplateID) && !_categoryFiles.Contains(b.TemplateID))
								{
									_categoryFiles.Add(b.TemplateID);
									if (!string.IsNullOrEmpty(b.Name)) _idToDisplayName[b.TemplateID] = b.Name;
								}
							}
						}
						if (GameHost.BuildingRegistry != null)
						{
							foreach (var kvp in GameHost.BuildingRegistry)
							{
								if (!string.IsNullOrEmpty(kvp.Key) && !_categoryFiles.Contains(kvp.Key))
								{
									_categoryFiles.Add(kvp.Key);
									if (!string.IsNullOrEmpty(kvp.Value.Name)) _idToDisplayName[kvp.Key] = kvp.Value.Name;
								}
							}
						}
					}
					else if (category == "Units" || category == "Characters")
					{
						if (metadata.CustomUnits != null)
						{
							foreach (var u in metadata.CustomUnits)
							{
								if (!string.IsNullOrEmpty(u.TemplateID) && !_categoryFiles.Contains(u.TemplateID))
								{
									_categoryFiles.Add(u.TemplateID);
									if (!string.IsNullOrEmpty(u.Name)) _idToDisplayName[u.TemplateID] = u.Name;
								}
							}
						}
						if (GameHost.UnitRegistry != null)
						{
							foreach (var kvp in GameHost.UnitRegistry)
							{
								if (!string.IsNullOrEmpty(kvp.Key) && !_categoryFiles.Contains(kvp.Key))
								{
									_categoryFiles.Add(kvp.Key);
									if (!string.IsNullOrEmpty(kvp.Value.Name)) _idToDisplayName[kvp.Key] = kvp.Value.Name;
								}
							}
						}
					}
					else if (category == "Resources" || category == "Environment")
					{
						if (metadata.CustomResources != null)
						{
							foreach (var r in metadata.CustomResources)
							{
								if (!string.IsNullOrEmpty(r.TemplateID) && !_categoryFiles.Contains(r.TemplateID))
								{
									_categoryFiles.Add(r.TemplateID);
									if (!string.IsNullOrEmpty(r.Name)) _idToDisplayName[r.TemplateID] = r.Name;
								}
							}
						}
						if (GameHost.ResourceRegistry != null)
						{
							foreach (var kvp in GameHost.ResourceRegistry)
							{
								if (!string.IsNullOrEmpty(kvp.Key) && !_categoryFiles.Contains(kvp.Key))
								{
									_categoryFiles.Add(kvp.Key);
									if (!string.IsNullOrEmpty(kvp.Value.Name)) _idToDisplayName[kvp.Key] = kvp.Value.Name;
								}
							}
						}
					}
					else if (category == "Props")
					{
						if (metadata.CustomProps != null)
						{
							foreach (var p in metadata.CustomProps)
							{
								if (!string.IsNullOrEmpty(p.TemplateID) && !_categoryFiles.Contains(p.TemplateID))
								{
									_categoryFiles.Add(p.TemplateID);
									if (!string.IsNullOrEmpty(p.Name)) _idToDisplayName[p.TemplateID] = p.Name;
								}
							}
						}
						if (GameHost.PropRegistry != null)
						{
							foreach (var kvp in GameHost.PropRegistry)
							{
								if (!string.IsNullOrEmpty(kvp.Key) && !_categoryFiles.Contains(kvp.Key))
								{
									_categoryFiles.Add(kvp.Key);
									if (!string.IsNullOrEmpty(kvp.Value.Name)) _idToDisplayName[kvp.Key] = kvp.Value.Name;
								}
							}
						}
					}
				}
			}
		}
		catch { }

		if (category == "VFX" && _categoryFiles.Count == 0)
		{
			foreach (var prim in Enum.GetValues<VfxPrimitiveType>())
			{
				string primKey = $"vfx:{prim}";
				if (!_categoryFiles.Contains(primKey))
				{
					_categoryFiles.Add(primKey);
					_idToDisplayName[primKey] = "✨ " + prim.ToString();
				}
			}

			if (GameHost.VfxRegistry != null)
			{
				foreach (var kvp in GameHost.VfxRegistry)
				{
					string vfxKey = kvp.Key;
					if (!_categoryFiles.Contains(vfxKey))
					{
						_categoryFiles.Add(vfxKey);
						_idToDisplayName[vfxKey] = "✨ " + (!string.IsNullOrEmpty(kvp.Value.Name) ? kvp.Value.Name : kvp.Key);
					}
				}
			}
		}

		_categoryFiles.Sort((a, b) => string.Compare(GetDisplayNameForId(a), GetDisplayNameForId(b), StringComparison.OrdinalIgnoreCase));

		foreach (var file in _categoryFiles)
		{
			string displayName = GetDisplayNameForId(file);
			_optCategoryItems.AddItem(TranslationServer.Translate(displayName));
		}

		if (_optCategoryItems.ItemCount > 0)
		{
			int targetIndex = 0;
			if (!string.IsNullOrEmpty(previousSelectedId))
			{
				int found = _categoryFiles.FindIndex(f => f.Equals(previousSelectedId, StringComparison.OrdinalIgnoreCase));
				if (found >= 0) targetIndex = found;
			}
			_optCategoryItems.Selected = targetIndex;
			SelectCategoryItem(targetIndex);
		}

		if (triggerAddObject)
		{
			TriggerAddObjectMode();
		}
	}

	public void SelectCategoryItem(int index)
	{
		if (index >= 0 && index < _categoryFiles.Count)
		{
			string selectedFile = _categoryFiles[index];
			if (GameHost.Instance != null)
			{
				GameHost.Instance.ActivePlaceId = selectedFile;
			}
		}
	}

	public void TriggerAddObjectMode()
	{
		if (GameHost.Instance == null) return;

		GameHost.EditorTool targetTool = GameHost.EditorTool.PlaceProp;
		if (_currentCategory == "Units" || _currentCategory == "Characters" || _currentCategory == "Buildings")
		{
			targetTool = GameHost.EditorTool.PlaceUnit;
		}
		else if (_currentCategory == "Decals")
		{
			targetTool = GameHost.EditorTool.PlaceDecal;
		}
		else if (_currentCategory == "VFX")
		{
			targetTool = GameHost.EditorTool.PlaceVfx;
		}

		string placeId = "";
		int selectedIndex = _optCategoryItems != null ? _optCategoryItems.Selected : -1;
		if (selectedIndex >= 0 && selectedIndex < _categoryFiles.Count)
		{
			placeId = _categoryFiles[selectedIndex];
		}

		if (string.IsNullOrEmpty(placeId) && _categoryFiles.Count > 0)
		{
			placeId = _categoryFiles[0];
		}

		_hud.TriggerToolSelection(targetTool, _btnAddObject, placeId);
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
			GameHost.EditorTool targetTool = GameHost.EditorTool.PlaceProp;
			if (category == "Characters" || category == "Buildings")
			{
				targetTool = GameHost.EditorTool.PlaceUnit;
			}
			else if (category == "Decals")
			{
				targetTool = GameHost.EditorTool.PlaceDecal;
			}
			else if (category == "VFX")
			{
				targetTool = GameHost.EditorTool.PlaceVfx;
			}

			_hud.TriggerToolSelection(targetTool, _btnAddObject, filename);
		}
	}
}
