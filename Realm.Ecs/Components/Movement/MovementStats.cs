using System;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Movement;

/// <summary>
/// Defines the movement, locomotion, and crowd footprint properties of an entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct MovementStats
{
	public float Speed;
	public float Acceleration;
	public float TurnRate;
	public int PushPriority;
	public MovementType Type;

	public MovementStats(
		float speed,
		float acceleration = 0f,
		float turnRate = 0f,
		int pushPriority = 0,
		MovementType movementType = Realm.Ecs.Components.Movement.MovementType.Ground)
	{
		Speed = speed;
		Acceleration = acceleration;
		TurnRate = turnRate;
		PushPriority = pushPriority;
		Type = movementType;
	}

	public MovementStats(
		float speed,
		float acceleration,
		float turnRate,
		int pushPriority,
		string movementType)
		: this(speed, acceleration, turnRate, pushPriority, ParseMovementType(movementType))
	{
	}

	public string MovementType
	{
		get => Type switch
		{
			Realm.Ecs.Components.Movement.MovementType.Air => "Air",
			Realm.Ecs.Components.Movement.MovementType.Water => "Water",
			Realm.Ecs.Components.Movement.MovementType.Hover => "Hover",
			_ => "Ground"
		};
		set => Type = ParseMovementType(value);
	}

	private static MovementType ParseMovementType(string? type)
	{
		if (string.Equals(type, "Air", StringComparison.OrdinalIgnoreCase)) return Realm.Ecs.Components.Movement.MovementType.Air;
		if (string.Equals(type, "Water", StringComparison.OrdinalIgnoreCase)) return Realm.Ecs.Components.Movement.MovementType.Water;
		if (string.Equals(type, "Hover", StringComparison.OrdinalIgnoreCase)) return Realm.Ecs.Components.Movement.MovementType.Hover;
		return Realm.Ecs.Components.Movement.MovementType.Ground;
	}
}
