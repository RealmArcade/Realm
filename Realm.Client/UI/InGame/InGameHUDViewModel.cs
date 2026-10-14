using System;
using System.Collections.Generic;
using Arch.Core;
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
		GameHost tempQualifier = GameHost.Instance;
		if ((tempQualifier != null ? GameContext.World : null) == null)
			return Entity.Null;

		if (IsPlayingNormally())
			return GameHost.Instance._playerEntity;

		int targetPeerId = GetTargetPeerId();
		if (targetPeerId == -1)
			return GameHost.Instance._playerEntity;

		return GetSpectatedPlayer(targetPeerId);
	}

	private bool IsPlayingNormally()
	{
		bool isSpectator = LobbyManager.Instance?.LocalPlayer?.Team == "Spectator";
		bool isPlayingReplay = ReplayPlaybackManager.Instance.IsPlayingReplay;
		return !isSpectator && !isPlayingReplay;
	}

	private int GetTargetPeerId()
	{
		if (ReplayPlaybackManager.Instance.IsPlayingReplay)
			return ReplayPlaybackManager.Instance.SpectatorPerspective;

		return InGameHUD.Instance?.LiveSpectatorPerspective ?? -1;
	}

	private Entity GetSpectatedPlayer(int targetPeerId)
	{
		var world = GameContext.World;
		var worldEntity = ECSContext.WorldEntity;

		if (worldEntity == Entity.Null) return GameHost.Instance._playerEntity;
		if (!world.IsAlive(worldEntity)) return GameHost.Instance._playerEntity;
		if (!world.Has<NetworkMappingState>(worldEntity)) return GameHost.Instance._playerEntity;

		var mapping = world.Get<NetworkMappingState>(worldEntity);
		if (mapping.PeerIdToPlayerEntityMap == null) return GameHost.Instance._playerEntity;
		if (!mapping.PeerIdToPlayerEntityMap.TryGetValue(targetPeerId, out var spectatedPlayer)) return GameHost.Instance._playerEntity;

		return spectatedPlayer;
	}

	public float Gold
	{
		get
		{
			if (GameHost.Instance != null && GameContext.World != null)
			{
				var world = GameContext.World;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerResources>(player))
				{
					var res = world.Get<PlayerResources>(player);
					var goldId = "gold".AsResourceId(ECSContext.DefinitionManager);
					if (res.Value.TryGetValue(goldId, out var val)) return val;
				}
			}

			return 500f;
		}
		set
		{
			if (GameHost.Instance != null && GameContext.World != null)
			{
				var world = GameContext.World;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerResources>(player))
				{
					ref var res = ref world.Get<PlayerResources>(player);
					var goldId = "gold".AsResourceId(ECSContext.DefinitionManager);
					res.Value[goldId] = (int)value;
				}
			}
		}
	}

	public float Wood
	{
		get
		{
			if (GameHost.Instance != null && GameContext.World != null)
			{
				var world = GameContext.World;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerResources>(player))
				{
					var res = world.Get<PlayerResources>(player);
					var woodId = "wood".AsResourceId(ECSContext.DefinitionManager);
					if (res.Value.TryGetValue(woodId, out var val)) return val;
				}
			}

			return 400f;
		}
		set
		{
			if (GameHost.Instance != null && GameContext.World != null)
			{
				var world = GameContext.World;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerResources>(player))
				{
					ref var res = ref world.Get<PlayerResources>(player);
					var woodId = "wood".AsResourceId(ECSContext.DefinitionManager);
					res.Value[woodId] = (int)value;
				}
			}
		}
	}

	public float Stone
	{
		get
		{
			if (GameHost.Instance != null && GameContext.World != null)
			{
				var world = GameContext.World;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerResources>(player))
				{
					var res = world.Get<PlayerResources>(player);
					var stoneId = "stone".AsResourceId(ECSContext.DefinitionManager);
					if (res.Value.TryGetValue(stoneId, out var val)) return val;
				}
			}

			return 200f;
		}
		set
		{
			if (GameHost.Instance != null && GameContext.World != null)
			{
				var world = GameContext.World;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerResources>(player))
				{
					ref var res = ref world.Get<PlayerResources>(player);
					var stoneId = "stone".AsResourceId(ECSContext.DefinitionManager);
					res.Value[stoneId] = (int)value;
				}
			}
		}
	}

	public float ResourceGatherMultiplier
	{
		get
		{
			if (GameHost.Instance != null && GameContext.World != null)
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
			if (GameHost.Instance != null && GameContext.World != null)
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
			if (GameHost.Instance != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
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
			if (GameHost.Instance != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
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
			if (GameHost.Instance != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
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
			if (GameHost.Instance != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
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
			if (GameHost.Instance != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
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
			if (GameHost.Instance != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
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

	public bool LeaderboardVisible
	{
		get
		{
			if (GameHost.Instance != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(ECSContext.WorldEntity) && world.Has<LeaderboardState>(ECSContext.WorldEntity))
				{
					return world.Get<LeaderboardState>(ECSContext.WorldEntity).Visible;
				}
			}

			return false;
		}
		set
		{
			if (GameHost.Instance != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(ECSContext.WorldEntity) && world.Has<LeaderboardState>(ECSContext.WorldEntity))
				{
					ref var lb = ref world.Get<LeaderboardState>(ECSContext.WorldEntity);
					lb.Visible = value;
				}
			}
		}
	}

	public string LeaderboardTitle
	{
		get
		{
			if (GameHost.Instance != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(ECSContext.WorldEntity) && world.Has<LeaderboardState>(ECSContext.WorldEntity))
				{
					return world.Get<LeaderboardState>(ECSContext.WorldEntity).Title;
				}
			}

			return "";
		}
		set
		{
			if (GameHost.Instance != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(ECSContext.WorldEntity) && world.Has<LeaderboardState>(ECSContext.WorldEntity))
				{
					ref var lb = ref world.Get<LeaderboardState>(ECSContext.WorldEntity);
					lb.Title = value;
				}
			}
		}
	}

	public Dictionary<string, string> LeaderboardValues
	{
		get
		{
			if (GameHost.Instance != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(ECSContext.WorldEntity) && world.Has<LeaderboardState>(ECSContext.WorldEntity))
				{
					return world.Get<LeaderboardState>(ECSContext.WorldEntity).Values;
				}
			}

			return new Dictionary<string, string>();
		}
	}

	private static ref SummaryTableState EnsureSummaryTableState(World world, Entity entity)
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
			if (GameHost.Instance != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
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
			if (GameHost.Instance != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
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
			if (GameHost.Instance != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
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
			if (GameHost.Instance != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
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
			if (GameHost.Instance != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
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
			if (GameHost.Instance != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
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
			if (GameHost.Instance != null && GameContext.World != null && ECSContext.WorldEntity != Entity.Null)
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
	public string ShroudType { get; set; } = "VisionShroud";
	public bool ShowMinimapTerrain { get; set; } = true;

	private static readonly byte[,] _emptyShroudGrid = new byte[32, 32];
	public byte[,] ShroudGrid { get; set; } = _emptyShroudGrid;

	public byte[,] FogGrid
	{
		get => ShroudGrid;
		set => ShroudGrid = value;
	}

	public int IdleCount { get; set; }

	public bool IsBuildSubMenuOpen { get; set; }

	private int _lastClockMins = -1;
	private int _lastClockSecs = -1;
	private int _lastClockPhase = -1;


	public void Update(double delta)
	{
		GoldPerSec = 1.5f * ResourceGatherMultiplier;
		WoodPerSec = 1.0f * ResourceGatherMultiplier;
		StonePerSec = 0.8f * ResourceGatherMultiplier;

		if (GameHost.Instance == null)
			return;

		UpdatePopulation();
		UpdateClock();

		IsConnectionLost = GameHost.Instance.IsConnectionLost;
		CycleSelectionIndex = GameHost.Instance.CycleSelectionIndex;

		UpdateIdleCount();
	}

	private void UpdatePopulation()
	{
		var world = GameContext.World;
		if (world == null)
		{
			CurrentPopulation = GameHost.Instance.CurrentPopulation;
			MaxPopulation = GameHost.Instance.MaxPopulation;
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

		CurrentPopulation = GameHost.Instance.CurrentPopulation;
		MaxPopulation = GameHost.Instance.MaxPopulation;
	}

	private void UpdateClock()
	{
		float t = GameHost.Instance.GameElapsedTime;
		int mins = (int)(t / 60);
		int secs = (int)(t % 60);
		int phaseIdx = GameHost.Instance.TimeOfDayIndex;

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

		int idleCount = 0;
		world.Query(in QueryCache.AllIdleMovableQuery, (Entity entity, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value == targetPlayer)
				idleCount++;
		});

		IdleCount = idleCount;
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

		if (GameHost.Instance == null || !GameContext.World.IsAlive(u.Entity))
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
		info.IsEnemy = u.IsEnemy;
		info.IsBuilding = u.IsBuilding;
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

	private void UpdateUnitBasicStats(World world, Unit3D u, SelectedUnitInfo info)
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

		if (world.Has<Armor>(u.Entity))
			info.Armor = world.Get<Armor>(u.Entity).Value;

		if (world.Has<MovementStats>(u.Entity))
			info.Speed = world.Get<MovementStats>(u.Entity).Speed;
	}

	private void UpdateUnitStateText(World world, Unit3D u, SelectedUnitInfo info)
	{
		info.StateText = GetBaseStateText(world, u);

		if (u.UnitId == "tower" && world.Has<TowerUpgradeLevel>(u.Entity))
		{
			int lvl = world.Get<TowerUpgradeLevel>(u.Entity).Value;
			info.StateText += $"   ★ {TranslationServer.Translate("LVL")} {lvl}";
		}
	}

	private string GetBaseStateText(World world, Unit3D u)
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

	private string GetMovementOrAttackStateText(World world, Entity entity)
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

	private void UpdateUnitInventory(World world, Unit3D u, SelectedUnitInfo info)
	{
		if (!world.Has<Inventory>(u.Entity))
			return;

		var inv = world.Get<Inventory>(u.Entity);
		info.InventoryItems.Clear();
		if (inv.Items != null)
		{
			foreach (var kvp in inv.Items)
			{
				info.InventoryItems[kvp.Key] = kvp.Value;
			}
		}
	}

	private void UpdateUnitProduction(World world, Unit3D u, SelectedUnitInfo info)
	{
		if (!u.IsBuilding)
			return;

		if (!u.IsEnemy && world.Has<ConstructionState>(u.Entity))
		{
			var cs = world.Get<ConstructionState>(u.Entity);
			info.HasProduction = true;
			info.ProductionTitle = TranslationServer.Translate("UNDER CONSTRUCTION");
			info.ProductionProgress = cs.Progress;
			info.ProductionMaxProgress = cs.TotalBuildTime;
		}
		else if (u.UnitId == "castle" && world.Has<ProductionQueue>(u.Entity))
		{
			var prod = world.Get<ProductionQueue>(u.Entity);
			info.HasProduction = true;

			if (prod.UnitIds.Count > 0)
			{
				info.ProductionTitle = string.Format(TranslationServer.Translate("TRAINING: {0}"), prod.UnitIds[0].ToUpper());
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
		bool queueChanged = info.ProductionQueue.Count != prod.UnitIds.Count;
		if (!queueChanged)
		{
			for (int q = 0; q < prod.UnitIds.Count; q++)
			{
				if (info.ProductionQueue[q] != prod.UnitIds[q])
				{
					queueChanged = true;
					break;
				}
			}
		}

		if (queueChanged)
		{
			info.ProductionQueue.Clear();
			info.ProductionQueue.AddRange(prod.UnitIds);
		}
	}

	private void UpdateUnitAbilities(World world, Unit3D u, SelectedUnitInfo info)
	{
		info.Abilities.Clear();

		if (world.Has<AbilityState>(u.Entity))
		{
			var state = world.Get<AbilityState>(u.Entity);
			if (state.Abilities != null && state.Abilities.Count > 0)
			{
				info.Abilities.AddRange(state.Abilities);
				return;
			}
		}

		if (GameHost.UnitRegistry.TryGetValue(u.UnitId, out var regMeta))
		{
			info.Description = regMeta.Description;
			if (regMeta.Abilities != null && regMeta.Abilities.Length > 0)
			{
				info.Abilities.AddRange(regMeta.Abilities);
			}
		}
	}
}