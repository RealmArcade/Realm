using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Tags;

/// <summary>
///     Tag indicating the entity belongs to a player but takes no orders from that player, because the map script drives it.
/// </summary>
[TagDefinition("PlayerOrdersLocked", "Player Orders Locked", "Marks a player-owned unit the map drives itself, so the player cannot order or cast from it while the script still can.")]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct PlayerOrdersLocked;
