// Extracted from MovementAndPathfindingService.cs (task 6 decomposition).
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Realm.Ecs;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;

namespace Realm.Ecs.Services
{
	public partial class MovementAndPathfindingService
	{
		private void ReportOffMeshStartRecovery(Entity entity, in NavMeshStartRecovery startRecovery, OffMeshReplanCause replanCause)
		{
			if (!startRecovery.UsedFallback) return;

			PurgeDeadOffMeshStartRecoveryEntries();

			int entityId = entity.Id;
			if (_offMeshStartRecoveryLogTimeByEntityId.TryGetValue(entityId, out float lastLogTime) && _elapsedTime - lastLogTime < OffMeshStartRecoveryLogInterval) return;

			_offMeshStartRecoveryLogTimeByEntityId[entityId] = _elapsedTime;
			_offMeshStartRecoveryCount++;

			var world = _context.World;
			string entityName = world != null && world.Has<Name>(entity) ? world.Get<Name>(entity).Value : "-";
			string definitionId = world != null && world.Has<DefinitionId>(entity) ? world.Get<DefinitionId>(entity).Value : "-";
			string snappedText = startRecovery.Found
				? $"({startRecovery.Snapped.X:0.00},{startRecovery.Snapped.Y:0.00},{startRecovery.Snapped.Z:0.00})"
				: "na";
			string lateralText = startRecovery.Found ? startRecovery.LateralDistance.ToString("0.00") : "na";
			string verticalText = startRecovery.Found ? startRecovery.VerticalDelta.ToString("0.00") : "na";

			WarningSink?.Invoke(
				$"[NavMesh.StartRecovery] incident=#{_offMeshStartRecoveryCount} entity={entityId} name={entityName} def={definitionId} cause={GetOffMeshReplanCauseLabel(replanCause)} found={startRecovery.Found} startRef={startRecovery.StartRef} requested=({startRecovery.Requested.X:0.00},{startRecovery.Requested.Y:0.00},{startRecovery.Requested.Z:0.00}) snapped={snappedText} lateral={lateralText} dy={verticalText}");
		}

		private void ResetOffMeshStartRecoveryDiagnostics()
		{
			_offMeshStartRecoveryLogTimeByEntityId.Clear();
			_deadOffMeshStartRecoveryEntityIds.Clear();
			_elapsedTime = 0f;
			_offMeshStartRecoveryCount = 0;
		}

		private static string GetOffMeshReplanCauseLabel(OffMeshReplanCause replanCause)
		{
			return replanCause switch
			{
				OffMeshReplanCause.NoPath => "nopath",
				OffMeshReplanCause.Retarget => "retarget",
				OffMeshReplanCause.Stuck => "stuck",
				_ => "none"
			};
		}

		private void PurgeDeadOffMeshStartRecoveryEntries()
		{
			if (_offMeshStartRecoveryLogTimeByEntityId.Count == 0) return;

			var world = _context.World;
			if (world == null) return;

			_deadOffMeshStartRecoveryEntityIds.Clear();
			foreach (int entityId in _offMeshStartRecoveryLogTimeByEntityId.Keys)
			{
				if (!world.IsAlive(new Entity(entityId)))
				{
					_deadOffMeshStartRecoveryEntityIds.Add(entityId);
				}
			}

			foreach (int entityId in _deadOffMeshStartRecoveryEntityIds)
			{
				_offMeshStartRecoveryLogTimeByEntityId.Remove(entityId);
			}
		}
	}
}
