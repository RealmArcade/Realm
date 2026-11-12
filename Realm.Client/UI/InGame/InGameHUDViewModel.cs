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

public partial class InGameHUDViewModel
{
	private Entity GetTargetPlayerEntity()
	{
		if (GameContext.Host is not { } host || GameContext.World == null)
			return Entity.Null;

		if (IsPlayingNormally())
			return host.GetPlayerEntityForPlayerIndex(GameContext.PerspectiveFilter.ControlledPlayerIndex);

		if (GameContext.ReplayPlayback.IsPlayingReplay)
		{
			int replayPeerId = GameContext.ReplayPlayback.SpectatorPerspective;
			if (replayPeerId == -1) return Entity.Null;
			return GetSpectatedPlayer(replayPeerId);
		}

		int livePerspective = GameContext.InGameHud?.LiveSpectatorPerspective ?? -1;
		if (livePerspective <= 0)
			return Entity.Null;

		return host.GetPlayerEntityForPlayerIndex(livePerspective);
	}

	private bool IsPlayingNormally()
	{
		bool isSpectator = GameContext.PerspectiveFilter.IsSpectating;
		bool isPlayingReplay = GameContext.ReplayPlayback.IsPlayingReplay;
		return !isSpectator && !isPlayingReplay;
	}

	private Entity GetSpectatedPlayer(int targetPeerId)
	{
		var world = GameContext.World
		            ?? throw new InvalidOperationException($"No player entity is mapped for peer {targetPeerId}.");
		var worldEntity = GameContext.Simulation.WorldEntity;
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
			if (GameContext.Host != null && GameContext.World != null && GameContext.Simulation.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(GameContext.Simulation.WorldEntity) && world.Has<CountdownState>(GameContext.Simulation.WorldEntity))
				{
					return world.Get<CountdownState>(GameContext.Simulation.WorldEntity).Active;
				}
			}

			return false;
		}
		set
		{
			if (GameContext.Host != null && GameContext.World != null && GameContext.Simulation.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(GameContext.Simulation.WorldEntity) && world.Has<CountdownState>(GameContext.Simulation.WorldEntity))
				{
					ref var countdown = ref world.Get<CountdownState>(GameContext.Simulation.WorldEntity);
					countdown.Active = value;
				}
			}
		}
	}

	public float CountdownDuration
	{
		get
		{
			if (GameContext.Host != null && GameContext.World != null && GameContext.Simulation.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(GameContext.Simulation.WorldEntity) && world.Has<CountdownState>(GameContext.Simulation.WorldEntity))
				{
					return world.Get<CountdownState>(GameContext.Simulation.WorldEntity).Duration;
				}
			}

			return 0f;
		}
		set
		{
			if (GameContext.Host != null && GameContext.World != null && GameContext.Simulation.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(GameContext.Simulation.WorldEntity) && world.Has<CountdownState>(GameContext.Simulation.WorldEntity))
				{
					ref var countdown = ref world.Get<CountdownState>(GameContext.Simulation.WorldEntity);
					countdown.Duration = value;
				}
			}
		}
	}

	public string CountdownText
	{
		get
		{
			if (GameContext.Host != null && GameContext.World != null && GameContext.Simulation.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(GameContext.Simulation.WorldEntity) && world.Has<CountdownState>(GameContext.Simulation.WorldEntity))
				{
					return world.Get<CountdownState>(GameContext.Simulation.WorldEntity).Text;
				}
			}

			return "";
		}
		set
		{
			if (GameContext.Host != null && GameContext.World != null && GameContext.Simulation.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(GameContext.Simulation.WorldEntity) && world.Has<CountdownState>(GameContext.Simulation.WorldEntity))
				{
					ref var countdown = ref world.Get<CountdownState>(GameContext.Simulation.WorldEntity);
					countdown.Text = value;
				}
			}
		}
	}

	public LeaderboardBoards Leaderboards
	{
		get
		{
			if (GameContext.Host != null && GameContext.World != null && GameContext.Simulation.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(GameContext.Simulation.WorldEntity) && world.Has<LeaderboardState>(GameContext.Simulation.WorldEntity))
				{
					LeaderboardBoards? boards = world.Get<LeaderboardState>(GameContext.Simulation.WorldEntity).Boards;
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

	/// <summary>
	///     The pages the Instructions menu offers, in the order the authority published them. They belong to this screen's
	///     presentation only: a page states a rule, it does not change one.
	/// </summary>
	public List<InstructionsSection> InstructionSections { get; } = new();

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
			if (GameContext.Host != null && GameContext.World != null && GameContext.Simulation.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(GameContext.Simulation.WorldEntity) && world.Has<SummaryTableState>(GameContext.Simulation.WorldEntity))
				{
					return world.Get<SummaryTableState>(GameContext.Simulation.WorldEntity).Visible;
				}
			}

			return false;
		}
		set
		{
			if (GameContext.Host != null && GameContext.World != null && GameContext.Simulation.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(GameContext.Simulation.WorldEntity))
				{
					ref var st = ref EnsureSummaryTableState(world, GameContext.Simulation.WorldEntity);
					st.Visible = value;
				}
			}
		}
	}

	public string SummaryTableTitle
	{
		get
		{
			if (GameContext.Host != null && GameContext.World != null && GameContext.Simulation.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(GameContext.Simulation.WorldEntity) && world.Has<SummaryTableState>(GameContext.Simulation.WorldEntity))
				{
					return world.Get<SummaryTableState>(GameContext.Simulation.WorldEntity).Title;
				}
			}

			return "Stats";
		}
		set
		{
			if (GameContext.Host != null && GameContext.World != null && GameContext.Simulation.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(GameContext.Simulation.WorldEntity))
				{
					ref var st = ref EnsureSummaryTableState(world, GameContext.Simulation.WorldEntity);
					st.Title = value;
				}
			}
		}
	}

	public string[] SummaryTableHeaders
	{
		get
		{
			if (GameContext.Host != null && GameContext.World != null && GameContext.Simulation.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(GameContext.Simulation.WorldEntity) && world.Has<SummaryTableState>(GameContext.Simulation.WorldEntity))
				{
					return world.Get<SummaryTableState>(GameContext.Simulation.WorldEntity).Headers ?? Array.Empty<string>();
				}
			}

			return Array.Empty<string>();
		}
		set
		{
			if (GameContext.Host != null && GameContext.World != null && GameContext.Simulation.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(GameContext.Simulation.WorldEntity))
				{
					ref var st = ref EnsureSummaryTableState(world, GameContext.Simulation.WorldEntity);
					st.Headers = value;
				}
			}
		}
	}

	public Dictionary<string, string[]> SummaryTableRows
	{
		get
		{
			if (GameContext.Host != null && GameContext.World != null && GameContext.Simulation.WorldEntity != Entity.Null)
			{
				var world = GameContext.World;
				if (world.IsAlive(GameContext.Simulation.WorldEntity))
				{
					ref var st = ref EnsureSummaryTableState(world, GameContext.Simulation.WorldEntity);
					return st.Rows;
				}
			}

			return _cachedEmptySummaryRows;
		}
	}

	private static readonly Dictionary<string, string[]> _cachedEmptySummaryRows = new();

	public List<SelectedUnitInfo> SelectedUnits { get; } = new();
	public Prop3D? SelectedProp { get; set; }
	public LootItem3D? SelectedLootItem { get; set; }
	public Entity SelectedResourceNode { get; set; } = Entity.Null;
	public string SelectedResourceNodePropId { get; set; } = string.Empty;
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
		if (GameContext.Host is not { } host) return;

		float t = host.GameElapsedTime;
		int mins = (int)(t / 60);
		int secs = (int)(t % 60);
		int phaseIdx = host.TimeOfDayIndex;

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

}