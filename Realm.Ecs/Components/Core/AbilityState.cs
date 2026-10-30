using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Holds the ability identifiers an entity can currently use.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct AbilityState
{
	public int Handle;
	public int Count;

	public AbilityState(IReadOnlyList<string>? abilities)
	{
		Handle = InternTextIds(abilities, out int count);
		Count = count;
	}

	public string? Get(int index)
	{
		if (index < 0 || index >= Count) return null;

		int textId = ContentIdStore.GetTextListItemId(Handle, index);
		return textId == 0 ? null : StringRegistry.Get(textId);
	}

	public bool Contains(string? abilityId)
	{
		if (string.IsNullOrEmpty(abilityId)) return false;

		for (int index = 0; index < Count; index++)
		{
			string? existing = Get(index);
			if (existing != null && string.Equals(existing, abilityId, StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
	}

	public bool Add(string? abilityId)
	{
		int textId = StringRegistry.GetOrAdd(abilityId);
		if (textId == 0 || Contains(abilityId)) return false;

		Span<int> textIds = stackalloc int[Count + 1];
		for (int index = 0; index < Count; index++)
		{
			textIds[index] = ContentIdStore.GetTextListItemId(Handle, index);
		}

		textIds[Count] = textId;
		Handle = ContentIdStore.GetOrAddTextList(textIds);
		Count = textIds.Length;
		return true;
	}

	public void CopyTo(List<string> destination)
	{
		for (int index = 0; index < Count; index++)
		{
			string? abilityId = Get(index);
			if (abilityId != null) destination.Add(abilityId);
		}
	}

	public string[] ToArray()
	{
		var abilityIds = new string[Count];
		for (int index = 0; index < Count; index++)
		{
			abilityIds[index] = Get(index) ?? string.Empty;
		}

		return abilityIds;
	}

	private static int InternTextIds(IReadOnlyList<string>? abilities, out int count)
	{
		count = 0;
		if (abilities == null || abilities.Count == 0) return 0;

		Span<int> textIds = stackalloc int[abilities.Count];
		int writeIndex = 0;
		for (int index = 0; index < abilities.Count; index++)
		{
			int textId = StringRegistry.GetOrAdd(abilities[index]);
			if (textId == 0) continue;
			textIds[writeIndex++] = textId;
		}

		count = writeIndex;
		return ContentIdStore.GetOrAddTextList(textIds.Slice(0, writeIndex));
	}
}
