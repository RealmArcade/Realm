using Realm.Client.Services;
using System;
using Godot;
using Realm.Client.Core;
using Realm.Ecs;
using Realm.Shared.Abilities;
using Realm.Shared.Metadata;

namespace Realm.Client.UI.InGame;

public class InventoryPanel
{
	private struct SlotControls
	{
		public Control SlotRoot;
		public Panel BlackTile;
		public Button ItemButton;
		public Label CountLabel;
		public string? LastItemId;
		public int LastCount;
		public bool LastIsCarriedConsumable;
	}

	private struct ItemSlotData
	{
		public string ItemId;
		public int Count;

		public bool IsCarriedConsumable;
	}

	private readonly GridContainer _inventoryGrid = null!;
	private readonly SlotControls[] _slots = new SlotControls[6];

	private struct SlotStyles
	{
		public StyleBoxFlat BlackTile;
		public StyleBoxFlat Normal;
		public StyleBoxFlat Hover;
		public StyleBoxFlat Pressed;
		public StyleBoxEmpty Focus;
	}

	private static SlotStyles? s_styles;

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

		InitializeSlots(EnsureStyles());
	}

	private static SlotStyles EnsureStyles()
	{
		if (s_styles.HasValue) return s_styles.Value;

		var blackTile = UIStyle.CreateButtonPlate(
			new Color(0.04f, 0.04f, 0.06f, 0.35f),
			new Color(UIStyle.ColorBronze.R, UIStyle.ColorBronze.G, UIStyle.ColorBronze.B, 0.25f),
			3);

		var styles = new SlotStyles
		{
			BlackTile = blackTile,
			Normal = UIStyle.CreateHUDButtonStyle(false, false),
			Hover = UIStyle.CreateHUDButtonStyle(true, false),
			Pressed = UIStyle.CreateHUDButtonStyle(false, true),
			Focus = new StyleBoxEmpty()
		};
		s_styles = styles;
		return styles;
	}

	private void InitializeSlots(SlotStyles styles)
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
			blackTile.AddThemeStyleboxOverride("panel", styles.BlackTile);
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

			btn.AddThemeStyleboxOverride("normal", styles.Normal);
			btn.AddThemeStyleboxOverride("hover", styles.Hover);
			btn.AddThemeStyleboxOverride("pressed", styles.Pressed);
			btn.AddThemeStyleboxOverride("focus", styles.Focus);
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
				LastCount = -1,
				LastIsCarriedConsumable = false
			};
		}
	}

	private void OnSlotPressed(int slotIndex)
	{
		if (_slots[slotIndex].LastIsCarriedConsumable) return;

		string? itemId = _slots[slotIndex].LastItemId;
		if (string.IsNullOrEmpty(itemId)) return;

		if (GameContext.Host is not { } host) return;
		var selected = host.SelectedUnits;
		int localPlayer = GameContext.PerspectiveFilter.ControlledPlayerIndex;
		int focusIdx = _currentFocusIdx;
		if (selected.Count > focusIdx && selected[focusIdx].Player == localPlayer)
		{
			host.UseItem(selected[focusIdx], itemId);
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

		int carriedConsumableCount = focusedUnit.CarriedConsumables?.Count ?? 0;
		bool showsNothing = (focusedUnit.InventoryItems == null || focusedUnit.InventoryItems.Count == 0) && carriedConsumableCount == 0;
		if (focusedUnit.IsEnemy || (focusedUnit.IsBuilding && !focusedUnit.HasInventory && showsNothing))
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

		if (focusedUnit.CarriedConsumables != null)
		{
			foreach (CarriedConsumableInfo carried in focusedUnit.CarriedConsumables)
			{
				if (currentItemCount >= 6) break;
				if (string.IsNullOrEmpty(carried.DefinitionId)) continue;

				_scratchItems[currentItemCount] = new ItemSlotData { ItemId = carried.DefinitionId, Count = 1, IsCarriedConsumable = true };
				currentItemCount++;
			}
		}

		bool dirty = !_lastVisible || focusedUnit.Entity != _lastFocusedEntity || focusIdx != _lastFocusIdx || currentItemCount != _lastItemCount;
		if (!dirty)
		{
			for (int i = 0; i < currentItemCount; i++)
			{
				if (_scratchItems[i].ItemId != _lastItemSlots[i].ItemId
				    || _scratchItems[i].Count != _lastItemSlots[i].Count
				    || _scratchItems[i].IsCarriedConsumable != _lastItemSlots[i].IsCarriedConsumable)
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

				if (_slots[i].LastItemId != itemData.ItemId || _slots[i].LastCount != itemData.Count || _slots[i].LastIsCarriedConsumable != itemData.IsCarriedConsumable)
				{
					UpdateItemSlot(i, itemData);
				}

				_slots[i].BlackTile.Visible = false;
				_slots[i].ItemButton.Visible = true;
			}
			else
			{
				_lastItemSlots[i] = default;
				_slots[i].LastItemId = null;
				_slots[i].LastCount = -1;
				_slots[i].LastIsCarriedConsumable = false;

				_slots[i].BlackTile.Visible = true;
				_slots[i].ItemButton.Visible = false;
			}
		}
	}

	private void UpdateItemSlot(int index, ItemSlotData itemData)
	{
		ref var slot = ref _slots[index];
		slot.LastItemId = itemData.ItemId;
		slot.LastCount = itemData.Count;
		slot.LastIsCarriedConsumable = itemData.IsCarriedConsumable;

		if (itemData.IsCarriedConsumable)
		{
			RenderCarriedConsumable(ref slot, itemData.ItemId);
			return;
		}

		string name = itemData.ItemId.ToUpper();
		string desc = "Item";
		string iconPath = "res://Assets/UI/alliance_flag.webp";

		if (GameHost.ItemRegistry.TryGetValue(itemData.ItemId, out var itemMeta))
		{
			if (!string.IsNullOrEmpty(itemMeta.Name)) name = MapText.TranslateName(itemMeta.Name);
			if (!string.IsNullOrEmpty(itemMeta.Description)) desc = MapText.Translate(itemMeta.Description);
			if (!string.IsNullOrEmpty(itemMeta.IconPath)) iconPath = itemMeta.IconPath;
		}

		string statBlock = TooltipStatBlock.ForItem(itemMeta, itemData.Count);
		string rawTooltip = $"{name} (Have: {itemData.Count})\n{desc}";
		if (statBlock.Length > 0) rawTooltip += "\n" + statBlock;
		string transTooltip = TranslationServer.Translate(rawTooltip);

		slot.ItemButton.Icon = !string.IsNullOrEmpty(iconPath) ? RtexIconLoader.Load(iconPath) : null;
		slot.ItemButton.TooltipText = TooltipTemplate.Resolve(
			string.IsNullOrEmpty(transTooltip) ? rawTooltip : transTooltip,
			TooltipContext.ForUnit(_lastFocusedEntity));
		slot.CountLabel.Text = itemData.Count.ToString();
	}

	private void RenderCarriedConsumable(ref SlotControls slot, string definitionId)
	{
		string name = definitionId.ToUpper();
		string iconPath = "res://Assets/UI/alliance_flag.webp";
		string tooltip = string.Empty;

		var registry = GameContext.RegistryService;
		ConsumableMetadata? consumable = registry?.GetConsumable(definitionId);
		if (consumable != null)
		{
			AbilityDefinition? card = registry?.GetAbilityDefinition(consumable.CardId);
			if (card != null)
			{
				if (!string.IsNullOrEmpty(card.DisplayName)) name = MapText.TranslateName(card.DisplayName);
				if (!string.IsNullOrEmpty(card.IconPath)) iconPath = card.IconPath;
				tooltip = MapText.TranslateTemplate(card.TooltipTemplate);
			}
		}

		if (tooltip.Length == 0) tooltip = $"{name}\n{TranslationServer.Translate("CARRIED")}";

		slot.ItemButton.Icon = RtexIconLoader.Load(iconPath);
		slot.ItemButton.TooltipText = TooltipTemplate.Resolve(tooltip, TooltipContext.ForAbility(_lastFocusedEntity, consumable?.CardId));
		slot.CountLabel.Text = string.Empty;
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
			_slots[i].LastIsCarriedConsumable = false;
			_lastItemSlots[i] = default;
		}
	}

	public static void ClearCache()
	{
		s_styles = null;
	}
}
