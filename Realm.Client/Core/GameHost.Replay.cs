using System.Collections.Generic;
using Godot;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Services;
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
			InitializeMapScript();
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
		EntityToProp3D.Clear();
	}

	private void InitializeMapScript()
	{
		if (_activeMapScript == null) return;

		try
		{
			_activeMapScript.Initialize(this);
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[GameHost.Replay] Exception during active map script Initialize: {ex.Message}");
		}

		var entitiesToDestroy = new List<Arch.Core.Entity>();
		foreach (var unit in AllUnits)
		{
			entitiesToDestroy.Add(unit.Entity);
			if (GodotObject.IsInstanceValid(unit)) unit.QueueFree();
		}

		GameContext.SimulationService.DestroyEntitiesDomain(entitiesToDestroy);

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

		GameContext.ReplayService.SetupPlayersForPlayback(players);
	}

	public void SpawnUnitFromReplaySnapshot(ReplayUnitSnapshot snap)
	{
		var result = GameContext.ReplayService.SpawnUnitFromReplaySnapshot(snap);
		if (result.Entity == default) return;

		SpawnUnit3D(result.Entity, snap.UnitId, result.ModelPath, snap.Position.ToGodot(), snap.IsBuilding, result.Player);
	}

	private void RecordGameplayTick()
	{
		var transforms = new List<(Arch.Core.Entity Entity, System.Numerics.Vector3 Position, System.Numerics.Vector3 Velocity, float RotationY)>();
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