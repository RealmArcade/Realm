using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Meta;

/// <summary>
/// Represents the level of an entity, such as a hero.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct Level(int Value);