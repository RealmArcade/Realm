using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using MemoryPack;
using Realm.Ecs;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using Realm.Shared;
using Realm.Shared.Diagnostics;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Simulation;

namespace Realm.ListenServer;

/// <summary>navmesh obstacles and disconnects.</summary>
public partial class ListenServerHost
	{
	private void SendNavMeshObstacles(int peerId)
	{
		if (!TryBuildObstacleVolumes(out NavMeshObstacleVolumes volumes, out int signature, out int count)) return;

		_obstacleSignaturePerPeer[peerId] = signature;
		_transport.Send(peerId, NetworkFrameKind.NavMeshObstacles, MemoryPackSerializer.Serialize(volumes));
		_log.Write($"peer {peerId} given the {count} obstacle volumes this match pathing was baked with");
	}

	private void BroadcastNavMeshObstaclesWhenChanged()
	{
		if (_seatedPeers.Count == 0) return;
		if (!TryBuildObstacleVolumes(out NavMeshObstacleVolumes volumes, out int signature, out int count)) return;

		int staleCount = 0;
		foreach (int seatedPeerId in _seatedPeers.Keys)
		{
			if (staleCount == _peersOwedObstacleVolumes.Length) break;
			if (_obstacleSignaturePerPeer.TryGetValue(seatedPeerId, out int sentSignature) && sentSignature == signature) continue;

			_peersOwedObstacleVolumes[staleCount++] = seatedPeerId;
		}

		if (staleCount == 0) return;

		byte[] payload = MemoryPackSerializer.Serialize(volumes);
		for (int index = 0; index < staleCount; index++)
		{
			_obstacleSignaturePerPeer[_peersOwedObstacleVolumes[index]] = signature;
			_transport.Send(_peersOwedObstacleVolumes[index], NetworkFrameKind.NavMeshObstacles, payload);
		}

		_log.Write($"the match now paths with {count} obstacle volumes; {staleCount} peer(s) were given the new set");
	}

	private bool TryBuildObstacleVolumes(out NavMeshObstacleVolumes volumes, out int signature, out int count)
	{
		volumes = default;
		signature = 0;
		count = 0;

		TerrainNavMeshService? navMesh = _simulation?.Map.Simulation.TerrainNavMeshService;
		if (navMesh == null) return false;

		if (!navMesh.HasBakedMesh) return false;

		ReadOnlySpan<System.Numerics.Vector3> positions = navMesh.BakedObstaclePositions;
		ReadOnlySpan<float> radii = navMesh.BakedObstacleRadii;
		count = Math.Min(positions.Length, radii.Length);

		volumes = new NavMeshObstacleVolumes
		{
			Positions = new NetworkVector3[count],
			Radii = new float[count]
		};

		int hash = 17;
		for (int index = 0; index < count; index++)
		{
			System.Numerics.Vector3 position = positions[index];
			float radius = radii[index];
			volumes.Positions[index] = new NetworkVector3(position.X, position.Y, position.Z);
			volumes.Radii[index] = radius;

			hash = hash * 31 + BitConverter.SingleToInt32Bits(position.X);
			hash = hash * 31 + BitConverter.SingleToInt32Bits(position.Y);
			hash = hash * 31 + BitConverter.SingleToInt32Bits(position.Z);
			hash = hash * 31 + BitConverter.SingleToInt32Bits(radius);
		}

		signature = hash;
		return true;
	}

	private void DisconnectPeer(int peerId)
	{
		if (!_seatedPeers.Remove(peerId)) return;

		_log.Write($"peer {peerId} was dropped by the map");
		_transport.Send(peerId, NetworkFrameKind.Shutdown, Array.Empty<byte>());
		_transport.Disconnect(peerId);
	}
}
