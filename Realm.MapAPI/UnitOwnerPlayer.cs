using System.Runtime.InteropServices;

namespace Realm.MapAPI;

/// <summary>
/// Represents the zero-based player slot index (0-24) owning a unit or building entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct UnitOwnerPlayer
{
	/// <summary>The zero-based player slot index.</summary>
	public int PlayerIndex;

	/// <summary>
	/// Initializes a new instance of the <see cref="UnitOwnerPlayer"/> struct.
	/// </summary>
	/// <param name="playerIndex">The zero-based player slot index.</param>
	public UnitOwnerPlayer(int playerIndex)
	{
		PlayerIndex = playerIndex;
	}

	/// <summary>
	/// Implicitly converts an integer player index into a <see cref="UnitOwnerPlayer"/> struct.
	/// </summary>
	public static implicit operator UnitOwnerPlayer(int playerIndex) => new(playerIndex);

	/// <summary>
	/// Implicitly converts a <see cref="UnitOwnerPlayer"/> struct into an integer player index.
	/// </summary>
	public static implicit operator int(UnitOwnerPlayer owner) => owner.PlayerIndex;
}
