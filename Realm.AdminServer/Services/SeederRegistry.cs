using System.Collections.Concurrent;
using System.Net.WebSockets;
using Realm.AdminServer.Models;

namespace Realm.AdminServer.Services;

public class SeederRegistry
{
	private readonly ConcurrentDictionary<string, SeederInfo> _seeders = new();
	private readonly ConcurrentDictionary<string, WebSocket> _seederConnections = new();
	private readonly ConcurrentDictionary<string, DateTime> _lastSeen = new();

	public void Register(SeederInfo info)
	{
		info.LastSeen = DateTime.UtcNow;
		_seeders[info.SeederId] = info;
		_lastSeen[info.SeederId] = DateTime.UtcNow;
	}

	public void Touch(string seederId)
	{
		_lastSeen[seederId] = DateTime.UtcNow;
		if (_seeders.TryGetValue(seederId, out var info))
		{
			info.LastSeen = DateTime.UtcNow;
		}
	}

	public void Unregister(string seederId)
	{
		_seeders.TryRemove(seederId, out _);
		_seederConnections.TryRemove(seederId, out _);
		_lastSeen.TryRemove(seederId, out _);
	}

	public void AddConnection(string seederId, WebSocket ws)
	{
		_seederConnections[seederId] = ws;
	}

	public void RemoveConnection(string seederId)
	{
		_seederConnections.TryRemove(seederId, out _);
	}

	public List<SeederInfo> GetAll()
	{
		PruneStale();
		return _seeders.Values.ToList();
	}

	/// <summary>
	///     The seeders that may be offered to a player elsewhere: every one this node reached from outside. A LAN-only
	///     seeder stays registered - the players on its own network can still be sent to it - but it is never advertised,
	///     because an endpoint that does not answer is worse than no seeder at all.
	/// </summary>
	/// <returns>The seeders that may be advertised.</returns>
	public List<SeederInfo> GetAdvertised()
	{
		PruneStale();
		return _seeders.Values.Where(seeder => !seeder.LanOnly).ToList();
	}

	/// <summary>One seeder by identifier, or null when this node holds none under that name.</summary>
	/// <param name="seederId">The seeder's identifier.</param>
	/// <returns>The seeder, or null.</returns>
	public SeederInfo? Get(string seederId)
	{
		return seederId != null && _seeders.TryGetValue(seederId, out SeederInfo? seeder) ? seeder : null;
	}

	public List<SeederInfo> GetSeedersForMap(string mapIdOrManifestHash)
	{
		PruneStale();
		if (string.IsNullOrWhiteSpace(mapIdOrManifestHash))
		{
			return new List<SeederInfo>();
		}

		string rawQuery = mapIdOrManifestHash.Trim();
		string normalizedQuery = rawQuery.Replace('_', ' ');

		return _seeders.Values
			.Where(s => !s.LanOnly)
			.Where(s => s.MapIds != null && s.MapIds.Any(m =>
			{
				if (string.IsNullOrWhiteSpace(m)) return false;
				string rawM = m.Trim();
				string normM = rawM.Replace('_', ' ');

				if (string.Equals(rawM, rawQuery, StringComparison.OrdinalIgnoreCase)) return true;
				if (string.Equals(normM, normalizedQuery, StringComparison.OrdinalIgnoreCase)) return true;

				if (rawM.StartsWith(rawQuery + "_", StringComparison.OrdinalIgnoreCase) ||
				    normM.StartsWith(normalizedQuery + " ", StringComparison.OrdinalIgnoreCase)) return true;

				if (rawQuery.StartsWith(rawM + "_", StringComparison.OrdinalIgnoreCase) ||
				    normalizedQuery.StartsWith(normM + " ", StringComparison.OrdinalIgnoreCase)) return true;

				return false;
			}))
			.OrderByDescending(s => s.LastSeen)
			.ToList();
	}

	public WebSocket? GetConnection(string seederId)
	{
		_seederConnections.TryGetValue(seederId, out var ws);
		return ws;
	}

	public void PruneStale()
	{
		var cutoff = DateTime.UtcNow.AddSeconds(-30);
		foreach (var kvp in _lastSeen)
		{
			if (kvp.Value < cutoff)
			{
				Unregister(kvp.Key);
			}
		}
	}
}