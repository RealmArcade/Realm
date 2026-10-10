using Arch.Core;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Client.ReplaySystem;
using System;
using System.Collections.Generic;

namespace Realm.Client.UI.InGame;

public class InGameHUDViewModel
{
	private Entity GetTargetPlayerEntity()
	{
		if (Realm.Client.Core.GameHost.Instance?.EcsWorld == null)
			return Entity.Null;

		if (IsPlayingNormally())
			return Realm.Client.Core.GameHost.Instance.PlayerEntity;

		int targetPeerId = GetTargetPeerId();
		if (targetPeerId == -1)
			return Realm.Client.Core.GameHost.Instance.PlayerEntity;

		return GetSpectatedPlayer(targetPeerId);
	}

	private bool IsPlayingNormally()
	{
		bool isSpectator = Network.LobbyManager.Instance?.LocalPlayer?.Team == "Spectator";
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
		var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
		var worldEntity = Realm.Client.Core.GameHost.Instance.WorldEntity;

		if (worldEntity == Entity.Null) return Realm.Client.Core.GameHost.Instance.PlayerEntity;
		if (!world.IsAlive(worldEntity)) return Realm.Client.Core.GameHost.Instance.PlayerEntity;
		if (!world.Has<NetworkMappingState>(worldEntity)) return Realm.Client.Core.GameHost.Instance.PlayerEntity;

		var mapping = world.Get<NetworkMappingState>(worldEntity);
		if (mapping.PeerIdToPlayerEntityMap == null) return Realm.Client.Core.GameHost.Instance.PlayerEntity;
		if (!mapping.PeerIdToPlayerEntityMap.TryGetValue(targetPeerId, out var spectatedPlayer)) return Realm.Client.Core.GameHost.Instance.PlayerEntity;

		return spectatedPlayer;
	}

	public float Gold
	{
		get
		{
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerResources>(player))
				{
					var res = world.Get<PlayerResources>(player);
					var goldId = "gold".AsResourceId(Realm.Client.Core.GameHost.Instance.DefinitionManager);
					if (res.Value.TryGetValue(goldId, out var val)) return val;
				}
			}
			return 500f;
		}
		set
		{
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerResources>(player))
				{
					ref var res = ref world.Get<PlayerResources>(player);
					var goldId = "gold".AsResourceId(Realm.Client.Core.GameHost.Instance.DefinitionManager);
					res.Value[goldId] = (int)value;
				}
			}
		}
	}

	public float Wood
	{
		get
		{
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerResources>(player))
				{
					var res = world.Get<PlayerResources>(player);
					var woodId = "wood".AsResourceId(Realm.Client.Core.GameHost.Instance.DefinitionManager);
					if (res.Value.TryGetValue(woodId, out var val)) return val;
				}
			}
			return 400f;
		}
		set
		{
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerResources>(player))
				{
					ref var res = ref world.Get<PlayerResources>(player);
					var woodId = "wood".AsResourceId(Realm.Client.Core.GameHost.Instance.DefinitionManager);
					res.Value[woodId] = (int)value;
				}
			}
		}
	}

	public float Stone
	{
		get
		{
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerResources>(player))
				{
					var res = world.Get<PlayerResources>(player);
					var stoneId = "stone".AsResourceId(Realm.Client.Core.GameHost.Instance.DefinitionManager);
					if (res.Value.TryGetValue(stoneId, out var val)) return val;
				}
			}
			return 200f;
		}
		set
		{
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerResources>(player))
				{
					ref var res = ref world.Get<PlayerResources>(player);
					var stoneId = "stone".AsResourceId(Realm.Client.Core.GameHost.Instance.DefinitionManager);
					res.Value[stoneId] = (int)value;
				}
			}
		}
	}

	public float ResourceGatherMultiplier
	{
		get
		{
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
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
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
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
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null && Realm.Client.Core.GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
				if (world.IsAlive(Realm.Client.Core.GameHost.Instance.WorldEntity) && world.Has<CountdownState>(Realm.Client.Core.GameHost.Instance.WorldEntity))
				{
					return world.Get<CountdownState>(Realm.Client.Core.GameHost.Instance.WorldEntity).Active;
				}
			}
			return false;
		}
		set
		{
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null && Realm.Client.Core.GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
				if (world.IsAlive(Realm.Client.Core.GameHost.Instance.WorldEntity) && world.Has<CountdownState>(Realm.Client.Core.GameHost.Instance.WorldEntity))
				{
					ref var countdown = ref world.Get<CountdownState>(Realm.Client.Core.GameHost.Instance.WorldEntity);
					countdown.Active = value;
				}
			}
		}
	}

	public float CountdownDuration
	{
		get
		{
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null && Realm.Client.Core.GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
				if (world.IsAlive(Realm.Client.Core.GameHost.Instance.WorldEntity) && world.Has<CountdownState>(Realm.Client.Core.GameHost.Instance.WorldEntity))
				{
					return world.Get<CountdownState>(Realm.Client.Core.GameHost.Instance.WorldEntity).Duration;
				}
			}
			return 0f;
		}
		set
		{
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null && Realm.Client.Core.GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
				if (world.IsAlive(Realm.Client.Core.GameHost.Instance.WorldEntity) && world.Has<CountdownState>(Realm.Client.Core.GameHost.Instance.WorldEntity))
				{
					ref var countdown = ref world.Get<CountdownState>(Realm.Client.Core.GameHost.Instance.WorldEntity);
					countdown.Duration = value;
				}
			}
		}
	}

	public string CountdownText
	{
		get
		{
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null && Realm.Client.Core.GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
				if (world.IsAlive(Realm.Client.Core.GameHost.Instance.WorldEntity) && world.Has<CountdownState>(Realm.Client.Core.GameHost.Instance.WorldEntity))
				{
					return world.Get<CountdownState>(Realm.Client.Core.GameHost.Instance.WorldEntity).Text;
				}
			}
			return "";
		}
		set
		{
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null && Realm.Client.Core.GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
				if (world.IsAlive(Realm.Client.Core.GameHost.Instance.WorldEntity) && world.Has<CountdownState>(Realm.Client.Core.GameHost.Instance.WorldEntity))
				{
					ref var countdown = ref world.Get<CountdownState>(Realm.Client.Core.GameHost.Instance.WorldEntity);
					countdown.Text = value;
				}
			}
		}
	}

	public bool LeaderboardVisible
	{
		get
		{
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null && Realm.Client.Core.GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
				if (world.IsAlive(Realm.Client.Core.GameHost.Instance.WorldEntity) && world.Has<LeaderboardState>(Realm.Client.Core.GameHost.Instance.WorldEntity))
				{
					return world.Get<LeaderboardState>(Realm.Client.Core.GameHost.Instance.WorldEntity).Visible;
				}
			}
			return false;
		}
		set
		{
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null && Realm.Client.Core.GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
				if (world.IsAlive(Realm.Client.Core.GameHost.Instance.WorldEntity) && world.Has<LeaderboardState>(Realm.Client.Core.GameHost.Instance.WorldEntity))
				{
					ref var lb = ref world.Get<LeaderboardState>(Realm.Client.Core.GameHost.Instance.WorldEntity);
					lb.Visible = value;
				}
			}
		}
	}

	public string LeaderboardTitle
	{
		get
		{
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null && Realm.Client.Core.GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
				if (world.IsAlive(Realm.Client.Core.GameHost.Instance.WorldEntity) && world.Has<LeaderboardState>(Realm.Client.Core.GameHost.Instance.WorldEntity))
				{
					return world.Get<LeaderboardState>(Realm.Client.Core.GameHost.Instance.WorldEntity).Title;
				}
			}
			return "";
		}
		set
		{
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null && Realm.Client.Core.GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
				if (world.IsAlive(Realm.Client.Core.GameHost.Instance.WorldEntity) && world.Has<LeaderboardState>(Realm.Client.Core.GameHost.Instance.WorldEntity))
				{
					ref var lb = ref world.Get<LeaderboardState>(Realm.Client.Core.GameHost.Instance.WorldEntity);
					lb.Title = value;
				}
			}
		}
	}

	public Dictionary<string, string> LeaderboardValues
	{
		get
		{
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null && Realm.Client.Core.GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
				if (world.IsAlive(Realm.Client.Core.GameHost.Instance.WorldEntity) && world.Has<LeaderboardState>(Realm.Client.Core.GameHost.Instance.WorldEntity))
				{
					return world.Get<LeaderboardState>(Realm.Client.Core.GameHost.Instance.WorldEntity).Values;
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
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null && Realm.Client.Core.GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
				if (world.IsAlive(Realm.Client.Core.GameHost.Instance.WorldEntity) && world.Has<SummaryTableState>(Realm.Client.Core.GameHost.Instance.WorldEntity))
				{
					return world.Get<SummaryTableState>(Realm.Client.Core.GameHost.Instance.WorldEntity).Visible;
				}
			}
			return false;
		}
		set
		{
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null && Realm.Client.Core.GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
				if (world.IsAlive(Realm.Client.Core.GameHost.Instance.WorldEntity))
				{
					ref var st = ref EnsureSummaryTableState(world, Realm.Client.Core.GameHost.Instance.WorldEntity);
					st.Visible = value;
				}
			}
		}
	}

	public string SummaryTableTitle
	{
		get
		{
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null && Realm.Client.Core.GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
				if (world.IsAlive(Realm.Client.Core.GameHost.Instance.WorldEntity) && world.Has<SummaryTableState>(Realm.Client.Core.GameHost.Instance.WorldEntity))
				{
					return world.Get<SummaryTableState>(Realm.Client.Core.GameHost.Instance.WorldEntity).Title;
				}
			}
			return "Stats";
		}
		set
		{
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null && Realm.Client.Core.GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
				if (world.IsAlive(Realm.Client.Core.GameHost.Instance.WorldEntity))
				{
					ref var st = ref EnsureSummaryTableState(world, Realm.Client.Core.GameHost.Instance.WorldEntity);
					st.Title = value;
				}
			}
		}
	}

	public string[] SummaryTableHeaders
	{
		get
		{
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null && Realm.Client.Core.GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
				if (world.IsAlive(Realm.Client.Core.GameHost.Instance.WorldEntity) && world.Has<SummaryTableState>(Realm.Client.Core.GameHost.Instance.WorldEntity))
				{
					return world.Get<SummaryTableState>(Realm.Client.Core.GameHost.Instance.WorldEntity).Headers ?? Array.Empty<string>();
				}
			}
			return Array.Empty<string>();
		}
		set
		{
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null && Realm.Client.Core.GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
				if (world.IsAlive(Realm.Client.Core.GameHost.Instance.WorldEntity))
				{
					ref var st = ref EnsureSummaryTableState(world, Realm.Client.Core.GameHost.Instance.WorldEntity);
					st.Headers = value;
				}
			}
		}
	}

	public Dictionary<string, string[]> SummaryTableRows
	{
		get
		{
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EcsWorld != null && Realm.Client.Core.GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
				if (world.IsAlive(Realm.Client.Core.GameHost.Instance.WorldEntity))
				{
					ref var st = ref EnsureSummaryTableState(world, Realm.Client.Core.GameHost.Instance.WorldEntity);
					return st.Rows;
				}
			}
			return _cachedEmptySummaryRows;
		}
	}
	private static readonly Dictionary<string, string[]> _cachedEmptySummaryRows = new();

	public List<SelectedUnitInfo> SelectedUnits { get; } = new();
	public Realm.Client.Prop3D SelectedProp { get; set; }
	public int CycleSelectionIndex { get; set; }

	public bool IsChatActive { get; set; }


	public string CurrentWeather { get; set; } = "clear";
	public string ShroudType { get; set; } = "VisionShroud";
	public bool ShowMinimapTerrain { get; set; } = true;
	
	private static readonly byte[,] _emptyShroudGrid = new byte[32, 32];
	public byte[,] ShroudGrid { get; set; } = _emptyShroudGrid;
	public byte[,] FogGrid { get => ShroudGrid; set => ShroudGrid = value; }

	public int IdleCount { get; set; }

	public bool IsBuildSubMenuOpen { get; set; }

	private int _lastClockMins = -1;
	private int _lastClockSecs = -1;
	private int _lastClockPhase = -1;

	public class SelectedUnitInfo
	{
		public Entity Entity { get; set; }
		public string UnitId { get; set; }
		public string Name { get; set; }
		public float Health { get; set; }
		public float MaxHealth { get; set; }
		public float Damage { get; set; }
		public float Range { get; set; }
		public float Armor { get; set; }
		public float Speed { get; set; }
		public float Dps { get; set; }
		public bool IsEnemy { get; set; }
		public bool IsBuilding { get; set; }
		public bool IsUnderConstruction { get; set; }
		public string StateText { get; set; }
		public string Description { get; set; }
		public List<string> Abilities { get; set; } = new();
		public int Potions { get; set; }
		public Dictionary<string, int> InventoryItems { get; set; } = new(StringComparer.OrdinalIgnoreCase);

		public bool HasProduction { get; set; }
		public string ProductionTitle { get; set; }
		public float ProductionProgress { get; set; }
		public float ProductionMaxProgress { get; set; }
		public List<string> ProductionQueue { get; set; } = new();
	}

	public void Update(double delta)
	{
		GoldPerSec = 1.5f * ResourceGatherMultiplier;
		WoodPerSec = 1.0f * ResourceGatherMultiplier;
		StonePerSec = 0.8f * ResourceGatherMultiplier;

		if (Realm.Client.Core.GameHost.Instance == null)
			return;

		UpdatePopulation();
		UpdateClock();

		IsConnectionLost = Realm.Client.Core.GameHost.Instance.IsConnectionLost;
		CycleSelectionIndex = Realm.Client.Core.GameHost.Instance.CycleSelectionIndex;

		UpdateIdleCount();
	}

	private void UpdatePopulation()
	{
		var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
		if (world == null)
		{
			CurrentPopulation = Realm.Client.Core.GameHost.Instance.CurrentPopulation;
			MaxPopulation = Realm.Client.Core.GameHost.Instance.MaxPopulation;
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

		CurrentPopulation = Realm.Client.Core.GameHost.Instance.CurrentPopulation;
		MaxPopulation = Realm.Client.Core.GameHost.Instance.MaxPopulation;
	}

	private void UpdateClock()
	{
		float t = Realm.Client.Core.GameHost.Instance.GameElapsedTime;
		int mins = (int)(t / 60);
		int secs = (int)(t % 60);
		int phaseIdx = Realm.Client.Core.GameHost.Instance.TimeOfDayIndex;
		
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
		var world = Realm.Client.Core.GameHost.Instance.EcsWorld;
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
		world.Query(in Realm.Ecs.Common.QueryCache.AllIdleMovableQuery, (Entity entity, ref Owner owner) => {
			if (owner.PlayerEntity.Value == targetPlayer)
				idleCount++;
		});
		
		IdleCount = idleCount;
	}

	public void UpdateSelectedUnits(List<Realm.Client.Unit3D> selectedUnits)
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

	private void UpdateSingleSelectedUnit(Realm.Client.Unit3D u, SelectedUnitInfo info)
	{
		bool entityChanged = info.Entity != u.Entity;

		InitializeUnitInfo(u, info, entityChanged);

		if (Realm.Client.Core.GameHost.Instance == null || !Realm.Client.Core.GameHost.Instance.EcsWorld.IsAlive(u.Entity))
			return;

		var world = Realm.Client.Core.GameHost.Instance.EcsWorld;

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

	private void InitializeUnitInfo(Realm.Client.Unit3D u, SelectedUnitInfo info, bool entityChanged)
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

	private void UpdateUnitBasicStats(World world, Realm.Client.Unit3D u, SelectedUnitInfo info)
	{
		if (world.Has<Name>(u.Entity))
			info.Name = world.Get<Name>(u.Entity).Value;

		if (u.IsBuilding && world.Has<Realm.Ecs.Components.Tags.UnderConstruction>(u.Entity))
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

	private void UpdateUnitStateText(World world, Realm.Client.Unit3D u, SelectedUnitInfo info)
	{
		info.StateText = GetBaseStateText(world, u);

		if (u.UnitId == "tower" && world.Has<TowerUpgradeLevel>(u.Entity))
		{
			int lvl = world.Get<TowerUpgradeLevel>(u.Entity).Value;
			info.StateText += $"   ★ {TranslationServer.Translate("LVL")} {lvl}";
		}
	}

	private string GetBaseStateText(World world, Realm.Client.Unit3D u)
	{
		if (world.Has<Gatherer>(u.Entity))
			return GetGathererStateText(world.Get<Gatherer>(u.Entity));

		if (world.Has<Realm.Ecs.Components.Resources.BuildTask>(u.Entity))
			return GetBuildTaskStateText(world.Get<Realm.Ecs.Components.Resources.BuildTask>(u.Entity));

		return GetMovementOrAttackStateText(world, u.Entity);
	}

	private string GetGathererStateText(Gatherer gather)
	{
		string cachedDelivering = TranslationServer.Translate("DELIVERING");
		string cachedHarvesting = TranslationServer.Translate("HARVESTING");
		string stateLabel = gather.ReturningToBase ? "● " + cachedDelivering : "● " + cachedHarvesting;
		return $"{stateLabel} ({gather.CarriedAmount:F0} / {gather.MaxCapacity:F0} {TranslationServer.Translate(gather.ResourceType.ToUpper())})";
	}

	private string GetBuildTaskStateText(Realm.Ecs.Components.Resources.BuildTask bt)
	{
		string cachedConstructing = TranslationServer.Translate("CONSTRUCTING");
		int pct = (int)(bt.Progress / Mathf.Max(bt.TotalBuildTime, 0.001f) * 100f);
		return $"🔨 {cachedConstructing} ({pct}%)";
	}

	private string GetMovementOrAttackStateText(World world, Entity entity)
	{
		if (world.Has<Realm.Ecs.Components.Movement.HoldPosition>(entity))
			return "● " + TranslationServer.Translate("HOLDING");
		
		if (world.Has<Realm.Ecs.Components.Movement.Patrol>(entity))
			return "● " + TranslationServer.Translate("PATROLLING");
		
		if (world.Has<Realm.Ecs.Components.Movement.AttackMove>(entity))
			return "● " + TranslationServer.Translate("ATTACK-MOVE");
		
		if (world.Has<Realm.Ecs.Components.Movement.Follow>(entity))
			return "● " + TranslationServer.Translate("FOLLOWING");
		
		if (world.Has<Realm.Ecs.Components.Movement.MoveTo>(entity))
			return "● " + TranslationServer.Translate("MOVING");
		
		if (world.Has<AttackTarget>(entity))
			return "● " + TranslationServer.Translate("ATTACKING");
		
		return "○ " + TranslationServer.Translate("IDLE");
	}

	private void UpdateUnitInventory(World world, Realm.Client.Unit3D u, SelectedUnitInfo info)
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

	private void UpdateUnitProduction(World world, Realm.Client.Unit3D u, SelectedUnitInfo info)
	{
		if (!u.IsBuilding)
			return;

		if (!u.IsEnemy && world.Has<Realm.Ecs.Components.Resources.ConstructionState>(u.Entity))
		{
			var cs = world.Get<Realm.Ecs.Components.Resources.ConstructionState>(u.Entity);
			info.HasProduction = true;
			info.ProductionTitle = TranslationServer.Translate("UNDER CONSTRUCTION");
			info.ProductionProgress = cs.Progress;
			info.ProductionMaxProgress = cs.TotalBuildTime;
		}
		else if (u.UnitId == "castle" && world.Has<Realm.Ecs.Components.Core.ProductionQueue>(u.Entity))
		{
			var prod = world.Get<Realm.Ecs.Components.Core.ProductionQueue>(u.Entity);
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

	private void UpdateUnitProductionQueue(SelectedUnitInfo info, Realm.Ecs.Components.Core.ProductionQueue prod)
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

	private void UpdateUnitAbilities(World world, Realm.Client.Unit3D u, SelectedUnitInfo info)
	{
		info.Abilities.Clear();
		
		if (world.Has<Realm.Ecs.Components.Core.AbilityState>(u.Entity))
		{
			var state = world.Get<Realm.Ecs.Components.Core.AbilityState>(u.Entity);
			if (state.Abilities != null && state.Abilities.Count > 0)
			{
				info.Abilities.AddRange(state.Abilities);
				return;
			}
		}

		if (Realm.Client.Core.GameHost.UnitRegistry.TryGetValue(u.UnitId, out var regMeta))
		{
			info.Description = regMeta.Description;
			if (regMeta.Abilities != null && regMeta.Abilities.Length > 0)
			{
				info.Abilities.AddRange(regMeta.Abilities);
			}
		}
	}
}