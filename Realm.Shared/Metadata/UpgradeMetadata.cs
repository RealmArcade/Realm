using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("One researchable upgrade: what it costs, how long it takes, which units it applies to and what it changes about them.")]
public class UpgradeMetadata
{
	[SchemaDescription("The identifier unit templates and requirement expressions name this upgrade by.")]
	[SchemaSensitive]
	public string TemplateID { get; set; } = string.Empty;

	[SchemaDescription("The name the upgrade shows on its command card.")]
	public string Name { get; set; } = string.Empty;

	[SchemaDescription("The tooltip text explaining what the upgrade changes and what it costs.")]
	[SchemaControl(RealmSchemaControl.Textarea)]
	public string Description { get; set; } = string.Empty;

	[SchemaDescription("Path to the icon shown for the upgrade on its command card.")]
	[SchemaAsset(new[] { "rtex" }, new[] { "Assets/icons" })]
	public string? IconPath { get; set; }

	[SchemaDescription("The keyboard key that starts researching the upgrade, as one character.")]
	[SchemaSensitive]
	public string? Hotkey { get; set; }

	[SchemaDescription("Gold the upgrade costs to research.")]
	public float CostGold { get; set; }

	[SchemaDescription("Wood the upgrade costs to research.")]
	public float CostWood { get; set; }

	[SchemaDescription("Stone the upgrade costs to research.")]
	public float CostStone { get; set; }

	[SchemaDescription("How long the researching building takes to finish the upgrade.")]
	[SchemaUnit("s")]
	public float ResearchTime { get; set; }

	[SchemaDescription("The requirement expression that has to hold before the upgrade may be researched.")]
	public string Requirement { get; set; } = string.Empty;

	[SchemaDescription("How many times the upgrade may be researched, each level stacking its modifiers again.")]
	public int MaxLevel { get; set; } = 1;

	/// <summary>
	///     The unit templates this upgrade applies to. Null or empty means the player's whole army, now and for units
	///     fielded later; "*" says the same thing explicitly.
	/// </summary>
	[SchemaDescription("The unit templates the upgrade applies to. Absent or empty means the player's whole army, now and for units fielded later; a single star says the same thing explicitly.")]
	[SchemaReference("Units")]
	public string[]? AffectedUnitIds { get; set; }

	[SchemaDescription("What the upgrade changes about the units it applies to, one stat at a time.")]
	public List<StatModifierDefinition> Modifiers { get; set; } = new();

	[SchemaDescription("The map the upgrade was imported from, kept so attribution survives a round trip through the editor.")]
	public string? Attribution_ImportedFromMap { get; set; }
}
