using System;
using System.Collections.Generic;
using Arch.Core;
using Realm.Client.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.MapAPI;

namespace Realm.Client.Services;

public class CheatService
{
	public CheatService()
	{
	}

	public bool UnlimitedPowerEnabled { get; set; } = false;
	public bool GigachadEnabled { get; set; } = false;


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
		if (playerEntity == Entity.Null || !GameContext.World.IsAlive(playerEntity) || !GameContext.World.Has<PlayerResources>(playerEntity))
		{
			return (CheatResult.Stonks, 0);
		}

		ref var playerRes = ref GameContext.World.Get<PlayerResources>(playerEntity);
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
			service.GigachadEnabled = true;
		}

		int affected = 0;
		var query = QueryCache.AllHealthAndOwnerQuery;
		var world = GameContext.World;

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
			if (!GameContext.World.IsAlive(entity) || !GameContext.World.Has<MovementStats>(entity)) continue;

			var mv = GameContext.World.Get<MovementStats>(entity);
			GameContext.World.Set(entity, new MovementStats(25f, mv.Acceleration, mv.TurnRate));
			affected++;
		}

		return (CheatResult.AbsoluteUnit, affected);
	}

	private static (CheatResult Result, int AffectedCount) ApplyThanosSnap(CheatService service, Entity playerEntity, DefinitionManager definitionManager, IEnumerable<Entity> selectedEntities)
	{
		var targets = new List<Entity>();
		var query = QueryCache.AllHealthAndOwnerQuery;
		var world = GameContext.World;

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
			((IGameAPI)GameHost.Instance).TriggerVictory();
		}

		return (CheatResult.EzClap, 0);
	}

	private static (CheatResult Result, int AffectedCount) ApplyAura(CheatService service, Entity playerEntity, DefinitionManager definitionManager, IEnumerable<Entity> selectedEntities)
	{
		Entity worldEntity = Entity.Null;
		var query = QueryCache.AllShroudStateQuery;
		GameContext.World.Query(in query, ent => worldEntity = ent);

		if (worldEntity != Entity.Null && GameContext.World.IsAlive(worldEntity) && GameContext.World.Has<ShroudState>(worldEntity))
		{
			ref var state = ref GameContext.World.Get<ShroudState>(worldEntity);
			state.ShroudType = "visible";
		}

		if (GameContext.ShroudService != null)
		{
			GameContext.ShroudService.TriggerImmediateUpdate();
		}

		return (CheatResult.NoCap, 0);
	}

	private static (CheatResult Result, int AffectedCount) ApplySpeedrun(CheatService service, Entity playerEntity, DefinitionManager definitionManager, IEnumerable<Entity> selectedEntities)
	{
		if (GameHost.Instance != null)
		{
			GameContext.SimulationService.FastBuildEnabled = !GameContext.SimulationService.FastBuildEnabled;
		}

		return (CheatResult.WarpSpeed, 0);
	}

	private static (CheatResult Result, int AffectedCount) ApplySkibidi(CheatService service, Entity playerEntity, DefinitionManager definitionManager, IEnumerable<Entity> selectedEntities)
	{
		if (GameHost.Instance == null) return (CheatResult.UnlimitedPower, 0);

		service.UnlimitedPowerEnabled = !service.UnlimitedPowerEnabled;

		if (service.UnlimitedPowerEnabled && playerEntity != Entity.Null && GameContext.World.IsAlive(playerEntity) && GameContext.World.Has<SpellCooldowns>(playerEntity))
		{
			GameContext.World.Get<SpellCooldowns>(playerEntity).Value?.Clear();
		}

		return (CheatResult.UnlimitedPower, 0);
	}
}