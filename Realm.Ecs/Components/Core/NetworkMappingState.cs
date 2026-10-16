using Arch.Core;

namespace Realm.Ecs.Components.Core
{
	/// <summary>
	///     Holds mapping dictionaries between server/client entities and peer ID to player entities.
	/// </summary>
	public struct NetworkMappingState
	{
		public Dictionary<int, Entity> ServerToClientEntityMap { get; }
		public Dictionary<int, int> ClientToServerEntityMap { get; }
		public Dictionary<int, Entity> PeerIdToPlayerEntityMap { get; }
		public Entity[] PlayerEntities { get; }

		public NetworkMappingState(
			Dictionary<int, Entity> serverToClientEntityMap,
			Dictionary<int, int> clientToServerEntityMap,
			Dictionary<int, Entity> peerIdToPlayerEntityMap)
		{
			ServerToClientEntityMap = serverToClientEntityMap;
			ClientToServerEntityMap = clientToServerEntityMap;
			PeerIdToPlayerEntityMap = peerIdToPlayerEntityMap;
			PlayerEntities = new Entity[25];
		}
	}
}