using Arch.Core;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Resources;
using Realm.MapAPI;
using System;
using System.Collections.Generic;

public class Unit_WasmRuntime : IUnit, IEcsEntityWrapper
{
	private readonly Entity _entity;
	private readonly World _world;

	public Unit_WasmRuntime(Entity entity, World world)
	{
		_entity = entity;
		_world = world;
	}

	Entity IEcsEntityWrapper.Entity => _entity;

	public int UniqueId => _entity.Id;

	public override bool Equals(object? obj)
	{
		return obj is Unit_WasmRuntime other && _entity == other._entity;
	}

	public override int GetHashCode()
	{
		return _entity.GetHashCode();
	}

	public string ObjectID
	{
		get
		{
			if (!_world.IsAlive(_entity)) return string.Empty;
			if (_world.Has<DefinitionId>(_entity))
				return _world.Get<DefinitionId>(_entity).Value;
			return string.Empty;
		}
	}

	public string UnitId => ObjectID;

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
			if (_world.Has<UnitFaction>(_entity))
				return _world.Get<UnitFaction>(_entity).IsEnemy ? 1 : 0;
			return 0;
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			_world.SetOrAdd(_entity, new UnitOwnerPlayer(value));

			bool isEnemy = value != 0;
			_world.SetOrAdd(_entity, new UnitFaction(isEnemy));

			if (_world.Has<Owner>(_entity) && GameHost.Instance != null)
			{
				var playerOwner = isEnemy
					? GameHost.Instance.EnemyEntity.AsPlayerEntity(_world)
					: GameHost.Instance.PlayerEntity.AsPlayerEntity(_world);
				_world.Set(_entity, new Owner(playerOwner));
			}

			if (GameHost.TryGetUnit3D(_entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
			{
				unit3D.Player = value;
			}
		}
	}

	public bool IsEnemy
	{
		get
		{
			if (!_world.IsAlive(_entity)) return false;
			if (_world.Has<UnitFaction>(_entity))
				return _world.Get<UnitFaction>(_entity).IsEnemy;
			return false;
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			_world.SetOrAdd(_entity, new UnitFaction(value));
			if (_world.Has<Owner>(_entity))
			{
				var playerOwner = value
					? GameHost.Instance.EnemyEntity.AsPlayerEntity(_world)
					: GameHost.Instance.PlayerEntity.AsPlayerEntity(_world);
				_world.Set(_entity, new Owner(playerOwner));
			}
			if (GameHost.TryGetUnit3D(_entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
			{
				unit3D.Player = value ? 1 : 0;
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

	public System.Numerics.Vector3 Position
	{
		get
		{
			if (!_world.IsAlive(_entity)) return System.Numerics.Vector3.Zero;
			if (_world.Has<Position>(_entity))
			{
				var pos = _world.Get<Position>(_entity).Value;
				return new System.Numerics.Vector3(pos.X, pos.Y, pos.Z);
			}
			return System.Numerics.Vector3.Zero;
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
							GameHost.Instance.TriggerKillUnit(deadU3D);
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
			var statService = ServiceLocator.TryGet<Realm.Ecs.Services.StatService>();
			if (statService != null)
			{
				float actualDamage = statService.GetStatValue(_entity, new Realm.Ecs.Common.StatId("Attack"));
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
			if (_world.Has<Realm.Ecs.Components.Stats.Stats>(_entity))
			{
				var stats = _world.Get<Realm.Ecs.Components.Stats.Stats>(_entity);
				stats.Value[new Realm.Ecs.Common.StatId("Attack")] = value;
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
			var statService = ServiceLocator.TryGet<Realm.Ecs.Services.StatService>();
			if (statService != null)
			{
				float actualArmor = statService.GetStatValue(_entity, new Realm.Ecs.Common.StatId("Armor"));
				if (actualArmor > 0f) return actualArmor;
			}
			if (_world.Has<Armor>(_entity))
			{
				return _world.Get<Armor>(_entity).Value;
			}
			return 0f;
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			if (_world.Has<Armor>(_entity))
			{
				_world.Set(_entity, new Armor(value));
			}
			if (_world.Has<Realm.Ecs.Components.Stats.Stats>(_entity))
			{
				var stats = _world.Get<Realm.Ecs.Components.Stats.Stats>(_entity);
				stats.Value[new Realm.Ecs.Common.StatId("Armor")] = value;
			}
		}
	}

	public float Speed
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 0f;
			var statService = ServiceLocator.TryGet<Realm.Ecs.Services.StatService>();
			if (statService != null)
			{
				float actualSpeed = statService.GetStatValue(_entity, new Realm.Ecs.Common.StatId("MovementSpeed"));
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
			if (_world.Has<Realm.Ecs.Components.Stats.Stats>(_entity))
			{
				var stats = _world.Get<Realm.Ecs.Components.Stats.Stats>(_entity);
				stats.Value[new Realm.Ecs.Common.StatId("MovementSpeed")] = value;
			}
		}
	}

	public bool IsHero
	{
		get
		{
			if (!_world.IsAlive(_entity)) return false;
			return _world.Has<Realm.Ecs.Components.Tags.Hero>(_entity);
		}
	}

	public int Level
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 1;
			if (_world.Has<Realm.Ecs.Components.Meta.Level>(_entity))
			{
				return _world.Get<Realm.Ecs.Components.Meta.Level>(_entity).Value;
			}
			return 1;
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			var levelComp = new Realm.Ecs.Components.Meta.Level(value);
			_world.SetOrAdd(_entity, levelComp);
		}
	}

	public float Experience
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 0f;
			if (_world.Has<Realm.Ecs.Components.Meta.Experience>(_entity))
			{
				return _world.Get<Realm.Ecs.Components.Meta.Experience>(_entity).Value;
			}
			return 0f;
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			var expComp = new Realm.Ecs.Components.Meta.Experience(value);
			_world.SetOrAdd(_entity, expComp);
		}
	}

	public float XpBounty
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 0f;
			if (GameHost.UnitRegistry.TryGetValue(UnitId, out var meta))
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
			if (GameHost.UnitRegistry.TryGetValue(UnitId, out var meta))
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

	public void MoveTo(System.Numerics.Vector3 destination)
	{
		if (!_world.IsAlive(_entity) || IsDead) return;
		if (_world.Has<Realm.Ecs.Components.Movement.HoldPosition>(_entity))
		{
			_world.Remove<Realm.Ecs.Components.Movement.HoldPosition>(_entity);
		}
		if (_world.Has<Realm.Ecs.Components.Movement.AttackMove>(_entity))
		{
			_world.Remove<Realm.Ecs.Components.Movement.AttackMove>(_entity);
		}
		if (_world.Has<Realm.Ecs.Components.Combat.AttackTarget>(_entity))
		{
			_world.Remove<Realm.Ecs.Components.Combat.AttackTarget>(_entity);
		}
		var mv = new MoveTo(destination);
		_world.SetOrAdd(_entity, mv);
	}

	public void AttackMove(System.Numerics.Vector3 destination)
	{
		if (!_world.IsAlive(_entity) || IsDead) return;
		if (_world.Has<Realm.Ecs.Components.Movement.HoldPosition>(_entity))
		{
			_world.Remove<Realm.Ecs.Components.Movement.HoldPosition>(_entity);
		}
		if (_world.Has<Realm.Ecs.Components.Combat.AttackTarget>(_entity))
		{
			_world.Remove<Realm.Ecs.Components.Combat.AttackTarget>(_entity);
		}
		var am = new Realm.Ecs.Components.Movement.AttackMove(destination);
		_world.SetOrAdd(_entity, am);
		var mv = new MoveTo(destination);
		_world.SetOrAdd(_entity, mv);
	}

	public void Attack(IUnit target)
	{
		if (!_world.IsAlive(_entity) || IsDead || target == null) return;
		if (target is IEcsEntityWrapper wrapper && _world.IsAlive(wrapper.Entity))
		{
			var at = new AttackTarget(wrapper.Entity);
			_world.SetOrAdd(_entity, at);
		}
	}

	public void Gather(IResourceNode resourceNode)
	{
		if (!_world.IsAlive(_entity) || IsDead || resourceNode == null) return;
		if (resourceNode is IEcsPropWrapper nodeWrapper)
		{
			var propNode = nodeWrapper.Prop;
			if (GodotObject.IsInstanceValid(propNode))
			{
				var gatherer = new Gatherer(resourceNode.ResourceType, propNode.Entity);
				_world.SetOrAdd(_entity, gatherer);
			}
		}
	}

	public void Teleport(System.Numerics.Vector3 position)
	{
		if (!_world.IsAlive(_entity)) return;

		var newPosComp = new Position(position);
		_world.SetOrAdd(_entity, newPosComp);

		if (GameHost.TryGetUnit3D(_entity, out var unit3D))
		{
			if (GodotObject.IsInstanceValid(unit3D))
			{
				unit3D.GlobalPosition = new Vector3(position.X, position.Y, position.Z);
			}
		}
	}

	public float Mana
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 0f;
			if (_world.Has<Realm.Ecs.Components.Core.Mana>(_entity))
			{
				return _world.Get<Realm.Ecs.Components.Core.Mana>(_entity).Current;
			}
			return 0f;
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			float max = MaxMana;
			var val = Math.Max(0f, value);
			_world.SetOrAdd(_entity, new Realm.Ecs.Components.Core.Mana(val, max));
		}
	}

	public float MaxMana
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 0f;
			if (_world.Has<Realm.Ecs.Components.Core.Mana>(_entity))
			{
				return _world.Get<Realm.Ecs.Components.Core.Mana>(_entity).Max;
			}
			return 0f;
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			float current = Mana;
			var val = Math.Max(0f, value);
			_world.SetOrAdd(_entity, new Realm.Ecs.Components.Core.Mana(current, val));
		}
	}

	public float AttackSpeed
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 1f;
			if (_world.Has<Realm.Ecs.Components.Combat.AttackSpeed>(_entity))
			{
				return _world.Get<Realm.Ecs.Components.Combat.AttackSpeed>(_entity).Multiplier;
			}
			return 1f;
		}
		set
		{
			if (!_world.IsAlive(_entity) || value <= 0f) return;
			_world.SetOrAdd(_entity, new Realm.Ecs.Components.Combat.AttackSpeed(value));
		}
	}

	public float ManaRegen
	{
		get
		{
			if (!_world.IsAlive(_entity)) return 0f;
			if (_world.Has<Realm.Ecs.Components.Core.ManaRegen>(_entity))
			{
				return _world.Get<Realm.Ecs.Components.Core.ManaRegen>(_entity).PerSecond;
			}
			return 0f;
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			_world.SetOrAdd(_entity, new Realm.Ecs.Components.Core.ManaRegen(Math.Max(0f, value)));
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
					u3d.Scale = new Vector3(safeScale, safeScale, safeScale);
				}
			}
		}
	}

	public bool Invulnerable
	{
		get
		{
			if (!_world.IsAlive(_entity)) return false;
			return _world.Has<Realm.Ecs.Components.Tags.Invulnerable>(_entity);
		}
		set
		{
			if (!_world.IsAlive(_entity)) return;
			if (value)
			{
				if (!_world.Has<Realm.Ecs.Components.Tags.Invulnerable>(_entity))
				{
					_world.Add<Realm.Ecs.Components.Tags.Invulnerable>(_entity);
				}
			}
			else
			{
				if (_world.Has<Realm.Ecs.Components.Tags.Invulnerable>(_entity))
				{
					_world.Remove<Realm.Ecs.Components.Tags.Invulnerable>(_entity);
				}
			}
		}
	}

	public void Stop()
	{
		if (!_world.IsAlive(_entity)) return;
		GameHost.Instance.ClearUnitOrders(_entity);
		if (GameHost.TryGetUnit3D(_entity, out var u3d))
		{
			if (GodotObject.IsInstanceValid(u3d))
			{
				u3d.Velocity = Vector3.Zero;
			}
		}
	}

	public void HoldPosition()
	{
		if (!_world.IsAlive(_entity)) return;
		Stop();
		if (!_world.Has<Realm.Ecs.Components.Movement.HoldPosition>(_entity))
		{
			_world.Add<Realm.Ecs.Components.Movement.HoldPosition>(_entity);
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
		if (!_world.IsAlive(_entity)) return false;
		List<string> items;
		if (_world.Has<Realm.Ecs.Components.Core.UnitItems>(_entity))
		{
			items = _world.Get<Realm.Ecs.Components.Core.UnitItems>(_entity).Value;
		}
		else
		{
			items = new List<string>();
			_world.Add(_entity, new Realm.Ecs.Components.Core.UnitItems(items));
		}
		if (items.Count >= 6) return false;
		items.Add(itemId);
		return true;
	}

	public bool RemoveItem(string itemId)
	{
		if (!_world.IsAlive(_entity)) return false;
		if (_world.Has<Realm.Ecs.Components.Core.UnitItems>(_entity))
		{
			var items = _world.Get<Realm.Ecs.Components.Core.UnitItems>(_entity).Value;
			return items.Remove(itemId);
		}
		return false;
	}

	public bool HasItem(string itemId)
	{
		if (!_world.IsAlive(_entity)) return false;
		if (_world.Has<Realm.Ecs.Components.Core.UnitItems>(_entity))
		{
			var items = _world.Get<Realm.Ecs.Components.Core.UnitItems>(_entity).Value;
			return items.Contains(itemId);
		}
		return false;
	}

	public IEnumerable<string> GetItems()
	{
		if (!_world.IsAlive(_entity)) return Array.Empty<string>();
		if (_world.Has<Realm.Ecs.Components.Core.UnitItems>(_entity))
		{
			return _world.Get<Realm.Ecs.Components.Core.UnitItems>(_entity).Value;
		}
		return Array.Empty<string>();
	}

	public void AddBuff(string buffId, float duration)
	{
		if (!_world.IsAlive(_entity)) return;
		Dictionary<string, float> buffs;
		if (_world.Has<Realm.Ecs.Components.Core.Buffs>(_entity))
		{
			buffs = _world.Get<Realm.Ecs.Components.Core.Buffs>(_entity).Value;
		}
		else
		{
			buffs = new Dictionary<string, float>();
			_world.Add(_entity, new Realm.Ecs.Components.Core.Buffs(buffs));
		}
		buffs[buffId] = duration;

		Dictionary<string, float> buffStateDict;
		if (_world.Has<Realm.Ecs.Components.Core.BuffState>(_entity))
		{
			buffStateDict = _world.Get<Realm.Ecs.Components.Core.BuffState>(_entity).Value;
		}
		else
		{
			buffStateDict = new Dictionary<string, float>();
			_world.Add(_entity, new Realm.Ecs.Components.Core.BuffState(buffStateDict));
		}
		buffStateDict[buffId] = duration;
	}

	public void RemoveBuff(string buffId)
	{
		if (!_world.IsAlive(_entity)) return;
		if (_world.Has<Realm.Ecs.Components.Core.Buffs>(_entity))
		{
			var buffs = _world.Get<Realm.Ecs.Components.Core.Buffs>(_entity).Value;
			buffs.Remove(buffId);
		}
		if (_world.Has<Realm.Ecs.Components.Core.BuffState>(_entity))
		{
			var buffs = _world.Get<Realm.Ecs.Components.Core.BuffState>(_entity).Value;
			buffs.Remove(buffId);
		}
	}

	public bool HasBuff(string buffId)
	{
		if (!_world.IsAlive(_entity)) return false;
		if (_world.Has<Realm.Ecs.Components.Core.BuffState>(_entity))
		{
			var buffs = _world.Get<Realm.Ecs.Components.Core.BuffState>(_entity).Value;
			return buffs.ContainsKey(buffId);
		}
		if (_world.Has<Realm.Ecs.Components.Core.Buffs>(_entity))
		{
			var buffs = _world.Get<Realm.Ecs.Components.Core.Buffs>(_entity).Value;
			return buffs.ContainsKey(buffId);
		}
		return false;
	}

	public IEnumerable<string> GetModifiers()
	{
		if (!_world.IsAlive(_entity)) return Array.Empty<string>();
		var result = new List<string>();
		if (_world.Has<Realm.Ecs.Components.Core.ModifierState>(_entity))
		{
			var modState = _world.Get<Realm.Ecs.Components.Core.ModifierState>(_entity);
			foreach (var mod in modState.Value)
			{
				result.Add($"{mod.StatTypeId.Value}: {(mod.Value >= 0f ? "+" : "")}{mod.Value} ({mod.Type})");
			}
		}
		if (_world.Has<Realm.Ecs.Components.Core.BuffState>(_entity))
		{
			var buffState = _world.Get<Realm.Ecs.Components.Core.BuffState>(_entity);
			foreach (var buffKey in buffState.Value.Keys)
			{
				if (Realm.Ecs.Common.BuffRegistry.BuffModifiers.TryGetValue(buffKey, out var mods))
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
		if (_world.Has<Realm.Ecs.Components.Core.CustomMetadata>(_entity))
		{
			dict = _world.Get<Realm.Ecs.Components.Core.CustomMetadata>(_entity).Value;
		}
		else
		{
			dict = new Dictionary<string, object>();
			_world.Add(_entity, new Realm.Ecs.Components.Core.CustomMetadata(dict));
		}
		dict[key] = value;
	}

	string? IUnit.GetCustomData(string key) => GetCustomData(key)?.ToString();

	public object? GetCustomData(string key)
	{
		if (!_world.IsAlive(_entity)) return null;
		if (_world.Has<Realm.Ecs.Components.Core.CustomMetadata>(_entity))
		{
			var dict = _world.Get<Realm.Ecs.Components.Core.CustomMetadata>(_entity).Value;
			if (dict.TryGetValue(key, out var val)) return val;
		}
		return null;
	}

	public bool RemoveCustomData(string key)
	{
		if (!_world.IsAlive(_entity)) return false;
		if (_world.Has<Realm.Ecs.Components.Core.CustomMetadata>(_entity))
		{
			var dict = _world.Get<Realm.Ecs.Components.Core.CustomMetadata>(_entity).Value;
			return dict.Remove(key);
		}
		return false;
	}

	public bool HasCustomData(string key)
	{
		if (!_world.IsAlive(_entity)) return false;
		if (_world.Has<Realm.Ecs.Components.Core.CustomMetadata>(_entity))
		{
			var dict = _world.Get<Realm.Ecs.Components.Core.CustomMetadata>(_entity).Value;
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
}
