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
		var def = GameContext.Host?.GetAbilityDefinition(item.AbilityId);
		if (def == null) return;

		// Godot drops the tooltip popup as soon as its text changes while it is on screen, and a server sync can
		// resolve a slightly different string moments after the hover opened it. Leave the text alone while the
		// pointer is on the card so the tooltip stays up for as long as the player keeps hovering.
		if (!IsPointerOver(btn))
		{
			string transTooltip = ResolveAbilityTooltip(item.AbilityId, def, IsAbilityStateDisabled(item.CasterEntity, item.AbilityId), item.ManaCost);
			if (!string.IsNullOrEmpty(transTooltip) && !item.Hotkey.IsNone)
			{
				transTooltip = $"[{item.Hotkey}] {transTooltip}";
			}

			if (!string.IsNullOrEmpty(transTooltip) && btn.TooltipText != transTooltip)
			{
				btn.TooltipText = transTooltip;
			}
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

		if (GameContext.Host?.GetAbilityDefinition(item.AbilityId)?.AutoCastEnabled == true)
		{
			var marquee = new ColorRect
			{
				Name = AutoCastMarqueeName,
				Color = Colors.White,
				Visible = true,
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Material = new ShaderMaterial { Shader = GetAutoCastMarqueeShader() }
			};
			marquee.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			btn.AddChild(marquee);
		}

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
}
