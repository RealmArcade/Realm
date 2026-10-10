using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Client.ReplaySystem;
using System.Collections.Generic;

namespace Realm.Client.Core;

public partial class GameHost
{
	public void ResetStateForReplayPlayback()
	{
		ClearInitialState();

		_isResettingForReplay = true;
		try
		{
			ReinitializeEcsAndServices();
			InitializeMapScript();
		}
		finally
		{
			_isResettingForReplay = false;
		}

		SetupReplayPlayers();
	}

	private void ClearInitialState()
	{
		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit)) unit.QueueFree();
		}
		AllUnits.Clear();
		
		foreach (var prop in AllProps)
		{
			if (GodotObject.IsInstanceValid(prop)) prop.QueueFree();
		}
		AllProps.Clear();
		
		EntityToUnit3D.Clear();
		EntityToProp3D.Clear();
	}

	private void InitializeMapScript()
	{
		if (_activeMapScript == null) return;
		
		_activeMapScript.Initialize(this);
		
		foreach (var unit in AllUnits)
		{
			if (EcsWorld.IsAlive(unit.Entity)) EcsWorld.Destroy(unit.Entity);
			if (GodotObject.IsInstanceValid(unit)) unit.QueueFree();
		}
		AllUnits.Clear();
		EntityToUnit3D.Clear();
	}

	private void SetupReplayPlayers()
	{
		var players = new List<(int PeerId, string Name)>();
		if (ReplayPlaybackManager.Instance.Header.Players != null)
		{
			foreach (var p in ReplayPlaybackManager.Instance.Header.Players)
			{
				players.Add((p.PeerId, p.Name));
			}
		}
		_replayService.SetupPlayersForPlayback(players);
	}

	public void SpawnUnitFromReplaySnapshot(ReplayUnitSnapshot snap)
	{
		var result = _replayService.SpawnUnitFromReplaySnapshot(snap);
		if (result.Entity == default) return;

		string targetModel = !string.IsNullOrEmpty(result.ModelPath) ? result.ModelPath : snap.UnitId;
		string modelPath = GetFallbackModelPath(targetModel, snap.IsBuilding);
		SpawnUnit3D(result.Entity, snap.UnitId, modelPath, snap.Position.ToGodot(), snap.IsBuilding, result.IsEnemy);
	}

	private void RecordGameplayTick()
	{
		foreach (var unit in AllUnits)
		{
			if (!GodotObject.IsInstanceValid(unit)) continue;

			if (EcsWorld.Has<Position>(unit.Entity))
			{
				EcsWorld.Set(unit.Entity, new Position(new System.Numerics.Vector3(unit.GlobalPosition.X, unit.GlobalPosition.Y, unit.GlobalPosition.Z)));
			}

			EcsWorld.SetOrAdd(unit.Entity, new Velocity(new System.Numerics.Vector3(unit.Velocity.X, unit.Velocity.Y, unit.Velocity.Z)));

			EcsWorld.SetOrAdd(unit.Entity, new RotationY(unit.GlobalRotation.Y));
		}

		_replayService.RecordGameplayTick();
	}
}
