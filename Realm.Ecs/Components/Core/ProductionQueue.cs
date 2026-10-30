using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Holds the unit production and training queue state for a production building.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ProductionQueue
{
	public int Handle;
	public int Count;
	public float CurrentProgress;
	public float BuildTime;

	public ProductionQueue()
	{
		Handle = 0;
		Count = 0;
		CurrentProgress = 0f;
		BuildTime = 10f;
	}

	public string? Get(int index)
	{
		if (index < 0 || index >= Count) return null;

		int textId = ContentIdStore.GetTextListItemId(Handle, index);
		return textId == 0 ? null : StringRegistry.Get(textId);
	}

	public bool Enqueue(string? unitId)
	{
		int textId = StringRegistry.GetOrAdd(unitId);
		if (textId == 0) return false;

		Span<int> textIds = stackalloc int[Count + 1];
		CopyTextIds(textIds);
		textIds[Count] = textId;
		ApplyTextIds(textIds);
		return true;
	}

	public string? RemoveAt(int index)
	{
		if (index < 0 || index >= Count) return null;

		int removedTextId = ContentIdStore.GetTextListItemId(Handle, index);
		Span<int> textIds = stackalloc int[Count - 1];
		int writeIndex = 0;
		for (int readIndex = 0; readIndex < Count; readIndex++)
		{
			if (readIndex == index) continue;
			textIds[writeIndex++] = ContentIdStore.GetTextListItemId(Handle, readIndex);
		}

		ApplyTextIds(textIds);
		return removedTextId == 0 ? null : StringRegistry.Get(removedTextId);
	}

	public void Clear()
	{
		Handle = 0;
		Count = 0;
	}

	public void Replace(IReadOnlyList<string>? unitIds)
	{
		if (unitIds == null || unitIds.Count == 0)
		{
			Clear();
			return;
		}

		Span<int> textIds = stackalloc int[unitIds.Count];
		for (int index = 0; index < unitIds.Count; index++)
		{
			textIds[index] = StringRegistry.GetOrAdd(unitIds[index]);
		}

		ApplyTextIds(textIds);
	}

	public void CopyTo(List<string> destination)
	{
		for (int index = 0; index < Count; index++)
		{
			string? unitId = Get(index);
			if (unitId != null) destination.Add(unitId);
		}
	}

	public string[] ToArray()
	{
		var unitIds = new string[Count];
		for (int index = 0; index < Count; index++)
		{
			unitIds[index] = Get(index) ?? string.Empty;
		}

		return unitIds;
	}

	private void CopyTextIds(Span<int> destination)
	{
		for (int index = 0; index < Count; index++)
		{
			destination[index] = ContentIdStore.GetTextListItemId(Handle, index);
		}
	}

	private void ApplyTextIds(ReadOnlySpan<int> textIds)
	{
		Handle = ContentIdStore.GetOrAddTextList(textIds);
		Count = textIds.Length;
	}
}
