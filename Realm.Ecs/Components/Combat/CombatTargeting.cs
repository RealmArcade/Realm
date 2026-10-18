using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Combat;

/// <summary>
///     Defines which target types an entity's attacks can affect.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct CombatTargeting(bool CanTargetAir, bool CanTargetGround);