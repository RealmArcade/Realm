using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GdUnit4;
using Realm.Client;
using Realm.Ecs;
using Realm.Ecs.Replication;

namespace Realm.Client.Tests;

[TestSuite]
public partial class ReplicationContractTests
{
	private static readonly string[] HostOnlyComponents =
	{
		"Position", "PreviousPosition", "Health", "Owner",
		"ManaRegen", "CollisionRadius", "CollisionScale", "Cooldowns",
		"SpellCooldowns", "DefinitionId", "CustomMetadata", "CombatAlertState",
		"CountdownState", "DecalIdentity", "DecalSpawnRequest", "EditorState",
		"GroundLoot", "InputState", "Inventory", "LeaderboardState",
		"ModifierState", "NetworkMappingState", "NetworkState", "OccupiedZones",
		"ProductionQueue", "PropIdentity", "PropSpawnRequest", "RallyPoint",
		"ReplayState", "ScriptPlayer", "ScriptPlayersState", "ScriptZonesState", "SpectatorPerspective",
		"SummaryTableState", "TowerUpgradeLevel", "UnitOwnerPlayer", "UnitSpawnRequest",
		"VFXQueue", "VFXRequest", "ZoneBounds",
		"AutoCombatState", "CombatTargeting", "Weapon", "Weapons",
		"TargetIntel", "Ability", "HasAbilities", "AbilityState",
		"AutoCastState", "CastTarget", "BuffState", "Buffs",
		"MoveTo", "Velocity", "AttackMove", "Follow",
		"HoldPosition", "InterpolationTarget", "RenderPosition", "PathingFlags",
		"Patrol", "WaypointBuffer", "WaypointQueue", "PlayerResources",
		"Gatherer", "ResourceNode", "ResourceCost", "BuildQueue",
		"BuildTask", "ConstructionState", "Food", "Gold",
		"Stone", "Wood", "ArmorStat", "AttackDamage",
		"AttackSpeed", "DerivedCombatStats", "HealthStat", "MovementSpeed",
		"StatModifier", "Stats", "UnitAttributes", "UnitBaseStats",
		"Attackable", "Building", "BypassPopulationTag", "Caster",
		"Dead", "Hero", "Movable", "Player",
		"Prop", "Unit", "Icon", "Model",
		"Name", "ReplayAnimationState", "Rotation3D", "RotationY",
		"PreviousRotationY", "ShroudState", "TerrainCell", "TerrainColorsState",
		"TerrainState", "TerrainSwatchConfig", "WeatherState",
		"PlayerRelationshipMatrix", "PlayerUpgrades", "AttackTarget", "HealingTarget",
		"LastAttacker", "ScanRadius", "AbilityTargetFilter", "CameraState", "BuildingSpawnOffset"
	};

	private static readonly string[] StorageStructs =
	{
		"BuffSlot", "BuffSlotArray", "InventorySlot", "InventorySlotArray",
		"ModifierSlotArray", "PlayerSlotArray", "StatSlot", "StatSlotArray",
		"VFXSlotArray", "OccupiedZoneMask", "CameraLocationSlot"
	};

	private const int MinimumComponentTypeCount = 120;

	[TestCase]
	public void EveryEcsComponentIsClassified()
	{
		var replicatedNames = new HashSet<string>(StringComparer.Ordinal);
		foreach (var codec in ReplicatedComponents.All)
		{
			replicatedNames.Add(codec.ComponentName);
		}

		var hostOnlyNames = new HashSet<string>(HostOnlyComponents, StringComparer.Ordinal);
		var storageNames = new HashSet<string>(StorageStructs, StringComparer.Ordinal);

		var duplicateHostOnly = HostOnlyComponents.GroupBy(name => name).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
		var duplicateStorage = StorageStructs.GroupBy(name => name).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
		Assertions.AssertThat(duplicateHostOnly).OverrideFailureMessage($"Duplicate host-only component entries: {string.Join(", ", duplicateHostOnly)}").IsEmpty();
		Assertions.AssertThat(duplicateStorage).OverrideFailureMessage($"Duplicate storage struct entries: {string.Join(", ", duplicateStorage)}").IsEmpty();

		var overlaps = replicatedNames.Intersect(hostOnlyNames)
			.Concat(replicatedNames.Intersect(storageNames))
			.Concat(hostOnlyNames.Intersect(storageNames))
			.ToList();
		Assertions.AssertThat(overlaps).OverrideFailureMessage($"Component classified more than once: {string.Join(", ", overlaps)}").IsEmpty();

		var componentTypes = EnumerateComponentTypes().ToList();
		Assertions.AssertThat(componentTypes.Count >= MinimumComponentTypeCount)
			.OverrideFailureMessage($"Component enumeration returned only {componentTypes.Count} types, below the expected minimum of {MinimumComponentTypeCount}, so the classification guard would pass vacuously.")
			.IsTrue();

		var unclassified = componentTypes
			.Select(type => type.Name)
			.Where(name => !replicatedNames.Contains(name) && !hostOnlyNames.Contains(name) && !storageNames.Contains(name))
			.ToList();

		Assertions.AssertThat(unclassified).OverrideFailureMessage($"Unclassified ECS components: {string.Join(", ", unclassified)}").IsEmpty();
	}

	[TestCase]
	public void ClassificationListsHaveNoStaleEntries()
	{
		var componentNames = new HashSet<string>(EnumerateComponentTypes().Select(type => type.Name), StringComparer.Ordinal);
		var stale = HostOnlyComponents.Concat(StorageStructs).Where(name => !componentNames.Contains(name)).ToList();
		Assertions.AssertThat(stale).OverrideFailureMessage($"Host-only and storage lists reference unknown components: {string.Join(", ", stale)}").IsEmpty();
	}

	[TestCase]
	public void ReplicatedComponentIdsAreUnique()
	{
		var ids = new Dictionary<int, string>();
		foreach (var codec in ReplicatedComponents.All)
		{
			if (ids.TryGetValue(codec.ComponentTypeId, out string existing))
			{
				throw new InvalidOperationException($"Component type id {codec.ComponentTypeId} is registered by both {existing} and {codec.ComponentName}.");
			}
			ids[codec.ComponentTypeId] = codec.ComponentName;
		}
	}

	private static IEnumerable<Type> EnumerateComponentTypes()
	{
		foreach (var type in typeof(EcsWorld).Assembly.GetTypes())
		{
			if (!type.IsValueType || type.IsEnum || !IsComponentType(type)) continue;
			if (IsCompilerGenerated(type)) continue;
			yield return type;
		}
	}

	private static bool IsCompilerGenerated(Type type)
	{
		return type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false);
	}

	private static bool IsComponentType(Type type)
	{
		return type.Namespace != null && type.Namespace.StartsWith("Realm.Ecs.Components", StringComparison.Ordinal);
	}
}
