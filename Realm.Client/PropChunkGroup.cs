using System.Collections.Generic;
using Godot;

namespace Realm.Client;

public class PropChunkGroup
{
	public Vector2I ChunkIndex;
	public Aabb Bounds;
	public List<MultiMeshInstance3D> MultiMeshNodes = new();
	public int LastInstanceCount = -1;
	public ulong LastDataHash = 0;
}