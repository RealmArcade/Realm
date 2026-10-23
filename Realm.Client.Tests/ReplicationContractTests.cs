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
		"ReplayState", "ScriptPlayersState", "ScriptZonesState", "SpectatorPerspective",
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
		"TerrainPathingFlags", "TerrainState", "TerrainSwatchConfig", "WeatherState",
		"PlayerRelationshipMatrix"
	};

	[TestCase]
	public void EveryEcsComponentIsClassified()
	{
		var replicatedNames = new HashSet<string>(StringComparer.Ordinal);
		foreach (var codec in ReplicatedComponents.All)
		{
			replicatedNames.Add(codec.ComponentName);
		}

		var hostOnlyNames = new HashSet<string>(HostOnlyComponents, StringComparer.Ordinal);
		var duplicates = HostOnlyComponents.GroupBy(name => name).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
		Assertions.AssertThat(duplicates).OverrideFailureMessage($"Duplicate host-only component entries: {string.Join(", ", duplicates)}").IsEmpty();
		Assertions.AssertThat(replicatedNames.Overlaps(hostOnlyNames)).OverrideFailureMessage($"Component classified as both replicated and host-only: {string.Join(", ", replicatedNames.Intersect(hostOnlyNames))}").IsFalse();

		var unclassified = new List<string>();
		foreach (var type in EnumerateComponentTypes())
		{
			string name = type.Name;
			if (replicatedNames.Contains(name) || hostOnlyNames.Contains(name)) continue;
			unclassified.Add(name);
		}

		Assertions.AssertThat(unclassified).OverrideFailureMessage($"Unclassified ECS components: {string.Join(", ", unclassified)}").IsEmpty();
	}

	[TestCase]
	public void HostOnlyComponentListHasNoStaleEntries()
	{
		var componentNames = new HashSet<string>(EnumerateComponentTypes().Select(type => type.Name), StringComparer.Ordinal);
		var stale = HostOnlyComponents.Where(name => !componentNames.Contains(name)).ToList();
		Assertions.AssertThat(stale).OverrideFailureMessage($"Host-only component list references unknown components: {string.Join(", ", stale)}").IsEmpty();
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
			if (type.GetCustomAttribute<System.Runtime.InteropServices.StructLayoutAttribute>() == null) continue;
			yield return type;
		}
	}

	private static bool IsComponentType(Type type)
	{
		return type.Namespace != null && type.Namespace.StartsWith("Realm.Ecs.Components", StringComparison.Ordinal);
	}
}
