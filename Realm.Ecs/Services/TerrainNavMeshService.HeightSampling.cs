// Extracted from TerrainNavMeshService.cs (task 6 decomposition).
using System;
using System.Collections.Generic;
using System.Numerics;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;

namespace Realm.Ecs.Services;

public partial class TerrainNavMeshService
{
	private float GetVertexHeight(in TerrainState state, int x, int z)
	{
		var cells = state.Cells;
		if (cells == null) return 0.0f;
		int w = state.Width;
		int d = state.Depth;
		if (w <= 0 || d <= 0) return 0.0f;
		int cellX = Math.Clamp(x, 0, w - 1);
		int cellZ = Math.Clamp(z, 0, d - 1);
		if (x < w && z < d) return cells[cellX, cellZ].Y_NW;
		if (x == w && z < d) return cells[w - 1, cellZ].Y_NE;
		if (x < w && z == d) return cells[cellX, d - 1].Y_SW;
		return cells[w - 1, d - 1].Y_SE;
	}

	public void GetHeightAndNormal(in TerrainState state, float worldX, float worldZ, out float height, out Vector3 normal)
	{
		float halfW = state.Width / 2.0f * state.QuadSize;
		float halfD = state.Depth / 2.0f * state.QuadSize;
		float gridX = (worldX + halfW) / state.QuadSize;
		float gridZ = (worldZ + halfD) / state.QuadSize;
		int x0 = (int)Math.Floor(gridX);
		int z0 = (int)Math.Floor(gridZ);
		x0 = Math.Max(0, Math.Min(state.Width - 1, x0));
		z0 = Math.Max(0, Math.Min(state.Depth - 1, z0));
		float tx = gridX - x0;
		float tz = gridZ - z0;
		float h00 = GetVertexHeight(in state, x0, z0);
		float h10 = GetVertexHeight(in state, Math.Min(state.Width, x0 + 1), z0);
		float h01 = GetVertexHeight(in state, x0, Math.Min(state.Depth, z0 + 1));
		float h11 = GetVertexHeight(in state, Math.Min(state.Width, x0 + 1), Math.Min(state.Depth, z0 + 1));
		height = (1 - tx) * (1 - tz) * h00 + tx * (1 - tz) * h10 + (1 - tx) * tz * h01 + tx * tz * h11;
		Vector3 n00 = GetVertexNormal(in state, x0, z0);
		Vector3 n10 = GetVertexNormal(in state, Math.Min(state.Width, x0 + 1), z0);
		Vector3 n01 = GetVertexNormal(in state, x0, Math.Min(state.Depth, z0 + 1));
		Vector3 n11 = GetVertexNormal(in state, Math.Min(state.Width, x0 + 1), Math.Min(state.Depth, z0 + 1));
		normal = Vector3.Normalize((1 - tx) * (1 - tz) * n00 + tx * (1 - tz) * n10 + (1 - tx) * tz * n01 + tx * tz * n11);
	}

	private Vector3 GetVertexNormal(in TerrainState state, int x, int z)
	{
		var cells = state.Cells;
		if (cells == null) return Vector3.UnitY;
		float h = GetVertexHeight(in state, x, z);
		float cliffThreshold = 0.95f * Math.Max(0.1f, state.QuadSize);

		float dx = GetSlopeDx(in state, x, z, h, cliffThreshold);
		float dz = GetSlopeDz(in state, x, z, h, cliffThreshold);

		if (Math.Abs(dx) < 0.001f && Math.Abs(dz) < 0.001f)
		{
			return Vector3.UnitY;
		}

		Vector3 tangentX = Vector3.Normalize(new Vector3(state.QuadSize, dx, 0.0f));
		Vector3 tangentZ = Vector3.Normalize(new Vector3(0.0f, dz, state.QuadSize));
		return Vector3.Normalize(Vector3.Cross(tangentZ, tangentX));
	}

	private static bool IsCliff(bool hasNeighbor, float delta, float cliffThreshold)
	{
		if (!hasNeighbor) return false;
		return Math.Abs(delta) >= cliffThreshold;
	}

	private static float ResolveSingleSide(bool hasNeighbor, bool neighborIsCliff, float neighborDelta)
	{
		if (!hasNeighbor) return 0.0f;
		if (neighborIsCliff) return 0.0f;
		return neighborDelta;
	}

	private static float ResolveNormalSlope(bool hasPositive, bool hasNegative, float heightPositive, float heightNegative, float deltaPositive, float deltaNegative)
	{
		if (hasPositive)
		{
			if (hasNegative)
			{
				return (heightPositive - heightNegative) * 0.5f;
			}

			return deltaPositive;
		}

		if (hasNegative)
		{
			return deltaNegative;
		}

		return 0.0f;
	}

	private static float ResolveSlope(bool hasPositive, bool hasNegative, float deltaPositive, float deltaNegative, bool positiveIsCliff, bool negativeIsCliff, float heightPositive, float heightNegative)
	{
		if (positiveIsCliff)
		{
			if (negativeIsCliff) return 0.0f;
			return ResolveSingleSide(hasNegative, negativeIsCliff, deltaNegative);
		}

		if (negativeIsCliff)
		{
			return ResolveSingleSide(hasPositive, positiveIsCliff, deltaPositive);
		}

		return ResolveNormalSlope(hasPositive, hasNegative, heightPositive, heightNegative, deltaPositive, deltaNegative);
	}

	private float GetSlopeDx(in TerrainState state, int x, int z, float h, float cliffThreshold)
	{
		bool hasRight = x < state.Width;
		bool hasLeft = x > 0;

		float heightRight = hasRight ? GetVertexHeight(in state, x + 1, z) : 0.0f;
		float heightLeft = hasLeft ? GetVertexHeight(in state, x - 1, z) : 0.0f;

		float deltaRight = hasRight ? heightRight - h : 0.0f;
		float deltaLeft = hasLeft ? h - heightLeft : 0.0f;

		bool rightIsCliff = IsCliff(hasRight, deltaRight, cliffThreshold);
		bool leftIsCliff = IsCliff(hasLeft, deltaLeft, cliffThreshold);

		return ResolveSlope(hasRight, hasLeft, deltaRight, deltaLeft, rightIsCliff, leftIsCliff, heightRight, heightLeft);
	}


	private float GetSlopeDz(in TerrainState state, int x, int z, float h, float cliffThreshold)
	{
		bool hasUp = z < state.Depth;
		bool hasDown = z > 0;

		float heightUp = hasUp ? GetVertexHeight(in state, x, z + 1) : 0.0f;
		float heightDown = hasDown ? GetVertexHeight(in state, x, z - 1) : 0.0f;

		float deltaUp = hasUp ? heightUp - h : 0.0f;
		float deltaDown = hasDown ? h - heightDown : 0.0f;

		bool upIsCliff = IsCliff(hasUp, deltaUp, cliffThreshold);
		bool downIsCliff = IsCliff(hasDown, deltaDown, cliffThreshold);

		return ResolveSlope(hasUp, hasDown, deltaUp, deltaDown, upIsCliff, downIsCliff, heightUp, heightDown);
	}
}
