using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Core;

/// <summary>
///     Tracks the cooldown timer for under-attack UI alert notifications, stored on the world entity
///     so both the ECS service and the GameHost orchestrator can read and update it without coupling.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct CombatAlertState(float UnderAttackAlertTimer);