using System;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client.Services;

/// <summary>
///     Making a splat map reference its neighbours' textures: after an edit, every touched cell adopts
///     the indices its four neighbours blend so that adjacent quads can share a texture slot instead of
///     falling back to a blend of two different slots. A pure pass over the matrix it is handed.
/// </summary>
internal static class EditorSplatAlignment
{
	internal static void AlignSplatMapMatrix(TerrainSplatWeights[,] splatMatrix, int minX, int minZ, int maxX, int maxZ)
	{
		int width = splatMatrix.GetLength(0);
		int depth = splatMatrix.GetLength(1);

		minX = Math.Max(0, minX);
		minZ = Math.Max(0, minZ);
		maxX = Math.Min(width - 1, maxX);
		maxZ = Math.Min(depth - 1, maxZ);

		for (int z = minZ; z <= maxZ; z++)
		{
			for (int x = minX; x <= maxX; x++)
			{
				ProcessSplatNeighbors(splatMatrix, x, z, width, depth);
			}
		}
	}

	private static void ProcessSplatNeighbors(TerrainSplatWeights[,] splatMatrix, int x, int z, int width, int depth)
	{
		for (int dz = -1; dz <= 1; dz++)
		{
			for (int dx = -1; dx <= 1; dx++)
			{
				if (dx == 0 && dz == 0) continue;
				int nx = x + dx;
				int nz = z + dz;
				if (nx >= 0 && nx < width && nz >= 0 && nz < depth)
				{
					ProcessSingleNeighborSplat(splatMatrix, x, z, nx, nz);
				}
			}
		}
	}

	private static void ProcessSingleNeighborSplat(TerrainSplatWeights[,] splatMatrix, int x, int z, int nx, int nz)
	{
		var neighbor = splatMatrix[nx, nz];
		if (neighbor.Weight0 > 0.001f) TryAddIndexToUnusedSlot(splatMatrix, x, z, neighbor.Index0);
		if (neighbor.Weight1 > 0.001f) TryAddIndexToUnusedSlot(splatMatrix, x, z, neighbor.Index1);
		if (neighbor.Weight2 > 0.001f) TryAddIndexToUnusedSlot(splatMatrix, x, z, neighbor.Index2);
		if (neighbor.Weight3 > 0.001f) TryAddIndexToUnusedSlot(splatMatrix, x, z, neighbor.Index3);
	}

	private static void TryAddIndexToUnusedSlot(TerrainSplatWeights[,] splatMatrix, int x, int z, int index)
	{
		var current = splatMatrix[x, z];

		if (HasIndex(ref current, index)) return;

		if (TryAssignPreferredSlot(ref current, index))
		{
			splatMatrix[x, z] = current;
			return;
		}

		if (TryAssignAnyAvailableSlot(ref current, index))
		{
			splatMatrix[x, z] = current;
		}
	}

	private static bool HasIndex(ref TerrainSplatWeights current, int index)
	{
		return current.Index0 == index || current.Index1 == index || current.Index2 == index || current.Index3 == index;
	}

	private static bool TryAssignPreferredSlot(ref TerrainSplatWeights current, int index)
	{
		int preferredSlot = index % 4;
		bool slotAvailable = preferredSlot switch
		{
			0 => current.Weight0 <= 0.001f,
			1 => current.Weight1 <= 0.001f,
			2 => current.Weight2 <= 0.001f,
			3 => current.Weight3 <= 0.001f,
			_ => false
		};

		if (!slotAvailable) return false;

		switch (preferredSlot)
		{
			case 0:
				current.Index0 = index;
				current.Weight0 = 0.0f;
				break;
			case 1:
				current.Index1 = index;
				current.Weight1 = 0.0f;
				break;
			case 2:
				current.Index2 = index;
				current.Weight2 = 0.0f;
				break;
			case 3:
				current.Index3 = index;
				current.Weight3 = 0.0f;
				break;
		}

		return true;
	}

	private static bool TryAssignAnyAvailableSlot(ref TerrainSplatWeights current, int index)
	{
		if (current.Weight0 <= 0.001f)
		{
			current.Index0 = index;
			current.Weight0 = 0.0f;
			return true;
		}

		if (current.Weight1 <= 0.001f)
		{
			current.Index1 = index;
			current.Weight1 = 0.0f;
			return true;
		}

		if (current.Weight2 <= 0.001f)
		{
			current.Index2 = index;
			current.Weight2 = 0.0f;
			return true;
		}

		if (current.Weight3 <= 0.001f)
		{
			current.Index3 = index;
			current.Weight3 = 0.0f;
			return true;
		}

		return false;
	}
}
