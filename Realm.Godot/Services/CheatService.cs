using Arch.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Ecs.Components.Tags;
using System;
using System.Collections.Generic;

public class CheatService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World EcsWorld => _ecsWorldAccessor.Current;

	public CheatService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	public enum CheatResult
	{
		None,
		Stonks,
		Gigachad,
		AbsoluteUnit,
		ThanosSnap,
		EzClap,
		NoCap,
		WarpSpeed,
		UnlimitedPower
	}

	private delegate (CheatResult Result, int AffectedCount) CheatHandler(CheatService service, Entity playerEntity, DefinitionManager definitionManager, IEnumerable<Entity> selectedEntities);

	private static readonly IReadOnlyDictionary<string, CheatHandler> _cheatHandlers = new Dictionary<string, CheatHandler>(StringComparer.OrdinalIgnoreCase)
	{
		{ "securethebag", ApplySecureTheBag },
		{ "gigachad", ApplyGigachad },
		{ "needforspeed", ApplyNeedForSpeed },
		{ "thanossnap", ApplyThanosSnap },
		{ "ezclap", ApplyEzClap },
		{ "aura", ApplyAura },
		{ "speedrun", ApplySpeedrun },
		{ "skibidi", ApplySkibidi }
	};

	internal (CheatResult Result, int AffectedCount) TryTriggerCheat(
		string text,
		bool isMultiplayer,
		Entity playerEntity,
		DefinitionManager definitionManager,
		IEnumerable<Entity> selectedEntities)
	{
		if (isMultiplayer)
		{
			return (CheatResult.None, 0);
		}

		string key = text.Trim();
		if (_cheatHandlers.TryGetValue(key, out var handler))
		{
			return handler(this, playerEntity, definitionManager, selectedEntities);
		}

		return (CheatResult.None, 0);
	}

	private static (CheatResult Result, int AffectedCount) ApplySecureTheBag(CheatService service, Entity playerEntity, DefinitionManager definitionManager, IEnumerable<Entity> selectedEntities)
	{
		if (playerEntity == Entity.Null || !service.EcsWorld.IsAlive(playerEntity) || !service.EcsWorld.Has<PlayerResources>(playerEntity))
		{
			return (CheatResult.Stonks, 0);
		}

		ref var playerRes = ref service.EcsWorld.Get<PlayerResources>(playerEntity);
		var goldId = "gold".AsResourceId(definitionManager);
		var woodId = "wood".AsResourceId(definitionManager);
		var stoneId = "stone".AsResourceId(definitionManager);

		const float resourceCap = 9999f;
		if (playerRes.Value.TryGetValue(goldId, out var currentGold)) playerRes.Value[goldId] = (int)Math.Min(resourceCap, currentGold + 10000);
		if (playerRes.Value.TryGetValue(woodId, out var currentWood)) playerRes.Value[woodId] = (int)Math.Min(resourceCap, currentWood + 10000);
		if (playerRes.Value.TryGetValue(stoneId, out var currentStone)) playerRes.Value[stoneId] = (int)Math.Min(resourceCap, currentStone + 10000);
		
		return (CheatResult.Stonks, 0);
	}

	private static (CheatResult Result, int AffectedCount) ApplyGigachad(CheatService service, Entity playerEntity, DefinitionManager definitionManager, IEnumerable<Entity> selectedEntities)
	{
		if (GameHost.Instance != null)
		{
			GameHost.Instance.GigachadEnabled = true;
		}

		int affected = 0;
		var query = QueryCache.AllHealthAndOwnerQuery;
		var world = service.EcsWorld;
		
		world.Query(in query, (Entity entity, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value != playerEntity || !world.IsAlive(entity)) return;

			if (world.Has<Health>(entity))
			{
				world.Set(entity, new Health(9000f, 9000f));
			}
			if (world.Has<Attack>(entity))
			{
				var atk = world.Get<Attack>(entity);
				world.Set(entity, new Attack(9001f, atk.Range, atk.Cooldown, atk.CurrentCooldown));
			}
			affected++;
		});
		
		return (CheatResult.Gigachad, affected);
	}

	private static (CheatResult Result, int AffectedCount) ApplyNeedForSpeed(CheatService service, Entity playerEntity, DefinitionManager definitionManager, IEnumerable<Entity> selectedEntities)
	{
		int affected = 0;
		foreach (var entity in selectedEntities)
		{
			if (!service.EcsWorld.IsAlive(entity) || !service.EcsWorld.Has<MovementStats>(entity)) continue;

			var mv = service.EcsWorld.Get<MovementStats>(entity);
			service.EcsWorld.Set(entity, new MovementStats(25f, mv.Acceleration, mv.TurnRate));
			affected++;
		}
		return (CheatResult.AbsoluteUnit, affected);
	}

	private static (CheatResult Result, int AffectedCount) ApplyThanosSnap(CheatService service, Entity playerEntity, DefinitionManager definitionManager, IEnumerable<Entity> selectedEntities)
	{
		var targets = new List<Entity>();
		var query = QueryCache.AllHealthAndOwnerQuery;
		var world = service.EcsWorld;

		world.Query(in query, (Entity entity, ref Owner owner) =>
		{
			if (world.IsAlive(owner.PlayerEntity.Value) && world.Has<Name>(owner.PlayerEntity.Value))
			{
				if (world.Get<Name>(owner.PlayerEntity.Value).Value == "Enemy_AI")
				{
					targets.Add(entity);
				}
			}
		});

		int destroyed = 0;
		foreach (var entity in targets)
		{
			if (!world.IsAlive(entity)) continue;

			var hp = world.Get<Health>(entity);
			world.Set(entity, new Health(0f, hp.Max));
			
			if (!world.Has<Dead>(entity))
			{
				world.Add<Dead>(entity);
			}
			
			if (GameHost.Instance != null && GameHost.TryGetUnit3D(entity, out var unit3D))
			{
				GameHost.Instance.TriggerKillUnit(unit3D);
			}
			destroyed++;
		}
		return (CheatResult.ThanosSnap, destroyed);
	}

	private static (CheatResult Result, int AffectedCount) ApplyEzClap(CheatService service, Entity playerEntity, DefinitionManager definitionManager, IEnumerable<Entity> selectedEntities)
	{
		if (GameHost.Instance != null)
		{
			((Realm.MapAPI.IGameAPI)GameHost.Instance).TriggerVictory();
		}
		return (CheatResult.EzClap, 0);
	}

	private static (CheatResult Result, int AffectedCount) ApplyAura(CheatService service, Entity playerEntity, DefinitionManager definitionManager, IEnumerable<Entity> selectedEntities)
	{
		Entity worldEntity = Entity.Null;
		var query = QueryCache.AllShroudStateQuery;
		service.EcsWorld.Query(in query, ent => worldEntity = ent);

		if (worldEntity != Entity.Null && service.EcsWorld.IsAlive(worldEntity) && service.EcsWorld.Has<ShroudState>(worldEntity))
		{
			ref var state = ref service.EcsWorld.Get<ShroudState>(worldEntity);
			state.ShroudType = "visible";
		}
		
		if (GameHost.Instance?.ShroudService != null)
		{
			GameHost.Instance.ShroudService.TriggerImmediateUpdate();
		}
		
		return (CheatResult.NoCap, 0);
	}

	private static (CheatResult Result, int AffectedCount) ApplySpeedrun(CheatService service, Entity playerEntity, DefinitionManager definitionManager, IEnumerable<Entity> selectedEntities)
	{
		if (GameHost.Instance != null)
		{
			GameHost.Instance.FastBuildEnabled = !GameHost.Instance.FastBuildEnabled;
		}
		return (CheatResult.WarpSpeed, 0);
	}

	private static (CheatResult Result, int AffectedCount) ApplySkibidi(CheatService service, Entity playerEntity, DefinitionManager definitionManager, IEnumerable<Entity> selectedEntities)
	{
		if (GameHost.Instance == null) return (CheatResult.UnlimitedPower, 0);
		
		GameHost.Instance.UnlimitedPowerEnabled = !GameHost.Instance.UnlimitedPowerEnabled;
		
		if (GameHost.Instance.UnlimitedPowerEnabled && playerEntity != Entity.Null && service.EcsWorld.IsAlive(playerEntity) && service.EcsWorld.Has<SpellCooldowns>(playerEntity))
		{
			service.EcsWorld.Get<SpellCooldowns>(playerEntity).Value?.Clear();
		}
		
		return (CheatResult.UnlimitedPower, 0);
	}
}
