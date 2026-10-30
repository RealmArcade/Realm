using System.Collections.Generic;
using Godot;
using Realm.Ecs;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Services;
using Realm.Shared.Network;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Core;

public partial class GameHost
{
	public void ResetStateForReplayPlayback()
	{
		ClearInitialStateView();

		GameContext.ReplayService.IsResettingForReplay = true;
		try
		{
			ReinitializeEcsAndServices();
		}
		finally
		{
			GameContext.ReplayService.IsResettingForReplay = false;
		}

		SetupReplayPlayers();
	}

	private void ClearInitialStateView()
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
		EntityIdToUnit3D.Clear();
		EntityToProp3D.Clear();
	}


	private void SetupReplayPlayers()
	{
		var players = new List<(int PeerId, string Name)>();
		if (GameContext.ReplayPlayback.Header.Players != null)
		{
			foreach (var p in GameContext.ReplayPlayback.Header.Players)
			{
				players.Add((p.PeerId, p.Name));
			}
		}

		GameContext.ReplayService.SetupPlayersForPlayback(players);
	}

	public void SpawnUnitFromReplaySnapshot(ReplayUnitSnapshot snap)
	{
		var result = GameContext.ReplayService.SpawnUnitFromReplaySnapshot(snap);
		if (result.Entity == Entity.Null) return;

		SpawnUnit3D(result.Entity, snap.UnitId, result.ModelPath, new Godot.Vector3(snap.Position.X, snap.Position.Y, snap.Position.Z), snap.IsBuilding, result.Player);
	}

	private void RecordGameplayTick()
	{
		var transforms = new List<(Entity Entity, System.Numerics.Vector3 Position, System.Numerics.Vector3 Velocity, float RotationY)>();
		foreach (var unit in AllUnits)
		{
			if (!GodotObject.IsInstanceValid(unit)) continue;
			transforms.Add((
				unit.Entity, 
				new System.Numerics.Vector3(unit.GlobalPosition.X, unit.GlobalPosition.Y, unit.GlobalPosition.Z), 
				new System.Numerics.Vector3(unit.Velocity.X, unit.Velocity.Y, unit.Velocity.Z), 
				unit.GlobalRotation.Y
			));
		}
		GameContext.ReplayService.RecordGameplayTickDomain(transforms);
	}
}