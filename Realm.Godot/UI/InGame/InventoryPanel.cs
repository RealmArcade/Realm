using Godot;
using System;
using System.Collections.Generic;
using Arch.Core;
using Realm.Godot.Utils;

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

		foreach (Node child in _inventoryGrid.GetChildren())
		{
			child.QueueFree();
		}

		if (viewModel.SelectedUnits.Count == 0)
		{
			return;
		}

		int focusIdx = viewModel.CycleSelectionIndex;
		if (focusIdx < 0 || focusIdx >= viewModel.SelectedUnits.Count) focusIdx = 0;
		var focusedUnit = viewModel.SelectedUnits[focusIdx];

		if (focusedUnit.IsEnemy || focusedUnit.IsBuilding)
		{
			return;
		}

        int totalItems = 0;

        if (focusedUnit.InventoryItems != null)
        {
            foreach (var kvp in focusedUnit.InventoryItems)
            {
                if (totalItems >= 6) break;

                string itemId = kvp.Key;
                int count = kvp.Value;
                if (count <= 0) continue;

                string name = itemId.ToUpper();
                string desc = "Item";
                string iconPath = "res://Assets/UI/alliance_flag.png";

                if (GameHost.ItemRegistry.TryGetValue(itemId, out var itemMeta))
                {
                    if (!string.IsNullOrEmpty(itemMeta.Name)) name = itemMeta.Name;
                    if (!string.IsNullOrEmpty(itemMeta.Description)) desc = itemMeta.Description;
                    if (!string.IsNullOrEmpty(itemMeta.IconPath)) iconPath = itemMeta.IconPath;
                }

                string capturedItemId = itemId;
                var btn = CreateButton(
                    iconPath,
                    $"{name} (Have: {count})\n{desc}",
                    $" {count} ",
                    () => {
                        var selected = GameHost.Instance?.SelectedUnits;
                        if (selected != null && selected.Count > focusIdx && !selected[focusIdx].IsEnemy)
                        {
                            GameHost.Instance.UseItem(selected[focusIdx], capturedItemId);
                        }
                    }
                );
                _inventoryGrid.AddChild(btn);
                totalItems++;
            }
        }

        // Fill rest of the 2x3 grid
        for (int i = totalItems; i < 6; i++)
        {
            _inventoryGrid.AddChild(CreateBlackTile());
        }
	}

	private Control CreateBlackTile()
	{
		var tile = new Panel();
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.04f, 0.04f, 0.06f, 0.35f);
		style.BorderColor = new Color(UIStyle.ColorBronze.R, UIStyle.ColorBronze.G, UIStyle.ColorBronze.B, 0.25f);
		style.SetBorderWidthAll(1);
		style.CornerRadiusTopLeft = 3;
		style.CornerRadiusTopRight = 3;
		style.CornerRadiusBottomLeft = 3;
		style.CornerRadiusBottomRight = 3;
		tile.AddThemeStyleboxOverride("panel", style);
		tile.CustomMinimumSize = new Vector2(34, 34);
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
		
		btn.CustomMinimumSize = new Vector2(34, 34);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		btn.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		btn.FocusMode = Control.FocusModeEnum.None;
		btn.ClipContents = true;
		btn.AddThemeConstantOverride("icon_max_width", 28);

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
