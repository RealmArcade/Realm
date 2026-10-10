using Arch.Core;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Stores the gathering state for a worker unit entity, including the resource type,
///     carried amount, capacity, and the target resource node entity.
/// </summary>
public struct Gatherer
{
	public string ResourceType;
	public float CarriedAmount;
	public float MaxCapacity;
	public Entity TargetEntity;
	public bool ReturningToBase;

	public Gatherer(string type, Entity targetEntity)
	{
		ResourceType = type;
		CarriedAmount = 0f;
		MaxCapacity = 15f;
		TargetEntity = targetEntity;
		ReturningToBase = false;
	}
}
