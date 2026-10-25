using System.Collections.Generic;
using Realm.MapAPI;

namespace Realm.Ecs.Services;

public static class ECSContext
{
	public static EcsWorld? World { get; private set; }
	public static Entity WorldEntity { get; set; } = Entity.Null;
	public static bool IsGameOver { get; set; }
	public static string ActiveMapName { get; set; } = "";

	public static DefinitionManager? DefinitionManager { get; private set; }
	public static CombatService? CombatService { get; private set; }
	public static MovementService? MovementService { get; private set; }
	public static PlayerResourceService? PlayerResourceService { get; private set; }
	public static StatService? StatService { get; private set; }
	public static TerrainNavMeshService? TerrainNavMeshService { get; private set; }
	public static IWasmEvaluationEngine? EvaluationEngine { get; set; }

	/// <summary>
	///     The active stat tuning. Replaced when a map loads its own <c>CombatTuning</c> block, so a
	///     map that authors nothing is tuned exactly like the engine defaults.
	/// </summary>
	public static CombatTuning ActiveCombatTuning { get; set; } = CombatTuning.Default;

	/// <summary>The active damage-resolution config, including the damage/armor matrix.</summary>
	public static CombatConfig ActiveCombatConfig { get; set; } = new();

	static ECSContext()
	{
		EnsureServices();
	}

	public static void EnsureServices()
	{
		if (World != null)
		{
			return;
		}

		World = new EcsWorld();
		WorldEntity = Entity.Null;
		IsGameOver = false;
		ActiveMapName = "";
		ActiveCombatTuning = CombatTuning.Default;
		ActiveCombatConfig = new CombatConfig();

		DefinitionManager = new DefinitionManager();
		CombatService = new CombatService();
		MovementService = new MovementService();
		StatService = new StatService();
		TerrainNavMeshService = new TerrainNavMeshService();

		PlayerResourceService = new();
	}

	public static void Dispose()
	{
		WorldEntity = Entity.Null;
		IsGameOver = false;
		ActiveMapName = "";
		DefinitionManager = null;
		CombatService = null;
		MovementService = null;
		StatService = null;
		TerrainNavMeshService = null;
		PlayerResourceService = null;
		ActiveCombatTuning = CombatTuning.Default;
		ActiveCombatConfig = new CombatConfig();
		World?.Dispose();
		World = null;
		ColdPayloadRegistry.ResetAll();
		StringRegistry.Reset();
	}

	public static void DisposeAndRecreateServices()
	{
		Dispose();
		EnsureServices();
	}
}
