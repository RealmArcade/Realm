using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using Realm.Client.Core;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client.Services;

/// <summary>
/// The ramp tool: the world-space start point of a ramp drag and the height interpolation along it, with
/// the pathing and terrain writes each touched grid node needs. It owns the drag state and writes through
/// <see cref="EditorTerrainSurface"/>; the editor only starts and applies it.
/// </summary>
public sealed class EditorRampTool
{
	private readonly EditorTerrainSurface _surface;

	private Godot.Vector3? _rampStartPos;

	public EditorRampTool(EditorTerrainSurface surface)
	{
		_surface = surface;
	}

	public Godot.Vector3? RampStartPos => _rampStartPos;

	public void SetStart(Godot.Vector3? pos)
	{
		_rampStartPos = pos;
	}

	private void ProcessRampCell(Godot.Vector3 start, Godot.Vector3 end, float brushRadius, float segmentLengthSquared, int width, int depth, float quadSize, int gridX, int gridZ, ref TerrainState terrain, ref bool modified, HashSet<Vector2I> modifiedCells)
	{
		float worldX = (gridX - width / 2.0f) * quadSize;
		float worldZ = (gridZ - depth / 2.0f) * quadSize;
		float interpolationFactor = ((worldX - start.X) * (end.X - start.X) + (worldZ - start.Z) * (end.Z - start.Z)) / segmentLengthSquared;
		interpolationFactor = Mathf.Clamp(interpolationFactor, 0.0f, 1.0f);
		float projectedX = start.X + interpolationFactor * (end.X - start.X);
		float projectedZ = start.Z + interpolationFactor * (end.Z - start.Z);
		float distanceToProjected = Mathf.Sqrt((worldX - projectedX) * (worldX - projectedX) + (worldZ - projectedZ) * (worldZ - projectedZ));
		if (distanceToProjected > brushRadius) return;

		float targetHeight = Mathf.Lerp(start.Y, end.Y, interpolationFactor);
		float innerRadius = brushRadius * 0.7f;
		float oldHeight = EditorTerrainGeometry.GetGridNodeHeight(in terrain, gridX, gridZ);
		float newHeight = targetHeight;

		if (distanceToProjected > innerRadius)
		{
			float edgeFactor = 1.0f - ((distanceToProjected - innerRadius) / Math.Max(0.001f, brushRadius - innerRadius));
			edgeFactor = Mathf.Sin(edgeFactor * Mathf.Pi / 2.0f);
			newHeight = Mathf.Lerp(oldHeight, targetHeight, edgeFactor);
		}

		if (Mathf.Abs(newHeight - oldHeight) <= 0.001f) return;

		int cellX = Math.Clamp(gridX, 0, width - 1);
		int cellZ = Math.Clamp(gridZ, 0, depth - 1);
		if (terrain.PathingCodes != null && terrain.Cells != null)
		{
			int defaultPathBefore = EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
			if (terrain.PathingCodes[cellX, cellZ] == defaultPathBefore)
			{
				terrain.PathingCodes[cellX, cellZ] = EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
			}
		}

		EditorTerrainGeometry.SetGridNodeHeight(ref terrain, gridX, gridZ, newHeight);
		int rampPaintIdx = GameContext.Host != null ? GameContext.EditorService.Tools.PaintTextureIndex : 0;
		int rampCliffIdx = GameContext.Host != null ? GameContext.EditorService.Tools.CliffPaintTextureIndex : 1;
		_surface.SetGridNodeSplat(gridX, gridZ, new TerrainSplatWeights { Index0 = rampPaintIdx, Weight0 = 1.0f }, new TerrainSplatWeights { Index0 = rampPaintIdx, Weight0 = 1.0f }, new TerrainSplatWeights { Index0 = rampPaintIdx, Weight0 = 1.0f }, new TerrainSplatWeights { Index0 = rampPaintIdx, Weight0 = 1.0f });
		_surface.SetGridNodeCliffSplat(gridX, gridZ, new TerrainSplatWeights { Index0 = rampCliffIdx, Weight0 = 1.0f }, new TerrainSplatWeights { Index0 = rampCliffIdx, Weight0 = 1.0f }, new TerrainSplatWeights { Index0 = rampCliffIdx, Weight0 = 1.0f }, new TerrainSplatWeights { Index0 = rampCliffIdx, Weight0 = 1.0f });
		modified = true;
		modifiedCells.Add(new Vector2I(gridX, gridZ));
	}

	public bool Apply(Godot.Vector3 start, Godot.Vector3 end, float brushRadius, bool blockMode, float blockLevelHeight)
	{
		ref var terrain = ref EditorService.GetTerrainState();
		var cells = terrain.Cells;
		if (cells == null) return false;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;
		bool modified = false;

		float segmentLengthSquared = (end.X - start.X) * (end.X - start.X) + (end.Z - start.Z) * (end.Z - start.Z);
		if (segmentLengthSquared <= 0.0001f) return false;

		float minWorldX = Mathf.Min(start.X, end.X) - brushRadius;
		float maxWorldX = Mathf.Max(start.X, end.X) + brushRadius;
		float minWorldZ = Mathf.Min(start.Z, end.Z) - brushRadius;
		float maxWorldZ = Mathf.Max(start.Z, end.Z) + brushRadius;

		int minGridX = Mathf.Clamp(Mathf.FloorToInt(minWorldX / quadSize + width / 2.0f), 0, width);
		int maxGridX = Mathf.Clamp(Mathf.CeilToInt(maxWorldX / quadSize + width / 2.0f), 0, width);
		int minGridZ = Mathf.Clamp(Mathf.FloorToInt(minWorldZ / quadSize + depth / 2.0f), 0, depth);
		int maxGridZ = Mathf.Clamp(Mathf.CeilToInt(maxWorldZ / quadSize + depth / 2.0f), 0, depth);

		var modifiedCells = new HashSet<Vector2I>();

		for (int gridZ = minGridZ; gridZ <= maxGridZ; gridZ++)
		{
			for (int gridX = minGridX; gridX <= maxGridX; gridX++)
			{
				ProcessRampCell(start, end, brushRadius, segmentLengthSquared, width, depth, quadSize, gridX, gridZ, ref terrain, ref modified, modifiedCells);
			}
		}

		if (modified)
		{
			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
			TerrainSwatchProfileData? rampProf = null;
			int rampTexIdx = GameContext.Host != null ? GameContext.EditorService.Tools.PaintTextureIndex : 0;
			if (!string.IsNullOrEmpty(wsPath) && GameContext.MetadataService.TryLoadMetadata(wsPath, out var metaRoot) && metaRoot != null)
			{
				string? swatchName = null;
				if (GameContext.Host?.GroundTerrain != null && rampTexIdx >= 0 && rampTexIdx < GameContext.Host.GroundTerrain.LoadedTextureList.Count)
				{
					swatchName = GameContext.Host.GroundTerrain.LoadedTextureList[rampTexIdx];
				}

				if (!string.IsNullOrEmpty(swatchName))
				{
					rampProf = metaRoot.GetTerrainProfile(swatchName);
				}
			}

			foreach (var cellPos in modifiedCells)
			{
				if (rampProf != null && terrain.PathingCodes != null && cellPos.X < width && cellPos.Y < depth)
				{
					terrain.PathingCodes[cellPos.X, cellPos.Y] = rampProf.DefaultPathingCode;
				}

				if (terrain.Cells != null && cellPos.X < width && cellPos.Y < depth)
				{
					float tY = terrain.Cells[cellPos.X, cellPos.Y].CenterHeight;
					var terrainSpawns = EditorProceduralSpawns.BuildTerrain(cellPos.X, cellPos.Y, rampTexIdx, tY, quadSize, width, depth);
					GameContext.Host?.ApplyProceduralSpawns(cellPos.X, cellPos.Y, CollectionsMarshal.AsSpan(terrainSpawns), ProceduralSpawnLayer.Terrain);
				}
			}

			_surface.AlignSplatMapSlots(minGridX - 2, minGridZ - 2, maxGridX + 2, maxGridZ + 2);
		}

		return modified;
	}
}
