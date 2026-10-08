namespace Realm.Ecs.Components.Combat;

/// <summary>
///     Defines targeting, detection, sight, and stealth intelligence attributes for an entity.
/// </summary>
internal record struct TargetIntel(
	float SightRange = 15f,
	float AcquisitionRange = 15f,
	string DetectionType = "normal",
	string StealthState = "none",
	string TargetAllowedTypes = "ground,air");
