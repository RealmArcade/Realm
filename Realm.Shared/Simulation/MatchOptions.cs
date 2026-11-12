using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Realm.Ecs;
using Realm.Shared.Network;

namespace Realm.Shared.Simulation;

/// <summary>
///     One value a lobby's host set for a match, as it travels to the authority that plays it.
/// </summary>
public readonly record struct MatchOptionValue(MatchOption Option, int Value);

/// <summary>
///     The set of parameters a lobby's host configured for one match. An option the host left alone carries no value at
///     all, so the map that plays the match keeps its own default rather than being handed a number the engine invented.
/// </summary>
/// <remarks>
///     Every option here modifies a formula the engine already evaluates; none of them selects a different set of rules.
///     The authority reads the set once when the match starts and never changes it, so identical participation under the
///     same options always produces the same match.
/// </remarks>
public class MatchOptionSet
{
	/// <summary>The ceiling a host may ask for on <see cref="MatchOption.CapacityMultiplierPercent" /> and its charge twin.</summary>
	public const int MaximumMultiplierPercent = 1000;

	private readonly Dictionary<MatchOption, int> _values = new();

	/// <summary>Gets a value indicating whether the host set no option at all.</summary>
	public bool IsEmpty => _values.Count == 0;

	/// <summary>Gets the number of options the host set.</summary>
	public int Count => _values.Count;

	/// <summary>
	///     Sets one option, or clears it when zero is given, which is how a host returns an option to the map's own
	///     default.
	/// </summary>
	/// <param name="option">The option being configured.</param>
	/// <param name="value">The value to play, or zero to leave the option to the map.</param>
	/// <returns>Whether the option accepted the value.</returns>
	public bool Set(MatchOption option, int value)
	{
		if (!IsValueInRange(option, value)) return false;

		if (value == 0)
		{
			_values.Remove(option);
			return true;
		}

		_values[option] = value;
		return true;
	}

	/// <summary>
	///     Reads one option as the match plays it. An option the host left alone answers zero, which every option treats as
	///     "the map decides".
	/// </summary>
	/// <param name="option">The option being read.</param>
	/// <returns>The value the host set, or zero when the host left the option alone.</returns>
	public int Get(MatchOption option)
	{
		return _values.TryGetValue(option, out int value) ? value : 0;
	}

	/// <summary>Reads one option, falling back to a default the caller states.</summary>
	/// <param name="option">The option being read.</param>
	/// <param name="fallback">The value to answer when the host left the option alone.</param>
	/// <returns>The value the host set, or the fallback.</returns>
	public int GetOr(MatchOption option, int fallback)
	{
		int value = Get(option);
		return value == 0 ? fallback : value;
	}

	/// <summary>Reads whether an option the host may switch on was switched on.</summary>
	/// <param name="option">The option being read.</param>
	/// <returns>Whether the host enabled it.</returns>
	public bool IsEnabled(MatchOption option)
	{
		return Get(option) != 0;
	}

	/// <summary>Reads the shroud mode the lobby's host asked for.</summary>
	/// <returns>The requested mode, or <see cref="MatchShroudMode.FromMap" /> when the host left it to the map.</returns>
	public MatchShroudMode GetShroudMode()
	{
		int value = Get(MatchOption.ShroudMode);
		return Enum.IsDefined(typeof(MatchShroudMode), value) ? (MatchShroudMode)value : MatchShroudMode.FromMap;
	}

	/// <summary>Copies every option another set holds, which is how a lobby's state reaches the screen that starts it.</summary>
	/// <param name="source">The set to copy.</param>
	public void CopyFrom(MatchOptionSet source)
	{
		_values.Clear();
		foreach (KeyValuePair<MatchOption, int> pair in source._values) _values[pair.Key] = pair.Value;
	}

	/// <summary>Copies the options a lobby published into this set.</summary>
	/// <param name="values">The published options.</param>
	public void CopyFrom(IReadOnlyList<MatchOptionValue> values)
	{
		_values.Clear();
		if (values == null) return;

		for (int index = 0; index < values.Count; index++)
		{
			Set(values[index].Option, values[index].Value);
		}
	}

	/// <summary>Lists every option the host set, which is how a lobby publishes them.</summary>
	/// <returns>The options, in no particular order.</returns>
	public MatchOptionValue[] ToValues()
	{
		var values = new MatchOptionValue[_values.Count];
		int index = 0;
		foreach (KeyValuePair<MatchOption, int> pair in _values) values[index++] = new MatchOptionValue(pair.Key, pair.Value);

		return values;
	}

	/// <summary>States what this set holds as one line per option, for a log or a report.</summary>
	/// <returns>The options, one per line.</returns>
	public string Describe()
	{
		if (_values.Count == 0) return "standard";

		var builder = new StringBuilder();
		foreach (KeyValuePair<MatchOption, int> pair in _values)
		{
			if (builder.Length > 0) builder.Append(", ");
			builder.Append(pair.Key).Append('=').Append(pair.Value.ToString(CultureInfo.InvariantCulture));
		}

		return builder.ToString();
	}

	private static bool IsValueInRange(MatchOption option, int value)
	{
		if (value < 0) return false;

		return option switch
		{
			MatchOption.MaximumPlayers => value <= LobbyLimits.MaximumSeats,
			MatchOption.MaximumBuildingDraftCount => value <= 24,
			MatchOption.MaximumUniqueBuildingInstances => value <= 24,
			MatchOption.ShroudMode => value <= (int)MatchShroudMode.ExplorationShroud,
			MatchOption.MirroredAutoDraft => value <= 1,
			MatchOption.OpenPoolDrafting => value <= 1,
			MatchOption.CapacityMultiplierPercent => value <= MaximumMultiplierPercent,
			MatchOption.ChargeMultiplierPercent => value <= MaximumMultiplierPercent,
			MatchOption.MaximumBuildingSupply => value <= 24,
			_ => false
		};
	}
}
