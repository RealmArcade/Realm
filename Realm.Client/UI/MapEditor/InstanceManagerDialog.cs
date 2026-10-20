using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Client.VFX;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class InstanceManagerDialog : FloatingDialogBase
{
	public InstanceManagerDialog() { }

	private Tree _objectTree;
	private LineEdit _filterInput;
	private OptionButton _typeFilterOpt;
	private Label _summaryLabel;
	private string _filterText = string.Empty;
	private readonly Dictionary<TreeItem, Node3D> _treeItemToObjectMap = new();
	private readonly Dictionary<TreeItem, string> _treeItemToCoordinateMap = new();
	private Texture2D? _trashIconTexture;

	private double _refreshCheckTimer = 0.0;
	private int _lastUnitsCount = -1;
	private int _lastPropsCount = -1;
	private int _lastDecalsCount = -1;
	private int _lastCoordinatesCount = -1;

	public InstanceManagerDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Placed Objects"), new Vector2(720, 680))
	{
		SetUncompressedPanelTexture("res://Assets/UI/map_editor_placed_objects.webp", 55, 60, 135, 135);
		BuildControls();
		SetFooterCloseOnly();
	}

	private void BuildControls()
	{
		var topHBox = new HBoxContainer();
		topHBox.Name = "TopHBox_HBoxContainer";
		topHBox.AddThemeConstantOverride("separation", 8);

		var searchLabel = new Label();
		searchLabel.Name = "SearchLabel_Label";
		searchLabel.Text = TranslationServer.Translate("Filter:");
		searchLabel.AddThemeFontSizeOverride("font_size", 11);
		searchLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		topHBox.AddChild(searchLabel);

		_typeFilterOpt = new OptionButton();
		_typeFilterOpt.Name = "TypeFilterOpt_OptionButton";
		_typeFilterOpt.CustomMinimumSize = new Vector2(110, 26);
		_typeFilterOpt.AddThemeFontSizeOverride("font_size", 11);
		_typeFilterOpt.AddItem(TranslationServer.Translate("All Types"), 0);
		_typeFilterOpt.AddItem(TranslationServer.Translate("Units"), 1);
		_typeFilterOpt.AddItem(TranslationServer.Translate("Buildings"), 2);
		_typeFilterOpt.AddItem(TranslationServer.Translate("Resources"), 3);
		_typeFilterOpt.AddItem(TranslationServer.Translate("Props"), 4);
		_typeFilterOpt.AddItem(TranslationServer.Translate("Decals"), 5);
		_typeFilterOpt.AddItem(TranslationServer.Translate("VFX / Effects"), 6);
		_typeFilterOpt.AddItem(TranslationServer.Translate("Coordinates"), 7);
		_typeFilterOpt.Selected = 0;
		_typeFilterOpt.ItemSelected += (_) => RefreshObjectTree();
		topHBox.AddChild(_typeFilterOpt);

		_filterInput = new LineEdit();
		_filterInput.Name = "FilterInput_LineEdit";
		_filterInput.PlaceholderText = TranslationServer.Translate("Search by name, ID, or category...");
		_filterInput.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_filterInput.AddThemeFontSizeOverride("font_size", 11);
		_filterInput.TextChanged += (text) =>
		{
			_filterText = text?.Trim() ?? string.Empty;
			RefreshObjectTree();
		};
		topHBox.AddChild(_filterInput);

		AddButton(topHBox, $"{UnicodeIcons.REFRESH} " + TranslationServer.Translate("Refresh"), () => RefreshObjectTree(), "Refresh object list", 11, new Vector2(85, 26));

		BodyContainer.AddChild(topHBox);

		_summaryLabel = new Label();
		_summaryLabel.Name = "SummaryLabel_Label";
		_summaryLabel.AddThemeFontSizeOverride("font_size", 11);
		_summaryLabel.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		BodyContainer.AddChild(_summaryLabel);

		_objectTree = new Tree();
		_objectTree.Name = "ObjectTree_Tree";
		_objectTree.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_objectTree.SizeFlagsVertical = SizeFlags.ExpandFill;
		_objectTree.CustomMinimumSize = new Vector2(0, 420);
		_objectTree.HideRoot = true;
		_objectTree.Columns = 2;
		_objectTree.SetColumnTitle(0, TranslationServer.Translate("Instance"));
		_objectTree.SetColumnTitle(1, TranslationServer.Translate("Position"));
		_objectTree.SetColumnExpand(0, true);
		_objectTree.SetColumnExpand(1, false);
		_objectTree.SetColumnCustomMinimumWidth(1, 180);
		_objectTree.ColumnTitlesVisible = true;
		_objectTree.AddThemeFontSizeOverride("font_size", 11);

		_objectTree.ItemSelected += OnTreeItemSelected;
		_objectTree.ItemActivated += OnTreeItemActivated;
		_objectTree.ButtonClicked += OnTreeButtonClicked;

		BodyContainer.AddChild(_objectTree);
	}

	private Texture2D? GetTrashIconTexture()
	{
		if (_trashIconTexture != null) return _trashIconTexture;

		_trashIconTexture = UIStyle.GetUnicodeIconTexture(UnicodeIcons.TRASH);
		return _trashIconTexture;
	}

	private bool HasCountsChanged()
	{
		int curUnits = GameHost.Instance.AllUnits?.Count ?? 0;
		int curProps = GameHost.Instance.AllProps?.Count ?? 0;
		int curDecals = GameHost.Instance.AllDecals?.Count ?? 0;
		int curCoords = GameContext.EditorService?.EditorCoordinates?.Count ?? 0;

		return curUnits != _lastUnitsCount || curProps != _lastPropsCount || curDecals != _lastDecalsCount || curCoords != _lastCoordinatesCount;
	}

	public override void _Process(double delta)
	{
		base._Process(delta);
		if (!IsOpen || GameHost.Instance == null) return;

		_refreshCheckTimer += delta;
		if (_refreshCheckTimer < 0.15) return;
		_refreshCheckTimer = 0.0;

		if (HasCountsChanged() || IsAnyTrackedNodeInvalid())
		{
			RefreshObjectTree();
		}
	}

	private bool IsAnyTrackedNodeInvalid()
	{
		foreach (var node in _treeItemToObjectMap.Values)
		{
			if (!GodotObject.IsInstanceValid(node) || node.IsQueuedForDeletion() || !node.IsInsideTree())
			{
				return true;
			}
		}

		return false;
	}

	public void RefreshIfOpen()
	{
		if (IsOpen)
		{
			RefreshObjectTree();
		}
	}

	public void OpenDialog(string? initialTypeFilter)
	{
		if (_typeFilterOpt != null && !string.IsNullOrEmpty(initialTypeFilter))
		{
			for (int i = 0; i < _typeFilterOpt.ItemCount; i++)
			{
				string itemText = _typeFilterOpt.GetItemText(i);
				if (itemText.IndexOf(initialTypeFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
				    initialTypeFilter.IndexOf(itemText, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					_typeFilterOpt.Selected = i;
					break;
				}
			}
		}

		base.OpenDialog();
		RefreshObjectTree();
	}

	public override void OpenDialog()
	{
		OpenDialog(null);
	}

	public void RefreshObjectTree()
	{
		_objectTree.Clear();
		_treeItemToObjectMap.Clear();
		_treeItemToCoordinateMap.Clear();

		if (GameHost.Instance == null)
		{
			TreeItem emptyRoot = _objectTree.CreateItem();
			_summaryLabel.Text = TranslationServer.Translate("No active map loaded.");
			return;
		}

		UpdateLastCounts();

		TreeItem rootNode = _objectTree.CreateItem();

		var placedObjects = CollectAllPlacedObjects();
		int totalObjectCount = placedObjects.Count;

		var groupedObjects = GroupPlacedObjectsByAssetType(placedObjects);

		string selectedType = _typeFilterOpt != null && _typeFilterOpt.Selected > 0
			? _typeFilterOpt.GetItemText(_typeFilterOpt.Selected)
			: string.Empty;

		int matchedObjectCount = 0;
		foreach (var categoryGroup in groupedObjects)
		{
			if (!string.IsNullOrEmpty(selectedType) &&
			    !categoryGroup.Key.Equals(selectedType, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			matchedObjectCount += ProcessCategoryGroup(rootNode, categoryGroup.Key, categoryGroup.Value);
		}

		UpdateSummaryLabel(totalObjectCount, matchedObjectCount);
	}

	private void UpdateLastCounts()
	{
		_lastUnitsCount = GameHost.Instance.AllUnits?.Count ?? 0;
		_lastPropsCount = GameHost.Instance.AllProps?.Count ?? 0;
		_lastDecalsCount = GameHost.Instance.AllDecals?.Count ?? 0;
		_lastCoordinatesCount = GameContext.EditorService?.EditorCoordinates?.Count ?? 0;
	}

	private void UpdateSummaryLabel(int totalCount, int matchedCount)
	{
		string selectedType = _typeFilterOpt != null && _typeFilterOpt.Selected > 0
			? _typeFilterOpt.GetItemText(_typeFilterOpt.Selected)
			: string.Empty;

		if (string.IsNullOrEmpty(_filterText) && string.IsNullOrEmpty(selectedType))
		{
			_summaryLabel.Text = string.Format(TranslationServer.Translate("Total placed objects: {0}"), totalCount);
		}
		else
		{
			_summaryLabel.Text = string.Format(TranslationServer.Translate("Matching objects: {0} of {1}"), matchedCount, totalCount);
		}
	}

	private int ProcessCategoryGroup(TreeItem rootNode, string categoryName, List<(string DisplayTitle, Node3D? Node, string? PosText, string? CoordName)> objectsInCategory)
	{
		int matchedCount = 0;
		TreeItem categoryNode = null;

		foreach (var (displayTitle, node, posText, coordName) in objectsInCategory)
		{
			if (!string.IsNullOrEmpty(_filterText) &&
			    displayTitle.IndexOf(_filterText, StringComparison.OrdinalIgnoreCase) < 0 &&
			    categoryName.IndexOf(_filterText, StringComparison.OrdinalIgnoreCase) < 0)
			{
				continue;
			}

			if (categoryNode == null)
			{
				categoryNode = _objectTree.CreateItem(rootNode);
				categoryNode.SetText(0, $"{categoryName} ({objectsInCategory.Count})");
				categoryNode.SetSelectable(0, false);
				categoryNode.SetSelectable(1, false);
				categoryNode.SetCustomColor(0, UIStyle.ColorGold);
			}

			TreeItem itemNode = _objectTree.CreateItem(categoryNode);
			itemNode.SetText(0, displayTitle);

			var trashIcon = GetTrashIconTexture();
			if (trashIcon != null)
			{
				itemNode.AddButton(0, trashIcon, 0, false, TranslationServer.Translate("Delete Object"));
			}

			if (node != null)
			{
				Vector3 pos = node.Position;
				itemNode.SetText(1, $"({pos.X:F1}, {pos.Y:F1}, {pos.Z:F1})");

				if (GameHost.Instance.SelectedEditorObject == node)
				{
					itemNode.Select(0);
					categoryNode.Collapsed = false;
				}

				_treeItemToObjectMap[itemNode] = node;
			}
			else if (!string.IsNullOrEmpty(coordName))
			{
				itemNode.SetText(1, posText ?? string.Empty);
				_treeItemToCoordinateMap[itemNode] = coordName;
			}

			matchedCount++;
		}

		return matchedCount;
	}

	private List<(string DisplayTitle, string AssetType, Node3D? Node, string? PosText, string? CoordName)> CollectAllPlacedObjects()
	{
		var result = new List<(string DisplayTitle, string AssetType, Node3D? Node, string? PosText, string? CoordName)>();
		if (GameHost.Instance == null) return result;

		CollectUnits(result);
		CollectProps(result);
		CollectDecals(result);
		CollectVfxObjectsRecursive(GameHost.Instance, result);
		CollectCoordinates(result);

		return result;
	}

	private void CollectUnits(List<(string DisplayTitle, string AssetType, Node3D? Node, string? PosText, string? CoordName)> result)
	{
		if (GameHost.Instance.AllUnits == null) return;

		foreach (var unit in GameHost.Instance.AllUnits)
		{
			if (!GodotObject.IsInstanceValid(unit)) continue;

			string assetType = "Units";
			string resolvedName = GetUnitRegistryName(unit.UnitId);

			if (unit.IsBuilding)
			{
				assetType = "Buildings";
				resolvedName = GetBuildingRegistryName(unit.UnitId);
			}
			else if (unit.IsResource)
			{
				assetType = "Resources";
				resolvedName = GetResourceRegistryName(unit.UnitId);
			}

			string displayTitle = $"{resolvedName} [Player {unit.Player}]";
			result.Add((displayTitle, assetType, unit, null, null));
		}
	}

	private string GetBuildingRegistryName(string id)
	{
		if (GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.TryGetValue(id, out var bMeta) && !string.IsNullOrEmpty(bMeta.Name))
			return bMeta.Name;
		return Path.GetFileNameWithoutExtension(id);
	}

	private string GetResourceRegistryName(string id)
	{
		if (GameHost.ResourceRegistry != null && GameHost.ResourceRegistry.TryGetValue(id, out var rMeta) && !string.IsNullOrEmpty(rMeta.Name))
			return rMeta.Name;
		return Path.GetFileNameWithoutExtension(id);
	}

	private string GetUnitRegistryName(string id)
	{
		if (GameHost.UnitRegistry != null && GameHost.UnitRegistry.TryGetValue(id, out var uMeta) && !string.IsNullOrEmpty(uMeta.Name))
			return uMeta.Name;
		return Path.GetFileNameWithoutExtension(id);
	}

	private void CollectProps(List<(string DisplayTitle, string AssetType, Node3D? Node, string? PosText, string? CoordName)> result)
	{
		if (GameHost.Instance.AllProps == null) return;

		foreach (var prop in GameHost.Instance.AllProps)
		{
			if (!GodotObject.IsInstanceValid(prop)) continue;

			string resolvedName = GetPropRegistryName(prop.PropId);
			result.Add((resolvedName, "Props", prop, null, null));
		}
	}

	private string GetPropRegistryName(string id)
	{
		if (GameHost.PropRegistry != null && GameHost.PropRegistry.TryGetValue(id, out var pMeta) && !string.IsNullOrEmpty(pMeta.Name))
			return pMeta.Name;
		return Path.GetFileNameWithoutExtension(id);
	}

	private void CollectDecals(List<(string DisplayTitle, string AssetType, Node3D? Node, string? PosText, string? CoordName)> result)
	{
		if (GameHost.Instance.AllDecals == null) return;

		foreach (var decal in GameHost.Instance.AllDecals)
		{
			if (!GodotObject.IsInstanceValid(decal)) continue;

			string resolvedName = decal is Decal3D d3d ? d3d.DecalId : Path.GetFileNameWithoutExtension(decal.Name);
			result.Add((resolvedName, "Decals", decal, null, null));
		}
	}

	private void CollectVfxObjectsRecursive(Node parentNode, List<(string DisplayTitle, string AssetType, Node3D? Node, string? PosText, string? CoordName)> resultList)
	{
		if (parentNode == null) return;

		foreach (Node childNode in parentNode.GetChildren())
		{
			if (childNode is ProceduralVfxInstance3D vfx && GodotObject.IsInstanceValid(vfx))
			{
				string resolvedName = !string.IsNullOrEmpty(vfx.Config?.Name)
					? vfx.Config.Name
					: vfx.Config?.PrimitiveType.ToString() ?? "VFX";
				resultList.Add((resolvedName, "VFX / Effects", vfx, null, null));
			}
			else
			{
				CollectVfxObjectsRecursive(childNode, resultList);
			}
		}
	}

	private void CollectCoordinates(List<(string DisplayTitle, string AssetType, Node3D? Node, string? PosText, string? CoordName)> result)
	{
		var coordinates = GameContext.EditorService?.EditorCoordinates;
		if (coordinates == null) return;

		foreach (var coord in coordinates)
		{
			string displayTitle = coord.Name;
			string posText = $"({coord.MinX:F0},{coord.MinZ:F0}) → ({coord.MaxX:F0},{coord.MaxZ:F0})";
			result.Add((displayTitle, "Coordinates", null, posText, coord.Name));
		}
	}

	private Dictionary<string, List<(string DisplayTitle, Node3D? Node, string? PosText, string? CoordName)>> GroupPlacedObjectsByAssetType(List<(string DisplayTitle, string AssetType, Node3D? Node, string? PosText, string? CoordName)> rawList)
	{
		var grouped = new Dictionary<string, List<(string DisplayTitle, Node3D? Node, string? PosText, string? CoordName)>>(StringComparer.OrdinalIgnoreCase);

		foreach (var item in rawList)
		{
			if (!grouped.TryGetValue(item.AssetType, out var list))
			{
				list = new List<(string DisplayTitle, Node3D? Node, string? PosText, string? CoordName)>();
				grouped[item.AssetType] = list;
			}

			list.Add((item.DisplayTitle, item.Node, item.PosText, item.CoordName));
		}

		return grouped;
	}

	private void OnTreeItemSelected()
	{
		TreeItem selectedItem = _objectTree.GetSelected();
		if (selectedItem == null) return;

		if (_treeItemToObjectMap.TryGetValue(selectedItem, out Node3D targetObject) && GodotObject.IsInstanceValid(targetObject))
		{
			FocusOnObject(targetObject);
		}
		else if (_treeItemToCoordinateMap.TryGetValue(selectedItem, out string coordName))
		{
			FocusOnCoordinate(coordName);
		}
	}

	private void OnTreeButtonClicked(TreeItem item, long column, long id, long mouseButtonIndex)
	{
		if (item == null) return;

		if (_treeItemToObjectMap.TryGetValue(item, out Node3D targetObject) && GodotObject.IsInstanceValid(targetObject))
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.SelectedEditorObject = targetObject;
			}
			DeleteObject(targetObject);
		}
		else if (_treeItemToCoordinateMap.TryGetValue(item, out string coordName) && !string.IsNullOrEmpty(coordName))
		{
			GameContext.IEditorAPI?.DeleteCoordinate(coordName);
			RefreshObjectTree();
		}
	}

	private void OnTreeItemActivated()
	{
		TreeItem selectedItem = _objectTree.GetSelected();
		if (selectedItem == null) return;

		if (_treeItemToObjectMap.TryGetValue(selectedItem, out Node3D targetObject) && GodotObject.IsInstanceValid(targetObject))
		{
			FocusOnObject(targetObject);
		}
		else if (_treeItemToCoordinateMap.TryGetValue(selectedItem, out string coordName))
		{
			FocusOnCoordinate(coordName);
		}
	}

	private void FocusOnCoordinate(string coordName)
	{
		if (string.IsNullOrEmpty(coordName)) return;

		GameContext.IEditorAPI?.SelectCoordinate(coordName);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Selected and focused on coordinate '{0}'"), coordName));
	}

	private void FocusOnObject(Node3D targetObject)
	{
		if (targetObject == null || !GodotObject.IsInstanceValid(targetObject) || GameHost.Instance == null) return;

		Hud?.SelectToolFromHotkey(EditorTool.SelectMove);
		GameHost.Instance.SelectedEditorObject = targetObject;
		GameHost.Instance.MainCamera?.FocusOnPosition(targetObject.Position);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Selected and focused on {0}"), targetObject.Name));
	}

	private void DeleteObject(Node3D targetObject)
	{
		if (targetObject == null || !GodotObject.IsInstanceValid(targetObject) || GameHost.Instance == null) return;

		Hud?.DeleteSelectedObjectAction();
		RefreshObjectTree();
	}
}