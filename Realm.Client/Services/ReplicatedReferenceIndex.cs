using System.Collections.Generic;
using Realm.Client.Core;
using Realm.Ecs;
using Realm.Ecs.Replication;
using Realm.Shared.Network;

namespace Realm.Client.Services;

/// <summary>
///     Tracks, for every replicated component this client applied, which server entity that component's payload points
///     at, so a payload can be repointed when an identifier is recycled and so a reference that names a unit this screen
///     does not have yet can be reported as unresolved instead of silently dangling.
/// </summary>
public class ReplicatedReferenceIndex
{
	private readonly Dictionary<int, List<(int LocalEntityId, IReplicatedComponentCodec Codec)>> _referrersByServerEntityId = new();
	private readonly Dictionary<(int LocalEntityId, int ComponentTypeId), int> _referencedServerEntityIds = new();
	private readonly Dictionary<int, List<int>> _referencedComponentTypesByLocalEntity = new();

	internal void Clear()
	{
		_referrersByServerEntityId.Clear();
		_referencedServerEntityIds.Clear();
		_referencedComponentTypesByLocalEntity.Clear();
	}

	internal void Record(ComponentDelta delta, Entity referrer, IReplicatedComponentCodec codec)
	{
		if (codec is not INormalizingComponentCodec normalizing) return;

		if (delta.Bytes == null || delta.Bytes.Length == 0)
		{
			Forget(referrer.Id, codec.ComponentTypeId);
			return;
		}

		int referencedServerEntityId = normalizing.ReadReferencedServerEntityId(delta.Bytes);
		var referenceKey = (referrer.Id, codec.ComponentTypeId);
		if (_referencedServerEntityIds.TryGetValue(referenceKey, out int previousServerEntityId))
		{
			if (previousServerEntityId == referencedServerEntityId) return;
			Forget(referrer.Id, codec.ComponentTypeId);
		}

		if (referencedServerEntityId < 0) return;

		if (!_referrersByServerEntityId.TryGetValue(referencedServerEntityId, out var referrers))
		{
			referrers = new List<(int LocalEntityId, IReplicatedComponentCodec Codec)>(4);
			_referrersByServerEntityId[referencedServerEntityId] = referrers;
		}

		referrers.Add((referrer.Id, codec));
		_referencedServerEntityIds[referenceKey] = referencedServerEntityId;

		if (!_referencedComponentTypesByLocalEntity.TryGetValue(referrer.Id, out List<int>? componentTypeIds))
		{
			componentTypeIds = new List<int>(2);
			_referencedComponentTypesByLocalEntity[referrer.Id] = componentTypeIds;
		}

		if (!componentTypeIds.Contains(codec.ComponentTypeId)) componentTypeIds.Add(codec.ComponentTypeId);
	}

	internal bool IsReferenceUnresolved(int localEntityId, int componentTypeId)
	{
		if (!_referencedServerEntityIds.TryGetValue((localEntityId, componentTypeId), out int serverEntityId)) return false;

		return ReplicatedEntityReferences.Resolve(new Entity(serverEntityId)) == Entity.Null;
	}

	internal void ForgetAllHeldBy(int localEntityId)
	{
		if (!_referencedComponentTypesByLocalEntity.Remove(localEntityId, out List<int>? componentTypeIds)) return;

		for (int index = 0; index < componentTypeIds.Count; index++) Forget(localEntityId, componentTypeIds[index]);
	}

	internal void Forget(int localEntityId, int componentTypeId)
	{
		var referenceKey = (localEntityId, componentTypeId);

		if (_referencedComponentTypesByLocalEntity.TryGetValue(localEntityId, out List<int>? heldComponentTypeIds))
		{
			heldComponentTypeIds.Remove(componentTypeId);
			if (heldComponentTypeIds.Count == 0) _referencedComponentTypesByLocalEntity.Remove(localEntityId);
		}

		if (!_referencedServerEntityIds.TryGetValue(referenceKey, out int serverEntityId)) return;

		_referencedServerEntityIds.Remove(referenceKey);
		if (!_referrersByServerEntityId.TryGetValue(serverEntityId, out var referrers)) return;

		for (int index = referrers.Count - 1; index >= 0; index--)
		{
			if (referrers[index].LocalEntityId != localEntityId || referrers[index].Codec.ComponentTypeId != componentTypeId) continue;
			referrers.RemoveAt(index);
		}

		if (referrers.Count == 0) _referrersByServerEntityId.Remove(serverEntityId);
	}

	internal void Refix(int serverEntityId)
	{
		if (!_referrersByServerEntityId.TryGetValue(serverEntityId, out var referrers)) return;

		// The identifier names a different unit now, so every component still pointing at the unit it named before is
		// pointed at the one it names now. The codec does it rather than a payload of the identifier alone: a build task
		// carries the timings of the work beside the site, so applying one from four bytes of identifier threw, and the
		// description that carried the unit the identifier had been given to was dropped whole.
		Entity reference = ReplicatedEntityReferences.Resolve(new Entity(serverEntityId));

		for (int index = 0; index < referrers.Count; index++)
		{
			var referrer = referrers[index];
			Entity localEntity = new(referrer.LocalEntityId);
			if (!GameContext.World.IsAlive(localEntity)) continue;
			if (referrer.Codec is not INormalizingComponentCodec normalizing) continue;

			normalizing.Repoint(GameContext.World, localEntity, reference);
		}
	}
}
