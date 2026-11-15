using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Names the resource a worker was assigned to by the building that raised it, which is what the map's worker AI
///     sends it to gather.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct WorkerAssignment
{
	public int ResourceTypeStringId;

	public WorkerAssignment(string resourceType)
	{
		ResourceTypeStringId = StringRegistry.GetOrAdd(resourceType);
	}

	public string ResourceType
	{
		get => StringRegistry.Get(ResourceTypeStringId);
		set => ResourceTypeStringId = StringRegistry.GetOrAdd(value);
	}
}
