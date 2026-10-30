using System;
using System.Collections.Generic;
using Godot;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Services;

public class UnitVisibilityIndex
{
	public const float VisionRadiusMeters = 15.0f;

	private const float CellSizeMeters = VisionRadiusMeters;

	private readonly Dictionary<long, List<int>> _playerEntityIdsByCell = new();
	private readonly EntityCallback<Position, Owner> _collectDelegate;
	private EcsWorld? _builtWorld;
	private ulong _builtFrame = ulong.MaxValue;

	public UnitVisibilityIndex()
	{
		_collectDelegate = Collect;
	}

	public void Invalidate()
	{
		_builtFrame = ulong.MaxValue;
		_builtWorld = null;
	}

	public bool IsVisibleToPlayer(int playerEntityId, Vector3 position)
	{
		if (playerEntityId <= 0) return false;
		Refresh();

		int cellX = ToCell(position.X);
		int cellZ = ToCell(position.Z);
		for (int offsetX = -1; offsetX <= 1; offsetX++)
		{
			for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
			{
				if (!_playerEntityIdsByCell.TryGetValue(PackCell(cellX + offsetX, cellZ + offsetZ), out List<int>? playerEntityIds)) continue;
				for (int index = 0; index < playerEntityIds.Count; index++)
				{
					if (playerEntityIds[index] == playerEntityId) return true;
				}
			}
		}

		return false;
	}

	private void Refresh()
	{
		EcsWorld? world = GameContext.World;
		if (world == null) return;

		ulong frame = Engine.GetPhysicsFrames();
		if (ReferenceEquals(world, _builtWorld) && frame == _builtFrame) return;

		_builtWorld = world;
		_builtFrame = frame;

		foreach (var cell in _playerEntityIdsByCell.Values) cell.Clear();
		world.Query(in QueryCache.AllPositionAndOwnerNoneDeadQuery, _collectDelegate);
	}

	private void Collect(Entity entity, ref Position position, ref Owner owner)
	{
		int playerEntityId = owner.PlayerEntity.Value.Id;
		if (playerEntityId <= 0) return;
		if (!float.IsFinite(position.Value.X) || !float.IsFinite(position.Value.Z)) return;

		long key = PackCell(ToCell(position.Value.X), ToCell(position.Value.Z));
		if (!_playerEntityIdsByCell.TryGetValue(key, out List<int>? playerEntityIds))
		{
			playerEntityIds = new List<int>(4);
			_playerEntityIdsByCell[key] = playerEntityIds;
		}

		playerEntityIds.Add(playerEntityId);
	}

	private static int ToCell(float coordinate) => (int)MathF.Floor(coordinate / CellSizeMeters);

	private static long PackCell(int cellX, int cellZ) => ((long)cellX << 32) ^ (long)(uint)cellZ;
}
