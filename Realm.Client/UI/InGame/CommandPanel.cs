using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Arch.Core;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Services;

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

	[GeneratedRegex(@"^\[.*?\] ")]
	private static partial Regex HotkeyPrefixRegex();


	private int _pageIndex = 0;
	private List<CommandCardItem> _activeItems = new();
	private Entity _lastFocusedEntity = Entity.Null;
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

		foreach (Node child in _commandGrid.GetChildren())
		{
			child.QueueFree();
		}
	}

	private CommandCardItem GetActiveItemForIndex(int index, int totalItems, int pageOffset)
	{
		if (totalItems <= 12)
		{
			return index < totalItems ? _activeItems[index] : null;
		}

		if (index < 11)
		{
			int itemIndex = pageOffset + index;
			return itemIndex < totalItems ? _activeItems[itemIndex] : null;
		}

		return null;
	}

	private void UpdateExistingButtons()
	{
		var children = _commandGrid.GetChildren();
		int childCount = children.Count;
		int totalItems = _activeItems.Count;
		int pageOffset = _pageIndex * 11;

		for (int i = 0; i < 12; i++)
		{
			if (i >= childCount) break;

			if (children[i] is not Button btn) continue;

			var item = GetActiveItemForIndex(i, totalItems, pageOffset);
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

	private void GetCasterState(Entity caster, World world, string abilityId, ref float currentMana, ref float cdRemaining)
	{
		if (caster == Entity.Null || !world.IsAlive(caster)) return;

		if (world.Has<Mana>(caster))
		{
			currentMana = world.Get<Mana>(caster).Current;
		}

		if (world.Has<Cooldowns>(caster))
		{
			var cds = world.Get<Cooldowns>(caster).Value;
			if (cds.TryGetValue(abilityId, out float val))
			{
				cdRemaining = val;
			}
		}

		if (world.Has<SpellCooldowns>(caster))
		{
			var scd = world.Get<SpellCooldowns>(caster).Value;
			if (scd != null && scd.TryGetValue(abilityId, out float val))
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
			cdRemaining = GameHost.Instance.GetPlayerSpellCooldown(GameHost.Instance.LocalViewingPlayerIndex, item.AbilityId);
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
		_pageIndex = 0;
		_activeItems.Clear();
		_lastIsBuildSubMenuOpen = false;
	}

	public void Update(InGameHUDViewModel viewModel)
	{
		if (_commandGrid == null) return;
		if (GameHost.Instance?.IsClientSpectator == true)
		{
			ClearCommandPanelState();
			return;
		}

		int localPlayerIndex = GameHost.Instance?.LocalViewingPlayerIndex ?? 1;
		if (viewModel.SelectedUnits.Count == 0 || GetFocusedUnit(viewModel, out var focusedUnit) || focusedUnit.IsEnemy || focusedUnit.Player != localPlayerIndex)
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
		bool focusedUnitChanged = focusedUnit.Entity != _lastFocusedEntity;
		bool subMenuChanged = subMenuOpen != _lastIsBuildSubMenuOpen;

		if (focusedUnitChanged)
		{
			_pageIndex = 0;
			_lastFocusedEntity = focusedUnit.Entity;
		}

		_lastIsBuildSubMenuOpen = subMenuOpen;
		var prevItems = _activeItems;
		_activeItems = GetCommandCardItems(focusedUnit, subMenuOpen);

		bool itemsChanged = prevItems.Count != _activeItems.Count;
		if (!itemsChanged)
		{
			for (int i = 0; i < _activeItems.Count; i++)
			{
				if (prevItems[i].Id != _activeItems[i].Id)
				{
					itemsChanged = true;
					break;
				}
			}
		}

		UpdateItemHotkeys();

		bool pageChanged = _pageIndex != _lastPageIndex;
		_lastPageIndex = _pageIndex;

		if (focusedUnitChanged || subMenuChanged || pageChanged || itemsChanged || _commandGrid.GetChildCount() == 0)
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
				_activeItems[i].Tooltip = HotkeyPrefixRegex().Replace(_activeItems[i].Tooltip, $"[{_activeItems[i].Hotkey}] ");
			}
		}
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
			if (item != null)
			{
				if (item.Hotkey == Key.None)
				{
					item.Hotkey = gridHotkeys[i];
				}
				item.Tooltip = HotkeyPrefixRegex().Replace(item.Tooltip, $"[{item.Hotkey}] ");
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
		btn.Flat = false;
		btn.Text = "";
		btn.ExpandIcon = true;
		btn.Icon = GD.Load<Texture2D>("res://Assets/UI/command_card_next_arrow.png");
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
			tex = RtexIconLoader.Load("res://Assets/UI/alliance_flag.png");
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

		bool hasMetadata = GameHost.UnitRegistry.TryGetValue(focusedUnit.UnitId, out var meta);

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
			IconPath = "res://Assets/UI/cancel_button_2.png",
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
					IconPath = "res://Assets/UI/golden_hammers.png",
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
			IconPath = "res://Assets/UI/cancel_button_2.png",
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
			AddStandardCombatCommands(items);
			foreach (var ab in otherAbilities) items.Add(CreateAbilityItem(ab, focusedUnit.Entity));
		}
		else
		{
			foreach (var ab in focusedUnit.Abilities) items.Add(CreateAbilityItem(ab, focusedUnit.Entity));
			AddStandardCombatCommands(items);
		}
	}

	private void PopulateMobileUnitItems(List<CommandCardItem> items, SelectedUnitInfo focusedUnit, bool hasMetadata, UnitMetadata meta)
	{
		items.Add(new CommandCardItem
		{
			Id = "move",
			IconPath = "res://Assets/UI/move_speed.png",
			Tooltip = "[M] Move / Right-Click Ground",
			Hotkey = Key.M,
			Callback = () => GameHost.Instance?.EnterCommandTargeting("move")
		});

		items.Add(CreateStopCommand());
		items.Add(CreateHoldCommand());

		items.Add(new CommandCardItem
		{
			Id = "attack",
			IconPath = "res://Assets/UI/battle_axe.png",
			Tooltip = "[A] Attack / Attack-Move — Click enemy to attack, click ground to attack-move",
			Hotkey = Key.A,
			Callback = () => GameHost.Instance?.EnterCommandTargeting("attack")
		});

		items.Add(new CommandCardItem
		{
			Id = "patrol",
			IconPath = "res://Assets/UI/patrol.jpg",
			Tooltip = "[P] Patrol — Unit patrols between current position and target, engaging enemies",
			Hotkey = Key.P,
			Callback = () => GameHost.Instance?.EnterCommandTargeting("patrol")
		});

		if (hasMetadata && meta.BuildOptions?.Length > 0)
		{
			items.Add(new CommandCardItem
			{
				Id = "build",
				IconPath = "res://Assets/UI/golden_hammers.png",
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

	private void AddStandardCombatCommands(List<CommandCardItem> items)
	{
		items.Add(new CommandCardItem
		{
			Id = "attack",
			IconPath = "res://Assets/UI/battle_axe.png",
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
			IconPath = "res://Assets/UI/cancel_button_2.png",
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
			IconPath = "res://Assets/UI/magic_upgrade_arrow.png",
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

		if (focusedUnit.UnitId == "castle")
		{
			items.Add(new CommandCardItem
			{
				Id = "set_rally",
				IconPath = "res://Assets/UI/alliance_flag.png",
				Tooltip = "[Y] Set Rally Point — Set location where new units will walk",
				Hotkey = Key.Y,
				Callback = () => GameHost.Instance?.EnterCommandTargeting("rally")
			});

			PopulateShopItems(items);
			PopulateGlobalUpgrades(items);
		}

		foreach (var ab in focusedUnit.Abilities)
		{
			items.Add(CreateAbilityItem(ab, focusedUnit.Entity));
		}
	}

	private void PopulateShopItems(List<CommandCardItem> items)
	{
		if (GameHost.ItemRegistry.Count == 0) return;

		foreach (var itemMeta in GameHost.ItemRegistry.Values)
		{
			string itemId = itemMeta.TemplateID;
			string itemName = !string.IsNullOrEmpty(itemMeta.Name) ? itemMeta.Name : itemId;
			string itemDesc = !string.IsNullOrEmpty(itemMeta.Description) ? itemMeta.Description : $"Buy {itemName} for a nearby combat unit";

			items.Add(new CommandCardItem
			{
				Id = "buy_" + itemId,
				IconPath = !string.IsNullOrEmpty(itemMeta.IconPath) ? itemMeta.IconPath : "res://Assets/UI/alliance_flag.png",
				Tooltip = $"[I] Buy {itemName} (Cost: {itemMeta.CostGold:F0} Gold) — {itemDesc}",
				Hotkey = Key.None,
				Callback = () =>
				{
					var selected = GameHost.Instance?.SelectedUnits;
					if (selected?.Count == 1) GameHost.Instance.BuyItem(itemId, selected[0].Entity);
				}
			});
		}
	}

	private void PopulateGlobalUpgrades(List<CommandCardItem> items)
	{
		int localViewingIndex = GameHost.Instance?.LocalViewingPlayerIndex ?? 1;
		AddGlobalUpgradeItem(items, "upgrade_weapons", "res://Assets/UI/battle_axe.png",
			"[W] Upgrade Weapons (Cost: 150 Gold, 100 Wood)\nPermanently increases unit damage by +3",
			Key.W, () => GameHost.Instance?.BuyWeaponsUpgrade(localViewingIndex), () => GameHost.Instance?.HasWeaponsUpgrade(localViewingIndex) ?? false);

		AddGlobalUpgradeItem(items, "upgrade_shields", "res://Assets/UI/battle_shield.png",
			"[G] Upgrade Armor (Cost: 150 Gold, 100 Stone)\nPermanently increases unit armor by +2",
			Key.G, () => GameHost.Instance?.BuyShieldsUpgrade(localViewingIndex), () => GameHost.Instance?.HasShieldsUpgrade(localViewingIndex) ?? false);

		AddGlobalUpgradeItem(items, "upgrade_harvesting", "res://Assets/UI/gold_coin.png",
			"[T] Upgrade Harvesting (Cost: 150 Wood, 100 Stone)\nPermanently increases passive resource gathering rates by +50%",
			Key.T, () => GameHost.Instance?.BuyHarvestingUpgrade(localViewingIndex), () => GameHost.Instance?.HasHarvestingUpgrade(localViewingIndex) ?? false);
	}

	private void AddGlobalUpgradeItem(List<CommandCardItem> items, string id, string iconPath, string tooltip, Key hotkey, Action callback, Func<bool> hasUpgrade)
	{
		items.Add(new CommandCardItem
		{
			Id = id,
			IconPath = iconPath,
			Tooltip = tooltip,
			Hotkey = hotkey,
			Callback = callback,
			IsDisabled = hasUpgrade,
			GetButtonText = () => hasUpgrade() ? TranslationServer.Translate("MAXED") : ""
		});
	}

	private CommandCardItem? CreateBuildOptionItem(string unitId, Entity builderEntity = default)
	{
		var host = GameHost.Instance;
		var unitWrapper = GetUnitWrapperIfAlive(host, builderEntity);
		if (IsAbilityHidden(host, unitWrapper, unitId)) return null;

		var hotkey = Key.None;

		string name = unitId.ToUpper();
		float gold = 0, wood = 0, stone = 0;
		string desc = "";
		if (GameHost.TryGetUnitOrBuildingMetadata(unitId, out var structureMeta))
		{
			if (!string.IsNullOrEmpty(structureMeta.Name)) name = structureMeta.Name;
			gold = structureMeta.CostGold;
			wood = structureMeta.CostWood;
			stone = structureMeta.CostStone;
			desc = structureMeta.Description ?? "";
		}

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
		else if (gold == 0 && wood == 0 && stone == 0 && !string.IsNullOrEmpty(desc))
		{
			finalTooltip = hotkey != Key.None
				? $"[{hotkey}] Build {name} — {desc}"
				: $"Build {name} — {desc}";
		}
		else
		{
			finalTooltip = hotkey != Key.None
				? string.Format(TranslationServer.Translate("[{0}] Build {1} (Cost: {2} Gold, {3} Wood, {4} Stone)"), hotkey.ToString(), name, gold, wood, stone)
				: string.Format(TranslationServer.Translate("Build {0} (Cost: {1} Gold, {2} Wood, {3} Stone)"), name, gold, wood, stone);
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

		string name = unitId.ToUpper();
		float gold = 0, wood = 0, stone = 0;
		int pop = 0;
		string desc = "";
		if (GameHost.TryGetUnitOrBuildingMetadata(unitId, out var meta))
		{
			name = meta.Name;
			gold = meta.CostGold;
			wood = meta.CostWood;
			stone = meta.CostStone;
			pop = meta.PopCost;
			desc = meta.Description;
		}

		string costStr = $"Cost: {gold} Gold";
		if (wood > 0) costStr += $", {wood} Wood";
		if (stone > 0) costStr += $", {stone} Stone";
		if (pop > 0) costStr += $", {pop} Pop";

		string finalTooltip = hotkey != Key.None
			? string.Format(TranslationServer.Translate("[{0}] Train {1} ({2}) — {3}"), hotkey.ToString(), name, costStr, desc)
			: string.Format(TranslationServer.Translate("Train {0} ({1}) — {2}"), name, costStr, desc);

		return new CommandCardItem
		{
			Id = "train_" + unitId,
			IconPath = GetUnitIcon(unitId),
			Tooltip = finalTooltip,
			Hotkey = hotkey,
			Callback = () => GameHost.Instance?.TrainUnitAtCastle(unitId)
		};
	}

	private string GetDefaultAbilityIcon(string abilityId)
	{
		return "res://Assets/UI/alliance_flag.png";
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
			: host.LocalViewingPlayerIndex;
		if (host.GetPlayerSpellCooldown(casterPlayer, abilityId) > 0f) return true;

		var world = GameContext.World;
		if (world == null || casterEntity == Entity.Null || !world.IsAlive(casterEntity)) return false;

		return IsAbilityOnCooldownOrOOM(world, casterEntity, abilityId, manaCost);
	}

	private bool IsAbilityOnCooldownOrOOM(World world, Entity casterEntity, string abilityId, float manaCost)
	{
		if (world.Has<Mana>(casterEntity))
		{
			if (world.Get<Mana>(casterEntity).Current < manaCost) return true;
		}

		if (world.Has<Cooldowns>(casterEntity))
		{
			if (world.Get<Cooldowns>(casterEntity).Value.TryGetValue(abilityId, out float cd) && cd > 0f) return true;
		}

		if (world.Has<SpellCooldowns>(casterEntity))
		{
			var scd = world.Get<SpellCooldowns>(casterEntity).Value;
			if (scd?.TryGetValue(abilityId, out float cd) == true && cd > 0f) return true;
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
		return "res://Assets/UI/unit_placeholder.png";
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
		btn.ClipContents = true;
		btn.AddThemeConstantOverride("icon_max_width", 42);

		if (tooltip.StartsWith('[') && tooltip.Contains(']'))
		{
			int end = tooltip.IndexOf(']');
			string hotkeyText = tooltip.Substring(1, end - 1);
			var hotkeyLabel = new Label();
			hotkeyLabel.Name = "HotkeyLabel";
			hotkeyLabel.Text = hotkeyText;
			hotkeyLabel.AddThemeFontSizeOverride("font_size", 12);
			hotkeyLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			hotkeyLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
			hotkeyLabel.AddThemeConstantOverride("outline_size", 4);
			hotkeyLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
			hotkeyLabel.OffsetLeft = 4;
			hotkeyLabel.OffsetTop = 3;
			hotkeyLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
			btn.AddChild(hotkeyLabel);
		}

		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateHUDButtonStyle(false, false));
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateHUDButtonStyle(true, false));
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateHUDButtonStyle(false, true));
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		btn.Pressed += () => onClick?.Invoke();
	}
}