using System;
using System.Runtime.InteropServices;

namespace Realm.MapAPI;

/// <summary>
/// Defines the movement, locomotion, and crowd footprint properties of an entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct MovementStats
{
	/// <summary>Maximum linear traversal speed in world units per second.</summary>
	public float Speed;
	/// <summary>Acceleration rate in units per second squared.</summary>
	public float Acceleration;
	/// <summary>Rotational turning rate in radians per second.</summary>
	public float TurnRate;
	/// <summary>Crowd simulation push avoidance priority.</summary>
	public int PushPriority;
	/// <summary>Locomotion traversal medium.</summary>
	public MovementType Type;

	/// <summary>
	/// Initializes a new instance of the <see cref="MovementStats"/> struct.
	/// </summary>
	public MovementStats(
		float speed,
		float acceleration = 0f,
		float turnRate = 0f,
		int pushPriority = 0,
		Realm.MapAPI.MovementType movementType = Realm.MapAPI.MovementType.Ground)
	{
		Speed = speed;
		Acceleration = acceleration;
		TurnRate = turnRate;
		PushPriority = pushPriority;
		Type = movementType;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="MovementStats"/> struct with a string movement type.
	/// </summary>
	public MovementStats(
		float speed,
		float acceleration,
		float turnRate,
		int pushPriority,
		string movementType)
		: this(speed, acceleration, turnRate, pushPriority, ParseMovementType(movementType))
	{
	}

	/// <summary>
	/// Gets or sets the movement type category as a readable string.
	/// </summary>
	public string MovementType
	{
		get => Type switch
		{
			Realm.MapAPI.MovementType.Air => "Air",
			Realm.MapAPI.MovementType.Water => "Water",
			Realm.MapAPI.MovementType.Hover => "Hover",
			_ => "Ground"
		};
		set => Type = ParseMovementType(value);
	}

	private static MovementType ParseMovementType(string? type)
	{
		if (string.Equals(type, "Air", StringComparison.OrdinalIgnoreCase)) return Realm.MapAPI.MovementType.Air;
		if (string.Equals(type, "Water", StringComparison.OrdinalIgnoreCase)) return Realm.MapAPI.MovementType.Water;
		if (string.Equals(type, "Hover", StringComparison.OrdinalIgnoreCase)) return Realm.MapAPI.MovementType.Hover;
		return Realm.MapAPI.MovementType.Ground;
	}
}
