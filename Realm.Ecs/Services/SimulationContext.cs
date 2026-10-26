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
	public PlayerResourceService PlayerResourceService { get; private set; }
	public StatService StatService { get; private set; }
	public TerrainNavMeshService TerrainNavMeshService { get; private set; }

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

	public SimulationContext()
	{
		World = new EcsWorld { Context = this };
		DefinitionManager = new DefinitionManager();
		CombatService = new CombatService(this);
		MovementService = new MovementService(this);
		MovementPathfindingService = new MovementAndPathfindingService(this);
		NavMeshPathfinder = new NavMeshPathfinder();
		StatService = new StatService(this);
		TerrainNavMeshService = new TerrainNavMeshService(this);
		PlayerResourceService = new PlayerResourceService();
	}

	/// <summary>
	///     Clears the match state a reload replaces while keeping the same world and services, which is what starting a
	///     second match without restarting the process needs.
	/// </summary>
	public void ResetForMatch()
	{
		WorldEntity = Entity.Null;
		IsGameOver = false;
		ActiveMapName = "";
		ActiveCombatTuning = CombatTuning.Default;
		ActiveCombatConfig = new CombatConfig();
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
