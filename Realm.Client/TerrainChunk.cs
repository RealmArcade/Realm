using System.Collections.Generic;
using Godot;

namespace Realm.Client;

public class TerrainChunk
{
	public MeshInstance3D MeshInstance;
	public ArrayMesh ArrayMesh;
	public CollisionShape3D CollisionShape;
	public Dictionary<byte, (MeshInstance3D MeshInstance, ArrayMesh ArrayMesh)> ProfileWaterMeshes = new();
	public int StartX;
	public int StartZ;
	public int EndX;
	public int EndZ;
	public Vector3[] VerticesCache;
	public Color[] ColorsCache;
	public float[] TexIndicesCache;
	public float[] TexWeightsCache01;
	public float[] CliffIndicesCache;
	public float[] CliffWeightsCache;
	public Vector3[] NormalsCache;
	public Vector2[] UvsCache;
	public int[] IndicesCache;
	public float[] MapDataCache;
	public Dictionary<TerrainVertexKey, int> WeldVertexMap;
	public int[] WeldRemapTable;
	public float MinY;
	public float MaxY;
	public Aabb WorldAabb;
}