﻿using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Client.Core;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Replication;
using Realm.Shared.Network;
using Godot;

namespace Realm.Client.Services;

/// <summary>
///     The periodic check that this screen's copy of the authoritative state still matches what the host
///     described: the player components and a rolling window of unit components are re-encoded from the
///     local ECS and compared with the mirror, and a difference is reported once per unit and component.
///     It runs only while a recording harness is watching, because that is where the report is read.
/// </summary>
internal sealed class ReplicatedDriftAudit
{
	private static EcsWorld Ecs => GameContext.World
		?? throw new InvalidOperationException("The drift audit was used before a world existed.");

	private readonly ReplicatedStateMirror _mirror;
	private readonly Func<Entity> _findWorldEntity;

	public ReplicatedDriftAudit(ReplicatedStateMirror mirror, Func<Entity> findWorldEntity)
	{
		_mirror = mirror;
		_findWorldEntity = findWorldEntity;
	}

	private int _replicatedEcsDriftCheckCounter;

	public void Check()
	{
		_replicatedEcsDriftCheckCounter++;
		if (_replicatedEcsDriftCheckCounter % 30 != 0) return;

		Entity worldEntity = _findWorldEntity();
		if (worldEntity == Entity.Null || !Ecs.Has<NetworkMappingState>(worldEntity)) return;
		var mapping = Ecs.Get<NetworkMappingState>(worldEntity);

		var codecs = ReplicatedComponents.All;
		for (int codecIndex = 0; codecIndex < codecs.Count; codecIndex++)
		{
			var codec = codecs[codecIndex];
			if (codec.Scope != ReplicatedScope.Player) continue;

			for (int slot = 0; slot < Realm.Ecs.EcsWorld.MAX_PLAYERS; slot++)
			{
				if (mapping.PlayerEntities == null || slot >= mapping.PlayerEntities.Length) continue;
				if (!_mirror.Components.TryGetValue(((byte)ReplicatedScope.Player, slot, codec.ComponentTypeId), out var expected)) continue;

				Entity playerEntity = mapping.PlayerEntities[slot];
				if (playerEntity == Entity.Null || !Ecs.IsAlive(playerEntity)) continue;

				if (_replicatedEcsDriftBuffer.Length < codec.SizeInBytes) _replicatedEcsDriftBuffer = new byte[codec.SizeInBytes];
				var actual = _replicatedEcsDriftBuffer.AsSpan(0, codec.SizeInBytes);
				if (!codec.TryWrite(Ecs, playerEntity, actual))
				{
					ReportReplicatedEcsDrift(slot, codec.ComponentName, "component missing");
					continue;
				}				if (!actual.SequenceEqual(expected))
				{
					ReportReplicatedEcsDrift(slot, codec.ComponentName, "bytes differ from the last authoritative value");
				}
			}
		}

		AuditReplicatedUnitDrift(mapping);
	}

	private const int UnitDriftAuditWindowEntities = 128;
	private int _unitDriftAuditNextServerEntityId;

	/// <summary>
	///     Audits one window of the replicated unit components this screen has applied, so a whole board is swept every few
	///     seconds rather than every tick. It runs only while a recording harness is watching, because that is where the
	///     report is read: a unit component this screen writes itself is overwritten by the next description, which looks
	///     exactly like a description that never arrived, and this is the only check that can say which of the two it was.
	/// </summary>
	private void AuditReplicatedUnitDrift(NetworkMappingState mapping)
	{
		if (!MultiplayerSyncDiagnostics.IsRecording) return;

		IReadOnlyList<IReplicatedComponentCodec> codecs = ReplicatedUnitComponentSlots.Slots;
		int windowEnd = _unitDriftAuditNextServerEntityId + UnitDriftAuditWindowEntities;
		if (windowEnd > Realm.Ecs.EcsWorld.MAX_ENTITIES) windowEnd = Realm.Ecs.EcsWorld.MAX_ENTITIES;

		for (int serverEntityId = _unitDriftAuditNextServerEntityId; serverEntityId < windowEnd; serverEntityId++)
		{
			if (!mapping.ServerToClientEntityMap.TryGetValue(serverEntityId, out Entity localEntity)) continue;
			if (!Ecs.IsAlive(localEntity)) continue;

			for (int codecIndex = 0; codecIndex < codecs.Count; codecIndex++)
			{
				var codec = codecs[codecIndex];
				if (!_mirror.Components.TryGetValue(((byte)ReplicatedScope.Unit, serverEntityId, codec.ComponentTypeId), out var expected)) continue;

				if (_replicatedEcsDriftBuffer.Length < codec.SizeInBytes) _replicatedEcsDriftBuffer = new byte[codec.SizeInBytes];
				var actual = _replicatedEcsDriftBuffer.AsSpan(0, codec.SizeInBytes);
				if (!codec.TryWrite(Ecs, localEntity, actual))
				{
					ReportReplicatedUnitDrift(serverEntityId, codec.ComponentName, "the component the match described is gone");
					continue;
				}

				if (!actual.SequenceEqual(expected))
				{
					ReportReplicatedUnitDrift(serverEntityId, codec.ComponentName, "bytes differ from the last authoritative value");
				}
			}
		}

		_unitDriftAuditNextServerEntityId = windowEnd >= Realm.Ecs.EcsWorld.MAX_ENTITIES ? 0 : windowEnd;
	}

	private readonly HashSet<(int ServerEntityId, string ComponentName)> _reportedUnitEcsDrift = new();

	private void ReportReplicatedUnitDrift(int serverEntityId, string componentName, string reason)
	{
		if (!_reportedUnitEcsDrift.Add((serverEntityId, componentName))) return;
		GD.PushError($"[Replication] local write clobbered replicated unit state: {componentName} for unit {serverEntityId} ({reason})");
		MultiplayerSyncDiagnostics.RecordEcsDrift(serverEntityId, componentName);
	}

	private void ReportReplicatedEcsDrift(int slot, string componentName, string reason)
	{
		if (_reportedEcsDrift.Contains((slot, componentName))) return;
		_reportedEcsDrift.Add((slot, componentName));
		GD.PushError($"[Replication] local write clobbered replicated state: {componentName} for player slot {slot} ({reason})");
		MultiplayerSyncDiagnostics.RecordEcsDrift(slot, componentName);
	}

	private readonly HashSet<(int Slot, string ComponentName)> _reportedEcsDrift = new();
	private byte[] _replicatedEcsDriftBuffer = new byte[16];
}
