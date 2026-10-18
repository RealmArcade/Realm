using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Core;

/// <summary>
///     Tracks the current and maximum population limit for a player.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct PlayerPopulation(int Current, int Max);