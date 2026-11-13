using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Realm.Ecs;
using Realm.Ecs.Replication;
using DotRecast.Detour;
using Godot;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Client.Utils;
using Realm.Client.VFX;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Environment = Godot.Environment;
using FileAccess = System.IO.FileAccess;
using Realm.Shared.Abilities;

namespace Realm.Client.Core;
public partial class GameHost
{
	public static int GetUnitPathingFlags(UnitMetadata meta)
	{
		return (GameContext.Host != null ? GameContext.UnitSpawnService : null)?.GetUnitPathingFlags(meta) ?? 8;
	}

	public int GetMaxPopulation(int playerIndex)
	{
		return GameContext.ResourceEconomyService?.GetPlayerMaxPopulation(playerIndex) ?? 0;
	}

	public void SetMaxPopulation(int playerIndex, int max)
	{
		GameContext.ResourceEconomyService?.SetPlayerMaxPopulation(playerIndex, max);
	}

	public int GetCurrentPopulation(int playerIndex)
	{
		return GameContext.ResourceEconomyService?.GetPlayerCurrentPopulation(playerIndex) ?? 0;
	}

	public void SetCurrentPopulation(int playerIndex, int current)
	{
		GameContext.ResourceEconomyService?.SetPlayerCurrentPopulation(playerIndex, current);
	}

	public bool CheatsEnabled
		=> GameContext.SessionState.IsMapEditorMode || GameContext.SessionState.IsTestMode || (GameContext.Lobby?.IsSinglePlayer ?? false);

	/// <summary>
	///     True when this match belongs to the one screen playing it: a single-player game or a playtest started from the
	///     map editor. Nobody else is waiting on it, so what the match asks of a player who might be holding other people up
	///     does not apply.
	/// </summary>
	public bool IsSinglePlayerMatch => CheatsEnabled;

	/// <summary>
	///     True when this match is played with other people, and its outcome is therefore part of a player's record. A
	///     single-player game and a playtest are one screen's own practice: they are not counted anywhere.
	/// </summary>
	public bool IsRankedMultiplayerMatch => !IsSinglePlayerMatch && (GameContext.Lobby?.IsGameStarted ?? false);

	public string GetStringText(int stringId) => Realm.Ecs.StringRegistry.Get(stringId);

	public float GameElapsedTime
	{
		get => GameContext.World?.GetFieldOrDefault<WorldState, float>(GameContext.Simulation.WorldEntity, s => s.GameElapsedTime) ?? 0f;
		private set => GameContext.World?.Mutate<WorldState>(GameContext.Simulation.WorldEntity, (ref WorldState s) =>
			World.Set(GameContext.Simulation.WorldEntity, new WorldState(value, s.TimeOfDayIndex, s.TimeOfDayTimer, s.DayNightCycleEnabled)));
	}

	public int TimeOfDayIndex
		=> GameContext.World?.GetFieldOrDefault<WorldState, int>(GameContext.Simulation.WorldEntity, s => s.TimeOfDayIndex) ?? 0;

	private float TimeOfDayTimer
	{
		get => GameContext.World?.GetFieldOrDefault<WorldState, float>(GameContext.Simulation.WorldEntity, s => s.TimeOfDayTimer) ?? 0f;
		set => GameContext.World?.Mutate<WorldState>(GameContext.Simulation.WorldEntity, (ref WorldState s) =>
			World.Set(GameContext.Simulation.WorldEntity, new WorldState(s.GameElapsedTime, s.TimeOfDayIndex, value, s.DayNightCycleEnabled)));
	}

	public static PlaceableTemplates Placeables => GameContext.RegistryService?.Placeables ?? new PlaceableTemplates();
	public static PlaceableView<UnitMetadata> UnitRegistry => GameContext.RegistryService?.UnitRegistry ?? new(PlaceableKind.Unit);
	public static PlaceableView<UnitMetadata> BuildingRegistry => GameContext.RegistryService?.BuildingRegistry ?? new(PlaceableKind.Building);
	public static PlaceableView<PropMetadata> PropRegistry => GameContext.RegistryService?.PropRegistry ?? new(PlaceableKind.Prop);
	public static PlaceableView<ResourceMetadata> ResourceRegistry => GameContext.RegistryService?.ResourceRegistry ?? new(PlaceableKind.Resource);
	public static Dictionary<StringName, WeaponMetadata> WeaponRegistry => GameContext.RegistryService?.WeaponRegistry ?? new();
	public static Dictionary<StringName, AttachmentMetadata> AttachmentRegistry => GameContext.RegistryService?.AttachmentRegistry ?? new();
	public static Dictionary<StringName, ItemMetadata> ItemRegistry => GameContext.RegistryService?.ItemRegistry ?? new();
	public static Dictionary<StringName, UpgradeMetadata> UpgradeRegistry => GameContext.RegistryService?.UpgradeRegistry ?? new();

	public static bool TryGetUnitOrBuildingMetadata(StringName objectId, [NotNullWhen(true)] out UnitMetadata? meta)
	{
		meta = default;
		if (objectId.IsEmpty) return false;
		return Placeables.TryGetUnitOrBuilding(objectId, out meta);
	}

	public static bool TryGetUnitOrBuildingMetadata(string? objectId, [NotNullWhen(true)] out UnitMetadata? meta)
	{
		meta = default;
		if (string.IsNullOrEmpty(objectId)) return false;
		return TryGetUnitOrBuildingMetadata((StringName)objectId, out meta);
	}

	public string GetFallbackModelPath(string? unitId, bool isBuilding)
	{
		return GameContext.UnitSpawnService.GetFallbackModelPath(unitId, isBuilding);
	}

	private float _goldBackup
	{
		get => GameContext.World?.GetFieldOrDefault<ReplayState, float>(GameContext.Simulation.WorldEntity, s => s.GoldBackup, 500f) ?? 500f;
		set => GameContext.World?.Mutate<ReplayState>(GameContext.Simulation.WorldEntity, (ref ReplayState s) => s.GoldBackup = value);
	}

	private float _woodBackup
	{
		get => GameContext.World?.GetFieldOrDefault<ReplayState, float>(GameContext.Simulation.WorldEntity, s => s.WoodBackup, 400f) ?? 400f;
		set => GameContext.World?.Mutate<ReplayState>(GameContext.Simulation.WorldEntity, (ref ReplayState s) => s.WoodBackup = value);
	}

	private float _stoneBackup
	{
		get => GameContext.World?.GetFieldOrDefault<ReplayState, float>(GameContext.Simulation.WorldEntity, s => s.StoneBackup, 200f) ?? 200f;
		set => GameContext.World?.Mutate<ReplayState>(GameContext.Simulation.WorldEntity, (ref ReplayState s) => s.StoneBackup = value);
	}

	private bool DayNightCycleEnabled
	{
		get => GameContext.World?.GetFieldOrDefault<WorldState, bool>(GameContext.Simulation.WorldEntity, s => s.DayNightCycleEnabled, true) ?? true;
		set => GameContext.World?.Mutate<WorldState>(GameContext.Simulation.WorldEntity, (ref WorldState s) =>
			World.Set(GameContext.Simulation.WorldEntity, new WorldState(s.GameElapsedTime, s.TimeOfDayIndex, s.TimeOfDayTimer, value)));
	}

	public event Action<UnitId>? OnUnitCreated;
	public event Action<UnitId, UnitId>? OnUnitDied;
	public event Action<UnitId, UnitId, float>? OnUnitDamaged;
	public event Action<UnitId, string, System.Numerics.Vector3>? OnSpellCast;
	public event Action<UnitId>? OnUnitSelected;
	public event Action<UnitId, UnitId>? OnUnitAttacked;

	public event Action<UnitId, string>? OnConsumableChanged;

	public void RaiseConsumableChanged(UnitId unit, string consumableId)
	{
		OnConsumableChanged?.Invoke(unit, consumableId ?? string.Empty);
	}

	private bool ExecuteConsumablePurchase(UnitId caster, string abilityId, Godot.Vector3 position)
	{
		var result = GameContext.ConsumableService.TryPurchase(
			caster, abilityId, new System.Numerics.Vector3(position.X, position.Y, position.Z));

		if (!result.IsConsumable) return false;

		if (!result.Purchased && !string.IsNullOrEmpty(result.Message))
		{
			GameContext.InGameHud?.ShowFeedbackText(result.Message, new Godot.Color(1f, 0.55f, 0.2f));
		}

		return true;
	}

	public void TriggerPlayerChatMessage(string message)
	{
		if (string.IsNullOrWhiteSpace(message)) return;

		int selectedServerEntityId = 0;
		if (SelectedUnits.Count > 0 && World.IsAlive(SelectedUnits[0].Entity))
		{
			selectedServerEntityId = GetServerEntityId(SelectedUnits[0].Entity);
		}

		if (GameContext.ServerSession?.SubmitConsoleCommand(message, selectedServerEntityId) == true) return;

		GD.PushWarning($"[GameHost] '{message.Trim()}' was not sent: there is no match running to hand it to.");
	}

	public void TriggerKillUnit(Unit3D unit, bool executeDespawnShader = true, bool playDeathAnimation = true)
	{
		KillUnit(unit, executeDespawnShader, playDeathAnimation);
	}

	private void InitializePlayerResources(Entity playerEntity)
	{
		var resourcesDict = new Dictionary<ResourceId, int>
		{
			{ GameContext.Simulation.PlayerResourceService.GoldResourceId, 500 },
			{ GameContext.Simulation.PlayerResourceService.WoodResourceId, 400 },
			{ GameContext.Simulation.PlayerResourceService.StoneResourceId, 200 }
		};
		World.Add(playerEntity, new PlayerResources(resourcesDict));
	}

	private void SetupWorldEntityComponents()
	{
		int width = GroundTerrain != null ? GroundTerrain.Width : 128;
		int depth = GroundTerrain != null ? GroundTerrain.Depth : 128;
		float quadSize = GroundTerrain != null ? GroundTerrain.QuadSize : 2.0f;
		float cellSize = GroundTerrain != null ? GroundTerrain.CellSize : TerrainState.DefaultCellSize;
		var cells = GroundTerrain != null ? GroundTerrain.Cells : null;
		int[,]? pathingCodes = GroundTerrain != null ? GroundTerrain.PathingCodes : null;
		DtNavMesh? navMesh = GroundTerrain != null ? GroundTerrain.NavMesh : null;
		DtNavMeshQuery? navMeshQuery = GroundTerrain != null ? GroundTerrain.NavMeshQuery : null;

		GameContext.Simulation.WorldEntity = GameContext.WorldInitService.SetupWorldEntityComponents(
			width, depth, quadSize, cellSize,
			cells, pathingCodes, navMesh, navMeshQuery
		);
	}

	private readonly ResourceNodeId[] _cachedResourceNodesBuffer = new ResourceNodeId[4096];
	private int _cachedResourceNodesCount = 0;

	public ReadOnlySpan<ResourceNodeId> GetCachedResourceNodes()
	{
		_cachedResourceNodesCount = 0;
		for (int i = 0; i < AllProps.Count && _cachedResourceNodesCount < _cachedResourceNodesBuffer.Length; i++)
		{
			var prop = AllProps[i];
			if (GodotObject.IsInstanceValid(prop))
			{
				if (prop.PropId == "goldmine" || prop.PropId == "tree" || prop.PropId == "rock")
				{
					_cachedResourceNodesBuffer[_cachedResourceNodesCount++] = (ResourceNodeId)prop.Entity.Id;
				}
			}
		}

		return new ReadOnlySpan<ResourceNodeId>(_cachedResourceNodesBuffer, 0, _cachedResourceNodesCount);
	}

	public bool TryGetResourceProp(ResourceNodeId node, out Prop3D? prop)
	{
		for (int i = 0; i < AllProps.Count; i++)
		{
			var p = AllProps[i];
			if (GodotObject.IsInstanceValid(p) && p.Entity.Id == node.Value)
			{
				prop = p;
				return true;
			}
		}
		prop = null;
		return false;
	}

	public static void EnsureMapProjectFiles(string mapDir)
	{
		string csprojPath = Path.Combine(mapDir, "MapScript.csproj");
		string libDir = Path.Combine(mapDir, "lib");
		Directory.CreateDirectory(libDir);

		string vscodeDir = Path.Combine(mapDir, ".vscode");
		Directory.CreateDirectory(vscodeDir);
		string vscodeSettingsPath = Path.Combine(vscodeDir, "settings.json");

		string projectRoot = PathUtils.GetProjectRoot();
		string templateDir = PathUtils.FindPath("MapTemplate");
		if (!Directory.Exists(templateDir))
			templateDir = Path.Combine(projectRoot, "..", "MapTemplate");

		CopyTemplateFile(PathUtils.FindPath("MapTemplate/.vscode/settings.json"), Path.Combine(templateDir, ".vscode", "settings.json"), vscodeSettingsPath);

		string repoRoot = Path.GetFullPath(Path.Combine(projectRoot, ".."));
		EnsureMapApiDllsCopied(repoRoot, templateDir, libDir);

		CopyTemplateFile(PathUtils.FindPath("MapTemplate/MapScript.csproj"), Path.Combine(templateDir, "MapScript.csproj"), csprojPath);
		CopyTemplateFile(PathUtils.FindPath("MapTemplate/Directory.Build.targets"), Path.Combine(templateDir, "Directory.Build.targets"), Path.Combine(mapDir, "Directory.Build.targets"));

		RestoreDotnetProject(csprojPath, mapDir);
	}

	private static void CopyTemplateFile(string primaryPath, string fallbackPath, string destPath)
	{
		string srcPath = File.Exists(primaryPath) ? primaryPath : fallbackPath;
		if (File.Exists(srcPath))
		{
			File.WriteAllText(destPath, File.ReadAllText(srcPath));
		}
	}

	private static void EnsureMapApiDllsCopied(string repoRoot, string templateDir, string libDir)
	{
		string sourceDll = Path.Combine(repoRoot, "Realm.MapAPI", "bin", "Release", "net10.0", "Realm.MapAPI.dll");
		string sourceXml = Path.Combine(repoRoot, "Realm.MapAPI", "bin", "Release", "net10.0", "Realm.MapAPI.xml");

		if (!File.Exists(sourceDll))
		{
			sourceDll = Path.Combine(repoRoot, "Realm.MapAPI", "bin", "Debug", "net10.0", "Realm.MapAPI.dll");
			sourceXml = Path.Combine(repoRoot, "Realm.MapAPI", "bin", "Debug", "net10.0", "Realm.MapAPI.xml");
		}

		if (TryCopyMapApiDll(sourceDll, sourceXml, libDir)) return;

		string templateLib = Path.Combine(templateDir, "lib");
		if (TryCopyMapApiDll(Path.Combine(templateLib, "Realm.MapAPI.dll"), Path.Combine(templateLib, "Realm.MapAPI.xml"), libDir)) return;

		string mapApiCsproj = Path.Combine(repoRoot, "Realm.MapAPI", "Realm.MapAPI.csproj");
		if (!File.Exists(mapApiCsproj)) return;

		string dotnetExecutablePath = DotnetSdkResolver.ResolveSdkExecutablePath();
		if (string.IsNullOrEmpty(dotnetExecutablePath))
		{
			GameLog.LogError("Realm.MapAPI.dll is missing and the .NET SDK (map compiler) is not installed yet, so it cannot be built.");
			return;
		}

		GameLog.LogToConsole("Realm.MapAPI.dll not found. Auto-building Realm.MapAPI...");
		try
		{
			using var buildProcess = new Process();
			buildProcess.StartInfo.FileName = dotnetExecutablePath;
			buildProcess.StartInfo.Arguments = $"build \"{mapApiCsproj}\" -c Debug";
			buildProcess.StartInfo.CreateNoWindow = true;
			buildProcess.StartInfo.UseShellExecute = false;
			buildProcess.Start();
			buildProcess.WaitForExit();
		}
		catch (Exception exception)
		{
			GameLog.LogError("Realm.MapAPI.dll could not be built: " + exception.Message);
			return;
		}

		TryCopyMapApiDll(Path.Combine(repoRoot, "Realm.MapAPI", "bin", "Debug", "net10.0", "Realm.MapAPI.dll"), Path.Combine(repoRoot, "Realm.MapAPI", "bin", "Debug", "net10.0", "Realm.MapAPI.xml"), libDir);
	}

	private static bool TryCopyMapApiDll(string dllPath, string xmlPath, string libDir)
	{
		if (!File.Exists(dllPath)) return false;

		CopyIfDifferentBytes(dllPath, Path.Combine(libDir, "Realm.MapAPI.dll"));
		if (File.Exists(xmlPath))
		{
			CopyIfDifferentBytes(xmlPath, Path.Combine(libDir, "Realm.MapAPI.xml"));
			CopyIfDifferentBytes(xmlPath, Path.Combine(libDir, "Realm.EditorAPI.xml"));
		}

		string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
		if (File.Exists(pdbPath))
			CopyIfDifferentBytes(pdbPath, Path.Combine(libDir, "Realm.MapAPI.pdb"));
		return true;
	}

	private static void CopyIfDifferentBytes(string src, string dst)
	{
		if (File.Exists(dst))
		{
			var sInfo = new FileInfo(src);
			var dInfo = new FileInfo(dst);
			if (sInfo.Length == dInfo.Length && File.ReadAllBytes(src).AsSpan().SequenceEqual(File.ReadAllBytes(dst)))
				return;
		}

		File.Copy(src, dst, true);
	}

	private static void RestoreDotnetProject(string csprojPath, string mapDir)
	{
		try
		{
			string dotnetExecutablePath = DotnetSdkResolver.ResolveSdkExecutablePath();
			if (string.IsNullOrEmpty(dotnetExecutablePath)) return;

			using var restoreProcess = new Process();
			restoreProcess.StartInfo.FileName = dotnetExecutablePath;
			restoreProcess.StartInfo.Arguments = $"restore \"{csprojPath}\"";
			restoreProcess.StartInfo.WorkingDirectory = mapDir;
			restoreProcess.StartInfo.CreateNoWindow = true;
			restoreProcess.StartInfo.UseShellExecute = false;
			restoreProcess.Start();
			restoreProcess.WaitForExit(10000);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"could not restore the packages of map project {csprojPath}; the build that follows may fail: {ex.GetType().Name}: {ex.Message}");
		}
	}

}
