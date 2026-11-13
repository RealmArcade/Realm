using System;
using System.Collections.Generic;
using System.Text;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using Realm.Client.Utils;
using Realm.Shared.Abilities;

namespace Realm.Client.UI.InGame;

public partial class CommandPanel
{
	private GridContainer _commandGrid;
	private List<Button> _dynamicBuildButtons = new();

	public CommandPanel(GridContainer commandGrid)
	{
		_commandGrid = commandGrid;
	}

	private static Texture2D? s_whiteTexture;
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
	private readonly CommandCardItem?[] _displayedGridItems = new CommandCardItem?[12];
	private Entity _lastFocusedEntity = Entity.Null;
	private string? _lastFocusedUnitId;
	private bool _lastIsUnderConstruction = false;
	private bool _lastIsMobile = false;
	private int _lastAbilityCount = -1;
	private int _lastPageIndex = -1;
	private bool _lastIsBuildSubMenuOpen = false;
	private int _lastAbilityStateRevision = int.MinValue;
	private string _lastItemSignature = string.Empty;

	private const string BuildCommandAbilityId = "ability/build";

	private const string AutoCastMarqueeName = "AutoCastMarquee";
	private const string AutoCastMarqueeShaderPath = "res://Assets/shaders/ability_autocast_marquee.gdshader";
	private static readonly StringName AutoCastScrollScaleParameter = new("scroll_scale");
	private static readonly StringName AutoCastEnabledParameter = new("cast_enabled");
	private static Shader? s_autoCastMarqueeShader;

	private static Shader? GetAutoCastMarqueeShader()
	{
		s_autoCastMarqueeShader ??= GD.Load<Shader>(AutoCastMarqueeShaderPath);
		return s_autoCastMarqueeShader;
	}

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
		_lastItemSignature = string.Empty;

		Array.Clear(_displayedGridItems);

		foreach (Node child in _commandGrid.GetChildren())
		{
			_commandGrid.RemoveChild(child);
			child.QueueFree();
		}
	}

	private static bool IsCommandsFrozen(Entity entity)
	{
		if (entity == Entity.Null || GameContext.World == null || !GameContext.World.IsAlive(entity)) return false;

		// A unit the map drives keeps its ability cards so a player can read them, but the order commands are left off
		// its grid entirely: the player cannot issue them, and a card that only refuses is a card that lies.
		return GameContext.World.Has<CommandsFrozen>(entity) || GameContext.World.Has<PlayerOrdersLocked>(entity);
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


	private void UpdateAbilityButtonVisuals(Button btn, CommandCardItem item)
	{
		float cdRemaining = 0f;
		float currentMana = float.MaxValue;

		UpdateAbilityStats(item, ref cdRemaining, ref currentMana);

		var abilityDef = GameContext.Host?.GetAbilityDefinition(item.AbilityId);
		float maxCd = GetMaxCooldown(item, abilityDef, cdRemaining);

		// The card carries the caster's effective cost: the per-unit cost the map set, which is what the tooltip
		// header shows and what the cast gate compares against. The ability definition's cost is shared by every
		// unit, so reading that here replaced a unit's own number with whichever unit the map set last.
		float manaCost = item.ManaCost;

		UpdateAbilityLabelsAndSweep(btn, manaCost, cdRemaining, maxCd);
		UpdateAbilityButtonState(btn, item, currentMana, manaCost, cdRemaining);
		UpdateAutoCastMarquee(btn, item);
	}

	/// <summary>
	///     Draws the frame around an icon whose ability supports auto-cast, and keeps it drawn whether that auto-cast is on
	///     or off: the dashes travel while this caster is casting on its own and hold still while it is not, and the whole
	///     frame is drawn dimmer while it is off, so the switched-on state is read at a glance instead of taken from the
	///     dashes being in motion. The state is the replicated one, so the frame follows what the authority actually does
	///     rather than the last click this screen made.
	/// </summary>
	private static void UpdateAutoCastMarquee(Button btn, CommandCardItem item)
	{
		var marquee = btn.GetNodeOrNull<ColorRect>(AutoCastMarqueeName);
		if (marquee == null) return;

		bool active = item.CasterEntity != Entity.Null
			&& GameContext.SimulationService.IsAbilityAutoCast(new UnitId(item.CasterEntity.Id), item.AbilityId);

		if (!marquee.Visible) marquee.Visible = true;

		if (marquee.Material is not ShaderMaterial material) return;

		float scrollScale = active ? 1f : 0f;
		if (MathF.Abs(material.GetShaderParameter(AutoCastScrollScaleParameter).AsSingle() - scrollScale) < 0.001f) return;

		material.SetShaderParameter(AutoCastScrollScaleParameter, scrollScale);
		material.SetShaderParameter(AutoCastEnabledParameter, scrollScale);
	}

	private float GetMaxCooldown(CommandCardItem item, AbilityDefinition? abilityDef, float cdRemaining)
	{
		if (abilityDef != null && abilityDef.Cooldown > 0f) return abilityDef.Cooldown;

		// A script-set cooldown carries its own duration, which is the only scale available for a command the map
		// timed but never defined as an ability. Guessing one made the sweep sit full until the last ten seconds.
		EcsWorld? world = GameContext.World;
		Entity caster = item.CasterEntity != Entity.Null && world != null && world.IsAlive(item.CasterEntity)
			? item.CasterEntity
			: _lastFocusedEntity;

		if (world != null && caster != Entity.Null && world.IsAlive(caster) && world.Has<Cooldowns>(caster))
		{
			ref Cooldowns cooldowns = ref world.Get<Cooldowns>(caster);
			if (cooldowns.TryGetDuration(item.AbilityId, out float duration) && duration > 0f) return duration;
		}

		return Math.Max(10f, cdRemaining);
	}

	private void UpdateAbilityButtonState(Button btn, CommandCardItem item, float currentMana, float manaCost, float cdRemaining)
	{
		bool notEnoughMana = currentMana < manaCost;
		bool isItemDisabled = item.IsDisabled?.Invoke() ?? false;
		bool disabled = (!item.IgnoresCooldownGate && cdRemaining > 0f) || notEnoughMana || isItemDisabled;

		if (btn.Disabled != disabled) btn.Disabled = disabled;

		Color targetModulate = notEnoughMana ? new Color(0.5f, 0.5f, 0.5f, 0.7f) : Colors.White;
		if (btn.Modulate != targetModulate) btn.Modulate = targetModulate;
	}

	private void UpdateAbilityStats(CommandCardItem item, ref float cdRemaining, ref float currentMana)
	{
		EcsWorld? world = GameContext.Host != null ? GameContext.World : null;
		if (world == null) return;

		var caster = item.CasterEntity != Entity.Null && world.IsAlive(item.CasterEntity) ? item.CasterEntity : _lastFocusedEntity;
		GetCasterState(caster, world, item.AbilityId, ref currentMana, ref cdRemaining);
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

	private static bool IsPointerOver(Control control)
	{
		if (!GodotObject.IsInstanceValid(control) || !control.IsVisibleInTree()) return false;
		if (control.GetViewport() == null) return false;

		return new Rect2(Vector2.Zero, control.Size).HasPoint(control.GetLocalMousePosition());
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
			|| abilityCount != _lastAbilityCount
			|| GameContext.RegistryService.AbilityStateRevision != _lastAbilityStateRevision;

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
			_lastAbilityStateRevision = GameContext.RegistryService.AbilityStateRevision;

			_activeItems = GetCommandCardItems(focusedUnit, subMenuOpen);
			UpdateItemHotkeys();
		}

		bool pageChanged = _pageIndex != _lastPageIndex;
		_lastPageIndex = _pageIndex;

		string itemSignature = ComputeItemSignature();
	bool itemsChanged = itemSignature != _lastItemSignature;

		if (_commandGrid.GetChildCount() == 0 || itemsChanged || pageChanged)
		{
			RebuildCommandGrid();
			_lastItemSignature = itemSignature;
		}
		else
		{
			// The same cards are on screen, so refresh them in place. Recreating them would free the node under the
			// pointer and take its open tooltip with it, which made ability tooltips vanish during a hover.
			UpdateExistingButtons();
		}
	}

	private string ComputeItemSignature()
	{
		var builder = new StringBuilder(64);
		builder.Append(_pageIndex).Append('|');
		for (int i = 0; i < _activeItems.Count; i++)
		{
			CommandCardItem item = _activeItems[i];
			builder.Append(item.Id).Append('@').Append(item.GridX).Append(',').Append(item.GridY).Append(';');
		}

		return builder.ToString();
	}

	private static readonly Key[] GridHotkeys =
	{
		Key.Q, Key.W, Key.E, Key.R,
		Key.A, Key.S, Key.D, Key.F,
		Key.Z, Key.X, Key.C, Key.V
	};

	/// <summary>
	///     A card's key follows its icon: anything the map did not give a hotkey of its own answers to the key of the grid
	///     slot it is drawn in, so moving or reordering an ability moves its key with it.
	/// </summary>
	private void UpdateItemHotkeys()
	{
		int totalItems = _activeItems.Count;
		if (totalItems == 0) return;

		GetHotkeyCheckRange(out int startIndex, out int itemsToCheck);
		CommandCardItem?[] slots = PlaceItemsOnGrid(startIndex, itemsToCheck, totalItems);
		for (int i = 0; i < itemsToCheck; i++)
		{
			if (slots[i] != null) ApplyGridHotkey(slots[i]!, GridHotkeys[i]);
		}
	}

	private static void ApplyGridHotkey(CommandCardItem item, Key key)
	{
		if (item.Hotkey.IsNone) item.Hotkey = key;
		FormatItemTooltipHotkey(item);
	}

	private static void FormatItemTooltipHotkey(CommandCardItem item)
	{
		if (item == null || item.Hotkey.IsNone || string.IsNullOrEmpty(item.Tooltip)) return;

		// A core command is written with a stand-in letter so its localized text can be looked up by that exact string;
		// the real key replaces it after translation, in SetupBaseButtonProperties.
		if (item.HotkeyFromGrid) return;

		string tooltip = item.Tooltip;
		if (tooltip.StartsWith("[")) return;

		item.Tooltip = $"[{item.Hotkey}] {tooltip}";
	}

	/// <summary>
	///     Where each card of the current page is drawn: a card that names a grid slot takes it, and the rest fill what is
	///     left in the order they were added. Drawing the page and reading its hotkeys both go through here, so a card's
	///     key can never describe a slot other than the one its icon sits in.
	/// </summary>
	private CommandCardItem?[] PlaceItemsOnGrid(int startIndex, int maxItems, int totalItems)
	{
		var slots = new CommandCardItem?[12];
		var unslotted = new List<CommandCardItem>();

		for (int i = 0; i < maxItems; i++)
		{
			int itemIndex = startIndex + i;
			if (itemIndex >= totalItems) break;

			var item = _activeItems[itemIndex];
			if (item.GridX >= 0 && item.GridX < 4 && item.GridY >= 0 && item.GridY < 3)
			{
				int slotIndex = item.GridY * 4 + item.GridX;
				if (slots[slotIndex] == null)
				{
					slots[slotIndex] = item;
					continue;
				}
			}

			unslotted.Add(item);
		}

		int unslottedIdx = 0;
		for (int i = 0; i < maxItems; i++)
		{
			if (slots[i] == null && unslottedIdx < unslotted.Count)
			{
				slots[i] = unslotted[unslottedIdx++];
			}
		}

		return slots;
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
		CommandCardItem?[] gridSlots = PlaceItemsOnGrid(startIndex, maxItems, totalItems);

		for (int i = 0; i < maxItems; i++)
		{
			var item = gridSlots[i];
			_displayedGridItems[i] = item;
			if (item != null)
			{
				ApplyGridHotkey(item, GridHotkeys[i]);
				_commandGrid.AddChild(CreateButtonForItem(item));
			}
			else
			{
				_commandGrid.AddChild(CreateBlackTile());
			}
		}
	}

	public bool HandleHotkey(Key keycode, KeyModifierMask modifiers)
	{
		if (_activeItems.Count == 0) return false;

		if (TryInvokeHotkey(keycode, modifiers, ignoreQueueModifier: false)) return true;
		return (modifiers & KeyModifierMask.MaskShift) != 0 && TryInvokeHotkey(keycode, modifiers, ignoreQueueModifier: true);
	}

	private bool TryInvokeHotkey(Key keycode, KeyModifierMask modifiers, bool ignoreQueueModifier)
	{
		GetHotkeyCheckRange(out int startIndex, out int itemsToCheck);

		for (int i = 0; i < itemsToCheck; i++)
		{
			int itemIndex = startIndex + i;
			if (itemIndex >= _activeItems.Count) break;

			var item = _activeItems[itemIndex];
			bool hotkeyMatches = ignoreQueueModifier
				? item.Hotkey.MatchesIgnoringQueueModifier(keycode, modifiers)
				: item.Hotkey.Matches(keycode, modifiers);

			if (hotkeyMatches && !(item.IsDisabled?.Invoke() ?? false))
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
		tile.AddThemeStyleboxOverride("panel", UIStyle.CreateButtonPlate(
			new Color(0.04f, 0.04f, 0.06f, 0.35f),
			new Color(UIStyle.ColorBronze.R, UIStyle.ColorBronze.G, UIStyle.ColorBronze.B, 0.2f),
			3));
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

		UIStyle.ApplyHudButtonStyleboxes(btn);

		var label = new Label();
		label.Name = "Label_Label";
		label.Text = TranslationServer.Translate("Next");
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
			if (GameContext.Host != null)
			{
				GameContext.InGameHud?.RefreshUI(GameContext.Host.SelectedUnits);
			}
		};

		return btn;
	}

	private static readonly HashSet<string> s_reportedMissingIcons = new(StringComparer.OrdinalIgnoreCase);

	private Texture2D? LoadCommandIcon(string iconPath)
	{
		if (string.IsNullOrEmpty(iconPath)) return null;
		var tex = RtexIconLoader.Load(iconPath);
		if (tex == null)
		{
			// A map can name an icon it never shipped, and the card is rebuilt on every selection. Say it once per
			// icon instead of once per rebuild, so a genuinely missing asset is still visible in the log.
			if (s_reportedMissingIcons.Add(iconPath))
			{
				GD.PushWarning($"[CommandPanel] Failed to load ability icon at '{iconPath}', using fallback.");
			}

			tex = RtexIconLoader.Load("res://Assets/UI/alliance_flag.webp");
		}

		return tex;
	}

	private Button CreateButtonForItem(CommandCardItem item)
	{
		var btn = new CommandCardButton();
		btn.Name = "Btn_CommandCard";
		btn.TooltipContext = item.TooltipContext;
		SetupBaseButtonProperties(btn, item);
		AddHotkeyLabelIfNeeded(btn, item);
		SetButtonStyles(btn);

		if (!string.IsNullOrEmpty(item.AbilityId))
		{
			AddAbilityUIElements(btn, item);
			if (GameContext.Host?.GetAbilityDefinition(item.AbilityId)?.AutoCastEnabled == true)
			{
				btn.RightClicked += () => GameContext.Host?.ToggleAbilityAutoCast(item.AbilityId);
			}

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
		if (string.IsNullOrEmpty(transTooltip)) transTooltip = item.Tooltip;
		if (item.HotkeyFromGrid) transTooltip = WithGridHotkey(transTooltip, item.Hotkey);
		btn.TooltipText = transTooltip;

		btn.CustomMinimumSize = new Vector2(56, 56);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		btn.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		btn.FocusMode = Control.FocusModeEnum.None;
		btn.ClipContents = true;
		btn.AddThemeConstantOverride("icon_max_width", 50);
	}

	/// <summary>
	///     Swaps the stand-in letter a core command's tooltip is written with for the key its card actually answers to.
	///     The substitution happens on the translated text because a tooltip can only be looked up by the exact string it
	///     was authored with, and the letter is not something a translation should have to carry.
	/// </summary>
	private static string WithGridHotkey(string tooltip, HotkeySpec hotkey)
	{
		if (string.IsNullOrEmpty(tooltip)) return tooltip;

		int close = tooltip.StartsWith('[') ? tooltip.IndexOf(']') : -1;
		string body = close > 0 ? tooltip[(close + 1)..].TrimStart() : tooltip;

		return hotkey.IsNone ? body : $"[{hotkey}] {body}";
	}

	private void AddHotkeyLabelIfNeeded(Button btn, CommandCardItem item)
	{
		if (item.Hotkey.IsNone) return;

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
		UIStyle.ApplyHudButtonStyleboxes(btn);
	}


	private void ApplyStandardButtonState(Button btn, CommandCardItem item)
	{
		bool disabled = item.IsDisabled?.Invoke() ?? false;
		btn.Disabled = disabled;
		btn.Modulate = disabled ? new Color(0.5f, 0.5f, 0.5f, 0.7f) : Colors.White;
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

		UIStyle.ApplyHudButtonStyleboxes(btn);

		btn.Pressed += () => onClick?.Invoke();
	}

	public static void ClearCache()
	{
		s_whiteTexture = null;
	}
}
