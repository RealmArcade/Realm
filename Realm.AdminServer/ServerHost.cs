using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using NSec.Cryptography;
using Realm.AdminServer;
using Realm.AdminServer.Accounts;
using Realm.AdminServer.Models;
using Realm.AdminServer.Services;
using Realm.Shared;
using Realm.Shared.Accounts;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using Realm.Shared.Reachability;
using Realm.AdminServer.Helpers;

namespace Realm.AdminServer;

/// <summary>
/// Builder configuration, middleware and the background loops the admin node runs
/// independently of any single route. Routes stay in Program, the composition root.
/// </summary>
public record ServerSetup(
	WebApplication App,
	ContentAddressableStorage Cas,
	HashSet<string> AdminPublicKeys,
	bool ServesClientDownloads,
	ConcurrentDictionary<string, WebSocket> HostConnections,
	string SeederId,
	int CapacityPercent,
	UdpReachabilityProbeService ReachabilityProbe);

public static class ServerHost
{
	public const string ContentNotServedError = "content_not_served_by_this_node";

	public static ServerSetup Start(WebApplicationBuilder builder)
	{
	var storageDirectory = builder.Configuration.GetValue<string>("StorageDirectory")
	                       ?? builder.Configuration.GetValue<string>("storage-path");

	if (string.IsNullOrWhiteSpace(storageDirectory))
	{
		throw new InvalidOperationException("StorageDirectory is required and must be configured in appsettings.json.");
	}

	var cas = new ContentAddressableStorage(storageDirectory);
	var dataStore = new DataStoreService(Path.Combine(storageDirectory, "data"));

	builder.Services.AddSingleton<LobbyRegistry>();
	builder.Services.AddSingleton<GeoIpService>();
	builder.Services.AddSingleton<SeederRegistry>();
	builder.Services.AddSingleton(dataStore);
	builder.Services.AddSingleton(cas);

	var capacityPercent = builder.Configuration.GetValue<int?>("CapacityPercentage") ?? 100;
	var seederId = builder.Configuration.GetValue<string>("SeederId") ?? "seed_node_server";

	// A node that serves accounts is usually a small private instance, and the bandwidth that map and asset downloads cost belongs
	// on the community nodes that volunteer for it. This flag lets the same binary decline that traffic instead of being sized for it.
	var servesClientDownloads = builder.Configuration.GetValue<bool?>("ServeClientDownloads") ?? true;

	Console.WriteLine(servesClientDownloads
		? "[Distribution] This node serves map and asset downloads to clients."
		: "[Distribution] This node refuses map and asset download requests; clients take them from the nodes that do serve them.");

	var serversConfigFile = builder.Configuration.GetValue<string>("ServersConfigFile");
	var serversConfig = ServersConfigHelper.Load(serversConfigFile);

	var adminPublicKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	foreach (var k in serversConfig.AdminPublicKeys)
	{
		if (!string.IsNullOrWhiteSpace(k)) adminPublicKeys.Add(k.Trim());
	}

	var singleAdminKey = builder.Configuration.GetValue<string>("AdminPublicKey");
	if (!string.IsNullOrWhiteSpace(singleAdminKey)) adminPublicKeys.Add(singleAdminKey.Trim());

	var adminPublicKeyArray = builder.Configuration.GetSection("AdminPublicKey").Get<List<string>>();
	if (adminPublicKeyArray != null)
	{
		foreach (var k in adminPublicKeyArray)
		{
			if (!string.IsNullOrWhiteSpace(k)) adminPublicKeys.Add(k.Trim());
		}
	}

	var adminKeysList = builder.Configuration.GetSection("AdminPublicKeys").Get<List<string>>();
	if (adminKeysList != null)
	{
		foreach (var k in adminKeysList)
		{
			if (!string.IsNullOrWhiteSpace(k)) adminPublicKeys.Add(k.Trim());
		}
	}

	Console.WriteLine($"[AdminAuth] {adminPublicKeys.Count} admin public key(s) configured; administrative routes accept only requests signed by one of them.");

	builder.Services.AddHttpClient();
	builder.Services.AddSingleton(serversConfig);

	var selfUrl = builder.Configuration.GetValue<string>("SelfUrl");
	var peersStr = builder.Configuration.GetValue<string>("Peers") ?? "";
	var peerUrls = peersStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

	foreach (var server in serversConfig.Servers)
	{
		if (!string.IsNullOrWhiteSpace(server))
		{
			if (!string.Equals(server.TrimEnd('/'), selfUrl?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
			{
				if (!peerUrls.Contains(server, StringComparer.OrdinalIgnoreCase))
				{
					peerUrls.Add(server);
				}
			}
		}
	}

	var peerRegistry = new PeerRegistry
	{
		SelfUrl = selfUrl,
		PeerUrls = peerUrls
	};
	builder.Services.AddSingleton(peerRegistry);

	var accountAuthorityOptions = AccountAuthorityOptions.FromConfiguration(builder.Configuration, selfUrl, peersStr.Length > 0);
	builder.Services.AddSingleton(accountAuthorityOptions);
	builder.Services.AddSingleton<AccountStoreService>();
	builder.Services.AddSingleton<SteamTicketService>();
	builder.Services.AddSingleton<IAccountReplicationSink, AccountClusterPublisher>();
	builder.Services.AddSingleton<ClusterEventService>();
	builder.Services.AddSingleton<ContentPruner>();


	builder.Services.AddCors(options => { options.AddPolicy("AllowAll", policy => { policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader(); }); });

	// A lobby is only registered once this node has reached its game port itself, so the socket that check is made from
	// is part of the node rather than of one route.
	var reachabilityProbePort = builder.Configuration.GetValue<int?>("ReachabilityProbePort") ?? UdpReachabilityProbeService.DefaultProbePort;
	var reachabilityProbePublicIp = builder.Configuration.GetValue<string>("ReachabilityProbePublicIp") ?? string.Empty;
	var reachabilityProbe = new UdpReachabilityProbeService(reachabilityProbePort, reachabilityProbePublicIp);
	builder.Services.AddSingleton(reachabilityProbe);

	var app = builder.Build();

	app.UseCors("AllowAll");
	app.UseWebSockets();

	AccountEndpoints.MapAccountEndpoints(app, accountAuthorityOptions, adminPublicKeys);

	if (accountAuthorityOptions.IsAuthority)
	{
		Console.WriteLine($"[AccountAuthority] This node owns account and display-name writes. Signing key configured: {(accountAuthorityOptions.SigningPrivateKeyBase64.Length > 0 ? "yes" : "no")}.");

		if (!builder.Configuration.GetValue<bool?>("AccountAuthority").HasValue && peerUrls.Count > 0)
		{
			Console.WriteLine("[AccountAuthority] This node inferred that it owns account writes because AccountAuthority is not set, while its server list names other nodes. If another node is the authority, set AccountAuthority to false here and AccountAuthorityUrl to that node.");
		}
	}
	else if (accountAuthorityOptions.AuthorityUrl.Length > 0)
	{
		Console.WriteLine($"[AccountAuthority] Account writes belong to {accountAuthorityOptions.AuthorityUrl}; this node will refuse them.");
	}
	else
	{
		Console.WriteLine("[AccountAuthority] This node is not the account authority and AccountAuthorityUrl is empty, so every account request is refused. Set AccountAuthority to true on exactly one node and AccountAuthorityUrl on the rest.");
	}


	var hostConnections = new ConcurrentDictionary<string, WebSocket>();

	app.Lifetime.ApplicationStopping.Register(reachabilityProbe.Dispose);
	Console.WriteLine($"[Reachability] Lobbies are registered only after this node reaches their game port from UDP port {reachabilityProbePort}; that port must be reachable over UDP at this node's public address, and ReachabilityProbePublicIp names that address when it differs from the HTTP host name.");


	_ = Task.Run(async () =>
	{
		while (true)
		{
			try
			{
				await Task.Delay(5000);
				var registry = app.Services.GetRequiredService<LobbyRegistry>();
				var inactiveIds = registry.PruneExpiredLobbies(TimeSpan.FromSeconds(30));
				foreach (var id in inactiveIds)
				{
					if (hostConnections.TryRemove(id, out var socket))
					{
						try
						{
							await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Lobby expired", CancellationToken.None);
						}
						catch
						{
							/* Ignore socket close errors */
						}
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error pruning lobbies: {ex.Message}");
			}
		}
	});

		return new ServerSetup(app, cas, adminPublicKeys, servesClientDownloads, hostConnections, seederId, capacityPercent, reachabilityProbe);
	}

	/// <summary>Every 63 seconds, pull cluster state from the peers and drop events older than a day.</summary>
	public static void RunQuorumSyncLoop(WebApplication app, HashSet<string> adminPublicKeys)
	{
		_ = Task.Run(async () =>
		{
			while (true)
			{
				try
				{
					await Task.Delay(3000);
					var clusterSvc = app.Services.GetRequiredService<ClusterEventService>();
					var dbSvc = app.Services.GetRequiredService<DataStoreService>();
					var casSvc = app.Services.GetRequiredService<ContentAddressableStorage>();
					var peersSvc = app.Services.GetRequiredService<PeerRegistry>();
					var httpFactory = app.Services.GetRequiredService<IHttpClientFactory>();

					await clusterSvc.RunQuorumSyncAsync(peersSvc, httpFactory, dbSvc, casSvc, adminPublicKeys);
					clusterSvc.PruneOldEvents(dbSvc, TimeSpan.FromHours(24));
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[QuorumLoop] Sync error: {ex.Message}");
				}

				await Task.Delay(TimeSpan.FromSeconds(60));
			}
		});
	}
}
