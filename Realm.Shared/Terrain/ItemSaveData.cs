namespace Realm.Shared.Terrain;

/// <summary>
///     One item the map places on the ground, waiting for a unit to walk over and collect it. The entry names the item
///     template and where it lies; the item's own template states what it looks like and how much of it one pickup grants.
/// </summary>
public class ItemSaveData
{
	public string TemplateId { get; set; } = string.Empty;
	public float PosX { get; set; }
	public float PosY { get; set; }
	public float PosZ { get; set; }
}
