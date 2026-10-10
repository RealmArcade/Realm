namespace Realm.Shared.Metadata;

public class GlbItemMetadata
{
	public string? DefaultAssetType { get; set; }
	public float MinY { get; set; }
	public float YOffset { get; set; }
	public float Scale { get; set; } = 1.0f;
	public float CollisionCircleRatio { get; set; }
	public float CollisionRadius { get; set; }
	public float Brightness { get; set; } = 1.0f;
	public float Contrast { get; set; } = 1.0f;
	public float Saturation { get; set; } = 1.0f;
	public bool NormalizeLuminance { get; set; }
	public bool DespillPlayerColor { get; set; }
	public float RotX { get; set; }
	public float RotY { get; set; }
	public float RotZ { get; set; }
	public object? WeaponLayers { get; set; }
	public string? WeaponPreset { get; set; }
	public string? WeaponRibbon { get; set; }
	public bool IgnorePlayerColor { get; set; }
	public string? TeamColorMask { get; set; }
	public string? SpawnShader { get; set; }
	public string? DeathShader { get; set; }
	public string? DespawnShader
	{
		get => DeathShader;
		set => DeathShader = value;
	}
}