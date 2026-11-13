using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Services;

namespace Realm.ListenServer;

public class MatchPlacementService
{
	private const int Unranked = 0;
	private const int FirstPlace = 1;
	private const int NeutralTeam = 0;

	private readonly SimulationContext _context;
	private readonly int[] _eliminatedTeams = new int[ScriptPlayersState.SlotCount];
	private int _eliminatedTeamCount;

	public MatchPlacementService(SimulationContext context)
	{
		_context = context;
	}

	public void RecordPlayerDefeat(int playerIndex)
	{
		if (!TryReadPlayers(out ScriptPlayersState players)) return;
		if (!players.IsValidSlot(playerIndex)) return;

		int team = players.Get(playerIndex).Team;
		if (team == NeutralTeam) return;
		if (TeamHasActiveMember(players, team)) return;

		RecordEliminatedTeam(team);

		if (_context.IsGameOver) WritePlacements();
	}

	public void WritePlacements()
	{
		if (_eliminatedTeamCount == 0) return;
		if (!TryReadPlayers(out ScriptPlayersState players)) return;

		Entity worldEntity = _context.WorldEntity;
		ref ScriptPlayersState state = ref _context.World.Get<ScriptPlayersState>(worldEntity);

		int survivingTeamCount = CountSurvivingTeams(players);
		for (int slot = 1; slot < ScriptPlayersState.SlotCount; slot++)
		{
			ScriptPlayer player = state.Get(slot);
			if (player.Team == NeutralTeam || string.IsNullOrEmpty(player.Name)) continue;

			int placement = ResolvePlacement(players, player.Team, survivingTeamCount);
			if (placement == Unranked) continue;
			if (player.Placement == placement) continue;

			state.SetPlacement(slot, placement);
			ScriptPlayer updated = state.Get(slot);
			_context.World.GetBuffer<ScriptPlayer>().Set(slot, in updated);
		}
	}

	private int ResolvePlacement(ScriptPlayersState players, int team, int survivingTeamCount)
	{
		if (TeamHasActiveMember(players, team)) return FirstPlace;

		int eliminatedIndex = IndexOfEliminatedTeam(team);
		if (eliminatedIndex < 0) return Unranked;

		return survivingTeamCount + (_eliminatedTeamCount - eliminatedIndex);
	}

	private int CountSurvivingTeams(ScriptPlayersState players)
	{
		Span<int> teams = stackalloc int[ScriptPlayersState.SlotCount];
		int teamCount = 0;

		for (int slot = 1; slot < ScriptPlayersState.SlotCount; slot++)
		{
			ScriptPlayer player = players.Get(slot);
			if (player.Team == NeutralTeam || !player.Active) continue;
			if (ContainsTeam(teams, teamCount, player.Team)) continue;

			teams[teamCount++] = player.Team;
		}

		return teamCount;
	}

	private static bool TeamHasActiveMember(ScriptPlayersState players, int team)
	{
		for (int slot = 1; slot < ScriptPlayersState.SlotCount; slot++)
		{
			ScriptPlayer player = players.Get(slot);
			if (player.Team != team || !player.Active) continue;

			return true;
		}

		return false;
	}

	private static bool ContainsTeam(Span<int> teams, int teamCount, int team)
	{
		for (int index = 0; index < teamCount; index++)
		{
			if (teams[index] == team) return true;
		}

		return false;
	}

	private void RecordEliminatedTeam(int team)
	{
		if (IndexOfEliminatedTeam(team) >= 0) return;

		_eliminatedTeams[_eliminatedTeamCount++] = team;
	}

	private int IndexOfEliminatedTeam(int team)
	{
		for (int index = 0; index < _eliminatedTeamCount; index++)
		{
			if (_eliminatedTeams[index] == team) return index;
		}

		return -1;
	}

	private bool TryReadPlayers(out ScriptPlayersState players)
	{
		players = default;

		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.IsAlive(worldEntity)) return false;
		if (!_context.World.Has<ScriptPlayersState>(worldEntity)) return false;

		players = _context.World.Get<ScriptPlayersState>(worldEntity);
		return true;
	}
}
