namespace Realm.Shared.Metadata;


public abstract class PlaceableMetadata
{
	public string TemplateID { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string? Description { get; set; }
	public string? ModelPath { get; set; }
	public string? PortraitModelPath { get; set; }
	public string VisualMode { get; set; } = "GroundPlane";
	public virtual float Scale { get; set; } = 1.0f;
	public float YOffset { get; set; }
	public float CollisionCircle { get; set; }
	public float Brightness { get; set; } = 0.5f;
	public string? Tint { get; set; }
	public bool NormalizeLuminance { get; set; } = true;
	public bool IgnorePlayerColor { get; set; } = true;
	public bool DespillPlayerColor { get; set; } = false;
	public int PathingType { get; set; }
	public string? SpawnShader { get; set; }
	public string? DeathShader { get; set; }
	public string? ProceduralAnimation { get; set; }
	public bool? EnableProceduralAnimation { get; set; }
	public List<PseudoSocketAnchor>? PseudoSockets { get; set; }

	public string? DespawnShader
	{
		get => DeathShader;
		set => DeathShader = value;
	}

	public string? Attribution_ImportedFromMap { get; set; }
}
