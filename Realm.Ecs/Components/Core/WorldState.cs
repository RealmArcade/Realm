namespace Realm.Ecs.Components.Core;

/// <summary>
///     Holds global world simulation state such as elapsed game time and day-night cycle parameters.
/// </summary>
public record struct WorldState(float GameElapsedTime, int TimeOfDayIndex, float TimeOfDayTimer, bool DayNightCycleEnabled);
