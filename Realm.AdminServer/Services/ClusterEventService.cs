using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using DBreeze.Transactions;
using Realm.AdminServer.Accounts;
using Realm.Shared;
using Realm.Shared.Accounts;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;

namespace Realm.AdminServer.Services;

public partial class ClusterEventService : IClusterStateStore
	{
	private readonly ConcurrentDictionary<string, byte> _processedEvents = new(StringComparer.OrdinalIgnoreCase);
	private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
	private readonly AccountEventApplier _accountEvents;
	private readonly ClusterPeerSync _peerSync;

	public ClusterEventService(AccountAuthorityOptions accountAuthorityOptions)
	{
		_accountEvents = new AccountEventApplier(accountAuthorityOptions);
		_peerSync = new ClusterPeerSync(this);
	}

	/// <summary>Replicates a recorded event to every configured peer in the background.</summary>
	public void BroadcastEvent(ClusterEventDto evt, PeerRegistry peerRegistry, IHttpClientFactory httpClientFactory)
	{
		_peerSync.BroadcastEvent(evt, peerRegistry, httpClientFactory);
	}

	/// <summary>Catches up from peers, or absorbs the majority snapshot when this node has diverged.</summary>
	public Task RunQuorumSyncAsync(PeerRegistry peerRegistry, IHttpClientFactory httpClientFactory, DataStoreService db, ContentAddressableStorage cas, HashSet<string> adminPublicKeys)
	{
		return _peerSync.RunQuorumSyncAsync(peerRegistry, httpClientFactory, db, cas, adminPublicKeys);
	}

	public bool HasEventBeenProcessed(string eventId, DataStoreService db)
	{
		if (string.IsNullOrWhiteSpace(eventId)) return false;
		if (_processedEvents.ContainsKey(eventId)) return true;
		var existing = db.Get<JsonDocument>("processed_events", eventId);
		if (existing != null)
		{
			_processedEvents.TryAdd(eventId, 0);
			return true;
		}

		return false;
	}

	public void MarkEventProcessed(string eventId, DataStoreService db)
	{
		if (string.IsNullOrWhiteSpace(eventId)) return;
		_processedEvents.TryAdd(eventId, 0);
		var doc = JsonSerializer.SerializeToDocument(new { eventId, processedAt = DateTime.UtcNow });
		db.Upsert("processed_events", eventId, doc);
	}

	public void RecordEvent(ClusterEventDto evt, DataStoreService db)
	{
		if (string.IsNullOrWhiteSpace(evt.EventId)) return;
		db.Upsert("cluster_events", evt.EventId, evt);
		MarkEventProcessed(evt.EventId, db);
	}

	public List<ClusterEventDto> GetEvents(DateTime? sinceUtc, int limit, DataStoreService db)
	{
		var allEvents = db.GetAll<ClusterEventDto>("cluster_events");
		var query = allEvents.AsEnumerable();
		if (sinceUtc.HasValue)
		{
			query = query.Where(e => e.TimestampUtc >= sinceUtc.Value);
		}

		return query
			.OrderBy(e => e.TimestampUtc)
			.Take(Math.Clamp(limit, 1, 1000))
			.ToList();
	}

	public async Task<bool> ApplyEventAsync(ClusterEventDto evt, DataStoreService db, ContentAddressableStorage cas, HashSet<string> adminPublicKeys)
	{
		if (string.IsNullOrWhiteSpace(evt.EventId) || string.IsNullOrWhiteSpace(evt.EventType))
		{
			return false;
		}

		if (HasEventBeenProcessed(evt.EventId, db))
		{
			return true;
		}

		bool success = RouteEvent(evt, db, cas, adminPublicKeys);

		if (success)
		{
			RecordEvent(evt, db);
		}

		return await Task.FromResult(success);
	}

	private bool RouteEvent(ClusterEventDto evt, DataStoreService db, ContentAddressableStorage cas, HashSet<string> adminPublicKeys)
	{
		return evt.EventType.ToLowerInvariant() switch
		{
			"map_published" => ApplyMapPublished(evt, db, cas),
			"creator_registered" => ApplyCreatorRegistered(evt, db, adminPublicKeys),
			"admin_greenlight" => ApplyAdminGreenlight(evt, db, adminPublicKeys),
			"admin_remove_manifest" => ApplyAdminRemoveManifest(evt, db, cas, adminPublicKeys),
			"map_maintainers_updated" or "map_maintainer_updated" => ApplyMapMaintainersUpdated(evt, db, adminPublicKeys),
			"map_metric_report" => ApplyMapMetricReport(evt, db),
			AccountClusterEventPayload.CreatedEvent or AccountClusterEventPayload.RenamedEvent or AccountClusterEventPayload.LinkAddedEvent => _accountEvents.Apply(evt, db),
			_ => false
		};
	}

	public void PruneOldEvents(DataStoreService db, TimeSpan retentionWindow)
	{
		DateTime cutoffUtc = DateTime.UtcNow - retentionWindow;
		var allEvents = db.GetAll<ClusterEventDto>("cluster_events").ToList();
		int pruned = 0;
		foreach (var evt in allEvents)
		{
			if (evt.TimestampUtc < cutoffUtc)
			{
				db.Delete("cluster_events", evt.EventId);
				_processedEvents.TryRemove(evt.EventId, out _);
				pruned++;
			}
		}

		if (pruned > 0)
		{
			Console.WriteLine($"[ClusterEventService] Pruned {pruned} cluster events older than {retentionWindow.TotalHours:F0} hours.");
		}
	}
}
