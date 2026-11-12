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
	private static (sbyte minTier, sbyte maxTier, int cellCount) CalculateTierBounds(int gx, int gz, TerrainCell[,] cells, int w, int d)
	{
		sbyte minTier = sbyte.MaxValue;
		sbyte maxTier = sbyte.MinValue;
		int cellCount = 0;

		void CheckCell(int cx, int cz)
		{
			if (cx >= 0 && cx < w && cz >= 0 && cz < d)
			{
				sbyte t = cells[cx, cz].MacroTier;
				if (t < minTier) minTier = t;
				if (t > maxTier) maxTier = t;
				cellCount++;
			}
		}

		CheckCell(gx - 1, gz - 1);
		CheckCell(gx, gz - 1);
		CheckCell(gx - 1, gz);
		CheckCell(gx, gz);

		return (minTier, maxTier, cellCount);
	}

	private void ProcessCellQuad(
		TerrainChunk chunk,
		int x, int z,
		TerrainCell[,] cells,
		int w, int d,
		float quadSize,
		float halfWQuadSize,
		float halfDQuadSize,
		TerrainSplatWeights[,] splatMap,
		TerrainSplatWeights[,] cliffSplatMap,
		ref int vertexIndex,
		ref int indexIndex)
	{
		var cell = cells[x, z];
		var heights = ExtractQuadHeights(cell);
		var pos = CalculateQuadPositions(x, z, heights, halfWQuadSize, halfDQuadSize, quadSize);
		var norms = CalculateQuadNormals(x, z, cells, w, d, quadSize);

		float quadCliffWeight = CalculateQuadCliffWeight(x, z, cell, cells, w, d, heights);
		var cliffWeights = CalculateQuadVertexCliffWeights(x, z, cells, w, d, quadSize);

		var (floorS00, floorS10, floorS11, floorS01, floorSC) = GetSplatMapQuad(x, z, splatMap);
		var (cliffS00, cliffS10, cliffS11, cliffS01, cliffSC) = GetSplatMapQuad(x, z, cliffSplatMap);

		var uvs = CalculateQuadUvs(pos);

		var gTex = GetQuadDominantTextures(floorS00, floorS10, floorS11, floorS01);
		var cTex = GetQuadDominantTextures(cliffS00, cliffS10, cliffS11, cliffS01);

		ProcessSubTriangleGround(chunk, pos.NW, pos.NE, pos.C, norms.NW, norms.NE, norms.C, uvs.NW, uvs.NE, uvs.C, cliffWeights.NW, cliffWeights.NE, cliffWeights.C, quadCliffWeight, floorS00, floorS10, floorSC, gTex.tex0, gTex.tex1, gTex.tex2, gTex.tex3, cliffS00, cliffS10, cliffSC, cTex.tex0, cTex.tex1, cTex.tex2, cTex.tex3, ref vertexIndex, ref indexIndex);
		ProcessSubTriangleGround(chunk, pos.NE, pos.SE, pos.C, norms.NE, norms.SE, norms.C, uvs.NE, uvs.SE, uvs.C, cliffWeights.NE, cliffWeights.SE, cliffWeights.C, quadCliffWeight, floorS10, floorS11, floorSC, gTex.tex0, gTex.tex1, gTex.tex2, gTex.tex3, cliffS10, cliffS11, cliffSC, cTex.tex0, cTex.tex1, cTex.tex2, cTex.tex3, ref vertexIndex, ref indexIndex);
		ProcessSubTriangleGround(chunk, pos.SE, pos.SW, pos.C, norms.SE, norms.SW, norms.C, uvs.SE, uvs.SW, uvs.C, cliffWeights.SE, cliffWeights.SW, cliffWeights.C, quadCliffWeight, floorS11, floorS01, floorSC, gTex.tex0, gTex.tex1, gTex.tex2, gTex.tex3, cliffS11, cliffS01, cliffSC, cTex.tex0, cTex.tex1, cTex.tex2, cTex.tex3, ref vertexIndex, ref indexIndex);
		ProcessSubTriangleGround(chunk, pos.SW, pos.NW, pos.C, norms.SW, norms.NW, norms.C, uvs.SW, uvs.NW, uvs.C, cliffWeights.SW, cliffWeights.NW, cliffWeights.C, quadCliffWeight, floorS01, floorS00, floorSC, gTex.tex0, gTex.tex1, gTex.tex2, gTex.tex3, cliffS01, cliffS00, cliffSC, cTex.tex0, cTex.tex1, cTex.tex2, cTex.tex3, ref vertexIndex, ref indexIndex);
	}

	private (float hNW, float hNE, float hSE, float hSW, float hC) ExtractQuadHeights(TerrainCell cell)
	{
		return (cell.Y_NW, cell.Y_NE, cell.Y_SE, cell.Y_SW, cell.CenterHeight);
	}

	private (Vector3 NW, Vector3 NE, Vector3 SE, Vector3 SW, Vector3 C) CalculateQuadPositions(int x, int z, (float hNW, float hNE, float hSE, float hSW, float hC) heights, float halfWQuadSize, float halfDQuadSize, float quadSize)
	{
		return (
			GetWorldPosition(x, z, heights.hNW, halfWQuadSize, halfDQuadSize, quadSize),
			GetWorldPosition(x + 1, z, heights.hNE, halfWQuadSize, halfDQuadSize, quadSize),
			GetWorldPosition(x + 1, z + 1, heights.hSE, halfWQuadSize, halfDQuadSize, quadSize),
			GetWorldPosition(x, z + 1, heights.hSW, halfWQuadSize, halfDQuadSize, quadSize),
			GetWorldPosition(x + 0.5f, z + 0.5f, heights.hC, halfWQuadSize, halfDQuadSize, quadSize)
		);
	}

	private (Vector3 NW, Vector3 NE, Vector3 SE, Vector3 SW, Vector3 C) CalculateQuadNormals(int x, int z, TerrainCell[,] cells, int w, int d, float quadSize)
	{
		Vector3 normNW = GetVertexNormal(x, z, cells, w, d, quadSize);
		Vector3 normNE = GetVertexNormal(x + 1, z, cells, w, d, quadSize);
		Vector3 normSE = GetVertexNormal(x + 1, z + 1, cells, w, d, quadSize);
		Vector3 normSW = GetVertexNormal(x, z + 1, cells, w, d, quadSize);
		Vector3 normC = (normNW + normNE + normSE + normSW).Normalized();

		return (normNW, normNE, normSE, normSW, normC);
	}

	private float CalculateQuadCliffWeight(int x, int z, TerrainCell cell, TerrainCell[,] cells, int w, int d, (float hNW, float hNE, float hSE, float hSW, float hC) heights)
	{
		bool bordersDifferentTier = CheckBordersDifferentTier(x, z, cell.MacroTier, cells, w, d);

		float maxDelta = Math.Max(
			Math.Max(Math.Abs(heights.hNW - heights.hNE), Math.Abs(heights.hNE - heights.hSE)),
			Math.Max(Math.Abs(heights.hSE - heights.hSW), Math.Abs(heights.hSW - heights.hNW))
		);
		float maxCenterDelta = Math.Max(
			Math.Max(Math.Abs(heights.hC - heights.hNW), Math.Abs(heights.hC - heights.hNE)),
			Math.Max(Math.Abs(heights.hC - heights.hSE), Math.Abs(heights.hC - heights.hSW))
		);
		float totalQuadDelta = Math.Max(maxDelta, maxCenterDelta);

		float tierStepThreshold = TerrainCell.TIER_HEIGHT * 0.70f;
		bool isCliffQuad = (bordersDifferentTier && totalQuadDelta >= tierStepThreshold * 0.5f) || totalQuadDelta >= tierStepThreshold;

		return isCliffQuad ? 1.0f : 0.0f;
	}

	private bool CheckBordersDifferentTier(int x, int z, sbyte currentTier, TerrainCell[,] cells, int w, int d)
	{
		if (x > 0 && Math.Abs(cells[x - 1, z].MacroTier - currentTier) >= 1) return true;
		if (x < w - 1 && Math.Abs(cells[x + 1, z].MacroTier - currentTier) >= 1) return true;
		if (z > 0 && Math.Abs(cells[x, z - 1].MacroTier - currentTier) >= 1) return true;
		if (z < d - 1 && Math.Abs(cells[x, z + 1].MacroTier - currentTier) >= 1) return true;
		return false;
	}

	private (float NW, float NE, float SE, float SW, float C) CalculateQuadVertexCliffWeights(int x, int z, TerrainCell[,] cells, int w, int d, float quadSize)
	{
		float cliffNW = GetVertexCliffWeight(x, z, cells, w, d, quadSize);
		float cliffNE = GetVertexCliffWeight(x + 1, z, cells, w, d, quadSize);
		float cliffSE = GetVertexCliffWeight(x + 1, z + 1, cells, w, d, quadSize);
		float cliffSW = GetVertexCliffWeight(x, z + 1, cells, w, d, quadSize);
		float cliffC = (cliffNW + cliffNE + cliffSE + cliffSW) * 0.25f;

		return (cliffNW, cliffNE, cliffSE, cliffSW, cliffC);
	}

	private (TerrainSplatWeights s00, TerrainSplatWeights s10, TerrainSplatWeights s11, TerrainSplatWeights s01, TerrainSplatWeights sC) GetSplatMapQuad(int x, int z, TerrainSplatWeights[,] splatMap)
	{
		if (splatMap == null || splatMap.GetLength(0) == 0 || splatMap.GetLength(1) == 0)
		{
			return (default, default, default, default, default);
		}

		int mapW = splatMap.GetLength(0);
		int mapD = splatMap.GetLength(1);

		TerrainSplatWeights s00 = splatMap[Math.Clamp(x, 0, mapW - 1), Math.Clamp(z, 0, mapD - 1)];
		TerrainSplatWeights s10 = splatMap[Math.Clamp(x + 1, 0, mapW - 1), Math.Clamp(z, 0, mapD - 1)];
		TerrainSplatWeights s11 = splatMap[Math.Clamp(x + 1, 0, mapW - 1), Math.Clamp(z + 1, 0, mapD - 1)];
		TerrainSplatWeights s01 = splatMap[Math.Clamp(x, 0, mapW - 1), Math.Clamp(z + 1, 0, mapD - 1)];
		TerrainSplatWeights sC = BlendSplatWeights(s00, s10, s11, s01);

		return (s00, s10, s11, s01, sC);
	}

	private (Vector2 NW, Vector2 NE, Vector2 SE, Vector2 SW, Vector2 C) CalculateQuadUvs((Vector3 NW, Vector3 NE, Vector3 SE, Vector3 SW, Vector3 C) pos)
	{
		return (
			new Vector2(pos.NW.X, pos.NW.Z),
			new Vector2(pos.NE.X, pos.NE.Z),
			new Vector2(pos.SE.X, pos.SE.Z),
			new Vector2(pos.SW.X, pos.SW.Z),
			new Vector2(pos.C.X, pos.C.Z)
		);
	}

	private void ProcessSubTriangleGround(
		TerrainChunk chunk,
		Vector3 pos0, Vector3 pos1, Vector3 pos2,
		Vector3 norm0, Vector3 norm1, Vector3 norm2,
		Vector2 uv0, Vector2 uv1, Vector2 uv2,
		float cliff0, float cliff1, float cliff2,
		float quadCliff,
		TerrainSplatWeights gS0, TerrainSplatWeights gS1, TerrainSplatWeights gS2,
		int gTex0, int gTex1, int gTex2, int gTex3,
		TerrainSplatWeights cS0, TerrainSplatWeights cS1, TerrainSplatWeights cS2,
		int cTex0, int cTex1, int cTex2, int cTex3,
		ref int vertexIndex,
		ref int indexIndex)
	{
		int baseIndex = vertexIndex;

		var (gw00, gw01, gw02, gw03) = GetSplatWeightsForQuad(gS0, gTex0, gTex1, gTex2, gTex3);
		var (gw10, gw11, gw12, gw13) = GetSplatWeightsForQuad(gS1, gTex0, gTex1, gTex2, gTex3);
		var (gw20, gw21, gw22, gw23) = GetSplatWeightsForQuad(gS2, gTex0, gTex1, gTex2, gTex3);

		var (cw00, cw01, cw02, cw03) = GetSplatWeightsForQuad(cS0, cTex0, cTex1, cTex2, cTex3);
		var (cw10, cw11, cw12, cw13) = GetSplatWeightsForQuad(cS1, cTex0, cTex1, cTex2, cTex3);
		var (cw20, cw21, cw22, cw23) = GetSplatWeightsForQuad(cS2, cTex0, cTex1, cTex2, cTex3);

		PopulateExplicitVertex(chunk, pos0, uv0, norm0, cliff0, quadCliff, gTex0, gTex1, gTex2, gTex3, gw00, gw01, gw02, gw03, cTex0, cTex1, cTex2, cTex3, cw00, cw01, cw02, cw03, ref vertexIndex);
		PopulateExplicitVertex(chunk, pos1, uv1, norm1, cliff1, quadCliff, gTex0, gTex1, gTex2, gTex3, gw10, gw11, gw12, gw13, cTex0, cTex1, cTex2, cTex3, cw10, cw11, cw12, cw13, ref vertexIndex);
		PopulateExplicitVertex(chunk, pos2, uv2, norm2, cliff2, quadCliff, gTex0, gTex1, gTex2, gTex3, gw20, gw21, gw22, gw23, cTex0, cTex1, cTex2, cTex3, cw20, cw21, cw22, cw23, ref vertexIndex);

		chunk.IndicesCache[indexIndex++] = baseIndex;
		chunk.IndicesCache[indexIndex++] = baseIndex + 1;
		chunk.IndicesCache[indexIndex++] = baseIndex + 2;
	}

	private void PopulateExplicitVertex(
		TerrainChunk chunk,
		Vector3 position,
		Vector2 uv,
		Vector3 faceNormal,
		float cliffWeight,
		float quadCliffWeight,
		int gTex0, int gTex1, int gTex2, int gTex3,
		float gw0, float gw1, float gw2, float gw3,
		int cTex0, int cTex1, int cTex2, int cTex3,
		float cw0, float cw1, float cw2, float cw3,
		ref int vertexIndex)
	{
		chunk.VerticesCache[vertexIndex] = position;
		chunk.NormalsCache[vertexIndex] = faceNormal;
		chunk.UvsCache[vertexIndex] = uv;
		chunk.ColorsCache[vertexIndex] = new Color(quadCliffWeight, 1.0f, 1.0f, cliffWeight);

		int sIdx = vertexIndex * 4;
		chunk.TexIndicesCache[sIdx + 0] = gTex0;
		chunk.TexIndicesCache[sIdx + 1] = gTex1;
		chunk.TexIndicesCache[sIdx + 2] = gTex2;
		chunk.TexIndicesCache[sIdx + 3] = gTex3;

		chunk.TexWeightsCache01[sIdx + 0] = gw0 > 0.0001f ? gw0 : 0.0f;
		chunk.TexWeightsCache01[sIdx + 1] = gw1 > 0.0001f ? gw1 : 0.0f;
		chunk.TexWeightsCache01[sIdx + 2] = gw2 > 0.0001f ? gw2 : 0.0f;
		chunk.TexWeightsCache01[sIdx + 3] = gw3 > 0.0001f ? gw3 : 0.0f;

		chunk.CliffIndicesCache[sIdx + 0] = cTex0;
		chunk.CliffIndicesCache[sIdx + 1] = cTex1;
		chunk.CliffIndicesCache[sIdx + 2] = cTex2;
		chunk.CliffIndicesCache[sIdx + 3] = cTex3;

		chunk.CliffWeightsCache[sIdx + 0] = cw0 > 0.0001f ? cw0 : 0.0f;
		chunk.CliffWeightsCache[sIdx + 1] = cw1 > 0.0001f ? cw1 : 0.0f;
		chunk.CliffWeightsCache[sIdx + 2] = cw2 > 0.0001f ? cw2 : 0.0f;
		chunk.CliffWeightsCache[sIdx + 3] = cw3 > 0.0001f ? cw3 : 0.0f;

		vertexIndex++;
	}

	private void WeldChunkVertices(TerrainChunk chunk, ref int vertexIndex, int indexIndex)
	{
		if (vertexIndex == 0)
		{
			return;
		}

		if (chunk.WeldVertexMap.Count == 0)
		{
			chunk.WeldVertexMap.EnsureCapacity(vertexIndex);
		}
		else
		{
			chunk.WeldVertexMap.Clear();
		}

		if (chunk.WeldRemapTable.Length < vertexIndex)
		{
			chunk.WeldRemapTable = new int[Math.Max(vertexIndex, chunk.WeldRemapTable.Length * 2)];
		}

		var vertexMap = chunk.WeldVertexMap;
		var remapTable = chunk.WeldRemapTable;
		int uniqueVertexCount = 0;

		for (int v = 0; v < vertexIndex; v++)
		{
			int texIdx = v * 4;
			TerrainVertexKey key = new TerrainVertexKey(
				chunk.VerticesCache[v],
				chunk.NormalsCache[v],
				chunk.UvsCache[v],
				chunk.ColorsCache[v],
				chunk.TexIndicesCache[texIdx],
				chunk.TexIndicesCache[texIdx + 1],
				chunk.TexIndicesCache[texIdx + 2],
				chunk.TexIndicesCache[texIdx + 3],
				chunk.TexWeightsCache01[texIdx],
				chunk.TexWeightsCache01[texIdx + 1],
				chunk.TexWeightsCache01[texIdx + 2],
				chunk.TexWeightsCache01[texIdx + 3],
				chunk.CliffIndicesCache[texIdx],
				chunk.CliffIndicesCache[texIdx + 1],
				chunk.CliffIndicesCache[texIdx + 2],
				chunk.CliffIndicesCache[texIdx + 3],
				chunk.CliffWeightsCache[texIdx],
				chunk.CliffWeightsCache[texIdx + 1],
				chunk.CliffWeightsCache[texIdx + 2],
				chunk.CliffWeightsCache[texIdx + 3]
			);

			if (vertexMap.TryGetValue(key, out int existingIndex))
			{
				remapTable[v] = existingIndex;
			}
			else
			{
				int newIndex = uniqueVertexCount++;
				vertexMap[key] = newIndex;
				remapTable[v] = newIndex;

				if (newIndex != v)
				{
					chunk.VerticesCache[newIndex] = chunk.VerticesCache[v];
					chunk.NormalsCache[newIndex] = chunk.NormalsCache[v];
					chunk.UvsCache[newIndex] = chunk.UvsCache[v];
					chunk.ColorsCache[newIndex] = chunk.ColorsCache[v];

					int newTexIdx = newIndex * 4;
					chunk.TexIndicesCache[newTexIdx] = chunk.TexIndicesCache[texIdx];
					chunk.TexIndicesCache[newTexIdx + 1] = chunk.TexIndicesCache[texIdx + 1];
					chunk.TexIndicesCache[newTexIdx + 2] = chunk.TexIndicesCache[texIdx + 2];
					chunk.TexIndicesCache[newTexIdx + 3] = chunk.TexIndicesCache[texIdx + 3];

					chunk.TexWeightsCache01[newTexIdx] = chunk.TexWeightsCache01[texIdx];
					chunk.TexWeightsCache01[newTexIdx + 1] = chunk.TexWeightsCache01[texIdx + 1];
					chunk.TexWeightsCache01[newTexIdx + 2] = chunk.TexWeightsCache01[texIdx + 2];
					chunk.TexWeightsCache01[newTexIdx + 3] = chunk.TexWeightsCache01[texIdx + 3];

					chunk.CliffIndicesCache[newTexIdx] = chunk.CliffIndicesCache[texIdx];
					chunk.CliffIndicesCache[newTexIdx + 1] = chunk.CliffIndicesCache[texIdx + 1];
					chunk.CliffIndicesCache[newTexIdx + 2] = chunk.CliffIndicesCache[texIdx + 2];
					chunk.CliffIndicesCache[newTexIdx + 3] = chunk.CliffIndicesCache[texIdx + 3];

					chunk.CliffWeightsCache[newTexIdx] = chunk.CliffWeightsCache[texIdx];
					chunk.CliffWeightsCache[newTexIdx + 1] = chunk.CliffWeightsCache[texIdx + 1];
					chunk.CliffWeightsCache[newTexIdx + 2] = chunk.CliffWeightsCache[texIdx + 2];
					chunk.CliffWeightsCache[newTexIdx + 3] = chunk.CliffWeightsCache[texIdx + 3];
				}
			}
		}

		for (int i = 0; i < indexIndex; i++)
		{
			chunk.IndicesCache[i] = remapTable[chunk.IndicesCache[i]];
		}

		vertexIndex = uniqueVertexCount;
	}

}
