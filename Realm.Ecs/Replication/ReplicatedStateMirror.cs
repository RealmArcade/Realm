using System;
using System.Collections.Generic;
using MemoryPack;

namespace Realm.Ecs.Replication;

[MemoryPackable]
public partial struct AbilityStateDelta
{
	public int ServerEntityId;
	public string AbilityId;
	public byte Flags;
}

[MemoryPackable]
public partial struct CatalogEntry
{
	public string Channel;
	public string Key;
	public string Value;
	public float Number;
}

public struct StaticTextState
{
	public string Text;
	public System.Numerics.Vector3 Position;
	public System.Numerics.Vector3 Color;
	public float FontSize;
	public bool Visible;
}

[MemoryPackable]
public partial struct StaticTextDelta
{
	public int Handle;
	public string Text;
	public float PositionX;
	public float PositionY;
	public float PositionZ;
	public float ColorX;
	public float ColorY;
	public float ColorZ;
	public float FontSize;
	public byte Visible;
	public byte Destroyed;
}

public class ReplicatedStateMirror
{
	public Dictionary<(byte Scope, int Key, int TypeId), byte[]> Components { get; } = new();
	public Dictionary<(int ServerEntityId, string AbilityId), byte> Abilities { get; } = new();
	public Dictionary<int, StaticTextDelta> Texts { get; } = new();
	public Dictionary<(byte Scope, int Key, byte Kind, byte Role), int> ColdPayloads { get; } = new();

	public void Clear()
	{
		Components.Clear();
		Abilities.Clear();
		Texts.Clear();
		ColdPayloads.Clear();
	}

	public void RecordComponent(ComponentDelta delta)
	{
		var key = (delta.Scope, delta.Key, delta.ComponentTypeId);
		if (delta.Bytes == null || delta.Bytes.Length == 0)
		{
			Components.Remove(key);
			return;
		}

		Components[key] = delta.Bytes;
	}

	public void RecordAbility(AbilityStateDelta delta)
	{
		Abilities[(delta.ServerEntityId, delta.AbilityId)] = delta.Flags;
	}

	public void RecordText(StaticTextDelta delta)
	{
		Texts[delta.Handle] = delta;
	}

	public void RecordColdPayload(ColdPayloadDelta delta)
	{
		var key = (delta.Scope, delta.Key, delta.Kind, delta.Role);
		if (delta.Destroyed != 0)
		{
			ColdPayloads.Remove(key);
			return;
		}

		ColdPayloads[key] = delta.ContentId;
	}

	public uint ComputeFingerprint()
	{
		var records = new List<(int Key, int ComponentTypeId, byte[] Bytes)>(Components.Count);
		foreach (var kvp in Components)
		{
			if (kvp.Key.Scope != (byte)ReplicatedScope.Player) continue;
			records.Add((kvp.Key.Key, kvp.Key.TypeId, kvp.Value));
		}
		return ComputeFingerprint(records);
	}

	public uint ComputeFingerprint(List<(int Key, int ComponentTypeId, byte[] Bytes)> records)
	{
		records.Sort(static (left, right) =>
		{
			int byKey = left.Key.CompareTo(right.Key);
			return byKey != 0 ? byKey : left.ComponentTypeId.CompareTo(right.ComponentTypeId);
		});

		uint hash = ReplicatedStateFingerprint.Seed;
		for (int i = 0; i < records.Count; i++)
		{
			var record = records[i];
			hash = ReplicatedStateFingerprint.Combine(hash, 1);
			hash = ReplicatedStateFingerprint.Combine(hash, record.Key);
			hash = ReplicatedStateFingerprint.Combine(hash, record.ComponentTypeId);
			hash = ReplicatedStateFingerprint.Combine(hash, record.Bytes);
		}

		var abilityRecords = new List<((int ServerEntityId, string AbilityId) Key, byte Flags)>(Abilities.Count);
		foreach (var kvp in Abilities)
		{
			abilityRecords.Add((kvp.Key, kvp.Value));
		}
		abilityRecords.Sort(static (left, right) =>
		{
			int byEntity = left.Key.ServerEntityId.CompareTo(right.Key.ServerEntityId);
			return byEntity != 0 ? byEntity : string.CompareOrdinal(left.Key.AbilityId, right.Key.AbilityId);
		});
		for (int i = 0; i < abilityRecords.Count; i++)
		{
			var record = abilityRecords[i];
			hash = ReplicatedStateFingerprint.Combine(hash, 3);
			hash = ReplicatedStateFingerprint.Combine(hash, record.Key.ServerEntityId);
			hash = ReplicatedStateFingerprint.Combine(hash, record.Key.AbilityId);
			hash = ReplicatedStateFingerprint.Combine(hash, record.Flags);
		}

		var textHandles = new List<int>(Texts.Keys);
		textHandles.Sort();
		for (int i = 0; i < textHandles.Count; i++)
		{
			var text = Texts[textHandles[i]];
			hash = ReplicatedStateFingerprint.Combine(hash, 4);
			hash = ReplicatedStateFingerprint.Combine(hash, text.Handle);
			hash = ReplicatedStateFingerprint.Combine(hash, text.Text);
			hash = ReplicatedStateFingerprint.Combine(hash, BitConverter.SingleToInt32Bits(text.PositionX));
			hash = ReplicatedStateFingerprint.Combine(hash, BitConverter.SingleToInt32Bits(text.PositionY));
			hash = ReplicatedStateFingerprint.Combine(hash, BitConverter.SingleToInt32Bits(text.PositionZ));
			hash = ReplicatedStateFingerprint.Combine(hash, text.Visible);
		}

		var coldKeys = new List<(byte Scope, int Key, byte Kind, byte Role)>(ColdPayloads.Keys);
		coldKeys.Sort(static (left, right) =>
		{
			int byScope = left.Scope.CompareTo(right.Scope);
			if (byScope != 0) return byScope;
			int byKey = left.Key.CompareTo(right.Key);
			if (byKey != 0) return byKey;
			int byKind = left.Kind.CompareTo(right.Kind);
			return byKind != 0 ? byKind : left.Role.CompareTo(right.Role);
		});
		for (int i = 0; i < coldKeys.Count; i++)
		{
			var key = coldKeys[i];
			hash = ReplicatedStateFingerprint.CombineColdPayload(hash, key.Scope, key.Key, key.Kind, key.Role, ColdPayloads[key]);
		}

		return hash;
	}
}
