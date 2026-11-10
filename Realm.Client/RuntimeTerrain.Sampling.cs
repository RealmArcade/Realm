using Realm.Shared.Maps;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using DotRecast.Detour;
using Godot;
using Godot.Collections;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Array = Godot.Collections.Array;

namespace Realm.Client;
public partial class RuntimeTerrain
{
	public void BakeNavMesh()
	{
		TerrainStateStore.BakeNavMesh();
	}

	public bool TrySampleHeight(float worldX, float worldZ, out float height)
	{
		height = 0.0f;

		TerrainCell[,] cells = Cells;
		if (cells == null) return false;

		int cellWidth = cells.GetLength(0);
		int cellDepth = cells.GetLength(1);
		if (cellWidth <= 0 || cellDepth <= 0) return false;

		float quadSize = QuadSize;
		if (quadSize <= 0.0f) return false;

		float localX = (worldX + (cellWidth / 2.0f) * quadSize) / quadSize;
		float localZ = (worldZ + (cellDepth / 2.0f) * quadSize) / quadSize;

		int x0 = Math.Clamp((int)MathF.Floor(localX), 0, cellWidth - 1);
		int z0 = Math.Clamp((int)MathF.Floor(localZ), 0, cellDepth - 1);

		float tx = Math.Clamp(localX - x0, 0.0f, 1.0f);
		float tz = Math.Clamp(localZ - z0, 0.0f, 1.0f);

		TerrainCell cell = cells[x0, z0];
		height = (1 - tx) * (1 - tz) * cell.Y_NW
		         + tx * (1 - tz) * cell.Y_NE
		         + (1 - tx) * tz * cell.Y_SW
		         + tx * tz * cell.Y_SE;
		return true;
	}

	public void GetHeightAndNormal(float worldX, float worldZ, out float height, out Vector3 normal)
	{
		SampleHeightAndNormal(Cells, QuadSize, worldX, worldZ, out height, out normal);
	}

	/// <summary>
	///     Samples the terrain surface from the cells themselves. It takes the data rather than this node so a worker that
	///     is only allowed to read managed state can place a unit on the ground without reaching a scene node at all.
	/// </summary>
	public static void SampleHeightAndNormal(TerrainCell[,]? cells, float quadSize, float worldX, float worldZ, out float height, out Vector3 normal)
	{
		if (cells == null)
		{
			height = 0.0f;
			normal = Vector3.Up;
			return;
		}

		int cellW = cells.GetLength(0);
		int cellD = cells.GetLength(1);
		if (cellW <= 0 || cellD <= 0)
		{
			height = 0.0f;
			normal = Vector3.Up;
			return;
		}

		if (quadSize <= 0.0f)
		{
			height = 0.0f;
			normal = Vector3.Up;
			return;
		}

		float halfW = (cellW / 2.0f) * quadSize;
		float halfD = (cellD / 2.0f) * quadSize;

		float localX = (worldX + halfW) / quadSize;
		float localZ = (worldZ + halfD) / quadSize;

		int x0 = Math.Clamp((int)MathF.Floor(localX), 0, cellW - 1);
		int z0 = Math.Clamp((int)MathF.Floor(localZ), 0, cellD - 1);

		float tx = Math.Clamp(localX - x0, 0.0f, 1.0f);
		float tz = Math.Clamp(localZ - z0, 0.0f, 1.0f);

		var cell = cells[x0, z0];
		float h00 = cell.Y_NW;
		float h10 = cell.Y_NE;
		float h01 = cell.Y_SW;
		float h11 = cell.Y_SE;
		height = (1 - tx) * (1 - tz) * h00 + tx * (1 - tz) * h10 + (1 - tx) * tz * h01 + tx * tz * h11;
		Vector3 n00 = GetVertexNormal(x0, z0, cells, cellW, cellD, quadSize);
		Vector3 n10 = GetVertexNormal(x0 + 1, z0, cells, cellW, cellD, quadSize);
		Vector3 n01 = GetVertexNormal(x0, z0 + 1, cells, cellW, cellD, quadSize);
		Vector3 n11 = GetVertexNormal(x0 + 1, z0 + 1, cells, cellW, cellD, quadSize);
		normal = ((1 - tx) * (1 - tz) * n00 + tx * (1 - tz) * n10 + (1 - tx) * tz * n01 + tx * tz * n11).Normalized();
	}

	public static Vector3 GetVertexNormal(int x, int z, TerrainCell[,] cells, int w, int d, float quadSize)
	{
		if (cells == null || w <= 0 || d <= 0) return Vector3.Up;

		float dx = CalculateNormalDeltaX(x, z, cells, w, d, quadSize);
		float dz = CalculateNormalDeltaZ(x, z, cells, w, d, quadSize);

		if (Math.Abs(dx) < 0.0001f && Math.Abs(dz) < 0.0001f)
		{
			return Vector3.Up;
		}

		Vector3 tangentX = new Vector3(quadSize, dx * quadSize, 0.0f).Normalized();
		Vector3 tangentZ = new Vector3(0.0f, dz * quadSize, quadSize).Normalized();
		return tangentZ.Cross(tangentX).Normalized();
	}

	private static float CalculateNormalDeltaX(int x, int z, TerrainCell[,] cells, int w, int d, float quadSize)
	{
		if (x > 0 && x < w)
		{
			return (GetGridNodeHeight(x + 1, z, cells, w, d) - GetGridNodeHeight(x - 1, z, cells, w, d)) / (2.0f * quadSize);
		}

		if (x < w)
		{
			return (GetGridNodeHeight(x + 1, z, cells, w, d) - GetGridNodeHeight(x, z, cells, w, d)) / quadSize;
		}

		if (x > 0)
		{
			return (GetGridNodeHeight(x, z, cells, w, d) - GetGridNodeHeight(x - 1, z, cells, w, d)) / quadSize;
		}

		return 0.0f;
	}

	private static float CalculateNormalDeltaZ(int x, int z, TerrainCell[,] cells, int w, int d, float quadSize)
	{
		if (z > 0 && z < d)
		{
			return (GetGridNodeHeight(x, z + 1, cells, w, d) - GetGridNodeHeight(x, z - 1, cells, w, d)) / (2.0f * quadSize);
		}

		if (z < d)
		{
			return (GetGridNodeHeight(x, z + 1, cells, w, d) - GetGridNodeHeight(x, z, cells, w, d)) / quadSize;
		}

		if (z > 0)
		{
			return (GetGridNodeHeight(x, z, cells, w, d) - GetGridNodeHeight(x, z - 1, cells, w, d)) / quadSize;
		}

		return 0.0f;
	}

	public Vector3 GetVertexNormal(int x, int z)
	{
		return GetVertexNormal(x, z, Cells, Width, Depth, QuadSize);
	}
}
