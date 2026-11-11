using System.Collections.Generic;
using Realm.Ecs;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;

namespace Realm.Client.Services;

public class AttackTargetResolver
{
	private static readonly QueryDescription AttackTargetQuery = new QueryDescription().WithAll<UnitOwnerPlayer, AttackTarget>();

	private readonly List<Entity> _targets = new();
	private readonly EntityCallback<UnitOwnerPlayer, AttackTarget> _collectTarget;
	private EcsWorld? _world;
	private int _localSlot;

	public AttackTargetResolver()
	{
		_collectTarget = CollectTarget;
	}

	public IReadOnlyList<Entity> Collect()
	{
		_targets.Clear();

		_localSlot = GameContext.ServerSession?.LocalPlayerSlot ?? -1;
		_world = GameContext.World;
		if (_world == null || _localSlot <= 0 || GameContext.SessionState.IsMapEditorMode) return _targets;

		_world.Query(in AttackTargetQuery, _collectTarget);
		return _targets;
	}

	private void CollectTarget(Entity entity, ref UnitOwnerPlayer owner, ref AttackTarget attackTarget)
	{
		if (owner.Value != _localSlot) return;

		Entity target = attackTarget.Target;
		if (target == Entity.Null || _world == null || !_world.IsAlive(target)) return;
		if (Contains(target)) return;

		_targets.Add(target);
	}

	private bool Contains(Entity target)
	{
		for (int index = 0; index < _targets.Count; index++)
		{
			if (_targets[index].Id == target.Id) return true;
		}

		return false;
	}
}
