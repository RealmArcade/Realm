using System;

namespace Realm.Ecs.Services;

/// <summary>
///     One match worth of simulation state: the world, its services and the tuning a map loaded. It is constructed per
///     match and owned by whoever runs the simulation, so the authoritative process and a client each get their own
///     instance instead of sharing one static world. Nothing here is static, so two contexts can exist in one process
///     without one naming the other's world.
/// </summary>
public class SimulationContext : IDisposable
{
	public EcsWorld World { get; private set; }
	public Entity WorldEntity { get; set; } = Entity.Null;
	public bool IsGameOver { get; set; }
	public string ActiveMapName { get; set; } = "";

	public DefinitionManager DefinitionManager { get; private set; }
	public CombatService CombatService { get; private set; }
	public MovementService MovementService { get; private set; }
	public MovementAndPathfindingService MovementPathfindingService { get; private set; }
	public NavMeshPathfinder NavMeshPathfinder { get; private set; }

	/// <summary>The harvesting and placement rules this match runs under, as its map scripts declared them.</summary>
	public MapRulesCatalog MapRules { get; private set; }
	public PlayerResourceService PlayerResourceService { get; private set; }
	public StatService StatService { get; private set; }
	public TerrainNavMeshService TerrainNavMeshService { get; private set; }

	/// <summary>The harvesting and production stepping this match's resources run through.</summary>
	public ResourceEconomyService ResourceEconomyService { get; private set; }

	/// <summary>The damage resolution and targeting this match's fights run through.</summary>
	public CombatAndDamageService CombatDamageService { get; private set; }

	/// <summary>
	///     The evaluator a map's WASM scripts are called through. Only an authoritative context has one: a client never
	///     runs map scripts, so its context leaves this null and the stat pipeline skips formula modifiers.
	/// </summary>
	public IWasmEvaluationEngine? EvaluationEngine { get; set; }

	/// <summary>
	///     The active stat tuning. Replaced when a map loads its own <c>CombatTuning</c> block, so a map that authors
	///     nothing is tuned exactly like the engine defaults.
	/// </summary>
	public CombatTuning ActiveCombatTuning { get; set; } = CombatTuning.Default;

	/// <summary>The active damage-resolution config, including the damage/armor matrix.</summary>
	public CombatConfig ActiveCombatConfig { get; set; } = new();

	/// <summary>
	///     Whether this match hides anything from anyone. A match that has been told not to shroud stops advancing
	///     visibility altogether, which is what a map wants when every player is meant to see the whole board.
	/// </summary>
	public bool ShroudDisabled { get; set; }

	public SimulationContext()
	{
		World = new EcsWorld { Context = this };
		DefinitionManager = new DefinitionManager();
		CombatService = new CombatService(this);
		MovementService = new MovementService(this);
		MovementPathfindingService = new MovementAndPathfindingService(this);
		NavMeshPathfinder = new NavMeshPathfinder();
		MapRules = new MapRulesCatalog();
		StatService = new StatService(this);
		TerrainNavMeshService = new TerrainNavMeshService(this);
		ResourceEconomyService = new ResourceEconomyService(this);
		CombatDamageService = new CombatAndDamageService(this);
		PlayerResourceService = new PlayerResourceService();
	}

	/// <summary>
	///     The entity that owns a player slot this match, or <see cref="Entity.Null" /> when the slot has no live owner.
	///     Player assignment lands in the world's mapping state as the match is built, so every system that has to turn a
	///     player index into an entity asks here rather than reaching for a process-wide one.
	/// </summary>
	public Entity GetPlayerEntity(int playerSlot)
	{
		if (playerSlot < 0 || WorldEntity == Entity.Null || !World.IsAlive(WorldEntity)) return Entity.Null;
		if (!World.Has<NetworkMappingState>(WorldEntity)) return Entity.Null;

		Entity[]? playerEntities = World.Get<NetworkMappingState>(WorldEntity).PlayerEntities;
		if (playerEntities == null || playerSlot >= playerEntities.Length) return Entity.Null;

		Entity player = playerEntities[playerSlot];
		return player != Entity.Null && World.IsAlive(player) ? player : Entity.Null;
	}

	/// <summary>
	///     The player slot an entity owns a player for, or nothing when the entity is not one of this match's players.
	///     It is the reverse of <see cref="GetPlayerEntity" />, so a system holding only an owner entity can name the
	///     slot whose rules apply to it.
	/// </summary>
	public int PlayerSlotForEntity(Entity playerEntity)
	{
		if (playerEntity == Entity.Null || WorldEntity == Entity.Null) return -1;
		if (!World.IsAlive(WorldEntity) || !World.Has<NetworkMappingState>(WorldEntity)) return -1;

		Entity[]? playerEntities = World.Get<NetworkMappingState>(WorldEntity).PlayerEntities;
		if (playerEntities == null) return -1;

		for (int slot = 0; slot < playerEntities.Length; slot++)
		{
			if (playerEntities[slot] == playerEntity) return slot;
		}

		return -1;
	}

	/// <summary>
	///     Clears the match state a reload replaces while keeping the same world and services, which is what starting a
	///     second match without restarting the process needs.
	/// </summary>
	public void ResetForMatch()
	{
		WorldEntity = Entity.Null;
		MapRules = new MapRulesCatalog();
		IsGameOver = false;
		ActiveMapName = "";
		ActiveCombatTuning = CombatTuning.Default;
		ActiveCombatConfig = new CombatConfig();
		ShroudDisabled = false;
	}

	public void Dispose()
	{
		WorldEntity = Entity.Null;
		IsGameOver = false;
		ActiveMapName = "";
		ActiveCombatTuning = CombatTuning.Default;
		ActiveCombatConfig = new CombatConfig();
		EvaluationEngine = null;
		World?.Dispose();
	}
}
