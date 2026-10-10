using Realm.AdminServer.Models;
using System.Collections.Concurrent;
using System.Net.WebSockets;

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
