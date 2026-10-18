using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Abilities;

/// <summary>
///     Defines the core properties of an ability.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct Ability(float Cooldown, float CurrentCooldown = 0);