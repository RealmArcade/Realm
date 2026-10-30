using System;
using System.Text.Json;
using Realm.AdminServer.Services;
using Realm.Shared.Distribution;

namespace Realm.AdminServer.Accounts;

/// <summary>
///     Where an account mutation hands its replication event. The authority publishes a signed event; a node that is not the
///     authority never mutates locally in the first place, so it has nothing to publish.
/// </summary>
public interface IAccountReplicationSink
{
	void Publish(string eventType, AccountClusterEventPayload payload);
}

/// <summary>
///     Signs an account mutation with the account authority's key and publishes it to the cluster. The signature covers every
///     field a peer stores, so a peer can refuse a tampered event instead of merging it.
/// </summary>
public class AccountClusterPublisher : IAccountReplicationSink
{
	private readonly AccountAuthorityOptions _options;
	private readonly ClusterEventService _clusterEvents;
	private readonly PeerRegistry _peerRegistry;
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly DataStoreService _dataStore;
	private bool _missingSigningKeyReported;

	public AccountClusterPublisher(
		AccountAuthorityOptions options,
		ClusterEventService clusterEvents,
		PeerRegistry peerRegistry,
		IHttpClientFactory httpClientFactory,
		DataStoreService dataStore)
	{
		_options = options;
		_clusterEvents = clusterEvents;
		_peerRegistry = peerRegistry;
		_httpClientFactory = httpClientFactory;
		_dataStore = dataStore;
	}

	public void Publish(string eventType, AccountClusterEventPayload payload)
	{
		if (!_options.IsAuthority) return;

		if (string.IsNullOrEmpty(_options.SigningPrivateKeyBase64))
		{
			if (!_missingSigningKeyReported)
			{
				_missingSigningKeyReported = true;
				Console.WriteLine("[AccountClusterPublisher] AccountAuthoritySigningPrivateKey is not configured, so account mutations cannot be replicated to peers.");
			}

			return;
		}

		string signaturePayload = payload.BuildSignaturePayload(eventType);
		var clusterEvent = new ClusterEventDto
		{
			EventType = eventType,
			PayloadJson = JsonSerializer.Serialize(payload),
			Signature = AuthorSignatureHelper.SignMessage(_options.SigningPrivateKeyBase64, signaturePayload),
			PublicKey = AuthorSignatureHelper.GetPublicKey(_options.SigningPrivateKeyBase64),
			TimestampUtc = payload.MutationTimestampUtc
		};

		_clusterEvents.RecordEvent(clusterEvent, _dataStore);
		_clusterEvents.BroadcastEvent(clusterEvent, _peerRegistry, _httpClientFactory);
	}
}
