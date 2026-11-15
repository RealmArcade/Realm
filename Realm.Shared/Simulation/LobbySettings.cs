using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Realm.Ecs;

namespace Realm.Shared.Simulation;

/// <summary>
///     One value a map-declared match setting holds, as it travels to the authority that plays the match.
/// </summary>
/// <param name="SettingId">The identifier the map declared the setting under.</param>
/// <param name="Value">The value the host chose for it.</param>
public readonly record struct LobbySettingValue(int SettingId, int Value);

/// <summary>
///     One match setting a lobby's map declared, as the lobby publishes it to the screens sitting in it. Everything a
///     screen needs to draw the setting is here, so the panel that edits it never has to know which map asked for it.
/// </summary>
public class LobbyMatchSetting
{
	/// <summary>The identifier the map declared the setting under.</summary>
	public int SettingId { get; set; }

	/// <summary>The label the host reads, already resolved by the map that declared it.</summary>
	public string Label { get; set; } = string.Empty;

	/// <summary>The sentence that explains the setting, empty when the map offered none.</summary>
	public string Tooltip  { get; set; } = string.Empty;

	/// <summary>How the setting is edited.</summary>
	public LobbySettingControl Control { get; set; }

	/// <summary>The value the host chose, or the value the map stated while nobody has changed it.</summary>
	public int Value { get; set; }

	/// <summary>The lowest value a count may hold, which is zero for a toggle.</summary>
	public int Minimum { get; set; }

	/// <summary>The highest value a count may hold, which is one for a toggle.</summary>
	public int Maximum { get; set; } = 1;

	/// <summary>The options a choice offers, in the order their indices name them.</summary>
	public string[] Options { get; set; } = Array.Empty<string>();

	/// <summary>
	///     Reads the value as the checkbox a toggle is drawn as.
	/// </summary>
	public bool IsOn => Value != 0;
}

/// <summary>
///     The values of the match settings a lobby's map declared. The authority keeps one of these for the lobby it is
///     holding, hands it to the match it starts, and a map reads it back through its own API, so a setting a host changed
///     is what the map acts on and the engine never has to know what any of them mean.
/// </summary>
public class LobbySettingSet
{
	private readonly SortedDictionary<int, int> _values = new();

	/// <summary>Gets a value indicating whether the lobby holds no value at all.</summary>
	public bool IsEmpty => _values.Count == 0;

	/// <summary>Gets the number of settings the lobby holds a value for.</summary>
	public int Count => _values.Count;

	/// <summary>Holds one value.</summary>
	/// <param name="settingId">The identifier the map declared the setting under.</param>
	/// <param name="value">The value to hold.</param>
	public void Set(int settingId, int value)
	{
		_values[settingId] = value;
	}

	/// <summary>Reads one value, answering zero for a setting the lobby holds nothing for.</summary>
	/// <param name="settingId">The identifier the map declared the setting under.</param>
	/// <returns>The value held, or zero.</returns>
	public int Get(int settingId)
	{
		return _values.TryGetValue(settingId, out int value) ? value : 0;
	}

	/// <summary>Answers whether the lobby holds a value for one setting.</summary>
	/// <param name="settingId">The identifier the map declared the setting under.</param>
	/// <returns>Whether a value is held.</returns>
	public bool Contains(int settingId)
	{
		return _values.ContainsKey(settingId);
	}

	/// <summary>Copies every value another set holds.</summary>
	/// <param name="source">The set to copy.</param>
	public void CopyFrom(LobbySettingSet source)
	{
		_values.Clear();
		foreach (KeyValuePair<int, int> pair in source._values) _values[pair.Key] = pair.Value;
	}

	/// <summary>Copies the values a roster or a lobby published into this set.</summary>
	/// <param name="values">The published values.</param>
	public void CopyFrom(IReadOnlyList<LobbySettingValue> values)
	{
		_values.Clear();
		if (values == null) return;

		for (int index = 0; index < values.Count; index++) Set(values[index].SettingId, values[index].Value);
	}

	/// <summary>Lists every value the lobby holds, in setting identifier order.</summary>
	/// <returns>The values, ordered by identifier.</returns>
	public LobbySettingValue[] ToValues()
	{
		var values = new LobbySettingValue[_values.Count];
		int index = 0;
		foreach (KeyValuePair<int, int> pair in _values) values[index++] = new LobbySettingValue(pair.Key, pair.Value);

		return values;
	}

	/// <summary>States what this set holds as one line, for a log or a report.</summary>
	/// <returns>The values, or "standard" when the set is empty.</returns>
	public string Describe()
	{
		if (_values.Count == 0) return "standard";

		var builder = new StringBuilder();
		foreach (KeyValuePair<int, int> pair in _values)
		{
			if (builder.Length > 0) builder.Append(", ");
			builder.Append(pair.Key.ToString(CultureInfo.InvariantCulture)).Append('=').Append(pair.Value.ToString(CultureInfo.InvariantCulture));
		}

		return builder.ToString();
	}
}
