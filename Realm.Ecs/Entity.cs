using System;
using System.Runtime.InteropServices;

namespace Realm.Ecs;

/// <summary>
/// Represents an entity handle in the ECS world.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct Entity(int Id) : IEquatable<Entity>
{
	public static readonly Entity Null = new Entity(-1);

	public bool IsNull => Id < 0;

	public static implicit operator int(Entity entity) => entity.Id;
	public static implicit operator Entity(int id) => new Entity(id);

	public override string ToString() => IsNull ? "Entity.Null" : $"Entity({Id})";
}
