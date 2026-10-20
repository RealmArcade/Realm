using Godot;

namespace Realm.Client.UI.MapEditor;

public struct DecalNodeState
{
	public Color Modulate;
	public float AlbedoMix;
	public Texture2D TextureNormal;
	public Texture2D TextureOrm;
	public Texture2D TextureEmission;
	public float EmissionEnergy;
	public Vector3 Size;
	public float UpperFade;
	public float LowerFade;
}