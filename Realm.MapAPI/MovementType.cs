namespace Realm.MapAPI;

/// <summary>
/// Specifies the locomotion traversal medium for moving entities.
/// </summary>
public enum MovementType : byte
{
	/// <summary>Ground-based land locomotion.</summary>
	Ground = 0,
	/// <summary>Aerial flight traversal ignoring terrain height.</summary>
	Air = 1,
	/// <summary>Aquatic water surface or submerged traversal.</summary>
	Water = 2,
	/// <summary>Low-altitude hover traversal passing over minor terrain obstacles.</summary>
	Hover = 3
}
