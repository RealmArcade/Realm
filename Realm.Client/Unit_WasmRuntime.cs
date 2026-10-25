using System;
using System.Collections.Generic;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.Utils;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using Realm.MapAPI;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client;

public class Unit_WasmRuntime : IEcsEntityWrapper
{
	private readonly Entity _entity;
	private readonly EcsWorld _world;
	public bool HideHealthAndManaBars
	{
		get => _world.IsAlive(_entity) && _world.Has<OverheadBarsHidden>(_entity);
		set
		{
			if (!_world.IsAlive(_entity)) return;

			if (value)
			{
				if (!_world.Has<OverheadBarsHidden>(_entity))
				{
					_world.Add<OverheadBarsHidden>(_entity);
				}
			}
			else if (_world.Has<OverheadBarsHidden>(_entity))
			{
				_world.Remove<OverheadBarsHidden>(_entity);
			}
		}
	}

	public Unit_WasmRuntime(Entity entity, EcsWorld world)
	{
		_entity = entity;
		_world = world;
	}

	Entity IEcsEntityWrapper.Entity => _entity;
	public Entity Entity => _entity;
	public static implicit operator Realm.MapAPI.UnitId(Unit_WasmRuntime? runtime) => runtime != null ? (Realm.MapAPI.UnitId)runtime.UniqueId : Realm.MapAPI.UnitId.Invalid;

	public int UniqueId => _entity.Id;

	public override bool Equals(object? obj)
	{
		return obj is Unit_WasmRuntime other && _entity == other._entity;
	}

	public override int GetHashCode()
	{
		return _entity.GetHashCode();
	}

	public string TemplateID
	{
		get
		{
			if (!_world.IsAlive(_entity)) return string.Empty;
			if (_world.Has<DefinitionId>(_entity))
				return _world.Get<DefinitionId>(_entity).Value;
			return string.Empty;
		}
	}

	public string UnitId => TemplateID;

	public string Name
	{
		get
		{
			if (!_world.IsAlive(_entity)) return string.Empty;
			if (_world.Has<Name>(_entity))
			{
				return _world.Get<Name>(_entity).Value;
			}

			return string.Empty;
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			var nameComp = new Name(value);
			_world.SetOrAdd(_entity, nameComp);
		}
	}

	public int Player
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 0;
			if (_world.Has<UnitOwnerPlayer>(_entity))
				return _world.Get<UnitOwnerPlayer>(_entity).PlayerIndex;
			return 0;
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			_world.SetOrAdd(_entity, new UnitOwnerPlayer(value));

			if (_world.Has<Owner>(_entity) && GameContext.Host != null)
			{
				var playerOwner = GameContext.Host.GetPlayerEntityForPlayerIndex(value).AsPlayerEntity(_world);
				_world.Set(_entity, new Owner(playerOwner));
			}

			if (GameHost.TryGetUnit3D(_entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
			{
				unit3D.Player = value;
			}
		}
	}

	public bool IsBuilding
	{
		get
		{
			if (!_world.IsAlive(_entity)) return false;
			return _world.Has<Building>(_entity);
		}
	}

	public Vector3 Position
	{
		get
		{
			if (!_world.IsAlive(_entity)) return Vector3.Zero;
			if (_world.Has<Position>(_entity))
			{
				var pos = _world.Get<Position>(_entity).Value;
				return new Vector3(pos.X, pos.Y, pos.Z);
			}

			return Vector3.Zero;
		}
	}

	public float Health
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 0f;
			if (_world.Has<Health>(_entity))
			{
				return _world.Get<Health>(_entity).Current;
			}

			return 0f;
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			if (_world.Has<Health>(_entity))
			{
				var hp = _world.Get<Health>(_entity);
				float finalHp = Math.Max(0f, value);
				_world.Set(_entity, new Health(finalHp, hp.Max));
				if (GameHost.TryGetUnit3D(_entity, out var u3d))
				{
					u3d.SetDeferred("current_hp", finalHp);
				}

				if (finalHp <= 0f)
				{
					if (!_world.Has<Dead>(_entity))
					{
						_world.Add(_entity, new Dead());
						if (GameHost.TryGetUnit3D(_entity, out var deadU3D))
						{
							GameContext.Host.TriggerKillUnit(deadU3D);
						}
					}
				}
			}
		}
	}

	public float MaxHealth
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 0f;
			if (_world.Has<Health>(_entity))
			{
				return _world.Get<Health>(_entity).Max;
			}

			return 0f;
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			if (_world.Has<Health>(_entity))
			{
				var hp = _world.Get<Health>(_entity);
				float finalMax = Math.Max(1f, value);
				float finalCur = Math.Min(hp.Current, finalMax);
				_world.Set(_entity, new Health(finalCur, finalMax));
				if (GameHost.TryGetUnit3D(_entity, out var u3d))
				{
					u3d.SetDeferred("max_hp", finalMax);
					u3d.SetDeferred("current_hp", finalCur);
				}
			}
		}
	}

	public float Damage
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 0f;
			var statService = ECSContext.StatService;
			if (statService != null)
			{
				float actualDamage = statService.GetStatValue(_entity, new StatId("Attack"));
				if (actualDamage > 0f) return actualDamage;
			}

			if (_world.Has<Attack>(_entity))
			{
				return _world.Get<Attack>(_entity).Damage;
			}

			return 0f;
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			if (_world.Has<Attack>(_entity))
			{
				var atk = _world.Get<Attack>(_entity);
				_world.Set(_entity, new Attack(value, atk.Range, atk.Cooldown));
			}

			if (_world.Has<Stats>(_entity))
			{
				ref var stats = ref _world.Get<Stats>(_entity);
				stats.Set(new StatId("Attack"), value);
			}
		}
	}

	public float Range
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 0f;
			if (_world.Has<Attack>(_entity))
			{
				return _world.Get<Attack>(_entity).Range;
			}

			return 0f;
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			if (_world.Has<Attack>(_entity))
			{
				var atk = _world.Get<Attack>(_entity);
				_world.Set(_entity, new Attack(atk.Damage, value, atk.Cooldown));
			}
		}
	}

	public float Armor
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 0f;
			var statService = ECSContext.StatService;
			if (statService != null)
			{
				float actualArmor = statService.GetStatValue(_entity, new StatId("Armor"));
				if (actualArmor > 0f) return actualArmor;
			}

			if (_world.Has<Resistances>(_entity))
			{
				return _world.Get<Resistances>(_entity).Physical;
			}

			return 0f;
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			if (_world.Has<Resistances>(_entity))
			{
				ref var resistances = ref _world.Get<Resistances>(_entity);
				_world.Set(_entity, new Resistances(value, resistances.Magic));
			}

			if (_world.Has<Stats>(_entity))
			{
				ref var stats = ref _world.Get<Stats>(_entity);
				stats.Set(new StatId("Armor"), value);
			}
		}
	}

	public float Speed
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 0f;
			var statService = ECSContext.StatService;
			if (statService != null)
			{
				float actualSpeed = statService.GetStatValue(_entity, new StatId("MovementSpeed"));
				if (actualSpeed > 0f) return actualSpeed;
			}

			if (_world.Has<MovementStats>(_entity))
			{
				return _world.Get<MovementStats>(_entity).Speed;
			}

			return 0f;
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			if (_world.Has<MovementStats>(_entity))
			{
				var mv = _world.Get<MovementStats>(_entity);
				_world.Set(_entity, new MovementStats(value, mv.Acceleration, mv.TurnRate));
			}

			if (_world.Has<Stats>(_entity))
			{
				ref var stats = ref _world.Get<Stats>(_entity);
				stats.Set(new StatId("MovementSpeed"), value);
			}
		}
	}

	public bool IsHero
	{
		get
		{
			if (!_world.IsAlive(_entity)) return false;
			return _world.Has<Hero>(_entity);
		}
	}

	public int Level
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 1;
			if (_world.Has<Level>(_entity))
			{
				return _world.Get<Level>(_entity).Value;
			}

			return 1;
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			var levelComp = new Level(value);
			_world.SetOrAdd(_entity, levelComp);
		}
	}

	public float Experience
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 0f;
			if (_world.Has<Experience>(_entity))
			{
				return _world.Get<Experience>(_entity).Value;
			}

			return 0f;
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			var expComp = new Experience(value);
			_world.SetOrAdd(_entity, expComp);
		}
	}

	public float XpBounty
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 0f;
			if (GameHost.TryGetUnitOrBuildingMetadata(UnitId, out var meta))
			{
				return meta.XpBounty;
			}

			return 0f;
		}
	}

	public float GoldBounty
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 0f;
			if (GameHost.TryGetUnitOrBuildingMetadata(UnitId, out var meta))
			{
				return meta.GoldBounty;
			}

			return 0f;
		}
	}

	public bool IsDead
	{
		get
		{
			if (!_world.IsAlive(_entity)) return true;
			return _world.Has<Dead>(_entity);
		}
	}

	public void MoveTo(Vector3 destination)
	{
		if (!_world.IsAlive(_entity) || IsDead) return;
		if (_world.Has<HoldPosition>(_entity))
		{
			_world.Remove<HoldPosition>(_entity);
		}

		if (_world.Has<AttackMove>(_entity))
		{
			_world.Remove<AttackMove>(_entity);
		}

		if (_world.Has<AttackTarget>(_entity))
		{
			_world.Remove<AttackTarget>(_entity);
		}

		var mv = new MoveTo(destination);
		_world.SetOrAdd(_entity, mv);
	}

	public void AttackMove(Vector3 destination)
	{
		if (!_world.IsAlive(_entity) || IsDead) return;
		if (_world.Has<HoldPosition>(_entity))
		{
			_world.Remove<HoldPosition>(_entity);
		}

		if (_world.Has<AttackTarget>(_entity))
		{
			_world.Remove<AttackTarget>(_entity);
		}

		var am = new AttackMove(destination);
		_world.SetOrAdd(_entity, am);
		var mv = new MoveTo(destination);
		_world.SetOrAdd(_entity, mv);
	}

	public void Attack(UnitId target)
	{
		if (!_world.IsAlive(_entity) || IsDead || !target.IsValid) return;
		if (GameContext.SimulationService.TryGetEntity(target, out var targetEntity) && _world.IsAlive(targetEntity))
		{
			var at = new AttackTarget(targetEntity);
			_world.SetOrAdd(_entity, at);
		}
	}

	public void Gather(ResourceNodeId resourceNode)
	{
		if (!_world.IsAlive(_entity) || IsDead || !resourceNode.IsValid) return;
		if (GameContext.Host != null && GameContext.Host.TryGetResourceProp(resourceNode, out var propNode) && GodotObject.IsInstanceValid(propNode))
		{
			var gatherer = new Gatherer(propNode.PropId, propNode.Entity);
			_world.SetOrAdd(_entity, gatherer);
		}
	}

	public void Teleport(Vector3 position)
	{
		if (!_world.IsAlive(_entity)) return;

		var newPosComp = new Position(position);
		_world.SetOrAdd(_entity, newPosComp);
		_world.SetOrAdd(_entity, new PreviousPosition(position));

		if (GameHost.TryGetUnit3D(_entity, out var unit3D))
		{
			if (GodotObject.IsInstanceValid(unit3D))
			{
				unit3D.GlobalPosition = new Godot.Vector3(position.X, position.Y, position.Z);
			}
		}
	}

	public float Mana
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 0f;
			if (_world.Has<Mana>(_entity))
			{
				return _world.Get<Mana>(_entity).Current;
			}

			return 0f;
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			float max = MaxMana;
			var val = Math.Max(0f, value);
			_world.SetOrAdd(_entity, new Mana(val, max));
		}
	}

	public float MaxMana
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 0f;
			if (_world.Has<Mana>(_entity))
			{
				return _world.Get<Mana>(_entity).Max;
			}

			return 0f;
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			float current = Mana;
			var val = Math.Max(0f, value);
			_world.SetOrAdd(_entity, new Mana(current, val));
		}
	}

	public float AttackSpeed
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 1f;
			if (_world.Has<AttackSpeedMultiplier>(_entity))
			{
				return _world.Get<AttackSpeedMultiplier>(_entity).Multiplier;
			}

			return 1f;
		}
		set
		{
			if (!_world.IsAlive(_entity) || value <= 0f) return;
			_world.SetOrAdd(_entity, new AttackSpeedMultiplier(value));
		}
	}

	public float ManaRegen
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 0f;
			if (_world.Has<ManaRegen>(_entity))
			{
				return _world.Get<ManaRegen>(_entity).PerSecond;
			}

			return 0f;
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			_world.SetOrAdd(_entity, new ManaRegen(Math.Max(0f, value)));
		}
	}

	public float Scale
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 1.0f;
			if (_world.Has<ModelScale>(_entity))
			{
				return _world.Get<ModelScale>(_entity).Value;
			}

			if (GameHost.TryGetUnit3D(_entity, out var u3d))
			{
				if (GodotObject.IsInstanceValid(u3d))
				{
					return u3d.Scale.X;
				}
			}

			return 1.0f;
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			_world.SetOrAdd(_entity, new ModelScale(value));
			if (GameHost.TryGetUnit3D(_entity, out var u3d))
			{
				if (GodotObject.IsInstanceValid(u3d))
				{
					float safeScale = Mathf.Max(0.001f, value);
					u3d.Scale = new Godot.Vector3(safeScale, safeScale, safeScale);
				}
			}
		}
	}

	public bool Invulnerable
	{
		get
		{
			if (!_world.IsAlive(_entity)) return false;
			return _world.Has<Invulnerable>(_entity);
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			if (value)
			{
				if (!_world.Has<Invulnerable>(_entity))
				{
					_world.Add<Invulnerable>(_entity);
				}
			}
			else
			{
				if (_world.Has<Invulnerable>(_entity))
				{
					_world.Remove<Invulnerable>(_entity);
				}
			}
		}
	}

	public bool AnimationsFrozen
	{
		get
		{
			if (!_world.IsAlive(_entity)) return false;
			return _world.Has<AnimationsFrozen>(_entity);
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			bool isFrozen = _world.Has<AnimationsFrozen>(_entity);
			if (value == isFrozen) return;

			if (value)
			{
				_world.Add<AnimationsFrozen>(_entity);
				if (GameHost.TryGetUnit3D(_entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
				{
					unit3D.SetAnimationsFrozen(true);
				}
			}
			else
			{
				_world.Remove<AnimationsFrozen>(_entity);
				if (GameHost.TryGetUnit3D(_entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
				{
					unit3D.SetAnimationsFrozen(false);
				}
			}
		}
	}

	public bool CommandsFrozen
	{
		get
		{
			if (!_world.IsAlive(_entity)) return false;
			return _world.Has<CommandsFrozen>(_entity);
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			bool isFrozen = _world.Has<CommandsFrozen>(_entity);
			if (value == isFrozen) return;

			if (value)
			{
				_world.Add<CommandsFrozen>(_entity);
				Stop();
				if (GameHost.TryGetUnit3D(_entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
				{
					unit3D.Velocity = Godot.Vector3.Zero;
				}
			}
			else
			{
				_world.Remove<CommandsFrozen>(_entity);
			}
		}
	}

	public bool Frozen
	{
		get
		{
			if (!_world.IsAlive(_entity)) return false;
			return _world.Has<CommandsFrozen>(_entity) || _world.Has<AnimationsFrozen>(_entity);
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			AnimationsFrozen = value;
			CommandsFrozen = value;
		}
	}

	public float Facing
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 0f;
			if (GameHost.TryGetUnit3D(_entity, out var u3d) && GodotObject.IsInstanceValid(u3d))
			{
				return u3d.Rotation.Y;
			}
			return 0f;
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			((IGameAPI?)GameContext.Host)?.SetUnitFacing((Realm.MapAPI.UnitId)_entity.Id, value);
		}
	}

	public void Stop()
	{
		if (!_world.IsAlive(_entity)) return;
		GameContext.Host.ClearUnitOrders(_entity);
		if (GameHost.TryGetUnit3D(_entity, out var u3d))
		{
			if (GodotObject.IsInstanceValid(u3d))
			{
				u3d.Velocity = Godot.Vector3.Zero;
			}
		}
	}

	public void HoldPosition()
	{
		if (!_world.IsAlive(_entity)) return;
		Stop();
		if (!_world.Has<HoldPosition>(_entity))
		{
			_world.Add<HoldPosition>(_entity);
		}
	}

	public void Stun(float duration)
	{
		AddBuff("stun", duration);
		Stop();
	}

	public void Silence(float duration)
	{
		AddBuff("silence", duration);
	}

	public bool AddItem(string itemId)
	{
		if (!_world.IsAlive(_entity) || string.IsNullOrWhiteSpace(itemId)) return false;
		if (_world.Has<Inventory>(_entity) && _world.Get<Inventory>(_entity).Count >= 6 && !HasItem(itemId)) return false;
		bool success = ItemGrantingUtility.EquipItem(_world, _entity, itemId, 1);
		if (success)
		{
			NotifyUiInventoryChanged();
		}
		return success;
	}

	public bool AddItem(string itemId, int charges)
	{
		if (!_world.IsAlive(_entity) || string.IsNullOrWhiteSpace(itemId)) return false;
		if (_world.Has<Inventory>(_entity) && _world.Get<Inventory>(_entity).Count >= 6 && !HasItem(itemId)) return false;
		bool success = ItemGrantingUtility.EquipItem(_world, _entity, itemId, charges);
		if (success)
		{
			NotifyUiInventoryChanged();
		}
		return success;
	}

	public bool CollectLootItem(LootItemId lootItem)
	{
		if (!lootItem.IsValid) return false;
		return GameContext.Host?.CollectLootItem(lootItem.Value, (UnitId)_entity.Id) ?? false;
	}

	public int GetItemCharges(string itemId)
	{
		if (!_world.IsAlive(_entity) || string.IsNullOrWhiteSpace(itemId)) return 0;
		if (_world.Has<Inventory>(_entity))
		{
			return _world.Get<Inventory>(_entity).GetItemCount(itemId);
		}

		return 0;
	}

	public void SetItemCharges(string itemId, int charges)
	{
		if (!_world.IsAlive(_entity) || string.IsNullOrWhiteSpace(itemId)) return;
		if (_world.Has<Inventory>(_entity))
		{
			ref var inv = ref _world.Get<Inventory>(_entity);
			if (inv.HasItem(itemId))
			{
				inv.SetItemCount(itemId, charges);
				if (charges > 0)
				{
					ItemGrantingUtility.ApplyItemGrants(_world, _entity, itemId);
				}
				else
				{
					ItemGrantingUtility.RemoveItemGrants(_world, _entity, itemId);
					ItemGrantingUtility.RebuildEntityCapabilities(_world, _entity);
				}
				NotifyUiInventoryChanged();
			}
		}
	}

	public bool RemoveItem(string itemId)
	{
		if (!_world.IsAlive(_entity) || string.IsNullOrWhiteSpace(itemId)) return false;
		bool success = ItemGrantingUtility.UnequipItem(_world, _entity, itemId);
		if (success)
		{
			NotifyUiInventoryChanged();
		}
		return success;
	}

	private void NotifyUiInventoryChanged()
	{
		if (GameContext.Host != null && GameContext.InGameHud != null && GameHost.TryGetUnit3D(_entity, out var u3d) && GameContext.Host.SelectedUnits.Contains(u3d))
		{
			GameContext.InGameHud.RefreshUI(GameContext.Host.SelectedUnits);
		}
	}

	public bool HasItem(string itemId)
	{
		if (!_world.IsAlive(_entity) || string.IsNullOrWhiteSpace(itemId)) return false;
		if (_world.Has<Inventory>(_entity))
		{
			return _world.Get<Inventory>(_entity).HasItem(itemId);
		}

		return false;
	}

	public IReadOnlyList<string> GetItems()
	{
		if (!_world.IsAlive(_entity) || !_world.Has<Inventory>(_entity)) return Array.Empty<string>();
		return _world.Get<Inventory>(_entity).ToList();
	}

	public void AddBuff(string buffId, float duration)
	{
		if (!_world.IsAlive(_entity)) return;

		if (_world.Has<Buffs>(_entity))
		{
			_world.Get<Buffs>(_entity).Set(buffId, duration, duration);
		}
		else
		{
			var buffs = new Buffs(null);
			buffs.Set(buffId, duration, duration);
			_world.Add(_entity, buffs);
		}

		if (_world.Has<BuffState>(_entity))
		{
			_world.Get<BuffState>(_entity).Set(buffId, duration, duration);
		}
		else
		{
			var buffState = new BuffState(null);
			buffState.Set(buffId, duration, duration);
			_world.Add(_entity, buffState);
		}

		BuffService.Apply(_world, _entity, buffId, duration);
	}

	public void RemoveBuff(string buffId)
	{
		if (!_world.IsAlive(_entity)) return;

		if (_world.Has<Buffs>(_entity))
		{
			_world.Get<Buffs>(_entity).Remove(buffId);
		}

		if (_world.Has<BuffState>(_entity))
		{
			_world.Get<BuffState>(_entity).Remove(buffId);
		}

		BuffService.Remove(_world, _entity, buffId);
	}

	public bool HasBuff(string buffId)
	{
		if (!_world.IsAlive(_entity)) return false;

		if (_world.Has<BuffState>(_entity))
		{
			return _world.Get<BuffState>(_entity).TryGetValue(buffId, out _);
		}

		if (_world.Has<Buffs>(_entity))
		{
			return _world.Get<Buffs>(_entity).TryGetValue(buffId, out _);
		}

		return false;
	}

	public IEnumerable<string> GetModifiers()
	{
		if (!_world.IsAlive(_entity)) return Array.Empty<string>();
		var result = new List<string>();
		if (_world.Has<ModifierState>(_entity))
		{
			ref var modState = ref _world.Get<ModifierState>(_entity);
			for (int modIndex = 0; modIndex < modState.Count; modIndex++)
			{
				var mod = modState.Get(modIndex);
				result.Add($"{mod.StatTypeId.Value}: {(mod.Value >= 0f ? "+" : "")}{mod.Value} ({mod.Type})");
			}
		}

		if (_world.Has<BuffState>(_entity))
		{
			ref var buffState = ref _world.Get<BuffState>(_entity);
			for (int buffIndex = 0; buffIndex < buffState.Count; buffIndex++)
			{
				string buffKey = buffState.GetKey(buffIndex);
				if (BuffRegistry.BuffModifiers.TryGetValue(buffKey, out var mods))
				{
					foreach (var mod in mods)
					{
						result.Add($"[Buff: {buffKey}] {mod.StatTypeId.Value}: {(mod.Value >= 0f ? "+" : "")}{mod.Value} ({mod.Type})");
					}
				}
			}
		}

		return result;
	}

	public void SetCustomData(string key, string value) => SetCustomData(key, (object)value);

	public void SetCustomData(string key, object value)
	{
		if (!_world.IsAlive(_entity)) return;
		Dictionary<string, object> dict;
		if (_world.Has<CustomMetadata>(_entity))
		{
			dict = _world.Get<CustomMetadata>(_entity).Value;
		}
		else
		{
			dict = new Dictionary<string, object>();
			_world.Add(_entity, new CustomMetadata(dict));
		}

		dict[key] = value;
	}

	public string? GetCustomDataString(string key) => GetCustomData(key)?.ToString();

	public object? GetCustomData(string key)
	{
		if (!_world.IsAlive(_entity)) return null;
		if (_world.Has<CustomMetadata>(_entity))
		{
			var dict = _world.Get<CustomMetadata>(_entity).Value;
			if (dict.TryGetValue(key, out var val)) return val;
		}

		return null;
	}

	public bool RemoveCustomData(string key)
	{
		if (!_world.IsAlive(_entity)) return false;
		if (_world.Has<CustomMetadata>(_entity))
		{
			var dict = _world.Get<CustomMetadata>(_entity).Value;
			return dict.Remove(key);
		}

		return false;
	}

	public bool HasCustomData(string key)
	{
		if (!_world.IsAlive(_entity)) return false;
		if (_world.Has<CustomMetadata>(_entity))
		{
			var dict = _world.Get<CustomMetadata>(_entity).Value;
			return dict.ContainsKey(key);
		}

		return false;
	}

	public void TriggerMeshImpulse(float strength = 1.0f, float duration = 0.5f, float frequency = 12.0f)
	{
		if (GameHost.TryGetUnit3D(_entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
		{
			unit3D.TriggerImpulse(strength, duration, frequency);
		}
	}

	public IReadOnlyList<string> GetAbilities()
	{
		string typeId = TemplateID;
		if (string.IsNullOrEmpty(typeId)) return Array.Empty<string>();
		return GameContext.RegistryService?.GetUnitTypeAbilities(typeId) ?? Array.Empty<string>();
	}

	public bool AutoCombatEnabled
	{
		get
		{
			if (!_world.IsAlive(_entity)) return false;
			return _world.Has<AutoCombatState>(_entity) && _world.Get<AutoCombatState>(_entity).Enabled;
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			GameContext.SimulationService?.EnableUnitAutoCombat(this, value);
		}
	}

	public void SetAutoCast(string abilityId, bool active)
	{
		if (!_world.IsAlive(_entity)) return;
		GameContext.SimulationService?.SetAbilityAutoCast(this, abilityId, active);
	}
}