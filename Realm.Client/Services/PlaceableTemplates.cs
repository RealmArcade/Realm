using System;
using System.Collections;
using System.Collections.Generic;
using Godot;
using Realm.Shared.Metadata;

namespace Realm.Client.Services;

public enum PlaceableKind
{
	Unit,
	Building,
	Prop,
	Resource
}

public readonly record struct PlaceableTemplate(PlaceableMetadata Metadata, PlaceableKind Kind);

public sealed class PlaceableTemplates
{
	private readonly Dictionary<StringName, PlaceableTemplate> _entries = new();

	public int Count => _entries.Count;

	public IEnumerable<PlaceableTemplate> Entries => _entries.Values;

	public PlaceableTemplate this[StringName key]
	{
		get => _entries[key];
		set => Add(key, value.Metadata, value.Kind);
	}

	public bool TryGet(StringName key, out PlaceableTemplate template) => _entries.TryGetValue(key, out template);

	public bool TryGet(string key, out PlaceableTemplate template)
	{
		template = default;
		if (string.IsNullOrEmpty(key)) return false;

		return _entries.TryGetValue((StringName)key, out template);
	}

	public PlaceableMetadata? Find(string key)
	{
		return TryGet(key, out PlaceableTemplate template) ? template.Metadata : null;
	}

	public T? Find<T>(string key) where T : PlaceableMetadata
	{
		return TryGet(key, out PlaceableTemplate template) && template.Metadata is T typed ? typed : null;
	}

	public bool TryGet<T>(string key, out T metadata) where T : PlaceableMetadata
	{
		metadata = null!;
		if (!TryGet(key, out PlaceableTemplate template)) return false;
		if (template.Metadata is not T typed) return false;

		metadata = typed;
		return true;
	}

	public bool TryGetUnitOrBuilding(StringName key, out UnitMetadata metadata)
	{
		metadata = null!;
		if (!_entries.TryGetValue(key, out PlaceableTemplate template)) return false;
		if (template.Kind is not (PlaceableKind.Unit or PlaceableKind.Building)) return false;
		if (template.Metadata is not UnitMetadata typed) return false;

		metadata = typed;
		return true;
	}

	public bool TryGetUnitOrBuilding(string key, out UnitMetadata metadata)
	{
		metadata = null!;
		if (string.IsNullOrEmpty(key)) return false;

		return TryGetUnitOrBuilding((StringName)key, out metadata);
	}

	public bool ContainsKey(StringName key, PlaceableKind kind)
	{
		return _entries.TryGetValue(key, out PlaceableTemplate template) && template.Kind == kind;
	}

	public bool ContainsKey(string key, PlaceableKind kind)
	{
		return !string.IsNullOrEmpty(key) && ContainsKey((StringName)key, kind);
	}

	public void Add(StringName key, PlaceableMetadata metadata, PlaceableKind kind)
	{
		if (metadata == null) return;
		if (_entries.TryGetValue(key, out PlaceableTemplate existing) && existing.Kind != kind) return;

		_entries[key] = new PlaceableTemplate(metadata, kind);
	}

	public void Remove(StringName key) => _entries.Remove(key);

	public IEnumerable<T> ValuesOf<T>(PlaceableKind kind) where T : PlaceableMetadata
	{
		foreach (KeyValuePair<StringName, PlaceableTemplate> entry in _entries)
		{
			if (entry.Value.Kind != kind) continue;
			if (entry.Value.Metadata is T typed) yield return typed;
		}
	}

	public IEnumerable<KeyValuePair<StringName, T>> PairsOf<T>(PlaceableKind kind) where T : PlaceableMetadata
	{
		foreach (KeyValuePair<StringName, PlaceableTemplate> entry in _entries)
		{
			if (entry.Value.Kind != kind) continue;
			if (entry.Value.Metadata is T typed) yield return new KeyValuePair<StringName, T>(entry.Key, typed);
		}
	}

	public IEnumerable<PlaceableMetadata> ValuesOfKind(PlaceableKind kind)
	{
		foreach (KeyValuePair<StringName, PlaceableTemplate> entry in _entries)
		{
			if (entry.Value.Kind == kind) yield return entry.Value.Metadata;
		}
	}

	public void Clear() => _entries.Clear();
}

public sealed class PlaceableView<T> where T : PlaceableMetadata
{
	private readonly PlaceableTemplates _templates;
	private readonly PlaceableKind _kind;

	public PlaceableView(PlaceableTemplates templates, PlaceableKind kind)
	{
		_templates = templates;
		_kind = kind;
	}

	public PlaceableView(PlaceableKind kind) : this(new PlaceableTemplates(), kind)
	{
	}

	public PlaceableKind Kind => _kind;

	public IEnumerable<StringName> Keys
	{
		get
		{
			foreach (KeyValuePair<StringName, T> pair in Pairs) yield return pair.Key;
		}
	}

	public IEnumerable<T> Values => _templates.ValuesOf<T>(_kind);

	public IEnumerable<KeyValuePair<StringName, T>> Pairs => _templates.PairsOf<T>(_kind);

	public int Count
	{
		get
		{
			int count = 0;
			foreach (PlaceableTemplate entry in _templates.Entries)
			{
				if (entry.Kind == _kind) count++;
			}

			return count;
		}
	}

	public IEnumerator<KeyValuePair<StringName, T>> GetEnumerator() => Pairs.GetEnumerator();

	public bool TryGetValue(StringName key, out T metadata)
	{
		metadata = null!;
		if (!_templates.TryGet(key, out PlaceableTemplate template)) return false;
		if (template.Kind != _kind) return false;
		if (template.Metadata is not T typed) return false;

		metadata = typed;
		return true;
	}

	public bool TryGetValue(string key, out T metadata)
	{
		metadata = null!;
		if (string.IsNullOrEmpty(key)) return false;

		return TryGetValue((StringName)key, out metadata);
	}

	public bool ContainsKey(StringName key) => _templates.ContainsKey(key, _kind);

	public bool ContainsKey(string key) => _templates.ContainsKey(key, _kind);

	public T this[StringName key]
	{
		get => TryGetValue(key, out T metadata) ? metadata : throw new KeyNotFoundException(key.ToString());
		set => _templates.Add(key, value, _kind);
	}

	public void Set(StringName key, T metadata) => _templates.Add(key, metadata, _kind);
}
