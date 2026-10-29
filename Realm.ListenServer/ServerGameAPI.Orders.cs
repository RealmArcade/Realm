using System;
using System.Collections.Generic;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Abilities;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using Realm.Shared.Maps;
using Realm.Shared.Metadata;
using Realm.MapAPI;

namespace Realm.ListenServer;

public partial class ServerGameAPI
{
	private const int OwnedUnitsBufferCapacity = 4096;

	private static readonly QueryDescription ResourceNodeEntitiesQuery = new QueryDescription().WithAll<ResourceNode>();

	private readonly UnitId[] _ownedUnitsBuffer = new UnitId[OwnedUnitsBufferCapacity];
	private readonly ResourceNodeId[] _resourceNodeBuffer = new ResourceNodeId[OwnedUnitsBufferCapacity];
	private readonly EntityCallback<Owner, UnitOwnerPlayer> _collectOwnedUnits;
	private readonly EntityCallback<ResourceNode> _collectResourceNodes;
	private const int AnyPlayer = -1;

	private int _collectedCount;
	private int _requestedPlayerIndex = AnyPlayer;
	private Vector3 _radiusCenter;
	private float _radiusSquared;

	public ReadOnlySpan<UnitId> GetUnitsOwnedByPlayer(int playerIndex) => CollectWorldUnits(playerIndex);

	public ReadOnlySpan<UnitId> GetAllUnits() => CollectWorldUnits(AnyPlayer);

	public ReadOnlySpan<UnitId> GetUnitsInRadius(Vector3 center, float radius)
	{
		ReadOnlySpan<UnitId> units = CollectWorldUnits(AnyPlayer);
		if (units.Length == 0) return units;

		_radiusCenter = center;
		_radiusSquared = radius * radius;

		int kept = 0;
		for (int index = 0; index < units.Length; index++)
		{
			if (!IsWithinRadius(units[index])) continue;

			_ownedUnitsBuffer[kept++] = units[index];
		}

		return new ReadOnlySpan<UnitId>(_ownedUnitsBuffer, 0, kept);
	}

	private ReadOnlySpan<UnitId> CollectWorldUnits(int playerIndex)
	{
		_collectedCount = 0;
		_requestedPlayerIndex = playerIndex;
		_context.World.Query(in PlayerUnitsQuery, _collectOwnedUnits);

		if (_collectedCount == OwnedUnitsBufferCapacity)
	{
			_log.Write($"the world holds more than {OwnedUnitsBufferCapacity} units, so a script asking for units saw the first {OwnedUnitsBufferCapacity}");
		}

		return new ReadOnlySpan<UnitId>(_ownedUnitsBuffer, 0, _collectedCount);
	}

	private bool IsWithinRadius(UnitId unit)
	{
		var entity = new Entity(unit.Value);
		if (!_context.World.TryGet<Position>(entity, out Position position)) return false;

		Vector3 offset = position.Value - _radiusCenter;
		return offset.LengthSquared() <= _radiusSquared;
	}

	public void IssueMoveOrder(UnitId unit, Vector3 destination)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		UnitOrderService.MoveTo(_context.World, entity, destination);
	}

	public void IssueAttackMoveOrder(UnitId unit, Vector3 destination)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		UnitOrderService.AttackMove(_context.World, entity, destination);
	}

	public void IssueAttackMoveOrderToPlayer(int playerIndex, Vector3 destination)
	{
		ReadOnlySpan<UnitId> units = GetUnitsOwnedByPlayer(playerIndex);
		for (int index = 0; index < units.Length; index++)
		{
			Entity entity = ResolveUnit(units[index]);
			if (entity == Entity.Null) continue;
			if (_context.World.Has<Building>(entity)) continue;

			UnitOrderService.AttackMove(_context.World, entity, destination);
		}
	}

	public void IssueAttackOrder(UnitId unit, UnitId target)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		Entity targetEntity = ResolveUnit(target);
		if (targetEntity == Entity.Null) return;

		UnitOrderService.Attack(_context.World, entity, targetEntity);
	}

	public void IssueGatherOrder(UnitId unit, ResourceNodeId resourceNode)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		Entity node = ResolveResourceNode(resourceNode);
		if (node == Entity.Null) return;

		UnitOrderService.Gather(_context.World, entity, node, ReadResourceNodeType(node));
	}

	public void IssueStopOrder(UnitId unit)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		UnitOrderService.Stop(_context.World, entity);
	}

	public void IssueHoldPositionOrder(UnitId unit)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		UnitOrderService.HoldPosition(_context.World, entity);
	}

	public int GetUnitRouteState(UnitId unit)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return 0;

		return UnitOrderService.GetRouteState(_context.World, entity);
	}

	public void SetUnitRouteState(UnitId unit, int state)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		UnitOrderService.SetRouteState(_context.World, entity, state);
	}

	public void AddBuff(UnitId unit, string buffId, float duration)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		UnitOrderService.ApplyBuff(_context.World, entity, buffId, duration);
	}

	public void RemoveBuff(UnitId unit, string buffId)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		UnitOrderService.RemoveBuff(_context.World, entity, buffId);
	}

	public bool UnitHasBuff(UnitId unit, string buffId)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return false;

		return UnitOrderService.HasBuff(_context.World, entity, buffId);
	}

	public void RegisterBuffModifier(string buffId, string statName, bool isPercentage, float value)
	{
		if (string.IsNullOrEmpty(buffId) || string.IsNullOrWhiteSpace(statName)) return;

		StatKeys.WarnIfUnknown($"buff '{buffId}'", statName);
		if (!BuffRegistry.BuffModifiers.TryGetValue(buffId, out List<StatModifier>? modifiers))
		{
			modifiers = new List<StatModifier>();
			BuffRegistry.BuffModifiers[buffId] = modifiers;
		}

		modifiers.Add(new StatModifier(new StatId(statName), isPercentage ? ModifierType.Percentage : ModifierType.Flat, value));
	}

	public void StunUnit(UnitId unit, float duration)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		UnitOrderService.ApplyBuff(_context.World, entity, "stun", duration);
		UnitOrderService.Stop(_context.World, entity);
	}

	public void SilenceUnit(UnitId unit, float duration)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		UnitOrderService.ApplyBuff(_context.World, entity, "silence", duration);
	}

	public IEnumerable<string> GetModifiers(UnitId unit)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return Array.Empty<string>();
		if (!_context.World.Has<ModifierState>(entity)) return Array.Empty<string>();

		ref ModifierState modifierState = ref _context.World.Get<ModifierState>(entity);
		var described = new List<string>(modifierState.Count);
		for (int index = 0; index < modifierState.Count; index++)
		{
			StatModifier modifier = modifierState.Get(index);
			described.Add($"{modifier.StatTypeId.Value}: {(modifier.Value >= 0f ? "+" : "")}{modifier.Value} ({modifier.Type})");
		}

		return described;
	}

	public void AddStatModifier(UnitId unit, string statName, bool isPercentage, float value, string sourceId)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		StatKeys.WarnIfUnknown($"script '{sourceId}'", statName);
		UnitOrderService.AddStatModifier(_context.World, entity, statName, isPercentage, value, sourceId);
	}

	public void RemoveStatModifiers(UnitId unit, string sourceId)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		UnitOrderService.RemoveStatModifiers(_context.World, entity, sourceId);
	}

	public void SetUnitCustomData(UnitId unit, string key, string value)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		UnitOrderService.SetCustomData(_context.World, entity, key, value);
	}

	public string? GetUnitCustomData(UnitId unit, string key)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return null;

		return UnitOrderService.TryGetCustomData(_context.World, entity, key, out object? value) ? value?.ToString() : null;
	}

	public bool RemoveUnitCustomData(UnitId unit, string key)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return false;

		return UnitOrderService.RemoveCustomData(_context.World, entity, key);
	}

	public bool UnitHasCustomData(UnitId unit, string key)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return false;

		return UnitOrderService.HasCustomData(_context.World, entity, key);
	}

	public float GetAbilityCooldown(UnitId unit, string abilityId)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return 0f;

		return UnitOrderService.GetAbilityCooldownRemaining(_context.World, entity, abilityId);
	}

	public void SetAbilityCooldown(UnitId unit, string abilityId, float cooldown)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		UnitOrderService.SetAbilityCooldown(_context.World, entity, abilityId, cooldown);
	}

	public void SetAbilityAutoCast(UnitId unit, string abilityId, bool active)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		int slotIndex = FindAbilitySlot(entity, abilityId);
		uint bit = slotIndex >= 0 && slotIndex < 32 ? 1u << slotIndex : 1u;

		if (_context.World.Has<AutoCastState>(entity))
		{
			ref AutoCastState autoCast = ref _context.World.Get<AutoCastState>(entity);
			if (active) autoCast.ActiveAbilityBitmask |= bit;
			else autoCast.ActiveAbilityBitmask &= ~bit;
			autoCast.Enabled = autoCast.ActiveAbilityBitmask != 0;
			return;
		}

		if (active) _context.World.Add(entity, new AutoCastState(true, bit));
	}

	public void SetUnitName(UnitId unit, string value)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		_context.World.SetOrAdd(entity, new Name(value ?? string.Empty));
	}

	public void SetUnitAnimationsFrozen(UnitId unit, bool value) => SetUnitFlag<AnimationsFrozen>(unit, value);

	public void SetUnitHideHealthAndManaBars(UnitId unit, bool value) => SetUnitFlag<OverheadBarsHidden>(unit, value);

	public void DealTypedDamage(UnitId attacker, UnitId target, float damage, string damageType)
	{
		Entity targetEntity = ResolveUnit(target);
		if (targetEntity == Entity.Null) return;

		_context.CombatDamageService.DealResolvedDamage(ResolveUnit(attacker), targetEntity, damage, damageType);
	}

	public void SpawnResourceNode(string resourceType, Vector3 position, float amount)
	{
		if (string.IsNullOrEmpty(resourceType)) return;

		float groundHeight = ReadTerrainHeight(position);
		var placed = new Vector3(position.X, groundHeight, position.Z);
		PlaceableMetadata? metadata = _map.Catalog.FindResource(resourceType) ?? (PlaceableMetadata?)_map.Catalog.FindProp(resourceType);
		float collisionRadius = metadata != null ? PropEntityFactory.ResolveCollisionRadius(metadata) : 0f;
		Entity node = PropEntityFactory.CreateCore(_context.World, resourceType, placed, 0f, 1f, collisionRadius);
		PropEntityFactory.AddResourceNode(_context.World, node, metadata: null, savedAmount: amount);
		_log.Write($"spawned a '{resourceType}' node with {amount} in it");
	}

	public ResourceNodeId GetResourceNode(int index)
	{
		ReadOnlySpan<ResourceNodeId> nodes = GetResourceNodes();
		return index >= 0 && index < nodes.Length ? nodes[index] : ResourceNodeId.Invalid;
	}

	public ReadOnlySpan<ResourceNodeId> GetResourceNodes()
	{
		_collectedCount = 0;
		_context.World.Query(in ResourceNodeEntitiesQuery, _collectResourceNodes);
		return new ReadOnlySpan<ResourceNodeId>(_resourceNodeBuffer, 0, _collectedCount);
	}

	public string GetResourceNodeType(ResourceNodeId node)
	{
		Entity entity = ResolveResourceNode(node);
		if (entity == Entity.Null) return string.Empty;

		return ReadResourceNodeType(entity);
	}

	public Vector3 GetResourceNodePosition(ResourceNodeId node)
	{
		Entity entity = ResolveResourceNode(node);
		if (entity == Entity.Null) return Vector3.Zero;
		if (!_context.World.TryGet<Position>(entity, out Position position)) return Vector3.Zero;

		return position.Value;
	}

	public float GetResourceNodeAmount(ResourceNodeId node)
	{
		Entity entity = ResolveResourceNode(node);
		if (entity == Entity.Null) return 0f;

		return _context.World.Get<ResourceNode>(entity).Amount;
	}

	public void SetResourceNodeAmount(ResourceNodeId node, float value)
	{
		Entity entity = ResolveResourceNode(node);
		if (entity == Entity.Null) return;

		ref ResourceNode resourceNode = ref _context.World.Get<ResourceNode>(entity);
		resourceNode.Amount = Math.Max(0f, value);
	}

	public bool IsResourceNodeDepleted(ResourceNodeId node)
	{
		Entity entity = ResolveResourceNode(node);
		if (entity == Entity.Null) return true;

		return _context.World.Get<ResourceNode>(entity).Amount <= 0f;
	}

	private Entity ResolveResourceNode(ResourceNodeId node)
	{
		if (!node.IsValid) return Entity.Null;

		var entity = new Entity(node.Value);
		if (!_context.World.IsAlive(entity)) return Entity.Null;
		return _context.World.Has<ResourceNode>(entity) ? entity : Entity.Null;
	}

	private string ReadResourceNodeType(Entity node)
	{
		string propId = _context.World.TryGet<PropIdentity>(node, out PropIdentity identity) ? identity.PropId : string.Empty;
		if (string.IsNullOrEmpty(propId)) return string.Empty;

		string? declared = _context.MapRules.GetResourceTypeForNode(propId);
		return string.IsNullOrEmpty(declared) ? propId : declared;
	}

	private int FindAbilitySlot(Entity entity, string abilityId)
	{
		if (string.IsNullOrEmpty(abilityId)) return -1;
		if (!_context.World.TryGet<AbilityState>(entity, out AbilityState abilities)) return -1;

		for (int index = 0; index < abilities.Count; index++)
		{
			if (string.Equals(abilities.Get(index), abilityId, StringComparison.OrdinalIgnoreCase)) return index;
		}

		return -1;
	}

	private void CollectOwnedUnits(Entity entity, ref Owner owner, ref UnitOwnerPlayer unitOwner)
	{
		if (_requestedPlayerIndex != AnyPlayer && unitOwner.Value != _requestedPlayerIndex) return;
		if (_collectedCount >= OwnedUnitsBufferCapacity) return;

		_ownedUnitsBuffer[_collectedCount++] = new UnitId(entity.Id);
	}

	private void CollectResourceNodes(Entity entity, ref ResourceNode node)
	{
		if (_collectedCount >= OwnedUnitsBufferCapacity) return;

		_resourceNodeBuffer[_collectedCount++] = new ResourceNodeId(entity.Id);
	}
}
