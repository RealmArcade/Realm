using System;
using System.Collections.Generic;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Replication;
using Realm.Shared.Network;

namespace Realm.Client.Services;

/// <summary>
///     Creates and destroys the local entity that stands in for a server-side loot drop, keeping the server/client
///     identifier maps, the replicated reference registry and any payloads that point at the drop in step with it.
/// </summary>
internal static class ReplicatedLootLifecycle
{
	internal static Entity Create(int serverEntityId, NetworkMappingState mapping)
	{
		Entity localEntity = GameContext.World.Create();
		mapping.ServerToClientEntityMap[serverEntityId] = localEntity;
		mapping.ClientToServerEntityMap[localEntity.Id] = serverEntityId;
		ReplicatedEntityReferences.Register(serverEntityId, localEntity.Id);
		GameContext.NetworkService?.References.Refix(serverEntityId);
		return localEntity;
	}
	internal static void Destroy(int serverEntityId, Entity localEntity, NetworkMappingState mapping)
	{
		GameContext.Host?.RemoveLootItemLocal(serverEntityId);
		GameContext.NetworkService?.References.ForgetAllHeldBy(localEntity.Id);
		ReplicatedEntityReferences.Unregister(serverEntityId);
		mapping.ServerToClientEntityMap.Remove(serverEntityId);
		mapping.ClientToServerEntityMap.Remove(localEntity.Id);
		if (GameContext.World.IsAlive(localEntity)) GameContext.World.Destroy(localEntity);
	}
}
