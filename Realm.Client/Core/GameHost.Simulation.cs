using System;
using System.Collections.Generic;
using Realm.Ecs;
using Godot;
using Realm.Client.ReplaySystem;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using UnitId = Realm.Ecs.UnitId;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Core;

public partial class GameHost
{

	private void KillUnit(Unit3D unit)
	{
		KillUnit(unit, true, true);
	}

	private void KillUnit(Unit3D unit, bool executeDespawnShader, bool playDeathAnimation)
	{
		UnitId victimId = World.IsAlive(unit.Entity) ? (UnitId)unit.Entity.Id : UnitId.Invalid;
		Entity killerEntity = GameContext.SimulationService.GetKillerEntityDomain(unit.Entity);
		UnitId killerId = (killerEntity != Entity.Null && World.IsAlive(killerEntity)) ? (UnitId)killerEntity.Id : UnitId.Invalid;

		if (victimId.IsValid)
		{
			OnUnitDied?.Invoke(victimId, killerId);
		}

		GameContext.AudioService?.PlayUnitSound(unit.UnitId, UnitSoundEvent.Death, unit.GlobalPosition);
		RemoveUnitFromCollections(unit);
		HandleUnitDeathEconomy(unit);
		HandleMultiplayerUnitDeath(unit);

		string unitId = unit.UnitId;
		int unitPlayer = unit.Player;

		GameContext.FXService.NotifyVfxTargetDied(unit.Entity.Id);
		GameContext.SimulationService.DestroyUnitEntityDomain(unit.Entity);

		PlayDeathEffects(unit, executeDespawnShader, playDeathAnimation, unitId);

		GameLog.LogToConsole($"Unit {unit.Name} died.");
	}

	private void RemoveUnitFromCollections(Unit3D unit)
	{
		SelectedUnits.Remove(unit);
		_activeImpulseUnits.Remove(unit);
		AllUnits.Remove(unit);
		EntityToUnit3D.Remove(unit.Entity);
		EntityIdToUnit3D.Remove(unit.Entity.Id);
		if (PlayerToUnits.TryGetValue(unit.Player, out var pList))
		{
			pList.Remove(unit);
		}

		if (!unit.IsBuilding) return;

		var fallbackPos = new System.Numerics.Vector3(unit.Position.X, unit.Position.Y, unit.Position.Z);
		var (radius, unitPos) = GameContext.SimulationService.GetObstacleUncarveData(unit.Entity, fallbackPos);
		UncarveObstacle(new Vector3(unitPos.X, unitPos.Y, unitPos.Z), radius);
	}

	private void HandleUnitDeathEconomy(Unit3D unit)
	{
		HandleUnitBountyEconomy(unit);
	}

	private void HandleUnitBountyEconomy(Unit3D unit)
	{
		float resCap = GameContext.Simulation.PlayerResourceService.ResourceCap;
		Entity beneficiaryPlayerEntity = Entity.Null;

		if (World.IsAlive(unit.Entity) && World.Has<LastAttacker>(unit.Entity))
		{
			Entity killer = World.Get<LastAttacker>(unit.Entity).Value;
			if (World.IsAlive(killer) && World.Has<Owner>(killer))
			{
				beneficiaryPlayerEntity = World.Get<Owner>(killer).PlayerEntity.Value;
			}
		}

		if (beneficiaryPlayerEntity == Entity.Null && (GameContext.Lobby == null || GameContext.Lobby.IsSinglePlayer))
		{
			int localSlot = GameContext.PerspectiveFilter.ControlledPlayerIndex;
			beneficiaryPlayerEntity = GetPlayerEntityForPlayerIndex(localSlot);
		}

		if (beneficiaryPlayerEntity != Entity.Null)
		{
			bool shouldRefresh = GameContext.SimulationService.HandleEnemyDeathEconomyDomain(unit.Entity, unit.UnitId, beneficiaryPlayerEntity, (int)resCap);
			
			int localViewingSlot = GameContext.PerspectiveFilter.ControlledPlayerIndex;
			Entity localPlayerEntity = GetPlayerEntityForPlayerIndex(localViewingSlot);
			if (shouldRefresh && beneficiaryPlayerEntity == localPlayerEntity)
			{
				GameContext.InGameHud?.RefreshUI(SelectedUnits);
			}
		}
	}

	private void HandleMultiplayerUnitDeath(Unit3D unit)
	{
		if (!_multiplayerActive) return;

		GameContext.NetworkService.RemoveClientToServerMapping(unit.Entity.Id);
	}

	private void PlayDeathEffects(Unit3D unit, bool executeDespawnShader, bool playDeathAnimation, string unitId)
	{
		if (!GodotObject.IsInstanceValid(unit)) return;

		if (playDeathAnimation)
		{
			unit.PlayAnimation("Death");
		}


		if (executeDespawnShader && TryExecuteDeathShader(unit, unitId)) return;
		if (playDeathAnimation && TryPlayDeathTween(unit)) return;

		unit.QueueFree();
	}

	private bool TryExecuteDeathShader(Unit3D unit, string unitId)
	{
		if (unit.HasPlayedDespawnShader) return true;

		string deathShader = GetModelDeathShader(unitId);
		if (string.IsNullOrEmpty(deathShader))
		{
			deathShader = GetModelDeathShader(unit);
		}

		return unit.TryPlayDespawnShader(deathShader, () =>
		{
			if (GodotObject.IsInstanceValid(unit)) unit.QueueFree();
		});
	}

	private bool TryPlayDeathTween(Unit3D unit)
	{
		var tween = CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(unit, "position:y", -3.0f, 1.0f);
		tween.TweenProperty(unit, "scale", new Godot.Vector3(0.001f, 0.001f, 0.001f), 1.0f);
		tween.Chain().TweenCallback(Callable.From(unit.QueueFree));
		return true;
	}

	private float ConstructionWorkRatePerSecond => GameContext.SimulationService.FastBuildEnabled ? SimulationService.BaseConstructionWorkRatePerSecond * 10f : SimulationService.BaseConstructionWorkRatePerSecond;


	private List<(Entity Worker, BuildTask UpdatedTask)> _pendingBuildTaskUpdates => GameContext.SimulationService.PendingBuildTaskUpdates;

	private Dictionary<int, List<MeshInstance3D>> _buildQueueGhosts { get; set; } = new();

}