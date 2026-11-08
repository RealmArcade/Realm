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

/// <summary>describing one peer into a snapshot.</summary>
public partial class AuthoritativeSnapshotBuilder
	{
	/// <summary>
	///     Describes one peer. The ability deltas are handed in rather than collected here, because the registry keeps
	///     one scratch table per process and could not be entered by two lanes at once: the tick collects them for every
	///     peer before the lanes are forked, and a single-threaded caller collects them for itself.
	/// </summary>
	public void BuildInto(ref WorldSnapshot snapshot, int peerId, int sequence, PlayerEntity viewer, bool forceBaseline, List<AbilityStateDelta> preparedAbilityDeltas)
	{
		SnapshotPeerState state = _peerStates.Get(peerId);
		bool isBaseline = forceBaseline || state.LastBaselineSequence < 0;
		if (isBaseline)
		{
			state.LastBaselineSequence = sequence;
			state.ResetForDescription();
		}

		CollectDescribedUnits(viewer.Value, sequence);
		_describedUnitStates.Clear();

		snapshot.ResetForDescription(sequence, isBaseline, isBaseline ? sequence : state.LastBaselineSequence);
		if (isBaseline) snapshot.UnitComponentPresence = new List<UnitComponentPresence>(_describedUnits.Count);

		for (int index = 0; index < _describedUnits.Count; index++)
		{
			(Entity unitEntity, Vector3 unitPosition) = _describedUnits[index];

			UnitSnapshot described;
			if (!_descriptions.TryGetUnitRecord(sequence, unitEntity.Id, out described))
			{
				described = DescribeUnit(unitEntity, unitPosition);
				_descriptions.SetUnitRecord(sequence, unitEntity.Id, in described);
			}

			if (!state.SentUnits.TryGetValue(unitEntity.Id, out UnitDescriptionState? held))
			{
				held = new UnitDescriptionState(UnitCodecs.Length);

				// A unit coming back into this peer's sight is one it stopped hearing about. What the peer holds for it is
				// still what it was told before the unit left, so those components are stated again or stated gone; only the
				// bytes are unknown, which is why they are not remembered across the gap.
				if (state.UnseenUnitComponents.Remove(unitEntity.Id, out ulong toldBeforeLeavingSight)) held.ToldCodecMask = toldBeforeLeavingSight;

				state.SentUnits[unitEntity.Id] = held;
			}

			_describedUnitStates.Add(held);

			// Whether this peer holds the same unit under this identifier is decided once, because it answers two questions:
			// whether the components it was given still belong to this unit, and whether it still knows what the unit is built
			// from. A match recycles a server entity identifier the moment a unit is destroyed, so the second question is not
			// the same as "was this identifier described before".
			bool sameUnitAsSent = IsSameUnitAsSent(in described, in held.SentRecord);

			if (!isBaseline && !sameUnitAsSent) held.ForgetToldPayloadBytes();

			// A unit exactly where this peer was last told it is carries no information - a building, a unit holding position
			// or one standing where its order ended all stay on the peer's screen as they are - so it is left out and its
			// record kept, and the tick it moves it is described again. A baseline always states every unit.
			if (!isBaseline && MatchesIn(in described, in held.SentRecord)) continue;

			held.SentRecord = described;

			// A unit's definition never changes, so it is named once rather than on every description of it. A moving unit is
			// described thirty times a second, and the same text was encoded, carried and allocated on the screen each time;
			// the record this match keeps still holds it, so the next description can tell whether the peer has been told.
			// A unit that leaves this peer's sight loses its record here and is named again when it comes back, which is the
			// moment the screen has thrown its copy away. A different unit under the same identifier - the usual case, since a
			// drafted circle frees its identifier and the hero and the circle that replace it take the freed ones - is another
			// such moment: the screen compares the record with what it holds, throws its copy away and builds the unit again,
			// and a description that left the definition out would leave that unit standing nowhere on its screen for good.
			if (!isBaseline && sameUnitAsSent && held.ToldDefinition != null && string.Equals(held.ToldDefinition, described.UnitId, StringComparison.Ordinal))
			{
				described.UnitId = string.Empty;
			}
			else
			{
				held.ToldDefinition = described.UnitId;
			}

			snapshot.Units.Add(described);
		}

		if (isBaseline) AppendRuleEntries(snapshot.Catalog);
		AppendTextEntries(state, snapshot.Catalog);
		AppendLoot(state, ref snapshot, isBaseline);
		AppendContentPayloads(state, snapshot);
		if (preparedAbilityDeltas.Count > 0) snapshot.AbilityStates.AddRange(preparedAbilityDeltas);

		AppendComponentDeltas(state, snapshot, sequence, isBaseline);
		ForgetUntrackedUnitComponents(state, ref snapshot, isBaseline);
	}

	private static bool IsSameUnitAsSent(in UnitSnapshot described, in UnitSnapshot sent)
	{
		// A unit this peer was told is dead is not a unit it holds: it freezes the corpse, frees it once the presentation
		// clock reaches that description, and treats anything described under the identifier afterwards as a different
		// unit. The same holds for a unit it never built, whose record is the default one.
		if (sent.IsDead || string.IsNullOrEmpty(sent.UnitId)) return false;
		if (!string.Equals(described.UnitId, sent.UnitId, StringComparison.Ordinal)) return false;
		if (described.OwnerPlayerIndex != sent.OwnerPlayerIndex) return false;
		return described.IsBuilding == sent.IsBuilding;
	}

	private static bool MatchesIn(in UnitSnapshot described, in UnitSnapshot sent)
	{
		if (described.EntityId != sent.EntityId) return false;
		if (described.OwnerPlayerEntityId != sent.OwnerPlayerEntityId) return false;
		if (described.OwnerPlayerIndex != sent.OwnerPlayerIndex) return false;
		if (described.RotationY != sent.RotationY) return false;
		if (described.CurrentHp != sent.CurrentHp) return false;
		if (described.MaxHp != sent.MaxHp) return false;
		if (described.IsDead != sent.IsDead) return false;
		if (described.IsBuilding != sent.IsBuilding) return false;
		if (described.Position.X != sent.Position.X || described.Position.Y != sent.Position.Y || described.Position.Z != sent.Position.Z) return false;

		return string.Equals(described.UnitId, sent.UnitId, StringComparison.Ordinal);
	}
}
