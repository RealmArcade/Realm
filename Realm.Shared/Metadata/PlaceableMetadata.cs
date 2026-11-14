using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("A placeable template that is drawn on the map and carries the visual properties every unit, building, prop, resource and item model shares.")]
public abstract class PlaceableMetadata
{
	[SchemaDescription("The identifier every other template, order, build option and script uses to name this placeable.")]
	[SchemaSensitive]
	public string TemplateID { get; set; } = string.Empty;

	[SchemaDescription("The name the placeable shows on its command card, in its tooltip and in the editor.")]
	public string Name { get; set; } = string.Empty;

	[SchemaDescription("The tooltip text explaining what the placeable is and what it is for.")]
	[SchemaControl(RealmSchemaControl.Textarea)]
	public string? Description { get; set; }

	[SchemaDescription("Path to the model shown in the editor and on the battlefield.")]
	[SchemaAsset(new[] { "rmesh" }, new[] { "Assets/models/units", "Assets/models/buildings", "Assets/models/props" })]
	public string? ModelPath { get; set; }

	[SchemaDescription("Path to the model shown in the portrait panel while this placeable is selected.")]
	[SchemaAsset(new[] { "rmesh" }, new[] { "Assets/models/units", "Assets/models/buildings", "Assets/models/props" })]
	public string? PortraitModelPath { get; set; }

	[SchemaDescription("Path to the icon the command card shows for the button that offers this placeable, such as the hero a Build menu sells. A placeable without one shows the generic unit placeholder.")]
	[SchemaAsset(new[] { "rtex" }, new[] { "Assets/icons" })]
	public string? IconPath { get; set; }

	[SchemaDescription("How the model is laid out in the world, such as standing on the ground plane or lying flat as a decal.")]
	public string VisualMode { get; set; } = "GroundPlane";

	[SchemaDescription("The multiplier applied to the model's authored size when it is drawn on the battlefield.")]
	public virtual float Scale { get; set; } = 1.0f;

	[SchemaDescription("How far the model is lifted above the ground, in metres, so a mesh authored around its base sits correctly.")]
	[SchemaUnit("m")]
	public float YOffset { get; set; }

	[SchemaDescription("The radius of the footprint the placeable blocks with, in metres. Zero lets other units walk through it.")]
	[SchemaUnit("m")]
	public float CollisionCircle { get; set; }

	[SchemaDescription("How strongly the model brightens the terrain colour underneath it, from no effect to fully lit.")]
	public float Brightness { get; set; } = 0.5f;

	[SchemaDescription("The colour multiplied over the model's own colour, so one mesh can serve several teams.")]
	[SchemaControl(RealmSchemaControl.Color)]
	public string? Tint { get; set; }

	[SchemaDescription("Whether the model's overall brightness is normalised before it is tinted, so a dark skin and a light skin tint identically.")]
	public bool NormalizeLuminance { get; set; } = true;

	[SchemaDescription("Whether the player's colour is left out of the model entirely instead of being tinted over it.")]
	public bool IgnorePlayerColor { get; set; } = true;

	[SchemaDescription("Whether the player's colour is removed from the model's own pixels before team colour is applied, which stops a red skin tinting red twice.")]
	public bool DespillPlayerColor { get; set; } = false;

	[SchemaDescription("The pathing the placeable's footprint provides to units crossing it, declared as named bits.")]
	[SchemaFlag(1, "ShallowWater")]
	[SchemaFlag(2, "DeepWater")]
	[SchemaFlag(4, "Flying")]
	[SchemaFlag(8, "Ground")]
	[SchemaFlag(32, "Buildable")]
	public int PathingType { get; set; }

	[SchemaDescription("The spawn shader played where the placeable appears, such as the materialise effect a building site plays.")]
	[SchemaDictionaryReference("SpawnShader")]
	public string? SpawnShader { get; set; }

	[SchemaDescription("The shader played as the placeable dies or is removed.")]
	[SchemaDictionaryReference("SpawnShader")]
	public string? DeathShader { get; set; }

	[SchemaDescription("The procedural motion profile that makes the model sway, flutter or shake, referenced by name.")]
	[SchemaReference("CustomProceduralAnimations")]
	public string? ProceduralAnimation { get; set; }

	[SchemaDescription("Whether the procedural motion profile runs. Absent means it runs whenever one is named.")]
	public bool? EnableProceduralAnimation { get; set; }

	[SchemaDescription("Anchors the map adds to the model, which attachments and visual effects can be parented to by name.")]
	public List<PseudoSocketAnchor>? PseudoSockets { get; set; }

	[SchemaDescription("The shader played as the placeable is removed without dying, such as a building sold for its cost back.")]
	[SchemaDictionaryReference("SpawnShader")]
	public string? DespawnShader
	{
		get => DeathShader;
		set => DeathShader = value;
	}

	[SchemaDescription("The map the placeable was imported from, kept so attribution survives a round trip through the editor.")]
	public string? Attribution_ImportedFromMap { get; set; }
}
