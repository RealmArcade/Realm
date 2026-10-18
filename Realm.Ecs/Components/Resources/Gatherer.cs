using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Stores the gathering state for a worker unit entity, including the resource type,
///     carried amount, capacity, and the target resource node entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Gatherer
{
	public int ResourceTypeStringId;
	public float CarriedAmount;
	public float MaxCapacity;
	public Entity TargetEntity;
	public bool ReturningToBase;

	public Gatherer(string type, Entity targetEntity)
	{
		ResourceTypeStringId = StringRegistry.GetOrAdd(type);
		CarriedAmount = 0f;
		MaxCapacity = 15f;
		TargetEntity = targetEntity;
		ReturningToBase = false;
	}

	public string ResourceType
	{
		get => StringRegistry.Get(ResourceTypeStringId);
		set => ResourceTypeStringId = StringRegistry.GetOrAdd(value);
	}
}
