using Realm.Client.Services;
using System;
using Godot;
using Realm.Client.Core;
using Realm.Ecs;

namespace Realm.Client.UI.InGame;

public class InventoryPanel
{
	private struct SlotControls
	{
		public Control SlotRoot;
		public Panel BlackTile;
		public Button ItemButton;
		public Label CountLabel;
		public string LastItemId;
		public int LastCount;
	}

	private struct ItemSlotData
	{
		public string ItemId;
		public int Count;
	}

	private readonly GridContainer _inventoryGrid;
	private readonly SlotControls[] _slots = new SlotControls[6];

	private static StyleBoxFlat s_blackTileStyle;
	private static StyleBoxFlat s_buttonNormal;
	private static StyleBoxFlat s_buttonHover;
	private static StyleBoxFlat s_buttonPressed;
	private static StyleBoxEmpty s_buttonFocus;

	private Entity _lastFocusedEntity = Entity.Null;
	private int _lastFocusIdx = -1;
	private int _currentFocusIdx = 0;
	private bool _lastVisible = false;
	private int _lastItemCount = -1;
	private readonly ItemSlotData[] _lastItemSlots = new ItemSlotData[6];
	private readonly ItemSlotData[] _scratchItems = new ItemSlotData[6];

	public InventoryPanel(GridContainer inventoryGrid)
	{
		_inventoryGrid = inventoryGrid;
		if (_inventoryGrid == null) return;

		EnsureStyleBoxes();
		InitializeSlots();
	}

	private static void EnsureStyleBoxes()
	{
		if (s_blackTileStyle == null)
		{
			s_blackTileStyle = new StyleBoxFlat();
			s_blackTileStyle.BgColor = new Color(0.04f, 0.04f, 0.06f, 0.35f);
			s_blackTileStyle.BorderColor = new Color(UIStyle.ColorBronze.R, UIStyle.ColorBronze.G, UIStyle.ColorBronze.B, 0.25f);
			s_blackTileStyle.SetBorderWidthAll(1);
			s_blackTileStyle.CornerRadiusTopLeft = 3;
			s_blackTileStyle.CornerRadiusTopRight = 3;
			s_blackTileStyle.CornerRadiusBottomLeft = 3;
			s_blackTileStyle.CornerRadiusBottomRight = 3;
		}

		if (s_buttonNormal == null)
		{
			s_buttonNormal = UIStyle.CreateHUDButtonStyle(false, false);
			s_buttonHover = UIStyle.CreateHUDButtonStyle(true, false);
			s_buttonPressed = UIStyle.CreateHUDButtonStyle(false, true);
			s_buttonFocus = new StyleBoxEmpty();
		}
	}

	private void InitializeSlots()
	{
		foreach (Node child in _inventoryGrid.GetChildren())
		{
			child.QueueFree();
		}

		for (int i = 0; i < 6; i++)
		{
			var slotRoot = new Control();
			slotRoot.Name = $"Slot_{i}";
			slotRoot.CustomMinimumSize = new Vector2(40, 40);
			slotRoot.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			slotRoot.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
			slotRoot.Visible = false;

			var blackTile = new Panel();
			blackTile.Name = "Tile_Panel";
			blackTile.AddThemeStyleboxOverride("panel", s_blackTileStyle);
			blackTile.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			blackTile.MouseFilter = Control.MouseFilterEnum.Ignore;
			slotRoot.AddChild(blackTile);

			var btn = new Button();
			btn.Name = "Btn_Button";
			btn.Flat = false;
			btn.ExpandIcon = true;
			btn.CustomMinimumSize = new Vector2(40, 40);
			btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			btn.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
			btn.FocusMode = Control.FocusModeEnum.None;
			btn.ClipContents = true;
			btn.AddThemeConstantOverride("icon_max_width", 34);

			btn.AddThemeStyleboxOverride("normal", s_buttonNormal);
			btn.AddThemeStyleboxOverride("hover", s_buttonHover);
			btn.AddThemeStyleboxOverride("pressed", s_buttonPressed);
			btn.AddThemeStyleboxOverride("focus", s_buttonFocus);
			btn.SetAnchorsPreset(Control.LayoutPreset.FullRect);

			var countLabel = new Label();
			countLabel.Name = "CountLabel_Label";
			countLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
			countLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			countLabel.HorizontalAlignment = HorizontalAlignment.Right;
			countLabel.VerticalAlignment = VerticalAlignment.Bottom;
			countLabel.Modulate = new Color(1f, 1f, 1f, 0.7f);
			countLabel.AddThemeFontSizeOverride("font_size", 12);
			countLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.8f));
			countLabel.AddThemeConstantOverride("outline_size", 3);
			btn.AddChild(countLabel);

			slotRoot.AddChild(btn);
			_inventoryGrid.AddChild(slotRoot);

			int slotIndex = i;
			btn.Pressed += () => OnSlotPressed(slotIndex);

			_slots[i] = new SlotControls
			{
				SlotRoot = slotRoot,
				BlackTile = blackTile,
				ItemButton = btn,
				CountLabel = countLabel,
				LastItemId = null,
				LastCount = -1
			};
		}
	}

	private void OnSlotPressed(int slotIndex)
	{
		string itemId = _slots[slotIndex].LastItemId;
		if (string.IsNullOrEmpty(itemId)) return;

		var selected = GameContext.Host?.SelectedUnits;
		int localPlayer = GameContext.PerspectiveFilter.ControlledPlayerIndex;
		int focusIdx = _currentFocusIdx;
		if (selected != null && selected.Count > focusIdx && selected[focusIdx].Player == localPlayer)
		{
			GameContext.Host.UseItem(selected[focusIdx], itemId);
		}
	}

	public void Update(InGameHUDViewModel viewModel)
	{
		if (_inventoryGrid == null) return;

		if (viewModel.SelectedUnits.Count == 0)
		{
			HideAllSlots();
			return;
		}

		int focusIdx = viewModel.CycleSelectionIndex;
		if (focusIdx < 0 || focusIdx >= viewModel.SelectedUnits.Count) focusIdx = 0;
		var focusedUnit = viewModel.SelectedUnits[focusIdx];

		if (focusedUnit.IsEnemy || (focusedUnit.IsBuilding && !focusedUnit.HasInventory && (focusedUnit.InventoryItems == null || focusedUnit.InventoryItems.Count == 0)))
		{
			HideAllSlots();
			return;
		}

		_currentFocusIdx = focusIdx;

		int currentItemCount = 0;
		if (focusedUnit.InventoryItems != null)
		{
			foreach (var kvp in focusedUnit.InventoryItems)
			{
				if (currentItemCount >= 6) break;
				if (kvp.Value <= 0) continue;

				_scratchItems[currentItemCount] = new ItemSlotData { ItemId = kvp.Key, Count = kvp.Value };
				currentItemCount++;
			}
		}

		bool dirty = !_lastVisible || focusedUnit.Entity != _lastFocusedEntity || focusIdx != _lastFocusIdx || currentItemCount != _lastItemCount;
		if (!dirty)
		{
			for (int i = 0; i < currentItemCount; i++)
			{
				if (_scratchItems[i].ItemId != _lastItemSlots[i].ItemId || _scratchItems[i].Count != _lastItemSlots[i].Count)
				{
					dirty = true;
					break;
				}
			}
		}

		if (!dirty) return;

		_lastVisible = true;
		_lastFocusedEntity = focusedUnit.Entity;
		_lastFocusIdx = focusIdx;
		_lastItemCount = currentItemCount;

		for (int i = 0; i < 6; i++)
		{
			_slots[i].SlotRoot.Visible = true;

			if (i < currentItemCount)
			{
				var itemData = _scratchItems[i];
				_lastItemSlots[i] = itemData;

				if (_slots[i].LastItemId != itemData.ItemId || _slots[i].LastCount != itemData.Count)
				{
					UpdateItemSlot(i, itemData.ItemId, itemData.Count);
				}

				_slots[i].BlackTile.Visible = false;
				_slots[i].ItemButton.Visible = true;
			}
			else
			{
				_lastItemSlots[i] = default;
				_slots[i].LastItemId = null;
				_slots[i].LastCount = -1;

				_slots[i].BlackTile.Visible = true;
				_slots[i].ItemButton.Visible = false;
			}
		}
	}

	private void UpdateItemSlot(int index, string itemId, int count)
	{
		string name = itemId.ToUpper();
		string desc = "Item";
		string iconPath = "res://Assets/UI/alliance_flag.webp";

		if (GameHost.ItemRegistry.TryGetValue(itemId, out var itemMeta))
		{
			if (!string.IsNullOrEmpty(itemMeta.Name)) name = itemMeta.Name;
			if (!string.IsNullOrEmpty(itemMeta.Description)) desc = itemMeta.Description;
			if (!string.IsNullOrEmpty(itemMeta.IconPath)) iconPath = itemMeta.IconPath;
		}

		string statBlock = TooltipStatBlock.ForItem(itemMeta, count);
		string rawTooltip = $"{name} (Have: {count})\n{desc}";
		if (statBlock.Length > 0) rawTooltip += "\n" + statBlock;
		string transTooltip = TranslationServer.Translate(rawTooltip);

		ref var slot = ref _slots[index];
		slot.LastItemId = itemId;
		slot.LastCount = count;
		slot.ItemButton.Icon = !string.IsNullOrEmpty(iconPath) ? RtexIconLoader.Load(iconPath) : null;
		slot.ItemButton.TooltipText = TooltipTemplate.Resolve(
			string.IsNullOrEmpty(transTooltip) ? rawTooltip : transTooltip,
			TooltipContext.ForUnit(_lastFocusedEntity));
		slot.CountLabel.Text = count.ToString();
	}

	private void HideAllSlots()
	{
		if (!_lastVisible) return;

		_lastVisible = false;
		_lastFocusedEntity = Entity.Null;
		_lastFocusIdx = -1;
		_lastItemCount = -1;

		for (int i = 0; i < 6; i++)
		{
			_slots[i].SlotRoot.Visible = false;
			_slots[i].LastItemId = null;
			_slots[i].LastCount = -1;
			_lastItemSlots[i] = default;
		}
	}

	public static void ClearCache()
	{
		s_blackTileStyle = null;
		s_buttonNormal = null;
		s_buttonHover = null;
		s_buttonPressed = null;
		s_buttonFocus = null;
	}
}
