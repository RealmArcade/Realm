using System;
using System.Collections.Generic;
using Godot;
using MemoryPack;
using Realm.Client.Services;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;

namespace Realm.Client;

public static class ShadowClientVerifier
{
	private static readonly Dictionary<int, ReplicatedStateMirror> _mirrors = new();
	private static int _verifiedBaselines;
	private static int _replayedDeltaCount;
	private static int _structuralFailures;
	private static int _fingerprintFailures;

	public static int VerifiedBaselines => _verifiedBaselines;
	public static int StructuralFailures => _structuralFailures;
	public static int FingerprintFailures => _fingerprintFailures;
	public static int ReplayedDeltaCount => _replayedDeltaCount;

	public static void Reset()
	{
		_mirrors.Clear();
		_verifiedBaselines = 0;
		_replayedDeltaCount = 0;
		_structuralFailures = 0;
		_fingerprintFailures = 0;
	}

	public static void VerifyPayload(int peerId, byte[] payload, bool isBaseline)
	{
		if (!isBaseline) return;
		if (payload == null || payload.Length == 0) return;

		WorldSnapshot snapshot;
		try
		{
			snapshot = MemoryPackSerializer.Deserialize<WorldSnapshot>(payload);
		}
		catch (Exception exception)
		{
			RecordStructuralFailure(peerId, $"payload for peer {peerId} failed to deserialize: {exception.Message}");
			return;
		}

		if (!_mirrors.TryGetValue(peerId, out var mirror))
		{
			mirror = new ReplicatedStateMirror();
			_mirrors[peerId] = mirror;
		}

		mirror.Clear();

		_replayedDeltaCount += (snapshot.ComponentDeltas?.Count ?? 0) + (snapshot.AbilityStates?.Count ?? 0) + (snapshot.StaticTexts?.Count ?? 0);
		var errors = new List<string>();
		ReplicatedPayloadReplay.Replay(mirror, snapshot.ComponentDeltas, snapshot.AbilityStates, snapshot.StaticTexts, errors, snapshot.ColdPayloads);
		for (int i = 0; i < errors.Count; i++)
		{
			RecordStructuralFailure(peerId, $"peer {peerId} {errors[i]}");
		}

		_verifiedBaselines++;
		uint replayed = ReplicatedStateFingerprint.CombineWeatherState(mirror.ComputeFingerprint(), GameContext.World, ECSContext.WorldEntity);
		if (replayed == snapshot.StateFingerprint)
		{
			if (_verifiedBaselines == 1)
			{
				GD.Print($"[ShadowClient] replay fingerprint match at sequence {snapshot.Sequence} for peer {peerId}");
			}
			return;
		}

		RecordFingerprintFailure(peerId, snapshot.Sequence, snapshot.StateFingerprint, replayed);
	}

	private static void RecordStructuralFailure(int peerId, string message)
	{
		_structuralFailures++;
		if (_structuralFailures > 10) return;
		GD.PushError($"[ShadowClient] {message}");
	}

	private static void RecordFingerprintFailure(int peerId, int sequence, uint expected, uint replayed)
	{
		_fingerprintFailures++;
		if (_fingerprintFailures > 10) return;
		GD.PushError($"[ShadowClient] peer {peerId} replay fingerprint mismatch at sequence {sequence}: host={expected:x8} replay={replayed:x8}");
		MultiplayerSyncDiagnostics.RecordFingerprintMismatch(sequence, expected, replayed);
	}
}
