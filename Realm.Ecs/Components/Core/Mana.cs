namespace Realm.Ecs.Components.Core;

/// <summary>
///     Represents the current, maximum, and regeneration rate of mana for an entity.
/// </summary>
internal record struct Mana(
	float Current,
	float Max,
	float ManaRegen = 0f);
