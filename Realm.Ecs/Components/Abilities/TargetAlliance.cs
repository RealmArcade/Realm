namespace Realm.Ecs.Components.Abilities;

/// <summary>
///     Specifies the valid targets for an ability.
/// </summary>
[Flags]
public enum TargetAlliance
{
	None = 0,
	Self = 1 << 0,
	Ally = 1 << 1,
	Neutral = 1 << 2,
	Enemy = 1 << 3,
	All = Self | Ally | Neutral | Enemy
}