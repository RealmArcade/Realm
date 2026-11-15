using System.Collections.Generic;
using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("One ability a unit casts from its command card: what it costs, how far it reaches, what it summons or spawns, and what it does to the units it lands on.")]
public class AbilityMetadata
{
	[SchemaDescription("The identifier command cards, items and scripts use to name this ability.")]
	[SchemaSensitive]
	public string TemplateID { get; set; } = string.Empty;

	[SchemaDescription("The name the ability shows on its command card.")]
	public string Name { get; set; } = string.Empty;

	[SchemaDescription("The tooltip text explaining what the ability does and how much it costs.")]
	[SchemaControl(RealmSchemaControl.Textarea)]
	public string Description { get; set; } = string.Empty;

	[SchemaDescription("How the ability is grouped when the command card decides what a hero's spells are for, such as damage, control or support.")]
	public string? Role { get; set; }

	[SchemaDescription("How the ability is aimed, such as at a point, at a unit or at no target at all.")]
	public string AbilityType { get; set; } = "target_spell";

	[SchemaDescription("Path to the icon shown for the ability on its command card.")]
	[SchemaAsset(new[] { "rtex" }, new[] { "Assets/icons" })]
	public string? IconPath { get; set; }

	[SchemaDescription("Mana the caster spends to cast the ability.")]
	public float ManaCost { get; set; }

	[SchemaDescription("Gold the caster spends to cast the ability, for a script-defined spell that charges resources of its own.")]
	public float CostGold { get; set; }

	[SchemaDescription("Wood the caster spends to cast the ability, for a script-defined spell that charges resources of its own.")]
	public float CostWood { get; set; }

	[SchemaDescription("Stone the caster spends to cast the ability, for a script-defined spell that charges resources of its own.")]
	public float CostStone { get; set; }

	[SchemaDescription("How long the ability has to wait after a cast before it may be cast again.")]
	[SchemaUnit("s")]
	public float Cooldown { get; set; }

	[SchemaDescription("How far from the caster the ability may be aimed.")]
	[SchemaUnit("m")]
	public float TargetRange { get; set; }

	[SchemaDescription("The visual effect played where the ability is cast.")]
	[SchemaReference("Vfx")]
	public string? VisualEffect { get; set; }

	/// <summary>
	///     A weapon template this ability fires as its own visual, from the caster towards the point it was cast at. The
	///     engine flies it at the weapon's own speed and reports the landing through OnSpellImpact, so a map neither
	///     repeats a speed its weapon template already carries nor guesses how long the flight takes.
	/// </summary>
	[SchemaDescription("A weapon template the ability fires as its own visual, flown from the caster to the point it was cast at and reported back through onSpellImpact as an impact.")]
	[SchemaReference("Weapons")]
	public string? ProjectileId { get; set; }

	[SchemaDescription("The sound played when the ability is cast.")]
	public string? CastSound { get; set; }

	/// <summary>
	///     The template this card places on the board instead of casting at a point. Pressing it arms the standard
	///     construction placement - the coloured silhouette of the template that follows the cursor - and clicking
	///     finishes the order, so a map that raises a site needs no targeting cross-hair of its own.
	/// </summary>
	[SchemaDescription("The template this card places on the board instead of casting at a point, using the standard construction placement that follows the cursor.")]
	[SchemaReference("Units", "Buildings")]
	public string? PlacementTemplateId { get; set; }

	[SchemaDescription("The conditions under which a unit casts the ability on its own, without being ordered to.")]
	public AutoCastMetadata? AutoCast { get; set; }

	[SchemaDescription("The radius around the point the ability is aimed at inside which it lands.")]
	[SchemaUnit("m")]
	public float AreaOfEffectRadius { get; set; }

	[SchemaDescription("What the ability does, effect by effect: damage, healing, buffs, summons and their target shapes.")]
	public List<AbilityEffectMetadata>? Effects { get; set; }

	[SchemaDescription("The column of the command card the ability occupies, counted from the left. Minus one lets the card place it.")]
	public int GridX { get; set; } = -1;

	[SchemaDescription("The row of the command card the ability occupies, counted from the top. Minus one lets the card place it.")]
	public int GridY { get; set; } = -1;

	[SchemaDescription("The keyboard key that casts the ability, as one character.")]
	[SchemaSensitive]
	public string? Hotkey { get; set; }

	[SchemaDescription("The map the ability was imported from, kept so attribution survives a round trip through the editor.")]
	public string? Attribution_ImportedFromMap { get; set; }
}
