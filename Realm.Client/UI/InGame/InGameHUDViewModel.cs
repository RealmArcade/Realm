using System;
using System.Collections.Generic;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;

namespace Realm.Client.UI.InGame;

public class InGameHUDViewModel
{
	private Entity GetTargetPlayerEntity()
	{
		GameHost tempQualifier = GameContext.Host;
		if ((tempQualifier != null ? GameContext.World : null) == null)
			return Entity.Null;

		if (IsPlayingNormally())
			return GameContext.Host.GetPlayerEntityForPlayerIndex(GameContext.PerspectiveFilter.ControlledPlayerIndex);

		if (GameContext.ReplayPlayback.IsPlayingReplay)
		{
			int replayPeerId = GameContext.ReplayPlayback.SpectatorPerspective;
			if (replayPeerId == -1) return Entity.Null;
			return GetSpectatedPlayer(replayPeerId);
		}

		int livePerspective = GameContext.InGameHud?.LiveSpectatorPerspective ?? -1;
		if (livePerspective <= 0)
			return Entity.Null;

		return GameContext.Host.GetPlayerEntityForPlayerIndex(livePerspective);
	}

	private bool IsPlayingNormally()
	{
		bool isSpectator = GameContext.PerspectiveFilter.IsSpectating;
		bool isPlayingReplay = GameContext.ReplayPlayback.IsPlayingReplay;
		return !isSpectator && !isPlayingReplay;
	}

	private Entity GetSpectatedPlayer(int targetPeerId)
	{
		var world = GameContext.World;
		var worldEntity = ECSContext.WorldEntity;
		if (worldEntity == Entity.Null) throw new InvalidOperationException($"No player entity is mapped for peer {targetPeerId}.");
		if (!world.IsAlive(worldEntity)) throw new InvalidOperationException($"No player entity is mapped for peer {targetPeerId}.");
		if (!world.Has<NetworkMappingState>(worldEntity)) throw new InvalidOperationException($"No player entity is mapped for peer {targetPeerId}.");

		var mapping = world.Get<NetworkMappingState>(worldEntity);
		if (mapping.PeerIdToPlayerEntityMap == null) throw new InvalidOperationException($"No player entity is mapped for peer {targetPeerId}.");
		if (!mapping.PeerIdToPlayerEntityMap.TryGetValue(targetPeerId, out var spectatedPlayer)) throw new InvalidOperationException($"No player entity is mapped for peer {targetPeerId}.");

		return spectatedPlayer;
	}

	public float Gold
	{
		get
		{
			if (GameContext.Host != null && GameContext.World != null)
			{
				var world = GameContext.World;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerResources>(player))
				{
					return world.Get<PlayerResources>(player).Gold;
				}
			}

			return 500f;
		}
		set
		{
			if (GameContext.Host != null && GameContext.World != null)
			{
				var world = GameContext.World;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerResources>(player))
				{
					ref var res = ref world.Get<PlayerResources>(player);
					res.Gold = (int)value;
				}
			}
		}
	}

	public float Wood
	{
		get
		{
			if (GameContext.Host != null && GameContext.World != null)
			{
				var world = GameContext.World;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerResources>(player))
				{
					return world.Get<PlayerResources>(player).Wood;
				}
			}

			return 400f;
		}
		set
		{
			if (GameContext.Host != null && GameContext.World != null)
			{
				var world = GameContext.World;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerResources>(player))
				{
					ref var res = ref world.Get<PlayerResources>(player);
					res.Wood = (int)value;
				}
			}
		}
	}

	public float Stone
	{
		get
		{
			if (GameContext.Host != null && GameContext.World != null)
			{
				var world = GameContext.World;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerResources>(player))
				{
					return world.Get<PlayerResources>(player).Stone;
				}
			}

			return 200f;
		}
		set
		{
			if (GameContext.Host != null && GameContext.World != null)
			{
				var world = GameContext.World;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerResources>(player))
				{
					ref var res = ref world.Get<PlayerResources>(player);
					res.Stone = (int)value;
				}
			}
		}
	}

	public float ResourceGatherMultiplier
	{
		get
		{
			if (GameContext.Host != null && GameContext.World != null)
			{
				var world = GameContext.World;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerUpgrades>(player))
				{
					return world.Get<PlayerUpgrades>(player).HarvestingUpgrade ? 1.5f : 1.0f;
				}
			}

			return 1.0f;
		}
		set
		{
			if (GameContext.Host != null && GameContext.World != null)
			{
				var world = GameContext.World;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerUpgrades>(player))
				{
					ref var upgrades = ref world.Get<PlayerUpgrades>(player);
					upgrades.HarvestingUpgrade = value > 1.0f;
				}
			}
		}
	}

	public float GoldPerSec { get; set; } = 1.5f;
	public float WoodPerSec { get; set; } = 1.0f;
	public float StonePerSec { get; set; } = 0.8f;

	public int CurrentPopulation { get; set; }
	public int MaxPopulation { get; set; }
	public string ClockText { get; set; } = "0:00 (Day)";

	public bool IsConnectionLost { get; set; }

	public bool CountdownActive
	{
		get
		{
			if (GameContext.Host != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(ECSContext.WorldEntity) && world.Has<CountdownState>(ECSContext.WorldEntity))
				{
					return world.Get<CountdownState>(ECSContext.WorldEntity).Active;
				}
			}

			return false;
		}
		set
		{
			if (GameContext.Host != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(ECSContext.WorldEntity) && world.Has<CountdownState>(ECSContext.WorldEntity))
				{
					ref var countdown = ref world.Get<CountdownState>(ECSContext.WorldEntity);
					countdown.Active = value;
				}
			}
		}
	}

	public float CountdownDuration
	{
		get
		{
			if (GameContext.Host != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(ECSContext.WorldEntity) && world.Has<CountdownState>(ECSContext.WorldEntity))
				{
					return world.Get<CountdownState>(ECSContext.WorldEntity).Duration;
				}
			}

			return 0f;
		}
		set
		{
			if (GameContext.Host != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(ECSContext.WorldEntity) && world.Has<CountdownState>(ECSContext.WorldEntity))
				{
					ref var countdown = ref world.Get<CountdownState>(ECSContext.WorldEntity);
					countdown.Duration = value;
				}
			}
		}
	}

	public string CountdownText
	{
		get
		{
			if (GameContext.Host != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(ECSContext.WorldEntity) && world.Has<CountdownState>(ECSContext.WorldEntity))
				{
					return world.Get<CountdownState>(ECSContext.WorldEntity).Text;
				}
			}

			return "";
		}
		set
		{
			if (GameContext.Host != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(ECSContext.WorldEntity) && world.Has<CountdownState>(ECSContext.WorldEntity))
				{
					ref var countdown = ref world.Get<CountdownState>(ECSContext.WorldEntity);
					countdown.Text = value;
				}
			}
		}
	}

	public LeaderboardBoards Leaderboards
	{
		get
		{
			if (GameContext.Host != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(ECSContext.WorldEntity) && world.Has<LeaderboardState>(ECSContext.WorldEntity))
				{
					LeaderboardBoards? boards = world.Get<LeaderboardState>(ECSContext.WorldEntity).Boards;
					if (boards != null)
						return boards;
				}
			}

			return EmptyLeaderboards;
		}
	}

	public bool HasVisibleLeaderboard
	{
		get
		{
			IReadOnlyList<LeaderboardBoard> boards = Leaderboards.Boards;
			for (int index = 0; index < boards.Count; index++)
			{
				if (boards[index].Visible)
					return true;
			}

			return false;
		}
	}

	private static readonly LeaderboardBoards EmptyLeaderboards = new();

	private static ref SummaryTableState EnsureSummaryTableState(EcsWorld world, Entity entity)
	{
		if (!world.Has<SummaryTableState>(entity))
		{
			world.Add(entity, new SummaryTableState(false, "Stats", Array.Empty<string>(), new Dictionary<string, string[]>()));
		}

		return ref world.Get<SummaryTableState>(entity);
	}

	public bool SummaryTableVisible
	{
		get
		{
			if (GameContext.Host != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(ECSContext.WorldEntity) && world.Has<SummaryTableState>(ECSContext.WorldEntity))
				{
					return world.Get<SummaryTableState>(ECSContext.WorldEntity).Visible;
				}
			}

			return false;
		}
		set
		{
			if (GameContext.Host != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(ECSContext.WorldEntity))
				{
					ref var st = ref EnsureSummaryTableState(world, ECSContext.WorldEntity);
					st.Visible = value;
				}
			}
		}
	}

	public string SummaryTableTitle
	{
		get
		{
			if (GameContext.Host != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(ECSContext.WorldEntity) && world.Has<SummaryTableState>(ECSContext.WorldEntity))
				{
					return world.Get<SummaryTableState>(ECSContext.WorldEntity).Title;
				}
			}

			return "Stats";
		}
		set
		{
			if (GameContext.Host != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(ECSContext.WorldEntity))
				{
					ref var st = ref EnsureSummaryTableState(world, ECSContext.WorldEntity);
					st.Title = value;
				}
			}
		}
	}

	public string[] SummaryTableHeaders
	{
		get
		{
			if (GameContext.Host != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(ECSContext.WorldEntity) && world.Has<SummaryTableState>(ECSContext.WorldEntity))
				{
					return world.Get<SummaryTableState>(ECSContext.WorldEntity).Headers ?? Array.Empty<string>();
				}
			}

			return Array.Empty<string>();
		}
		set
		{
			if (GameContext.Host != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(ECSContext.WorldEntity))
				{
					ref var st = ref EnsureSummaryTableState(world, ECSContext.WorldEntity);
					st.Headers = value;
				}
			}
		}
	}

	public Dictionary<string, string[]> SummaryTableRows
	{
		get
		{
			if (GameContext.Host != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(ECSContext.WorldEntity))
				{
					ref var st = ref EnsureSummaryTableState(world, ECSContext.WorldEntity);
					return st.Rows;
				}
			}

			return _cachedEmptySummaryRows;
		}
	}

	private static readonly Dictionary<string, string[]> _cachedEmptySummaryRows = new();

	public List<SelectedUnitInfo> SelectedUnits { get; } = new();
	public Prop3D SelectedProp { get; set; }
	public int CycleSelectionIndex { get; set; }

	public bool IsChatActive { get; set; }


	public string CurrentWeather { get; set; } = "clear";
	public bool ShowMinimapTerrain { get; set; } = true;
	public MinimapColorScheme MinimapColorScheme { get; set; } = MinimapColorScheme.TeamColors;

	public int IdleCount { get; set; }

	public bool IsBuildSubMenuOpen { get; set; }

	private int _lastClockMins = -1;
	private int _lastClockSecs = -1;
	private int _lastClockPhase = -1;

	public void Update(double delta)
	{
		UpdateTactical(delta);
		UpdateMacro(delta);
	}

	public void UpdateTactical(double delta)
	{
		if (GameContext.Host == null)
			return;

		IsConnectionLost = GameContext.Host.IsConnectionLost;
		CycleSelectionIndex = GameContext.Host.CycleSelectionIndex;
	}

	public void UpdateMacro(double delta)
	{
		GoldPerSec = 1.5f * ResourceGatherMultiplier;
		WoodPerSec = 1.0f * ResourceGatherMultiplier;
		StonePerSec = 0.8f * ResourceGatherMultiplier;

		if (GameContext.Host == null)
			return;

		UpdatePopulation();
		UpdateClock();
		UpdateIdleCount();
	}

	private void UpdatePopulation()
	{
		var world = GameContext.World;
		if (world == null)
		{
			CurrentPopulation = 0;
			MaxPopulation = 0;
			return;
		}

		var player = GetTargetPlayerEntity();
		if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerPopulation>(player))
		{
			var pop = world.Get<PlayerPopulation>(player);
			CurrentPopulation = pop.Current;
			MaxPopulation = pop.Max;
			return;
		}

		CurrentPopulation = 0;
		MaxPopulation = 0;
	}

	private void UpdateClock()
	{
		float t = GameContext.Host.GameElapsedTime;
		int mins = (int)(t / 60);
		int secs = (int)(t % 60);
		int phaseIdx = GameContext.Host.TimeOfDayIndex;

		if (mins == _lastClockMins && secs == _lastClockSecs && phaseIdx == _lastClockPhase)
			return;

		string phase = phaseIdx switch
		{
			0 => TranslationServer.Translate("Day"),
			1 => TranslationServer.Translate("Dusk"),
			2 => TranslationServer.Translate("Night"),
			3 => TranslationServer.Translate("Dawn"),
			_ => TranslationServer.Translate("Day")
		};
		ClockText = $"{mins}:{secs:D2} ({phase})";
		_lastClockMins = mins;
		_lastClockSecs = secs;
		_lastClockPhase = phaseIdx;
	}

	private void UpdateIdleCount()
	{
		var world = GameContext.World;
		if (world == null)
		{
			IdleCount = 0;
			return;
		}

		var targetPlayer = GetTargetPlayerEntity();
		if (targetPlayer == Entity.Null || !world.IsAlive(targetPlayer))
		{
			IdleCount = 0;
			return;
		}

		int idle = 0;
		foreach (var entity in world.Query(in QueryCache.AllIdleMovableQuery))
		{
			if (world.Get<Owner>(entity).PlayerEntity.Value == targetPlayer)
			{
				idle++;
			}
		}

		IdleCount = idle;
	}

	public void UpdateSelectedUnits(List<Unit3D> selectedUnits)
	{
		if (selectedUnits == null)
		{
			SelectedUnits.Clear();
			return;
		}

		SyncSelectedUnitsListSize(selectedUnits.Count);

		for (int i = 0; i < selectedUnits.Count; i++)
		{
			UpdateSingleSelectedUnit(selectedUnits[i], SelectedUnits[i]);
		}
	}

	private void SyncSelectedUnitsListSize(int targetCount)
	{
		while (SelectedUnits.Count > targetCount)
		{
			SelectedUnits.RemoveAt(SelectedUnits.Count - 1);
		}

		while (SelectedUnits.Count < targetCount)
		{
			SelectedUnits.Add(new SelectedUnitInfo());
		}
	}

	private void UpdateSingleSelectedUnit(Unit3D u, SelectedUnitInfo info)
	{
		bool entityChanged = info.Entity != u.Entity;

		InitializeUnitInfo(u, info, entityChanged);

		if (GameContext.Host == null || !GameContext.World.IsAlive(u.Entity))
			return;

		var world = GameContext.World;

		UpdateUnitBasicStats(world, u, info);
		UpdateUnitStateText(world, u, info);
		UpdateUnitInventory(world, u, info);
		UpdateUnitProduction(world, u, info);

		if (!info.HasProduction && info.ProductionQueue.Count > 0)
		{
			info.ProductionQueue.Clear();
		}

		if (entityChanged || info.Abilities.Count == 0)
		{
			UpdateUnitAbilities(world, u, info);
		}
	}

	private void InitializeUnitInfo(Unit3D u, SelectedUnitInfo info, bool entityChanged)
	{
		info.Entity = u.Entity;
		info.UnitId = u.UnitId;
		info.IsEnemy = GameContext.PerspectiveFilter.IsHostileToObserved(u.Player);
		info.Player = u.Player;
		info.IsBuilding = u.IsBuilding;
		info.HasInventory = false;
		info.Name = u.UnitId;
		info.IsUnderConstruction = false;
		info.HasProduction = false;
		info.ProductionTitle = null;
		info.ProductionProgress = 0f;
		info.ProductionMaxProgress = 0f;

		if (entityChanged)
		{
			info.ProductionQueue.Clear();
			info.Abilities.Clear();
			info.Description = null;
		}

		info.Potions = 0;
		info.StateText = null;
		info.Health = 0f;
		info.MaxHealth = 0f;
		info.Damage = 0f;
		info.Range = 0f;
		info.Armor = 0f;
		info.Speed = 0f;
		info.Dps = 0f;
	}

	private void UpdateUnitBasicStats(EcsWorld world, Unit3D u, SelectedUnitInfo info)
	{
		if (world.Has<Name>(u.Entity))
			info.Name = world.Get<Name>(u.Entity).Value;

		if (u.IsBuilding && world.Has<UnderConstruction>(u.Entity))
			info.IsUnderConstruction = true;

		if (world.Has<Health>(u.Entity))
		{
			var hp = world.Get<Health>(u.Entity);
			info.MaxHealth = hp.Max;
			info.Health = hp.Current;
		}

		if (world.Has<Attack>(u.Entity))
		{
			var atk = world.Get<Attack>(u.Entity);
			info.Damage = atk.Damage;
			info.Range = atk.Range;
			if (atk.Cooldown > 0)
				info.Dps = atk.Damage / atk.Cooldown;
		}

		if (world.Has<Resistances>(u.Entity))
			info.Armor = world.Get<Resistances>(u.Entity).Physical;

		if (world.Has<MovementStats>(u.Entity))
			info.Speed = world.Get<MovementStats>(u.Entity).Speed;
	}

	private void UpdateUnitStateText(EcsWorld world, Unit3D u, SelectedUnitInfo info)
	{
		info.StateText = GetBaseStateText(world, u);

		if (u.UnitId == "tower" && world.Has<TowerUpgradeLevel>(u.Entity))
		{
			int lvl = world.Get<TowerUpgradeLevel>(u.Entity).Value;
			info.StateText += $"   ★ {TranslationServer.Translate("LVL")} {lvl}";
		}
	}

	private string GetBaseStateText(EcsWorld world, Unit3D u)
	{
		if (world.Has<Gatherer>(u.Entity))
			return GetGathererStateText(world.Get<Gatherer>(u.Entity));

		if (world.Has<BuildTask>(u.Entity))
			return GetBuildTaskStateText(world.Get<BuildTask>(u.Entity));

		return GetMovementOrAttackStateText(world, u.Entity);
	}

	private string GetGathererStateText(Gatherer gather)
	{
		string cachedDelivering = TranslationServer.Translate("DELIVERING");
		string cachedHarvesting = TranslationServer.Translate("HARVESTING");
		string stateLabel = gather.ReturningToBase ? "● " + cachedDelivering : "● " + cachedHarvesting;
		return $"{stateLabel} ({gather.CarriedAmount:F0} / {gather.MaxCapacity:F0} {TranslationServer.Translate(gather.ResourceType.ToUpper())})";
	}

	private string GetBuildTaskStateText(BuildTask bt)
	{
		string cachedConstructing = TranslationServer.Translate("CONSTRUCTING");
		int pct = (int)(bt.Progress / Mathf.Max(bt.TotalBuildTime, 0.001f) * 100f);
		return $"🔨 {cachedConstructing} ({pct}%)";
	}

	private string GetMovementOrAttackStateText(EcsWorld world, Entity entity)
	{
		if (world.Has<HoldPosition>(entity))
			return "● " + TranslationServer.Translate("HOLDING");

		if (world.Has<Patrol>(entity))
			return "● " + TranslationServer.Translate("PATROLLING");

		if (world.Has<AttackMove>(entity))
			return "● " + TranslationServer.Translate("ATTACK-MOVE");

		if (world.Has<Follow>(entity))
			return "● " + TranslationServer.Translate("FOLLOWING");

		if (world.Has<MoveTo>(entity))
			return "● " + TranslationServer.Translate("MOVING");

		if (world.Has<AttackTarget>(entity))
			return "● " + TranslationServer.Translate("ATTACKING");

		return "○ " + TranslationServer.Translate("IDLE");
	}

	private void UpdateUnitInventory(EcsWorld world, Unit3D u, SelectedUnitInfo info)
	{
		info.InventoryItems.Clear();
		info.HasInventory = world.Has<Inventory>(u.Entity);
		if (!info.HasInventory)
			return;

		var inv = world.Get<Inventory>(u.Entity);
		for (int itemIndex = 0; itemIndex < inv.Count; itemIndex++)
		{
			string? itemId = inv.GetItemId(itemIndex);
			if (itemId == null) continue;
			info.InventoryItems[itemId] = inv.GetCharges(itemIndex);
		}
	}

	private void UpdateUnitProduction(EcsWorld world, Unit3D u, SelectedUnitInfo info)
	{
		if (!info.IsEnemy && world.Has<ConstructionState>(u.Entity))
		{
			var cs = world.Get<ConstructionState>(u.Entity);
			info.HasProduction = true;
			info.ProductionTitle = TranslationServer.Translate("UNDER CONSTRUCTION");
			info.ProductionProgress = cs.Progress;
			info.ProductionMaxProgress = cs.TotalBuildTime;
		}
		else if (!info.IsEnemy && world.Has<ProductionQueue>(u.Entity))
		{
			var prod = world.Get<ProductionQueue>(u.Entity);
			info.HasProduction = true;

			string? trainedUnitId = prod.Get(0);
			if (trainedUnitId != null)
			{
				info.ProductionTitle = string.Format(TranslationServer.Translate("TRAINING: {0}"), trainedUnitId.ToUpper());
				info.ProductionProgress = prod.CurrentProgress;
				info.ProductionMaxProgress = prod.BuildTime;

				UpdateUnitProductionQueue(info, prod);
			}
			else
			{
				info.ProductionTitle = TranslationServer.Translate("PRODUCTION IDLE");
				if (info.ProductionQueue.Count > 0)
				{
					info.ProductionQueue.Clear();
				}
			}
		}
	}

	private void UpdateUnitProductionQueue(SelectedUnitInfo info, ProductionQueue prod)
	{
		bool queueChanged = info.ProductionQueue.Count != prod.Count;
		if (!queueChanged)
		{
			for (int q = 0; q < prod.Count; q++)
			{
				if (info.ProductionQueue[q] != prod.Get(q))
				{
					queueChanged = true;
					break;
				}
			}
		}

		if (queueChanged)
		{
			info.ProductionQueue.Clear();
			prod.CopyTo(info.ProductionQueue);
		}
	}

	private void UpdateUnitAbilities(EcsWorld world, Unit3D u, SelectedUnitInfo info)
	{
		info.Abilities.Clear();

		if (world.Has<AbilityState>(u.Entity))
		{
			var state = world.Get<AbilityState>(u.Entity);
			if (state.Count > 0)
			{
				state.CopyTo(info.Abilities);
				return;
			}
		}

		if (GameHost.TryGetUnitOrBuildingMetadata(u.UnitId, out var regMeta))
		{
			info.Description = regMeta.Description;
			if (regMeta.Abilities != null && regMeta.Abilities.Length > 0)
			{
				info.Abilities.AddRange(regMeta.Abilities);
			}
		}
	}
}