using Godot;
using Realm.MapAPI;
using System;
using System.Collections.Generic;

public class AbilityDefinition
{
	public string Id { get; set; } = "";
	public string DisplayName { get; set; } = "";
	public string Tooltip { get; set; } = "";
	public string IconPath { get; set; } = "";
	public bool IsInstant { get; set; }
	public string Hotkey { get; set; } = "";
	public int GridX { get; set; } = -1;
	public int GridY { get; set; } = -1;
	public float ManaCost { get; set; } = 0f;
	public float Cooldown { get; set; } = 0f;
	public float TargetRange { get; set; } = 0f;
	public float AreaOfEffectRadius { get; set; } = 0f;
	public float Damage { get; set; } = 0f;
	public float Healing { get; set; } = 0f;
	public string? VisualEffect { get; set; }
	public string? CastSound { get; set; }
}

public partial class GameHost
{
		private readonly Dictionary<string, AbilityDefinition> _abilityDefinitions = CreateDefaultAbilityCatalog();
	private readonly Dictionary<(int UnitUniqueId, string AbilityId), (bool Disabled, bool Hidden)> _unitAbilityStates = new();
	private readonly Dictionary<(int UnitUniqueId, string AbilityId), float> _unitAbilityManaCosts = new();
	private readonly Dictionary<string, string> _itemTooltips = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<(int PlayerIndex, string TechId), int> _playerTechLevels = new();

	private static Dictionary<string, AbilityDefinition> CreateDefaultAbilityCatalog()
	{
		return new Dictionary<string, AbilityDefinition>(StringComparer.OrdinalIgnoreCase);
	}

	public void ResetAbilityCatalog()
	{
		_abilityDefinitions.Clear();
	}

	public void RegisterCustomAbilities(List<AbilityMetadata> customAbilities)
	{
		if (customAbilities == null) return;
		foreach (var meta in customAbilities)
		{
			if (string.IsNullOrEmpty(meta.TemplateID)) continue;
			var def = new AbilityDefinition
			{
				Id = meta.TemplateID,
				DisplayName = meta.Name ?? "",
				Tooltip = meta.Description ?? "",
				IconPath = meta.IconPath ?? "",
				IsInstant = string.Equals(meta.AbilityType, "instant_spell", StringComparison.OrdinalIgnoreCase),
				ManaCost = meta.ManaCost,
				Cooldown = meta.Cooldown,
				TargetRange = meta.TargetRange,
				AreaOfEffectRadius = meta.AreaOfEffectRadius,
				Damage = meta.Damage,
				Healing = meta.Healing,
				VisualEffect = meta.VisualEffect,
				CastSound = meta.CastSound
			};
			_abilityDefinitions[meta.TemplateID] = def;
			if (meta.TemplateID.StartsWith("ability/", StringComparison.OrdinalIgnoreCase))
			{
				_abilityDefinitions[meta.TemplateID.Substring(8)] = def;
			}
		}
	}

	public AbilityDefinition GetAbilityDefinition(string abilityId)
	{
		if (string.IsNullOrEmpty(abilityId)) return null;
		_abilityDefinitions.TryGetValue(abilityId, out var def);
		return def;
	}

	void IGameAPI.RegisterAbility(string abilityId, string displayName, string tooltip, string iconPath, bool isInstant)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		if (!_abilityDefinitions.TryGetValue(abilityId, out var def))
		{
			def = new AbilityDefinition { Id = abilityId };
			_abilityDefinitions[abilityId] = def;
		}

		def.DisplayName = displayName ?? "";
		def.Tooltip = tooltip ?? "";
		if (!string.IsNullOrEmpty(iconPath)) def.IconPath = iconPath;
		def.IsInstant = isInstant;

		if (_multiplayerActive && IsServerActive())
		{
			Rpc(nameof(ClientRegisterAbility), abilityId, def.DisplayName, def.Tooltip, def.IconPath, isInstant);
		}
	}

	void IGameAPI.SetAbilityInstant(string abilityId, bool isInstant)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		if (!_abilityDefinitions.TryGetValue(abilityId, out var def))
		{
			def = new AbilityDefinition { Id = abilityId };
			_abilityDefinitions[abilityId] = def;
		}

		def.IsInstant = isInstant;
	}

	void IGameAPI.SetAbilityIcon(string abilityId, string iconPath)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		if (!_abilityDefinitions.TryGetValue(abilityId, out var def))
		{
			def = new AbilityDefinition { Id = abilityId };
			_abilityDefinitions[abilityId] = def;
		}

		def.IconPath = iconPath ?? "";
	}

	void IGameAPI.SetAbilityTooltip(string abilityId, string tooltip)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		if (!_abilityDefinitions.TryGetValue(abilityId, out var def))
		{
			def = new AbilityDefinition { Id = abilityId };
			_abilityDefinitions[abilityId] = def;
		}

		def.Tooltip = tooltip ?? "";
	}

	void IGameAPI.SetAbilityGridPosition(string abilityId, int x, int y)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		if (!_abilityDefinitions.TryGetValue(abilityId, out var def))
		{
			def = new AbilityDefinition { Id = abilityId };
			_abilityDefinitions[abilityId] = def;
		}

		def.GridX = x;
		def.GridY = y;
	}

	void IGameAPI.SetAbilityManaCost(IUnit unit, string abilityId, float manaCost)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		if (!_abilityDefinitions.TryGetValue(abilityId, out var def))
		{
			def = new AbilityDefinition { Id = abilityId };
			_abilityDefinitions[abilityId] = def;
		}

		def.ManaCost = manaCost;
	}

	void IGameAPI.ShowSummaryTable(string title, bool visible)
	{
		Callable.From(() => InGameHUD.Instance?.ShowSummaryTable(title, visible)).CallDeferred();
	}

	void IGameAPI.SetSummaryTableHeaders(params string[] columnHeaders)
	{
		Callable.From(() => InGameHUD.Instance?.SetSummaryTableHeaders(columnHeaders)).CallDeferred();
	}

	void IGameAPI.SetSummaryTableRow(string rowKey, params string[] cellValues)
	{
		Callable.From(() => InGameHUD.Instance?.SetSummaryTableRow(rowKey, cellValues)).CallDeferred();
	}

	void IGameAPI.ClearSummaryTable()
	{
		Callable.From(() => InGameHUD.Instance?.ClearSummaryTable()).CallDeferred();
	}

	string IGameAPI.GetPlayerLanguage(int playerIndex)
	{
		return LocalizationManager.GetCurrentLanguageCode();
	}

	string IGameAPI.Translate(string key, int playerIndex)
	{
		return LocalizationManager.TranslateKey(key);
	}

	void IGameAPI.TriggerMeshImpulse(IUnit unit, float strength, float duration, float frequency)
	{
		if (unit == null) return;
		unit.TriggerMeshImpulse(strength, duration, frequency);
	}

	void IGameAPI.TriggerResourceMeshImpulse(IResourceNode node, float strength, float duration, float frequency)
	{
		if (node == null) return;
		node.TriggerMeshImpulse(strength, duration, frequency);
	}

	void IGameAPI.SetAbilityHotkey(string abilityId, string hotkey)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		if (!_abilityDefinitions.TryGetValue(abilityId, out var def))
		{
			def = new AbilityDefinition { Id = abilityId };
			_abilityDefinitions[abilityId] = def;
		}

		def.Hotkey = hotkey ?? "";
	}

	void IGameAPI.SetAbilityState(IUnit unit, string abilityId, bool disabled, bool hidden)
	{
		if (unit == null || string.IsNullOrEmpty(abilityId)) return;
		_unitAbilityStates[(unit.UniqueId, abilityId)] = (disabled, hidden);
	}

	public bool IsAbilityDisabledForUnit(IUnit unit, string abilityId)
	{
		if (unit != null && _unitAbilityStates.TryGetValue((unit.UniqueId, abilityId), out var state))
		{
			return state.Disabled;
		}
		return false;
	}

	public bool IsAbilityHiddenForUnit(IUnit unit, string abilityId)
	{
		if (unit != null && _unitAbilityStates.TryGetValue((unit.UniqueId, abilityId), out var state))
		{
			return state.Hidden;
		}
		return false;
	}

	public float GetAbilityManaCostForUnit(IUnit unit, string abilityId)
	{
		if (unit != null && _unitAbilityManaCosts.TryGetValue((unit.UniqueId, abilityId), out float cost))
		{
			return cost;
		}
		var def = GetAbilityDefinition(abilityId);
		return def?.ManaCost ?? 0f;
	}

	void IGameAPI.SetItemTooltip(string itemId, string tooltip)
	{
		if (string.IsNullOrEmpty(itemId)) return;
		_itemTooltips[itemId] = tooltip ?? "";
		if (ItemRegistry.TryGetValue(itemId, out var itemMeta))
		{
			itemMeta.Description = tooltip ?? "";
		}
	}

	int IGameAPI.GetPlayerTechLevel(int playerIndex, string techId)
	{
		if (string.IsNullOrEmpty(techId)) return 0;
		return _playerTechLevels.TryGetValue((playerIndex, techId), out int level) ? level : 0;
	}

	void IGameAPI.SetPlayerTechLevel(int playerIndex, string techId, int level)
	{
		if (string.IsNullOrEmpty(techId)) return;
		_playerTechLevels[(playerIndex, techId)] = level;
	}

	void IGameAPI.AddPlayerTechLevel(int playerIndex, string techId, int delta)
	{
		if (string.IsNullOrEmpty(techId)) return;
		int current = ((IGameAPI)this).GetPlayerTechLevel(playerIndex, techId);
		_playerTechLevels[(playerIndex, techId)] = current + delta;
	}

}
