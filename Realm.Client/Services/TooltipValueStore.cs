using System;
using System.Collections.Generic;
using Realm.Ecs;

namespace Realm.Client.Services;

public class TooltipValueStore
{
	private readonly Dictionary<string, Dictionary<string, float>> _numbers =
		new(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, Dictionary<string, string>> _text =
		new(StringComparer.OrdinalIgnoreCase);

	public static string OwnerKey(string kind, string id) => $"{kind}:{id}";

	public static string OwnerKey(string kind, int id) => $"{kind}:{id}";

	/// <summary>
	///     Names the unit a map published tooltip values for. A map addresses a unit by the authority entity id it was
	///     handed, while the replicated world names it by a local id, so the authority id is the key both sides share.
	/// </summary>
	/// <param name="entity">The replicated unit whose published values are being read.</param>
	/// <returns>The key the unit's published values are stored under.</returns>
	public static string UnitOwnerKey(Entity entity)
	{
		int serverEntityId = GameContext.NetworkService?.GetServerEntityId(entity.Id) ?? -1;
		return OwnerKey("unit", serverEntityId > 0 ? serverEntityId : entity.Id);
	}

	public void SetNumber(string owner, string? key, float value)
	{
		if (string.IsNullOrEmpty(owner) || string.IsNullOrWhiteSpace(key)) return;

		if (!_numbers.TryGetValue(owner, out Dictionary<string, float>? bag))
		{
			bag = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
			_numbers[owner] = bag;
		}

		bag[key] = value;
	}

	public void SetNumbers(string owner, string[]? keys, float[]? values)
	{
		if (string.IsNullOrEmpty(owner) || keys == null || values == null) return;

		int count = Math.Min(keys.Length, values.Length);
		if (count == 0) return;

		if (!_numbers.TryGetValue(owner, out Dictionary<string, float>? bag))
		{
			bag = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
			_numbers[owner] = bag;
		}

		for (int index = 0; index < count; index++)
		{
			string key = keys[index];
			if (string.IsNullOrWhiteSpace(key)) continue;
			bag[key] = values[index];
		}
	}

	public void SetText(string owner, string? key, string? value)
	{
		if (string.IsNullOrEmpty(owner) || string.IsNullOrWhiteSpace(key)) return;

		if (!_text.TryGetValue(owner, out Dictionary<string, string>? bag))
		{
			bag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			_text[owner] = bag;
		}

		bag[key] = value ?? string.Empty;
	}

	public bool TryGetNumber(string owner, string key, out float value)
	{
		value = 0f;
		if (string.IsNullOrEmpty(owner) || string.IsNullOrWhiteSpace(key)) return false;
		if (!_numbers.TryGetValue(owner, out Dictionary<string, float>? bag)) return false;
		if (!bag.TryGetValue(key, out float found)) return false;

		value = found;
		return true;
	}

	public bool TryGetText(string owner, string key, out string value)
	{
		value = string.Empty;
		if (string.IsNullOrEmpty(owner) || string.IsNullOrWhiteSpace(key)) return false;
		if (!_text.TryGetValue(owner, out Dictionary<string, string>? bag)) return false;
		if (!bag.TryGetValue(key, out string? found) || found == null) return false;

		value = found;
		return true;
	}

	public void Clear()
	{
		_numbers.Clear();
		_text.Clear();
	}
}
