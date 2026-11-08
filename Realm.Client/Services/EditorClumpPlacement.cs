using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Ecs.Components.Core;

namespace Realm.Client.Services;

/// <summary>
/// Scattering a prop, unit or decal clump inside the brush: random or square jitter, a terrain height under
/// each piece, scale variation, and a spacing check against both the pieces of this clump and the points
/// already placed in the drag, so the pieces do not overlap. It is a pure function of its arguments - the
/// clump session hands it the points it has collected and the editor hands it the symmetry settings.
/// </summary>
internal static class EditorClumpPlacement
{

	public static List<EntitySpawnRequest> BuildSpawnRequests(
		Godot.Vector3 centerPos,
		EditorTool activeTool,
		string activePlaceId,
		int placeUnitPlayerIndex,
		float placementScale,
		float clumpCount,
		float clumpScale,
		float brushRadius,
		bool brushIsSquare,
		bool randomRotation,
		bool randomScale,
		float placementRotation,
		MirrorMode mirrorMode,
		float assetBaseCollisionRadius,
		IReadOnlyList<(Vector3 Position, float Radius)> separationPoints,
		Vector2 symmetryPivot,
		int symmetryFolds)
	{
		if (string.IsNullOrEmpty(activePlaceId)) return new List<EntitySpawnRequest>();

		var requests = new List<EntitySpawnRequest>();
		int spawnCount = Mathf.Max(1, (int)Math.Round(clumpCount));

		ref var terrain = ref EditorService.GetTerrainState();
		float quadSize = terrain.Cells != null ? terrain.QuadSize : 1f;
		int terrainWidth = terrain.Cells != null ? terrain.Width : 0;
		int terrainDepth = terrain.Cells != null ? terrain.Depth : 0;
		float halfW = terrainWidth / 2.0f * quadSize;
		float halfD = terrainDepth / 2.0f * quadSize;

		float baseRadius = Mathf.Max(0.1f, assetBaseCollisionRadius);
		float autoClumpSpacing = Mathf.Clamp(brushRadius / (1.5f * Mathf.Sqrt(Mathf.Max(1, spawnCount))), 0.5f, 4.0f);

		int maxAttemptsPerObject = 30;

		for (int i = 0; i < spawnCount; i++)
		{
			bool placed = false;
			for (int attempt = 0; attempt < maxAttemptsPerObject; attempt++)
			{
				float dx = 0.0f;
				float dz = 0.0f;
				if (brushIsSquare)
				{
					dx = (float)(GD.Randf() * 2.0 - 1.0) * brushRadius;
					dz = (float)(GD.Randf() * 2.0 - 1.0) * brushRadius;
				}
				else
				{
					float r = Mathf.Sqrt((float)GD.Randf()) * brushRadius;
					float theta = (float)(GD.Randf() * Mathf.Pi * 2.0);
					dx = r * Mathf.Cos(theta);
					dz = r * Mathf.Sin(theta);
				}

				Godot.Vector3 spawnPos = new Godot.Vector3(centerPos.X + dx, centerPos.Y, centerPos.Z + dz);
				if (terrain.Cells != null)
				{
					if (Mathf.Abs(spawnPos.X) > halfW || Mathf.Abs(spawnPos.Z) > halfD) continue;
				}

				spawnPos.Y = EditorTerrainQueries.GetHeightAt(spawnPos);

				float offsetRange = clumpScale * placementScale;
				float minScale = Mathf.Max(0.01f, placementScale - offsetRange);
				float maxScale = placementScale + offsetRange;
				float scaleVal = minScale + (float)GD.Randf() * (maxScale - minScale);

				float candidateRadius = baseRadius * scaleVal;

				bool collision = false;
				foreach (var req in requests)
				{
					float reqRadius = baseRadius * req.Scale;
					float minDist = (candidateRadius + reqRadius) * autoClumpSpacing;
					float distSq = (spawnPos.X - req.Position.X) * (spawnPos.X - req.Position.X) +
								   (spawnPos.Z - req.Position.Z) * (spawnPos.Z - req.Position.Z);
					if (distSq < minDist * minDist)
					{
						collision = true;
						break;
					}
				}

				if (collision) continue;

				foreach (var sessionPt in separationPoints)
				{
					float minDist = (candidateRadius + sessionPt.Radius) * autoClumpSpacing;
					float distSq = (spawnPos.X - sessionPt.Position.X) * (spawnPos.X - sessionPt.Position.X) +
								   (spawnPos.Z - sessionPt.Position.Z) * (spawnPos.Z - sessionPt.Position.Z);
					if (distSq < minDist * minDist)
					{
						collision = true;
						break;
					}
				}

				if (collision) continue;

				float rotY = randomRotation ? (float)(GD.Randf() * 360.0) : placementRotation;
				string spawnType = activeTool == EditorTool.PlaceUnit ? "unit"
					: activeTool == EditorTool.PlaceProp ? "prop"
					: "decal";
				int playerIndex = activeTool == EditorTool.PlaceUnit ? placeUnitPlayerIndex : 0;

				requests.Add(new EntitySpawnRequest
				{
					Type = spawnType,
					Id = activePlaceId,
					Position = spawnPos,
					Rotation = rotY,
					Scale = scaleVal,
					Player = playerIndex
				});

				if (mirrorMode != MirrorMode.None)
				{
					EditorGridSnapping.AddMirroredRequests(requests, spawnType, activePlaceId, spawnPos, rotY, scaleVal, playerIndex, mirrorMode, symmetryPivot, symmetryFolds);
				}

				placed = true;
				break;
			}

			if (!placed && requests.Count > 0)
			{
			}
		}

		return requests;
	}
}
