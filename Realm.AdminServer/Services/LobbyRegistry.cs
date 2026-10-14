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