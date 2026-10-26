using System;
using System.Collections.Generic;
using Godot;
using MemoryPack;
using Realm.Client.Services;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using Realm.Shared.Network;

namespace Realm.Client;

public static class ShadowClientVerifier
{
	private static readonly Dictionary<int, ReplicatedStateMirror> _mirrors = new();
	private static int _verifiedBaselines;
	private static int _replayedDeltaCount;
	private static int _structuralFailures;
	private static int _fingerprintFailures;

	/// <summary>
	/// Whether the host replays the stream it sends. Replaying decodes, deserializes and rebuilds a mirror for every
	/// payload of every peer on the sending tick, so it is a verification cost and not a shipping one: the harness and
	/// map-editor test mode turn it on explicitly, and a live match leaves it off.
	/// </summary>
	public static bool IsEnabled { get; set; }

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

	/// <summary>
	/// Replays every payload a peer is sent into a mirror, in the order the peer receives it, and compares fingerprints
	/// on baselines. A baseline is no longer self-contained - it restates only what changed, because the peer keeps the
	/// components it already acknowledged - so reconstruction has to accumulate the stream instead of restarting at each
	/// baseline, which is also what the peer itself does.
	/// </summary>
	public static void VerifyPayload(int peerId, byte[] payload, bool isBaseline)
	{
		if (!IsEnabled) return;
		if (payload == null || payload.Length == 0) return;

		WorldSnapshot snapshot;
		try
		{
			snapshot = MemoryPackSerializer.Deserialize<WorldSnapshot>(SnapshotWireFormat.Decode(payload));
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

		_replayedDeltaCount += (snapshot.ComponentDeltas?.Count ?? 0) + (snapshot.AbilityStates?.Count ?? 0) + (snapshot.StaticTexts?.Count ?? 0);
		var errors = new List<string>();
		ReplicatedPayloadReplay.Replay(mirror, snapshot.ComponentDeltas, snapshot.AbilityStates, snapshot.StaticTexts, errors, snapshot.ColdPayloads);
		for (int i = 0; i < errors.Count; i++)
		{
			RecordStructuralFailure(peerId, $"peer {peerId} {errors[i]}");
		}

		if (!isBaseline) return;

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
