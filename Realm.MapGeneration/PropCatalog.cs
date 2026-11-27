using System;
using System.Collections.Generic;

namespace Realm.MapGeneration;

public class PropCatalog
{
	private readonly List<PropCatalogEntry> _entries = new();

	public IReadOnlyList<PropCatalogEntry> Entries => _entries;

	public bool IsEmpty => _entries.Count == 0;

	public void Add(PropCatalogEntry entry)
	{
		_entries.Add(entry);
	}

	public void Add(string? templateId, string? name, float scale)
	{
		if (string.IsNullOrWhiteSpace(templateId))
		{
			return;
		}

		_entries.Add(new PropCatalogEntry(templateId, name ?? string.Empty, PropCatalogEntry.Classify(templateId, name), scale <= 0f ? 1f : scale));
	}

	public PropCatalogEntry FindForCategory(PropCategory category, int variation)
	{
		if (_entries.Count == 0)
		{
			return new PropCatalogEntry(string.Empty, string.Empty, category, 1f);
		}

		var matches = new List<PropCatalogEntry>();
		for (int index = 0; index < _entries.Count; index++)
		{
			if (_entries[index].Category == category)
			{
				matches.Add(_entries[index]);
			}
		}

		if (matches.Count == 0)
		{
			for (int index = 0; index < _entries.Count; index++)
			{
				PropCatalogEntry entry = _entries[index];
				if (entry.Category is PropCategory.Tree or PropCategory.Nature)
				{
					matches.Add(entry);
				}
			}
		}

		if (matches.Count == 0)
		{
			return _entries[0];
		}

		return matches[(int)((uint)variation % (uint)matches.Count)];
	}

	public int CountForCategory(PropCategory category)
	{
		int count = 0;
		for (int index = 0; index < _entries.Count; index++)
		{
			if (_entries[index].Category == category)
			{
				count++;
			}
		}

		return count;
	}
}
