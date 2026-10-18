namespace Realm.MapAPI;

/// <summary>
/// Defines splash area damage falloff options for combat attacks.
/// </summary>
public enum SplashType
{
	/// <summary>No area-of-effect splash damage.</summary>
	None = 0,
	/// <summary>Discrete stepped radial damage falloff.</summary>
	RadialStep = 1,
	/// <summary>Smooth linear radial damage falloff.</summary>
	RadialLinear = 2
}
