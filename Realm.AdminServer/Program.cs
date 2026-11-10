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
using Realm.AdminServer.Helpers;

var builder = WebApplication.CreateBuilder(args);

var setup = ServerHost.Start(builder);
var app = setup.App;
var cas = setup.Cas;
var adminPublicKeys = setup.AdminPublicKeys;
var servesClientDownloads = setup.ServesClientDownloads;
var hostConnections = setup.HostConnections;
var seederId = setup.SeederId;
var capacityPercent = setup.CapacityPercent;

LobbyEndpoints.MapLobbyEndpoints(app, hostConnections);

SeederEndpoints.MapSeederEndpoints(app, setup);

AssetEndpoints.MapAssetEndpoints(app, setup);

SeederEndpoints.MapSeederDownloadEndpoints(app, setup);

PublishMapEndpoints.MapPublishMapEndpoints(app, setup);

ClusterEndpoints.MapClusterEndpoints(app, setup);

AdminEndpoints.MapAdminRemovalEndpoints(app, setup);

ClusterEndpoints.MapClusterSyncEndpoints(app, setup);

PublishMapEndpoints.MapPublishMapAssetEndpoints(app, setup);

CreatorEndpoints.MapCreatorEndpoints(app, setup);

DiscoveryEndpoints.MapDiscoveryEndpoints(app, setup);

AdminEndpoints.MapAdminInfoEndpoints(app, setup);

MapRegistryEndpoints.MapMapRegistryEndpoints(app, setup);

DataEndpoints.MapDataEndpoints(app, setup);

AdminEndpoints.MapBanEndpoints(app, setup);

SeederEndpoints.MapSeederWebSocketEndpoints(app, setup);

MapRegistryEndpoints.MapMapMetricsEndpoints(app, setup);

ServerHost.RunQuorumSyncLoop(app, adminPublicKeys);

app.Run();
