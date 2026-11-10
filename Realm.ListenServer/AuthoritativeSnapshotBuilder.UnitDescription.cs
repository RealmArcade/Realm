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

/// <summary>the unit snapshot itself and the text and owner it resolves.</summary>
public partial class AuthoritativeSnapshotBuilder
	{
	private UnitSnapshot DescribeUnit(Entity entity, Vector3 position)
	{
		float rotation = _world.TryGet<RotationY>(entity, out RotationY describedRotation) ? describedRotation.Value : 0f;
		Health health = _world.Get<Health>(entity);

		return new UnitSnapshot
		{
			EntityId = entity.Id,
			UnitId = _world.TryGet<DefinitionId>(entity, out DefinitionId definition) ? ResolveDefinitionText(definition.StringId) : string.Empty,
			OwnerPlayerEntityId = ResolveOwnerPeerId(entity),
			OwnerPlayerIndex = _world.TryGet<UnitOwnerPlayer>(entity, out UnitOwnerPlayer owner) ? owner.PlayerIndex : -1,
			// Snapped to the precision the wire states a unit in, so what a peer is told is the value this match compares its
			// own copy against: separation and formation nudge a unit by fractions it cannot show, and unsnapped every such
			// nudge would cost a record in every peer's frame.
			Position = new NetworkVector3(
				UnitQuantization.SnapPosition(position.X),
				UnitQuantization.SnapPosition(position.Y),
				UnitQuantization.SnapPosition(position.Z)),
			RotationY = UnitQuantization.SnapRotation(rotation),
			CurrentHp = UnitQuantization.SnapHealth(health.Current),
			MaxHp = UnitQuantization.SnapHealth(health.Max),
			IsDead = _world.Has<Dead>(entity),
			IsBuilding = _world.Has<Building>(entity)
		};
	}

	private string ResolveDefinitionText(int stringId)
	{
		if (_textByStringId.TryGetValue(stringId, out string? known)) return known;

		string text = StringRegistry.Get(stringId);
		_textByStringId[stringId] = text;
		return text;
	}

	private int ResolveOwnerPeerId(Entity unitEntity)
	{
		if (!_world.TryGet<Owner>(unitEntity, out Owner owner)) return 0;
		if (!_world.TryGet<UnitOwnerPlayer>(owner.PlayerEntity.Value, out UnitOwnerPlayer ownerPlayer)) return 0;

		int playerIndex = ownerPlayer.PlayerIndex;
		if ((uint)playerIndex >= (uint)_peerIdByPlayerSlot.Length) return 0;

		return _peerIdByPlayerSlot[playerIndex];
	}
}
