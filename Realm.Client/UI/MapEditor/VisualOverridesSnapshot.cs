using Godot;

namespace Realm.Client.UI.MapEditor;

public struct VisualOverridesSnapshot
{
	public string ModelPath;
	public string VisualMode;
	public float Scale;
	public float YOffset;
	public float CollisionCircleRatio;
	public float Brightness;
	public Color ColorTint;
	public bool NormalizeLuminance;
	public bool IgnorePlayerColor;
	public bool DespillPlayerColor;
	public string SpawnShader;
	public string DeathShader;
	public bool EnableProceduralAnimation;
	public string ProceduralAnimation;
}