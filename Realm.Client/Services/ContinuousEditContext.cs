using Godot;
using Realm.Client.Core;

namespace Realm.Client.Services;

public struct ContinuousEditContext
{
	public Vector3 WorldPos;
	public float Delta;
	public EditorTool ActiveTool;
	public float BrushRadius;
	public float BrushStrength;
	public bool BrushIsSquare;
	public int PaintTextureIndex;
	public int CliffPaintTextureIndex;
	public int PathingMask;
	public bool PathingAdd;
	public bool ApplyGroundTexture;
	public bool ApplyCliffTexture;
	public int Width;
	public int Depth;
	public float QuadSize;
	public int ModMinX;
	public int ModMinZ;
	public int ModMaxX;
	public int ModMaxZ;
	public long NowMs;
	public bool IsHeights;
	public bool Modified;
	public bool PathingModified;
}