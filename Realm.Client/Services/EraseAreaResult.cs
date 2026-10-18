using System.Collections.Generic;
using Godot;

namespace Realm.Client.Services;

public struct EraseAreaResult
{
	public bool TerrainModified;
	public bool HeightsModified;
	public bool PathingModified;
	public List<Node3D> NodesToDelete;
}