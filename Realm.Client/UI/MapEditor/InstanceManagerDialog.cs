using Godot;
using Realm.Client.VFX;
using System;
using System.Collections.Generic;

namespace Realm.Client.UI.MapEditor;

public partial class InstanceManagerDialog : Realm.Client.UI.MapEditor.FloatingDialogBase
{
	private Tree _objectTree;
	private LineEdit _filterInput;
	private Label _summaryLabel;
	private string _filterText = string.Empty;
	private readonly Dictionary<TreeItem, Node3D> _treeItemToObjectMap = new();

	private double _refreshCheckTimer = 0.0;
	private int _lastUnitsCount = -1;
	private int _lastPropsCount = -1;
	private int _lastDecalsCount = -1;

	public InstanceManagerDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Instance Manager"), new Vector2(500, 620))
	{
		SetUncompressedPanelTexture("res://Assets/UI/map_editor_panel.png", 30, 40, 50, 50);
		BuildControls();
		SetFooterCloseOnly();
	}

	private void BuildControls()
	{
		var topHBox = new HBoxContainer();
		topHBox.AddThemeConstantOverride("separation", 8);

		var searchLabel = new Label();
		searchLabel.Text = TranslationServer.Translate("Filter:");
		searchLabel.AddThemeFontSizeOverride("font_size", 11);
		searchLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		topHBox.AddChild(searchLabel);

		_filterInput = new LineEdit();
		_filterInput.PlaceholderText = TranslationServer.Translate("Search by name, ID, or category...");
		_filterInput.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
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
		_summaryLabel.AddThemeFontSizeOverride("font_size", 11);
		_summaryLabel.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		BodyContainer.AddChild(_summaryLabel);

		_objectTree = new Tree();
		_objectTree.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_objectTree.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_objectTree.CustomMinimumSize = new Vector2(0, 420);
		_objectTree.HideRoot = true;
		_objectTree.Columns = 2;
		_objectTree.SetColumnTitle(0, TranslationServer.Translate("Instance"));
		_objectTree.SetColumnTitle(1, TranslationServer.Translate("Position"));
		_objectTree.SetColumnExpand(0, true);
		_objectTree.SetColumnExpand(1, false);
		_objectTree.SetColumnCustomMinimumWidth(1, 150);
		_objectTree.ColumnTitlesVisible = true;
		_objectTree.AddThemeFontSizeOverride("font_size", 11);

		_objectTree.ItemSelected += OnTreeItemSelected;
		_objectTree.ItemActivated += OnTreeItemActivated;

		BodyContainer.AddChild(_objectTree);
	}

	private bool HasCountsChanged()
	{
		int curUnits = Realm.Client.Core.GameHost.Instance.AllUnits?.Count ?? 0;
		int curProps = Realm.Client.Core.GameHost.Instance.AllProps?.Count ?? 0;
		int curDecals = Realm.Client.Core.GameHost.Instance.AllDecals?.Count ?? 0;

		return curUnits != _lastUnitsCount || curProps != _lastPropsCount || curDecals != _lastDecalsCount;
	}

	public override void _Process(double delta)
	{
		base._Process(delta);
		if (!IsOpen || Realm.Client.Core.GameHost.Instance == null) return;

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

	public override void OpenDialog()
	{
		base.OpenDialog();
		RefreshObjectTree();
	}

	public void RefreshObjectTree()
	{
		_objectTree.Clear();
		_treeItemToObjectMap.Clear();

		if (Realm.Client.Core.GameHost.Instance == null)
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

		int matchedObjectCount = 0;
		foreach (var categoryGroup in groupedObjects)
		{
			matchedObjectCount += ProcessCategoryGroup(rootNode, categoryGroup.Key, categoryGroup.Value);
		}

		UpdateSummaryLabel(totalObjectCount, matchedObjectCount);
	}

	private void UpdateLastCounts()
	{
		_lastUnitsCount = Realm.Client.Core.GameHost.Instance.AllUnits?.Count ?? 0;
		_lastPropsCount = Realm.Client.Core.GameHost.Instance.AllProps?.Count ?? 0;
		_lastDecalsCount = Realm.Client.Core.GameHost.Instance.AllDecals?.Count ?? 0;
	}

	private void UpdateSummaryLabel(int totalCount, int matchedCount)
	{
		if (string.IsNullOrEmpty(_filterText))
		{
			_summaryLabel.Text = string.Format(TranslationServer.Translate("Total placed objects: {0}"), totalCount);
		}
		else
		{
			_summaryLabel.Text = string.Format(TranslationServer.Translate("Matching objects: {0} of {1}"), matchedCount, totalCount);
		}
	}

	private int ProcessCategoryGroup(TreeItem rootNode, string categoryName, List<(string DisplayTitle, Node3D Node)> objectsInCategory)
	{
		int matchedCount = 0;
		TreeItem categoryNode = null;

		foreach (var (displayTitle, node) in objectsInCategory)
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
			Vector3 pos = node.Position;
			itemNode.SetText(1, $"({pos.X:F1}, {pos.Y:F1}, {pos.Z:F1})");

			if (Realm.Client.Core.GameHost.Instance.SelectedEditorObject == node)
			{
				itemNode.Select(0);
				categoryNode.Collapsed = false;
			}

			_treeItemToObjectMap[itemNode] = node;
			matchedCount++;
		}
		
		return matchedCount;
	}

	private List<(string DisplayTitle, string AssetType, Node3D Node)> CollectAllPlacedObjects()
	{
		var result = new List<(string DisplayTitle, string AssetType, Node3D Node)>();
		if (Realm.Client.Core.GameHost.Instance == null) return result;

		CollectUnits(result);
		CollectProps(result);
		CollectDecals(result);
		CollectVfxObjectsRecursive(Realm.Client.Core.GameHost.Instance, result);

		return result;
	}

	private void CollectUnits(List<(string DisplayTitle, string AssetType, Node3D Node)> result)
	{
		if (Realm.Client.Core.GameHost.Instance.AllUnits == null) return;

		foreach (var unit in Realm.Client.Core.GameHost.Instance.AllUnits)
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
			result.Add((displayTitle, assetType, unit));
		}
	}

	private string GetBuildingRegistryName(string id)
	{
		if (Realm.Client.Core.GameHost.BuildingRegistry != null && Realm.Client.Core.GameHost.BuildingRegistry.TryGetValue(id, out var bMeta) && !string.IsNullOrEmpty(bMeta.Name))
			return bMeta.Name;
		return System.IO.Path.GetFileNameWithoutExtension(id);
	}

	private string GetResourceRegistryName(string id)
	{
		if (Realm.Client.Core.GameHost.ResourceRegistry != null && Realm.Client.Core.GameHost.ResourceRegistry.TryGetValue(id, out var rMeta) && !string.IsNullOrEmpty(rMeta.Name))
			return rMeta.Name;
		return System.IO.Path.GetFileNameWithoutExtension(id);
	}

	private string GetUnitRegistryName(string id)
	{
		if (Realm.Client.Core.GameHost.UnitRegistry != null && Realm.Client.Core.GameHost.UnitRegistry.TryGetValue(id, out var uMeta) && !string.IsNullOrEmpty(uMeta.Name))
			return uMeta.Name;
		return System.IO.Path.GetFileNameWithoutExtension(id);
	}

	private void CollectProps(List<(string DisplayTitle, string AssetType, Node3D Node)> result)
	{
		if (Realm.Client.Core.GameHost.Instance.AllProps == null) return;

		foreach (var prop in Realm.Client.Core.GameHost.Instance.AllProps)
		{
			if (!GodotObject.IsInstanceValid(prop)) continue;

			string resolvedName = GetPropRegistryName(prop.PropId);
			result.Add((resolvedName, "Props", prop));
		}
	}

	private string GetPropRegistryName(string id)
	{
		if (Realm.Client.Core.GameHost.PropRegistry != null && Realm.Client.Core.GameHost.PropRegistry.TryGetValue(id, out var pMeta) && !string.IsNullOrEmpty(pMeta.Name))
			return pMeta.Name;
		return System.IO.Path.GetFileNameWithoutExtension(id);
	}

	private void CollectDecals(List<(string DisplayTitle, string AssetType, Node3D Node)> result)
	{
		if (Realm.Client.Core.GameHost.Instance.AllDecals == null) return;

		foreach (var decal in Realm.Client.Core.GameHost.Instance.AllDecals)
		{
			if (!GodotObject.IsInstanceValid(decal)) continue;

			string resolvedName = decal is Realm.Client.Decal3D d3d ? d3d.DecalId : System.IO.Path.GetFileNameWithoutExtension(decal.Name);
			result.Add((resolvedName, "Decals", decal));
		}
	}

	private void CollectVfxObjectsRecursive(Node parentNode, List<(string DisplayTitle, string AssetType, Node3D Node)> resultList)
	{
		if (parentNode == null) return;

		foreach (Node childNode in parentNode.GetChildren())
		{
			if (childNode is ProceduralVfxInstance3D vfx && GodotObject.IsInstanceValid(vfx))
			{
				string resolvedName = !string.IsNullOrEmpty(vfx.Config?.Name)
					? vfx.Config.Name
					: vfx.Config?.PrimitiveType.ToString() ?? "VFX";
				resultList.Add((resolvedName, "VFX / Effects", vfx));
			}
			else
			{
				CollectVfxObjectsRecursive(childNode, resultList);
			}
		}
	}

	private Dictionary<string, List<(string DisplayTitle, Node3D Node)>> GroupPlacedObjectsByAssetType(List<(string DisplayTitle, string AssetType, Node3D Node)> rawList)
	{
		var grouped = new Dictionary<string, List<(string DisplayTitle, Node3D Node)>>(StringComparer.OrdinalIgnoreCase);

		foreach (var item in rawList)
		{
			if (!grouped.TryGetValue(item.AssetType, out var list))
			{
				list = new List<(string DisplayTitle, Node3D Node)>();
				grouped[item.AssetType] = list;
			}
			list.Add((item.DisplayTitle, item.Node));
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
	}

	private void OnTreeItemActivated()
	{
		TreeItem selectedItem = _objectTree.GetSelected();
		if (selectedItem == null) return;

		if (_treeItemToObjectMap.TryGetValue(selectedItem, out Node3D targetObject) && GodotObject.IsInstanceValid(targetObject))
		{
			FocusOnObject(targetObject);
		}
	}

	private void FocusOnObject(Node3D targetObject)
	{
		if (targetObject == null || !GodotObject.IsInstanceValid(targetObject) || Realm.Client.Core.GameHost.Instance == null) return;

		Hud?.SelectToolFromHotkey(Realm.Client.Core.GameHost.EditorTool.SelectMove);
		Realm.Client.Core.GameHost.Instance.SelectedEditorObject = targetObject;
		(Realm.Client.Core.GameHost.Instance.MainCamera as Realm.Client.CameraControl)?.FocusOnPosition(targetObject.Position);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Selected and focused on {0}"), targetObject.Name));
	}

	private void DeleteObject(Node3D targetObject)
	{
		if (targetObject == null || !GodotObject.IsInstanceValid(targetObject) || Realm.Client.Core.GameHost.Instance == null) return;

		Hud?.DeleteSelectedObjectAction();
		RefreshObjectTree();
	}
}