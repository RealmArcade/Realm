namespace Realm.Ecs.Components.Core
{
	/// <summary>
	/// Represents the zero-based player slot index (0-24) owning a unit or building.
	/// </summary>
	public record struct UnitOwnerPlayer(int PlayerIndex);
}
