using Godot;

namespace Realm.Client.Utils;

public struct MaterialParameters
{
	public Texture2D RawAlbedoTexture;
	public Texture2D OrmTexture;
	public Texture2D NormalTexture;
	public Texture2D EmissionTexture;
	public Color AlbedoColor;
	public Color EmissionColor;
	public float EmissionEnergy;
	public float Roughness;
	public float Metallic;
	public float Specular;
	public Vector3 Uv1Scale;
	public Vector3 Uv1Offset;
	public bool UseAlphaBlend;
	public bool UseAlphaScissor;
	public float AlphaScissorThreshold;

	public static MaterialParameters Default => new MaterialParameters
	{
		AlbedoColor = new Color(1f, 1f, 1f, 1f),
		EmissionColor = new Color(0f, 0f, 0f, 1f),
		EmissionEnergy = 1f,
		Roughness = 1f,
		Metallic = 0f,
		Specular = 0.5f,
		Uv1Scale = Vector3.One,
		Uv1Offset = Vector3.Zero,
		AlphaScissorThreshold = 0.5f
	};
}