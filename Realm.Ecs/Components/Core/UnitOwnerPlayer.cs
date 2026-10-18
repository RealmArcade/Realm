using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Associates an entity with its controlling player slot index.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct UnitOwnerPlayer
{
	public int Value;

	public UnitOwnerPlayer(int value)
	{
		Value = value;
	}

	public int PlayerIndex => Value;

	public static implicit operator int(UnitOwnerPlayer owner) => owner.Value;
	public static implicit operator UnitOwnerPlayer(int value) => new(value);
}
