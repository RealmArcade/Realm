using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("One spawn shader: the materialising effect played where a placeable appears or dies, from where it starts to how long it takes.")]
public class SpawnShaderMetadata
{
	[SchemaDescription("The name the spawn shader shows in the editor.")]
	public string Name { get; set; } = string.Empty;

	[SchemaDescription("How the shader reveals the model, such as rising from the ground or dissolving into it.")]
	public int TransitionMode { get; set; }

	[SchemaDescription("Which way the reveal travels across the model, such as upwards, downwards or outwards.")]
	public int Direction { get; set; }

	[SchemaDescription("The colour of the glowing edge of the reveal.")]
	[SchemaControl(RealmSchemaControl.Color)]
	public string EdgeColor { get; set; } = "#ff661aff";

	[SchemaDescription("How wide the glowing edge of the reveal is.")]
	[SchemaUnit("m")]
	public float EdgeWidth { get; set; } = 0.06f;

	[SchemaDescription("How strongly the edge of the reveal glows.")]
	public float EdgeEmission { get; set; } = 5.0f;

	[SchemaDescription("The size of the noise the reveal is broken up with.")]
	public float NoiseScale { get; set; } = 12.0f;

	[SchemaDescription("How harsh the noise the reveal is broken up with reads.")]
	public float NoiseRoughness { get; set; } = 0.5f;

	[SchemaDescription("How tightly the model's rim glow hugs its silhouette while it materialises.")]
	public float FresnelPower { get; set; } = 2.5f;

	[SchemaDescription("How far the model's vertices are pushed apart while it materialises.")]
	[SchemaUnit("m")]
	public float VertexDisplacement { get; set; } = 0.0f;

	[SchemaDescription("How much the model fades as it appears. One fades from invisible; zero never fades.")]
	[SchemaUnit("%")]
	[SchemaRange(0, 1)]
	public float AlphaFade { get; set; } = 1.0f;

	[SchemaDescription("How long the reveal takes from start to finish.")]
	[SchemaUnit("s")]
	public float Duration { get; set; } = 1.2f;

	[SchemaDescription("The asset category the spawn shader is filed under, which decides where the editor lists it.")]
	public string AssetType { get; set; } = "SpawnShader";

	[SchemaDescription("The map the spawn shader was imported from, kept so attribution survives a round trip through the editor.")]
	public string? Attribution_ImportedFromMap { get; set; }
}
