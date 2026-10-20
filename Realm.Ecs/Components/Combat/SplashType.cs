namespace Realm.Ecs.Components.Combat;

/// <summary>
/// Defines area-of-effect splash damage distribution patterns.
/// </summary>
public enum SplashType : byte
{
	None = 0,
	RadialStep = 1,
	RadialLinear = 2
}
