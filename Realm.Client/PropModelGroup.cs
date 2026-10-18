using System.Collections.Generic;
using Godot;

namespace Realm.Client;

public class PropModelGroup
{
	public string AssetKey;
	public List<MeshSubInfo> SubMeshes = new();
	public Dictionary<Vector2I, PropChunkGroup> ChunkGroups = new();
}