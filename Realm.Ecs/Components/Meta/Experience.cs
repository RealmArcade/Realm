using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Meta;

/// <summary>
/// Represents the experience points of an entity, such as a hero.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct Experience(float Value);