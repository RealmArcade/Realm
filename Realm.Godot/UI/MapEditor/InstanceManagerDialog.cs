using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Godot.VFX;

public partial class InstanceManagerDialog : FloatingDialogBase
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

		AddButton(topHBox, "\uf021 " + TranslationServer.Translate("Refresh"), () => RefreshObjectTree(), "Refresh object list", 11, new Vector2(85, 26));

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

		var bottomHBox = new HBoxContainer();
		bottomHBox.AddThemeConstantOverride("separation", 8);

		AddButton(bottomHBox, "\uf2ed " + TranslationServer.Translate("Delete Selected"), () =>
		{
			var selectedItem = _objectTree.GetSelected();
			if (selectedItem != null && _treeItemToObjectMap.TryGetValue(selectedItem, out var node) && GodotObject.IsInstanceValid(node))
			{
				DeleteObject(node);
			}
		}, "Delete selected object", 11, new Vector2(130, 26));

		BodyContainer.AddChild(bottomHBox);
	}

	public override void _Process(double delta)
	{
		base._Process(delta);
		if (!IsOpen || GameHost.Instance == null) return;

		_refreshCheckTimer += delta;
		if (_refreshCheckTimer < 0.15) return;
		_refreshCheckTimer = 0.0;

		int curUnits = GameHost.Instance.AllUnits?.Count ?? 0;
		int curProps = GameHost.Instance.AllProps?.Count ?? 0;
		int curDecals = GameHost.Instance.AllDecals?.Count ?? 0;

		bool countsChanged = curUnits != _lastUnitsCount || curProps != _lastPropsCount || curDecals != _lastDecalsCount;
		bool anyNodeInvalid = false;

		if (!countsChanged)
		{
			foreach (var node in _treeItemToObjectMap.Values)
			{
				if (!GodotObject.IsInstanceValid(node) || node.IsQueuedForDeletion() || !node.IsInsideTree())
				{
					anyNodeInvalid = true;
					break;
				}
			}
		}

		if (countsChanged || anyNodeInvalid)
		{
			RefreshObjectTree();
		}
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

		if (GameHost.Instance != null)
		{
			_lastUnitsCount = GameHost.Instance.AllUnits?.Count ?? 0;
			_lastPropsCount = GameHost.Instance.AllProps?.Count ?? 0;
			_lastDecalsCount = GameHost.Instance.AllDecals?.Count ?? 0;
		}

		TreeItem rootNode = _objectTree.CreateItem();

		if (GameHost.Instance == null)
		{
			_summaryLabel.Text = TranslationServer.Translate("No active map loaded.");
			return;
		}

		var placedObjects = CollectAllPlacedObjects();
		int totalObjectCount = placedObjects.Count;

		var groupedObjects = GroupPlacedObjectsByAssetType(placedObjects);

		int matchedObjectCount = 0;

		foreach (var categoryGroup in groupedObjects)
		{
			string categoryName = categoryGroup.Key;
			var objectsInCategory = categoryGroup.Value;

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

				if (GameHost.Instance.SelectedEditorObject == node)
				{
					itemNode.Select(0);
					categoryNode.Collapsed = false;
				}

				_treeItemToObjectMap[itemNode] = node;
				matchedObjectCount++;
			}
		}

		if (string.IsNullOrEmpty(_filterText))
		{
			_summaryLabel.Text = string.Format(TranslationServer.Translate("Total placed objects: {0}"), totalObjectCount);
		}
		else
		{
			_summaryLabel.Text = string.Format(TranslationServer.Translate("Matching objects: {0} of {1}"), matchedObjectCount, totalObjectCount);
		}
	}

	private List<(string DisplayTitle, string AssetType, Node3D Node)> CollectAllPlacedObjects()
	{
		var result = new List<(string DisplayTitle, string AssetType, Node3D Node)>();
		if (GameHost.Instance == null) return result;

		if (GameHost.Instance.AllUnits != null)
		{
			foreach (var unit in GameHost.Instance.AllUnits)
			{
				if (!GodotObject.IsInstanceValid(unit)) continue;

				string assetType;
				string resolvedName;

				if (unit.IsBuilding)
				{
					assetType = "Buildings";
					if (GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.TryGetValue(unit.UnitId, out var bMeta) && !string.IsNullOrEmpty(bMeta.Name))
					{
						resolvedName = bMeta.Name;
					}
					else
					{
						resolvedName = System.IO.Path.GetFileNameWithoutExtension(unit.UnitId);
					}
				}
				else if (unit.IsResource)
				{
					assetType = "Resources";
					if (GameHost.ResourceRegistry != null && GameHost.ResourceRegistry.TryGetValue(unit.UnitId, out var rMeta) && !string.IsNullOrEmpty(rMeta.Name))
					{
						resolvedName = rMeta.Name;
					}
					else
					{
						resolvedName = System.IO.Path.GetFileNameWithoutExtension(unit.UnitId);
					}
				}
				else
				{
					assetType = "Units";
					if (GameHost.UnitRegistry != null && GameHost.UnitRegistry.TryGetValue(unit.UnitId, out var uMeta) && !string.IsNullOrEmpty(uMeta.Name))
					{
						resolvedName = uMeta.Name;
					}
					else
					{
						resolvedName = System.IO.Path.GetFileNameWithoutExtension(unit.UnitId);
					}
				}

				string displayTitle = $"{resolvedName} [Player {unit.Player}]";
				result.Add((displayTitle, assetType, unit));
			}
		}

		if (GameHost.Instance.AllProps != null)
		{
			foreach (var prop in GameHost.Instance.AllProps)
			{
				if (!GodotObject.IsInstanceValid(prop)) continue;

				string assetType = "Props";
				string resolvedName;

				if (GameHost.PropRegistry != null && GameHost.PropRegistry.TryGetValue(prop.PropId, out var pMeta) && !string.IsNullOrEmpty(pMeta.Name))
				{
					resolvedName = pMeta.Name;
				}
				else
				{
					resolvedName = System.IO.Path.GetFileNameWithoutExtension(prop.PropId);
				}

				result.Add((resolvedName, assetType, prop));
			}
		}

		if (GameHost.Instance.AllDecals != null)
		{
			foreach (var decal in GameHost.Instance.AllDecals)
			{
				if (!GodotObject.IsInstanceValid(decal)) continue;

				string assetType = "Decals";
				string resolvedName = decal is Decal3D d3d ? d3d.DecalId : System.IO.Path.GetFileNameWithoutExtension(decal.Name);
				result.Add((resolvedName, assetType, decal));
			}
		}

		CollectVfxObjectsRecursive(GameHost.Instance, result);

		return result;
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
		if (targetObject == null || !GodotObject.IsInstanceValid(targetObject) || GameHost.Instance == null) return;

		Hud?.SelectToolFromHotkey(GameHost.EditorTool.SelectMove);
		GameHost.Instance.SelectedEditorObject = targetObject;
		(GameHost.Instance.MainCamera as CameraControl)?.FocusOnPosition(targetObject.Position);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Selected and focused on {0}"), targetObject.Name));
	}

	private void DeleteObject(Node3D targetObject)
	{
		if (targetObject == null || !GodotObject.IsInstanceValid(targetObject) || GameHost.Instance == null) return;

		Hud?.DeleteSelectedObjectAction();
		RefreshObjectTree();
	}
}
