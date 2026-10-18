using System.Collections.Generic;

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
		EvaluationEngine = null;
		World = null;
	}

	public static void DisposeAndRecreateServices()
	{
		Dispose();
		EnsureServices();
	}
}
