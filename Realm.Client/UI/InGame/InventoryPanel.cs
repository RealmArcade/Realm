using Godot;
using System;

namespace Realm.Client.UI.InGame;

public class InventoryPanel
{
	private GridContainer _inventoryGrid;

	public InventoryPanel(GridContainer inventoryGrid)
	{
		_inventoryGrid = inventoryGrid;
	}

	public void Update(InGameHUDViewModel viewModel)
	{
		if (_inventoryGrid == null) return;

		ClearGrid();

		if (viewModel.SelectedUnits.Count == 0)
		{
			FillWithBlackTiles(6);
			return;
		}

		int focusIdx = viewModel.CycleSelectionIndex;
		if (focusIdx < 0 || focusIdx >= viewModel.SelectedUnits.Count) focusIdx = 0;
		var focusedUnit = viewModel.SelectedUnits[focusIdx];

		if (focusedUnit.IsEnemy || focusedUnit.IsBuilding)
		{
			FillWithBlackTiles(6);
			return;
		}

		int totalItems = PopulateInventoryItems(focusedUnit, focusIdx);
		FillWithBlackTiles(6 - totalItems);
	}

	private void ClearGrid()
	{
		foreach (Node child in _inventoryGrid.GetChildren())
		{
			child.QueueFree();
		}
	}

	private void FillWithBlackTiles(int count)
	{
		for (int i = 0; i < count; i++)
		{
			_inventoryGrid.AddChild(CreateBlackTile());
		}
	}

	private int PopulateInventoryItems(InGameHUDViewModel.SelectedUnitInfo focusedUnit, int focusIdx)
	{
		if (focusedUnit.InventoryItems == null) return 0;

		int totalItems = 0;
		foreach (var kvp in focusedUnit.InventoryItems)
		{
			if (totalItems >= 6) break;

			string itemId = kvp.Key;
			int count = kvp.Value;
			if (count <= 0) continue;

			var btn = CreateItemButton(itemId, count, focusIdx);
			_inventoryGrid.AddChild(btn);
			totalItems++;
		}
		
		return totalItems;
	}

	private Button CreateItemButton(string itemId, int count, int focusIdx)
	{
		string name = itemId.ToUpper();
		string desc = "Item";
		string iconPath = "res://Assets/UI/alliance_flag.png";

		if (Realm.Client.Core.GameHost.ItemRegistry.TryGetValue(itemId, out var itemMeta))
		{
			if (!string.IsNullOrEmpty(itemMeta.Name)) name = itemMeta.Name;
			if (!string.IsNullOrEmpty(itemMeta.Description)) desc = itemMeta.Description;
			if (!string.IsNullOrEmpty(itemMeta.IconPath)) iconPath = itemMeta.IconPath;
		}

		string capturedItemId = itemId;
		return CreateButton(
			iconPath,
			$"{name} (Have: {count})\n{desc}",
			$" {count} ",
			() => {
				var selected = Realm.Client.Core.GameHost.Instance?.SelectedUnits;
				if (selected != null && selected.Count > focusIdx && !selected[focusIdx].IsEnemy)
				{
					Realm.Client.Core.GameHost.Instance.UseItem(selected[focusIdx], capturedItemId);
				}
			}
		);
	}

	private ColorRect CreateBlackTile()
	{
		var tile = new ColorRect();
		tile.Color = Colors.Black;
		tile.CustomMinimumSize = new Vector2(44, 44);
		tile.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		tile.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		return tile;
	}

	private Button CreateButton(string iconPath, string tooltip, string text, Action callback)
	{
		var btn = new Button();
		btn.Flat = false;
		btn.Text = text;
		btn.ExpandIcon = true;
		btn.Icon = !string.IsNullOrEmpty(iconPath) ? RtexIconLoader.Load(iconPath) : null;
		
		string transTooltip = TranslationServer.Translate(tooltip);
		btn.TooltipText = string.IsNullOrEmpty(transTooltip) ? tooltip : transTooltip;
		
		btn.CustomMinimumSize = new Vector2(44, 44);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		btn.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		btn.FocusMode = Control.FocusModeEnum.None;
		btn.ClipContents = true;
		btn.AddThemeConstantOverride("icon_max_width", 38);

		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateHUDButtonStyle(false, false));
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateHUDButtonStyle(true, false));
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateHUDButtonStyle(false, true));
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		btn.Disabled = false;
		btn.Modulate = Colors.White;

		btn.Pressed += () => {
			callback?.Invoke();
		};

		return btn;
	}
}