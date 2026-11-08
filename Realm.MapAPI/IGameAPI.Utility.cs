// Extracted from IGameAPI.cs (task 6 decomposition).
using System.Numerics;


namespace Realm.MapAPI;


/// <summary>
/// Defines the safe, public API exposed to map scripts to interact with the game engine.
/// </summary>
public partial interface IGameAPI
{
	/// <summary>
	/// Returns a uniformly random element of the given list.
	/// </summary>
	/// <typeparam name="T">The element type.</typeparam>
	/// <param name="items">The list to pick from.</param>
	/// <returns>A random element, or the default value of <typeparamref name="T"/> if the list is empty.</returns>
	T? PickRandom<T>(IReadOnlyList<T> items)
	{
		if (items.Count == 0)
			return default;
		return items[RandomInt(0, items.Count - 1)];
	}

	/// <summary>
	/// Shuffles the given list in place using a Fisher-Yates shuffle driven by <see cref="RandomInt"/>.
	/// </summary>
	/// <typeparam name="T">The element type.</typeparam>
	/// <param name="items">The list to shuffle.</param>
	void Shuffle<T>(IList<T> items)
	{
		for (int index = items.Count - 1; index > 0; index--)
		{
			int swapIndex = RandomInt(0, index);
			(items[index], items[swapIndex]) = (items[swapIndex], items[index]);
		}
	}

	/// <summary>
	/// Returns a random alive unit within the radius that satisfies the optional filter.
	/// </summary>
	/// <param name="center">The center point in 3D world space.</param>
	/// <param name="radius">The search radius.</param>
	/// <param name="filter">Optional predicate that returns true for eligible units.</param>
	/// <returns>A random matching unit, or UnitId.Invalid if none qualify.</returns>
	UnitId GetRandomUnitInRadius(Vector3 center, float radius, Func<UnitId, bool>? filter = null)
	{
		var units = GetUnitsInRadius(center, radius);
		int matchCount = 0;
		for (int i = 0; i < units.Length; i++)
		{
			var u = units[i];
			if (u.IsDead)
				continue;
			if (filter != null && !filter(u))
				continue;
			matchCount++;
		}

		if (matchCount == 0)
			return UnitId.Invalid;

		int pickIndex = RandomInt(0, matchCount - 1);
		int current = 0;
		for (int i = 0; i < units.Length; i++)
		{
			var u = units[i];
			if (u.IsDead)
				continue;
			if (filter != null && !filter(u))
				continue;
			if (current == pickIndex)
				return u;
			current++;
		}

		return UnitId.Invalid;
	}

	/// <summary>
	/// Restores health to the unit, clamped to its maximum health. Dead units are ignored.
	/// </summary>
	/// <param name="unit">The unit to heal.</param>
	/// <param name="amount">The amount of health to restore.</param>
	void HealUnit(UnitId unit, float amount)
	{
		if (!unit.IsValid || unit.IsDead || amount <= 0f)
			return;
		unit.Health = MathF.Min(unit.MaxHealth, unit.Health + amount);
	}

	/// <summary>
	/// Exchanges the world positions of two units.
	/// </summary>
	/// <param name="first">The first unit.</param>
	/// <param name="second">The second unit.</param>
	void SwapUnitPositions(UnitId first, UnitId second)
	{
		Vector3 firstPosition = first.Position;
		Vector3 secondPosition = second.Position;
		first.Teleport(secondPosition);
		second.Teleport(firstPosition);
	}

	/// <summary>
	/// Returns a point on a ring around a center, useful for distributing player bases evenly.
	/// </summary>
	/// <param name="center">The ring center.</param>
	/// <param name="radius">The ring radius.</param>
	/// <param name="index">The zero-based slot index around the ring.</param>
	/// <param name="count">The total number of slots on the ring.</param>
	/// <returns>The position of the slot on the X/Z plane, preserving the center's Y.</returns>
	Vector3 GetRingPosition(Vector3 center, float radius, int index, int count)
	{
		float angle = MathF.Tau * index / Math.Max(1, count);
		return new Vector3(center.X + MathF.Cos(angle) * radius, center.Y, center.Z + MathF.Sin(angle) * radius);
	}
}
