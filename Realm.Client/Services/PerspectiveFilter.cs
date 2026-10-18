using System;

namespace Realm.Client.Services;

public class PerspectiveFilter
{
	private enum PerspectiveRole
	{
		Unconfigured,
		Participant,
		Spectator
	}

	private PerspectiveRole _role = PerspectiveRole.Unconfigured;
	private int _controlledPlayerIndex;

	public bool IsConfigured => _role != PerspectiveRole.Unconfigured;

	public bool IsSpectating
	{
		get
		{
			RequireConfigured();
			return _role == PerspectiveRole.Spectator;
		}
	}

	public int ControlledPlayerIndex
	{
		get
		{
			RequireConfigured();
			if (_role != PerspectiveRole.Participant)
				throw new InvalidOperationException("A spectator does not control any player.");
			return _controlledPlayerIndex;
		}
	}

	public bool ObservesAllPlayers
	{
		get
		{
			RequireConfigured();
			return _role == PerspectiveRole.Spectator && ObservedSpectatorPlayerIndex <= 0;
		}
	}

	public int ObservedPlayerIndex
	{
		get
		{
			RequireConfigured();
			if (_role == PerspectiveRole.Participant) return _controlledPlayerIndex;
			int spectated = ObservedSpectatorPlayerIndex;
			if (spectated <= 0)
				throw new InvalidOperationException("The spectator is observing all players; no single observed player exists.");
			return spectated;
		}
	}

	private static int ObservedSpectatorPlayerIndex
	{
		get
		{
			if (GameContext.SpectatorService == null)
				throw new InvalidOperationException("SpectatorService is not available.");
			return GameContext.SpectatorService.GetSpectatorPerspective();
		}
	}

	public void ConfigureParticipant(int playerIndex)
	{
		if (playerIndex <= 0)
			throw new ArgumentOutOfRangeException(nameof(playerIndex), playerIndex, "A participant must control a player index greater than zero.");
		_role = PerspectiveRole.Participant;
		_controlledPlayerIndex = playerIndex;
	}

	public void ConfigureSpectator()
	{
		_role = PerspectiveRole.Spectator;
		_controlledPlayerIndex = -1;
	}

	public void Reset()
	{
		_role = PerspectiveRole.Unconfigured;
		_controlledPlayerIndex = -1;
	}

	public bool CanControl(int playerIndex)
	{
		RequireConfigured();
		return _role == PerspectiveRole.Participant && _controlledPlayerIndex == playerIndex;
	}

	public bool CanObserve(int playerIndex)
	{
		RequireConfigured();
		if (_role == PerspectiveRole.Participant) return _controlledPlayerIndex == playerIndex;
		int spectated = ObservedSpectatorPlayerIndex;
		return spectated <= 0 || spectated == playerIndex;
	}

	public bool IsHostileToObserved(int playerIndex)
	{
		if (ObservesAllPlayers) return false;
		return NetworkService.ArePlayerIndicesEnemies(ObservedPlayerIndex, playerIndex);
	}

	public bool CanSeeOwnedUi(int ownerPlayerIndex)
	{
		RequireConfigured();
		return _role == PerspectiveRole.Spectator || _controlledPlayerIndex == ownerPlayerIndex;
	}

	private void RequireConfigured()
	{
		if (_role == PerspectiveRole.Unconfigured)
			throw new InvalidOperationException("PerspectiveFilter has not been configured for this session.");
	}
}
