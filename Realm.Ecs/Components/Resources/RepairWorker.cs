using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Stores how fast a unit mends a building, as a fraction of the building's maximum health per second. A unit
///     carrying it accepts a repair order; the rate belongs to the repairer, so two heroes can mend the same building at
///     different speeds and a building of any size takes the same share of a given hero's time.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct RepairWorker
{
	/// <summary>Gets or sets the fraction of the repaired building's maximum health restored each second.</summary>
	public float HealthPercentPerSecond;
}
