using System.Collections.Concurrent;
using Realm.AdminServer.Models;

namespace Realm.AdminServer.Services;

public class LobbyRegistry
{
	/// <summary>
	///     One handover a lobby's current host has permitted, named by a token only that handover is completed with. It
	///     outlives the check that granted it for a short window, so a handover whose screens are slow still completes and a
	///     handover nobody completed grants nothing.
	/// </summary>
	private sealed record HandoverGrant(string Token, int FromEpoch, DateTime ExpiresAtUtc);

	private readonly ConcurrentDictionary<string, LobbyInfo> _lobbies = new();

	private readonly ConcurrentDictionary<string, HandoverGrant> _handoverGrants = new(StringComparer.Ordinal);

	private static readonly TimeSpan HandoverGrantLifetime = TimeSpan.FromSeconds(90);

	/// <summary>
	///     Records that a lobby's current host permitted it to be handed on, and answers the token the screen taking it
	///     over completes it with. A grant a previous attempt left behind is replaced, because only the host the lobby
	///     currently has may permit one.
	/// </summary>
	/// <param name="lobbyId">The lobby being handed over.</param>
	/// <param name="fromEpoch">The ownership the grant is made under.</param>
	/// <returns>The token and the ownership the handover is completed under, or null when the lobby is not listed.</returns>
	public (string Token, int OwnershipEpoch, int ExpiresInSeconds)? BeginHandover(string lobbyId, int fromEpoch)
	{
		if (!_lobbies.ContainsKey(lobbyId)) return null;

		string token = Guid.NewGuid().ToString();
		_handoverGrants[lobbyId] = new HandoverGrant(token, fromEpoch, DateTime.UtcNow + HandoverGrantLifetime);
		return (token, fromEpoch, (int)HandoverGrantLifetime.TotalSeconds);
	}

	/// <summary>
	///     Reads a handover grant and forgets it, which is what makes a completion single-use: a token cannot move a lobby
	///     twice, and one an abandoned handover left behind expires on its own.
	/// </summary>
	/// <param name="lobbyId">The lobby being handed over.</param>
	/// <param name="token">The token the completing screen stated.</param>
	/// <param name="fromEpoch">The ownership the grant was made under.</param>
	/// <returns>Whether the grant is this lobby's, unexpired, and made under that ownership.</returns>
	public bool TryTakeHandoverGrant(string lobbyId, string token, int fromEpoch)
	{
		if (!_handoverGrants.TryGetValue(lobbyId, out HandoverGrant? grant) || grant == null) return false;
		if (!string.Equals(grant.Token, token, StringComparison.Ordinal)) return false;
		if (grant.FromEpoch != fromEpoch) return false;
		if (grant.ExpiresAtUtc < DateTime.UtcNow)
		{
			_handoverGrants.TryRemove(lobbyId, out _);
			return false;
		}

		return _handoverGrants.TryRemove(lobbyId, out _);
	}

	/// <summary>Forgets a lobby's handover grant, which is what closing or losing the lobby does.</summary>
	/// <param name="lobbyId">The lobby that is gone.</param>
	public void ForgetHandoverGrant(string lobbyId) => _handoverGrants.TryRemove(lobbyId, out _);

	public bool TryRemove(string lobbyId, out LobbyInfo? info)
	{
		_handoverGrants.TryRemove(lobbyId, out _);
		return _lobbies.TryRemove(lobbyId, out info);
	}

	public void AddOrUpdate(LobbyInfo info)
	{
		_lobbies[info.LobbyId] = info;
	}

	public bool TryGet(string lobbyId, out LobbyInfo? info)
	{
		return _lobbies.TryGetValue(lobbyId, out info);
	}

	/// <summary>
	///     Replaces a lobby with a changed copy in one step, so a reader of the browser, a joining player or a host message
	///     either sees the lobby as it was or as it became and never half a handover.
	/// </summary>
	/// <param name="expected">The lobby as the caller read it, which is what the change is applied to.</param>
	/// <param name="replacement">The lobby as it should be afterwards.</param>
	/// <returns>Whether the lobby was still the one the caller read.</returns>
	public bool TryReplace(LobbyInfo expected, LobbyInfo replacement) => _lobbies.TryUpdate(replacement.LobbyId, replacement, expected);

	/// <summary>
	///     Reads a lobby, applies a change to a copy of it, and stores the copy, retrying while another writer got there
	///     first. The change is never applied twice, because it is applied to a fresh copy of the lobby as it currently is.
	/// </summary>
	/// <param name="lobbyId">The lobby being changed.</param>
	/// <param name="apply">Applies the change to a copy and answers whether anything changed.</param>
	/// <returns>The lobby afterwards, or null when it is no longer listed.</returns>
	public LobbyInfo? Update(string lobbyId, Func<LobbyInfo, bool> apply)
	{
		for (int attempt = 0; attempt < 8; attempt++)
		{
			if (!_lobbies.TryGetValue(lobbyId, out LobbyInfo? current) || current == null) return null;

			LobbyInfo replacement = current.Clone();
			if (!apply(replacement)) return current;
			if (_lobbies.TryUpdate(lobbyId, replacement, current)) return replacement;
		}

		return null;
	}

	public IEnumerable<LobbyInfo> GetAllLobbies()
	{
		return _lobbies.Values;
	}

	public List<string> PruneExpiredLobbies(TimeSpan expiry)
	{
		var cutoff = DateTime.UtcNow - expiry;
		var expiredIds = new List<string>();

		foreach (var (id, lobby) in _lobbies)
		{
			if (lobby.LastHeartbeat < cutoff)
			{
				expiredIds.Add(id);
			}
		}

		foreach (var id in expiredIds)
		{
			_lobbies.TryRemove(id, out _);
			Console.WriteLine($"[Registry] Pruned expired lobby {id}");
		}

		return expiredIds;
	}
}