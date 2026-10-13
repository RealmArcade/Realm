using Godot;

namespace Realm.Client.Core;

public class DecalAssetData
{
	public string DecalId = "";
	public string TexturePath = "";
	public Texture2D PrimaryTexture;
	public Texture2D? PrimaryNormal;
	public Texture2D[]? AlbedoFrames;
	public Texture2D[]? NormalFrames;
	public int Columns = 1;
	public int Rows = 1;
	public float Fps = 12.0f;
	public bool SubframeBlend = true;
	public bool IsAnimated => Columns > 1 || Rows > 1;
}