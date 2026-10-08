using Godot;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Arch.Core;
using Realm.Godot.UI;
using Realm.Godot.Utils;

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

	public class CommandCardItem
	{
		public string Id { get; set; }
		public string IconPath { get; set; }
		public string Tooltip { get; set; }
		public Action Callback { get; set; }
		public Key Hotkey { get; set; }
		public Func<bool> IsDisabled { get; set; }
		public Func<string> GetButtonText { get; set; }
		public string AbilityId { get; set; }
		public Entity CasterEntity { get; set; } = Entity.Null;
		public float ManaCost { get; set; } = 0f;
	}

	public partial class CommandCardButton : Button
	{
		public override Control _MakeCustomTooltip(string forText)
		{
			return RichTooltip.Create(forText);
		}
	}

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

	private void UpdateExistingButtons()
	{
		var children = _commandGrid.GetChildren();
		int childCount = children.Count;
		int totalItems = _activeItems.Count;
		int pageOffset = _pageIndex * 11;

		for (int i = 0; i < 12; i++)
		{
			if (i >= childCount)
			{
				break;
			}
			var child = children[i];
			if (child is Button btn)
			{
				CommandCardItem item = null;
				if (totalItems <= 12)
				{
					if (i < totalItems)
					{
						item = _activeItems[i];
					}
				}
				else
				{
					if (i < 11)
					{
						int itemIndex = pageOffset + i;
						if (itemIndex < totalItems)
						{
							item = _activeItems[itemIndex];
						}
					}
				}

				if (item != null)
				{
					string newText = item.GetButtonText?.Invoke() ?? "";
					if (btn.Text != newText)
					{
						btn.Text = newText;
					}

					if (!string.IsNullOrEmpty(item.AbilityId))
					{
						var def = GameHost.Instance?.GetAbilityDefinition(item.AbilityId);
						if (def != null)
						{
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
						UpdateAbilityButtonVisuals(btn, item);
					}
					else
					{
						bool disabled = item.IsDisabled?.Invoke() ?? false;
						if (btn.Disabled != disabled)
						{
							btn.Disabled = disabled;
							btn.Modulate = disabled ? new Color(0.5f, 0.5f, 0.5f, 0.7f) : Colors.White;
						}
					}
				}
			}
		}
	}

	private void UpdateAbilityButtonVisuals(Button btn, CommandCardItem item)
	{
		float cdRemaining = 0f;
		float maxCd = 10f;
		float currentMana = float.MaxValue;
		float manaCost = item.ManaCost;

		var world = GameHost.Instance?.EcsWorld;
		if (world != null)
		{
			var caster = item.CasterEntity != Entity.Null && world.IsAlive(item.CasterEntity) ? item.CasterEntity : _lastFocusedEntity;
			if (caster != Entity.Null && world.IsAlive(caster))
			{
				if (world.Has<Realm.Ecs.Components.Core.Mana>(caster))
				{
					currentMana = world.Get<Realm.Ecs.Components.Core.Mana>(caster).Current;
				}

				if (world.Has<Realm.Ecs.Components.Core.Cooldowns>(caster))
				{
					var cds = world.Get<Realm.Ecs.Components.Core.Cooldowns>(caster).Value;
					if (cds.TryGetValue(item.AbilityId, out float val))
					{
						cdRemaining = val;
					}
				}

				if (world.Has<Realm.Ecs.Components.Core.SpellCooldowns>(caster))
				{
					var scd = world.Get<Realm.Ecs.Components.Core.SpellCooldowns>(caster).Value;
					if (scd != null && scd.TryGetValue(item.AbilityId, out float val))
					{
						cdRemaining = Math.Max(cdRemaining, val);
					}
				}
			}

			if (cdRemaining <= 0f && GameHost.Instance != null)
			{
				cdRemaining = GameHost.Instance.GetPlayerSpellCooldown(item.AbilityId);
			}
		}

		var abilityDef = GameHost.Instance?.GetAbilityDefinition(item.AbilityId);
		if (abilityDef != null && abilityDef.Cooldown > 0f)
		{
			maxCd = abilityDef.Cooldown;
		}
		else
		{
			maxCd = Math.Max(10f, cdRemaining);
		}
		if (abilityDef != null && abilityDef.ManaCost > 0f)
		{
			manaCost = abilityDef.ManaCost;
		}

		var manaLabel = btn.GetNodeOrNull<Label>("ManaCostLabel");
		if (manaLabel != null)
		{
			string manaText = manaCost > 0f ? GetCachedIntegerString((int)manaCost) : "";
			if (manaLabel.Text != manaText) manaLabel.Text = manaText;
		}

		var sweep = btn.GetNodeOrNull<TextureProgressBar>("CooldownSweep");
		if (sweep != null)
		{
			bool onCd = cdRemaining > 0f;
			if (sweep.Visible != onCd) sweep.Visible = onCd;
			if (onCd)
			{
				double sweepVal = Math.Clamp((cdRemaining / maxCd) * 100.0, 0.0, 100.0);
				if (Math.Abs(sweep.Value - sweepVal) > 0.4) sweep.Value = sweepVal;
			}
		}

		var cdLabel = btn.GetNodeOrNull<Label>("CooldownLabel");
		if (cdLabel != null)
		{
			string cdText = cdRemaining > 1.0f ? GetCachedIntegerString((int)Math.Ceiling(cdRemaining)) : "";
			if (cdLabel.Text != cdText) cdLabel.Text = cdText;
		}

		bool onCooldown = cdRemaining > 0f;
		bool notEnoughMana = currentMana < manaCost;
		bool disabled = onCooldown || notEnoughMana || (item.IsDisabled?.Invoke() ?? false);
		if (btn.Disabled != disabled) btn.Disabled = disabled;

		Color targetModulate = notEnoughMana ? new Color(0.5f, 0.5f, 0.5f, 0.7f) : Colors.White;
		if (btn.Modulate != targetModulate) btn.Modulate = targetModulate;
	}

	public void Update(InGameHUDViewModel viewModel)
	{
		if (_commandGrid == null)
		{
			return;
		}

		if (viewModel.SelectedUnits.Count == 0)
		{
			if (_lastFocusedEntity != Entity.Null || _commandGrid.GetChildCount() > 0)
			{
				ClearCommandGrid();
			}
			_lastFocusedEntity = Entity.Null;
			_pageIndex = 0;
			_activeItems.Clear();
			_lastIsBuildSubMenuOpen = false;
			return;
		}

		int focusIdx = viewModel.CycleSelectionIndex;
		if (focusIdx < 0 || focusIdx >= viewModel.SelectedUnits.Count)
		{
			focusIdx = 0;
		}
		var focusedUnit = viewModel.SelectedUnits[focusIdx];

		if (focusedUnit.IsEnemy)
		{
			if (_lastFocusedEntity != Entity.Null || _commandGrid.GetChildCount() > 0)
			{
				ClearCommandGrid();
			}
			_lastFocusedEntity = Entity.Null;
			_pageIndex = 0;
			_activeItems.Clear();
			_lastIsBuildSubMenuOpen = false;
			return;
		}

		bool subMenuOpen = viewModel.IsBuildSubMenuOpen;
		bool focusedUnitChanged = focusedUnit.Entity != _lastFocusedEntity;
		bool subMenuChanged = subMenuOpen != _lastIsBuildSubMenuOpen;

		if (focusedUnitChanged)
		{
			_pageIndex = 0;
			_lastFocusedEntity = focusedUnit.Entity;
		}

		_lastIsBuildSubMenuOpen = subMenuOpen;

		_activeItems = GetCommandCardItems(focusedUnit, subMenuOpen);
		int totalItems = _activeItems.Count;

		Key[] gridHotkeys = new Key[] {
			Key.Q, Key.W, Key.E, Key.R,
			Key.A, Key.S, Key.D, Key.F,
			Key.Z, Key.X, Key.C, Key.V
		};

		int pageOffset = _pageIndex * 11;
		for (int i = 0; i < totalItems; i++)
		{
			int localIdx = -1;
			if (totalItems <= 12)
			{
				localIdx = i;
			}
			else
			{
				if (i >= pageOffset && i < pageOffset + 11)
				{
					localIdx = i - pageOffset;
				}
			}

			if (localIdx >= 0 && localIdx < 12)
			{
				_activeItems[i].Hotkey = gridHotkeys[localIdx];
				_activeItems[i].Tooltip = HotkeyPrefixRegex().Replace(_activeItems[i].Tooltip, "[" + gridHotkeys[localIdx].ToString() + "] ");
			}
			else
			{
				_activeItems[i].Hotkey = Key.None;
			}
		}

		bool pageChanged = _pageIndex != _lastPageIndex;
		_lastPageIndex = _pageIndex;

		if (focusedUnitChanged || subMenuChanged || pageChanged || _commandGrid.GetChildCount() == 0)
		{
			ClearCommandGrid();

			if (totalItems <= 12)
			{
				_pageIndex = 0;
				for (int i = 0; i < 12; i++)
				{
					if (i < totalItems)
					{
						_commandGrid.AddChild(CreateButtonForItem(_activeItems[i]));
					}
					else
					{
						_commandGrid.AddChild(CreateBlackTile());
					}
				}
			}
			else
			{
				int numPages = (totalItems + 10) / 11;
				if (_pageIndex >= numPages)
				{
					_pageIndex = 0;
				}

				int startIndex = _pageIndex * 11;
				for (int i = 0; i < 11; i++)
				{
					int itemIndex = startIndex + i;
					if (itemIndex < totalItems)
					{
						_commandGrid.AddChild(CreateButtonForItem(_activeItems[itemIndex]));
					}
					else
					{
						_commandGrid.AddChild(CreateBlackTile());
					}
				}

				_commandGrid.AddChild(CreateCycleButton(numPages));
			}
		}
		else
		{
			UpdateExistingButtons();
		}
	}

	public bool HandleHotkey(Key keycode)
	{
		if (_activeItems.Count == 0) return false;

		int totalItems = _activeItems.Count;
		if (totalItems <= 12)
		{
			for (int i = 0; i < totalItems; i++)
			{
				var item = _activeItems[i];
				if (item.Hotkey == keycode && !(item.IsDisabled?.Invoke() ?? false))
				{
					item.Callback?.Invoke();
					return true;
				}
			}
		}
		else
		{
			int numPages = (totalItems + 10) / 11;
			if (_pageIndex >= numPages) _pageIndex = 0;
			int startIndex = _pageIndex * 11;
			for (int i = 0; i < 11; i++)
			{
				int itemIndex = startIndex + i;
				if (itemIndex < totalItems)
				{
					var item = _activeItems[itemIndex];
					if (item.Hotkey == keycode && !(item.IsDisabled?.Invoke() ?? false))
					{
						item.Callback?.Invoke();
						return true;
					}
				}
			}
		}
		return false;
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

	private Button CreateCycleButton(int numPages)
	{
		var btn = new Button();
		btn.Flat = false;
		btn.Text = "";
		btn.ExpandIcon = true;
		btn.Icon = GD.Load<Texture2D>("res://Assets/UI/search_icon_clean.png");
		btn.TooltipText = TranslationServer.Translate("Cycle Abilities / Commands");
		btn.CustomMinimumSize = new Vector2(44, 44);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		btn.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		btn.FocusMode = Control.FocusModeEnum.None;
		btn.AddThemeConstantOverride("icon_max_width", 38);

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
		btn.Flat = false;
		btn.Text = item.GetButtonText?.Invoke() ?? "";
		btn.ExpandIcon = true;
		btn.Icon = !string.IsNullOrEmpty(item.IconPath) ? LoadCommandIcon(item.IconPath) : null;
		
		string transTooltip = TranslationServer.Translate(item.Tooltip);
		btn.TooltipText = string.IsNullOrEmpty(transTooltip) ? item.Tooltip : transTooltip;
		
		btn.CustomMinimumSize = new Vector2(44, 44);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		btn.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		btn.FocusMode = Control.FocusModeEnum.None;
		btn.ClipContents = true;
		btn.AddThemeConstantOverride("icon_max_width", 38);

		if (item.Hotkey != Key.None)
		{
			string hotkeyText = item.Hotkey.ToString();
			var hotkeyLabel = new Label();
			hotkeyLabel.Name = "HotkeyLabel";
			hotkeyLabel.Text = hotkeyText;
			hotkeyLabel.AddThemeFontSizeOverride("font_size", 10);
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

		if (!string.IsNullOrEmpty(item.AbilityId))
		{
			var sweep = new TextureProgressBar();
			sweep.Name = "CooldownSweep";
			sweep.FillMode = (int)TextureProgressBar.FillModeEnum.CounterClockwise;
			sweep.NinePatchStretch = true;
			sweep.TextureProgress = GetOrCreateWhiteTexture();
			sweep.TintProgress = new Color(0f, 0f, 0f, 0.65f);
			sweep.MinValue = 0.0;
			sweep.MaxValue = 100.0;
			sweep.Value = 0.0;
			sweep.Visible = false;
			sweep.MouseFilter = Control.MouseFilterEnum.Ignore;
			sweep.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			btn.AddChild(sweep);

			var cdLabel = new Label();
			cdLabel.Name = "CooldownLabel";
			cdLabel.Text = "";
			cdLabel.AddThemeFontSizeOverride("font_size", 14);
			cdLabel.AddThemeColorOverride("font_color", Colors.White);
			cdLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.95f));
			cdLabel.AddThemeConstantOverride("outline_size", 4);
			cdLabel.HorizontalAlignment = HorizontalAlignment.Center;
			cdLabel.VerticalAlignment = VerticalAlignment.Center;
			cdLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			cdLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
			btn.AddChild(cdLabel);

			if (item.ManaCost > 0f)
			{
				var manaLabel = new Label();
				manaLabel.Name = "ManaCostLabel";
				manaLabel.Text = GetCachedIntegerString((int)item.ManaCost);
				manaLabel.AddThemeFontSizeOverride("font_size", 10);
				manaLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.75f, 1.0f));
				manaLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
				manaLabel.AddThemeConstantOverride("outline_size", 4);
				manaLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
				manaLabel.OffsetRight = -4;
				manaLabel.OffsetTop = 3;
				manaLabel.GrowHorizontal = Control.GrowDirection.Begin;
				manaLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
				btn.AddChild(manaLabel);
			}

			UpdateAbilityButtonVisuals(btn, item);
		}
		else
		{
			bool disabled = item.IsDisabled?.Invoke() ?? false;
			if (disabled)
			{
				btn.Disabled = true;
				btn.Modulate = new Color(0.5f, 0.5f, 0.5f, 0.7f);
			}
			else
			{
				btn.Disabled = false;
				btn.Modulate = Colors.White;
			}
		}

		btn.Pressed += () => {
			item.Callback?.Invoke();
		};

		return btn;
	}

	private List<CommandCardItem> GetCommandCardItems(InGameHUDViewModel.SelectedUnitInfo focusedUnit, bool isBuildSubMenuOpen)
	{
		var items = new List<CommandCardItem>();
		if (focusedUnit == null) return items;

		if (focusedUnit.IsBuilding && focusedUnit.IsUnderConstruction)
		{
			items.Add(new CommandCardItem
			{
				Id = "stop",
				IconPath = "res://Assets/UI/cancel_button_2.png",
				Tooltip = "[S] Cancel Construction",
				Hotkey = Key.S,
				// Future: Implement cancel construction logic
				Callback = () => { } 
			});
			return items;
		}

		bool hasMetadata = GameHost.UnitRegistry.TryGetValue(focusedUnit.UnitId, out var meta);

		if (!focusedUnit.IsBuilding)
		{
			if (isBuildSubMenuOpen)
			{
				string[] options = hasMetadata && meta.BuildOptions != null ? meta.BuildOptions : new string[0];
				foreach (var opt in options)
				{
					items.Add(CreateBuildOptionItem(opt));
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
			else
			{
				bool isStationary = focusedUnit.Speed <= 0.001f;

				if (isStationary)
				{
					var perkAbilities = new List<string>();
					var otherAbilities = new List<string>();
					foreach (var ab in focusedUnit.Abilities)
					{
						if (ab.StartsWith("perk_"))
						{
							perkAbilities.Add(ab);
						}
						else
						{
							otherAbilities.Add(ab);
						}
					}

					if (perkAbilities.Count > 0)
					{
						foreach (var ab in perkAbilities)
						{
							items.Add(CreateAbilityItem(ab, focusedUnit.Entity));
						}

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
							Id = "stop",
							IconPath = "res://Assets/UI/cancel_button_2.png",
							Tooltip = "[S] Stop Selected Units",
							Hotkey = Key.S,
							Callback = () => {
								InGameHUD.Instance?.ShowFeedbackText(TranslationServer.Translate("Command: Stop Current Action"), new Color(0.9f, 0.2f, 0.2f));
								GameHost.Instance?.StopSelectedUnits();
							}
						});

						foreach (var ab in otherAbilities)
						{
							items.Add(CreateAbilityItem(ab, focusedUnit.Entity));
						}

						items.Add(new CommandCardItem
						{
							Id = "hold",
							IconPath = "res://Assets/UI/magic_upgrade_arrow.png",
							Tooltip = "[H] Hold Position — Unit stays put and attacks in place",
							Hotkey = Key.H,
							Callback = () => {
								InGameHUD.Instance?.ShowFeedbackText(TranslationServer.Translate("Command: Hold Position"), new Color(0.9f, 0.8f, 0.1f));
								GameHost.Instance?.HoldSelectedUnits();
							}
						});
					}
					else
					{
						foreach (var ab in focusedUnit.Abilities)
						{
							items.Add(CreateAbilityItem(ab, focusedUnit.Entity));
						}

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
							Id = "stop",
							IconPath = "res://Assets/UI/cancel_button_2.png",
							Tooltip = "[S] Stop Selected Units",
							Hotkey = Key.S,
							Callback = () => {
								InGameHUD.Instance?.ShowFeedbackText(TranslationServer.Translate("Command: Stop Current Action"), new Color(0.9f, 0.2f, 0.2f));
								GameHost.Instance?.StopSelectedUnits();
							}
						});
						items.Add(new CommandCardItem
						{
							Id = "hold",
							IconPath = "res://Assets/UI/magic_upgrade_arrow.png",
							Tooltip = "[H] Hold Position — Unit stays put and attacks in place",
							Hotkey = Key.H,
							Callback = () => {
								InGameHUD.Instance?.ShowFeedbackText(TranslationServer.Translate("Command: Hold Position"), new Color(0.9f, 0.8f, 0.1f));
								GameHost.Instance?.HoldSelectedUnits();
							}
						});
					}

					bool canBuild = hasMetadata && meta.BuildOptions != null && meta.BuildOptions.Length > 0;
					if (canBuild)
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
					items.Add(new CommandCardItem
					{
						Id = "move",
						IconPath = "res://Assets/UI/move_speed.png",
						Tooltip = "[M] Move / Right-Click Ground",
						Hotkey = Key.M,
						Callback = () => GameHost.Instance?.EnterCommandTargeting("move")
					});
					items.Add(new CommandCardItem
					{
						Id = "stop",
						IconPath = "res://Assets/UI/cancel_button_2.png",
						Tooltip = "[S] Stop Selected Units",
						Hotkey = Key.S,
						Callback = () => {
							InGameHUD.Instance?.ShowFeedbackText(TranslationServer.Translate("Command: Stop Current Action"), new Color(0.9f, 0.2f, 0.2f));
							GameHost.Instance?.StopSelectedUnits();
						}
					});
					items.Add(new CommandCardItem
					{
						Id = "hold",
						IconPath = "res://Assets/UI/magic_upgrade_arrow.png",
						Tooltip = "[H] Hold Position — Unit stays put and attacks in place",
						Hotkey = Key.H,
						Callback = () => {
							InGameHUD.Instance?.ShowFeedbackText(TranslationServer.Translate("Command: Hold Position"), new Color(0.9f, 0.8f, 0.1f));
							GameHost.Instance?.HoldSelectedUnits();
						}
					});
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

					bool canBuild = hasMetadata && meta.BuildOptions != null && meta.BuildOptions.Length > 0;
					if (canBuild)
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
			}
		}
		else
		{
			if (focusedUnit.UnitId == "castle")
			{
				string[] trainOptions = hasMetadata && meta.BuildOptions != null && meta.BuildOptions.Length > 0 ? meta.BuildOptions : System.Array.Empty<string>();
				foreach (var opt in trainOptions)
				{
					items.Add(CreateTrainOptionItem(opt));
				}

				items.Add(new CommandCardItem
				{
					Id = "set_rally",
					IconPath = "res://Assets/UI/alliance_flag.png",
					Tooltip = "[Y] Set Rally Point — Set location where new units will walk",
					Hotkey = Key.Y,
					Callback = () => GameHost.Instance?.EnterCommandTargeting("rally")
				});

				if (GameHost.ItemRegistry.Count > 0)
				{
					foreach (var itemMeta in GameHost.ItemRegistry.Values)
					{
						string itemId = itemMeta.TemplateID;
						string itemName = !string.IsNullOrEmpty(itemMeta.Name) ? itemMeta.Name : itemId;
						float itemCost = itemMeta.CostGold;
						string itemIcon = !string.IsNullOrEmpty(itemMeta.IconPath) ? itemMeta.IconPath : "res://Assets/UI/alliance_flag.png";
						string itemDesc = !string.IsNullOrEmpty(itemMeta.Description) ? itemMeta.Description : $"Buy {itemName} for a nearby combat unit";

						string capturedItemId = itemId;
						items.Add(new CommandCardItem
						{
							Id = "buy_" + itemId,
							IconPath = itemIcon,
							Tooltip = $"[I] Buy {itemName} (Cost: {itemCost:F0} Gold) — {itemDesc}",
							Hotkey = Key.None,
							Callback = () => {
								var selected = GameHost.Instance?.SelectedUnits;
								if (selected != null && selected.Count == 1)
								{
									GameHost.Instance.BuyItem(capturedItemId, selected[0].Entity);
								}
							}
						});
					}
				}

				items.Add(new CommandCardItem
				{
					Id = "upgrade_weapons",
					IconPath = "res://Assets/UI/battle_axe.png",
					Tooltip = "[W] Upgrade Weapons (Cost: 150 Gold, 100 Wood)\nPermanently increases unit damage by +3",
					Hotkey = Key.W,
					Callback = () => GameHost.Instance?.BuyWeaponsUpgrade(),
					IsDisabled = () => GameHost.Instance != null && GameHost.Instance.HasWeaponsUpgrade,
					GetButtonText = () => (GameHost.Instance != null && GameHost.Instance.HasWeaponsUpgrade) ? TranslationServer.Translate("MAXED") : ""
				});

				items.Add(new CommandCardItem
				{
					Id = "upgrade_shields",
					IconPath = "res://Assets/UI/battle_shield.png",
					Tooltip = "[G] Upgrade Armor (Cost: 150 Gold, 100 Stone)\nPermanently increases unit armor by +2",
					Hotkey = Key.G,
					Callback = () => GameHost.Instance?.BuyShieldsUpgrade(),
					IsDisabled = () => GameHost.Instance != null && GameHost.Instance.HasShieldsUpgrade,
					GetButtonText = () => (GameHost.Instance != null && GameHost.Instance.HasShieldsUpgrade) ? TranslationServer.Translate("MAXED") : ""
				});

				items.Add(new CommandCardItem
				{
					Id = "upgrade_harvesting",
					IconPath = "res://Assets/UI/gold_coin.png",
					Tooltip = "[T] Upgrade Harvesting (Cost: 150 Wood, 100 Stone)\nPermanently increases passive resource gathering rates by +50%",
					Hotkey = Key.T,
					Callback = () => GameHost.Instance?.BuyHarvestingUpgrade(),
					IsDisabled = () => GameHost.Instance != null && GameHost.Instance.HasHarvestingUpgrade,
					GetButtonText = () => (GameHost.Instance != null && GameHost.Instance.HasHarvestingUpgrade) ? TranslationServer.Translate("MAXED") : ""
				});

				foreach (var ab in focusedUnit.Abilities)
				{
					items.Add(CreateAbilityItem(ab, focusedUnit.Entity));
				}
			}
			else
			{
				if (hasMetadata && meta.BuildOptions != null)
				{
					foreach (var opt in meta.BuildOptions)
					{
						items.Add(CreateTrainOptionItem(opt));
					}
				}
				foreach (var ab in focusedUnit.Abilities)
				{
					items.Add(CreateAbilityItem(ab, focusedUnit.Entity));
				}
			}
		}

		items.RemoveAll(i => i == null);
		return items;
	}

	private CommandCardItem CreateBuildOptionItem(string unitId)
	{
		var hotkey = Key.None;
		
		string name = unitId.ToUpper();
		float gold = 0, wood = 0, stone = 0;
		if (GameHost.TryGetUnitOrBuildingMetadata(unitId, out var structureMeta))
		{
			name = structureMeta.Name;
			gold = structureMeta.CostGold;
			wood = structureMeta.CostWood;
			stone = structureMeta.CostStone;
		}

		string tooltipFormat = hotkey != Key.None 
			? "[{0}] Build {1} (Cost: {2} Gold, {3} Wood, {4} Stone)"
			: "Build {0} (Cost: {1} Gold, {2} Wood, {3} Stone)";

		string finalTooltip = string.Format(TranslationServer.Translate(tooltipFormat), 
			hotkey.ToString(), name, gold, wood, stone);

		return new CommandCardItem
		{
			Id = "build_" + unitId,
			IconPath = GetUnitIcon(unitId),
			Tooltip = finalTooltip,
			Hotkey = hotkey,
			Callback = () => GameHost.Instance?.EnterBuildingPlacement(unitId)
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

		string tooltipFormat = hotkey != Key.None
			? "[{0}] Train {1} ({2}) — {3}"
			: "Train {0} ({1}) — {2}";

		string finalTooltip = string.Format(TranslationServer.Translate(tooltipFormat), 
			hotkey.ToString(), name, costStr, desc);

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
		var unitWrapper = host?.AllUnits.Find(u => u.Entity == casterEntity);
		if (host != null && host.IsAbilityHiddenForUnit(unitWrapper, abilityId))
		{
			return null;
		}

		var abilityDef = host?.GetAbilityDefinition(abilityId);

		string iconPath = !string.IsNullOrEmpty(abilityDef?.IconPath)
			? abilityDef.IconPath
			: GetDefaultAbilityIcon(abilityId);

		string tooltip = !string.IsNullOrEmpty(abilityDef?.Tooltip)
			? abilityDef.Tooltip
			: GetDefaultAbilityTooltip(abilityId);

		float manaCost = host != null ? host.GetAbilityManaCostForUnit(unitWrapper, abilityId) : (abilityDef?.ManaCost ?? 0f);
		bool isInstant = abilityDef != null && abilityDef.IsInstant;

		Key hotkey = Key.None;
		if (!string.IsNullOrEmpty(abilityDef?.Hotkey) && Enum.TryParse<Key>(abilityDef.Hotkey, true, out var parsedKey))
		{
			hotkey = parsedKey;
		}

		Action callback;
		if (isInstant)
		{
			callback = () => GameHost.Instance?.CastInstantAbility(abilityId);
		}
		else
		{
			callback = () => GameHost.Instance?.EnterSpellTargeting(abilityId);
		}

		return new CommandCardItem
		{
			Id = abilityId,
			AbilityId = abilityId,
			CasterEntity = casterEntity,
			ManaCost = manaCost,
			IconPath = iconPath,
			Tooltip = tooltip,
			Hotkey = hotkey,
			Callback = callback,
			IsDisabled = () => {
				if (host != null && host.IsAbilityDisabledForUnit(unitWrapper, abilityId))
				{
					return true;
				}
				if (host?.EcsWorld == null) return false;
				var world = host.EcsWorld;
				if (casterEntity != Entity.Null && world.IsAlive(casterEntity))
				{
					if (world.Has<Realm.Ecs.Components.Core.Mana>(casterEntity))
					{
						if (world.Get<Realm.Ecs.Components.Core.Mana>(casterEntity).Current < manaCost) return true;
					}
					if (world.Has<Realm.Ecs.Components.Core.Cooldowns>(casterEntity))
					{
						if (world.Get<Realm.Ecs.Components.Core.Cooldowns>(casterEntity).Value.TryGetValue(abilityId, out float cd) && cd > 0f) return true;
					}
					if (world.Has<Realm.Ecs.Components.Core.SpellCooldowns>(casterEntity))
					{
						var scd = world.Get<Realm.Ecs.Components.Core.SpellCooldowns>(casterEntity).Value;
						if (scd != null && scd.TryGetValue(abilityId, out float cd) && cd > 0f) return true;
					}
				}
				if (host != null && host.GetPlayerSpellCooldown(abilityId) > 0f)
				{
					return true;
				}
				return false;
			}
		};
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
		btn.CustomMinimumSize = new Vector2(44, 44);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		btn.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		btn.FocusMode = Control.FocusModeEnum.None;
		btn.ClipContents = true;
		btn.AddThemeConstantOverride("icon_max_width", 38);

		if (tooltip.StartsWith('[') && tooltip.Contains(']'))
		{
			int end = tooltip.IndexOf(']');
			string hotkeyText = tooltip.Substring(1, end - 1);
			var hotkeyLabel = new Label();
			hotkeyLabel.Name = "HotkeyLabel";
			hotkeyLabel.Text = hotkeyText;
			hotkeyLabel.AddThemeFontSizeOverride("font_size", 10);
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
