using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Realm.Ecs;

/// <summary>
/// Bitmask representing a set of component types for entity filter queries.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct ComponentMask : IEquatable<ComponentMask>
{
	public readonly ulong Mask0;
	public readonly ulong Mask1;
	public readonly ulong Mask2;
	public readonly ulong Mask3;

	public ComponentMask(ulong mask0, ulong mask1 = 0, ulong mask2 = 0, ulong mask3 = 0)
	{
		Mask0 = mask0;
		Mask1 = mask1;
		Mask2 = mask2;
		Mask3 = mask3;
	}

	public static ComponentMask Empty => default;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ComponentMask Of<T>()
	{
		int id = ComponentType<T>.Id;
		return FromId(id);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ComponentMask FromId(int componentId)
	{
		int word = componentId >> 6;
		int bit = componentId & 63;
		ulong bitVal = 1UL << bit;

		return word switch
		{
			0 => new ComponentMask(bitVal, 0, 0, 0),
			1 => new ComponentMask(0, bitVal, 0, 0),
			2 => new ComponentMask(0, 0, bitVal, 0),
			3 => new ComponentMask(0, 0, 0, bitVal),
			_ => throw new ArgumentOutOfRangeException(nameof(componentId))
		};
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ComponentMask Of<T1, T2>() => Of<T1>() | Of<T2>();

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ComponentMask Of<T1, T2, T3>() => Of<T1>() | Of<T2>() | Of<T3>();

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ComponentMask Of<T1, T2, T3, T4>() => Of<T1>() | Of<T2>() | Of<T3>() | Of<T4>();

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ComponentMask operator |(ComponentMask left, ComponentMask right)
	{
		return new ComponentMask(
			left.Mask0 | right.Mask0,
			left.Mask1 | right.Mask1,
			left.Mask2 | right.Mask2,
			left.Mask3 | right.Mask3);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ComponentMask operator &(ComponentMask left, ComponentMask right)
	{
		return new ComponentMask(
			left.Mask0 & right.Mask0,
			left.Mask1 & right.Mask1,
			left.Mask2 & right.Mask2,
			left.Mask3 & right.Mask3);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool HasAll(ComponentMask required)
	{
		return (Mask0 & required.Mask0) == required.Mask0
			&& (Mask1 & required.Mask1) == required.Mask1
			&& (Mask2 & required.Mask2) == required.Mask2
			&& (Mask3 & required.Mask3) == required.Mask3;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool HasAny(ComponentMask required)
	{
		return (Mask0 & required.Mask0) != 0
			|| (Mask1 & required.Mask1) != 0
			|| (Mask2 & required.Mask2) != 0
			|| (Mask3 & required.Mask3) != 0;
	}

	public bool Equals(ComponentMask other)
	{
		return Mask0 == other.Mask0 && Mask1 == other.Mask1 && Mask2 == other.Mask2 && Mask3 == other.Mask3;
	}

	public override bool Equals(object? obj) => obj is ComponentMask other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Mask0, Mask1, Mask2, Mask3);

	public static bool operator ==(ComponentMask left, ComponentMask right) => left.Equals(right);
	public static bool operator !=(ComponentMask left, ComponentMask right) => !left.Equals(right);
}
