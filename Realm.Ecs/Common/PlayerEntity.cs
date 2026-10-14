using Arch.Core;

namespace Realm.Ecs.Common;

/// <summary>
///     A type-safe wrapper for an Entity that is guaranteed to represent a player.
///     This enforces compile-time type safety for player references.
/// </summary>
public readonly record struct PlayerEntity
{
	public PlayerEntity(Entity value)
	{
		Value = value;
	}

	public Entity Value { get; }
}