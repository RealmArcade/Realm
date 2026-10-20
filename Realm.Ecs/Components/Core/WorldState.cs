using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Core;

/// <summary>
///     Holds global world simulation state such as elapsed game time and day-night cycle parameters.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct WorldState
{
	public float GameElapsedTime;
	public int TimeOfDayIndex;
	public float TimeOfDayTimer;
	public byte DayNightCycleEnabledByte;

	public WorldState(float gameElapsedTime, int timeOfDayIndex, float timeOfDayTimer, bool dayNightCycleEnabled)
	{
		GameElapsedTime = gameElapsedTime;
		TimeOfDayIndex = timeOfDayIndex;
		TimeOfDayTimer = timeOfDayTimer;
		DayNightCycleEnabledByte = dayNightCycleEnabled ? (byte)1 : (byte)0;
	}

	public bool DayNightCycleEnabled
	{
		get => DayNightCycleEnabledByte != 0;
		set => DayNightCycleEnabledByte = value ? (byte)1 : (byte)0;
	}

	public void Deconstruct(out float gameElapsedTime, out int timeOfDayIndex, out float timeOfDayTimer, out bool dayNightCycleEnabled)
	{
		gameElapsedTime = GameElapsedTime;
		timeOfDayIndex = TimeOfDayIndex;
		timeOfDayTimer = TimeOfDayTimer;
		dayNightCycleEnabled = DayNightCycleEnabled;
	}
}