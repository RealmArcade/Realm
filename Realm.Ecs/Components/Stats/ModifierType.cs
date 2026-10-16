namespace Realm.Ecs.Components.Stats;

/// <summary>
///     The type of modification to apply to a stat.
/// </summary>
public enum ModifierType
{
	Flat,
	PercentAdditive,
	Percentage = PercentAdditive,
	PercentMultiplicative,
	CustomFormula
}