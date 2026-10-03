namespace Realm.Ecs.Components.Movement;

/// <summary>
///     Defines the movement, locomotion, and crowd footprint properties of an entity.
/// </summary>
internal record struct MovementStats(
	float Speed,
	float Acceleration = 0f,
	float TurnRate = 0f,
	int PushPriority = 0,
	string MovementType = "Ground");
