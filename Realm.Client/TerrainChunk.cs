using System;
using System.Collections.Generic;
using Godot;

namespace Realm.Client;

public class TerrainChunk
{
	public MeshInstance3D MeshInstance = null!;
	public ArrayMesh ArrayMesh = null!;
	public Dictionary<byte, (MeshInstance3D MeshInstance, ArrayMesh ArrayMesh)> ProfileWaterMeshes = new();
	public int StartX;
	public int StartZ;
	public int EndX;
	public int EndZ;
	// The caches start empty and are sized by the mesh builder, so clearing a chunk releases the
	// arrays without ever leaving a null the writer paths would have to check.
	public Vector3[] VerticesCache = Array.Empty<Vector3>();
	public Color[] ColorsCache = Array.Empty<Color>();
	public float[] TexIndicesCache = Array.Empty<float>();
	public float[] TexWeightsCache01 = Array.Empty<float>();
	public float[] CliffIndicesCache = Array.Empty<float>();
	public float[] CliffWeightsCache = Array.Empty<float>();
	public Vector3[] NormalsCache = Array.Empty<Vector3>();
	public Vector2[] UvsCache = Array.Empty<Vector2>();
	public int[] IndicesCache = Array.Empty<int>();
	public float[] MapDataCache = Array.Empty<float>();
	public Dictionary<TerrainVertexKey, int> WeldVertexMap = new();
	public int[] WeldRemapTable = Array.Empty<int>();
	public float MinY;
	public float MaxY;
	public Aabb WorldAabb;
}