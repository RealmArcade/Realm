using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Core;

/// <summary>
///     Mana points restored per second for an entity that has a <see cref="Mana" /> component.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct ManaRegen(float PerSecond);