using Godot;
using Realm.Client.Services;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client.Core;

public class EditorSaveState
{
	public byte WaterProfileIndex;
	public WaterType WaterMode;
	public float WaterHeight;
	public bool BlockMode;
	public float BlockLevelHeight;
	public float ExactHeight;
	public int PaintTextureIndex;
	public int CliffPaintTextureIndex;
	public EditorTool ActiveTool;
	public string ActivePlaceId;
	public CopiedAreaTemplate CopiedArea;
	public Vector2I? SelectionStart;
	public Vector2I? SelectionEnd;
}