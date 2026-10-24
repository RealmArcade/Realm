using System.Collections.Generic;

namespace Realm.Ecs.Replication;

/// <summary>
///     Maps the host entity ids carried by replicated components onto the client entities that represent them.
/// </summary>
public static class ReplicatedEntityReferences
{
	private static readonly Dictionary<int, int> _localIdsByServerId = new();

	public static void Register(int serverEntityId, int localEntityId)
	{
		if (serverEntityId < 0 || localEntityId < 0) return;
		_localIdsByServerId[serverEntityId] = localEntityId;
	}

	public static void Unregister(int serverEntityId)
	{
		_localIdsByServerId.Remove(serverEntityId);
	}

	public static void Clear()
	{
		_localIdsByServerId.Clear();
	}

	public static Entity Resolve(Entity serverEntity)
	{
		if (serverEntity == Entity.Null) return Entity.Null;
		return _localIdsByServerId.TryGetValue(serverEntity.Id, out int localEntityId) ? new Entity(localEntityId) : Entity.Null;
	}
}
