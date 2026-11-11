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
	public void StopRecording()
	{
		GameContext.ReplayService?.StopRecording();
	}

	public override void _ExitTree()
	{
		LogGameStabilitySummary();

		StopRecording();

		// Only the screen that joined this match may end it: the editor's screen leaves as the playtest's arrives, and
		// the session under it belongs to the playtest, which would be left playing a world nothing runs.
		if (_joinedSession != null && ReferenceEquals(GameContext.ServerSession, _joinedSession))
		{
			GameContext.ServerSession?.Stop();
			GameContext.DetachServerSession();
		}

		// A removal held against a clock this screen no longer has would free a node of whatever match comes next.
		_pendingUnitViewRemovals.Clear();

		if (!GameContext.GlobalScope.IsDisposed)
		{
			GameContext.MapAutosaveService.FlushPendingWrites(TimeSpan.FromSeconds(2));
			GameContext.MapHistoryService.WaitForQueue(TimeSpan.FromSeconds(2));
		}

		GameContext.Dispose();
		GameContext.UnregisterHost(this);
	}

	private void LogGameStabilitySummary()
	{
		if ((Multiplayer.MultiplayerPeer == null || Multiplayer.IsServer()) && GameContext.PerformanceTrackingService.TrackerTickDurations != null && GameContext.PerformanceTrackingService.TrackerTickDurations.Count >= 30)
		{
			HostStabilityTracker.AddGameSummary(new GameStabilitySummary
			{
				AvgTickMs = HostStabilityTracker.CalculateAverage(GameContext.PerformanceTrackingService.TrackerTickDurations),
				MedianTickMs = HostStabilityTracker.CalculateMedian(GameContext.PerformanceTrackingService.TrackerTickDurations),
				MaxTickMs = HostStabilityTracker.CalculateMax(GameContext.PerformanceTrackingService.TrackerTickDurations),
				AvgApiMs = HostStabilityTracker.CalculateAverage(GameContext.PerformanceTrackingService.TrackerApiDurations),
				MedianApiMs = HostStabilityTracker.CalculateMedian(GameContext.PerformanceTrackingService.TrackerApiDurations),
				MaxApiMs = HostStabilityTracker.CalculateMax(GameContext.PerformanceTrackingService.TrackerApiDurations)
			});
		}
	}

	private void CreateGround()
	{
		if (!GameContext.SessionState.IsMapEditorMode)
		{
			string? activeMapName = GameContext.Lobby?.ActiveMapName;
			if (!string.IsNullOrEmpty(activeMapName))
			{
				string? resolvedDirectory = ResolveMapDirectory(activeMapName);
				if (!string.IsNullOrEmpty(resolvedDirectory) && GameContext.MapStorageService != null)
					GameContext.MapStorageService.CurrentMapDirectory = resolvedDirectory;
			}
		}

		ClearExistingGround();

		if (GameContext.SessionState.IsMapEditorMode || GameContext.SaveLoadService.IsLoadingMap)
		{
			CreateEditableGround();
			return;
		}

		if (!ShouldCreateRuntimeGround()) return;

		string? rawMapName = GameContext.Lobby?.ActiveMapName;
		if (string.IsNullOrEmpty(rawMapName)) return;

		string terrainPath = ResolveTerrainPath(rawMapName);

		CreateRuntimeGround(terrainPath);
	}

	private void ClearExistingGround()
	{
		var toRemove = new List<Node>();
		foreach (var child in GetChildren())
		{
			if (child.Name.ToString().StartsWith("Ground")) toRemove.Add(child);
		}

		foreach (var child in toRemove)
		{
			RemoveChild(child);
			child.QueueFree();
		}

		GroundTerrain = null;
	}

	private void CreateEditableGround()
	{
		var terrainNode = new EditableTerrain();
		terrainNode.Name = "Ground";
		AddChild(terrainNode);
		GroundTerrain = terrainNode;
	}

	private bool ShouldCreateRuntimeGround()
	{
		return GameContext.Lobby != null && (GameContext.Lobby.IsGameStarted || IsBuildingMatchWorld);
	}

	private string ResolveTerrainPath(string rawMapName)
	{
		string? resolvedDir = ResolveMapDirectory(rawMapName);
		if (!string.IsNullOrEmpty(resolvedDir))
			return Path.Combine(resolvedDir, "terrain.json");

		string normalizedRawMapName = rawMapName.Replace('\\', '/');
		string explicitPath = TryGetExplicitTerrainPath(normalizedRawMapName);
		if (!string.IsNullOrEmpty(explicitPath)) return explicitPath;

		return FallbackTerrainPath(rawMapName);
	}

	private string TryGetExplicitTerrainPath(string normalizedRawMapName)
	{
		if (!normalizedRawMapName.StartsWith("user://") && !normalizedRawMapName.StartsWith("res://") && !Path.IsPathRooted(normalizedRawMapName))
			return string.Empty;

		string checkDir = normalizedRawMapName;
		if (normalizedRawMapName.StartsWith("user://") || normalizedRawMapName.StartsWith("res://"))
			checkDir = ProjectSettings.GlobalizePath(normalizedRawMapName);

		return Directory.Exists(checkDir) ? Path.Combine(checkDir, "terrain.json") : string.Empty;
	}

	private string FallbackTerrainPath(string rawMapName)
	{
		string normalizedMapName = rawMapName.ToLower().Trim();
		string mapDir = $"res://Maps/{normalizedMapName}";
		if (Directory.Exists(ProjectSettings.GlobalizePath(mapDir)))
			return $"res://Maps/{normalizedMapName}/terrain.json";

		string userDir = ProjectSettings.GlobalizePath($"user://maps/{normalizedMapName}");
		if (Directory.Exists(userDir))
			return $"user://maps/{normalizedMapName}/terrain.json";

		return $"res://Maps/{normalizedMapName}/terrain.json";
	}

	private void CreateRuntimeGround(string terrainPath)
	{
		var activeTerrainNode = new RuntimeTerrain();
		activeTerrainNode.Name = "Ground";
		AddChild(activeTerrainNode);
		GroundTerrain = activeTerrainNode;

		string targetPath = terrainPath.StartsWith("user://") || terrainPath.StartsWith("res://")
			? ProjectSettings.GlobalizePath(terrainPath)
			: terrainPath;

		if (!File.Exists(targetPath)) return;

		if (IsBuildingMatchWorld || _worldBuildRunning) return;

		LoadMapFromFile(terrainPath, true, false);
	}

	public void SetSkyboxTexture(string path)
	{
		var worldEnv = GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
		if (worldEnv == null || worldEnv.Environment == null) return;

		string fullPath = GetSkyboxFullPath(path);
		Texture2D? skyTexture = LoadSkyboxTexture(fullPath);

		if (skyTexture != null)
		{
			worldEnv.Environment.Sky = new Sky { SkyMaterial = new PanoramaSkyMaterial { Panorama = skyTexture } };
			worldEnv.Environment.BackgroundMode = Environment.BGMode.Sky;
			EditorSkyboxPath = path;
		}
	}

	private string GetSkyboxFullPath(string path)
	{
		if (path.StartsWith("res://") || File.Exists(path)) return path;

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string fullPath = Path.Combine(wsPath, path.Replace('/', Path.DirectorySeparatorChar));
		if (File.Exists(fullPath)) return fullPath;

		fullPath = Path.Combine(wsPath, "Assets", "skyboxes", Path.GetFileName(path));
		if (File.Exists(fullPath)) return fullPath;

		string rtexName = Path.GetFileNameWithoutExtension(path) + ".rtex";
		string rtexCandidate = Path.Combine(wsPath, "Assets", "skyboxes", rtexName);
		if (File.Exists(rtexCandidate)) return rtexCandidate;

		return path;
	}

	private Texture2D? LoadSkyboxTexture(string fullPath)
	{
		if (fullPath.StartsWith("res://"))
		{
			return ResourceLoader.Exists(fullPath) ? GD.Load<Texture2D>(fullPath) : null;
		}

		if (!File.Exists(fullPath)) return null;

		if (fullPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			byte[]? webpBytes = RtexFile.GetLayer(File.ReadAllBytes(fullPath), 0);
			if (webpBytes != null && webpBytes.Length > 0)
			{
				var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
				if (img.LoadWebpFromBuffer(webpBytes) != Error.Ok) img.LoadPngFromBuffer(webpBytes);
				return ImageTexture.CreateFromImage(img);
			}

			return null;
		}

		var standardImg = Image.LoadFromFile(fullPath);
		return standardImg != null ? ImageTexture.CreateFromImage(standardImg) : null;
	}

	private void SetupSkybox()
	{
		SetSkyboxTexture(EditorSkyboxPath);
	}

	private Prop3D? FindProp3DInParentChain(Node? node)
	{
		while (node != null)
		{
			if (node is Prop3D prop)
			{
				return prop;
			}

			node = node.GetParent();
		}

		return null;
	}

	private void RemoveIfHas<T>(Entity entity) where T : unmanaged
	{
		if (World.Has<T>(entity)) World.Remove<T>(entity);
	}

	public void ClearUnitOrders(Entity entity)
	{
		UnitOrderService.ClearOrders(World, entity);
	}

	private void StopGatheringMovement(Entity entity)
	{
		if (World.IsAlive(entity))
		{
			if (World.Has<MoveTo>(entity)) World.Remove<MoveTo>(entity);
		}
	}

	private Prop3D? FindNearbyResourceNode(Vector3 pos, string type, float radius)
	{
		Prop3D? closest = null;
		float closestDist = radius;
		foreach (var prop in AllProps)
		{
			if (GodotObject.IsInstanceValid(prop))
			{
				string? pType = prop.PropId switch
				{
					"goldmine" => "gold",
					"tree" => "wood",
					"rock" => "stone",
					_ => null
				};

				if (pType == type)
				{
					float d = pos.DistanceTo(prop.GlobalPosition);
					if (d < closestDist)
					{
						closestDist = d;
						closest = prop;
					}
				}
			}
		}

		return closest;
	}

	private const float ResourceNodeClickReach = 3.5f;

	/// <summary>
	///     Finds the resource node a right-click landed on. A static prop is drawn by a multimesh and has no node of its
	///     own to hit, so the click is resolved against the replicated props around the point it landed on.
	/// </summary>
	/// <param name="worldPosition">The point on the ground the click landed at.</param>
	/// <param name="nodeEntity">The resource node nearest that point, or <see cref="Entity.Null" />.</param>
	/// <returns>True when a resource node stands within reach of the click.</returns>
	public bool TryFindResourceNodeAt(Vector3 worldPosition, out Entity nodeEntity)
	{
		Entity found = Entity.Null;
		var clickPosition = new System.Numerics.Vector3(worldPosition.X, worldPosition.Y, worldPosition.Z);
		float closestDistance = ResourceNodeClickReach;

		var query = QueryCache.AllPropIdentityAndPositionQuery;
		World.Query(in query, (Entity entity, ref Position position, ref PropIdentity identity) =>
		{
			if (GetPropResourceType(identity.PropId) == null) return;

			float distance = System.Numerics.Vector3.Distance(position.Value, clickPosition);
			if (distance >= closestDistance) return;

			closestDistance = distance;
			found = entity;
		});

		nodeEntity = found;
		return nodeEntity != Entity.Null;
	}

	/// <summary>
	///     Sends a gather order for a click that landed on a resource node, which is how a right-click on a mine or a tree
	///     line reaches the selected workers without a node to hit.
	/// </summary>
	/// <param name="worldPosition">The point on the ground the click landed at.</param>
	/// <param name="isQueued">Whether the order is appended behind the units' current ones.</param>
	/// <returns>True when the click named a resource node and a selected unit can gather from it.</returns>
	public bool TryIssueGatherCommandAt(Vector3 worldPosition, bool isQueued = false)
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return false;
		if (!TryFindResourceNodeAt(worldPosition, out Entity nodeEntity)) return false;
		if (!HasSelectedResourceWorker()) return false;

		IssueGatherCommand(nodeEntity, isQueued);
		return true;
	}

	private bool HasSelectedResourceWorker()
	{
		foreach (Unit3D unit in SelectedUnits)
		{
			if (unit.IsBuilding || !GameContext.PerspectiveFilter.CanControl(unit.Player) || !CanCommandUnit(unit)) continue;
			if (GameContext.RegistryService.IsResourceWorkerUnit(unit.UnitId)) return true;
		}

		return false;
	}

	public void IssueGatherCommand(Entity propEntity, bool isQueued = false)
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		if (SelectedUnits.Count == 0 || propEntity == Entity.Null || !World.IsAlive(propEntity)) return;

		string propId = World.Has<PropIdentity>(propEntity) ? World.Get<PropIdentity>(propEntity).PropId : "";
		string? resType = GetPropResourceType(propId);
		if (resType == null) return;

		Vector3 propPos = Vector3.Zero;
		if (World.Has<Position>(propEntity))
		{
			var p = World.Get<Position>(propEntity).Value;
			propPos = new Vector3(p.X, p.Y, p.Z);
		}

		SpawnTargetIndicator(propPos, new Color(0.9f, 0.8f, 0.1f));
		GameContext.InGameHud?.ShowFeedbackText($"Gathering {resType.ToUpper()} from {propId}", new Color(0.9f, 0.8f, 0.1f));

		SubmitGatherOrderCommand(propEntity, propPos, isQueued);
	}

	private string? GetPropResourceType(string propId)
	{
		return GameContext.RegistryService.GetResourceTypeForNode(propId);
	}

	private void SubmitGatherOrderCommand(Entity propEntity, Vector3 propPos, bool isQueued)
	{
		int localPlayerIndex = GameContext.PerspectiveFilter.ControlledPlayerIndex;
		if (localPlayerIndex <= 0) return;
		var targetIds = new List<int>();
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.Player != localPlayerIndex || !CanCommandUnit(unit)) continue;
			if (!GameContext.RegistryService.IsResourceWorkerUnit(unit.UnitId)) continue;
			targetIds.Add(GetServerEntityId(unit.Entity));
		}

		SubmitOrderCommand(isQueued ? "gather_queued" : "gather", targetIds, propPos, GetServerEntityId(propEntity), string.Empty);
	}

	public void IssueGatherCommand(Prop3D prop, bool isQueued = false)
	{
		if (prop != null && GodotObject.IsInstanceValid(prop))
		{
			IssueGatherCommand(prop.Entity, isQueued);
		}
	}

	/// <summary>
	///     Answers whether a right-click on this building is the player asking a selected unit to work on it: the building has
	///     to be a site still being raised or a standing building that has lost health, and one of the selected units has to
	///     carry the repair rate that makes building work its job.
	/// </summary>
	private bool HasSelectedRepairerFor(Unit3D building)
	{
		if (building == null || !GodotObject.IsInstanceValid(building) || !building.IsBuilding) return false;
		if (building.Entity == Entity.Null || !World.IsAlive(building.Entity)) return false;
		if (!IsBuildingWorkAvailable(building.Entity)) return false;

		foreach (Unit3D unit in SelectedUnits)
		{
			if (unit == null || !GodotObject.IsInstanceValid(unit) || unit.IsBuilding || !CanCommandUnit(unit)) continue;
			if (!World.TryGet(unit.Entity, out RepairWorker repairWorker) || repairWorker.HealthPercentPerSecond <= 0f) continue;
			if (GameContext.PerspectiveFilter.IsHostileToObserved(building.Player)) continue;

			return true;
		}

		return false;
	}

	/// <summary>
	///     Answers whether a building still has work left in it: a site that has not been raised yet, or a repairable building
	///     short of its maximum health. A whole building offers no work, so a right-click on it is not a build order.
	/// </summary>
	private bool IsBuildingWorkAvailable(Entity buildingEntity)
	{
		if (World.Has<UnderConstruction>(buildingEntity)) return World.Has<ConstructionState>(buildingEntity);
		if (!World.Has<RepairableBuilding>(buildingEntity)) return false;

		return World.TryGet(buildingEntity, out Health health) && health.Current < health.Max;
	}

	/// <summary>Asks the selected units that can work on buildings to raise a site or mend a standing one.</summary>
	public void IssueRepairCommand(Unit3D building, bool isQueued = false)
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		if (SelectedUnits.Count == 0 || building == null || !GodotObject.IsInstanceValid(building)) return;
		if (!World.IsAlive(building.Entity)) return;

		int localPlayerIndex = GameContext.PerspectiveFilter.ControlledPlayerIndex;
		if (localPlayerIndex <= 0) return;

		var targetIds = new List<int>();
		foreach (Unit3D unit in SelectedUnits)
		{
			if (unit.IsBuilding || unit.Player != localPlayerIndex || !CanCommandUnit(unit)) continue;
			if (!World.TryGet(unit.Entity, out RepairWorker repairWorker) || repairWorker.HealthPercentPerSecond <= 0f) continue;
			targetIds.Add(GetServerEntityId(unit.Entity));
		}

		if (targetIds.Count == 0) return;

		var waypoint = World.TryGet(building.Entity, out Position position) ? position.Value : System.Numerics.Vector3.Zero;
		Vector3 buildingPosition = new(waypoint.X, waypoint.Y, waypoint.Z);
		SpawnTargetIndicator(buildingPosition, new Color(0.45f, 0.85f, 0.95f));

		bool raising = World.Has<UnderConstruction>(building.Entity);
		string feedback = raising ? TranslationServer.Translate("Raising the site") : TranslationServer.Translate("Repairing");
		GameContext.InGameHud?.ShowFeedbackText(feedback, new Color(0.45f, 0.85f, 0.95f));

		SubmitOrderCommand(isQueued ? "repair_queued" : "repair", targetIds, buildingPosition, GetServerEntityId(building.Entity), string.Empty);
	}
}
