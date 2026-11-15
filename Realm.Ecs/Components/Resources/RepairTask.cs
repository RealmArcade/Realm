using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Tracks the building a unit is mending. The work advances only while the unit stands within reach of the
///     building, so walking away from it leaves the building exactly as far along as the hero left it.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct RepairTask
{
	/// <summary>Gets or sets the building this unit is mending.</summary>
	public Entity BuildingEntity;

	public RepairTask(Entity buildingEntity)
	{
		BuildingEntity = buildingEntity;
	}
}
