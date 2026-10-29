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
		"ManaRegen", "DefinitionId",		"CustomMetadata",
		"CountdownState", "DecalIdentity", "DecalSpawnRequest", "EditorState",
		"DescribedAtSequence",
		"InputState",
		"NetworkMappingState", "NetworkState", "OccupiedZones",
		"PropIdentity", "PropSpawnRequest",
		"ReplayState", "ScriptPlayer", "SpectatorPerspective",
		"TowerUpgradeLevel", "UnitOwnerPlayer", "UnitSpawnRequest",
		"VFXQueue", "VFXRequest", "ZoneBounds",
		"Weapon", "Weapons",
		"TargetIntel", "Ability", "HasAbilities",
		"AutoCastState", "CastTarget",
		"Velocity",
		"InterpolationTarget", "RenderPosition", "PathingFlags",
		"WaypointBuffer",
		"Gatherer", "ResourceCost",
		"Food", "Gold",
		"Stone", "Wood", "ArmorStat", "AttackDamage",
		"AttackSpeedMultiplier",
		"StatModifier",
		"Attackable", "Building", "BypassPopulationTag", "Caster",
		"Dead", "Hero", "Movable", "Player",
		"Prop", "Unit", "Icon", "Model",
		"Name", "ReplayAnimationState", "Rotation3D", "RotationY",
		"PreviousRotationY", "ShroudState", "TerrainCell", "TerrainColorsState",
		"TerrainSwatchConfig",
		"PlayerRelationshipMatrix", "HealingTarget",
		"ScanRadius", "AbilityTargetFilter", "CameraState", "BuildingSpawnOffset",
		"SummonedUnit"
	};

	private static readonly string[] RpcReplicatedComponents =
	{
		"TerrainState", "WeatherState"
	};

	private static readonly string[] ColdPayloadComponents =
	{
		"LeaderboardState", "SummaryTableState"
	};

	private static readonly Dictionary<string, (string[] Covered, string[] NotReplicated)> RpcChannelFieldCoverage = new(StringComparer.Ordinal)
	{
		["TerrainState"] = (new[] { "PathingCodesHandle" }, new[] { "CellsHandle", "CellSize", "Depth", "NavMeshHandle", "NavMeshQueryHandle", "QuadSize", "SwatchConfigsHandle", "Width" }),
		["WeatherState"] = (new[] { "Type" }, new[] { "BaseFogDensity" })
	};

	private static readonly string[] StorageStructs =
	{
		"BuffSlot", "BuffSlotArray", "InventorySlot", "InventorySlotArray",
		"ModifierSlotArray", "PlayerSlotArray", "StatSlot", "StatSlotArray",
		"VFXSlotArray", "OccupiedZoneMask", "CameraLocationSlot",
		"BuildQueueSlot", "BuildQueueSlots", "AbsorbShieldSlot", "AbsorbShieldSlotArray"
	};

	private const int MinimumComponentTypeCount = 120;

	[TestCase]
	public void EveryEcsComponentIsClassified()
	{
		var replicatedNames = ReplicatedComponents.All.Select(codec => codec.ComponentName).ToList();

		var buckets = new (string Name, IReadOnlyList<string> Entries)[]
		{
			("replicated", replicatedNames),
			("rpc", RpcReplicatedComponents),
			("cold-payload", ColdPayloadComponents),
			("host-only", HostOnlyComponents),
			("storage", StorageStructs)
		};

		var duplicates = buckets
			.SelectMany(bucket => bucket.Entries
				.GroupBy(name => name)
				.Where(group => group.Count() > 1)
				.Select(group => $"{group.Key} ({bucket.Name})"))
			.ToList();
		Assertions.AssertThat(duplicates).OverrideFailureMessage($"Component classified twice inside one list: {string.Join(", ", duplicates)}").IsEmpty();

		var overlaps = new List<string>();
		for (int left = 0; left < buckets.Length; left++)
		{
			for (int right = left + 1; right < buckets.Length; right++)
			{
				foreach (string name in buckets[left].Entries.Intersect(buckets[right].Entries))
				{
					overlaps.Add($"{name} ({buckets[left].Name}/{buckets[right].Name})");
				}
			}
		}

		Assertions.AssertThat(overlaps).OverrideFailureMessage($"Component classified more than once: {string.Join(", ", overlaps)}").IsEmpty();

		var componentTypes = EnumerateComponentTypes().ToList();
		Assertions.AssertThat(componentTypes.Count >= MinimumComponentTypeCount)
			.OverrideFailureMessage($"Component enumeration returned only {componentTypes.Count} types, below the expected minimum of {MinimumComponentTypeCount}, so the classification guard would pass vacuously.")
			.IsTrue();

		var unclassified = componentTypes
			.Select(type => type.Name)
			.Where(name => !buckets.Any(bucket => bucket.Entries.Contains(name)))
			.ToList();

		Assertions.AssertThat(unclassified).OverrideFailureMessage($"Unclassified ECS components: {string.Join(", ", unclassified)}").IsEmpty();
	}

	[TestCase]
	public void ClassificationListsHaveNoStaleEntries()
	{
		var componentNames = new HashSet<string>(EnumerateComponentTypes().Select(type => type.Name), StringComparer.Ordinal);
		var stale = HostOnlyComponents.Concat(RpcReplicatedComponents).Concat(ColdPayloadComponents).Concat(StorageStructs).Where(name => !componentNames.Contains(name)).ToList();
		Assertions.AssertThat(stale).OverrideFailureMessage($"Classification lists reference unknown components: {string.Join(", ", stale)}").IsEmpty();
	}

	[TestCase]
	public void RpcReplicatedComponentsDocumentFieldCoverage()
	{
		var rpcNames = new HashSet<string>(RpcReplicatedComponents, StringComparer.Ordinal);
		var coverageNames = new HashSet<string>(RpcChannelFieldCoverage.Keys, StringComparer.Ordinal);
		Assertions.AssertThat(coverageNames.SetEquals(rpcNames))
			.OverrideFailureMessage($"RPC bucket and coverage disagree. Missing coverage: {string.Join(", ", rpcNames.Except(coverageNames))}. Stale coverage: {string.Join(", ", coverageNames.Except(rpcNames))}.")
			.IsTrue();

		var componentTypes = EnumerateComponentTypes().GroupBy(type => type.Name).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
		var problems = new List<string>();

		foreach (var (name, coverage) in RpcChannelFieldCoverage)
		{
			if (!componentTypes.TryGetValue(name, out Type componentType))
			{
				problems.Add($"{name}: component type was not found");
				continue;
			}

			var fields = componentType.GetFields(BindingFlags.Public | BindingFlags.Instance).Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
			var covered = new HashSet<string>(coverage.Covered, StringComparer.Ordinal);
			var notReplicated = new HashSet<string>(coverage.NotReplicated, StringComparer.Ordinal);

			if (covered.Count == 0) problems.Add($"{name}: no covered fields are documented");

			foreach (string member in covered.Intersect(notReplicated))
			{
				problems.Add($"{name}.{member}: listed as both covered and not replicated");
			}

			foreach (string member in covered.Concat(notReplicated).Where(member => !fields.Contains(member)))
			{
				problems.Add($"{name}.{member}: no such field on the component");
			}

			foreach (string field in fields.Where(field => !covered.Contains(field) && !notReplicated.Contains(field)))
			{
				problems.Add($"{name}.{field}: field is not classified as covered or not replicated");
			}
		}

		Assertions.AssertThat(problems).OverrideFailureMessage($"RPC channel field coverage is incomplete: {string.Join("; ", problems)}").IsEmpty();
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
