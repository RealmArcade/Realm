using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Combat;

/// <summary>
///     Defines which target types an entity's attacks can affect.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct CombatTargeting
{
	public byte CanTargetAirByte;
	public byte CanTargetGroundByte;

	public CombatTargeting(bool canTargetAir, bool canTargetGround)
	{
		CanTargetAirByte = canTargetAir ? (byte)1 : (byte)0;
		CanTargetGroundByte = canTargetGround ? (byte)1 : (byte)0;
	}

	public bool CanTargetAir
	{
		get => CanTargetAirByte != 0;
		set => CanTargetAirByte = value ? (byte)1 : (byte)0;
	}

	public bool CanTargetGround
	{
		get => CanTargetGroundByte != 0;
		set => CanTargetGroundByte = value ? (byte)1 : (byte)0;
	}

	public void Deconstruct(out bool canTargetAir, out bool canTargetGround)
	{
		canTargetAir = CanTargetAir;
		canTargetGround = CanTargetGround;
	}
}