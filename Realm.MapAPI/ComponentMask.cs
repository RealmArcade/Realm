using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Realm.MapAPI;

/// <summary>
/// Bitmask representing a set of component types for entity filter queries.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct ComponentMask : IEquatable<ComponentMask>
{
	/// <summary>
	/// Bitmask word 0 (components 0-63).
	/// </summary>
	public readonly ulong Mask0;

	/// <summary>
	/// Bitmask word 1 (components 64-127).
	/// </summary>
	public readonly ulong Mask1;

	/// <summary>
	/// Bitmask word 2 (components 128-191).
	/// </summary>
	public readonly ulong Mask2;

	/// <summary>
	/// Bitmask word 3 (components 192-255).
	/// </summary>
	public readonly ulong Mask3;

	/// <summary>
	/// Initializes a new instance of the <see cref="ComponentMask"/> struct.
	/// </summary>
	/// <param name="mask0">Bitmask word 0.</param>
	/// <param name="mask1">Bitmask word 1.</param>
	/// <param name="mask2">Bitmask word 2.</param>
	/// <param name="mask3">Bitmask word 3.</param>
	public ComponentMask(ulong mask0, ulong mask1 = 0, ulong mask2 = 0, ulong mask3 = 0)
	{
		Mask0 = mask0;
		Mask1 = mask1;
		Mask2 = mask2;
		Mask3 = mask3;
	}

	/// <summary>
	/// Gets an empty component mask with all bits set to 0.
	/// </summary>
	public static ComponentMask Empty => default;

	/// <summary>
	/// Creates a component mask from a specific 0-based component type identifier.
	/// </summary>
	/// <param name="componentId">The component type identifier (0-255).</param>
	/// <returns>A new component mask with only the specified component bit set.</returns>
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

	/// <summary>
	/// Computes the bitwise OR union of two component masks.
	/// </summary>
	/// <param name="left">The first component mask.</param>
	/// <param name="right">The second component mask.</param>
	/// <returns>The bitwise OR union mask.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ComponentMask operator |(ComponentMask left, ComponentMask right)
	{
		return new ComponentMask(
			left.Mask0 | right.Mask0,
			left.Mask1 | right.Mask1,
			left.Mask2 | right.Mask2,
			left.Mask3 | right.Mask3);
	}

	/// <summary>
	/// Computes the bitwise AND intersection of two component masks.
	/// </summary>
	/// <param name="left">The first component mask.</param>
	/// <param name="right">The second component mask.</param>
	/// <returns>The bitwise AND intersection mask.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ComponentMask operator &(ComponentMask left, ComponentMask right)
	{
		return new ComponentMask(
			left.Mask0 & right.Mask0,
			left.Mask1 & right.Mask1,
			left.Mask2 & right.Mask2,
			left.Mask3 & right.Mask3);
	}

	/// <summary>
	/// Determines whether this mask contains all bits set in the required mask.
	/// </summary>
	/// <param name="required">The required component mask.</param>
	/// <returns>True if all required bits are present; otherwise false.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool HasAll(ComponentMask required)
	{
		return (Mask0 & required.Mask0) == required.Mask0
			&& (Mask1 & required.Mask1) == required.Mask1
			&& (Mask2 & required.Mask2) == required.Mask2
			&& (Mask3 & required.Mask3) == required.Mask3;
	}

	/// <summary>
	/// Determines whether this mask contains any bits set in the required mask.
	/// </summary>
	/// <param name="required">The target component mask.</param>
	/// <returns>True if any bits intersect; otherwise false.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool HasAny(ComponentMask required)
	{
		return (Mask0 & required.Mask0) != 0
			|| (Mask1 & required.Mask1) != 0
			|| (Mask2 & required.Mask2) != 0
			|| (Mask3 & required.Mask3) != 0;
	}

	/// <summary>
	/// Indicates whether the current object is equal to another object of the same type.
	/// </summary>
	/// <param name="other">An object to compare with this object.</param>
	/// <returns>True if the current object is equal to the other parameter; otherwise false.</returns>
	public bool Equals(ComponentMask other)
	{
		return Mask0 == other.Mask0 && Mask1 == other.Mask1 && Mask2 == other.Mask2 && Mask3 == other.Mask3;
	}

	/// <summary>
	/// Determines whether the specified object is equal to the current object.
	/// </summary>
	/// <param name="obj">The object to compare with the current object.</param>
	/// <returns>True if the specified object is equal to the current object; otherwise false.</returns>
	public override bool Equals(object? obj) => obj is ComponentMask other && Equals(other);

	/// <summary>
	/// Returns the hash code for this instance.
	/// </summary>
	/// <returns>A 32-bit signed integer hash code.</returns>
	public override int GetHashCode() => HashCode.Combine(Mask0, Mask1, Mask2, Mask3);

	/// <summary>
	/// Compares two component masks for equality.
	/// </summary>
	/// <param name="left">The first component mask.</param>
	/// <param name="right">The second component mask.</param>
	/// <returns>True if equal; otherwise false.</returns>
	public static bool operator ==(ComponentMask left, ComponentMask right) => left.Equals(right);

	/// <summary>
	/// Compares two component masks for inequality.
	/// </summary>
	/// <param name="left">The first component mask.</param>
	/// <param name="right">The second component mask.</param>
	/// <returns>True if not equal; otherwise false.</returns>
	public static bool operator !=(ComponentMask left, ComponentMask right) => !left.Equals(right);
}
