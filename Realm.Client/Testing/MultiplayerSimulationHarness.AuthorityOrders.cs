using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;

namespace Realm.Client.Testing;

public partial class MultiplayerSimulationHarness
{
	private const int AuthorityOrderStartTick = 120;
	private const int AuthorityOrderSpacingTicks = 26;
	private const int AuthorityOrderCount = 16;

	private int _authorityOrderTick;
	private int _authorityOrderIndex;

	/// <summary>
	/// Issues move orders on the authority for units the client does not own. Nothing in the client can anticipate these,
	/// because they are not submitted through the command transport, so the delay until the client first sees the order
	/// is the measured cost of a host-only decision such as an AI order or a map-script order. Every order names the unit,
	/// the target and the tick it was issued on so a report can attribute the measurement.
	/// </summary>
	private void RunAuthorityOrderScenarioTick()
	{
		if (GameContext.World == null || GameContext.Host == null) return;
		if (!IsScenarioNavigationReady()) return;

		int tick = _authorityOrderTick++;
		if (_authorityOrderIndex >= AuthorityOrderCount) return;
		int dueTick = AuthorityOrderStartTick + _authorityOrderIndex * AuthorityOrderSpacingTicks;
		if (tick < dueTick) return;

		EcsWorld world = GameContext.World;
		GameHost gameHost = GameContext.Host;
		int minimumRemotePlayerIndex = _peerCount + 1;

		Unit3D? selected = null;
		for (int index = 0; index < gameHost.AllUnits.Count; index++)
		{
			var candidate = gameHost.AllUnits[index];
			if (!GodotObject.IsInstanceValid(candidate)) continue;
			if (candidate.IsBuilding) continue;
			if (candidate.Player < minimumRemotePlayerIndex) continue;
			if (!world.IsAlive(candidate.Entity)) continue;
			if (!world.Has<Movable>(candidate.Entity)) continue;
			if (!world.Has<UnitOwnerPlayer>(candidate.Entity)) continue;
			selected = candidate;
			break;
		}

		if (selected == null)
		{
			GD.PrintErr("[MP_HARNESS] No remote unit is available for an authority-only order.");
			return;
		}

		int orderIndex = _authorityOrderIndex++;
		float bearing = orderIndex * 0.7f;
		float distance = 10f + orderIndex % 3 * 3f;
		Vector3 target = selected.GlobalPosition + new Vector3(Mathf.Cos(bearing) * distance, 0f, Mathf.Sin(bearing) * distance);
		if (world.Has<PathFollow>(selected.Entity)) world.Remove<PathFollow>(selected.Entity);
		world.SetOrAdd(selected.Entity, new MoveTo(new System.Numerics.Vector3(target.X, target.Y, target.Z)));

		GD.Print($"[MP_HARNESS] Authority-only move order {orderIndex} at tick {tick}: unit {selected.Entity.Id} player {selected.Player} to ({target.X:0.00}, {target.Y:0.00}, {target.Z:0.00}).");
	}
}
