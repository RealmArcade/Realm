using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Ecs;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Services;
using Realm.MapAPI;

namespace Realm.Client.Testing;

public partial class MultiplayerSimulationHarness
{
	private int _duelAttackerEntityId = -1;
	private int _duelDefenderEntityId = -1;
	private int _meleeAttackerEntityId = -1;
	private int _meleeDefenderEntityId = -1;
	private int _clientDuelAttackerEntityId = -1;
	private int _clientDuelDefenderEntityId = -1;
	private string _meleeUnitId = string.Empty;
	private bool _clientCommandIssued;

	private async Task RunCombatProbeAsync(bool isHost)
	{
		await ToSignal(GetTree().CreateTimer(3.0f), SceneTreeTimer.SignalName.Timeout);
		if (isHost) SetupDuelUnits();

		for (int sample = 0; sample < 16; sample++)
		{
			LogCombatProbe(isHost, sample);
			if (!isHost && sample == 3)
			{
				ProbeBuildingPlacement();
				_ = ProbeBuildingPreviewRefreshAsync();
			}

			await ToSignal(GetTree().CreateTimer(0.5f), SceneTreeTimer.SignalName.Timeout);
		}
	}

	private void SetupDuelUnits()
	{
		try
		{
			GameHost host = GameHost.Instance;
			if (host == null || GameContext.World == null) return;

			string rangedUnitId = string.Empty;
			float bestRange = 0f;
			foreach (var entry in GameHost.UnitRegistry)
			{
				if (entry.Value == null) continue;
				if (entry.Value.Damage <= 0f || entry.Value.Range <= 3f) continue;
				string candidate = entry.Key.ToString();
				if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(entry.Value.ModelPath)) continue;
				if (entry.Value.Weapons == null || entry.Value.Weapons.Length == 0) continue;
				if (entry.Value.Range <= bestRange) continue;
				bestRange = entry.Value.Range;
				rangedUnitId = candidate;
			}

			if (string.IsNullOrEmpty(rangedUnitId))
			{
				GD.PrintErr("[COMBAT_PROBE] No ranged unit archetype was found in the registry.");
				return;
			}

			Unit3D anchorUnit = host.AllUnits.Find(unit => unit != null && GodotObject.IsInstanceValid(unit) && !unit.IsBuilding);
			Vector3 anchor = anchorUnit != null ? anchorUnit.GlobalPosition : Vector3.Zero;
			var spawnPoint = new System.Numerics.Vector2(anchor.X + 40f, anchor.Z + 40f);

			UnitId attackerId = ((IGameAPI)host).SpawnUnitForPlayer(rangedUnitId, spawnPoint, 1, false, true);
			UnitId defenderId = ((IGameAPI)host).SpawnUnitForPlayer(rangedUnitId, spawnPoint + new System.Numerics.Vector2(4f, 0f), 3, false, true);

			_duelAttackerEntityId = attackerId.Value;
			_duelDefenderEntityId = defenderId.Value;

			GD.Print($"[COMBAT_PROBE] ranged duel unit='{rangedUnitId}' at {spawnPoint} attacker={_duelAttackerEntityId} defender={_duelDefenderEntityId} enemies={NetworkService.ArePlayerIndicesEnemies(1, 3)}");

			string meleeUnitId = string.Empty;
			foreach (var entry in GameHost.UnitRegistry)
			{
				if (entry.Value == null) continue;
				if (entry.Value.Damage <= 0f || entry.Value.Range > 3f) continue;
				string candidate = entry.Key.ToString();
				if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(entry.Value.ModelPath)) continue;
				meleeUnitId = candidate;
				break;
			}

			if (!string.IsNullOrEmpty(meleeUnitId))
			{
				var meleePoint = new System.Numerics.Vector2(spawnPoint.X + 20f, spawnPoint.Y);
				UnitId meleeAttackerId = ((IGameAPI)host).SpawnUnitForPlayer(meleeUnitId, meleePoint, 1, false, true);
				UnitId meleeDefenderId = ((IGameAPI)host).SpawnUnitForPlayer(meleeUnitId, meleePoint + new System.Numerics.Vector2(2.5f, 0f), 3, false, true);
				_meleeAttackerEntityId = meleeAttackerId.Value;
				_meleeDefenderEntityId = meleeDefenderId.Value;
				_meleeUnitId = meleeUnitId;
				GD.Print($"[COMBAT_PROBE] melee duel unit='{meleeUnitId}' at {meleePoint} attacker={_meleeAttackerEntityId} defender={_meleeDefenderEntityId}");
			}

			int clientSlot = 12;
		var clientInfo = LobbyManager.Instance?.PlayerList?.Find(player => player.PeerId > 1);
		if (clientInfo != null) clientSlot = clientInfo.Slot;

			string commandUnitId = string.IsNullOrEmpty(meleeUnitId) ? rangedUnitId : meleeUnitId;
			var commandPoint = new System.Numerics.Vector2(spawnPoint.X - 20f, spawnPoint.Y);
			UnitId commandAttackerId = ((IGameAPI)host).SpawnUnitForPlayer(commandUnitId, commandPoint, clientSlot, false, true);
			UnitId commandDefenderId = ((IGameAPI)host).SpawnUnitForPlayer(rangedUnitId, commandPoint + new System.Numerics.Vector2(8f, 0f), 1, false, true);
			_clientDuelAttackerEntityId = commandAttackerId.Value;
			_clientDuelDefenderEntityId = commandDefenderId.Value;
			GD.Print($"[COMBAT_PROBE] human duel unit='{commandUnitId}' at {commandPoint} attacker={_clientDuelAttackerEntityId} player={clientSlot} defender={_clientDuelDefenderEntityId} player=1");

			string lootItemId = string.Empty;
			var itemRegistry = GameContext.RegistryService?.ItemRegistry;
			if (itemRegistry != null)
			{
				foreach (var key in itemRegistry.Keys)
				{
					lootItemId = key.ToString();
					break;
				}
			}

			if (!string.IsNullOrEmpty(lootItemId))
			{
				var lootPosition = new System.Numerics.Vector3(commandPoint.X, 0f, commandPoint.Y + 6f);
				LootItemId spawnedLoot = ((IGameAPI)host).SpawnLootItem(lootItemId, lootPosition, 3, 20f);
				GD.Print($"[COMBAT_PROBE] loot item='{lootItemId}' id={spawnedLoot.Value} charges=3 lifetime=20 pickupRadius=2 position={lootPosition}");
			}
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[COMBAT_PROBE] Setup failed: {exception}");
		}
	}

	private static string DescribeDuel(int attackerId, int defenderId)
	{
		if (attackerId < 0 || defenderId < 0) return "not spawned";

		var world = GameContext.World;
		Entity attacker = ResolveDuelEntity(attackerId);
		Entity defender = ResolveDuelEntity(defenderId);
		if (!world.IsAlive(attacker) || !world.IsAlive(defender))
		{
			return $"dead attackerAlive={world.IsAlive(attacker)} defenderAlive={world.IsAlive(defender)}";
		}

		float attackerHp = world.Has<Health>(attacker) ? world.Get<Health>(attacker).Current : -1f;
		float defenderHp = world.Has<Health>(defender) ? world.Get<Health>(defender).Current : -1f;
		bool attackerTargeted = world.Has<AttackTarget>(attacker);
		bool defenderTargeted = world.Has<AttackTarget>(defender);
		float distance = 0f;
		if (world.Has<Position>(attacker) && world.Has<Position>(defender))
		{
			distance = System.Numerics.Vector3.Distance(world.Get<Position>(attacker).Value, world.Get<Position>(defender).Value);
		}

		return $"attackerHp={attackerHp:F0} defenderHp={defenderHp:F0} attackerTargeted={attackerTargeted} defenderTargeted={defenderTargeted} distance={distance:F1}";
	}

	private static Entity ResolveDuelEntity(int entityId)
	{
		return GameHost.EntityIdToUnit3D.TryGetValue(entityId, out Unit3D unit) && GodotObject.IsInstanceValid(unit) ? unit.Entity : Entity.Null;
	}

	private void LogCombatProbe(bool isHost, int sample)
	{
		try
		{
			GameHost host = GameHost.Instance;
			if (host == null || GameContext.World == null) return;

			int attackUnits = 0;
			int targetedUnits = 0;
			int damagedUnits = 0;
			foreach (var unit in host.AllUnits)
			{
				if (unit == null || !GodotObject.IsInstanceValid(unit)) continue;
				var world = GameContext.World;
				if (!world.IsAlive(unit.Entity)) continue;
				if (world.Has<Attack>(unit.Entity)) attackUnits++;
				if (world.Has<AttackTarget>(unit.Entity)) targetedUnits++;
				if (world.Has<Health>(unit.Entity) && world.Get<Health>(unit.Entity).Current < world.Get<Health>(unit.Entity).Max) damagedUnits++;
			}

			if (!isHost && !_clientCommandIssued && sample == 2)
			{
				_clientCommandIssued = true;
				IssueClientAttackCommand(host);
			}

			string duelSummary = isHost
				? $"ranged=[{DescribeDuel(_duelAttackerEntityId, _duelDefenderEntityId)}] melee=[{DescribeDuel(_meleeAttackerEntityId, _meleeDefenderEntityId)}] clientDuel=[{DescribeDuel(_clientDuelAttackerEntityId, _clientDuelDefenderEntityId)}]"
				: DescribeClientOwnUnit(host);

			string placement = isHost ? string.Empty : $" activePlacement={host.ActiveBuildingPlacementType ?? "none"}";
			GD.Print($"[COMBAT_PROBE] role={(isHost ? "host" : "client")} sample={sample} time={host.GameElapsedTime:F1} units={host.AllUnits.Count} attackUnits={attackUnits} targeted={targetedUnits} damaged={damagedUnits} projectiles={VisualProjectilePool.Instance.ActiveCount} allocated={VisualProjectilePool.Instance.TotalAllocated} {duelSummary}{placement}");
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[COMBAT_PROBE] Sample failed: {exception}");
		}
	}

	private void IssueClientAttackCommand(GameHost host)
	{
		try
		{
			int localPlayer = host.LocalPlayerIndex;
			Unit3D ownUnit = host.AllUnits.Find(unit => unit != null && GodotObject.IsInstanceValid(unit) && unit.Player == localPlayer && GameContext.World.Has<Attack>(unit.Entity));
			if (ownUnit == null)
			{
				GD.PrintErr($"[CLIENT_CMD] no own combat unit was found for player {localPlayer}.");
				return;
			}

			Unit3D enemyUnit = null;
			float bestDistance = float.MaxValue;
			foreach (var candidate in host.AllUnits)
			{
				if (candidate == null || !GodotObject.IsInstanceValid(candidate)) continue;
				if (candidate.Player == localPlayer) continue;
				if (!GameContext.World.Has<Attack>(candidate.Entity)) continue;
				float distance = candidate.GlobalPosition.DistanceTo(ownUnit.GlobalPosition);
				if (distance >= bestDistance) continue;
				bestDistance = distance;
				enemyUnit = candidate;
			}

			if (enemyUnit == null)
			{
				GD.PrintErr("[CLIENT_CMD] no enemy unit was found.");
				return;
			}

			bool hostile = GameContext.PerspectiveFilter.IsHostileToObserved(enemyUnit.Player);
			GD.Print($"[CLIENT_CMD] own='{ownUnit.UnitId}' player={ownUnit.Player} enemy='{enemyUnit.UnitId}' player={enemyUnit.Player} distance={bestDistance:F1} hostileToObserved={hostile} canControl={GameContext.PerspectiveFilter.CanControl(ownUnit.Player)}");

			host.SelectedUnits.Clear();
			host.SelectedUnits.Add(ownUnit);
			host.IssueAttackCommand(enemyUnit);
			GD.Print($"[CLIENT_CMD] issued attack; ownTargeted={GameContext.World.Has<AttackTarget>(ownUnit.Entity)}");
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[CLIENT_CMD] Command probe failed: {exception}");
		}
	}

	private static string DescribeClientOwnUnit(GameHost host)
	{
		int localPlayer = host.LocalPlayerIndex;
		Unit3D ownUnit = host.AllUnits.Find(unit => unit != null && GodotObject.IsInstanceValid(unit) && unit.Player == localPlayer && GameContext.World.Has<Attack>(unit.Entity));
		if (ownUnit == null) return $"own=none player={localPlayer}";

		var world = GameContext.World;
		bool targeted = world.Has<AttackTarget>(ownUnit.Entity);
		float ownHp = world.Has<Health>(ownUnit.Entity) ? world.Get<Health>(ownUnit.Entity).Current : -1f;
		string targetInfo = "target=none";
		if (targeted)
		{
			Entity target = world.Get<AttackTarget>(ownUnit.Entity).Target;
			float targetHp = world.IsAlive(target) && world.Has<Health>(target) ? world.Get<Health>(target).Current : -1f;
			var ownPos = world.Get<Position>(ownUnit.Entity).Value;
			var targetPos = world.IsAlive(target) && world.Has<Position>(target) ? world.Get<Position>(target).Value : ownPos;
			targetInfo = $"targetHp={targetHp:F0} targetDistance={System.Numerics.Vector3.Distance(ownPos, targetPos):F1}";
		}

		return $"ownHp={ownHp:F0} ownTargeted={targeted} {targetInfo}";
	}

	private void ProbeBuildingPlacement()
	{
		try
		{
			GameHost host = GameHost.Instance;
			if (host == null) return;

			string buildingId = string.Empty;
			foreach (var entry in GameHost.BuildingRegistry)
			{
				string candidate = entry.Key.ToString();
				if (string.IsNullOrEmpty(candidate)) continue;
				if (entry.Value == null) continue;
				if (string.IsNullOrEmpty(entry.Value.ModelPath)) continue;
				buildingId = candidate;
				break;
			}

			int localIndex = GameContext.PerspectiveFilter.ControlledPlayerIndex;
			var world = GameContext.World;
			Entity worldEntity = ECSContext.WorldEntity;
			bool hasMatrix = world.Has<PlayerRelationshipMatrix>(worldEntity);
			string matrixRelationship = hasMatrix ? world.Get<PlayerRelationshipMatrix>(worldEntity).GetRelationship(localIndex, 1).ToString() : "missing";
			bool hasPlayersState = world.Has<ScriptPlayersState>(worldEntity);
			int componentTeam = hasPlayersState ? world.Get<ScriptPlayersState>(worldEntity).Get(localIndex).Team : -1;
			int componentEnemyTeam = hasPlayersState ? world.Get<ScriptPlayersState>(worldEntity).Get(1).Team : -1;
			int bufferTeam = world.GetBuffer<ScriptPlayer>()?.Get(localIndex).Team ?? -1;
			GD.Print($"[PLACEMENT_PROBE] relations local={localIndex} matrixRelationshipToPlayer1={matrixRelationship} areEnemies={NetworkService.ArePlayerIndicesEnemies(localIndex, 1)} componentTeamLocal={componentTeam} componentTeamPlayer1={componentEnemyTeam} bufferTeamLocal={bufferTeam} getPlayerTeam={GameContext.SimulationService.GetPlayerTeam(localIndex)}");

			GD.Print($"[PLACEMENT_PROBE] spectating={GameContext.PerspectiveFilter.IsSpectating} controlled={GameContext.PerspectiveFilter.ControlledPlayerIndex} buildingRegistry={GameHost.BuildingRegistry.Count} unitRegistry={GameHost.UnitRegistry.Count} building='{buildingId}'");

			var center = GetViewport().GetVisibleRect().Size / 2f;
			bool terrainHit = host.TryRaycastTerrainFromMousePosition(center, out var terrainPosition);
			GD.Print($"[PLACEMENT_PROBE] terrainRaycastHit={terrainHit} position={terrainPosition}");

			host.EnterBuildingPlacement(buildingId);
			GD.Print($"[PLACEMENT_PROBE] afterEnter activePlacement={host.ActiveBuildingPlacementType ?? "none"}");

			var previewProperty = typeof(GameHost).GetProperty("_buildingPreviewMesh", BindingFlags.NonPublic | BindingFlags.Instance);
			Node3D preview = previewProperty?.GetValue(host) as Node3D;
			if (preview == null || !GodotObject.IsInstanceValid(preview))
			{
				GD.PrintErr("[PLACEMENT_PROBE] preview node is null after EnterBuildingPlacement.");
				return;
			}

			var meshCount = 0;
			var overrideCount = 0;
			string materialSummary = string.Empty;
			CollectMeshStats(preview, ref meshCount, ref overrideCount, ref materialSummary);

			Input.WarpMouse(center);
			string firstMaterialId = ResolveFirstMaterialId(preview);
			GD.Print($"[PLACEMENT_PROBE] previewType={preview.GetType().Name} visible={preview.Visible} meshes={meshCount} materialOverrides={overrideCount} material={materialSummary} materialIdBeforeTick={firstMaterialId} position={preview.GlobalPosition} mouse={GetViewport().GetMousePosition()} parent={preview.GetParent()?.Name}");
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[PLACEMENT_PROBE] Probe failed: {exception}");
		}
	}

	private async Task ProbeBuildingPreviewRefreshAsync()
	{
		GameHost host = GameHost.Instance;
		if (host == null || string.IsNullOrEmpty(host.ActiveBuildingPlacementType)) return;

		var previewProperty = typeof(GameHost).GetProperty("_buildingPreviewMesh", BindingFlags.NonPublic | BindingFlags.Instance);
		Node3D preview = previewProperty?.GetValue(host) as Node3D;
		string beforeId = ResolveFirstMaterialId(preview);
		await ToSignal(GetTree().CreateTimer(0.3f), SceneTreeTimer.SignalName.Timeout);
		Node3D refreshedPreview = previewProperty?.GetValue(host) as Node3D;
		string afterId = ResolveFirstMaterialId(refreshedPreview);
		GD.Print($"[PLACEMENT_PROBE] tickRefresh materialIdBefore={beforeId} materialIdAfter={afterId} refreshed={(beforeId != afterId)} position={refreshedPreview?.GlobalPosition.ToString() ?? "null"}");

		host.CancelBuildingPlacement();
	}

	private static string ResolveFirstMaterialId(Node node)
	{
		if (node == null || !GodotObject.IsInstanceValid(node)) return "none";

		if (node is MeshInstance3D meshInstance && meshInstance.MaterialOverride != null)
		{
			return meshInstance.MaterialOverride.GetInstanceId().ToString();
		}

		foreach (var child in node.GetChildren())
		{
			string childId = ResolveFirstMaterialId(child);
			if (childId != "none") return childId;
		}

		return "none";
	}

	private static void CollectMeshStats(Node node, ref int meshCount, ref int overrideCount, ref string materialSummary)
	{
		if (node is MeshInstance3D meshInstance)
		{
			meshCount++;
			if (meshInstance.MaterialOverride != null)
			{
				overrideCount++;
				if (meshInstance.MaterialOverride is StandardMaterial3D standard)
				{
					materialSummary += $"[albedo={standard.AlbedoColor} emission={standard.Emission} transparency={standard.Transparency}]";
				}
			}
		}

		foreach (var child in node.GetChildren())
		{
			CollectMeshStats(child, ref meshCount, ref overrideCount, ref materialSummary);
		}
	}
}
