using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Realm.Shared;
using Realm.Shared.Distribution;

namespace Realm.AdminServer.Services;

/// <summary>
///     The slice of the local state a node needs in order to follow its peers: apply an event, digest the state, and absorb a snapshot.
/// </summary>
public interface IClusterStateStore
{
	Task<bool> ApplyEventAsync(ClusterEventDto evt, DataStoreService db, ContentAddressableStorage cas, HashSet<string> adminPublicKeys);

	ClusterStateDigestDto ComputeStateDigest(DataStoreService db);

	ApplySnapshotResponseDto ApplySnapshot(ClusterSnapshotDto snapshot, DataStoreService db, ContentAddressableStorage cas, HashSet<string> adminPublicKeys);
}

/// <summary>
///     Talks to the other nodes in the cluster: replicates a recorded event, catches up on events it missed, and decides by
///     majority digest whether to follow a peer's snapshot. It never writes to the store itself; it asks the state store to.
/// </summary>
public class ClusterPeerSync
{
	private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
	private readonly IClusterStateStore _state;

	public ClusterPeerSync(IClusterStateStore state)
	{
		_state = state;
	}

	public void BroadcastEvent(ClusterEventDto evt, PeerRegistry peerRegistry, IHttpClientFactory httpClientFactory)
	{
		if (peerRegistry.PeerUrls.Count == 0) return;

		evt.OriginServerUrl = peerRegistry.SelfUrl;
		string json = JsonSerializer.Serialize(evt, JsonOpts);

		_ = Task.Run(async () =>
		{
			using var client = httpClientFactory.CreateClient();
			foreach (var peerUrl in peerRegistry.PeerUrls)
			{
				if (!string.IsNullOrWhiteSpace(evt.OriginServerUrl) &&
				    string.Equals(peerUrl.TrimEnd('/'), evt.OriginServerUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				try
				{
					var content = new StringContent(json, Encoding.UTF8, "application/json");
					var response = await client.PostAsync($"{peerUrl.TrimEnd('/')}/api/cluster/event", content);
					if (response.IsSuccessStatusCode)
					{
						Console.WriteLine($"[ClusterEventService] Replicated event '{evt.EventType}' ({evt.EventId}) to peer {peerUrl}");
					}
					else
					{
						Console.WriteLine($"[ClusterEventService] Failed to replicate event '{evt.EventType}' ({evt.EventId}) to peer {peerUrl}: {response.StatusCode}");
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[ClusterEventService] Error replicating event '{evt.EventType}' ({evt.EventId}) to peer {peerUrl}: {ex.Message}");
				}
			}
		});
	}

	private async Task RunCatchUpSyncAsync(PeerRegistry peerRegistry, IHttpClientFactory httpClientFactory, DataStoreService db, ContentAddressableStorage cas, HashSet<string> adminPublicKeys)
	{
		if (peerRegistry.PeerUrls.Count == 0) return;

		var allEvents = db.GetAll<ClusterEventDto>("cluster_events").ToList();
		DateTime? latestUtc = allEvents.Count > 0 ? allEvents.Max(e => e.TimestampUtc) : null;

		using var client = httpClientFactory.CreateClient();
		foreach (var peerUrl in peerRegistry.PeerUrls)
		{
			try
			{
				var distClient = new DistributionClient(peerUrl, client);
				var events = await distClient.GetClusterEventsAsync(latestUtc, 200);
				foreach (var evt in events)
				{
					await _state.ApplyEventAsync(evt, db, cas, adminPublicKeys);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[ClusterEventService] Error catching up from peer {peerUrl}: {ex.Message}");
			}
		}
	}

	public async Task RunQuorumSyncAsync(PeerRegistry peerRegistry, IHttpClientFactory httpClientFactory, DataStoreService db, ContentAddressableStorage cas, HashSet<string> adminPublicKeys)
	{
		if (peerRegistry.PeerUrls.Count == 0) return;

		var localDigest = _state.ComputeStateDigest(db);
		using var client = httpClientFactory.CreateClient();

		var peerDigests = await PollPeerDigestsAsync(peerRegistry, client);
		if (peerDigests.Count == 0) return;

		var digestGroups = peerDigests.GroupBy(p => p.Value.StateDigest, StringComparer.OrdinalIgnoreCase)
			.OrderByDescending(g => g.Count())
			.ToList();

		if (digestGroups.Count == 0) return;

		var majorityGroup = digestGroups[0];
		string majorityDigest = majorityGroup.Key;
		int majorityCount = majorityGroup.Count();
		int totalResponding = peerDigests.Count;

		if (majorityCount <= (totalResponding / 2))
		{
			Console.WriteLine($"[QuorumSync] No clear majority state digest reached ({majorityCount}/{totalResponding} votes). Preserving current state.");
			return;
		}

		if (string.Equals(localDigest.StateDigest, majorityDigest, StringComparison.OrdinalIgnoreCase))
		{
			await RunCatchUpSyncAsync(peerRegistry, httpClientFactory, db, cas, adminPublicKeys);
			return;
		}

		Console.WriteLine($"[QuorumSync] Local state digest ({localDigest.StateDigest}) diverged from majority consensus ({majorityDigest}, {majorityCount}/{totalResponding} votes). Fetching snapshot...");
		await SelfHealFromPeerAsync(majorityGroup.First().Key, client, db, cas, adminPublicKeys);
	}

	private async Task<ConcurrentDictionary<string, ClusterStateDigestDto>> PollPeerDigestsAsync(PeerRegistry peerRegistry, HttpClient client)
	{
		var peerDigests = new ConcurrentDictionary<string, ClusterStateDigestDto>(StringComparer.OrdinalIgnoreCase);
		var pollTasks = peerRegistry.PeerUrls.Select(async peerUrl =>
		{
			try
			{
				var distClient = new DistributionClient(peerUrl, client);
				var digest = await distClient.GetStateDigestAsync();
				if (digest != null && !string.IsNullOrEmpty(digest.StateDigest))
				{
					peerDigests[peerUrl] = digest;
				}
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"[ClusterEvents] could not read the state digest from cluster peer {peerUrl}: {ex.GetType().Name}: {ex.Message}");
			}
		});
		await Task.WhenAll(pollTasks);
		return peerDigests;
	}

	private async Task SelfHealFromPeerAsync(string candidatePeer, HttpClient client, DataStoreService db, ContentAddressableStorage cas, HashSet<string> adminPublicKeys)
	{
		try
		{
			var candidateClient = new DistributionClient(candidatePeer, client);
			var snapshot = await candidateClient.GetSnapshotAsync();
			if (snapshot != null)
			{
				var result = _state.ApplySnapshot(snapshot, db, cas, adminPublicKeys);
				Console.WriteLine($"[QuorumSync] Self-healing snapshot applied from {candidatePeer}: {result.Message}");
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[QuorumSync] Error applying snapshot from {candidatePeer}: {ex.Message}");
		}
	}
}
