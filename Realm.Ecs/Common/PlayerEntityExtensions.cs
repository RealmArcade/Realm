using Arch.Core;
using Realm.Ecs.Components.Tags;

namespace Realm.Ecs.Common;

public static class PlayerEntityExtensions
{
	/// <summary>
	///     Creates a type-safe PlayerEntity wrapper from a raw Entity.
	///     Performs a runtime check to ensure the Entity has the 'Player' tag.
	///     This is the trusted way to construct a PlayerEntity.
	/// </summary>
	/// <param name="world">The Arch World instance.</param>
	/// <param name="entity">The raw Entity to wrap.</param>
	/// <returns>A PlayerEntity instance if the entity is a player.</returns>
	/// <exception cref="ArgumentException">Thrown if the entity does not have the 'Player' tag.</exception>
	public static PlayerEntity AsPlayerEntity(this Entity entity, World world)
	{
		if (entity != Entity.Null && !world.Has<Player>(entity))
			throw new ArgumentException($"Entity {entity.Id} does not represent a player (missing Player tag).");
		return new PlayerEntity(entity);
	}
}