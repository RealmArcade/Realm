using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Core;

/// <summary>
///     Holds global world simulation state such as elapsed game time and day-night cycle parameters.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct WorldState(float GameElapsedTime, int TimeOfDayIndex, float TimeOfDayTimer, bool DayNightCycleEnabled);