using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using Realm.Shared.Maps;
using Realm.Shared.Metadata;
using Realm.Shared.Network;

namespace Realm.ListenServer;

public class ServerTechLevels
{
	private static readonly QueryDescription OwnedUnitsQuery = new QueryDescription().WithAll<Owner>().WithNone<Dead>();

	private readonly SimulationContext _context;
	private readonly LoadedMap _map;
	private readonly Dictionary<(int PlayerIndex, string TechId), int> _levels = new();
	private readonly List<(int PlayerIndex, string TechId, int Level)> _changes = new();
	private int _appliedPlayerIndex;
	private string _appliedTechId = string.Empty;
	private int _appliedLevel;

	public ServerTechLevels(SimulationContext context, LoadedMap map)
	{
		_context = context;
		_map = map;
		_applyLevelToOwnedUnit = ApplyLevelToOwnedUnit;
	}

	private readonly EntityCallback<Owner> _applyLevelToOwnedUnit;

	public int Get(int playerIndex, string techId)
	{
		if (string.IsNullOrEmpty(techId)) return 0;
		return _levels.TryGetValue((playerIndex, techId), out int level) ? level : 0;
	}

	public void Set(int playerIndex, string techId, int level)
	{
		if (string.IsNullOrEmpty(techId)) return;

		var key = (playerIndex, techId);
		if (_levels.TryGetValue(key, out int existing) && existing == level) return;

		_levels[key] = level;
		ApproveLevel(playerIndex, techId, level);
		_changes.Add((playerIndex, techId, level));
	}

	public void Add(int playerIndex, string techId, int delta)
	{
		if (string.IsNullOrEmpty(techId)) return;

		Set(playerIndex, techId, Get(playerIndex, techId) + delta);
	}

	public IReadOnlyList<(int PlayerIndex, string TechId, int Level)> ConsumeChanges()
	{
		_changes.Clear();
		return _changes;
	}

	public void AppendDescription(List<CatalogEntry> entries)
	{
		foreach (var entry in _levels)
		{
			if (entry.Value == 0) continue;

			entries.Add(new CatalogEntry
			{
				Channel = TechLevelChannel,
				Key = $"{entry.Key.PlayerIndex}|{entry.Key.TechId}",
				Number = entry.Value
			});
		}
	}

	public const string TechLevelChannel = "tech_level";

	private void ApproveLevel(int playerIndex, string techId, int level)
	{
		UpgradeMetadata? upgrade = _map.Catalog.FindUpgrade(techId);
		if (upgrade == null || upgrade.Modifiers.Count == 0) return;

		_appliedPlayerIndex = playerIndex;
		_appliedTechId = techId;
		_appliedLevel = level;
		_context.World.Query(in OwnedUnitsQuery, _applyLevelToOwnedUnit);
	}

	private void ApplyLevelToOwnedUnit(Entity entity, ref Owner owner)
	{
		if (_context.PlayerSlotForEntity(owner.PlayerEntity.Value) != _appliedPlayerIndex) return;
		if (!IsAffectedByUpgrade(entity, _appliedTechId)) return;

		UpgradeMetadata? upgrade = _map.Catalog.FindUpgrade(_appliedTechId);
		if (upgrade == null) return;

		if (!_context.World.Has<ModifierState>(entity)) _context.World.Add(entity, new ModifierState(null));

		ref ModifierState modifiers = ref _context.World.Get<ModifierState>(entity);
		for (int index = modifiers.Count - 1; index >= 0; index--)
		{
			if (string.Equals(modifiers.Get(index).SourceUpgradeId, _appliedTechId, StringComparison.OrdinalIgnoreCase)) modifiers.RemoveAt(index);
		}

		if (_appliedLevel > 0) AddLevelModifiers(ref modifiers, upgrade, _appliedLevel);
		AttributeStatCalculator.RecalculateEntityStats(_context.World, entity);
	}

	private static void AddLevelModifiers(ref ModifierState modifiers, UpgradeMetadata upgrade, int level)
	{
		for (int index = 0; index < upgrade.Modifiers.Count; index++)
		{
			StatModifierDefinition definition = upgrade.Modifiers[index];
			if (string.IsNullOrWhiteSpace(definition.StatKey)) continue;

			StatKeys.WarnIfUnknown($"upgrade '{upgrade.TemplateID}'", definition.StatKey);
			var modifierType = Enum.TryParse(definition.Type, out ModifierType parsedType) ? parsedType : ModifierType.Flat;

			modifiers.Add(new StatModifier(
				statTypeId: new StatId(definition.StatKey),
				type: modifierType,
				value: definition.Value + ((level - 1) * definition.PerLevelScaling),
				duration: -1f,
				wasmFunctionName: definition.WASMFunctionName,
				sourceLevel: level,
				sourceUpgradeId: upgrade.TemplateID));
		}
	}

	private bool IsAffectedByUpgrade(Entity entity, string techId)
	{
		string[]? affected = _map.Catalog.FindUpgrade(techId)?.AffectedUnitIds;
		if (affected == null || affected.Length == 0) return true;
		if (!_context.World.TryGet<DefinitionId>(entity, out DefinitionId definition)) return false;

		for (int index = 0; index < affected.Length; index++)
		{
			if (string.Equals(affected[index], definition.Value, StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
	}

}
