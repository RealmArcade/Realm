using System;
using System.Collections.Generic;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Client.UI;

namespace Realm.Client.Services;

public struct PredictedUnitState
{
	public Vector3 Position;
	public Vector3 Velocity;
}

public class PredictedTickFrame
{
	public int Sequence;
	public Dictionary<int, PredictedUnitState> UnitStates = new();
}

public class MovementPredictionService
{
	private const int BufferSize = 128;
	private readonly PredictedTickFrame[] _historyBuffer = new PredictedTickFrame[BufferSize];
	private int _currentSequence;

	public bool IsPredictiveFrameContext { get; private set; }

	public MovementPredictionService()
	{
		for (int i = 0; i < BufferSize; i++)
		{
			_historyBuffer[i] = new PredictedTickFrame();
		}
	}

	public bool IsPredictionEnabled => GameSettings.MovementPrediction;

	public void ApplyLocalPrediction(EcsWorld world, IEnumerable<int> unitEntityIds, Vector3 targetPos)
	{
		if (!IsPredictionEnabled || world == null) return;

		IsPredictiveFrameContext = true;
		try
		{
			foreach (int entityId in unitEntityIds)
			{
				var entity = new Entity(entityId);
				if (!world.IsAlive(entity)) continue;

				if (world.Has<Position>(entity))
				{
					var currentPos = world.Get<Position>(entity).Value;
					if (!world.Has<RenderPosition>(entity))
					{
						world.Set(entity, new RenderPosition(currentPos));
					}
				}

				world.SetOrAdd(entity, new MoveTo(targetPos));
			}
		}
		finally
		{
			IsPredictiveFrameContext = false;
		}
	}

	public void RecordTick(int sequence, EcsWorld world)
	{
		if (!IsPredictionEnabled || world == null) return;

		_currentSequence = sequence;
		int index = sequence % BufferSize;
		var frame = _historyBuffer[index];
		frame.Sequence = sequence;
		frame.UnitStates.Clear();

		var query = QueryCache.AllPositionAndDefinitionIdQuery;
		world.Query(in query, (Entity entity, ref Position pos) =>
		{
			Vector3 vel = world.Has<Velocity>(entity) ? world.Get<Velocity>(entity).Value : Vector3.Zero;
			frame.UnitStates[entity.Id] = new PredictedUnitState
			{
				Position = pos.Value,
				Velocity = vel
			};

			if (!world.Has<RenderPosition>(entity))
			{
				world.Set(entity, new RenderPosition(pos.Value));
			}
		});
	}

	public void ReconcileServerSnapshot(EcsWorld world, int sequence, Dictionary<int, Vector3> serverPositions)
	{
		if (!IsPredictionEnabled || world == null || serverPositions == null) return;

		int index = sequence % BufferSize;
		var recordedFrame = _historyBuffer[index];
		bool rollbackNeeded = false;
		var mispredictedEntities = new List<Entity>();

		foreach (var kvp in serverPositions)
		{
			int entityId = kvp.Key;
			Vector3 serverPos = kvp.Value;
			var entity = new Entity(entityId);

			if (!world.IsAlive(entity)) continue;

			if (!world.Has<RenderPosition>(entity) && world.Has<Position>(entity))
			{
				world.Set(entity, new RenderPosition(world.Get<Position>(entity).Value));
			}

			Vector3 predictedPos = (recordedFrame.Sequence == sequence && recordedFrame.UnitStates.TryGetValue(entityId, out var state))
				? state.Position
				: (world.Has<Position>(entity) ? world.Get<Position>(entity).Value : serverPos);

			float delta = Vector3.Distance(predictedPos, serverPos);

			if (delta > 0.1f)
			{
				rollbackNeeded = true;
				mispredictedEntities.Add(entity);
				world.Set(entity, new Position(serverPos));
				world.SetOrAdd(entity, new PreviousPosition(serverPos));
			}
		}

		if (rollbackNeeded)
		{
			IsPredictiveFrameContext = true;
			try
			{
				FastForwardReSimulation(world, sequence, _currentSequence, mispredictedEntities);
			}
			finally
			{
				IsPredictiveFrameContext = false;
			}
		}
	}

	private void FastForwardReSimulation(EcsWorld world, int startSeq, int targetSeq, List<Entity> entities)
	{
		int steps = Math.Clamp(targetSeq - startSeq, 1, BufferSize);
		float tickDelta = 0.033f;

		for (int step = 0; step < steps; step++)
		{
			foreach (var entity in entities)
			{
				if (!world.IsAlive(entity) || !world.Has<Position>(entity) || !world.Has<MoveTo>(entity)) continue;

				Vector3 current = world.Get<Position>(entity).Value;
				Vector3 target = world.Get<MoveTo>(entity).Target;
				float speed = world.Has<MovementStats>(entity) ? world.Get<MovementStats>(entity).Speed : 5.0f;

				Vector3 dir = target - current;
				float dist = dir.Length();
				if (dist > 0.05f)
				{
					dir = Vector3.Normalize(dir);
					float moveStep = Math.Min(speed * tickDelta, dist);
					world.Set(entity, new Position(current + dir * moveStep));
				}
				else
				{
					world.Remove<MoveTo>(entity);
				}
			}
		}
	}

	public void Clear()
	{
		_currentSequence = 0;
		IsPredictiveFrameContext = false;
		for (int i = 0; i < BufferSize; i++)
		{
			_historyBuffer[i].Sequence = 0;
			_historyBuffer[i].UnitStates.Clear();
		}
	}
}
