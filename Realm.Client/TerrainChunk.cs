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
	public Vector3[] VerticesCache = null!;
	public Color[] ColorsCache = null!;
	public float[] TexIndicesCache = null!;
	public float[] TexWeightsCache01 = null!;
	public float[] CliffIndicesCache = null!;
	public float[] CliffWeightsCache = null!;
	public Vector3[] NormalsCache = null!;
	public Vector2[] UvsCache = null!;
	public int[] IndicesCache = null!;
	public float[] MapDataCache = null!;
	public Dictionary<TerrainVertexKey, int> WeldVertexMap = null!;
	public int[] WeldRemapTable = null!;
	public float MinY;
	public float MaxY;
	public Aabb WorldAabb;
}