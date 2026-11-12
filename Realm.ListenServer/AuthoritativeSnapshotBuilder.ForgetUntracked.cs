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

/// <summary>forgetting the components of units the peer no longer has.</summary>
public partial class AuthoritativeSnapshotBuilder
	{
	private void ForgetUntrackedUnitComponents(SnapshotPeerState state, ref WorldSnapshot snapshot, bool isBaseline)
	{
		// Only the units that left this peer's sight are dropped, found by comparing the set it was described with last
		// tick against the one it is described with now. Walking every remembered component instead cost a full pass over
		// the peer's whole history every tick, and a peer whose sight is steady has nothing to forget at all.
		_forgottenEntityIds.Clear();
		foreach (int rememberedEntityId in state.DescribedEntityIds)
		{
			if (!_describedEntityIds.Contains(rememberedEntityId)) _forgottenEntityIds.Add(rememberedEntityId);
		}

		// The comparison is also what the peer needs in order to drop the replica. A baseline states the whole world and a
		// peer recovers from one by forgetting what it held, so it states nothing retired.
		if (!isBaseline) snapshot.RetiredEntityIds!.AddRange(_forgottenEntityIds);

		for (int index = 0; index < _forgottenEntityIds.Count; index++)
		{
			int forgottenEntityId = _forgottenEntityIds[index];
			if (state.SentUnits.TryGetValue(forgottenEntityId, out UnitDescriptionState? forgotten) && forgotten.ToldCodecMask != 0)
			{
				state.UnseenUnitComponents[forgottenEntityId] = forgotten.ToldCodecMask;
			}

			state.SentUnits.Remove(forgottenEntityId);
		}

		state.DescribedEntityIds.Clear();
		foreach (int describedEntityId in _describedEntityIds) state.DescribedEntityIds.Add(describedEntityId);
	}

	private Entity[] ResolvePlayerEntities()
	{
		if (_playerEntities.Length > 0) return _playerEntities;
		if (!_world.IsAlive(_worldEntity)) return _playerEntities;
		if (!_world.TryGet<NetworkMappingState>(_worldEntity, out NetworkMappingState mapping)) return _playerEntities;
		if (mapping.PlayerEntities == null) return _playerEntities;

		_playerEntities = mapping.PlayerEntities;
		return _playerEntities;
	}
}
