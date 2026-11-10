using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Godot;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.VFX;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using Realm.Shared.Metadata;
using Realm.Shared.Network;

namespace Realm.Client.Core;


public partial class GameHost
{
	private void ApplyNavMeshObstacles(NavMeshObstacleVolumes volumes)
	{
		StopAwaitingAuthoritativeObstacleVolumes();

		if (volumes.Positions == null || volumes.Radii == null) return;

		int count = Math.Min(volumes.Positions.Length, volumes.Radii.Length);
		var positions = new System.Numerics.Vector3[count];
		for (int index = 0; index < count; index++)
		{
			NetworkVector3 position = volumes.Positions[index];
			positions[index] = new System.Numerics.Vector3(position.X, position.Y, position.Z);
		}

		var radii = new float[count];
		Array.Copy(volumes.Radii, radii, count);

		_authoritativeObstaclePositions = positions;
		_authoritativeObstacleRadii = radii;
		_hasAuthoritativeObstacleVolumes = true;

		ApplyStoredObstacleVolumes();
		RebakeNavMesh();
	}

	private System.Numerics.Vector3[] _authoritativeObstaclePositions = System.Array.Empty<System.Numerics.Vector3>();
	private float[] _authoritativeObstacleRadii = System.Array.Empty<float>();
	private bool _hasAuthoritativeObstacleVolumes;

	private void ApplyStoredObstacleVolumes()
	{
		if (!_hasAuthoritativeObstacleVolumes) return;

		TerrainNavMeshService? navMesh = GameContext.Simulation.TerrainNavMeshService;
		if (navMesh == null) return;

		navMesh.SetAuthoritativeObstacleVolumes(_authoritativeObstaclePositions, _authoritativeObstacleRadii);
	}

}
