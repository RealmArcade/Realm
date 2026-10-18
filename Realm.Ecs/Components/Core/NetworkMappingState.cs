using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core
{
	/// <summary>
	///     Holds mapping dictionaries between server/client entities and peer ID to player entities.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public struct NetworkMappingState
	{
		public int ServerToClientHandle;
		public int ClientToServerHandle;
		public int PeerIdToPlayerHandle;
		public int PlayerEntitiesHandle;

		public NetworkMappingState(
			Dictionary<int, Entity> serverToClientEntityMap,
			Dictionary<int, int> clientToServerEntityMap,
			Dictionary<int, Entity> peerIdToPlayerEntityMap)
		{
			ServerToClientHandle = ColdPayloadRegistry<Dictionary<int, Entity>>.Register(serverToClientEntityMap);
			ClientToServerHandle = ColdPayloadRegistry<Dictionary<int, int>>.Register(clientToServerEntityMap);
			PeerIdToPlayerHandle = ColdPayloadRegistry<Dictionary<int, Entity>>.Register(peerIdToPlayerEntityMap);
			PlayerEntitiesHandle = ColdPayloadRegistry<Entity[]>.Register(new Entity[25]);
		}

		public Dictionary<int, Entity> ServerToClientEntityMap
		{
			get => ColdPayloadRegistry<Dictionary<int, Entity>>.Get(ServerToClientHandle);
			set => ServerToClientHandle = ColdPayloadRegistry<Dictionary<int, Entity>>.Register(value);
		}

		public Dictionary<int, int> ClientToServerEntityMap
		{
			get => ColdPayloadRegistry<Dictionary<int, int>>.Get(ClientToServerHandle);
			set => ClientToServerHandle = ColdPayloadRegistry<Dictionary<int, int>>.Register(value);
		}

		public Dictionary<int, Entity> PeerIdToPlayerEntityMap
		{
			get => ColdPayloadRegistry<Dictionary<int, Entity>>.Get(PeerIdToPlayerHandle);
			set => PeerIdToPlayerHandle = ColdPayloadRegistry<Dictionary<int, Entity>>.Register(value);
		}

		public Entity[] PlayerEntities
		{
			get => ColdPayloadRegistry<Entity[]>.Get(PlayerEntitiesHandle);
			set => PlayerEntitiesHandle = ColdPayloadRegistry<Entity[]>.Register(value);
		}
	}
}
