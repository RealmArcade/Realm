using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>loot collection, visibility and the signature folded over it.</summary>
public partial class AuthoritativeSnapshotBuilder
	{
	private void CollectLootItem(Entity entity, ref GroundLoot loot, ref Position position) => _lootItems.Add((entity, loot, position.Value));

	private void AppendLoot(SnapshotPeerState state, ref WorldSnapshot snapshot, bool isBaseline)
	{
		_lootItems.Clear();
		_world.Query(in LootItemsQuery, _collectLootItems);

		uint signature = 2166136261u;
		int visibleCount = 0;
		for (int index = 0; index < _lootItems.Count; index++)
		{
			(Entity entity, GroundLoot loot, Vector3 position) = _lootItems[index];
			if (!IsLootVisible(position)) continue;

			visibleCount++;
			signature = FoldLootSignature(signature, entity.Id, in loot, position);
		}

		signature ^= (uint)visibleCount;
		if (!isBaseline && state.HasSentLoot && state.SentLootSignature == signature) return;

		state.SentLootSignature = signature;
		state.HasSentLoot = true;

		var entries = new List<LootSnapshot>(visibleCount);
		for (int index = 0; index < _lootItems.Count; index++)
		{
			(Entity entity, GroundLoot loot, Vector3 position) = _lootItems[index];
			if (!IsLootVisible(position)) continue;

			entries.Add(new LootSnapshot
			{
				EntityId = entity.Id,
				ItemTypeId = loot.ItemTypeId,
				Charges = loot.Charges,
				Lifetime = loot.Lifetime,
				RemainingLifetime = loot.RemainingLifetime,
				PickupRadius = loot.PickupRadius,
				AutoPickup = loot.AutoPickup,
				OwnerPlayerIndex = loot.OwnerPlayerIndex,
				Position = new NetworkVector3(position.X, position.Y, position.Z)
			});
		}

		snapshot.Loot = entries;
	}

	private bool IsLootVisible(Vector3 position)
	{
		if (_context.ShroudDisabled || _viewer == Entity.Null) return true;

		for (int index = 0; index < _visionSources.Count; index++)
		{
			(Vector3 sourcePosition, float radius) = _visionSources[index];
			if (Vector3.DistanceSquared(sourcePosition, position) <= radius * radius) return true;
		}

		return false;
	}

	private static uint FoldLootSignature(uint hash, int entityId, in GroundLoot loot, Vector3 position)
	{
		hash = FoldLootValue(hash, entityId);
		hash = FoldLootValue(hash, loot.StringId);
		hash = FoldLootValue(hash, loot.Charges);
		hash = FoldLootValue(hash, BitConverter.SingleToInt32Bits(loot.Lifetime));
		hash = FoldLootValue(hash, BitConverter.SingleToInt32Bits(MathF.Round(loot.RemainingLifetime, 1)));
		hash = FoldLootValue(hash, BitConverter.SingleToInt32Bits(loot.PickupRadius));
		hash = FoldLootValue(hash, loot.AutoPickupByte);
		hash = FoldLootValue(hash, loot.OwnerPlayerIndex);
		hash = FoldLootValue(hash, BitConverter.SingleToInt32Bits(position.X));
		hash = FoldLootValue(hash, BitConverter.SingleToInt32Bits(position.Y));
		return FoldLootValue(hash, BitConverter.SingleToInt32Bits(position.Z));
	}

	private static uint FoldLootValue(uint hash, int value) => (hash ^ (uint)value) * 16777619u;
}
