using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Movement;

/// <summary>
///     Defines the movement, locomotion, and crowd footprint properties of an entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct MovementStats
{
	public float Speed;
	public float Acceleration;
	public float TurnRate;
	public int PushPriority;
	public int MovementTypeStringId;

	public MovementStats(
		float speed,
		float acceleration = 0f,
		float turnRate = 0f,
		int pushPriority = 0,
		string movementType = "Ground")
	{
		Speed = speed;
		Acceleration = acceleration;
		TurnRate = turnRate;
		PushPriority = pushPriority;
		MovementTypeStringId = StringRegistry.GetOrAdd(movementType);
	}

	public string MovementType
	{
		get => StringRegistry.Get(MovementTypeStringId);
		set => MovementTypeStringId = StringRegistry.GetOrAdd(value);
	}
}
