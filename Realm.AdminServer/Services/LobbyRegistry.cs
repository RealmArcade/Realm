using System.Collections.Concurrent;
using Realm.AdminServer.Models;

namespace Realm.AdminServer.Services;

public class LobbyRegistry
{
	private readonly ConcurrentDictionary<string, LobbyInfo> _lobbies = new();

	public bool TryRemove(string lobbyId, out LobbyInfo? info)
	{
		bool result = _lobbies.TryRemove(lobbyId, out var tmp);
		info = tmp;
		return result;
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