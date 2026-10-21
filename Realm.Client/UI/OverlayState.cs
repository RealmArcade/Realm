using System.Collections.Generic;
using Godot;
using Realm.Client.Core;

namespace Realm.Client.UI;

public class OverlayState
{
	public bool WasBrushVisible;
	public GridOverlayMode WasGridMode;
	public bool WasPathingVisible;
	public MeshInstance3D PathingMesh;
	public bool WasPathingMeshVisible;
	public List<(Unit3D unit, bool visible)> UnitVisibility = new();
}