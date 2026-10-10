namespace Realm.Ecs.Components.Core;

/// <summary>
///     Represents the health and health regeneration state of an entity.
/// </summary>
public record struct Health(
	float Current,
	float Max,
	float HpRegen = 0f,
	float HpRegenCombatDelay = 0f,
	float TimeSinceLastDamage = 0f);
