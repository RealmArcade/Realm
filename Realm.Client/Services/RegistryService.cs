using System;
using System.Collections.Generic;
using System.IO;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.UI;
using Realm.Client.UI.InGame;
using Realm.Client.Utils;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Services;
using Realm.Shared.Abilities;

namespace Realm.Client.Services;

public class RegistryService
{
	static RegistryService() => AbilityEffectFactory.WarningSink = GD.PushWarning;

	private static MapRulesCatalog Rules => GameContext.Simulation.MapRules;

	public void RegisterPlacementRadius(string definitionKey, float radius) => Rules.RegisterPlacementRadius(definitionKey, radius);

	public float GetPlacementRadius(string placeId, float scale) => Rules.GetPlacementRadius(placeId, scale);

	public void RegisterResourceDropoffBuilding(string definitionKey) => Rules.RegisterResourceDropoffBuilding(definitionKey);

	public void RegisterResourceWorkerUnit(string unitKey) => Rules.RegisterResourceWorkerUnit(unitKey);

	public bool IsResourceDropoffBuilding(string definitionId) => Rules.IsResourceDropoffBuilding(definitionId);

	public void RegisterPrimaryBuilding(string definitionId) => Rules.RegisterPrimaryBuilding(definitionId);

	public bool IsPrimaryBuilding(string definitionId) => Rules.IsPrimaryBuilding(definitionId);

	public void RegisterResourceNodeType(string nodeDefinitionId, string resourceType) => Rules.RegisterResourceNodeType(nodeDefinitionId, resourceType);

	public string? GetResourceTypeForNode(string nodeDefinitionId) => Rules.GetResourceTypeForNode(nodeDefinitionId);

	public bool UnitHasAbility(string unitId, string abilityId)
	{
		if (GameHost.UnitRegistry.TryGetValue(unitId, out var meta))
		{
			if (meta.Abilities != null)
			{
				return Array.Exists(meta.Abilities, a => a == abilityId);
			}
		}

		return false;
	}

	public UnitMetadata? FindUnitOrBuildingMetadata(string definitionId)
	{
		if (string.IsNullOrEmpty(definitionId)) return null;
		if (UnitRegistry.TryGetValue(definitionId, out var unitMeta)) return unitMeta;
		if (BuildingRegistry.TryGetValue(definitionId, out var buildingMeta)) return buildingMeta;
		return null;
	}

	public string GetUnitTargetModel(string unitId, ref bool isBuilding)
	{
		if (UnitRegistry.TryGetValue(unitId, out var meta) && !string.IsNullOrEmpty(meta.ModelPath)) return meta.ModelPath;

		if (BuildingRegistry.TryGetValue(unitId, out var bldMeta) && !string.IsNullOrEmpty(bldMeta.ModelPath))
		{
			isBuilding = true;
			return bldMeta.ModelPath;
		}

		if (ResourceRegistry.TryGetValue(unitId, out var resMeta) && !string.IsNullOrEmpty(resMeta.ModelPath)) return resMeta.ModelPath;
		if (PropRegistry.TryGetValue(unitId, out var propMeta) && !string.IsNullOrEmpty(propMeta.ModelPath)) return propMeta.ModelPath;

		return null;
	}

	public static IEnumerable<string> GetNormalizedAbilityIdCandidates(string abilityId)
	{
		if (string.IsNullOrEmpty(abilityId)) yield break;
		yield return abilityId;

		string trimmed = abilityId;
		if (trimmed.StartsWith("ability/", StringComparison.OrdinalIgnoreCase))
		{
			trimmed = trimmed.Substring(8);
			yield return trimmed;
		}
		else if (trimmed.StartsWith("unit/", StringComparison.OrdinalIgnoreCase))
		{
			trimmed = trimmed.Substring(5);
			yield return trimmed;
		}
		else if (trimmed.StartsWith("building/", StringComparison.OrdinalIgnoreCase))
		{
			trimmed = trimmed.Substring(9);
			yield return trimmed;
		}
		else
		{
			yield return "ability/" + trimmed;
			yield return "unit/" + trimmed;
			yield return "building/" + trimmed;
		}
	}

	public void AddUnitTypeAbility(string unitTypeId, string abilityId)
	{
		if (string.IsNullOrEmpty(unitTypeId) || string.IsNullOrEmpty(abilityId)) return;

		void AddToRegistry(Dictionary<StringName, UnitMetadata> reg, string key)
		{
			if (reg.TryGetValue((StringName)key, out var meta))
			{
				var abilities = meta.Abilities != null
					? new List<string>(meta.Abilities)
					: new List<string>();
				if (!abilities.Contains(abilityId))
				{
					abilities.Add(abilityId);
					meta.Abilities = abilities.ToArray();
					reg[(StringName)key] = meta;
				}
			}
		}

		foreach (var candidateKey in GetNormalizedAbilityIdCandidates(unitTypeId))
		{
			AddToRegistry(UnitRegistry, candidateKey);
			AddToRegistry(BuildingRegistry, candidateKey);
		}
	}

	public float GetRegistryCollisionCircleRatio(string primaryKey)
	{
		if (string.IsNullOrEmpty(primaryKey)) return 1.0f;

		if (UnitRegistry.TryGetValue(primaryKey, out var meta) && meta.CollisionCircle > 0f) return meta.CollisionCircle;
		if (BuildingRegistry.TryGetValue(primaryKey, out var buildingMeta) && buildingMeta.CollisionCircle > 0f) return buildingMeta.CollisionCircle;
		if (ResourceRegistry.TryGetValue(primaryKey, out var resMeta) && resMeta.CollisionCircle > 0f) return resMeta.CollisionCircle;
		if (PropRegistry.TryGetValue(primaryKey, out var propMeta) && propMeta.CollisionCircle > 0f) return propMeta.CollisionCircle;

		return 1.0f;
	}

	public bool TryGetRegistryBrightness(string key, out float brightness)
	{
		brightness = 0f;
		if (UnitRegistry.TryGetValue(key, out var meta) && meta.Brightness > 0f)
		{
			brightness = meta.Brightness;
			return true;
		}

		if (BuildingRegistry.TryGetValue(key, out var buildingMeta) && buildingMeta.Brightness > 0f)
		{
			brightness = buildingMeta.Brightness;
			return true;
		}

		if (ResourceRegistry.TryGetValue(key, out var resMeta) && resMeta.Brightness > 0f)
		{
			brightness = resMeta.Brightness;
			return true;
		}

		if (PropRegistry.TryGetValue(key, out var propMeta) && propMeta.Brightness > 0f)
		{
			brightness = propMeta.Brightness;
			return true;
		}

		return false;
	}

	public bool TryGetRegistryColorTint(string key, out Color color)
	{
		color = default;
		if (UnitRegistry.TryGetValue(key, out var meta) && !string.IsNullOrEmpty(meta.Tint) && Color.HtmlIsValid(meta.Tint))
		{
			color = Color.FromHtml(meta.Tint);
			return true;
		}

		if (BuildingRegistry.TryGetValue(key, out var buildingMeta) && !string.IsNullOrEmpty(buildingMeta.Tint) && Color.HtmlIsValid(buildingMeta.Tint))
		{
			color = Color.FromHtml(buildingMeta.Tint);
			return true;
		}

		if (ResourceRegistry.TryGetValue(key, out var resMeta) && !string.IsNullOrEmpty(resMeta.Tint) && Color.HtmlIsValid(resMeta.Tint))
		{
			color = Color.FromHtml(resMeta.Tint);
			return true;
		}

		if (PropRegistry.TryGetValue(key, out var propMeta) && !string.IsNullOrEmpty(propMeta.Tint) && Color.HtmlIsValid(propMeta.Tint))
		{
			color = Color.FromHtml(propMeta.Tint);
			return true;
		}

		return false;
	}

	public void UpdateLuminanceInRegistries(string assetKey, bool normalizeLuminance)
	{
		if (UnitRegistry.TryGetValue(assetKey, out var unitMeta))
		{
			unitMeta.NormalizeLuminance = normalizeLuminance;
			UnitRegistry[assetKey] = unitMeta;
		}

		if (BuildingRegistry.TryGetValue(assetKey, out var bldMeta))
		{
			bldMeta.NormalizeLuminance = normalizeLuminance;
			BuildingRegistry[assetKey] = bldMeta;
		}

		if (ResourceRegistry.TryGetValue(assetKey, out var resMeta))
		{
			resMeta.NormalizeLuminance = normalizeLuminance;
			ResourceRegistry[assetKey] = resMeta;
		}

		if (PropRegistry.TryGetValue(assetKey, out var propMeta))
		{
			propMeta.NormalizeLuminance = normalizeLuminance;
			PropRegistry[assetKey] = propMeta;
		}
	}

	public bool TryGetRegistryDeathShader(string key, out string shader)
	{
		shader = "";
		if (UnitRegistry.TryGetValue(key, out var meta) && !string.IsNullOrWhiteSpace(meta.DeathShader))
		{
			shader = meta.DeathShader;
			return true;
		}

		if (BuildingRegistry.TryGetValue(key, out var bldMeta) && !string.IsNullOrWhiteSpace(bldMeta.DeathShader))
		{
			shader = bldMeta.DeathShader;
			return true;
		}

		if (ResourceRegistry.TryGetValue(key, out var resMeta) && !string.IsNullOrWhiteSpace(resMeta.DeathShader))
		{
			shader = resMeta.DeathShader;
			return true;
		}

		if (PropRegistry.TryGetValue(key, out var propMeta) && !string.IsNullOrWhiteSpace(propMeta.DeathShader))
		{
			shader = propMeta.DeathShader;
			return true;
		}

		return false;
	}

	public readonly Dictionary<StringName, UnitMetadata> UnitRegistry = new();
	public readonly Dictionary<StringName, UnitMetadata> BuildingRegistry = new();
	public readonly Dictionary<StringName, PropMetadata> PropRegistry = new();
	public readonly Dictionary<StringName, ResourceMetadata> ResourceRegistry = new();
	public readonly Dictionary<StringName, WeaponMetadata> WeaponRegistry = new();
	public readonly Dictionary<StringName, AttachmentMetadata> AttachmentRegistry = new();
	public readonly Dictionary<StringName, ItemMetadata> ItemRegistry = new();
	public readonly Dictionary<StringName, Realm.Shared.Metadata.UpgradeMetadata> UpgradeRegistry = new();
	public readonly Dictionary<StringName, Realm.Shared.Metadata.ConsumableMetadata> ConsumableRegistry = new();
	private readonly Dictionary<string, Realm.Shared.Metadata.ConsumableMetadata> _consumableByCard = new(StringComparer.OrdinalIgnoreCase);
	public Dictionary<string, VfxAttachmentConfig> VfxRegistry { get; set; } = new(StringComparer.OrdinalIgnoreCase);
	public Dictionary<string, AbilityDefinition> AbilityDefinitions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	public void RegisterConsumable(Realm.Shared.Metadata.ConsumableMetadata consumable)
	{
		if (consumable == null || string.IsNullOrWhiteSpace(consumable.TemplateID)) return;

		ConsumableRegistry[(StringName)consumable.TemplateID] = consumable;
		if (consumable.TemplateID.StartsWith("consumable/", StringComparison.OrdinalIgnoreCase))
		{
			ConsumableRegistry[(StringName)consumable.TemplateID.Substring(11)] = consumable;
		}

		_consumableByCard[consumable.CardId] = consumable;
		if (consumable.CardId.StartsWith("ability/", StringComparison.OrdinalIgnoreCase))
		{
			_consumableByCard[consumable.CardId.Substring(8)] = consumable;
		}
	}

	public Realm.Shared.Metadata.ConsumableMetadata? GetConsumable(string consumableId)
	{
		if (string.IsNullOrWhiteSpace(consumableId)) return null;
		return ConsumableRegistry.TryGetValue((StringName)consumableId, out var consumable) ? consumable : null;
	}

	public Realm.Shared.Metadata.ConsumableMetadata? GetConsumableForCard(string abilityId)
	{
		if (string.IsNullOrWhiteSpace(abilityId)) return null;
		if (_consumableByCard.TryGetValue(abilityId, out var consumable)) return consumable;

		int slash = abilityId.LastIndexOf('/');
		return slash >= 0 && _consumableByCard.TryGetValue(abilityId[(slash + 1)..], out consumable) ? consumable : null;
	}

	public void RegisterVfxConfig(string vfxId, VfxAttachmentConfig config)
	{
		if (string.IsNullOrWhiteSpace(vfxId) || config == null) return;

		VfxRegistry[vfxId] = config;

		var (_, slug) = TemplateIDHelper.ParseTemplateID(vfxId);
		if (!string.IsNullOrEmpty(slug))
		{
			VfxRegistry[slug] = config;
		}
	}

	public void UnregisterVfxConfig(string vfxId)
	{
		if (string.IsNullOrWhiteSpace(vfxId)) return;

		VfxRegistry.Remove(vfxId);

		var (_, slug) = TemplateIDHelper.ParseTemplateID(vfxId);
		if (!string.IsNullOrEmpty(slug))
		{
			VfxRegistry.Remove(slug);
			VfxRegistry.Remove("vfx/" + slug);
		}
	}

	public bool TryGetVfxConfig(string vfxId, out VfxAttachmentConfig config)
	{
		config = null;
		if (string.IsNullOrWhiteSpace(vfxId)) return false;

		if (VfxRegistry.TryGetValue(vfxId, out config) && config != null) return true;

		string normalizedId = vfxId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
			? vfxId.Substring(4)
			: vfxId;

		if (!string.Equals(normalizedId, vfxId, StringComparison.OrdinalIgnoreCase) &&
		    VfxRegistry.TryGetValue(normalizedId, out config) && config != null)
		{
			return true;
		}

		var (_, slug) = TemplateIDHelper.ParseTemplateID(normalizedId);
		if (!string.IsNullOrEmpty(slug) && VfxRegistry.TryGetValue(slug, out config) && config != null) return true;

		string fullId = TemplateIDHelper.NormalizeTemplateID("vfx", normalizedId);
		return VfxRegistry.TryGetValue(fullId, out config) && config != null;
	}
	public Dictionary<(int UnitUniqueId, string AbilityId), (bool Disabled, bool Hidden)> UnitAbilityStates { get; set; } = new();

	public IReadOnlyList<(string DefinitionKey, float Radius)> PlacementRadiusRules => Rules.PlacementRadiusRules;
	public IReadOnlyList<string> ResourceDropoffBuildingKeys => Rules.ResourceDropoffBuildingKeys;
	public IReadOnlyList<string> ResourceWorkerUnitKeys => Rules.ResourceWorkerUnitKeys;
	public IReadOnlyList<string> PrimaryBuildingIds => Rules.PrimaryBuildingIds;
	public IReadOnlyDictionary<string, string> ResourceNodeTypes => Rules.ResourceNodeTypes;

	public void ApplyCatalogEntry(string channel, string key, string value, float number)
	{
		if (string.IsNullOrEmpty(key)) return;

		if (channel == "string")
		{
			if (int.TryParse(key, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int stringId))
			{
				StringRegistry.RegisterRemote(stringId, value);
			}

			return;
		}

		Rules.ApplyCatalogEntry(channel, key, value, number);
	}

	public HashSet<(int UnitUniqueId, string AbilityId)> DirtyAbilityStates { get; } = new();
	private readonly List<(int UnitUniqueId, string AbilityId)> _abilityStateKeysToClear = new();

	public void ClearAbilityStateDirty()
	{
		DirtyAbilityStates.Clear();
	}

	public void ClearAbilityStateForUnit(UnitId unit)
	{
		if (!unit.IsValid) return;

		_abilityStateKeysToClear.Clear();
		foreach (var key in UnitAbilityStates.Keys)
		{
			if (key.UnitUniqueId == unit.Value) _abilityStateKeysToClear.Add(key);
		}

		for (int index = 0; index < _abilityStateKeysToClear.Count; index++)
		{
			var key = _abilityStateKeysToClear[index];
			UnitAbilityStates.Remove(key);
			DirtyAbilityStates.Remove(key);
		}
	}
	public Dictionary<(int UnitUniqueId, string AbilityId), float> UnitAbilityManaCosts { get; set; } = new();
	public Dictionary<string, string> ItemTooltips { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	public TooltipValueStore TooltipValues { get; } = new();

	public bool CheckPropRegistryForMatch(string id, string cleanId, string normTarget, Func<string, string, bool> isMatch)
	{
		if (PropRegistry.TryGetValue(id, out var meta) && isMatch(meta.ModelPath, normTarget)) return true;
		if (!string.IsNullOrEmpty(cleanId) && PropRegistry.TryGetValue(cleanId, out meta) && isMatch(meta.ModelPath, normTarget)) return true;
		return false;
	}

	public bool MatchesUnitKey(string unitId, string modelPath, string normTarget, Func<string, string, bool> isMatch)
	{
		if (isMatch(unitId, normTarget)) return true;
		if (isMatch(modelPath, normTarget)) return true;
		if (UnitRegistry.TryGetValue(unitId, out var meta) && isMatch(meta.ModelPath, normTarget)) return true;
		if (BuildingRegistry.TryGetValue(unitId, out var bldMeta) && isMatch(bldMeta.ModelPath, normTarget)) return true;
		return false;
	}

	public bool IsPropOrResourceKey(string key, string norm, string resolved, Func<string, string> normalizeFunc)
	{
		if (PropRegistry.ContainsKey(key) || PropRegistry.ContainsKey(norm)) return true;
		if (ResourceRegistry.ContainsKey(key) || ResourceRegistry.ContainsKey(norm)) return true;

		if (HasMatchingPropOrResourceMetadata(norm, normalizeFunc)) return true;
		if (MatchesPropOrResourcePathPattern(key) || MatchesPropOrResourcePathPattern(norm)) return true;

		return !string.IsNullOrEmpty(resolved) && MatchesPropOrResourcePathPattern(resolved);
	}

	public bool HasMatchingPropOrResourceMetadata(string norm, Func<string, string> normalizeFunc)
	{
		return HasMatchingPropMetadata(norm, normalizeFunc) || HasMatchingResourceMetadata(norm, normalizeFunc);
	}

	public bool HasMatchingPropMetadata(string norm, Func<string, string> normalizeFunc)
	{
		foreach (var propMeta in PropRegistry.Values)
		{
			if (!string.IsNullOrEmpty(propMeta.ModelPath) && normalizeFunc(propMeta.ModelPath) == norm) return true;
			if (!string.IsNullOrEmpty(propMeta.TemplateID) && normalizeFunc(propMeta.TemplateID) == norm) return true;
		}

		return false;
	}

	public bool HasMatchingResourceMetadata(string norm, Func<string, string> normalizeFunc)
	{
		foreach (var resMeta in ResourceRegistry.Values)
		{
			if (!string.IsNullOrEmpty(resMeta.ModelPath) && normalizeFunc(resMeta.ModelPath) == norm) return true;
			if (!string.IsNullOrEmpty(resMeta.TemplateID) && normalizeFunc(resMeta.TemplateID) == norm) return true;
		}

		return false;
	}

	public bool MatchesPropOrResourcePathPattern(string path)
	{
		return path.Contains("/props/", StringComparison.OrdinalIgnoreCase) ||
		       path.Contains("\\props\\", StringComparison.OrdinalIgnoreCase) ||
		       path.StartsWith("props/", StringComparison.OrdinalIgnoreCase) ||
		       path.Contains("/resources/", StringComparison.OrdinalIgnoreCase) ||
		       path.Contains("\\resources\\", StringComparison.OrdinalIgnoreCase) ||
		       path.StartsWith("resources/", StringComparison.OrdinalIgnoreCase);
	}

	public bool CheckBuildingRegistryForMatch(string id, string cleanId, string normTarget, Func<string, string, bool> isMatch)
	{
		if (BuildingRegistry.TryGetValue(id, out var meta) && isMatch(meta.ModelPath, normTarget)) return true;
		if (!string.IsNullOrEmpty(cleanId) && BuildingRegistry.TryGetValue(cleanId, out meta) && isMatch(meta.ModelPath, normTarget)) return true;
		return false;
	}

	public bool TryGetRegistryYOffset(string key, out float offset)
	{
		offset = 0f;
		if (UnitRegistry.TryGetValue(key, out var meta) && meta.YOffset != 0f)
		{
			offset = meta.YOffset;
			return true;
		}

		if (BuildingRegistry.TryGetValue(key, out var bldMeta) && bldMeta.YOffset != 0f)
		{
			offset = bldMeta.YOffset;
			return true;
		}

		if (ResourceRegistry.TryGetValue(key, out var resMeta) && resMeta.YOffset != 0f)
		{
			offset = resMeta.YOffset;
			return true;
		}

		if (PropRegistry.TryGetValue(key, out var propMeta) && propMeta.YOffset != 0f)
		{
			offset = propMeta.YOffset;
			return true;
		}

		return false;
	}

	public bool TryGetRegistryScale(string key, out float scale)
	{
		scale = 1.0f;
		if (UnitRegistry.TryGetValue(key, out var meta) && meta.Scale > 0f)
		{
			scale = meta.Scale;
			return true;
		}

		if (BuildingRegistry.TryGetValue(key, out var bldMeta) && bldMeta.Scale > 0f)
		{
			scale = bldMeta.Scale;
			return true;
		}

		if (ResourceRegistry.TryGetValue(key, out var resMeta) && resMeta.Scale > 0f)
		{
			scale = resMeta.Scale;
			return true;
		}

		if (PropRegistry.TryGetValue(key, out var propMeta) && propMeta.Scale > 0f)
		{
			scale = propMeta.Scale;
			return true;
		}

		return false;
	}

	public float GetScaleFromRegistryKey(string key)
	{
		if (string.IsNullOrEmpty(key)) return -1f;

		if (ResourceRegistry.ContainsKey(key)) return 2.75f;
		if (BuildingRegistry.ContainsKey(key)) return 1.5f;
		if (PropRegistry.ContainsKey(key)) return 1.25f;
		if (UnitRegistry.ContainsKey(key)) return 1.0f;

		return -1f;
	}


	public void TryAddResourceNodeComponent(Entity entity, string propId, string cleanId, float defaultAmount)
	{
		ResourceMetadata? metadata = null;
		if (!ResourceRegistry.TryGetValue(propId, out metadata) && !string.IsNullOrEmpty(cleanId))
		{
			ResourceRegistry.TryGetValue(cleanId, out metadata);
		}

		PropEntityFactory.AddResourceNode(GameContext.World, entity, metadata, defaultAmount);
	}


	private AbilityDefinition GetOrCreateAbilityDefinition(string abilityId)
	{
		AbilityDefinition? def = null;
		foreach (var key in GetNormalizedAbilityIdCandidates(abilityId))
		{
			if (AbilityDefinitions.TryGetValue(key, out def) && def != null)
			{
				break;
			}
		}

		if (def == null)
		{
			def = new AbilityDefinition { Id = abilityId };
		}

		foreach (var key in GetNormalizedAbilityIdCandidates(abilityId))
		{
			AbilityDefinitions[key] = def;
		}

		return def;
	}

	public bool RegisterAbility(string abilityId, string displayName, string tooltip, string iconPath, bool isInstant)
	{
		if (string.IsNullOrEmpty(abilityId)) return false;

		var def = GetOrCreateAbilityDefinition(abilityId);
		def.DisplayName = displayName ?? "";
		def.TooltipTemplate = tooltip ?? "";
		if (!string.IsNullOrEmpty(iconPath)) def.IconPath = iconPath;
		def.IsInstant = isInstant;

		return true;
	}

	public void SetAbilityInstant(string abilityId, bool isInstant)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		var def = GetOrCreateAbilityDefinition(abilityId);
		def.IsInstant = isInstant;
	}

	public void SetAbilityIcon(string abilityId, string iconPath)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		var def = GetOrCreateAbilityDefinition(abilityId);
		def.IconPath = iconPath ?? "";
	}

	public void SetAbilityHotkey(string abilityId, string hotkey)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		var def = GetOrCreateAbilityDefinition(abilityId);
		if (string.IsNullOrWhiteSpace(hotkey))
		{
			def.Hotkey = "";
			return;
		}

		if (!HotkeySpec.TryParse(hotkey, out _, out string errorMessage))
		{
			GD.PrintErr($"[RegistryService] Rejected hotkey '{hotkey}' for ability '{abilityId}': {errorMessage}");
			return;
		}

		def.Hotkey = hotkey.Trim();
	}

	public void SetAbilityState(UnitId unit, string abilityId, bool disabled, bool hidden)
	{
		if (!unit.IsValid || string.IsNullOrEmpty(abilityId)) return;
		foreach (var key in GetNormalizedAbilityIdCandidates(abilityId))
		{
			UnitAbilityStates[(unit.Value, key)] = (disabled, hidden);
			DirtyAbilityStates.Add((unit.Value, key));
		}
		if (GameContext.Host != null)
		{
			Callable.From(() => GameContext.InGameHud?.RefreshUI(GameContext.Host.SelectedUnits)).CallDeferred();
		}
	}

	public void SetAbilityState(Unit_WasmRuntime unit, string abilityId, bool disabled, bool hidden)
	{
		if (unit != null) SetAbilityState((UnitId)unit.UniqueId, abilityId, disabled, hidden);
	}

	public bool IsAbilityDisabledForUnit(UnitId unit, string abilityId)
	{
		if (unit.IsValid && !string.IsNullOrEmpty(abilityId))
		{
			foreach (var key in GetNormalizedAbilityIdCandidates(abilityId))
			{
				if (UnitAbilityStates.TryGetValue((unit.Value, key), out var state))
				{
					return state.Disabled;
				}
			}
		}

		return false;
	}

	public bool IsAbilityDisabledForUnit(Unit_WasmRuntime unit, string abilityId)
	{
		return unit != null && IsAbilityDisabledForUnit((UnitId)unit.UniqueId, abilityId);
	}

	public bool IsAbilityHiddenForUnit(UnitId unit, string abilityId)
	{
		if (unit.IsValid && !string.IsNullOrEmpty(abilityId))
		{
			foreach (var key in GetNormalizedAbilityIdCandidates(abilityId))
			{
				if (UnitAbilityStates.TryGetValue((unit.Value, key), out var state))
				{
					return state.Hidden;
				}
			}
		}

		return false;
	}

	public bool IsAbilityHiddenForUnit(Unit_WasmRuntime unit, string abilityId)
	{
		return unit != null && IsAbilityHiddenForUnit((UnitId)unit.UniqueId, abilityId);
	}

	public void SetAbilityTooltip(string abilityId, string tooltip)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		var def = GetOrCreateAbilityDefinition(abilityId);
		def.TooltipTemplate = tooltip ?? "";
	}

	public AbilityDefinition GetAbilityDefinition(string abilityId)
	{
		if (string.IsNullOrEmpty(abilityId)) return null;
		foreach (var key in GetNormalizedAbilityIdCandidates(abilityId))
		{
			if (AbilityDefinitions.TryGetValue(key, out var def) && def != null)
			{
				return def;
			}
		}
		return null;
	}

	public IReadOnlyList<string> GetUnitTypeAbilities(string unitTypeId)
	{
		if (string.IsNullOrEmpty(unitTypeId)) return Array.Empty<string>();

		if (UnitRegistry.TryGetValue((StringName)unitTypeId, out var meta) && meta.Abilities != null)
			return meta.Abilities;

		if (BuildingRegistry.TryGetValue((StringName)unitTypeId, out var bldMeta) && bldMeta.Abilities != null)
			return bldMeta.Abilities;

		string cleanId = Path.GetFileNameWithoutExtension(unitTypeId);
		if (UnitRegistry.TryGetValue((StringName)cleanId, out meta) && meta.Abilities != null)
			return meta.Abilities;

		if (BuildingRegistry.TryGetValue((StringName)cleanId, out bldMeta) && bldMeta.Abilities != null)
			return bldMeta.Abilities;

		return Array.Empty<string>();
	}

	public string GetUnitName(string unitTypeId)
	{
		if (string.IsNullOrEmpty(unitTypeId)) return string.Empty;

		if (UnitRegistry.TryGetValue((StringName)unitTypeId, out var meta) && !string.IsNullOrWhiteSpace(meta.Name))
			return meta.Name;

		if (BuildingRegistry.TryGetValue((StringName)unitTypeId, out var bldMeta) && !string.IsNullOrWhiteSpace(bldMeta.Name))
			return bldMeta.Name;

		string cleanId = Path.GetFileNameWithoutExtension(unitTypeId);
		if (UnitRegistry.TryGetValue((StringName)cleanId, out meta) && !string.IsNullOrWhiteSpace(meta.Name))
			return meta.Name;

		if (BuildingRegistry.TryGetValue((StringName)cleanId, out bldMeta) && !string.IsNullOrWhiteSpace(bldMeta.Name))
			return bldMeta.Name;

		return cleanId;
	}

	public string GetAbilityName(string abilityId)
	{
		if (string.IsNullOrEmpty(abilityId)) return string.Empty;

		var def = GetAbilityDefinition(abilityId);
		if (def != null && !string.IsNullOrWhiteSpace(def.DisplayName))
			return def.DisplayName;

		return Path.GetFileNameWithoutExtension(abilityId);
	}

	private static string BuildAbilityTooltip(AbilityMetadata meta)
	{
		string description = meta.Description ?? string.Empty;
		return string.IsNullOrWhiteSpace(meta.Role) ? description : $"[{meta.Role.Trim()}] {description}";
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
				TooltipTemplate = BuildAbilityTooltip(meta),
				IconPath = meta.IconPath ?? "",
				IsInstant = string.Equals(meta.AbilityType, "instant_spell", StringComparison.OrdinalIgnoreCase),
				AutoCastEnabled = meta.AutoCast?.Enabled ?? true,
				AutoCastRequiresEnemy = meta.AutoCast?.RequiresEnemyTarget ?? true,
				AutoCastAllyHealthPercentBelow = meta.AutoCast?.AllyHealthPercentBelow ?? 1f,
				AutoCastManaReserve = meta.AutoCast?.ManaReserve ?? 0f,
				AutoCastMinManaPercent = meta.AutoCast?.MinManaPercent ?? 0f,
				ManaCost = meta.ManaCost,
				Cooldown = meta.Cooldown,
				TargetRange = meta.TargetRange,
				AreaOfEffectRadius = meta.AreaOfEffectRadius,
				VisualEffect = meta.VisualEffect,
				CastSound = meta.CastSound,
				GridX = meta.GridX,
				GridY = meta.GridY,
				Hotkey = meta.Hotkey ?? ""
			};

			def.Effects = AbilityEffectFactory.Create(meta, meta.AreaOfEffectRadius, out float maxDamage, out float maxHealing);
			def.Damage = maxDamage;
			def.Healing = maxHealing;

			foreach (var key in GetNormalizedAbilityIdCandidates(meta.TemplateID))
			{
				AbilityDefinitions[key] = def;
			}
		}
	}

	public void SetAbilityGridPosition(string abilityId, int x, int y)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		var def = GetOrCreateAbilityDefinition(abilityId);
		def.GridX = x;
		def.GridY = y;
	}

	public void SetAbilityManaCost(UnitId unit, string abilityId, float manaCost)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		var def = GetOrCreateAbilityDefinition(abilityId);
		def.ManaCost = manaCost;
		if (unit.IsValid)
		{
			foreach (var key in GetNormalizedAbilityIdCandidates(abilityId))
			{
				UnitAbilityManaCosts[(unit.Value, key)] = manaCost;
			}
		}
	}

	public void SetAbilityManaCost(Unit_WasmRuntime unit, string abilityId, float manaCost)
	{
		if (unit != null) SetAbilityManaCost((UnitId)unit.UniqueId, abilityId, manaCost);
	}

	public float GetAbilityManaCostForUnit(UnitId unit, string abilityId)
	{
		if (unit.IsValid && !string.IsNullOrEmpty(abilityId))
		{
			foreach (var key in GetNormalizedAbilityIdCandidates(abilityId))
			{
				if (UnitAbilityManaCosts.TryGetValue((unit.Value, key), out float cost))
				{
					return cost;
				}
			}
		}

		var def = GetAbilityDefinition(abilityId);
		return def?.ManaCost ?? 0f;
	}

	public float GetAbilityManaCostForUnit(Unit_WasmRuntime unit, string abilityId)
	{
		return unit != null ? GetAbilityManaCostForUnit((UnitId)unit.UniqueId, abilityId) : 0f;
	}

	public void SetItemTooltip(string itemId, string tooltip)
	{
		if (string.IsNullOrEmpty(itemId)) return;
		ItemTooltips[itemId] = tooltip ?? "";
		if (ItemRegistry.TryGetValue(itemId, out var itemMeta))
		{
			itemMeta.Description = tooltip ?? "";
		}
	}

	public void SaveUnitObjectAttachment(string unitId, HumanoidBone hand, string attachmentId, HandAttachmentOrientation orientation, string wsPath)
	{
		string handKey = hand switch
		{
			HumanoidBone.LeftHand => "left_hand",
			HumanoidBone.RightHand => "right_hand",
			HumanoidBone.Chest => "chest",
			HumanoidBone.Hips => "root",
			HumanoidBone.Head => "head",
			HumanoidBone.LeftFoot => "left_foot",
			HumanoidBone.RightFoot => "right_foot",
			_ => "right_hand"
		};
		SaveUnitObjectAttachment(unitId, handKey, attachmentId, orientation, wsPath);
	}


	public void SaveUnitObjectAttachment(string unitId, string socket, string attachmentId, HandAttachmentOrientation orientation, string wsPath = null)
	{
		if (string.IsNullOrEmpty(unitId) || string.IsNullOrEmpty(attachmentId)) return;

		var (regKey, isBuildingMeta) = ResolveRegistryKeyAndType(unitId);

		UpdateLocalRegistry(regKey, isBuildingMeta, socket, attachmentId, orientation);

		UpdateMetadataFile(unitId, regKey, socket, attachmentId, orientation, wsPath);

		ApplyAttachmentsToGameInstances(unitId, regKey);
	}


	public (string regKey, bool isBuildingMeta) ResolveRegistryKeyAndType(string unitId)
	{
		string regKey = unitId;
		bool isBuildingMeta = false;

		if (GameHost.UnitRegistry.ContainsKey(unitId)) return (regKey, isBuildingMeta);
		if (GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.ContainsKey(unitId)) return (regKey, true);

		string cleanId = Path.GetFileNameWithoutExtension(unitId);
		if (GameHost.UnitRegistry.ContainsKey(cleanId)) return (cleanId, false);
		if (GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.ContainsKey(cleanId)) return (cleanId, true);

		return ResolveRegistryKeyByIterating(unitId, cleanId);
	}


	public (string regKey, bool isBuildingMeta) ResolveRegistryKeyByIterating(string unitId, string cleanId)
	{
		string regKey = unitId;
		bool isBuildingMeta = false;

		foreach (var k in GameHost.UnitRegistry.Keys)
		{
			string kStr = k.ToString();
			if (kStr.Equals(unitId, StringComparison.OrdinalIgnoreCase) ||
			    kStr.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
			    Path.GetFileNameWithoutExtension(kStr).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
			{
				return (k.ToString(), false);
			}
		}

		if (GameHost.BuildingRegistry != null)
		{
			foreach (var k in GameHost.BuildingRegistry.Keys)
			{
				string kStr = k.ToString();
				if (kStr.Equals(unitId, StringComparison.OrdinalIgnoreCase) ||
				    kStr.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
				    Path.GetFileNameWithoutExtension(kStr).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
				{
					return (k.ToString(), true);
				}
			}
		}

		return (regKey, isBuildingMeta);
	}


	public void UpdateLocalRegistry(string regKey, bool isBuildingMeta, string socket, string attachmentId, HandAttachmentOrientation orientation)
	{
		if (isBuildingMeta && GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.TryGetValue(regKey, out var bMeta))
		{
			bMeta.SetObjectAttachment(socket, attachmentId, orientation);
			GameHost.BuildingRegistry[regKey] = bMeta;
		}
		else if (GameHost.UnitRegistry.TryGetValue(regKey, out var uMeta))
		{
			uMeta.SetObjectAttachment(socket, attachmentId, orientation);
			GameHost.UnitRegistry[regKey] = uMeta;
		}
	}


	public void UpdateMetadataFile(string unitId, string regKey, string socket, string attachmentId, HandAttachmentOrientation orientation, string wsPath = null)
	{
		string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
		if (!File.Exists(metadataPath)) return;

		GameContext.MetadataService.UpdateMetadata(wsPath, meta =>
		{
			bool updated = meta.UpdateUnit(unitId, u =>
			{
				u.SetObjectAttachment(socket, attachmentId, orientation);
				return u;
			});
			if (!updated)
				updated = meta.UpdateBuilding(unitId, b =>
				{
					b.SetObjectAttachment(socket, attachmentId, orientation);
					return b;
				});
			if (!updated)
				updated = meta.UpdateUnit(regKey, u =>
				{
					u.SetObjectAttachment(socket, attachmentId, orientation);
					return u;
				});
			if (!updated)
				meta.UpdateBuilding(regKey, b =>
				{
					b.SetObjectAttachment(socket, attachmentId, orientation);
					return b;
				});
		});

				GameContext.EditorService.LastMetadataSyncTime = GameContext.EditorService.GetLastWriteTimeSafe(metadataPath);
	}


	public void ApplyAttachmentsToGameInstances(string unitId, string regKey)
	{
		if (GameContext.Host == null) return;

		if (GameContext.Host.AllUnits != null)
		{
			foreach (var u in GameContext.Host.AllUnits)
			{
				if (u != null && IsMatchingUnit(u.UnitId, unitId, regKey))
				{
					u.ApplyAllConfiguredAttachments();
				}
			}
		}

		if (GameContext.Host.SelectedEditorObject is Unit3D selUnit)
		{
			if (IsMatchingUnit(selUnit.UnitId, unitId, regKey))
			{
				selUnit.ApplyAllConfiguredAttachments();
			}
		}
	}


	public bool IsMatchingUnit(string instanceUnitId, string unitId, string regKey)
	{
		return instanceUnitId == unitId ||
		       instanceUnitId == regKey ||
		       Path.GetFileNameWithoutExtension(instanceUnitId ?? "").Equals(Path.GetFileNameWithoutExtension(unitId), StringComparison.OrdinalIgnoreCase);
	}


	public void SaveAllUnitObjectAttachments(string targetId, UnitObjectAttachments? attachments, Action<string, UnitObjectAttachments?> restoreCallback)
	{
		restoreCallback?.Invoke(targetId, attachments);
	}

	public void SaveUnitPseudoSockets(string targetId, List<PseudoSocketAnchor>? pseudoSockets, Action<string, List<PseudoSocketAnchor>?> restoreCallback)
	{
		restoreCallback?.Invoke(targetId, pseudoSockets);
	}


	public void SaveCustomWeaponToMetadata(string weaponId, WeaponMetadata weapon, string wsPath)
	{
		string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
		if (!File.Exists(metadataPath)) return;

		GameContext.MetadataService.UpdateMetadata(wsPath, meta => { meta.AddOrUpdateWeapon(weapon); });
		GameContext.EditorService.LastMetadataSyncTime = GameContext.EditorService.GetLastWriteTimeSafe(metadataPath);
	}

	public void SaveCustomVfxToMetadata(string vfxId, VfxAttachmentConfig config, string wsPath)
	{
		if (string.IsNullOrEmpty(vfxId) || config == null) return;

		RegisterVfxConfig(vfxId, config.Clone());

		string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
		if (!File.Exists(metadataPath)) return;

		GameContext.MetadataService.UpdateMetadata(wsPath, meta => { meta.AddOrUpdateVfx(config); });
		GameContext.EditorService.LastMetadataSyncTime = GameContext.EditorService.GetLastWriteTimeSafe(metadataPath);
	}

	public void RemoveCustomVfxFromMetadata(string vfxId, string wsPath)
	{
		if (string.IsNullOrEmpty(vfxId)) return;

		if (VfxRegistry != null)
		{
			UnregisterVfxConfig(vfxId);
		}

		string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
		if (!File.Exists(metadataPath)) return;

		GameContext.MetadataService.UpdateMetadata(wsPath, meta => { meta.RemoveVfx(vfxId); });
		GameContext.EditorService.LastMetadataSyncTime = GameContext.EditorService.GetLastWriteTimeSafe(metadataPath);
	}

	public void ResetAbilityCatalog()
	{
		AbilityDefinitions.Clear();
	}

	public void ClearAll()
	{
		UnitRegistry.Clear();
		BuildingRegistry.Clear();
		PropRegistry.Clear();
		ResourceRegistry.Clear();
		WeaponRegistry.Clear();
		AttachmentRegistry.Clear();
		ItemRegistry.Clear();
		UpgradeRegistry.Clear();
		ConsumableRegistry.Clear();
		_consumableByCard.Clear();
		VfxRegistry.Clear();
		AbilityDefinitions.Clear();
		UnitAbilityStates.Clear();
		UnitAbilityManaCosts.Clear();
		ItemTooltips.Clear();
		TooltipValues.Clear();
	}
}