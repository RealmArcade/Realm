namespace Realm.Ecs.Components.Movement;

/// <summary>
/// Specifies the locomotion medium traversed by a moving entity.
/// </summary>
public enum MovementType : byte
{
	Ground = 0,
	Air = 1,
	Water = 2,
	Hover = 3
}
