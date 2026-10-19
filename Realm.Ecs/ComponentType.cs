using System;
using System.Collections.Generic;
using Realm.Ecs.Components.Abilities;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;

namespace Realm.Ecs;

public static class ComponentTypeRegistry
{
	private static readonly Dictionary<string, int> _typeIds = new(StringComparer.Ordinal);
	private static readonly object _lock = new();
	private static int _nextDynamicId;

	static ComponentTypeRegistry()
	{
		RegisterCanonicalTypes();
	}

	private static void RegisterCanonicalTypes()
	{
		var canonicalTypes = new[]
		{
			typeof(Position),
			typeof(PreviousPosition),
			typeof(Health),
			typeof(Owner),
			typeof(Mana),
			typeof(ManaRegen),
			typeof(CollisionRadius),
			typeof(CollisionScale),
			typeof(Cooldowns),
			typeof(SpellCooldowns),
			typeof(DefinitionId),
			typeof(CustomMetadata),
			typeof(CombatAlertState),
			typeof(CountdownState),
			typeof(DecalIdentity),
			typeof(DecalSpawnRequest),
			typeof(EditorState),
			typeof(GroundLoot),
			typeof(InputState),
			typeof(Inventory),
			typeof(LeaderboardState),
			typeof(ModifierState),
			typeof(NetworkMappingState),
			typeof(NetworkState),
			typeof(OccupiedZones),
			typeof(PlayerPopulation),
			typeof(PlayerUpgrades),
			typeof(ProductionQueue),
			typeof(PropIdentity),
			typeof(PropSpawnRequest),
			typeof(RallyPoint),
			typeof(ReplayState),
			typeof(ScriptPlayer),
			typeof(ScriptPlayersState),
			typeof(ScriptZonesState),
			typeof(SpectatorPerspective),
			typeof(SummaryTableState),
			typeof(TowerUpgradeLevel),
			typeof(UnitOwnerPlayer),
			typeof(UnitSpawnRequest),
			typeof(VFXQueue),
			typeof(VFXRequest),
			typeof(WorldState),
			typeof(ZoneBounds),
			typeof(Attack),
			typeof(AutoCombatState),
			typeof(CombatTargeting),
			typeof(Weapon),
			typeof(Weapons),
			typeof(Armor),
			typeof(TargetIntel),
			typeof(Ability),
			typeof(HasAbilities),
			typeof(AbilityState),
			typeof(AutoCastState),
			typeof(Realm.Ecs.Components.Core.CastTarget),
			typeof(BuffState),
			typeof(Buffs),
			typeof(MovementStats),
			typeof(MoveTo),
			typeof(Velocity),
			typeof(AttackMove),
			typeof(Follow),
			typeof(HoldPosition),
			typeof(InterpolationTarget),
			typeof(RenderPosition),
			typeof(Realm.Ecs.Components.Movement.PathingFlags),
			typeof(Patrol),
			typeof(WaypointBuffer),
			typeof(WaypointQueue),
			typeof(PlayerResources),
			typeof(Gatherer),
			typeof(ResourceNode),
			typeof(ResourceCost),
			typeof(BuildQueue),
			typeof(BuildTask),
			typeof(ConstructionState),
			typeof(Food),
			typeof(Gold),
			typeof(Stone),
			typeof(Wood),
			typeof(ArmorStat),
			typeof(AttackDamage),
			typeof(Realm.Ecs.Components.Combat.AttackSpeed),
			typeof(Realm.Ecs.Components.Stats.AttackSpeed),
			typeof(DerivedCombatStats),
			typeof(HealthStat),
			typeof(MovementSpeed),
			typeof(StatModifier),
			typeof(Stats),
			typeof(UnitAttributes),
			typeof(UnitBaseStats),
			typeof(Attackable),
			typeof(Building),
			typeof(BypassPopulationTag),
			typeof(Caster),
			typeof(Dead),
			typeof(Hero),
			typeof(Invulnerable),
			typeof(Movable),
			typeof(Player),
			typeof(Prop),
			typeof(SpellImmune),
			typeof(UnderConstruction),
			typeof(Unit),
			typeof(Experience),
			typeof(Icon),
			typeof(Level),
			typeof(Model),
			typeof(ModelScale),
			typeof(Name),
			typeof(ReplayAnimationState),
			typeof(Rotation3D),
			typeof(RotationY),
			typeof(PreviousRotationY),
			typeof(ShroudState),
			typeof(TerrainCell),
			typeof(TerrainColorsState),
			typeof(TerrainPathingFlags),
			typeof(TerrainState),
			typeof(TerrainSwatchConfig),
			typeof(WeatherState),
			typeof(PlayerRelationshipMatrix),
			typeof(PlayerTeam)
		};

		for (int i = 0; i < canonicalTypes.Length; i++)
		{
			string key = canonicalTypes[i].FullName ?? canonicalTypes[i].Name;
			_typeIds[key] = i;
		}

		_nextDynamicId = canonicalTypes.Length;
	}

	public static int GetId<T>() => ComponentType<T>.Id;

	public static int AssignId<T>()
	{
		string key = typeof(T).FullName ?? typeof(T).Name;
		lock (_lock)
		{
			if (_typeIds.TryGetValue(key, out int existingId))
			{
				return existingId;
			}

			int id = _nextDynamicId++;
			if (id >= 256)
			{
				throw new InvalidOperationException($"Exceeded maximum component type limit of 256. Attempted ID: {id} for {key}");
			}
			_typeIds[key] = id;
			return id;
		}
	}

	public static int Count
	{
		get
		{
			lock (_lock)
			{
				return _nextDynamicId;
			}
		}
	}
}

public static class ComponentType<T>
{
	public static readonly int Id = ComponentTypeRegistry.AssignId<T>();
}
