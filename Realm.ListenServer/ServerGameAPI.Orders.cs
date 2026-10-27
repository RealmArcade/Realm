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

	/// <summary>
	///     The units a player owns, read from the authority's own world. The buffer belongs to this surface and is
	///     refilled per call, so a script can walk a player's army without making the authority allocate.
	/// </summary>
	public ReadOnlySpan<UnitId> GetUnitsOwnedByPlayer(int playerIndex) => CollectWorldUnits(playerIndex);

	/// <summary>Every unit in the match, in entity order.</summary>
	public ReadOnlySpan<UnitId> GetAllUnits() => CollectWorldUnits(AnyPlayer);

	/// <summary>The units within a radius of a place, which is how a script asks what an effect caught.</summary>
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

	/// <summary>
	///     Refills this surface's unit buffer from the world. A player index limits it to that player's units; the
	///     authority's own "any player" value walks every unit in the match.
	/// </summary>
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

	/// <summary>Sends a unit to a place, which is the order a script gives most often.</summary>
	public void IssueMoveOrder(UnitId unit, Vector3 destination)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		UnitOrderService.MoveTo(_context.World, entity, destination);
	}

	/// <summary>Sends a unit to a place while it fights whatever it meets on the way.</summary>
	public void IssueAttackMoveOrder(UnitId unit, Vector3 destination)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		UnitOrderService.AttackMove(_context.World, entity, destination);
	}

	/// <summary>Sends everything a player owns across the map on the warpath.</summary>
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

	/// <summary>Points a unit's attack at another unit.</summary>
	public void IssueAttackOrder(UnitId unit, UnitId target)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		Entity targetEntity = ResolveUnit(target);
		if (targetEntity == Entity.Null) return;

		UnitOrderService.Attack(_context.World, entity, targetEntity);
	}

	/// <summary>Sends a worker to a resource node, carrying the resource type the node harvests.</summary>
	public void IssueGatherOrder(UnitId unit, ResourceNodeId resourceNode)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		Entity node = ResolveResourceNode(resourceNode);
		if (node == Entity.Null) return;

		UnitOrderService.Gather(_context.World, entity, node, ReadResourceNodeType(node));
	}

	/// <summary>Stops a unit where it stands.</summary>
	public void IssueStopOrder(UnitId unit)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		UnitOrderService.Stop(_context.World, entity);
	}

	/// <summary>Stops a unit and tells it to hold its ground instead of chasing.</summary>
	public void IssueHoldPositionOrder(UnitId unit)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		UnitOrderService.HoldPosition(_context.World, entity);
	}

	/// <summary>The route a script put a unit on, or zero when it is on none.</summary>
	public int GetUnitRouteState(UnitId unit)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return 0;

		return UnitOrderService.GetRouteState(_context.World, entity);
	}

	/// <summary>Puts a unit on a route a map script tracks itself.</summary>
	public void SetUnitRouteState(UnitId unit, int state)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		UnitOrderService.SetRouteState(_context.World, entity, state);
	}

	/// <summary>
	///     Puts a timed status on a unit. The status lands in the replicated tables the match advances, so every client
	///     sees the same status running for the same time.
	/// </summary>
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

	/// <summary>Declares which stats a status changes while it lasts, which is what makes the status do anything.</summary>
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

	/// <summary>Takes a unit out of action for a while and stops it where it stands.</summary>
	public void StunUnit(UnitId unit, float duration)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		UnitOrderService.ApplyBuff(_context.World, entity, "stun", duration);
		UnitOrderService.Stop(_context.World, entity);
	}

	/// <summary>Keeps a unit from casting for a while.</summary>
	public void SilenceUnit(UnitId unit, float duration)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		UnitOrderService.ApplyBuff(_context.World, entity, "silence", duration);
	}

	/// <summary>The stat changes currently shaping a unit, as a script reads them back.</summary>
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

	/// <summary>Changes one of a unit's stats for as long as the source that granted it keeps it.</summary>
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

	/// <summary>How long a unit must wait before an ability is ready again.</summary>
	public float GetAbilityCooldown(UnitId unit, string abilityId)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return 0f;

		return UnitOrderService.GetAbilityCooldownRemaining(_context.World, entity, abilityId);
	}

	/// <summary>Puts an ability on cooldown for a unit.</summary>
	public void SetAbilityCooldown(UnitId unit, string abilityId, float cooldown)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		UnitOrderService.SetAbilityCooldown(_context.World, entity, abilityId, cooldown);
	}

	/// <summary>
	///     Turns one of a unit's abilities on or off as an automatic cast. The ability's own slot decides which bit of
	///     the unit's auto-cast mask it owns, so the client's ability bar and the authority agree on what is armed.
	/// </summary>
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

	/// <summary>Names a unit, which is what its own tooltip and its selection panel show.</summary>
	public void SetUnitName(UnitId unit, string value)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		_context.World.SetOrAdd(entity, new Name(value ?? string.Empty));
	}

	/// <summary>Stops a unit animating without stopping it taking orders.</summary>
	public void SetUnitAnimationsFrozen(UnitId unit, bool value) => SetUnitFlag<AnimationsFrozen>(unit, value);

	/// <summary>Hides the health and mana bars a unit floats over its head.</summary>
	public void SetUnitHideHealthAndManaBars(UnitId unit, bool value) => SetUnitFlag<OverheadBarsHidden>(unit, value);

	/// <summary>
	///     Deals damage of a named type through the match's own resolver, so armor, penetration and the damage matrix
	///     resolve the same way a weapon's damage does.
	/// </summary>
	public void DealTypedDamage(UnitId attacker, UnitId target, float damage, string damageType)
	{
		Entity targetEntity = ResolveUnit(target);
		if (targetEntity == Entity.Null) return;

		_context.CombatDamageService.DealResolvedDamage(ResolveUnit(attacker), targetEntity, damage, damageType);
	}

	/// <summary>Puts a harvestable node in the world where the map asked for one.</summary>
	public void SpawnResourceNode(string resourceType, Vector3 position, float amount)
	{
		if (string.IsNullOrEmpty(resourceType)) return;

		float groundHeight = ReadTerrainHeight(position);
		var placed = new Vector3(position.X, groundHeight, position.Z);
		Entity node = PropEntityFactory.CreateCore(_context.World, resourceType, placed, 0f, 1f, 1.5f);
		PropEntityFactory.AddResourceNode(_context.World, node, metadata: null, savedAmount: amount);
		_log.Write($"spawned a '{resourceType}' node with {amount} in it");
	}

	/// <summary>The node at one position in the authority's own node order, which is the order a script enumerates.</summary>
	public ResourceNodeId GetResourceNode(int index)
	{
		ReadOnlySpan<ResourceNodeId> nodes = GetResourceNodes();
		return index >= 0 && index < nodes.Length ? nodes[index] : ResourceNodeId.Invalid;
	}

	/// <summary>Every harvestable node the authority still has, in entity order.</summary>
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

	/// <summary>The entity a script's node handle names, or nothing when that node is gone.</summary>
	private Entity ResolveResourceNode(ResourceNodeId node)
	{
		if (!node.IsValid) return Entity.Null;

		var entity = new Entity(node.Value);
		if (!_context.World.IsAlive(entity)) return Entity.Null;
		return _context.World.Has<ResourceNode>(entity) ? entity : Entity.Null;
	}

	/// <summary>
	///     What a node harvests. A map declares it per node definition; a node the map declares nothing for harvests
	///     under its own name, which is what a script that spawns one by type means.
	/// </summary>
	private string ReadResourceNodeType(Entity node)
	{
		string propId = _context.World.TryGet<PropIdentity>(node, out PropIdentity identity) ? identity.PropId : string.Empty;
		if (string.IsNullOrEmpty(propId)) return string.Empty;

		string? declared = _context.MapRules.GetResourceTypeForNode(propId);
		return string.IsNullOrEmpty(declared) ? propId : declared;
	}

	/// <summary>Which slot of a unit's ability list an ability occupies, which is the bit its auto-cast uses.</summary>
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
