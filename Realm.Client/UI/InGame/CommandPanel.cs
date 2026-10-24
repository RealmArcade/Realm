using System;
using System.Collections.Generic;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using Realm.Client.Utils;

namespace Realm.Client.UI.InGame;

public partial class CommandPanel
{
	private GridContainer _commandGrid;
	private List<Button> _dynamicBuildButtons = new();

	public CommandPanel(GridContainer commandGrid)
	{
		_commandGrid = commandGrid;
	}

	private static Texture2D s_whiteTexture;
	private static readonly string[] s_integerStrings = CreateIntegerStringCache();

	private static string[] CreateIntegerStringCache()
	{
		var arr = new string[1000];
		for (int i = 0; i < arr.Length; i++)
		{
			arr[i] = i.ToString();
		}

		return arr;
	}

	private static string GetCachedIntegerString(int value)
	{
		if (value >= 0 && value < s_integerStrings.Length)
		{
			return s_integerStrings[value];
		}

		return value.ToString();
	}

	private static Texture2D GetOrCreateWhiteTexture()
	{
		if (s_whiteTexture == null)
		{
			var img = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
			img.Fill(Colors.White);
			s_whiteTexture = ImageTexture.CreateFromImage(img);
		}

		return s_whiteTexture;
	}

	private int _pageIndex = 0;
	private List<CommandCardItem> _activeItems = new();
	private readonly CommandCardItem[] _displayedGridItems = new CommandCardItem[12];
	private Entity _lastFocusedEntity = Entity.Null;
	private string _lastFocusedUnitId = null;
	private bool _lastIsUnderConstruction = false;
	private bool _lastIsMobile = false;
	private int _lastAbilityCount = -1;
	private int _lastPageIndex = -1;
	private bool _lastIsBuildSubMenuOpen = false;

	private void ClearCommandGrid()
	{
		foreach (var btn in _dynamicBuildButtons)
		{
			if (GodotObject.IsInstanceValid(btn))
			{
				btn.QueueFree();
			}
		}

		_dynamicBuildButtons.Clear();

		Array.Clear(_displayedGridItems);

		foreach (Node child in _commandGrid.GetChildren())
		{
			child.QueueFree();
		}
	}

	private static bool IsCommandsFrozen(Entity entity)
	{
		return entity != Entity.Null && GameContext.World != null && GameContext.World.IsAlive(entity) && GameContext.World.Has<CommandsFrozen>(entity);
	}

	private void UpdateExistingButtons()
	{
		var children = _commandGrid.GetChildren();
		int childCount = children.Count;

		for (int i = 0; i < _displayedGridItems.Length && i < childCount; i++)
		{
			if (children[i] is not Button btn) continue;

			var item = _displayedGridItems[i];
			if (item == null) continue;

			UpdateButtonContent(btn, item);
		}
	}

	private void UpdateButtonContent(Button btn, CommandCardItem item)
	{
		string newText = item.GetButtonText?.Invoke() ?? "";
		if (btn.Text != newText)
		{
			btn.Text = newText;
		}

		if (!string.IsNullOrEmpty(item.AbilityId))
		{
			UpdateAbilityButtonContent(btn, item);
			UpdateAbilityButtonVisuals(btn, item);
			return;
		}

		bool disabled = item.IsDisabled?.Invoke() ?? false;
		if (btn.Disabled != disabled)
		{
			btn.Disabled = disabled;
			btn.Modulate = disabled ? new Color(0.5f, 0.5f, 0.5f, 0.7f) : Colors.White;
		}
	}

	private void UpdateAbilityButtonContent(Button btn, CommandCardItem item)
	{
		var def = GameHost.Instance?.GetAbilityDefinition(item.AbilityId);
		if (def == null) return;

		string transTooltip = !string.IsNullOrEmpty(def.Tooltip) ? def.Tooltip : "";
		if (!string.IsNullOrEmpty(transTooltip) && btn.TooltipText != transTooltip)
		{
			btn.TooltipText = transTooltip;
		}

		if (!string.IsNullOrEmpty(def.IconPath))
		{
			var loadedIcon = LoadCommandIcon(def.IconPath);
			if (btn.Icon != loadedIcon)
			{
				btn.Icon = loadedIcon;
			}
		}
	}

	private void GetCasterState(Entity caster, EcsWorld world, string abilityId, ref float currentMana, ref float cdRemaining)
	{
		if (caster == Entity.Null || !world.IsAlive(caster)) return;

		if (world.Has<Mana>(caster))
		{
			currentMana = world.Get<Mana>(caster).Current;
		}

		if (world.Has<Cooldowns>(caster))
		{
			ref var cooldowns = ref world.Get<Cooldowns>(caster);
			if (cooldowns.TryGetValue(abilityId, out float val))
			{
				cdRemaining = val;
			}
		}

		if (world.Has<SpellCooldowns>(caster))
		{
			ref var spellCooldowns = ref world.Get<SpellCooldowns>(caster);
			if (spellCooldowns.TryGetValue(abilityId, out float val))
			{
				cdRemaining = Math.Max(cdRemaining, val);
			}
		}
	}

	private void UpdateAbilityButtonVisuals(Button btn, CommandCardItem item)
	{
		float cdRemaining = 0f;
		float currentMana = float.MaxValue;

		UpdateAbilityStats(item, ref cdRemaining, ref currentMana);

		var abilityDef = GameHost.Instance?.GetAbilityDefinition(item.AbilityId);
		float maxCd = GetMaxCooldown(abilityDef, cdRemaining);
		float manaCost = GetEffectiveManaCost(item, abilityDef);

		UpdateAbilityLabelsAndSweep(btn, manaCost, cdRemaining, maxCd);
		UpdateAbilityButtonState(btn, item, currentMana, manaCost, cdRemaining);
	}

	private float GetMaxCooldown(AbilityDefinition abilityDef, float cdRemaining)
	{
		if (abilityDef != null && abilityDef.Cooldown > 0f) return abilityDef.Cooldown;
		return Math.Max(10f, cdRemaining);
	}

	private float GetEffectiveManaCost(CommandCardItem item, AbilityDefinition abilityDef)
	{
		if (abilityDef != null && abilityDef.ManaCost > 0f) return abilityDef.ManaCost;
		return item.ManaCost;
	}

	private void UpdateAbilityButtonState(Button btn, CommandCardItem item, float currentMana, float manaCost, float cdRemaining)
	{
		bool notEnoughMana = currentMana < manaCost;
		bool isItemDisabled = item.IsDisabled?.Invoke() ?? false;
		bool disabled = cdRemaining > 0f || notEnoughMana || isItemDisabled;

		if (btn.Disabled != disabled) btn.Disabled = disabled;

		Color targetModulate = notEnoughMana ? new Color(0.5f, 0.5f, 0.5f, 0.7f) : Colors.White;
		if (btn.Modulate != targetModulate) btn.Modulate = targetModulate;
	}

	private void UpdateAbilityStats(CommandCardItem item, ref float cdRemaining, ref float currentMana)
	{
		GameHost tempQualifier = GameHost.Instance;
		var world = (tempQualifier != null ? GameContext.World : null);
		if (world == null) return;

		var caster = item.CasterEntity != Entity.Null && world.IsAlive(item.CasterEntity) ? item.CasterEntity : _lastFocusedEntity;
		GetCasterState(caster, world, item.AbilityId, ref currentMana, ref cdRemaining);

		if (cdRemaining <= 0f && GameHost.Instance != null)
		{
			cdRemaining = GameHost.Instance.GetPlayerSpellCooldown(GameContext.PerspectiveFilter.ControlledPlayerIndex, item.AbilityId);
		}
	}

	private void UpdateAbilityLabelsAndSweep(Button btn, float manaCost, float cdRemaining, float maxCd)
	{
		UpdateManaLabel(btn.GetNodeOrNull<Label>("ManaCostLabel"), manaCost);
		UpdateCooldownSweep(btn.GetNodeOrNull<TextureProgressBar>("CooldownSweep"), cdRemaining, maxCd);
		UpdateCooldownLabel(btn.GetNodeOrNull<Label>("CooldownLabel"), cdRemaining);
	}

	private void UpdateManaLabel(Label manaLabel, float manaCost)
	{
		if (manaLabel == null) return;
		string manaText = manaCost > 0f ? GetCachedIntegerString((int)manaCost) : "";
		if (manaLabel.Text != manaText) manaLabel.Text = manaText;
	}

	private void UpdateCooldownSweep(TextureProgressBar sweep, float cdRemaining, float maxCd)
	{
		if (sweep == null) return;
		bool onCd = cdRemaining > 0f;
		if (sweep.Visible != onCd) sweep.Visible = onCd;
		if (!onCd) return;

		double sweepVal = Math.Clamp((cdRemaining / maxCd) * 100.0, 0.0, 100.0);
		if (Math.Abs(sweep.Value - sweepVal) > 0.4) sweep.Value = sweepVal;
	}

	private void UpdateCooldownLabel(Label cdLabel, float cdRemaining)
	{
		if (cdLabel == null) return;
		string cdText = cdRemaining > 1.0f ? GetCachedIntegerString((int)Math.Ceiling(cdRemaining)) : "";
		if (cdLabel.Text != cdText) cdLabel.Text = cdText;
	}

	private void ClearCommandPanelState()
	{
		if (_lastFocusedEntity != Entity.Null || _commandGrid.GetChildCount() > 0)
		{
			ClearCommandGrid();
		}

		_lastFocusedEntity = Entity.Null;
		_lastFocusedUnitId = null;
		_lastIsUnderConstruction = false;
		_lastIsMobile = false;
		_lastAbilityCount = -1;
		_pageIndex = 0;
		_activeItems.Clear();
		_lastIsBuildSubMenuOpen = false;
	}

	public void Update(InGameHUDViewModel viewModel)
	{
		if (_commandGrid == null) return;
		if (GameContext.PerspectiveFilter.IsSpectating)
		{
			ClearCommandPanelState();
			return;
		}

		if (viewModel.SelectedUnits.Count == 0 || GetFocusedUnit(viewModel, out var focusedUnit) || focusedUnit.IsEnemy || !GameContext.PerspectiveFilter.CanControl(focusedUnit.Player))
		{
			ClearCommandPanelState();
			return;
		}

		UpdateCommandPanelState(focusedUnit, viewModel.IsBuildSubMenuOpen);
	}

	private bool GetFocusedUnit(InGameHUDViewModel viewModel, out SelectedUnitInfo focusedUnit)
	{
		int focusIdx = viewModel.CycleSelectionIndex;
		if (focusIdx < 0 || focusIdx >= viewModel.SelectedUnits.Count) focusIdx = 0;
		focusedUnit = viewModel.SelectedUnits[focusIdx];
		return false;
	}

	private void UpdateCommandPanelState(SelectedUnitInfo focusedUnit, bool subMenuOpen)
	{
		bool isMobile = focusedUnit.Speed > 0.001f;
		int abilityCount = focusedUnit.Abilities?.Count ?? 0;

		bool focusedUnitChanged = focusedUnit.Entity != _lastFocusedEntity;
		bool unitStateChanged = focusedUnitChanged
			|| focusedUnit.UnitId != _lastFocusedUnitId
			|| focusedUnit.IsUnderConstruction != _lastIsUnderConstruction
			|| isMobile != _lastIsMobile
			|| abilityCount != _lastAbilityCount;

		bool subMenuChanged = subMenuOpen != _lastIsBuildSubMenuOpen;

		if (focusedUnitChanged)
		{
			_pageIndex = 0;
		}

		bool needRebuildItems = unitStateChanged || subMenuChanged;

		if (needRebuildItems)
		{
			_lastFocusedEntity = focusedUnit.Entity;
			_lastFocusedUnitId = focusedUnit.UnitId;
			_lastIsUnderConstruction = focusedUnit.IsUnderConstruction;
			_lastIsMobile = isMobile;
			_lastAbilityCount = abilityCount;
			_lastIsBuildSubMenuOpen = subMenuOpen;

			_activeItems = GetCommandCardItems(focusedUnit, subMenuOpen);
			UpdateItemHotkeys();
		}

		bool pageChanged = _pageIndex != _lastPageIndex;
		_lastPageIndex = _pageIndex;

		if (needRebuildItems || pageChanged || _commandGrid.GetChildCount() == 0)
		{
			RebuildCommandGrid();
		}
		else
		{
			UpdateExistingButtons();
		}
	}

	private void UpdateItemHotkeys()
	{
		int totalItems = _activeItems.Count;
		int pageOffset = _pageIndex * 11;

		Key[] gridHotkeys =
		{
			Key.Q, Key.W, Key.E, Key.R,
			Key.A, Key.S, Key.D, Key.F,
			Key.Z, Key.X, Key.C, Key.V
		};

		for (int i = 0; i < totalItems; i++)
		{
			int localIdx = GetLocalGridIndex(i, totalItems, pageOffset);

			if (localIdx >= 0 && localIdx < 12)
			{
				if (_activeItems[i].Hotkey == Key.None)
				{
					_activeItems[i].Hotkey = gridHotkeys[localIdx];
				}
				FormatItemTooltipHotkey(_activeItems[i]);
			}
		}
	}

	private void FormatItemTooltipHotkey(CommandCardItem item)
	{
		if (item == null || item.Hotkey == Key.None || string.IsNullOrEmpty(item.Tooltip)) return;

		string tooltip = item.Tooltip;
		if (tooltip.StartsWith("[")) return;

		item.Tooltip = $"[{item.Hotkey}] {tooltip}";
	}

	private int GetLocalGridIndex(int globalIndex, int totalItems, int pageOffset)
	{
		if (totalItems <= 12) return globalIndex;
		if (globalIndex >= pageOffset && globalIndex < pageOffset + 11) return globalIndex - pageOffset;
		return -1;
	}

	private void RebuildCommandGrid()
	{
		ClearCommandGrid();
		int totalItems = _activeItems.Count;

		if (totalItems <= 12)
		{
			_pageIndex = 0;
			PopulateGridPage(0, 12, totalItems, false);
		}
		else
		{
			int numPages = (totalItems + 10) / 11;
			if (_pageIndex >= numPages) _pageIndex = 0;

			PopulateGridPage(_pageIndex * 11, 11, totalItems, true);
			_commandGrid.AddChild(CreateCycleButton(numPages));
		}
	}

	private void PopulateGridPage(int startIndex, int maxItems, int totalItems, bool hasCycleButton)
	{
		Key[] gridHotkeys =
		{
			Key.Q, Key.W, Key.E, Key.R,
			Key.A, Key.S, Key.D, Key.F,
			Key.Z, Key.X, Key.C, Key.V
		};

		var gridSlots = new CommandCardItem[12];
		var unslotted = new List<CommandCardItem>();

		for (int i = 0; i < maxItems; i++)
		{
			int itemIndex = startIndex + i;
			if (itemIndex >= totalItems) break;

			var item = _activeItems[itemIndex];
			if (item.GridX >= 0 && item.GridX < 4 && item.GridY >= 0 && item.GridY < 3)
			{
				int slotIndex = item.GridY * 4 + item.GridX;
				if (gridSlots[slotIndex] == null)
				{
					gridSlots[slotIndex] = item;
					continue;
				}
			}

			unslotted.Add(item);
		}

		int unslottedIdx = 0;
		for (int i = 0; i < maxItems; i++)
		{
			if (gridSlots[i] == null && unslottedIdx < unslotted.Count)
			{
				gridSlots[i] = unslotted[unslottedIdx++];
			}
		}

		for (int i = 0; i < maxItems; i++)
		{
			var item = gridSlots[i];
			_displayedGridItems[i] = item;
			if (item != null)
			{
				if (item.Hotkey == Key.None)
				{
					item.Hotkey = gridHotkeys[i];
				}
				FormatItemTooltipHotkey(item);
				_commandGrid.AddChild(CreateButtonForItem(item));
			}
			else
			{
				_commandGrid.AddChild(CreateBlackTile());
			}
		}
	}

	public bool HandleHotkey(Key keycode)
	{
		if (_activeItems.Count == 0) return false;

		GetHotkeyCheckRange(out int startIndex, out int itemsToCheck);

		for (int i = 0; i < itemsToCheck; i++)
		{
			int itemIndex = startIndex + i;
			if (itemIndex >= _activeItems.Count) break;

			var item = _activeItems[itemIndex];
			if (item.Hotkey == keycode && !(item.IsDisabled?.Invoke() ?? false))
			{
				item.Callback?.Invoke();
				return true;
			}
		}

		return false;
	}

	private void GetHotkeyCheckRange(out int startIndex, out int itemsToCheck)
	{
		int totalItems = _activeItems.Count;
		startIndex = 0;
		itemsToCheck = totalItems;

		if (totalItems > 12)
		{
			int numPages = (totalItems + 10) / 11;
			if (_pageIndex >= numPages) _pageIndex = 0;
			startIndex = _pageIndex * 11;
			itemsToCheck = 11;
		}
	}

	private Control CreateBlackTile()
	{
		var tile = new Panel();
		tile.Name = "Tile_Panel";
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.04f, 0.04f, 0.06f, 0.35f);
		style.BorderColor = new Color(UIStyle.ColorBronze.R, UIStyle.ColorBronze.G, UIStyle.ColorBronze.B, 0.2f);
		style.SetBorderWidthAll(1);
		style.CornerRadiusTopLeft = 3;
		style.CornerRadiusTopRight = 3;
		style.CornerRadiusBottomLeft = 3;
		style.CornerRadiusBottomRight = 3;
		tile.AddThemeStyleboxOverride("panel", style);
		tile.CustomMinimumSize = new Vector2(56, 56);
		tile.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		tile.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		return tile;
	}

	private Button CreateCycleButton(int numPages)
	{
		var btn = new Button();
		btn.Name = "Btn_Button";
		btn.Flat = false;
		btn.Text = "";
		btn.ExpandIcon = true;
		btn.Icon = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/command_card_next_arrow.webp");
		btn.TooltipText = TranslationServer.Translate("Cycle Abilities / Commands");
		btn.CustomMinimumSize = new Vector2(56, 56);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		btn.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		btn.FocusMode = Control.FocusModeEnum.None;
		btn.AddThemeConstantOverride("icon_max_width", 50);

		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateHUDButtonStyle(false, false));
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateHUDButtonStyle(true, false));
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateHUDButtonStyle(false, true));
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		var label = new Label();
		label.Name = "Label_Label";
		label.Text = TranslationServer.Translate("CYCLE");
		label.AddThemeFontSizeOverride("font_size", 10);
		label.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
		label.AddThemeConstantOverride("outline_size", 4);
		label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
		label.OffsetBottom = -3;
		label.GrowHorizontal = Control.GrowDirection.Both;
		label.HorizontalAlignment = HorizontalAlignment.Center;
		btn.AddChild(label);

		btn.Pressed += () =>
		{
			_pageIndex = (_pageIndex + 1) % numPages;
			if (GameHost.Instance != null)
			{
				InGameHUD.Instance?.RefreshUI(GameHost.Instance.SelectedUnits);
			}
		};

		return btn;
	}

	private Texture2D LoadCommandIcon(string iconPath)
	{
		if (string.IsNullOrEmpty(iconPath)) return null;
		var tex = RtexIconLoader.Load(iconPath);
		if (tex == null)
		{
			GD.PushWarning($"[CommandPanel] Failed to load ability icon at '{iconPath}', using fallback.");
			tex = RtexIconLoader.Load("res://Assets/UI/alliance_flag.webp");
		}

		return tex;
	}

	private Button CreateButtonForItem(CommandCardItem item)
	{
		var btn = new CommandCardButton();
		SetupBaseButtonProperties(btn, item);
		AddHotkeyLabelIfNeeded(btn, item);
		SetButtonStyles(btn);

		if (!string.IsNullOrEmpty(item.AbilityId))
		{
			AddAbilityUIElements(btn, item);
			UpdateAbilityButtonVisuals(btn, item);
		}
		else
		{
			ApplyStandardButtonState(btn, item);
		}

		btn.Pressed += () => item.Callback?.Invoke();
		return btn;
	}

	private void SetupBaseButtonProperties(Button btn, CommandCardItem item)
	{
		btn.Flat = false;
		btn.Text = item.GetButtonText?.Invoke() ?? "";
		btn.ExpandIcon = true;
		btn.Icon = !string.IsNullOrEmpty(item.IconPath) ? LoadCommandIcon(item.IconPath) : null;

		string transTooltip = TranslationServer.Translate(item.Tooltip);
		btn.TooltipText = string.IsNullOrEmpty(transTooltip) ? item.Tooltip : transTooltip;

		btn.CustomMinimumSize = new Vector2(56, 56);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		btn.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		btn.FocusMode = Control.FocusModeEnum.None;
		btn.ClipContents = true;
		btn.AddThemeConstantOverride("icon_max_width", 50);
	}

	private void AddHotkeyLabelIfNeeded(Button btn, CommandCardItem item)
	{
		if (item.Hotkey == Key.None) return;

		var hotkeyLabel = new Label
		{
			Name = "HotkeyLabel",
			Text = item.Hotkey.ToString(),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		hotkeyLabel.AddThemeFontSizeOverride("font_size", 12);
		hotkeyLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		hotkeyLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
		hotkeyLabel.AddThemeConstantOverride("outline_size", 4);
		hotkeyLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
		hotkeyLabel.OffsetLeft = 4;
		hotkeyLabel.OffsetTop = 3;

		btn.AddChild(hotkeyLabel);
	}

	private void SetButtonStyles(Button btn)
	{
		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateHUDButtonStyle(false, false));
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateHUDButtonStyle(true, false));
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateHUDButtonStyle(false, true));
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
	}

	private void AddAbilityUIElements(Button btn, CommandCardItem item)
	{
		var sweep = new TextureProgressBar
		{
			Name = "CooldownSweep",
			FillMode = (int)TextureProgressBar.FillModeEnum.CounterClockwise,
			NinePatchStretch = true,
			TextureProgress = GetOrCreateWhiteTexture(),
			TintProgress = new Color(0f, 0f, 0f, 0.65f),
			MinValue = 0.0,
			MaxValue = 100.0,
			Value = 0.0,
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		sweep.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		btn.AddChild(sweep);

		var cdLabel = new Label
		{
			Name = "CooldownLabel",
			Text = "",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		cdLabel.AddThemeFontSizeOverride("font_size", 14);
		cdLabel.AddThemeColorOverride("font_color", Colors.White);
		cdLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.95f));
		cdLabel.AddThemeConstantOverride("outline_size", 4);
		cdLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		btn.AddChild(cdLabel);

		if (item.ManaCost > 0f)
		{
			var manaLabel = new Label
			{
				Name = "ManaCostLabel",
				Text = GetCachedIntegerString((int)item.ManaCost),
				GrowHorizontal = Control.GrowDirection.Begin,
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			manaLabel.AddThemeFontSizeOverride("font_size", 10);
			manaLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.75f, 1.0f));
			manaLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
			manaLabel.AddThemeConstantOverride("outline_size", 4);
			manaLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
			manaLabel.OffsetRight = -4;
			manaLabel.OffsetTop = 3;
			btn.AddChild(manaLabel);
		}
	}

	private void ApplyStandardButtonState(Button btn, CommandCardItem item)
	{
		bool disabled = item.IsDisabled?.Invoke() ?? false;
		btn.Disabled = disabled;
		btn.Modulate = disabled ? new Color(0.5f, 0.5f, 0.5f, 0.7f) : Colors.White;
	}

	private List<CommandCardItem> GetCommandCardItems(SelectedUnitInfo focusedUnit, bool isBuildSubMenuOpen)
	{
		var items = new List<CommandCardItem>();
		if (focusedUnit == null) return items;

		if (focusedUnit.IsBuilding && focusedUnit.IsUnderConstruction)
		{
			items.Add(CreateCancelConstructionItem());
			return items;
		}

		bool hasMetadata = GameHost.TryGetUnitOrBuildingMetadata(focusedUnit.UnitId, out var meta);

		if (!focusedUnit.IsBuilding)
		{
			PopulateUnitItems(items, focusedUnit, meta, hasMetadata, isBuildSubMenuOpen);
		}
		else
		{
			PopulateBuildingItems(items, focusedUnit, meta, hasMetadata);
		}

		items.RemoveAll(i => i == null);
		return items;
	}

	private CommandCardItem CreateCancelConstructionItem()
	{
		return new CommandCardItem
		{
			Id = "stop",
			IconPath = "res://Assets/UI/cancel_button_2.webp",
			Tooltip = "[S] Cancel Construction",
			Hotkey = Key.S,
			Callback = () => { }
		};
	}

	private void PopulateUnitItems(List<CommandCardItem> items, SelectedUnitInfo focusedUnit, UnitMetadata meta, bool hasMetadata, bool isBuildSubMenuOpen)
	{
		if (isBuildSubMenuOpen)
		{
			PopulateBuildSubMenuItems(items, focusedUnit, meta, hasMetadata);
			return;
		}

		bool isStationary = focusedUnit.Speed <= 0.001f;
		if (isStationary)
		{
			PopulateStationaryUnitItems(items, focusedUnit);

			if (hasMetadata && meta.BuildOptions?.Length > 0)
			{
				items.Add(new CommandCardItem
				{
					Id = "build",
					IconPath = "res://Assets/UI/golden_hammers.webp",
					Tooltip = "[B] Build Structure",
					Hotkey = Key.B,
					Callback = () => InGameHUD.Instance?.EnterBuildSubMenu()
				});
			}
		}
		else
		{
			PopulateMobileUnitItems(items, focusedUnit, hasMetadata, meta);
		}

		if (hasMetadata)
		{
			PopulateShopItems(items, meta, focusedUnit);
			PopulateGlobalUpgrades(items, meta, focusedUnit);
		}
	}

	private void PopulateBuildSubMenuItems(List<CommandCardItem> items, SelectedUnitInfo focusedUnit, UnitMetadata meta, bool hasMetadata)
	{
		string[] options = hasMetadata && meta.BuildOptions != null ? meta.BuildOptions : Array.Empty<string>();
		foreach (var opt in options)
		{
			var item = CreateBuildOptionItem(opt, focusedUnit.Entity);
			if (item != null)
			{
				items.Add(item);
			}
		}

		items.Add(new CommandCardItem
		{
			Id = "cancel_build",
			IconPath = "res://Assets/UI/cancel_button_2.webp",
			Tooltip = "[Esc] Cancel",
			Hotkey = Key.Escape,
			Callback = () => InGameHUD.Instance?.ExitBuildSubMenu()
		});
	}

	private void PopulateStationaryUnitItems(List<CommandCardItem> items, SelectedUnitInfo focusedUnit)
	{
		var perkAbilities = new List<string>();
		var otherAbilities = new List<string>();

		foreach (var ab in focusedUnit.Abilities)
		{
			if (ab.StartsWith("perk_")) perkAbilities.Add(ab);
			else otherAbilities.Add(ab);
		}

		if (perkAbilities.Count > 0)
		{
			foreach (var ab in perkAbilities) items.Add(CreateAbilityItem(ab, focusedUnit.Entity));
			AddStandardCombatCommands(items, focusedUnit.Entity);
			foreach (var ab in otherAbilities) items.Add(CreateAbilityItem(ab, focusedUnit.Entity));
		}
		else
		{
			foreach (var ab in focusedUnit.Abilities) items.Add(CreateAbilityItem(ab, focusedUnit.Entity));
			AddStandardCombatCommands(items, focusedUnit.Entity);
		}
	}

	private void PopulateMobileUnitItems(List<CommandCardItem> items, SelectedUnitInfo focusedUnit, bool hasMetadata, UnitMetadata meta)
	{
		if (!IsCommandsFrozen(focusedUnit.Entity))
		{
			items.Add(new CommandCardItem
			{
				Id = "move",
				IconPath = "res://Assets/UI/move_speed.webp",
				Tooltip = "[M] Move / Right-Click Ground",
				Hotkey = Key.M,
				Callback = () => GameHost.Instance?.EnterCommandTargeting("move")
			});

			items.Add(CreateStopCommand());
			items.Add(CreateHoldCommand());

			items.Add(new CommandCardItem
			{
				Id = "attack",
				IconPath = "res://Assets/UI/battle_axe.webp",
				Tooltip = "[A] Attack / Attack-Move — Click enemy to attack, click ground to attack-move",
				Hotkey = Key.A,
				Callback = () => GameHost.Instance?.EnterCommandTargeting("attack")
			});

			items.Add(new CommandCardItem
			{
				Id = "patrol",
				IconPath = "res://Assets/UI/patrol.webp",
				Tooltip = "[P] Patrol — Unit patrols between current position and target, engaging enemies",
				Hotkey = Key.P,
				Callback = () => GameHost.Instance?.EnterCommandTargeting("patrol")
			});
		}

		if (hasMetadata && meta.BuildOptions?.Length > 0)
		{
			items.Add(new CommandCardItem
			{
				Id = "build",
				IconPath = "res://Assets/UI/golden_hammers.webp",
				Tooltip = "[B] Build Structure",
				Hotkey = Key.B,
				Callback = () => InGameHUD.Instance?.EnterBuildSubMenu()
			});
		}

		foreach (var ab in focusedUnit.Abilities)
		{
			items.Add(CreateAbilityItem(ab, focusedUnit.Entity));
		}
	}

	private void AddStandardCombatCommands(List<CommandCardItem> items, Entity entity)
	{
		if (IsCommandsFrozen(entity))
			return;

		items.Add(new CommandCardItem
		{
			Id = "attack",
			IconPath = "res://Assets/UI/battle_axe.webp",
			Tooltip = "[A] Attack / Attack-Move — Click enemy to attack, click ground to attack-move",
			Hotkey = Key.A,
			Callback = () => GameHost.Instance?.EnterCommandTargeting("attack")
		});

		items.Add(CreateStopCommand());
	}

	private CommandCardItem CreateStopCommand()
	{
		return new CommandCardItem
		{
			Id = "stop",
			IconPath = "res://Assets/UI/cancel_button_2.webp",
			Tooltip = "[S] Stop Selected Units",
			Hotkey = Key.S,
			Callback = () =>
			{
				InGameHUD.Instance?.ShowFeedbackText(TranslationServer.Translate("Command: Stop Current Action"), new Color(0.9f, 0.2f, 0.2f));
				GameHost.Instance?.StopSelectedUnits();
			}
		};
	}

	private CommandCardItem CreateHoldCommand()
	{
		return new CommandCardItem
		{
			Id = "hold",
			IconPath = "res://Assets/UI/magic_upgrade_arrow.webp",
			Tooltip = "[H] Hold Position — Unit stays put and attacks in place",
			Hotkey = Key.H,
			Callback = () =>
			{
				InGameHUD.Instance?.ShowFeedbackText(TranslationServer.Translate("Command: Hold Position"), new Color(0.9f, 0.8f, 0.1f));
				GameHost.Instance?.HoldSelectedUnits();
			}
		};
	}

	private void PopulateBuildingItems(List<CommandCardItem> items, SelectedUnitInfo focusedUnit, UnitMetadata meta, bool hasMetadata)
	{
		if (hasMetadata && meta.BuildOptions != null)
		{
			foreach (var opt in meta.BuildOptions) items.Add(CreateTrainOptionItem(opt));
		}

		if (meta != null && meta.BuildOptions != null && meta.BuildOptions.Length > 0)
		{
			items.Add(new CommandCardItem
			{
				Id = "set_rally",
				IconPath = "res://Assets/UI/alliance_flag.webp",
				Tooltip = "[Y] Set Rally Point — Set location where new units will walk",
				Hotkey = Key.Y,
				Callback = () => GameHost.Instance?.EnterCommandTargeting("rally")
			});
		}

		if (hasMetadata)
		{
			PopulateShopItems(items, meta, focusedUnit);
			PopulateGlobalUpgrades(items, meta, focusedUnit);
		}

		foreach (var ab in focusedUnit.Abilities)
		{
			items.Add(CreateAbilityItem(ab, focusedUnit.Entity));
		}
	}

	private void PopulateShopItems(List<CommandCardItem> items, UnitMetadata? meta, SelectedUnitInfo focusedUnit)
	{
		if (meta?.ShopItems == null || meta.ShopItems.Length == 0) return;

		foreach (var itemId in meta.ShopItems)
		{
			if (string.IsNullOrEmpty(itemId)) continue;

			ItemMetadata? itemMeta = null;
			if (!GameHost.ItemRegistry.TryGetValue(itemId, out itemMeta))
			{
				if (itemId.StartsWith("item/", StringComparison.OrdinalIgnoreCase))
				{
					GameHost.ItemRegistry.TryGetValue(itemId.Substring(5), out itemMeta);
				}
				else
				{
					GameHost.ItemRegistry.TryGetValue("item/" + itemId, out itemMeta);
				}
			}

			string itemName = itemMeta != null && !string.IsNullOrEmpty(itemMeta.Name) ? itemMeta.Name : itemId;
			string itemDesc = itemMeta != null && !string.IsNullOrEmpty(itemMeta.Description) ? itemMeta.Description : $"Buy {itemName} for a nearby combat unit";
			float costGold = itemMeta?.CostGold ?? 0f;
			string iconPath = itemMeta != null && !string.IsNullOrEmpty(itemMeta.IconPath) ? itemMeta.IconPath : "res://Assets/UI/alliance_flag.webp";

			string costStr = costGold > 0f ? $" (Cost: {costGold:F0} Gold)" : "";
			string tooltip = $"Buy {itemName}{costStr} — {itemDesc}";

			items.Add(new CommandCardItem
			{
				Id = "buy_" + itemId,
				IconPath = iconPath,
				Tooltip = tooltip,
				Hotkey = Key.None,
				Callback = () =>
				{
					var sellerEntity = focusedUnit != null && focusedUnit.Entity != Entity.Null
						? focusedUnit.Entity
						: (GameHost.Instance?.SelectedUnits.Count == 1 ? GameHost.Instance.SelectedUnits[0].Entity : Entity.Null);

					if (sellerEntity != Entity.Null)
					{
						GameHost.Instance?.BuyItem(itemId, sellerEntity);
					}
				}
			});
		}
	}

	private void PopulateGlobalUpgrades(List<CommandCardItem> items, UnitMetadata? meta, SelectedUnitInfo focusedUnit)
	{
		if (meta?.Upgrades == null || meta.Upgrades.Length == 0) return;

		int localViewingIndex = GameContext.PerspectiveFilter.ControlledPlayerIndex;

		foreach (var upgradeId in meta.Upgrades)
		{
			if (string.IsNullOrEmpty(upgradeId)) continue;

			UpgradeMetadata? upgMeta = null;
			if (!GameHost.UpgradeRegistry.TryGetValue(upgradeId, out upgMeta))
			{
				if (upgradeId.StartsWith("upgrade/", StringComparison.OrdinalIgnoreCase))
				{
					GameHost.UpgradeRegistry.TryGetValue(upgradeId.Substring(8), out upgMeta);
				}
				else
				{
					GameHost.UpgradeRegistry.TryGetValue("upgrade/" + upgradeId, out upgMeta);
				}
			}

			string name = upgMeta != null && !string.IsNullOrEmpty(upgMeta.Name) ? upgMeta.Name : upgradeId;
			string desc = upgMeta?.Description ?? "";
			float gold = upgMeta?.CostGold ?? 0f;
			float wood = upgMeta?.CostWood ?? 0f;
			float stone = upgMeta?.CostStone ?? 0f;
			int maxLevel = upgMeta != null && upgMeta.MaxLevel > 0 ? upgMeta.MaxLevel : 1;
			string iconPath = upgMeta != null && !string.IsNullOrEmpty(upgMeta.IconPath) ? upgMeta.IconPath : "res://Assets/UI/magic_upgrade_arrow.webp";

			var costParts = new List<string>();
			if (gold > 0f) costParts.Add($"{gold:F0} Gold");
			if (wood > 0f) costParts.Add($"{wood:F0} Wood");
			if (stone > 0f) costParts.Add($"{stone:F0} Stone");
			string costStr = costParts.Count > 0 ? $" (Cost: {string.Join(", ", costParts)})" : "";

			Key hotkey = Key.None;
			if (upgMeta != null && !string.IsNullOrEmpty(upgMeta.Hotkey))
			{
				hotkey = ParseHotkey(upgMeta.Hotkey);
			}

			string hotkeyPrefix = hotkey != Key.None ? $"[{hotkey}] " : "";
			string tooltip = !string.IsNullOrEmpty(desc)
				? $"{hotkeyPrefix}Upgrade {name}{costStr}\n{desc}"
				: $"{hotkeyPrefix}Upgrade {name}{costStr}";

			string canonicalId = upgMeta != null && !string.IsNullOrEmpty(upgMeta.TemplateID) ? upgMeta.TemplateID : upgradeId;

			Func<bool> isMaxed = () =>
			{
				int lvl = GameHost.Instance?.GetPlayerTechLevel(localViewingIndex, canonicalId) ?? 0;
				if (lvl == 0 && canonicalId != upgradeId)
				{
					lvl = GameHost.Instance?.GetPlayerTechLevel(localViewingIndex, upgradeId) ?? 0;
				}
				return lvl >= maxLevel;
			};

			Func<string> getButtonText = () =>
			{
				if (isMaxed()) return TranslationServer.Translate("MAXED");
				int lvl = GameHost.Instance?.GetPlayerTechLevel(localViewingIndex, canonicalId) ?? 0;
				if (lvl == 0 && canonicalId != upgradeId)
				{
					lvl = GameHost.Instance?.GetPlayerTechLevel(localViewingIndex, upgradeId) ?? 0;
				}
				if (lvl > 0 && maxLevel > 1) return $"Lvl {lvl}";
				return "";
			};

			Func<bool> cannotAfford = () =>
			{
				var hud = InGameHUD.Instance;
				if (hud == null) return false;
				return hud.Gold < gold || hud.Wood < wood || hud.Stone < stone;
			};

			items.Add(new CommandCardItem
			{
				Id = "upgrade_" + upgradeId,
				IconPath = iconPath,
				Tooltip = tooltip,
				Hotkey = hotkey,
				Callback = () => GameHost.Instance?.BuyUpgrade(upgradeId, localViewingIndex),
				IsDisabled = () => isMaxed() || cannotAfford(),
				GetButtonText = getButtonText
			});
		}
	}

	private CommandCardItem? CreateBuildOptionItem(string unitId, Entity builderEntity = default)
	{
		var host = GameHost.Instance;
		var unitWrapper = GetUnitWrapperIfAlive(host, builderEntity);
		if (IsAbilityHidden(host, unitWrapper, unitId)) return null;

		var hotkey = Key.None;

		var abilityDef = host?.GetAbilityDefinition(unitId);
		if (abilityDef != null && !string.IsNullOrEmpty(abilityDef.Hotkey))
		{
			hotkey = ParseHotkey(abilityDef.Hotkey);
		}

		string finalTooltip;
		if (abilityDef != null && !string.IsNullOrEmpty(abilityDef.Tooltip))
		{
			finalTooltip = abilityDef.Tooltip;
		}
		else
		{
				finalTooltip = UIHelper.GetUnitCostTooltip(unitId, hotkey, "Build");
		}

		string iconPath = abilityDef != null && !string.IsNullOrEmpty(abilityDef.IconPath)
			? abilityDef.IconPath
			: GetUnitIcon(unitId);

		return new CommandCardItem
		{
			Id = "build_" + unitId,
			IconPath = iconPath,
			Tooltip = finalTooltip,
			Hotkey = hotkey,
			GridX = abilityDef?.GridX ?? -1,
			GridY = abilityDef?.GridY ?? -1,
			Callback = () => GameHost.Instance?.EnterBuildingPlacement(unitId),
			IsDisabled = () => host != null && GameContext.RegistryService.IsAbilityDisabledForUnit(unitWrapper, unitId)
		};
	}

	private CommandCardItem CreateTrainOptionItem(string unitId)
	{
		var hotkey = Key.None;

			string finalTooltip = UIHelper.GetUnitCostTooltip(unitId, hotkey, "Train");

		return new CommandCardItem
		{
			Id = "train_" + unitId,
			IconPath = GetUnitIcon(unitId),
			Tooltip = finalTooltip,
			Hotkey = hotkey,
			Callback = () => GameHost.Instance?.TrainUnitAtBuilding(unitId)
		};
	}

	private string GetDefaultAbilityIcon(string abilityId)
	{
		return "res://Assets/UI/alliance_flag.webp";
	}

	private string GetDefaultAbilityTooltip(string abilityId)
	{
		return string.Format(TranslationServer.Translate("Cast {0}"), abilityId.ToUpper());
	}

	private CommandCardItem? CreateAbilityItem(string abilityId, Entity casterEntity)
	{
		var host = GameHost.Instance;
		var unitWrapper = GetUnitWrapperIfAlive(host, casterEntity);

		if (IsAbilityHidden(host, unitWrapper, abilityId)) return null;

		var abilityDef = host?.GetAbilityDefinition(abilityId);
		float manaCost = GetAbilityManaCost(host, unitWrapper, abilityDef, abilityId);

		return new CommandCardItem
		{
			Id = abilityId,
			AbilityId = abilityId,
			CasterEntity = casterEntity,
			ManaCost = manaCost,
			IconPath = ResolveAbilityIcon(abilityId, abilityDef),
			Tooltip = ResolveAbilityTooltip(abilityId, abilityDef),
			Hotkey = ParseHotkey(abilityDef?.Hotkey),
			GridX = abilityDef?.GridX ?? -1,
			GridY = abilityDef?.GridY ?? -1,
			Callback = GetAbilityCallback(abilityId, abilityDef?.IsInstant ?? false),
			IsDisabled = () => IsAbilityDisabled(host, unitWrapper, casterEntity, abilityId, manaCost)
		};
	}

	private bool IsAbilityHidden(GameHost host, Unit_WasmRuntime unitWrapper, string abilityId)
	{
		if (host == null) return false;
		return host.IsAbilityHiddenForUnit(unitWrapper, abilityId);
	}

	private string ResolveAbilityIcon(string abilityId, AbilityDefinition abilityDef)
	{
		if (abilityDef != null && !string.IsNullOrEmpty(abilityDef.IconPath))
		{
			return abilityDef.IconPath;
		}

		return GetDefaultAbilityIcon(abilityId);
	}

	private string ResolveAbilityTooltip(string abilityId, AbilityDefinition abilityDef)
	{
		if (abilityDef != null && !string.IsNullOrEmpty(abilityDef.Tooltip))
		{
			return abilityDef.Tooltip;
		}

		return GetDefaultAbilityTooltip(abilityId);
	}

	private Unit_WasmRuntime GetUnitWrapperIfAlive(GameHost host, Entity casterEntity)
	{
		if (host == null || casterEntity == Entity.Null) return null;
		return GameContext.World.IsAlive(casterEntity) ? GameContext.SimulationService.GetUnitWrapper(casterEntity) : null;
	}

	private float GetAbilityManaCost(GameHost host, Unit_WasmRuntime unitWrapper, AbilityDefinition abilityDef, string abilityId)
	{
		return host != null ? host.GetAbilityManaCostForUnit(unitWrapper, abilityId) : (abilityDef?.ManaCost ?? 0f);
	}

	private Key ParseHotkey(string hotkeyString)
	{
		if (!string.IsNullOrEmpty(hotkeyString) && Enum.TryParse<Key>(hotkeyString, true, out var parsedKey))
		{
			return parsedKey;
		}

		return Key.None;
	}

	private Action GetAbilityCallback(string abilityId, bool isInstant)
	{
		if (isInstant) return () => GameHost.Instance?.CastInstantAbility(abilityId);
		return () => GameHost.Instance?.EnterSpellTargeting(abilityId);
	}

	private bool IsAbilityDisabled(GameHost host, Unit_WasmRuntime unitWrapper, Entity casterEntity, string abilityId, float manaCost)
	{
		if (host == null) return false;
		if (GameContext.RegistryService.IsAbilityDisabledForUnit(unitWrapper, abilityId)) return true;

		int casterPlayer = GameContext.World != null && GameContext.World.IsAlive(casterEntity) && GameContext.World.Has<UnitOwnerPlayer>(casterEntity)
			? GameContext.World.Get<UnitOwnerPlayer>(casterEntity).PlayerIndex
			: GameContext.PerspectiveFilter.ControlledPlayerIndex;
		if (host.GetPlayerSpellCooldown(casterPlayer, abilityId) > 0f) return true;

		var world = GameContext.World;
		if (world == null || casterEntity == Entity.Null || !world.IsAlive(casterEntity)) return false;

		return IsAbilityOnCooldownOrOOM(world, casterEntity, abilityId, manaCost);
	}

	private bool IsAbilityOnCooldownOrOOM(EcsWorld world, Entity casterEntity, string abilityId, float manaCost)
	{
		if (world.Has<Mana>(casterEntity))
		{
			if (world.Get<Mana>(casterEntity).Current < manaCost) return true;
		}

		if (world.Has<Cooldowns>(casterEntity))
		{
			ref var cooldowns = ref world.Get<Cooldowns>(casterEntity);
			if (cooldowns.TryGetValue(abilityId, out float cooldown) && cooldown > 0f) return true;
		}

		if (world.Has<SpellCooldowns>(casterEntity))
		{
			ref var spellCooldowns = ref world.Get<SpellCooldowns>(casterEntity);
			if (spellCooldowns.TryGetValue(abilityId, out float spellCooldown) && spellCooldown > 0f) return true;
		}

		return false;
	}

	private void ApplyUpgradeButtonState(Button btn, bool isMaxed, string maxedLabel)
	{
		if (isMaxed)
		{
			btn.Disabled = true;
			btn.TooltipText = $"✓ {maxedLabel} — Already researched!";
			btn.Modulate = new Color(0.5f, 0.5f, 0.5f, 0.7f);
		}
		else
		{
			btn.Disabled = false;
			btn.Modulate = Colors.White;
		}
	}

	private string GetUnitIcon(string unitId)
	{
		return "res://Assets/UI/unit_placeholder.webp";
	}

	private void SetupHUDButton(Button btn, string iconPath, string tooltip, Action onClick)
	{
		btn.Flat = false;
		btn.Text = "";
		btn.ExpandIcon = true;
		btn.Icon = RtexIconLoader.Load(iconPath);
		btn.TooltipText = tooltip;
		btn.CustomMinimumSize = new Vector2(48, 48);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		btn.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		btn.FocusMode = Control.FocusModeEnum.None;
		UIStyle.SetupHUDButton(btn, tooltip);

		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateHUDButtonStyle(false, false));
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateHUDButtonStyle(true, false));
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateHUDButtonStyle(false, true));
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		btn.Pressed += () => onClick?.Invoke();
	}

	public static void ClearCache()
	{
		s_whiteTexture = null;
	}
}
