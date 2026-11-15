using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("One change an upgrade, item or ability makes to a unit's stats, applied flat, as a percentage or through a script function.")]
public class StatModifierDefinition
{
	[SchemaDescription("The stat the modifier changes, named the way the engine's stat table names it.")]
	[SchemaSensitive]
	public string StatKey { get; set; } = string.Empty;

	[SchemaDescription("How the value is applied: added flat, added as a percentage of the base, or set outright.")]
	public string Type { get; set; } = "Flat";

	[SchemaDescription("How much the modifier changes the stat by.")]
	public float Value { get; set; }

	[SchemaDescription("The map script function that computes the modifier's value instead of a fixed number, called once per affected unit.")]
	[SchemaSensitive]
	public string WASMFunctionName { get; set; } = string.Empty;

	[SchemaDescription("How much the modifier grows for every level the upgrade has been researched.")]
	public float PerLevelScaling { get; set; }
}
