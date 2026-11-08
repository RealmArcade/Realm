using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Realm.Client.Services;
using Realm.Ecs.Replication;
using Realm.Shared.Diagnostics;
using Realm.Shared.Network;
using Realm.Shared.Services;

namespace Realm.Client.Testing;

public static partial class MultiplayerEcsSyncTestSupport
{
	private readonly struct PlacementOffset
	{
		public readonly float HostToRendered;
		public readonly float AppliedToRendered;
		public readonly float HostToApplied;

		public PlacementOffset(float hostToRendered, float appliedToRendered, float hostToApplied)
		{
			HostToRendered = hostToRendered;
			AppliedToRendered = appliedToRendered;
			HostToApplied = hostToApplied;
		}

		public float DistanceFor(int quantity) => quantity switch
		{
			PlacementQuantityRendererFidelity => AppliedToRendered,
			_ => HostToRendered
		};
	}

	private struct PlacementStreak
	{
		public int Count;
		public int FirstSequence;
		public int LastSequence;
		public float WorstOffset;
		public int WorstSequence;
		public float WorstHostToRendered;
		public float WorstAppliedToRendered;
		public float WorstHostToApplied;
	}

	private static void CompareScopeDigests(MultiplayerSnapshotRecord hostRecord, MultiplayerSnapshotRecord clientRecord, ulong clientPeerMask, List<string> mismatches)
	{
		foreach (KeyValuePair<int, Dictionary<int, ComponentDigestEntry>> hostPlayer in hostRecord.Players)
		{
			if (!clientRecord.Players.TryGetValue(hostPlayer.Key, out Dictionary<int, ComponentDigestEntry>? clientDigests))
			{
				AddMismatch(mismatches, 0, $"seq {hostRecord.Sequence}: player slot {hostPlayer.Key} has no applied state on the client.");
				continue;
			}

			CompareDigestSets(hostRecord.Sequence, $"player slot {hostPlayer.Key}", hostPlayer.Value, clientDigests, SyncRecordFormat.NamedByEveryPeer, mismatches);
		}

		CompareDigestSets(hostRecord.Sequence, "world entity", hostRecord.WorldDigests, clientRecord.WorldDigests, SyncRecordFormat.NamedByEveryPeer, mismatches);

		CompareLootRecords(hostRecord, clientRecord, clientPeerMask, mismatches);
	}

	private static void CompareLootRecords(MultiplayerSnapshotRecord hostRecord, MultiplayerSnapshotRecord clientRecord, ulong clientPeerMask, List<string> mismatches)
	{
		foreach (KeyValuePair<int, MultiplayerLootState> hostLoot in hostRecord.Loot)
		{
			bool describedToThisPeer = hostLoot.Value.NamedByPeers == SyncRecordFormat.NamedByEveryPeer || (hostLoot.Value.NamedByPeers & clientPeerMask) != 0;
			if (!describedToThisPeer) continue;

			if (!clientRecord.Loot.TryGetValue(hostLoot.Key, out MultiplayerLootState? clientLoot))
			{
				AddMismatch(mismatches, 0, $"seq {hostRecord.Sequence}: loot {hostLoot.Key} ('{hostLoot.Value.ItemTypeId}') has no applied state on the client although the host described it to this peer.");
				continue;
			}

			CompareLootValues(hostRecord.Sequence, hostLoot.Key, hostLoot.Value, clientLoot, mismatches);
		}

		foreach (int clientLootId in clientRecord.Loot.Keys)
		{
			if (hostRecord.Loot.ContainsKey(clientLootId)) continue;
			AddMismatch(mismatches, 0, $"seq {hostRecord.Sequence}: the client holds loot {clientLootId} that the host does not.");
		}
	}

	private static void CompareLootValues(int sequence, int uniqueId, MultiplayerLootState hostLoot, MultiplayerLootState clientLoot, List<string> mismatches)
	{
		if (!string.Equals(hostLoot.ItemTypeId, clientLoot.ItemTypeId, StringComparison.Ordinal))
		{
			AddMismatch(mismatches, 0, $"seq {sequence}: loot {uniqueId} is '{hostLoot.ItemTypeId}' on the host and '{clientLoot.ItemTypeId}' on the client.");
		}

		if (hostLoot.Charges != clientLoot.Charges) AddMismatch(mismatches, 0, $"seq {sequence}: loot {uniqueId} holds {hostLoot.Charges} charges on the host and {clientLoot.Charges} on the client.");
		if (hostLoot.OwnerPlayerIndex != clientLoot.OwnerPlayerIndex) AddMismatch(mismatches, 0, $"seq {sequence}: loot {uniqueId} belongs to slot {hostLoot.OwnerPlayerIndex} on the host and {clientLoot.OwnerPlayerIndex} on the client.");
		if (hostLoot.AutoPickup != clientLoot.AutoPickup) AddMismatch(mismatches, 0, $"seq {sequence}: loot {uniqueId} takes itself up on the host={hostLoot.AutoPickup} and on the client={clientLoot.AutoPickup}.");

		float remainingLifeDifference = MathF.Abs(hostLoot.RemainingLifetime - clientLoot.RemainingLifetime);
		if (remainingLifeDifference > LootRemainingLifetimeToleranceSeconds)
		{
			AddMismatch(mismatches, 0, $"seq {sequence}: loot {uniqueId} has {hostLoot.RemainingLifetime:F2}s left on the host and {clientLoot.RemainingLifetime:F2}s on the client, {remainingLifeDifference:F2}s apart.");
		}

		float displacement = System.Numerics.Vector3.Distance(
			new System.Numerics.Vector3(hostLoot.PositionX, hostLoot.PositionY, hostLoot.PositionZ),
			new System.Numerics.Vector3(clientLoot.PositionX, clientLoot.PositionY, clientLoot.PositionZ));
		if (displacement > LootPositionToleranceMeters)
		{
			AddMismatch(mismatches, 0, $"seq {sequence}: loot {uniqueId} lies {displacement:F2} m from where the host described it.");
		}
	}

	private static void CompareDigestSets(int sequence, string owner, Dictionary<int, ComponentDigestEntry> hostDigests, Dictionary<int, ComponentDigestEntry> clientDigests, ulong peerMask, List<string> mismatches, ReferenceDigestCoverage? coverage = null)
	{
		const int mismatchCountBefore = 0;
		foreach (KeyValuePair<int, ComponentDigestEntry> hostDigest in hostDigests)
		{
			if (!hostDigest.Value.IsNameableBy(peerMask))
			{
				if (hostDigest.Value.IsScoped) coverage?.RecordSkipped();
				continue;
			}

			if (hostDigest.Value.IsScoped) coverage?.RecordCompared();

			string componentName = DescribeComponent(hostDigest.Key);
			if (!clientDigests.TryGetValue(hostDigest.Key, out ComponentDigestEntry clientDigest))
			{
				AddMismatch(mismatches, mismatchCountBefore, $"seq {sequence}: {owner} carries {componentName} on the host but not in the client's applied ECS.");
				continue;
			}

			if (clientDigest.Hash != hostDigest.Value.Hash)
			{
				AddMismatch(mismatches, mismatchCountBefore, $"seq {sequence}: {owner} {componentName} differs between the host ECS ({hostDigest.Value.Hash:x8}) and the client's applied ECS ({clientDigest.Hash:x8}).");
			}
		}

		foreach (KeyValuePair<int, ComponentDigestEntry> clientDigest in clientDigests)
		{
			if (hostDigests.TryGetValue(clientDigest.Key, out ComponentDigestEntry hostDigest) && !hostDigest.IsNameableBy(peerMask)) continue;
			if (hostDigests.ContainsKey(clientDigest.Key)) continue;
			AddMismatch(mismatches, mismatchCountBefore, $"seq {sequence}: {owner} carries {DescribeComponent(clientDigest.Key)} in the client's applied ECS but not on the host.");
		}
	}

	private static string DescribeComponent(int componentTypeId)
	{
		IReadOnlyList<IReplicatedComponentCodec> codecs = ReplicatedComponents.All;
		for (int index = 0; index < codecs.Count; index++)
		{
			if (codecs[index].ComponentTypeId != componentTypeId) continue;
			return codecs[index].ComponentName;
		}

		return $"component {componentTypeId}";
	}

	private const int DeltaDivergenceRecordAllowance = 15;

	/// <summary>
	///     How long a unit the host has described may be absent from the client's own state before the absence is a
	///     divergence. A description names the units the peer is being told about; the client builds them over the ticks that
	///     follow, spending a few milliseconds of each on the first of a kind, because building a whole board at once froze a
	///     tick for close to a second. At a record per physics tick this covers a board of dozens of units built one per tick.
	/// </summary>
	private const int MembershipRecordAllowanceSequences = 90;

	private static int ResolveDeltaDivergenceRecordAllowance(MultiplayerEcsRunSettings settings)
		=> settings.PacketLossPercent > 0
			? 2 * DeltaDivergenceRecordAllowance + 1
			: DeltaDivergenceRecordAllowance + 1;

	/// <summary>
	///     Reports each run of consecutive records a quantity stayed wrong for, and only counts a run the allowance does not
	///     cover: a value that heals inside the window it is stated in is what the protocol promises, while one that outlives
	///     it is a divergence.
	/// </summary>
	private static void ReportDivergenceStreaks(MultiplayerEcsSyncReport report, List<(int EntityId, string Field, int Sequence, string Message)> divergences, int allowance, string quantityDescription, string allowanceDescription)
	{
		if (divergences.Count == 0) return;

		var streaks = new Dictionary<(int EntityId, string Field), (int Count, int FirstSequence, int LastSequence, string Message)>();
		var reportedStreaks = new List<(int EntityId, string Field, int Count, int FirstSequence, int LastSequence, string Message)>();
		for (int index = 0; index < divergences.Count; index++)
		{
			var divergence = divergences[index];
			var key = (divergence.EntityId, divergence.Field);
			if (streaks.TryGetValue(key, out var streak) && divergence.Sequence == streak.LastSequence + 1)
			{
				streaks[key] = (streak.Count + 1, streak.FirstSequence, divergence.Sequence, divergence.Message);
				continue;
			}

			if (streaks.TryGetValue(key, out var finished)) reportedStreaks.Add((key.EntityId, key.Field, finished.Count, finished.FirstSequence, finished.LastSequence, finished.Message));
			streaks[key] = (1, divergence.Sequence, divergence.Sequence, divergence.Message);
		}

		foreach (KeyValuePair<(int EntityId, string Field), (int Count, int FirstSequence, int LastSequence, string Message)> remaining in streaks)
		{
			reportedStreaks.Add((remaining.Key.EntityId, remaining.Key.Field, remaining.Value.Count, remaining.Value.FirstSequence, remaining.Value.LastSequence, remaining.Value.Message));
		}

		int healedCount = 0;
		foreach (var streak in reportedStreaks)
		{
			if (streak.Count > allowance)
			{
				report.AddFailure($"entity {streak.EntityId} {streak.Field} diverged on {streak.Count} consecutive delta records between sequences {streak.FirstSequence} and {streak.LastSequence}, beyond the {allowance}-record {allowanceDescription}; last observation: {streak.Message}");
				continue;
			}

			healedCount += streak.Count;
		}

		report.AddSummaryLine($"{quantityDescription} and healed inside the {allowance}-record {allowanceDescription}: {healedCount} (counted from {divergences.Count} observations)");
	}

	private static void CompareSnapshotRecord(MultiplayerSnapshotRecord hostRecord, MultiplayerSnapshotRecord clientRecord, bool exact, List<string> mismatches, List<string> digestMismatches, ref int comparedUnits, ref int offscreenMissingUnits, List<(int EntityId, string Field, int Sequence, string Message)> deltaDivergences, List<(int EntityId, string Field, int Sequence, string Message)> membershipDivergences, ulong clientPeerMask, ReferenceDigestCoverage coverage)
	{
		int mismatchCountBefore = mismatches.Count;

		void ReportUnitField(int entityId, string field, string message)
		{
			if (exact)
			{
				mismatches.Add(message);
				return;
			}

			deltaDivergences.Add((entityId, field, hostRecord.Sequence, message));
		}

		foreach (MultiplayerSnapshotUnit hostUnit in hostRecord.Units.Values)
		{
			bool isDead = (hostUnit.Flags & 1) != 0;
			bool isVisibleToRemoteClient = (hostUnit.Flags & 8) != 0;
			bool isDescribedToThisPeer = (hostUnit.NamedByPeers & clientPeerMask) != 0;
			bool isDetailedForThisClient = (hostUnit.Flags & 4) != 0;
			bool exactUnit = exact && isDetailedForThisClient;
			float positionTolerance = exactUnit ? BaselineTolerance : DeltaPositionTolerance;
			float rotationTolerance = exactUnit ? BaselineTolerance : DeltaRotationTolerance;
			float healthTolerance = exactUnit ? BaselineTolerance : DeltaHealthTolerance;

			if (!clientRecord.Units.TryGetValue(hostUnit.EntityId, out MultiplayerSnapshotUnit? clientUnit))
			{
				if (isDead) continue;

				if (!isDescribedToThisPeer)
				{
					offscreenMissingUnits++;
					continue;
				}

				// A described unit is not instantly a unit the client holds: it is built by the ticks that follow, and a
				// baseline names a whole board at once. The absence is therefore measured as a streak against the spawn
				// window rather than taken as a divergence the moment it is seen, so only a unit that never arrives counts.
				membershipDivergences.Add((hostUnit.EntityId, "membership", hostRecord.Sequence, $"seq {hostRecord.Sequence}: client is missing live entity {hostUnit.EntityId} (owner slot {hostUnit.OwnerPlayerIndex}) that the host sent to it."));
				continue;
			}

			comparedUnits++;

			if (isDescribedToThisPeer)
			{
				CompareDigestSets(hostRecord.Sequence, $"entity {hostUnit.EntityId}", hostUnit.ComponentDigests, clientUnit.ComponentDigests, clientPeerMask, digestMismatches, coverage);
			}

			if (!isVisibleToRemoteClient) continue;

			if (hostUnit.OwnerPlayerIndex != clientUnit.OwnerPlayerIndex)
			{
				ReportUnitField(hostUnit.EntityId, "owner", $"seq {hostRecord.Sequence}: entity {hostUnit.EntityId} owner slot host={hostUnit.OwnerPlayerIndex} client={clientUnit.OwnerPlayerIndex}.");
			}

			bool hostIsBuilding = (hostUnit.Flags & 2) != 0;
			bool clientIsBuilding = (clientUnit.Flags & 2) != 0;
			if (hostIsBuilding != clientIsBuilding)
			{
				ReportUnitField(hostUnit.EntityId, "building", $"seq {hostRecord.Sequence}: entity {hostUnit.EntityId} building host={hostIsBuilding} client={clientIsBuilding}.");
			}

			bool clientDead = (clientUnit.Flags & 1) != 0;
			if (isDead != clientDead)
			{
				ReportUnitField(hostUnit.EntityId, "dead", $"seq {hostRecord.Sequence}: entity {hostUnit.EntityId} dead host={isDead} client={clientDead}.");
			}

			if (isDead || clientDead) continue;

			float dx = hostUnit.PositionX - clientUnit.PositionX;
			float dy = hostUnit.PositionY - clientUnit.PositionY;
			float dz = hostUnit.PositionZ - clientUnit.PositionZ;
			if (MathF.Abs(dx) > positionTolerance || MathF.Abs(dy) > positionTolerance || MathF.Abs(dz) > positionTolerance)
			{
				ReportUnitField(hostUnit.EntityId, "position", $"seq {hostRecord.Sequence}: entity {hostUnit.EntityId} position host=({hostUnit.PositionX:F3},{hostUnit.PositionY:F3},{hostUnit.PositionZ:F3}) client=({clientUnit.PositionX:F3},{clientUnit.PositionY:F3},{clientUnit.PositionZ:F3}).");
			}

			if (RotationDifference(hostUnit.RotationY, clientUnit.RotationY) > rotationTolerance)
			{
				ReportUnitField(hostUnit.EntityId, "rotation", $"seq {hostRecord.Sequence}: entity {hostUnit.EntityId} rotation host={hostUnit.RotationY:F3} client={clientUnit.RotationY:F3}.");
			}

			if (MathF.Abs(hostUnit.CurrentHp - clientUnit.CurrentHp) > healthTolerance)
			{
				ReportUnitField(hostUnit.EntityId, "health", $"seq {hostRecord.Sequence}: entity {hostUnit.EntityId} health host={hostUnit.CurrentHp:F3} client={clientUnit.CurrentHp:F3}.");
			}

			if (MathF.Abs(hostUnit.MaxHp - clientUnit.MaxHp) > healthTolerance)
			{
				ReportUnitField(hostUnit.EntityId, "max health", $"seq {hostRecord.Sequence}: entity {hostUnit.EntityId} max health host={hostUnit.MaxHp:F3} client={clientUnit.MaxHp:F3}.");
			}
		}

		if (!exact) return;

		foreach (MultiplayerSnapshotUnit clientUnit in clientRecord.Units.Values)
		{
			bool clientDead = (clientUnit.Flags & 1) != 0;
			if (clientDead) continue;
			if (hostRecord.Units.ContainsKey(clientUnit.EntityId)) continue;

			AddMismatch(mismatches, mismatchCountBefore, $"seq {hostRecord.Sequence}: client tracks live entity {clientUnit.EntityId} that the host baseline does not contain.");
		}
	}

}
