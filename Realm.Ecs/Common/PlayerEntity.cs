using System.Runtime.InteropServices;

namespace Realm.Ecs.Common;

/// <summary>
///     A type-safe wrapper for an Entity that is guaranteed to represent a player.
///     This enforces compile-time type safety for player references.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct PlayerEntity
{
	public PlayerEntity(Entity value)
	{
		Value = value;
	}

	public Entity Value { get; }
}
