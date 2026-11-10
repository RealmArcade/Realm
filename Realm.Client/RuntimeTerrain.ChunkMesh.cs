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
	protected void UpdateChunkMesh(TerrainChunk chunk, bool rebuildPhysics)
	{
		var cells = Cells;
		if (cells == null) return;

		int w = Width;
		int d = Depth;
		float quadSize = QuadSize;
		float halfWQuadSize = (w / 2.0f) * quadSize;
		float halfDQuadSize = (d / 2.0f) * quadSize;
		var splatMap = SplatMap;
		var cliffSplatMap = CliffSplatMap ?? splatMap;

		int maxVertices = 0;
		int maxIndices = 0;

		CountChunkElements(chunk, cells, w, d, ref maxVertices, ref maxIndices);
		EnsureChunkCaches(chunk, maxVertices, maxIndices);

		int vertexIndex = 0;
		int indexIndex = 0;

		ProcessChunkQuads(chunk, cells, w, d, quadSize, halfWQuadSize, halfDQuadSize, splatMap, cliffSplatMap, ref vertexIndex, ref indexIndex);
		WeldChunkVertices(chunk, ref vertexIndex, indexIndex);
		BuildMeshArraysFromCache(chunk, vertexIndex, indexIndex);
		UpdateChunkBoundsAndPhysics(chunk, w, d, quadSize, vertexIndex, rebuildPhysics);

		if (IsRuntimeOnly) ClearChunkCaches(chunk);
	}

	private void CountChunkElements(TerrainChunk chunk, TerrainCell[,] cells, int w, int d, ref int maxVertices, ref int maxIndices)
	{
		for (int z = chunk.StartZ; z < chunk.EndZ; z++)
		{
			for (int x = chunk.StartX; x < chunk.EndX; x++)
			{
				CountQuadElements(x, z, cells, w, d, ref maxVertices, ref maxIndices);
			}
		}
	}

	private void EnsureChunkCaches(TerrainChunk chunk, int maxVertices, int maxIndices)
	{
		if (chunk.VerticesCache == null || chunk.VerticesCache.Length < maxVertices)
		{
			chunk.VerticesCache = new Vector3[maxVertices];
			chunk.NormalsCache = new Vector3[maxVertices];
			chunk.UvsCache = new Vector2[maxVertices];
			chunk.ColorsCache = new Color[maxVertices];
			chunk.TexIndicesCache = new float[maxVertices * 4];
			chunk.TexWeightsCache01 = new float[maxVertices * 4];
			chunk.CliffIndicesCache = new float[maxVertices * 4];
			chunk.CliffWeightsCache = new float[maxVertices * 4];
		}

		if (chunk.IndicesCache == null || chunk.IndicesCache.Length < maxIndices)
		{
			chunk.IndicesCache = new int[maxIndices];
		}
	}

	private void ProcessChunkQuads(TerrainChunk chunk, TerrainCell[,] cells, int w, int d, float quadSize, float halfWQuadSize, float halfDQuadSize, TerrainSplatWeights[,] splatMap, TerrainSplatWeights[,] cliffSplatMap, ref int vertexIndex, ref int indexIndex)
	{
		for (int z = chunk.StartZ; z < chunk.EndZ; z++)
		{
			for (int x = chunk.StartX; x < chunk.EndX; x++)
			{
				ProcessCellQuad(chunk, x, z, cells, w, d, quadSize, halfWQuadSize, halfDQuadSize, splatMap, cliffSplatMap, ref vertexIndex, ref indexIndex);
				ProcessCellPerimeterSkirt(chunk, x, z, cells, w, d, quadSize, halfWQuadSize, halfDQuadSize, cliffSplatMap, ref vertexIndex, ref indexIndex);
			}
		}
	}

	private void ProcessCellPerimeterSkirt(
		TerrainChunk chunk,
		int x, int z,
		TerrainCell[,] cells,
		int w, int d,
		float quadSize,
		float halfWQuadSize,
		float halfDQuadSize,
		TerrainSplatWeights[,] cliffSplatMap,
		ref int vertexIndex,
		ref int indexIndex)
	{
		if (x != 0 && z != 0 && x != w - 1 && z != d - 1) return;

		var cell = cells[x, z];
		var (cliffS00, cliffS10, cliffS11, cliffS01, _) = GetSplatMapQuad(x, z, cliffSplatMap);
		var (cliffTex0, cliffTex1, cliffTex2, cliffTex3) = GetQuadDominantTextures(cliffS00, cliffS10, cliffS11, cliffS01);

		if (z == 0)
		{
			AddPerimeterSkirtWall(chunk,
				GetWorldPosition(x, z, cell.Y_NW, halfWQuadSize, halfDQuadSize, quadSize),
				GetWorldPosition(x + 1, z, cell.Y_NE, halfWQuadSize, halfDQuadSize, quadSize),
				Vector3.Forward, cliffS00, cliffS10,
				cliffTex0, cliffTex1, cliffTex2, cliffTex3, ref vertexIndex, ref indexIndex);
		}

		if (z == d - 1)
		{
			AddPerimeterSkirtWall(chunk,
				GetWorldPosition(x + 1, z + 1, cell.Y_SE, halfWQuadSize, halfDQuadSize, quadSize),
				GetWorldPosition(x, z + 1, cell.Y_SW, halfWQuadSize, halfDQuadSize, quadSize),
				Vector3.Back, cliffS01, cliffS11,
				cliffTex0, cliffTex1, cliffTex2, cliffTex3, ref vertexIndex, ref indexIndex);
		}

		if (x == 0)
		{
			AddPerimeterSkirtWall(chunk,
				GetWorldPosition(x, z + 1, cell.Y_SW, halfWQuadSize, halfDQuadSize, quadSize),
				GetWorldPosition(x, z, cell.Y_NW, halfWQuadSize, halfDQuadSize, quadSize),
				Vector3.Left, cliffS01, cliffS00,
				cliffTex0, cliffTex1, cliffTex2, cliffTex3, ref vertexIndex, ref indexIndex);
		}

		if (x == w - 1)
		{
			AddPerimeterSkirtWall(chunk,
				GetWorldPosition(x + 1, z, cell.Y_NE, halfWQuadSize, halfDQuadSize, quadSize),
				GetWorldPosition(x + 1, z + 1, cell.Y_SE, halfWQuadSize, halfDQuadSize, quadSize),
				Vector3.Right, cliffS10, cliffS11,
				cliffTex0, cliffTex1, cliffTex2, cliffTex3, ref vertexIndex, ref indexIndex);
		}
	}

	private void AddPerimeterSkirtWall(
		TerrainChunk chunk,
		Vector3 topSource,
		Vector3 topDestination,
		Vector3 outwardNormal,
		TerrainSplatWeights splatSource,
		TerrainSplatWeights splatDestination,
		int cliffTex0, int cliffTex1, int cliffTex2, int cliffTex3,
		ref int vertexIndex,
		ref int indexIndex)
	{
		int baseIndex = vertexIndex;
		float bottomY = _perimeterSkirtBottomY;
		Vector3 bottomSource = new Vector3(topSource.X, bottomY, topSource.Z);
		Vector3 bottomDestination = new Vector3(topDestination.X, bottomY, topDestination.Z);

		PopulatePerimeterSkirtVertex(chunk, topSource, outwardNormal, splatSource, cliffTex0, cliffTex1, cliffTex2, cliffTex3, ref vertexIndex);
		PopulatePerimeterSkirtVertex(chunk, bottomSource, outwardNormal, splatSource, cliffTex0, cliffTex1, cliffTex2, cliffTex3, ref vertexIndex);
		PopulatePerimeterSkirtVertex(chunk, bottomDestination, outwardNormal, splatDestination, cliffTex0, cliffTex1, cliffTex2, cliffTex3, ref vertexIndex);
		PopulatePerimeterSkirtVertex(chunk, topDestination, outwardNormal, splatDestination, cliffTex0, cliffTex1, cliffTex2, cliffTex3, ref vertexIndex);

		chunk.IndicesCache[indexIndex++] = baseIndex;
		chunk.IndicesCache[indexIndex++] = baseIndex + 1;
		chunk.IndicesCache[indexIndex++] = baseIndex + 2;
		chunk.IndicesCache[indexIndex++] = baseIndex;
		chunk.IndicesCache[indexIndex++] = baseIndex + 2;
		chunk.IndicesCache[indexIndex++] = baseIndex + 3;
	}

	private void PopulatePerimeterSkirtVertex(
		TerrainChunk chunk,
		Vector3 position,
		Vector3 normal,
		TerrainSplatWeights splat,
		int cliffTex0, int cliffTex1, int cliffTex2, int cliffTex3,
		ref int vertexIndex)
	{
		var (weight0, weight1, weight2, weight3) = GetSplatWeightsForQuad(splat, cliffTex0, cliffTex1, cliffTex2, cliffTex3);

		chunk.VerticesCache[vertexIndex] = position;
		chunk.NormalsCache[vertexIndex] = normal;
		chunk.UvsCache[vertexIndex] = new Vector2(position.X, position.Z);
		chunk.ColorsCache[vertexIndex] = new Color(1.0f, 1.0f, 1.0f, 0.0f);

		int splatIndex = vertexIndex * 4;
		chunk.TexIndicesCache[splatIndex + 0] = cliffTex0;
		chunk.TexIndicesCache[splatIndex + 1] = cliffTex1;
		chunk.TexIndicesCache[splatIndex + 2] = cliffTex2;
		chunk.TexIndicesCache[splatIndex + 3] = cliffTex3;

		chunk.TexWeightsCache01[splatIndex + 0] = weight0;
		chunk.TexWeightsCache01[splatIndex + 1] = weight1;
		chunk.TexWeightsCache01[splatIndex + 2] = weight2;
		chunk.TexWeightsCache01[splatIndex + 3] = weight3;

		chunk.CliffIndicesCache[splatIndex + 0] = cliffTex0;
		chunk.CliffIndicesCache[splatIndex + 1] = cliffTex1;
		chunk.CliffIndicesCache[splatIndex + 2] = cliffTex2;
		chunk.CliffIndicesCache[splatIndex + 3] = cliffTex3;

		chunk.CliffWeightsCache[splatIndex + 0] = weight0;
		chunk.CliffWeightsCache[splatIndex + 1] = weight1;
		chunk.CliffWeightsCache[splatIndex + 2] = weight2;
		chunk.CliffWeightsCache[splatIndex + 3] = weight3;

		vertexIndex++;
	}

	private void BuildMeshArraysFromCache(TerrainChunk chunk, int vertexIndex, int indexIndex)
	{
		Vector3[] finalVertices = chunk.VerticesCache;
		Color[] finalColors = chunk.ColorsCache;
		Vector3[] finalNormals = chunk.NormalsCache;
		Vector2[] finalUvs = chunk.UvsCache;
		float[] finalTexIndices = chunk.TexIndicesCache;
		float[] finalTexWeights = chunk.TexWeightsCache01;
		float[] finalCliffIndices = chunk.CliffIndicesCache;
		float[] finalCliffWeights = chunk.CliffWeightsCache;
		int[] finalIndices = chunk.IndicesCache;

		if (vertexIndex < chunk.VerticesCache.Length)
		{
			finalVertices = new Vector3[vertexIndex];
			System.Array.Copy(chunk.VerticesCache, finalVertices, vertexIndex);
			finalColors = new Color[vertexIndex];
			System.Array.Copy(chunk.ColorsCache, finalColors, vertexIndex);
			finalNormals = new Vector3[vertexIndex];
			System.Array.Copy(chunk.NormalsCache, finalNormals, vertexIndex);
			finalUvs = new Vector2[vertexIndex];
			System.Array.Copy(chunk.UvsCache, finalUvs, vertexIndex);
			finalTexIndices = new float[vertexIndex * 4];
			System.Array.Copy(chunk.TexIndicesCache, finalTexIndices, vertexIndex * 4);
			finalTexWeights = new float[vertexIndex * 4];
			System.Array.Copy(chunk.TexWeightsCache01, finalTexWeights, vertexIndex * 4);
			finalCliffIndices = new float[vertexIndex * 4];
			System.Array.Copy(chunk.CliffIndicesCache, finalCliffIndices, vertexIndex * 4);
			finalCliffWeights = new float[vertexIndex * 4];
			System.Array.Copy(chunk.CliffWeightsCache, finalCliffWeights, vertexIndex * 4);
		}

		if (indexIndex < chunk.IndicesCache.Length)
		{
			finalIndices = new int[indexIndex];
			System.Array.Copy(chunk.IndicesCache, finalIndices, indexIndex);
		}

		var arrays = new Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = finalVertices;
		arrays[(int)Mesh.ArrayType.Color] = finalColors;
		arrays[(int)Mesh.ArrayType.Normal] = finalNormals;
		arrays[(int)Mesh.ArrayType.TexUV] = finalUvs;
		arrays[(int)Mesh.ArrayType.Custom0] = finalTexIndices;
		arrays[(int)Mesh.ArrayType.Custom1] = finalTexWeights;
		arrays[(int)Mesh.ArrayType.Custom2] = finalCliffIndices;
		arrays[(int)Mesh.ArrayType.Custom3] = finalCliffWeights;
		arrays[(int)Mesh.ArrayType.Index] = finalIndices;

		chunk.ArrayMesh.ClearSurfaces();
		if (vertexIndex > 0)
		{
			int custom0Format = (int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift;
			int custom1Format = (int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift;
			int custom2Format = (int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom2Shift;
			int custom3Format = (int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom3Shift;
			chunk.ArrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays,
				new Array<Array>(),
				null,
				(Mesh.ArrayFormat)((int)(Mesh.ArrayFormat.FormatCustom0 | Mesh.ArrayFormat.FormatCustom1 | Mesh.ArrayFormat.FormatCustom2 | Mesh.ArrayFormat.FormatCustom3) | custom0Format | custom1Format | custom2Format | custom3Format));
		}
	}

	private void UpdateChunkBoundsAndPhysics(TerrainChunk chunk, int w, int d, float quadSize, int vertexIndex, bool rebuildPhysics)
	{
		float minY = float.MaxValue;
		float maxY = float.MinValue;

		for (int i = 0; i < vertexIndex; i++)
		{
			float y = chunk.VerticesCache[i].Y;
			if (y < minY) minY = y;
			if (y > maxY) maxY = y;
		}

		if (vertexIndex == 0)
		{
			minY = -2f;
			maxY = 2f;
		}

		chunk.MinY = minY;
		chunk.MaxY = maxY;

		float halfW = (w / 2.0f) * quadSize;
		float halfD = (d / 2.0f) * quadSize;
		float minX = chunk.StartX * quadSize - halfW;
		float maxX = chunk.EndX * quadSize - halfW;
		float minZ = chunk.StartZ * quadSize - halfD;
		float maxZ = chunk.EndZ * quadSize - halfD;

		chunk.WorldAabb = new Aabb(new Vector3(minX, minY - 2f, minZ), new Vector3(maxX - minX, Math.Max(0.5f, maxY - minY + 10f), maxZ - minZ));
		chunk.MeshInstance.CustomAabb = chunk.WorldAabb;

		if (chunk.ProfileWaterMeshes != null)
		{
			foreach (var (pMesh, _) in chunk.ProfileWaterMeshes.Values)
			{
				if (GodotObject.IsInstanceValid(pMesh)) pMesh.CustomAabb = chunk.WorldAabb;
			}
		}

	}

	private void ClearChunkCaches(TerrainChunk chunk)
	{
		chunk.VerticesCache = System.Array.Empty<Vector3>();
		chunk.NormalsCache = System.Array.Empty<Vector3>();
		chunk.ColorsCache = System.Array.Empty<Color>();
		chunk.TexIndicesCache = System.Array.Empty<float>();
		chunk.TexWeightsCache01 = System.Array.Empty<float>();
		chunk.CliffIndicesCache = System.Array.Empty<float>();
		chunk.CliffWeightsCache = System.Array.Empty<float>();
		chunk.UvsCache = System.Array.Empty<Vector2>();
		chunk.IndicesCache = System.Array.Empty<int>();
		chunk.MapDataCache = System.Array.Empty<float>();
		chunk.WeldVertexMap = new();
		chunk.WeldRemapTable = System.Array.Empty<int>();
	}

	public override void _Process(double delta)
	{
		UpdateFrustumCulling();
	}

	private Vector3 _lastFrustumCamPos;
	private Vector3 _lastFrustumCamRot;
	private readonly Plane[] _cachedFrustumPlanes = new Plane[6];

	protected void UpdateFrustumCulling()
	{
		if (IsMinimapRendering)
		{
			SetAllChunksVisible(true);
			return;
		}

		var viewport = GetViewport();
		var camera = viewport?.GetCamera3D();
		if (camera == null || !GodotObject.IsInstanceValid(camera)) return;

		if (camera.Projection == Camera3D.ProjectionType.Orthogonal)
		{
			SetAllChunksVisible(true);
			return;
		}

		Vector3 camPos = camera.GlobalPosition;
		Vector3 camRot = camera.GlobalRotation;
		if ((camPos - _lastFrustumCamPos).LengthSquared() < 0.0001f && (camRot - _lastFrustumCamRot).LengthSquared() < 0.0001f)
		{
			return;
		}

		_lastFrustumCamPos = camPos;
		_lastFrustumCamRot = camRot;

		UpdateVisibilityFromFrustum(camera);
	}

	private void UpdateVisibilityFromFrustum(Camera3D camera)
	{
		var frustum = camera.GetFrustum();
		if (frustum == null || frustum.Count < 6) return;

		for (int i = 0; i < 6; i++)
		{
			_cachedFrustumPlanes[i] = frustum[i];
		}

		ReadOnlySpan<Plane> planesSpan = _cachedFrustumPlanes;
		foreach (var chunk in _chunks)
		{
			bool visible = IntersectsFrustum(planesSpan, chunk.WorldAabb);
			UpdateChunkVisibility(chunk, visible);
		}
	}

	private void UpdateChunkVisibility(TerrainChunk chunk, bool visible)
	{
		if (GodotObject.IsInstanceValid(chunk.MeshInstance) && chunk.MeshInstance.Visible != visible)
		{
			chunk.MeshInstance.Visible = visible;
		}

		if (chunk.ProfileWaterMeshes != null)
		{
			foreach (var (pMesh, _) in chunk.ProfileWaterMeshes.Values)
			{
				if (GodotObject.IsInstanceValid(pMesh) && pMesh.Visible != visible)
				{
					pMesh.Visible = visible;
				}
			}
		}
	}

}
