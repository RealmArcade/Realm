using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client.Services;

/// <summary>
/// Where a point in the world lands on the editor grid, and where its mirrored and rotated copies land.
/// Everything here is a pure conversion of a position plus the map's dimensions, so it can be answered
/// without touching the editor's own selection or clipboard state.
/// </summary>
public static class EditorGridSnapping
{
	public static List<MirroredTransform> GetMirroredTransforms(Godot.Vector3 pos, float rotation, MirrorMode mirrorMode, Vector2 pivot, int folds)
	{
		var list = new List<MirroredTransform>();
		if (mirrorMode == MirrorMode.None) return list;

		Vector2 p = pivot;
		int n = folds;
		if (n < 2) n = 2;

		float dx = pos.X - p.X;
		float dz = pos.Z - p.Y;

		bool isHoriz = mirrorMode == MirrorMode.Horizontal || mirrorMode == MirrorMode.Both;
		bool isVert = mirrorMode == MirrorMode.Vertical || mirrorMode == MirrorMode.Both;
		if (isHoriz) list.Add(new MirroredTransform { Position = new Godot.Vector3(p.X - dx, pos.Y, p.Y + dz), Rotation = 180.0f - rotation });
		if (isVert) list.Add(new MirroredTransform { Position = new Godot.Vector3(p.X + dx, pos.Y, p.Y - dz), Rotation = -rotation });
		if (mirrorMode == MirrorMode.Both) list.Add(new MirroredTransform { Position = new Godot.Vector3(p.X - dx, pos.Y, p.Y - dz), Rotation = rotation + 180.0f });
		if (mirrorMode == MirrorMode.Rotational)
		{
			for (int k = 1; k < n; k++)
			{
				float angleRad = k * (Mathf.Tau / n);
				float cosA = Mathf.Cos(angleRad);
				float sinA = Mathf.Sin(angleRad);
				float rx = p.X + dx * cosA - dz * sinA;
				float rz = p.Y + dx * sinA + dz * cosA;
				float rotK = (rotation + k * (360.0f / n)) % 360.0f;
				if (rotK < 0) rotK += 360.0f;
				list.Add(new MirroredTransform { Position = new Godot.Vector3(rx, pos.Y, rz), Rotation = rotK });
			}
		}

		return list;
	}

	public static List<Godot.Vector3> GetMirroredPositions(Godot.Vector3 pos, MirrorMode mirrorMode, Vector2 pivot, int folds)
	{
		var list = new List<Godot.Vector3>();
		var transforms = GetMirroredTransforms(pos, 0.0f, mirrorMode, pivot, folds);
		foreach (var t in transforms)
		{
			list.Add(t.Position);
		}

		return list;
	}

	public static (int startX, int startZ, int targetWidth, int targetDepth) GetAnchoredPasteBounds(
		CopiedAreaTemplate? copiedArea, int targetX, int targetZ, float rotationDegrees, PasteReflection reflection)
	{
		if (copiedArea == null) return (targetX, targetZ, 0, 0);

		float r = rotationDegrees % 360.0f;
		if (r < 0) r += 360.0f;
		int rotSteps = (int)Math.Round(r / 90.0f) % 4;

		int pasteWidth = copiedArea.Width;
		int pasteDepth = copiedArea.Depth;
		int targetWidth = pasteWidth;
		int targetDepth = pasteDepth;
		if (rotSteps == 1 || rotSteps == 3)
		{
			targetWidth = pasteDepth;
			targetDepth = pasteWidth;
		}

		int anchorX = copiedArea.AnchorTileX;
		int anchorZ = copiedArea.AnchorTileZ;

		if (reflection == PasteReflection.Horizontal) anchorX = pasteWidth - 1 - anchorX;
		else if (reflection == PasteReflection.Vertical) anchorZ = pasteDepth - 1 - anchorZ;

		int rotAnchorX = anchorX;
		int rotAnchorZ = anchorZ;
		if (rotSteps == 1)
		{
			rotAnchorX = pasteDepth - 1 - anchorZ;
			rotAnchorZ = anchorX;
		}
		else if (rotSteps == 2)
		{
			rotAnchorX = pasteWidth - 1 - anchorX;
			rotAnchorZ = pasteDepth - 1 - anchorZ;
		}
		else if (rotSteps == 3)
		{
			rotAnchorX = anchorZ;
			rotAnchorZ = pasteWidth - 1 - anchorX;
		}

		return (targetX - rotAnchorX, targetZ - rotAnchorZ, targetWidth, targetDepth);
	}

	public static (int cx, int cz) WorldPosToCellCoords(Godot.Vector3 worldPos)
	{
		ref var terrain = ref EditorService.GetTerrainState();
		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;
		float fx = worldPos.X / quadSize + width / 2.0f;
		float fz = worldPos.Z / quadSize + depth / 2.0f;
		int cx = Mathf.Clamp(Mathf.FloorToInt(fx), 0, width - 1);
		int cz = Mathf.Clamp(Mathf.FloorToInt(fz), 0, depth - 1);
		return (cx, cz);
	}

	public static Godot.Vector3 SnapToGrid(Godot.Vector3 worldPos)
	{
		ref var terrain = ref EditorService.GetTerrainState();
		if (terrain.Cells == null) return worldPos;
		float quadSize = terrain.QuadSize;
		int width = terrain.Width;
		int depth = terrain.Depth;
		float fx = Mathf.Round(worldPos.X / quadSize + width / 2.0f);
		worldPos.X = (Mathf.Clamp(fx, 0, width) - width / 2.0f) * quadSize;
		float fz = Mathf.Round(worldPos.Z / quadSize + depth / 2.0f);
		worldPos.Z = (Mathf.Clamp(fz, 0, depth) - depth / 2.0f) * quadSize;
		return worldPos;
	}

	public static Godot.Vector3 SnapToVertex(Godot.Vector3 worldPos)
	{
		ref var terrain = ref EditorService.GetTerrainState();
		if (terrain.Cells == null) return worldPos;
		float quadSize = terrain.QuadSize;
		int width = terrain.Width;
		int depth = terrain.Depth;
		float fx = Mathf.Round(worldPos.X / quadSize + width / 2.0f);
		int vx = Mathf.Clamp((int)fx, 0, width);
		float fz = Mathf.Round(worldPos.Z / quadSize + depth / 2.0f);
		int vz = Mathf.Clamp((int)fz, 0, depth);
		float worldX = (vx - width / 2.0f) * quadSize;
		float worldZ = (vz - depth / 2.0f) * quadSize;
		float height = RuntimeTerrain.GetGridNodeHeight(vx, vz, terrain.Cells, width, depth);
		return new Godot.Vector3(worldX, height, worldZ);
	}

	public static (int vx, int vz) WorldPosToVertexCoords(Godot.Vector3 worldPos)
	{
		ref var terrain = ref EditorService.GetTerrainState();
		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;
		float fx = worldPos.X / quadSize + width / 2.0f;
		float fz = worldPos.Z / quadSize + depth / 2.0f;
		int vx = Mathf.Clamp((int)Math.Round(fx), 0, width);
		int vz = Mathf.Clamp((int)Math.Round(fz), 0, depth);
		return (vx, vz);
	}

	public static void SanitizeCornerHeights(ref TerrainState terrain)
	{
		var cells = terrain.Cells;
		if (cells == null) return;

		int w = terrain.Width;
		int d = terrain.Depth;

		for (int gz = 0; gz <= d; gz++)
		{
			for (int gx = 0; gx <= w; gx++)
			{
				EditorTerrainGeometry.SanitizeCellCorners(cells, gx, gz, w, d, EditorTerrainGeometry.GetGridNodeHeight(in terrain, gx, gz));
			}
		}
	}

	public static void AddMirroredRequests(
		List<EntitySpawnRequest> requests,
		string type, string id,
		Godot.Vector3 pos, float rotation, float scale,
		int player,
		MirrorMode mirrorMode,
		Vector2 symmetryPivot,
		int symmetryFolds)
	{
		var mirrored = EditorGridSnapping.GetMirroredTransforms(pos, rotation, mirrorMode, symmetryPivot, symmetryFolds);
		foreach (var m in mirrored)
		{
			Godot.Vector3 mPos = m.Position;
			mPos.Y = EditorTerrainQueries.GetHeightAt(mPos);
			requests.Add(new EntitySpawnRequest { Type = type, Id = id, Position = mPos, Rotation = m.Rotation, Scale = scale, Player = player });
		}
	}
}
