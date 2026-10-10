
namespace Realm.Ecs.Components.Core;

/// <summary>
/// Stores all script-defined zones in the world entity.
/// </summary>
public struct ScriptZonesState
{
	public ScriptZonesState(List<ZoneBounds> zones)
	{
		Zones = zones;
	}

	public List<ZoneBounds> Zones { get; }
}