using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>collecting the units a viewer can see and describing the whole board.</summary>
public partial class AuthoritativeSnapshotBuilder
	{
	private void CollectDescribedUnits(Entity viewerPlayer, int tick)
	{
		_describedUnits.Clear();
		_describedEntityIds.Clear();
		_viewer = viewerPlayer;
		_playerEntities = Array.Empty<Entity>();

		if (_context.ShroudDisabled || viewerPlayer == Entity.Null)
		{
			CollectWholeBoard(tick);
			return;
		}

		foreach (var cell in _unitCells) cell.Value.Clear();

		_candidates.Clear();
		_visionSources.Clear();
		_world.Query(in DescribedUnitsQuery, _collectCandidates);

		MarkUnitsWithinSight();
		AppendVisibleCandidates();
	}

	/// <summary>
	///     Collects the units a whole-board description covers, once for the sequence, on the tick thread before the
	///     lanes are forked. Every lane then copies the same published list instead of taking a lock to read it.
	/// </summary>
	public WholeBoardDescription PrepareWholeBoard(int sequence)
	{
		WholeBoardDescription? published = _descriptions.ReadWholeBoard(sequence);
		if (published != null) return published;

		_candidates.Clear();
		_world.Query(in DescribedUnitsQuery, _collectCandidates);

		Span<WholeBoardEntry> units = _descriptions.RentWholeBoard(sequence, _candidates.Count);
		for (int index = 0; index < units.Length; index++) units[index] = new WholeBoardEntry(_candidates[index].Entity, _candidates[index].Position);

		return _descriptions.PublishWholeBoard(sequence, units.Length);
	}

	private void CollectWholeBoard(int tick)
	{
		WholeBoardDescription description = PrepareWholeBoard(tick);
		for (int index = 0; index < description.Count; index++)
		{
			WholeBoardEntry entry = description.Units[index];
			_describedUnits.Add((entry.Entity, entry.Position));
			_describedEntityIds.Add(entry.Entity.Id);
		}
	}

	private void CollectCandidate(Entity entity, ref UnitOwnerPlayer owner, ref Position position)
	{
		bool visible = true;
		if (_world.TryGet<Owner>(entity, out Owner entityOwner))
		{
			if (entityOwner.PlayerEntity.Value == _viewer)
			{
				float radius = _world.TryGet<ScanRadius>(entity, out ScanRadius scanRadius) ? scanRadius.Value : DefaultVisionRadius;
				if (radius > 0f) _visionSources.Add((position.Value, radius));
			}
			else visible = false;
		}

		int candidateIndex = _candidates.Count;
		_candidates.Add((entity, position.Value));
		_candidateVisibility[candidateIndex] = visible;

		(int cellX, int cellZ) cell = VisionCell(position.Value);
		if (!_unitCells.TryGetValue(cell, out List<int>? unitsInCell))
		{
			unitsInCell = new List<int>();
			_unitCells[cell] = unitsInCell;
		}

		unitsInCell.Add(candidateIndex);
	}

	private void MarkUnitsWithinSight()
	{
		for (int sourceIndex = 0; sourceIndex < _visionSources.Count; sourceIndex++)
		{
			(Vector3 sourcePosition, float radius) = _visionSources[sourceIndex];
			float squaredRadius = radius * radius;

			(int minimumCellX, int minimumCellZ) = VisionCellCoordinates(sourcePosition.X - radius, sourcePosition.Z - radius);
			(int maximumCellX, int maximumCellZ) = VisionCellCoordinates(sourcePosition.X + radius, sourcePosition.Z + radius);
			long coveredCells = (long)(maximumCellX - minimumCellX + 1) * (maximumCellZ - minimumCellZ + 1);

			if (coveredCells > _unitCells.Count)
			{
				MarkUnitsWithinSightOf(sourcePosition, squaredRadius, null);
				continue;
			}

			for (int cellX = minimumCellX; cellX <= maximumCellX; cellX++)
			{
				for (int cellZ = minimumCellZ; cellZ <= maximumCellZ; cellZ++)
				{
					if (_unitCells.TryGetValue((cellX, cellZ), out List<int>? unitsInCell)) MarkUnitsWithinSightOf(sourcePosition, squaredRadius, unitsInCell);
				}
			}
		}
	}

	private void MarkUnitsWithinSightOf(Vector3 sourcePosition, float squaredRadius, List<int>? unitsInCell)
	{
		int count = unitsInCell?.Count ?? _candidates.Count;
		for (int index = 0; index < count; index++)
		{
			int candidateIndex = unitsInCell == null ? index : unitsInCell[index];
			if (_candidateVisibility[candidateIndex]) continue;
			if (Vector3.DistanceSquared(sourcePosition, _candidates[candidateIndex].Position) > squaredRadius) continue;

			_candidateVisibility[candidateIndex] = true;
		}
	}

	private void AppendVisibleCandidates()
	{
		for (int index = 0; index < _candidates.Count; index++)
		{
			if (!_candidateVisibility[index]) continue;

			(Entity entity, Vector3 position) = _candidates[index];
			_describedUnits.Add((entity, position));
			_describedEntityIds.Add(entity.Id);
		}
	}

	private static (int X, int Z) VisionCell(Vector3 position) => VisionCellCoordinates(position.X, position.Z);

	private static (int X, int Z) VisionCellCoordinates(float x, float z)
	{
		float clampedX = Math.Clamp(x, -MaximumDescribedCoordinate, MaximumDescribedCoordinate);
		float clampedZ = Math.Clamp(z, -MaximumDescribedCoordinate, MaximumDescribedCoordinate);
		return ((int)MathF.Floor(clampedX / VisionCellSize), (int)MathF.Floor(clampedZ / VisionCellSize));
	}
}
