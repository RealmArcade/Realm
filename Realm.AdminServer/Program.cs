using Realm.AdminServer.Models;
using Realm.AdminServer.Services;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddSingleton<ClusterEventService>();


builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseCors("AllowAll");
app.UseWebSockets();


var hostConnections = new ConcurrentDictionary<string, WebSocket>();


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
                    catch { /* Ignore socket close errors */ }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error pruning lobbies: {ex.Message}");
        }
    }
});




app.MapGet("/lobbies", (LobbyRegistry registry, GeoIpService geoIp, HttpContext context) =>
{
    var clientIpStr = context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "127.0.0.1";
    var clientCoords = geoIp.GetCoordinates(clientIpStr);
    
    var list = registry.GetAllLobbies().Select(lobby =>
    {
        var distance = GeoIpService.CalculateDistance(
            clientCoords.lat, clientCoords.lon,
            lobby.Latitude, lobby.Longitude
        );
        

        int estimatedPing = 15 + (int)(distance / 100);
        if (clientIpStr == lobby.HostIP || clientIpStr == "127.0.0.1" || lobby.HostIP == "127.0.0.1")
        {
            estimatedPing = 5; // Local connection ping
        }

        return new LobbyResponseDto(
            lobby.LobbyId,
            lobby.Map,
            lobby.HostIP,
            lobby.HostPort,
            lobby.NatType,
            lobby.SlotsUsed,
            lobby.MaxPlayers,
            lobby.Latitude,
            lobby.Longitude,
            distance,
            estimatedPing,
            lobby.OriginServerUri,
            lobby.HostPingBaseline,
            lobby.GameVersion,
            lobby.LocalIP,
            lobby.MapVersion,
            lobby.MapSizeBytes
        );
    });

    return Results.Ok(list);
});


app.MapPost("/lobbies/register", async (RegisterRequest req, LobbyRegistry registry, PeerRegistry registeredPeers, GeoIpService geoIp, IHttpClientFactory httpClientFactory, HttpContext context) =>
{

    if (!string.IsNullOrEmpty(req.NatType) && req.NatType.Equals("Symmetric", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { Message = "Lobby creation rejected: Symmetric NAT is not supported." });
    }

    var hostIp = context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "127.0.0.1";
    

    if (!string.IsNullOrEmpty(req.ReportedHostIP) && req.ReportedHostIP != "0.0.0.0" && req.ReportedHostIP != "127.0.0.1")
    {
        hostIp = req.ReportedHostIP;
    }

    var hostCoords = geoIp.GetCoordinates(hostIp);
    var lobbyId = Guid.NewGuid().ToString();
    var hostToken = Guid.NewGuid().ToString();

    var db = context.RequestServices.GetRequiredService<DataStoreService>();

    bool isCustom = !string.IsNullOrEmpty(req.Signature) || !string.IsNullOrEmpty(req.PublicKey);

    if (isCustom)
    {
        if (string.IsNullOrEmpty(req.Signature) || string.IsNullOrEmpty(req.PublicKey) || string.IsNullOrEmpty(req.MapHash))
        {
            return Results.BadRequest(new { Message = "Map signature, public key, and hash are required for custom maps." });
        }
        
        try
        {
            byte[] pubKeyBytes = Convert.FromBase64String(req.PublicKey);
            byte[] sigBytes = Convert.FromBase64String(req.Signature);
            var pubKey = NSec.Cryptography.PublicKey.Import(NSec.Cryptography.SignatureAlgorithm.Ed25519, pubKeyBytes, NSec.Cryptography.KeyBlobFormat.RawPublicKey);
            byte[] hashBytes = System.Text.Encoding.UTF8.GetBytes(req.MapHash);
            if (!NSec.Cryptography.SignatureAlgorithm.Ed25519.Verify(pubKey, hashBytes, sigBytes)) {
                return Results.BadRequest(new { Message = "Invalid map signature." });
            }
        }
        catch
        {
            return Results.BadRequest(new { Message = "Invalid signature or public key format." });
        }
        
        string slug = req.Map.ToLowerInvariant().Replace(" ", "-");
        string? officialOwner = db.Get<string>("map_ownership", req.Map) ?? db.Get<string>("map_ownership", slug);
        if (officialOwner == null)
        {
            var publishedMap = db.Get<JsonDocument>("published_maps", req.Map) ?? db.Get<JsonDocument>("published_maps", slug);
            if (publishedMap != null)
            {
                if (publishedMap.RootElement.TryGetProperty("owner_public_key", out var opk))
                {
                    officialOwner = opk.GetString();
                }
                else if (publishedMap.RootElement.TryGetProperty("OwnerPublicKey", out var opk2))
                {
                    officialOwner = opk2.GetString();
                }
            }
        }

        if (!string.IsNullOrEmpty(officialOwner))
        {
            if (!string.Equals(officialOwner, req.PublicKey, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { Message = $"The map name '{req.Map}' conflicts with an officially published map. Please rename your map to host a lobby." });
            }
        }
    }

    string finalMapName = req.Map;
    string mapVersion = req.MapVersion ?? "1.0";
    string compositeKey = $"{req.Map}_{mapVersion}";
    var stats = MapStatsHelper.GetStats(db, req.Map, mapVersion, req.PublicKey);
    bool isGreenlit = !isCustom || (stats != null && stats.IsGreenlit);

    if (!isGreenlit) {
        string author = "Unknown";
        if (!string.IsNullOrEmpty(req.PublicKey)) {
            var creator = db.Get<JsonDocument>("creators", req.PublicKey);
            if (creator != null && creator.RootElement.TryGetProperty("username", out var uProp)) {
                author = uProp.GetString() ?? "Unknown";
            }
        }
        if (author == "Unknown") {
            var existingMap = db.Get<JsonDocument>("published_maps", compositeKey);
            if (existingMap != null) {
                var root = existingMap.RootElement;
                if (root.TryGetProperty("Contributors", out var conts) && conts.GetArrayLength() > 0) {
                    author = conts[0].GetString() ?? "Unknown";
                }
            }
        }
        if (!finalMapName.Contains("[Beta-Testing]")) {
            finalMapName = $"[Beta-Testing] {finalMapName} - {author}";
        }
    }

    var info = new LobbyInfo
    {
        LobbyId = lobbyId,
        Map = finalMapName,
        MapVersion = mapVersion,
        HostIP = hostIp,
        HostPort = req.HostPort,
        NatType = req.NatType,
        PasswordHash = req.PasswordHash ?? "",
        MaxPlayers = req.MaxPlayers,
        SlotsUsed = req.SlotsUsed,
        GameVersion = req.GameVersion,
        Latitude = hostCoords.lat,
        Longitude = hostCoords.lon,
        LastHeartbeat = DateTime.UtcNow,
        OriginServerUri = registeredPeers.SelfUrl,
        HostToken = hostToken,
        HostPingBaseline = req.HostPingBaseline,
        LocalIP = req.LocalIP,
        MapSizeBytes = req.MapSizeBytes ?? 0
    };

    registry.AddOrUpdate(info);

    _ = Task.Run(async () =>
    {
        using var httpClient = httpClientFactory.CreateClient();
        foreach (var peerUrl in registeredPeers.PeerUrls)
        {
            try
            {
                var content = new StringContent(JsonSerializer.Serialize(info), Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync($"{peerUrl.TrimEnd('/')}/lobbies/propagate", content);
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Registry] Propagated lobby {lobbyId} to peer {peerUrl}");
                }
                else
                {
                    Console.WriteLine($"[Registry] Failed to propagate lobby {lobbyId} to peer {peerUrl}: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Registry] Error propagating lobby {lobbyId} to peer {peerUrl}: {ex.Message}");
            }
        }
    });

    return Results.Ok(new { LobbyId = lobbyId, Latitude = hostCoords.lat, Longitude = hostCoords.lon, HostToken = hostToken });
});

app.MapPost("/lobbies/propagate", (LobbyInfo propagatedLobby, LobbyRegistry registry) =>
{

    if (!string.IsNullOrEmpty(propagatedLobby.NatType) && propagatedLobby.NatType.Equals("Symmetric", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { Message = "Symmetric NAT lobbies are not supported." });
    }

    registry.AddOrUpdate(propagatedLobby);
    Console.WriteLine($"[Registry] Received propagated lobby {propagatedLobby.LobbyId} from {propagatedLobby.OriginServerUri}");
    return Results.Ok(new { Status = "Propagated" });
});


app.MapPost("/lobbies/heartbeat", async (HeartbeatRequest req, LobbyRegistry registry, PeerRegistry registeredPeers, IHttpClientFactory httpClientFactory) =>
{
    if (registry.TryGet(req.LobbyId, out var lobby) && lobby != null)
    {
        lobby.LastHeartbeat = DateTime.UtcNow;
        lobby.SlotsUsed = req.SlotsUsed;


        _ = Task.Run(async () =>
        {
            using var httpClient = httpClientFactory.CreateClient();
            foreach (var peerUrl in registeredPeers.PeerUrls)
            {
                try
                {
                    var content = new StringContent(JsonSerializer.Serialize(lobby), Encoding.UTF8, "application/json");
                    var response = await httpClient.PostAsync($"{peerUrl.TrimEnd('/')}/lobbies/propagate", content);
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[Registry] Failed to propagate heartbeat for {lobby.LobbyId} to peer {peerUrl}: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Registry] Error propagating heartbeat for {lobby.LobbyId} to peer {peerUrl}: {ex.Message}");
                }
            }
        });

        return Results.Ok(new { Status = "Ok" });
    }
    return Results.NotFound(new { Message = "Lobby not found" });
});

app.MapPost("/lobbies/close", async (CloseLobbyRequest req, LobbyRegistry registry, PeerRegistry registeredPeers, IHttpClientFactory httpClientFactory) =>
{
    if (registry.TryGet(req.LobbyId, out var lobby) && lobby != null)
    {
        if (lobby.HostToken == req.HostToken)
        {
            registry.TryRemove(req.LobbyId, out _);

            if (hostConnections.TryRemove(req.LobbyId, out var socket))
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Lobby closed by host", CancellationToken.None);
                }
                catch { /* Ignore socket close errors */ }
            }

            _ = Task.Run(async () =>
            {
                using var httpClient = httpClientFactory.CreateClient();
                foreach (var peerUrl in registeredPeers.PeerUrls)
                {
                    try
                    {
                        var content = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");
                        await httpClient.PostAsync($"{peerUrl.TrimEnd('/')}/lobbies/propagate-close", content);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error propagating close: {ex.Message}");
                    }
                }
            });

            return Results.Ok(new { Status = "Closed" });
        }
        return Results.Json(new { Message = "Unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
    }
    return Results.NotFound(new { Message = "Lobby not found" });
});

app.MapPost("/lobbies/propagate-close", async (CloseLobbyRequest req, LobbyRegistry registry) =>
{
    if (registry.TryGet(req.LobbyId, out var lobby) && lobby != null)
    {
        if (lobby.HostToken == req.HostToken)
        {
            registry.TryRemove(req.LobbyId, out _);

            if (hostConnections.TryRemove(req.LobbyId, out var socket))
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Lobby closed by host", CancellationToken.None);
                }
                catch { /* Ignore socket close errors */ }
            }

            return Results.Ok(new { Status = "Closed" });
        }
        return Results.Json(new { Message = "Unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
    }
    return Results.NotFound(new { Message = "Lobby not found" });
});


app.MapPost("/lobbies/join", async (JoinRequest req, LobbyRegistry registry, IHttpClientFactory httpClientFactory) =>
{
    if (!registry.TryGet(req.LobbyId, out var lobby) || lobby == null)
    {
        return Results.NotFound(new { Message = "Lobby not found" });
    }


    if (hostConnections.TryGetValue(req.LobbyId, out var hostSocket) && hostSocket.State == WebSocketState.Open)
    {
        var msg = JsonSerializer.Serialize(new
        {
            Action = "Punch",
            ClientIP = req.ClientPublicIP,
            ClientPort = req.ClientPublicPort
        });

        var bytes = Encoding.UTF8.GetBytes(msg);
        await hostSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        
        Console.WriteLine($"[Registry] Relayed punch request to host {lobby.LobbyId} for client {req.ClientPublicIP}:{req.ClientPublicPort}");
        
        return Results.Ok(new JoinResponseDto(lobby.HostIP, lobby.HostPort, lobby.LocalIP));
    }
    else
    {

        if (!string.IsNullOrEmpty(lobby.OriginServerUri))
        {
            Console.WriteLine($"[Registry] Relaying join request for lobby {lobby.LobbyId} to origin {lobby.OriginServerUri}");
            try
            {
                using var httpClient = httpClientFactory.CreateClient();
                var content = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync($"{lobby.OriginServerUri.TrimEnd('/')}/lobbies/join", content);
                if (response.IsSuccessStatusCode)
                {
                    var respText = await response.Content.ReadAsStringAsync();
                    var joinResp = JsonSerializer.Deserialize<JoinResponseDto>(respText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (joinResp != null)
                    {
                        return Results.Ok(joinResp);
                    }
                }
                else
                {
                    var errText = await response.Content.ReadAsStringAsync();
                    return Results.BadRequest(new { Message = $"Relayed join failed: {errText}" });
                }
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Message = $"Failed to relay join to origin: {ex.Message}" });
            }
        }
    }

    return Results.BadRequest(new { Message = "Host signaling channel is disconnected" });
});


app.Map("/lobbies/ws", async (HttpContext context, LobbyRegistry registry) =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var lobbyId = context.Request.Query["lobbyId"].ToString();
        if (string.IsNullOrEmpty(lobbyId) || !registry.TryGet(lobbyId, out _))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        hostConnections[lobbyId] = webSocket;
        Console.WriteLine($"[Registry] Host connected WebSocket for Lobby {lobbyId}");

        var buffer = new byte[1024 * 4];
        while (webSocket.State == WebSocketState.Open)
        {
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }

        }

        hostConnections.TryRemove(lobbyId, out _);
        Console.WriteLine($"[Registry] Host disconnected WebSocket for Lobby {lobbyId}");
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
    }
});

app.MapGet("/auth/login", (string provider, int port) =>
{
    long part1 = Random.Shared.Next(100000000, 999999999);
    long part2 = Random.Shared.Next(100000000, 999999999);
    string randomSnowflake = $"{part1}{part2}";

    var html = $$"""
    <!DOCTYPE html>
    <html>
    <head>
        <title>Authorize Realm</title>
        <style>
            body {
                background: #0f111a;
                color: #e2e8f0;
                font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
                display: flex;
                align-items: center;
                justify-content: center;
                height: 100vh;
                margin: 0;
            }
            .card {
                background: #1a1d2e;
                border: 2px solid #3b3f5c;
                border-radius: 12px;
                padding: 40px;
                width: 400px;
                text-align: center;
                box-shadow: 0 8px 32px rgba(0,0,0,0.5);
            }
            h2 {
                color: #ffd700;
                margin-bottom: 10px;
            }
            .provider-title {
                text-transform: capitalize;
                font-weight: bold;
                color: #5cd6ff;
            }
            .btn {
                background: #5865F2;
                color: white;
                border: none;
                padding: 12px 24px;
                border-radius: 6px;
                font-size: 16px;
                font-weight: bold;
                cursor: pointer;
                transition: background 0.2s;
                width: 100%;
                margin-top: 20px;
            }
            .btn-steam {
                background: #171a21;
                border: 1px solid #66c0f4;
            }
            .btn:hover {
                filter: brightness(1.1);
            }
            input {
                width: 90%;
                padding: 10px;
                margin-top: 15px;
                border-radius: 4px;
                border: 1px solid #3b3f5c;
                background: #0f111a;
                color: #e2e8f0;
                text-align: center;
                font-size: 16px;
            }
        </style>
    </head>
    <body>
        <div class="card">
            <h2>Authorize Realm</h2>
            <p>Connect your <span class="provider-title">{{provider}}</span> account to play.</p>
            <form action="/auth/authorize" method="GET">
                <input type="hidden" name="provider" value="{{provider}}" />
                <input type="hidden" name="port" value="{{port}}" />
                <input type="text" name="username" placeholder="Enter Username" required value="Gamer_{{Random.Shared.Next(1000, 9999)}}" />
                {{(provider == "discord" ? $"<input type=\"text\" name=\"discord_id\" placeholder=\"Discord Snowflake ID\" required value=\"{randomSnowflake}\" />" : "")}}
                <button type="submit" class="btn {{ (provider == "steam" ? "btn-steam" : "") }}">
                    Login with {{provider}}
                </button>
            </form>
        </div>
    </body>
    </html>
    """;
    return Results.Content(html, "text/html");
});

app.MapGet("/auth/authorize", (string provider, int port, string username, string? discord_id, DataStoreService db) =>
{
    string finalUsername = username;
    string playerId = "";
    if (provider == "discord")
    {
        string snowflake = discord_id ?? "";
        if (string.IsNullOrEmpty(snowflake))
        {
            long part1 = Random.Shared.Next(100000000, 999999999);
            long part2 = Random.Shared.Next(100000000, 999999999);
            snowflake = $"{part1}{part2}";
        }
        playerId = $"discord_{snowflake}";

        var existingPlayer = db.Get<JsonDocument>("players", snowflake);
        if (existingPlayer != null)
        {
            var root = existingPlayer.RootElement;
            if (root.TryGetProperty("username", out var uProp))
            {
                finalUsername = uProp.GetString() ?? username;
            }
        }
        else
        {
            var playerDoc = JsonSerializer.SerializeToDocument(new
            {
                id = snowflake,
                username = username,
                provider = "discord",
                registration_date = DateTime.UtcNow
            });
            db.Upsert("players", snowflake, playerDoc);
        }
    }
    else
    {
        playerId = $"{provider}_{Guid.NewGuid().ToString("N")[..8]}";
        var playerDoc = JsonSerializer.SerializeToDocument(new
        {
            id = playerId,
            username = username,
            provider = provider,
            registration_date = DateTime.UtcNow
        });
        db.Upsert("players", playerId, playerDoc);
    }

    var token = Guid.NewGuid().ToString("N");
    var sessionDoc = JsonSerializer.SerializeToDocument(new
    {
        token = token,
        username = finalUsername,
        provider = provider,
        player_id = playerId,
        created_at = DateTime.UtcNow
    });
    db.Upsert("auth_sessions", token, sessionDoc);

    var callbackUrl = $"http://localhost:{port}/auth/callback/?username={Uri.EscapeDataString(finalUsername)}&token={token}&provider={provider}";
    return Results.Redirect(callbackUrl);
});

app.MapPost("/seeders/register", (SeederRegisterRequest req, SeederRegistry registry, HttpContext context) =>
{
    var ip = context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "127.0.0.1";
    if (!string.IsNullOrEmpty(req.ReportedIP) && req.ReportedIP != "0.0.0.0" && req.ReportedIP != "127.0.0.1")
    {
        ip = req.ReportedIP;
    }

    var info = new SeederInfo
    {
        SeederId = req.SeederId,
        IP = ip,
        Port = req.Port,
        MapIds = req.MapIds,
        CapacityPercentage = req.CapacityPercentage,
        AcceptingUploads = req.AcceptingUploads
    };
    registry.Register(info);
    return Results.Ok(new { Status = "Registered" });
});

app.MapPost("/seeders/unregister", (SeederUnregisterRequest req, SeederRegistry registry) =>
{
    registry.Unregister(req.SeederId);
    return Results.Ok(new { Status = "Unregistered" });
});

app.MapGet("/seeders", (SeederRegistry registry) =>
{
    var list = registry.GetAll().Select(s => new
    {
        ip = s.IP,
        port = s.Port,
        mapIds = s.MapIds,
        capacityPercentage = s.CapacityPercentage,
        acceptingUploads = s.AcceptingUploads
    });
    return Results.Ok(list);
});

app.MapGet("/api/seeders", (SeederRegistry registry) =>
{
    var list = registry.GetAll().Select(s => new SeederNodeDto
    {
        SeederId = s.SeederId,
        IP = s.IP,
        Port = s.Port,
        CapacityPercentage = s.CapacityPercentage,
        AcceptingUploads = s.AcceptingUploads,
        MapIds = s.MapIds
    });
    return Results.Ok(list);
});

app.MapGet("/api/seeders/catalog", (ContentAddressableStorage cas) =>
{
    return Results.Ok(new SeederCatalogResponseDto
    {
        SeederId = seederId,
        CapacityPercentage = capacityPercent,
        AssetHashes = cas.GetAllStoredHashes().ToList()
    });
});

app.MapMethods("/api/assets/{hash}", new[] { "HEAD" }, (string hash, ContentAddressableStorage cas) =>
{
    string normalized = ContentAddressableStorage.NormalizeBlake3Hash(hash);
    return cas.HasAsset(normalized) ? Results.Ok() : Results.NotFound();
});

app.MapPost("/api/assets/bundle", async (HttpContext context, ContentAddressableStorage cas) =>
{
    var syncIoFeature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature>();
    if (syncIoFeature != null)
    {
        syncIoFeature.AllowSynchronousIO = true;
    }

    using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
    string json = await reader.ReadToEndAsync(context.RequestAborted);
    AssetBundleRequestDto? req = null;
    try
    {
        req = JsonSerializer.Deserialize<AssetBundleRequestDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch { }

    if (req == null || req.Hashes == null || req.Hashes.Count == 0)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { Message = "Hashes array is required." }, context.RequestAborted);
        return;
    }

    var assetInfos = new List<(string AssetKey, string FilePath, string? Metadata)>();
    foreach (var hash in req.Hashes)
    {
        if (string.IsNullOrWhiteSpace(hash)) continue;
        string normalized = ContentAddressableStorage.NormalizeBlake3Hash(hash);
        string? filePath = cas.FindAssetFilePath(normalized);
        if (filePath != null && File.Exists(filePath))
        {
            string? metadata = cas.GetAssetMetadata(normalized);
            assetInfos.Add((normalized, filePath, metadata));
        }
    }

    try
    {
        using var memoryStream = new MemoryStream();
        await ZstdAssetBundleHelper.StreamAssetsToBundleAsync(
            memoryStream,
            assetInfos,
            compressionLevel: 1,
            cancellationToken: context.RequestAborted);

        memoryStream.Position = 0;
        context.Response.ContentType = "application/octet-stream";
        context.Response.ContentLength = memoryStream.Length;
        context.Response.StatusCode = StatusCodes.Status200OK;

        await memoryStream.CopyToAsync(context.Response.Body, context.RequestAborted);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[AdminServer] Error streaming asset bundle: {ex.Message}");
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { Error = ex.Message }, context.RequestAborted);
        }
    }
});

app.MapGet("/api/assets/{hash}", (string hash, ContentAddressableStorage cas, HttpContext context) =>
{
    string normalized = ContentAddressableStorage.NormalizeBlake3Hash(hash);
    var bytes = cas.GetAssetBytes(normalized);
    if (bytes == null) return Results.NotFound();

    string? meta = cas.GetAssetMetadata(normalized);
    if (!string.IsNullOrWhiteSpace(meta))
    {
        context.Response.Headers["X-Asset-Metadata"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(meta));
    }

    string? filePath = cas.FindAssetFilePath(normalized);
    string fileName = filePath != null ? Path.GetFileName(filePath) : $"{normalized}.bin";
    return Results.File(bytes, "application/octet-stream", fileDownloadName: fileName, enableRangeProcessing: true);
});

app.MapPost("/api/assets/{hash}", async (string hash, HttpRequest request, ContentAddressableStorage cas) =>
{
    string normalized = ContentAddressableStorage.NormalizeBlake3Hash(hash);
    if (!DistributionSharding.SeederAcceptsHash(seederId, capacityPercent, normalized))
    {
        return Results.StatusCode(403);
    }

    if (!cas.CheckFreeDiskSpaceAcceptingUploads())
    {
        return Results.StatusCode(507);
    }

    byte[] fileBytes;
    string? metadata = request.Headers["X-Asset-Metadata"];
    string? authorPub = request.Headers["X-Author-Public-Key"];
    string? authorSig = request.Headers["X-Author-Signature"];
    string ext = request.Headers["X-File-Extension"].ToString();
    if (string.IsNullOrEmpty(ext)) ext = ".bin";

    if (request.HasFormContentType)
    {
        var form = await request.ReadFormAsync();
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file != null)
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            fileBytes = ms.ToArray();
            ext = Path.GetExtension(file.FileName);
        }
        else
        {
            using var ms = new MemoryStream();
            await request.Body.CopyToAsync(ms);
            fileBytes = ms.ToArray();
        }

        if (form.TryGetValue("metadata", out var formMeta)) metadata = formMeta;
        if (form.TryGetValue("authorPublicKey", out var formPub)) authorPub = formPub;
        if (form.TryGetValue("authorSignature", out var formSig)) authorSig = formSig;
    }
    else
    {
        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms);
        fileBytes = ms.ToArray();
    }

    var storeResult = cas.StoreAsset(fileBytes, ext, metadata, authorPub, authorSig);
    return Results.Ok(new AssetUploadResponseDto
    {
        Success = storeResult.Success,
        Message = storeResult.Message,
        Deduplicated = storeResult.Deduplicated,
        Merged = storeResult.Merged,
        Blake3Hash = storeResult.Blake3Hash
    });
});

app.MapGet("/api/manifests/{mapId}", (string mapId, ContentAddressableStorage cas) =>
{
    string manifestDir = Path.Combine(cas.RootDirectory, "manifests");
    if (!Directory.Exists(manifestDir)) return Results.NotFound();

    var candidates = new List<string>
    {
        Path.Combine(manifestDir, $"{mapId}_manifest.json"),
        Path.Combine(manifestDir, $"{mapId}.json"),
        Path.Combine(manifestDir, $"{mapId.Replace(' ', '_')}_manifest.json"),
        Path.Combine(manifestDir, $"{mapId.Replace(' ', '_')}.json"),
        Path.Combine(manifestDir, $"{mapId.Replace('_', ' ')}_manifest.json"),
        Path.Combine(manifestDir, $"{mapId.Replace('_', ' ')}.json")
    };

    foreach (var path in candidates)
    {
        if (File.Exists(path))
        {
            return Results.File(File.ReadAllBytes(path), "application/json");
        }
    }

    foreach (var file in Directory.EnumerateFiles(manifestDir, "*.json"))
    {
        try
        {
            var manifest = MapManifest.LoadFromFile(file);
            if (manifest != null)
            {
                if (string.Equals(manifest.MapName, mapId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(manifest.MapName?.Replace(' ', '_'), mapId.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase))
                {
                    return Results.File(File.ReadAllBytes(file), "application/json");
                }
            }
        }
        catch { }
    }

    return Results.NotFound();
});

app.MapPost("/api/manifests", async (HttpRequest request, ContentAddressableStorage cas, DataStoreService db) =>
{
    using var reader = new StreamReader(request.Body, Encoding.UTF8);
    string manifestJson = await reader.ReadToEndAsync();
    var manifest = MapManifest.LoadFromJson(manifestJson);
    if (manifest == null || string.IsNullOrWhiteSpace(manifest.MapName))
    {
        return Results.BadRequest(new { Message = "Invalid manifest" });
    }

    string compositeKey = $"{manifest.MapName}_{manifest.Version}";
    var stats = MapStatsHelper.GetStats(db, manifest.MapName, manifest.Version, null);
    bool isGreenlit = stats != null && stats.IsGreenlit;
    string? bypassToken = request.Headers["X-Admin-Bypass"];

    if (!isGreenlit)
    {
        bool bypassValid = adminPublicKeys.Count > 0 &&
                           AdminBypassAuth.VerifyBypassToken(adminPublicKeys, manifest.MapName, manifest.Version, bypassToken);
        if (!bypassValid)
        {
            return Results.StatusCode(403);
        }
    }

    string manifestDir = Path.Combine(cas.RootDirectory, "manifests");
    Directory.CreateDirectory(manifestDir);
    string manifestPath = Path.Combine(manifestDir, $"{manifest.MapName}_manifest.json");
    await File.WriteAllTextAsync(manifestPath, manifestJson);

    var missing = new List<string>();
    foreach (var pair in manifest.Files)
    {
        string h = ContentAddressableStorage.NormalizeBlake3Hash(pair.Value);
        if (!cas.HasAsset(h))
        {
            missing.Add(h);
        }
    }

    return Results.Ok(new MapPublishResponseDto
    {
        Success = true,
        Status = "Published",
        MapId = $"{manifest.MapName}_{manifest.Version}",
        MissingAssetHashes = missing
    });
});

app.MapPost("/seeders/download", async (SeederDownloadRequest req, SeederRegistry registry) =>
{
    var seeders = registry.GetSeedersForMap(req.MapId);
    if (seeders.Count == 0)
    {
        return Results.NotFound(new { Message = "No seeders found for this map", seeders = Array.Empty<object>() });
    }

    if (!string.IsNullOrEmpty(req.ClientPublicIP) && req.ClientPublicPort > 0)
    {
        foreach (var seeder in seeders)
        {
            var ws = registry.GetConnection(seeder.SeederId);
            if (ws != null && ws.State == WebSocketState.Open)
            {
                try
                {
                    var msg = JsonSerializer.Serialize(new
                    {
                        Action = "Punch",
                        ClientIP = req.ClientPublicIP,
                        ClientPort = req.ClientPublicPort
                    });
                    var bytes = Encoding.UTF8.GetBytes(msg);
                    await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch
                {
                    registry.Unregister(seeder.SeederId);
                }
            }
        }
    }

    var resultList = seeders.Select(s => new { ip = s.IP, port = s.Port, seederId = s.SeederId }).ToList();
    return Results.Ok(new { seeders = resultList });
});

app.MapGet("/seeders/by_manifest/{manifestHash}", (string manifestHash, SeederRegistry registry) =>
{
    var seeders = registry.GetSeedersForMap(manifestHash);
    var resultList = seeders.Select(s => new { ip = s.IP, port = s.Port, seederId = s.SeederId }).ToList();
    return Results.Ok(new { seeders = resultList });
});


app.MapPost("/api/publish_map/initiate", (PublishMapInitiateRequest req, DataStoreService db, ContentAddressableStorage cas) =>
{
    try
    {
        string jsonToProcess = req.ManifestJson;
        if (string.IsNullOrWhiteSpace(jsonToProcess))
        {
            return Results.BadRequest(new PublishMapInitiateResponse
            {
                Success = false,
                Message = "ManifestJson is required."
            });
        }

        var mapDoc = JsonDocument.Parse(jsonToProcess);
        var root = mapDoc.RootElement;

        string mapTitle = req.MapTitle;
        string mapVersion = string.IsNullOrWhiteSpace(req.MapVersion) ? "1.0" : req.MapVersion;

        if (string.IsNullOrEmpty(mapTitle) && root.TryGetProperty("MapName", out var mapNameProp))
        {
            mapTitle = mapNameProp.GetString() ?? "";
        }
        if (root.TryGetProperty("Version", out var versionProp))
        {
            mapVersion = versionProp.GetString() ?? mapVersion;
        }

        if (string.IsNullOrEmpty(mapTitle) && root.TryGetProperty("MapProperties", out var mapProps))
        {
            if (mapProps.TryGetProperty("MapName", out var nameProp)) mapTitle = nameProp.GetString() ?? "";
            if (mapProps.TryGetProperty("MapTitle", out var titleProp)) mapTitle = titleProp.GetString() ?? mapTitle;
            if (mapProps.TryGetProperty("MapVersion", out var vProp)) mapVersion = vProp.GetString() ?? mapVersion;
        }

        if (string.IsNullOrEmpty(mapTitle))
        {
            return Results.BadRequest(new PublishMapInitiateResponse
            {
                Success = false,
                Message = "MapTitle is required in manifest."
            });
        }

        if (string.IsNullOrEmpty(req.Signature) || string.IsNullOrEmpty(req.PublicKey))
        {
            return Results.BadRequest(new PublishMapInitiateResponse
            {
                Success = false,
                Message = "Signature and PublicKey are required."
            });
        }

        byte[] pubKeyBytes = Convert.FromBase64String(req.PublicKey);
        byte[] sigBytes = Convert.FromBase64String(req.Signature);
        var pubKey = NSec.Cryptography.PublicKey.Import(NSec.Cryptography.SignatureAlgorithm.Ed25519, pubKeyBytes, NSec.Cryptography.KeyBlobFormat.RawPublicKey);

        byte[] mapBytes = Encoding.UTF8.GetBytes(jsonToProcess);
        string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(mapBytes, ".json");
        string mapHashStr = $"{canonicalBlake3}.json";
        byte[] mapHashBytes = Encoding.UTF8.GetBytes(mapHashStr);

        bool isSigValid = NSec.Cryptography.SignatureAlgorithm.Ed25519.Verify(pubKey, mapHashBytes, sigBytes)
                       || NSec.Cryptography.SignatureAlgorithm.Ed25519.Verify(pubKey, Encoding.UTF8.GetBytes(canonicalBlake3), sigBytes)
                       || NSec.Cryptography.SignatureAlgorithm.Ed25519.Verify(pubKey, mapBytes, sigBytes);

        if (!isSigValid)
        {
            return Results.BadRequest(new PublishMapInitiateResponse
            {
                Success = false,
                Message = "Invalid map manifest signature."
            });
        }

        string compositeKey = $"{mapTitle}_{mapVersion}";
        var stats = MapStatsHelper.GetStats(db, mapTitle, mapVersion, req.PublicKey);
        bool isGreenlit = stats != null && stats.IsGreenlit;
        if (!isGreenlit)
        {
            return Results.Json(new PublishMapInitiateResponse
            {
                Success = false,
                Status = "NotGreenlit",
                IsGreenlit = false,
                Message = $"Map '{mapTitle}' is not greenlit for publication to the public registry. Accumulate more community playtime and ratings or request an admin override."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var existingOwner = db.Get<string>("map_ownership", mapTitle);
        if (existingOwner != null && !string.Equals(existingOwner, req.PublicKey, StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new PublishMapInitiateResponse
            {
                Success = false,
                Message = "A map with this title already exists and is owned by a different key."
            });
        }

        var oversizedAssets = new List<string>();
        if (root.TryGetProperty("FileSizes", out var fileSizesProp) && fileSizesProp.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in fileSizesProp.EnumerateObject())
            {
                if (prop.Value.TryGetInt64(out long size) && size > ContentAddressableStorage.MaximumAssetSizeBytes)
                {
                    double sizeMb = (double)size / (1024 * 1024);
                    double maxMb = (double)ContentAddressableStorage.MaximumAssetSizeBytes / (1024 * 1024);
                    oversizedAssets.Add($"'{prop.Name}' ({sizeMb:F2} MB > {maxMb:F0} MB limit)");
                }
            }
        }
        if (oversizedAssets.Count > 0)
        {
            return Results.BadRequest(new PublishMapInitiateResponse
            {
                Success = false,
                Status = "OversizedAssets",
                Message = $"Publish rejected: {oversizedAssets.Count} asset(s) exceed the maximum per-asset file size limit of {ContentAddressableStorage.MaximumAssetSizeBytes / (1024 * 1024)} MB:\n{string.Join("\n", oversizedAssets)}"
            });
        }

        var referencedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (req.ReferencedHashes != null)
        {
            foreach (var h in req.ReferencedHashes)
            {
                if (!string.IsNullOrWhiteSpace(h)) referencedHashes.Add(ContentAddressableStorage.NormalizeBlake3Hash(h));
            }
        }
        if (root.TryGetProperty("Files", out var filesProp) && filesProp.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in filesProp.EnumerateObject())
            {
                string h = ContentAddressableStorage.NormalizeBlake3Hash(prop.Value.GetString() ?? "");
                if (!string.IsNullOrWhiteSpace(h)) referencedHashes.Add(h);
            }
        }

        var missingHashes = new List<string>();
        foreach (var hash in referencedHashes)
        {
            if (!cas.HasAsset(hash))
            {
                missingHashes.Add(hash);
            }
        }

        string sessionId = Guid.NewGuid().ToString("N");
        var sessionDoc = JsonSerializer.SerializeToDocument(new
        {
            SessionId = sessionId,
            MapTitle = mapTitle,
            MapVersion = mapVersion,
            ManifestJson = jsonToProcess,
            PublicKey = req.PublicKey,
            Signature = req.Signature,
            ReferencedHashes = referencedHashes.ToList(),
            CreatedAt = DateTime.UtcNow
        });
        db.Upsert("publish_sessions", sessionId, sessionDoc);

        return Results.Ok(new PublishMapInitiateResponse
        {
            Success = true,
            SessionId = sessionId,
            MapId = compositeKey,
            Status = missingHashes.Count == 0 ? "ReadyToFinalize" : "UploadsRequired",
            MissingHashes = missingHashes,
            IsGreenlit = true,
            Message = missingHashes.Count == 0 ? "All assets already present on server." : $"{missingHashes.Count} assets require upload."
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new PublishMapInitiateResponse
        {
            Success = false,
            Status = "Error",
            Message = ex.Message
        });
    }
});

app.MapPost("/api/publish_map/finalize", (PublishMapFinalizeRequest req, DataStoreService db, ContentAddressableStorage cas, ClusterEventService clusterEvents, PeerRegistry registeredPeers, IHttpClientFactory httpClientFactory) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(req.SessionId) && (string.IsNullOrWhiteSpace(req.MapTitle) || string.IsNullOrWhiteSpace(req.PublicKey)))
        {
            return Results.BadRequest(new PublishMapFinalizeResponse
            {
                Success = false,
                Message = "SessionId or MapTitle/PublicKey required."
            });
        }

        JsonDocument? sessionDoc = !string.IsNullOrWhiteSpace(req.SessionId) ? db.Get<JsonDocument>("publish_sessions", req.SessionId) : null;
        string manifestJson = "";
        string mapTitle = req.MapTitle;
        string mapVersion = string.IsNullOrWhiteSpace(req.MapVersion) ? "1.0" : req.MapVersion;
        string publicKey = req.PublicKey;
        string signature = req.Signature;
        List<string> referencedHashes = new();

        if (sessionDoc != null)
        {
            var root = sessionDoc.RootElement;
            manifestJson = root.TryGetProperty("ManifestJson", out var mj) ? mj.GetString() ?? "" : "";
            mapTitle = root.TryGetProperty("MapTitle", out var mt) ? mt.GetString() ?? mapTitle : mapTitle;
            mapVersion = root.TryGetProperty("MapVersion", out var mv) ? mv.GetString() ?? mapVersion : mapVersion;
            publicKey = root.TryGetProperty("PublicKey", out var pk) ? pk.GetString() ?? publicKey : publicKey;
            signature = root.TryGetProperty("Signature", out var sig) ? sig.GetString() ?? signature : signature;

            if (root.TryGetProperty("ReferencedHashes", out var rhProp) && rhProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in rhProp.EnumerateArray())
                {
                    var str = el.GetString();
                    if (!string.IsNullOrEmpty(str)) referencedHashes.Add(str);
                }
            }
        }

        if (string.IsNullOrWhiteSpace(manifestJson))
        {
            return Results.BadRequest(new PublishMapFinalizeResponse
            {
                Success = false,
                Message = "Publish session not found or expired."
            });
        }

        var mapDoc = JsonDocument.Parse(manifestJson);
        var missingHashes = new List<string>();
        foreach (var hash in referencedHashes)
        {
            if (!cas.HasAsset(hash))
            {
                missingHashes.Add(hash);
            }
        }

        if (missingHashes.Count > 0)
        {
            return Results.BadRequest(new PublishMapFinalizeResponse
            {
                Success = false,
                Status = "MissingAssets",
                Message = $"Cannot finalize publish: {missingHashes.Count} assets are still missing on the server.",
                MissingHashes = missingHashes
            });
        }

        string compositeKey = $"{mapTitle}_{mapVersion}";
        var existingMap = db.Get<JsonDocument>("published_maps", compositeKey);
        if (existingMap != null)
        {
            return Results.BadRequest(new PublishMapFinalizeResponse
            {
                Success = false,
                Status = "AlreadyPublished",
                Message = $"Map '{mapTitle}' version '{mapVersion}' has already been published. Please increment the map version in Map Settings to publish an update."
            });
        }

        var existingOwner = db.Get<string>("map_ownership", mapTitle);
        if (existingOwner == null)
        {
            db.Upsert("map_ownership", mapTitle, publicKey);
        }

        string manifestDir = Path.Combine(cas.RootDirectory, "manifests");
        if (!Directory.Exists(manifestDir)) Directory.CreateDirectory(manifestDir);
        string manifestPath = Path.Combine(manifestDir, $"{compositeKey}_manifest.json");
        File.WriteAllText(manifestPath, manifestJson);
        string defaultManifestPath = Path.Combine(manifestDir, $"{mapTitle}_manifest.json");
        File.WriteAllText(defaultManifestPath, manifestJson);

        db.Upsert("published_maps", compositeKey, mapDoc);

        var pubEvent = new ClusterEventDto
        {
            EventType = "map_published",
            PublicKey = publicKey,
            Signature = signature,
            PayloadJson = JsonSerializer.Serialize(new MapPublishedEventPayload
            {
                MapTitle = mapTitle,
                MapVersion = mapVersion,
                ManifestJson = manifestJson,
                ReferencedHashes = referencedHashes,
                PublicKey = publicKey,
                Signature = signature
            })
        };

        clusterEvents.RecordEvent(pubEvent, db);
        clusterEvents.BroadcastEvent(pubEvent, registeredPeers, httpClientFactory);

        if (!string.IsNullOrWhiteSpace(req.SessionId))
        {
            db.Delete("publish_sessions", req.SessionId);
        }

        return Results.Ok(new PublishMapFinalizeResponse
        {
            Success = true,
            MapId = compositeKey,
            Status = "Published",
            Message = $"Map '{compositeKey}' successfully published."
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new PublishMapFinalizeResponse
        {
            Success = false,
            Status = "Error",
            Message = ex.Message
        });
    }
});

app.MapPost("/api/publish_map", (PublishMapRequest req, DataStoreService db, ContentAddressableStorage cas, ClusterEventService clusterEvents, PeerRegistry registeredPeers, IHttpClientFactory httpClientFactory) =>
{
    try {
        string jsonToProcess = !string.IsNullOrWhiteSpace(req.ManifestJson) ? req.ManifestJson : req.MapJson;
        if (string.IsNullOrWhiteSpace(jsonToProcess)) {
            return Results.BadRequest(new { Message = "ManifestJson or MapJson is required." });
        }

        var mapDoc = JsonDocument.Parse(jsonToProcess);
        var root = mapDoc.RootElement;
        
        string mapTitle = "";
        string mapVersion = "1.0";
        
        if (root.TryGetProperty("MapName", out var mapNameProp)) {
            mapTitle = mapNameProp.GetString() ?? "";
        }
        if (root.TryGetProperty("Version", out var versionProp)) {
            mapVersion = versionProp.GetString() ?? "1.0";
        }
        
        if (string.IsNullOrEmpty(mapTitle) && root.TryGetProperty("MapProperties", out var mapProps)) {
            if (mapProps.TryGetProperty("MapName", out var nameProp)) mapTitle = nameProp.GetString() ?? "";
            if (mapProps.TryGetProperty("MapTitle", out var titleProp)) mapTitle = titleProp.GetString() ?? mapTitle;
            if (mapProps.TryGetProperty("MapVersion", out var vProp)) mapVersion = vProp.GetString() ?? "1.0";
        }
        
        if (string.IsNullOrEmpty(mapTitle)) {
            return Results.BadRequest(new { Message = "MapName or MapTitle is required in manifest." });
        }
        
        if (string.IsNullOrEmpty(req.Signature) || string.IsNullOrEmpty(req.PublicKey)) {
            return Results.BadRequest(new { Message = "Map signature and public key are required." });
        }
        
        byte[] pubKeyBytes;
        byte[] sigBytes;
        try {
            pubKeyBytes = Convert.FromBase64String(req.PublicKey);
            sigBytes = Convert.FromBase64String(req.Signature);
        } catch {
            return Results.BadRequest(new { Message = "Invalid base64 encoding for signature or public key." });
        }
        
        var pubKey = NSec.Cryptography.PublicKey.Import(NSec.Cryptography.SignatureAlgorithm.Ed25519, pubKeyBytes, NSec.Cryptography.KeyBlobFormat.RawPublicKey);
        
        byte[] mapBytes = System.Text.Encoding.UTF8.GetBytes(jsonToProcess);
        string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(mapBytes, ".json");
        string mapHashStr = $"{canonicalBlake3}.json";
        byte[] mapHashBytes = System.Text.Encoding.UTF8.GetBytes(mapHashStr);
        
        if (!NSec.Cryptography.SignatureAlgorithm.Ed25519.Verify(pubKey, mapHashBytes, sigBytes)) {
            byte[] rawHashBytes = System.Text.Encoding.UTF8.GetBytes(canonicalBlake3);
            if (!NSec.Cryptography.SignatureAlgorithm.Ed25519.Verify(pubKey, rawHashBytes, sigBytes)) {
                if (!NSec.Cryptography.SignatureAlgorithm.Ed25519.Verify(pubKey, mapBytes, sigBytes)) {
                    return Results.BadRequest(new { Message = "Invalid map signature." });
                }
            }
        }

        string compositeKey = $"{mapTitle}_{mapVersion}";
        var stats = MapStatsHelper.GetStats(db, mapTitle, mapVersion, req.PublicKey);
        bool isGreenlit = stats != null && stats.IsGreenlit;
        if (!isGreenlit) {
            return Results.Json(new {
                Message = $"Map '{mapTitle}' is not greenlit for publication to the public registry. Accumulate more community playtime and ratings or request an admin override.",
                IsGreenlit = false,
                Stats = stats ?? new MapStats()
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var existingMap = db.Get<JsonDocument>("published_maps", compositeKey);
        if (existingMap != null) {
            return Results.BadRequest(new { Message = $"Map with Title '{mapTitle}' and Version '{mapVersion}' already exists." });
        }
        
        var ownership = db.Get<string>("map_ownership", mapTitle);
        if (ownership != null && ownership != req.PublicKey) {
            return Results.BadRequest(new { Message = "A map with this title already exists and is owned by a different key." });
        }
        if (ownership == null) {
            db.Upsert("map_ownership", mapTitle, req.PublicKey);
        }

        var contributors = new HashSet<string>();
        if (root.TryGetProperty("Contributors", out var contProp) && contProp.ValueKind == JsonValueKind.Array) {
            foreach (var element in contProp.EnumerateArray()) {
                var str = element.GetString();
                if (str != null) contributors.Add(str);
            }
        }
        if (root.TryGetProperty("Author", out var authorProp) && authorProp.ValueKind == JsonValueKind.String) {
            var authorStr = authorProp.GetString();
            if (!string.IsNullOrEmpty(authorStr)) contributors.Add(authorStr);
        }
        
        if (req.ReferencedHashes != null) {
            foreach (var hash in req.ReferencedHashes) {
                var assetMeta = db.Get<JsonDocument>("asset_signatures", hash);
                if (assetMeta != null) {
                    var author = assetMeta.RootElement.GetProperty("AuthorUsername").GetString();
                    if (!string.IsNullOrEmpty(author) && !contributors.Contains(author)) {
                        return Results.BadRequest(new { Message = $"Author '{author}' from asset '{hash}' is missing from the Contributors list." });
                    }
                }
            }
        }
        
        string manifestDir = Path.Combine(cas.RootDirectory, "manifests");
        if (!Directory.Exists(manifestDir)) Directory.CreateDirectory(manifestDir);
        string manifestPath = Path.Combine(manifestDir, $"{compositeKey}_manifest.json");
        File.WriteAllText(manifestPath, jsonToProcess);
        string defaultManifestPath = Path.Combine(manifestDir, $"{mapTitle}_manifest.json");
        File.WriteAllText(defaultManifestPath, jsonToProcess);

        db.Upsert("published_maps", compositeKey, mapDoc);

        var pubEvent = new ClusterEventDto
        {
            EventType = "map_published",
            PublicKey = req.PublicKey,
            Signature = req.Signature,
            PayloadJson = JsonSerializer.Serialize(new MapPublishedEventPayload
            {
                MapTitle = mapTitle,
                MapVersion = mapVersion,
                ManifestJson = jsonToProcess,
                ReferencedHashes = req.ReferencedHashes ?? new List<string>(),
                PublicKey = req.PublicKey,
                Signature = req.Signature
            })
        };
        clusterEvents.RecordEvent(pubEvent, db);
        clusterEvents.BroadcastEvent(pubEvent, registeredPeers, httpClientFactory);

        return Results.Ok(new { Status = "Published", MapId = compositeKey });
    }
    catch (Exception ex) {
        return Results.BadRequest(new { Message = "Invalid JSON or request", Error = ex.Message });
    }
});

app.MapGet("/api/cluster/events", (DateTime? sinceUtc, int? limit, ClusterEventService clusterEvents, DataStoreService db) =>
{
    var events = clusterEvents.GetEvents(sinceUtc, limit ?? 100, db);
    return Results.Ok(events);
});

app.MapGet("/api/cluster/state_digest", (ClusterEventService clusterEvents, DataStoreService db, PeerRegistry registeredPeers) =>
{
    var digest = clusterEvents.ComputeStateDigest(db);
    digest.OriginServerUrl = registeredPeers.SelfUrl;
    return Results.Ok(digest);
});

app.MapGet("/api/cluster/snapshot", (ClusterEventService clusterEvents, DataStoreService db) =>
{
    var snapshot = clusterEvents.GenerateSnapshot(db);
    return Results.Ok(snapshot);
});

app.MapPost("/api/cluster/snapshot", (HttpRequest request, ClusterSnapshotDto snapshot, ClusterEventService clusterEvents, DataStoreService db, ContentAddressableStorage cas) =>
{
    string? bypassHeader = request.Headers["X-Admin-Bypass"];
    string? sigHeader = request.Headers["X-Cluster-Signature"];
    string? pubKeyHeader = request.Headers["X-Admin-PublicKey"];

    bool isAuth = (!string.IsNullOrEmpty(bypassHeader) && adminPublicKeys.Count > 0 && AdminBypassAuth.VerifyBypassToken(adminPublicKeys, "cluster", "snapshot", bypassHeader))
        || (!string.IsNullOrEmpty(sigHeader) && (
            (!string.IsNullOrEmpty(pubKeyHeader) && adminPublicKeys.Contains(pubKeyHeader.ToString().Trim()) && AuthorSignatureHelper.VerifySignature(pubKeyHeader.ToString().Trim(), "cluster_snapshot", sigHeader.ToString()))
            || AuthorSignatureHelper.VerifySignatureAny(adminPublicKeys, "cluster_snapshot", sigHeader.ToString())
        ));

    if (!isAuth)
    {
        return Results.Json(new { Message = "Unauthorized snapshot import request." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var result = clusterEvents.ApplySnapshot(snapshot, db, cas, adminPublicKeys);
    return Results.Ok(result);
});

app.MapPost("/api/cluster/event", async (ClusterEventDto evt, ClusterEventService clusterEvents, DataStoreService db, ContentAddressableStorage cas) =>
{
    bool applied = await clusterEvents.ApplyEventAsync(evt, db, cas, adminPublicKeys);
    if (!applied)
    {
        return Results.BadRequest(new { Message = "Event verification or application failed." });
    }
    return Results.Ok(new { Status = "Applied", EventId = evt.EventId });
});

app.MapPost("/api/cluster/sync_events", async (List<ClusterEventDto> events, ClusterEventService clusterEvents, DataStoreService db, ContentAddressableStorage cas) =>
{
    int appliedCount = 0;
    if (events != null)
    {
        foreach (var evt in events)
        {
            if (await clusterEvents.ApplyEventAsync(evt, db, cas, adminPublicKeys))
            {
                appliedCount++;
            }
        }
    }
    return Results.Ok(new { Status = "Sync Complete", TotalEvents = events?.Count ?? 0, AppliedCount = appliedCount });
});

app.MapPost("/api/admin/prune_cas", (HttpRequest request, ClusterEventService clusterEvents, DataStoreService db, ContentAddressableStorage cas) =>
{
    string? bypassHeader = request.Headers["X-Admin-Bypass"];
    string? sigHeader = request.Headers["X-Cluster-Signature"];
    string? pubKeyHeader = request.Headers["X-Admin-PublicKey"];

    bool isAuth = (!string.IsNullOrEmpty(bypassHeader) && adminPublicKeys.Count > 0 && AdminBypassAuth.VerifyBypassToken(adminPublicKeys, "admin", "prune_cas", bypassHeader))
        || (!string.IsNullOrEmpty(sigHeader) && (
            (!string.IsNullOrEmpty(pubKeyHeader) && adminPublicKeys.Contains(pubKeyHeader.ToString().Trim()) && AuthorSignatureHelper.VerifySignature(pubKeyHeader.ToString().Trim(), "prune_cas", sigHeader.ToString()))
            || AuthorSignatureHelper.VerifySignatureAny(adminPublicKeys, "prune_cas", sigHeader.ToString())
        ));

    if (!isAuth)
    {
        return Results.Json(new { Message = "Unauthorized CAS pruning request." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    clusterEvents.QueueCasPrune(cas, db);
    return Results.Ok(new CasPruneResponseDto
    {
        Success = true,
        Message = "CAS prune operation has been queued on a background task."
    });
});

app.MapPost("/api/admin/remove_manifest", async (HttpRequest request, ClusterEventService clusterEvents, DataStoreService db, ContentAddressableStorage cas, PeerRegistry registeredPeers, IHttpClientFactory httpClientFactory) =>
{
    string body = string.Empty;
    using (var reader = new StreamReader(request.Body, Encoding.UTF8))
    {
        body = await reader.ReadToEndAsync();
    }

    AdminRemoveManifestRequest? req = null;
    if (!string.IsNullOrWhiteSpace(body))
    {
        try
        {
            req = JsonSerializer.Deserialize<AdminRemoveManifestRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { }
    }

    string mapTitle = req?.MapTitle ?? (request.Query.TryGetValue("map", out var qm) ? qm.ToString() : "");
    string? mapVersion = !string.IsNullOrWhiteSpace(req?.MapVersion) ? req.MapVersion : (request.Query.TryGetValue("version", out var qv) ? qv.ToString() : null);
    if (string.IsNullOrWhiteSpace(mapVersion)) mapVersion = null;

    if (string.IsNullOrWhiteSpace(mapTitle))
    {
        return Results.BadRequest(new RemoveManifestResponseDto { Success = false, Message = "MapTitle is required." });
    }

    if (adminPublicKeys.Count == 0)
    {
        return Results.Json(new RemoveManifestResponseDto { Success = false, Message = "Server has no AdminPublicKeys configured." }, statusCode: StatusCodes.Status403Forbidden);
    }

    string? bypassHeader = request.Headers["X-Admin-Bypass"];
    string? sigHeader = request.Headers["X-Cluster-Signature"];
    string? pubKeyHeader = request.Headers["X-Admin-PublicKey"];

    string callerPubKey = req?.AdminPublicKey ?? pubKeyHeader?.ToString() ?? "";
    string callerSig = req?.Signature ?? sigHeader?.ToString() ?? "";

    string targetVerStr = mapVersion ?? "all";
    string sigPayload1 = $"remove_manifest:{mapTitle.Trim().ToLowerInvariant()}:{targetVerStr.ToLowerInvariant()}";
    string sigPayload2 = $"remove_manifest:{mapTitle.Trim().ToLowerInvariant()}";

    bool isAuth = false;

    if (!string.IsNullOrEmpty(bypassHeader))
    {
        isAuth = AdminBypassAuth.VerifyBypassToken(adminPublicKeys, mapTitle, targetVerStr, bypassHeader)
            || AdminBypassAuth.VerifyBypassToken(adminPublicKeys, "admin", "remove_manifest", bypassHeader)
            || AdminBypassAuth.VerifyBypassToken(adminPublicKeys, mapTitle, "*", bypassHeader);
    }

    if (!isAuth && !string.IsNullOrEmpty(callerSig))
    {
        if (!string.IsNullOrEmpty(callerPubKey) && adminPublicKeys.Contains(callerPubKey.Trim()))
        {
            isAuth = AuthorSignatureHelper.VerifySignature(callerPubKey.Trim(), sigPayload1, callerSig)
                || AuthorSignatureHelper.VerifySignature(callerPubKey.Trim(), sigPayload2, callerSig)
                || AdminBypassAuth.VerifyBypassToken(callerPubKey.Trim(), mapTitle, targetVerStr, callerSig);
        }
        else
        {
            isAuth = AuthorSignatureHelper.VerifySignatureAny(adminPublicKeys, sigPayload1, callerSig)
                || AuthorSignatureHelper.VerifySignatureAny(adminPublicKeys, sigPayload2, callerSig)
                || AdminBypassAuth.VerifyBypassToken(adminPublicKeys, mapTitle, targetVerStr, callerSig);
        }
    }

    if (!isAuth)
    {
        return Results.Json(new RemoveManifestResponseDto { Success = false, Message = "Unauthorized admin request." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var result = clusterEvents.RemoveManifest(cas, db, mapTitle, mapVersion);

    var removeEvent = new ClusterEventDto
    {
        EventType = "admin_remove_manifest",
        PublicKey = !string.IsNullOrEmpty(callerPubKey) ? callerPubKey.Trim() : (adminPublicKeys.FirstOrDefault() ?? ""),
        Signature = callerSig,
        PayloadJson = JsonSerializer.Serialize(new AdminRemoveManifestEventPayload
        {
            MapTitle = mapTitle,
            MapVersion = mapVersion,
            AdminPublicKey = callerPubKey,
            Signature = callerSig
        })
    };
    clusterEvents.MarkEventProcessed(removeEvent.EventId, db);
    clusterEvents.BroadcastEvent(removeEvent, registeredPeers, httpClientFactory);

    return Results.Ok(result);
});

app.MapDelete("/api/admin/manifests/{mapTitle}", (string mapTitle, HttpRequest request, ClusterEventService clusterEvents, DataStoreService db, ContentAddressableStorage cas, PeerRegistry registeredPeers, IHttpClientFactory httpClientFactory) =>
{
    if (string.IsNullOrWhiteSpace(mapTitle))
    {
        return Results.BadRequest(new RemoveManifestResponseDto { Success = false, Message = "MapTitle is required." });
    }

    if (adminPublicKeys.Count == 0)
    {
        return Results.Json(new RemoveManifestResponseDto { Success = false, Message = "Server has no AdminPublicKeys configured." }, statusCode: StatusCodes.Status403Forbidden);
    }

    string? bypassHeader = request.Headers["X-Admin-Bypass"];
    string? sigHeader = request.Headers["X-Cluster-Signature"];
    string? pubKeyHeader = request.Headers["X-Admin-PublicKey"];

    string callerPubKey = pubKeyHeader?.ToString() ?? "";
    string callerSig = sigHeader?.ToString() ?? "";

    string sigPayload1 = $"remove_manifest:{mapTitle.Trim().ToLowerInvariant()}:all";
    string sigPayload2 = $"remove_manifest:{mapTitle.Trim().ToLowerInvariant()}";

    bool isAuth = false;

    if (!string.IsNullOrEmpty(bypassHeader))
    {
        isAuth = AdminBypassAuth.VerifyBypassToken(adminPublicKeys, mapTitle, "all", bypassHeader)
            || AdminBypassAuth.VerifyBypassToken(adminPublicKeys, "admin", "remove_manifest", bypassHeader)
            || AdminBypassAuth.VerifyBypassToken(adminPublicKeys, mapTitle, "*", bypassHeader);
    }

    if (!isAuth && !string.IsNullOrEmpty(callerSig))
    {
        if (!string.IsNullOrEmpty(callerPubKey) && adminPublicKeys.Contains(callerPubKey.Trim()))
        {
            isAuth = AuthorSignatureHelper.VerifySignature(callerPubKey.Trim(), sigPayload1, callerSig)
                || AuthorSignatureHelper.VerifySignature(callerPubKey.Trim(), sigPayload2, callerSig)
                || AdminBypassAuth.VerifyBypassToken(callerPubKey.Trim(), mapTitle, "all", callerSig);
        }
        else
        {
            isAuth = AuthorSignatureHelper.VerifySignatureAny(adminPublicKeys, sigPayload1, callerSig)
                || AuthorSignatureHelper.VerifySignatureAny(adminPublicKeys, sigPayload2, callerSig)
                || AdminBypassAuth.VerifyBypassToken(adminPublicKeys, mapTitle, "all", callerSig);
        }
    }

    if (!isAuth)
    {
        return Results.Json(new RemoveManifestResponseDto { Success = false, Message = "Unauthorized admin request." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var result = clusterEvents.RemoveManifest(cas, db, mapTitle, null);

    var removeEvent = new ClusterEventDto
    {
        EventType = "admin_remove_manifest",
        PublicKey = !string.IsNullOrEmpty(callerPubKey) ? callerPubKey.Trim() : (adminPublicKeys.FirstOrDefault() ?? ""),
        Signature = callerSig,
        PayloadJson = JsonSerializer.Serialize(new AdminRemoveManifestEventPayload
        {
            MapTitle = mapTitle,
            MapVersion = null,
            AdminPublicKey = callerPubKey,
            Signature = callerSig
        })
    };
    clusterEvents.MarkEventProcessed(removeEvent.EventId, db);
    clusterEvents.BroadcastEvent(removeEvent, registeredPeers, httpClientFactory);

    return Results.Ok(result);
});

app.MapDelete("/api/admin/manifests/{mapTitle}/{mapVersion}", (string mapTitle, string mapVersion, HttpRequest request, ClusterEventService clusterEvents, DataStoreService db, ContentAddressableStorage cas, PeerRegistry registeredPeers, IHttpClientFactory httpClientFactory) =>
{
    if (string.IsNullOrWhiteSpace(mapTitle) || string.IsNullOrWhiteSpace(mapVersion))
    {
        return Results.BadRequest(new RemoveManifestResponseDto { Success = false, Message = "MapTitle and MapVersion are required." });
    }

    if (adminPublicKeys.Count == 0)
    {
        return Results.Json(new RemoveManifestResponseDto { Success = false, Message = "Server has no AdminPublicKeys configured." }, statusCode: StatusCodes.Status403Forbidden);
    }

    string? bypassHeader = request.Headers["X-Admin-Bypass"];
    string? sigHeader = request.Headers["X-Cluster-Signature"];
    string? pubKeyHeader = request.Headers["X-Admin-PublicKey"];

    string callerPubKey = pubKeyHeader?.ToString() ?? "";
    string callerSig = sigHeader?.ToString() ?? "";

    string sigPayload = $"remove_manifest:{mapTitle.Trim().ToLowerInvariant()}:{mapVersion.Trim().ToLowerInvariant()}";

    bool isAuth = false;

    if (!string.IsNullOrEmpty(bypassHeader))
    {
        isAuth = AdminBypassAuth.VerifyBypassToken(adminPublicKeys, mapTitle, mapVersion.Trim(), bypassHeader)
            || AdminBypassAuth.VerifyBypassToken(adminPublicKeys, "admin", "remove_manifest", bypassHeader)
            || AdminBypassAuth.VerifyBypassToken(adminPublicKeys, mapTitle, "*", bypassHeader);
    }

    if (!isAuth && !string.IsNullOrEmpty(callerSig))
    {
        if (!string.IsNullOrEmpty(callerPubKey) && adminPublicKeys.Contains(callerPubKey.Trim()))
        {
            isAuth = AuthorSignatureHelper.VerifySignature(callerPubKey.Trim(), sigPayload, callerSig)
                || AdminBypassAuth.VerifyBypassToken(callerPubKey.Trim(), mapTitle, mapVersion.Trim(), callerSig);
        }
        else
        {
            isAuth = AuthorSignatureHelper.VerifySignatureAny(adminPublicKeys, sigPayload, callerSig)
                || AdminBypassAuth.VerifyBypassToken(adminPublicKeys, mapTitle, mapVersion.Trim(), callerSig);
        }
    }

    if (!isAuth)
    {
        return Results.Json(new RemoveManifestResponseDto { Success = false, Message = "Unauthorized admin request." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var result = clusterEvents.RemoveManifest(cas, db, mapTitle, mapVersion);

    var removeEvent = new ClusterEventDto
    {
        EventType = "admin_remove_manifest",
        PublicKey = !string.IsNullOrEmpty(callerPubKey) ? callerPubKey.Trim() : (adminPublicKeys.FirstOrDefault() ?? ""),
        Signature = callerSig,
        PayloadJson = JsonSerializer.Serialize(new AdminRemoveManifestEventPayload
        {
            MapTitle = mapTitle,
            MapVersion = mapVersion,
            AdminPublicKey = callerPubKey,
            Signature = callerSig
        })
    };
    clusterEvents.MarkEventProcessed(removeEvent.EventId, db);
    clusterEvents.BroadcastEvent(removeEvent, registeredPeers, httpClientFactory);

    return Results.Ok(result);
});

app.MapPost("/api/cluster/sync_metrics", async (HttpRequest request, ClusterEventService clusterEvents, DataStoreService db, ContentAddressableStorage cas) =>
{
    using var reader = new StreamReader(request.Body, Encoding.UTF8);
    string body = await reader.ReadToEndAsync();

    string? sigHeader = request.Headers["X-Cluster-Signature"];
    string? pubKeyHeader = request.Headers["X-Admin-PublicKey"];
    string? bypassHeader = request.Headers["X-Admin-Bypass"];

    bool isAuth = (!string.IsNullOrEmpty(bypassHeader) && adminPublicKeys.Count > 0 && AdminBypassAuth.VerifyBypassToken(adminPublicKeys, "cluster", "sync", bypassHeader))
        || (!string.IsNullOrEmpty(sigHeader) && (
            (!string.IsNullOrEmpty(pubKeyHeader) && adminPublicKeys.Contains(pubKeyHeader.ToString().Trim()) && (AuthorSignatureHelper.VerifySignature(pubKeyHeader.ToString().Trim(), "sync_metrics", sigHeader.ToString()) || AuthorSignatureHelper.VerifySignature(pubKeyHeader.ToString().Trim(), body, sigHeader.ToString())))
            || AuthorSignatureHelper.VerifySignatureAny(adminPublicKeys, "sync_metrics", sigHeader.ToString())
            || AuthorSignatureHelper.VerifySignatureAny(adminPublicKeys, body, sigHeader.ToString())
        ));

    if (!isAuth)
    {
        return Results.Json(new { Message = "Unauthorized cluster sync request." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var metricsList = JsonSerializer.Deserialize<List<MapStatsSyncDto>>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    if (metricsList != null)
    {
        foreach (var item in metricsList)
        {
            if (string.IsNullOrWhiteSpace(item.MapId)) continue;

            var existing = db.Get<MapStats>("map_stats", item.MapId) ?? new MapStats();
            existing.TotalPlaytimeMinutes = Math.Max(existing.TotalPlaytimeMinutes, item.TotalPlaytimeMinutes);
            existing.TotalGamesPlayed = Math.Max(existing.TotalGamesPlayed, item.TotalGamesPlayed);
            existing.ReviewsCount = Math.Max(existing.ReviewsCount, item.TotalReviewsCount);
            existing.VerifiedGoodReviewsCount = Math.Max(existing.VerifiedGoodReviewsCount, item.VerifiedGoodReviewsCount);
            if (item.AverageRating > 0 && item.TotalReviewsCount > 0)
            {
                existing.TotalStars = Math.Max(existing.TotalStars, (int)Math.Round(item.AverageRating * item.TotalReviewsCount));
            }
            if (item.AdminOverrideGreenlit)
            {
                existing.AdminOverrideGreenlit = true;
            }
            db.Upsert("map_stats", item.MapId, existing);
        }
    }

    return Results.Ok(new { Status = "Metrics Synchronized", Count = metricsList?.Count ?? 0 });
});

app.MapPost("/api/cluster/sync_published_maps", async (HttpRequest request, ClusterEventService clusterEvents, DataStoreService db, ContentAddressableStorage cas) =>
{
    using var reader = new StreamReader(request.Body, Encoding.UTF8);
    string body = await reader.ReadToEndAsync();

    string? sigHeader = request.Headers["X-Cluster-Signature"];
    string? pubKeyHeader = request.Headers["X-Admin-PublicKey"];
    string? bypassHeader = request.Headers["X-Admin-Bypass"];

    bool isAuth = (!string.IsNullOrEmpty(bypassHeader) && adminPublicKeys.Count > 0 && AdminBypassAuth.VerifyBypassToken(adminPublicKeys, "cluster", "sync", bypassHeader))
        || (!string.IsNullOrEmpty(sigHeader) && (
            (!string.IsNullOrEmpty(pubKeyHeader) && adminPublicKeys.Contains(pubKeyHeader.ToString().Trim()) && (AuthorSignatureHelper.VerifySignature(pubKeyHeader.ToString().Trim(), "sync_published_maps", sigHeader.ToString()) || AuthorSignatureHelper.VerifySignature(pubKeyHeader.ToString().Trim(), body, sigHeader.ToString())))
            || AuthorSignatureHelper.VerifySignatureAny(adminPublicKeys, "sync_published_maps", sigHeader.ToString())
            || AuthorSignatureHelper.VerifySignatureAny(adminPublicKeys, body, sigHeader.ToString())
        ));

    if (!isAuth)
    {
        return Results.Json(new { Message = "Unauthorized cluster sync request." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var mapsList = JsonSerializer.Deserialize<List<PublishedMapSyncDto>>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    if (mapsList != null)
    {
        string manifestDir = Path.Combine(cas.RootDirectory, "manifests");
        if (!Directory.Exists(manifestDir)) Directory.CreateDirectory(manifestDir);

        foreach (var map in mapsList)
        {
            if (string.IsNullOrWhiteSpace(map.MapId)) continue;

            if (!string.IsNullOrWhiteSpace(map.ManifestJson))
            {
                string manifestPath = Path.Combine(manifestDir, $"{map.MapId}_manifest.json");
                File.WriteAllText(manifestPath, map.ManifestJson);
                if (!string.IsNullOrWhiteSpace(map.MapTitle))
                {
                    string defaultPath = Path.Combine(manifestDir, $"{map.MapTitle}_manifest.json");
                    File.WriteAllText(defaultPath, map.ManifestJson);
                }

                try
                {
                    var doc = JsonDocument.Parse(map.ManifestJson);
                    db.Upsert("published_maps", map.MapId, doc);
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(map.OwnerPublicKey) && !string.IsNullOrWhiteSpace(map.MapTitle))
            {
                var existingOwner = db.Get<string>("map_ownership", map.MapTitle);
                if (existingOwner == null)
                {
                    db.Upsert("map_ownership", map.MapTitle, map.OwnerPublicKey);
                }
            }
        }
    }

    return Results.Ok(new { Status = "Published Maps Synchronized", Count = mapsList?.Count ?? 0 });
});

app.MapPost("/api/cluster/sync_creators", async (HttpRequest request, ClusterEventService clusterEvents, DataStoreService db) =>
{
    using var reader = new StreamReader(request.Body, Encoding.UTF8);
    string body = await reader.ReadToEndAsync();

    string? sigHeader = request.Headers["X-Cluster-Signature"];
    string? pubKeyHeader = request.Headers["X-Admin-PublicKey"];
    string? bypassHeader = request.Headers["X-Admin-Bypass"];

    bool isAuth = (!string.IsNullOrEmpty(bypassHeader) && adminPublicKeys.Count > 0 && AdminBypassAuth.VerifyBypassToken(adminPublicKeys, "cluster", "sync", bypassHeader))
        || (!string.IsNullOrEmpty(sigHeader) && (
            (!string.IsNullOrEmpty(pubKeyHeader) && adminPublicKeys.Contains(pubKeyHeader.ToString().Trim()) && (AuthorSignatureHelper.VerifySignature(pubKeyHeader.ToString().Trim(), "sync_creators", sigHeader.ToString()) || AuthorSignatureHelper.VerifySignature(pubKeyHeader.ToString().Trim(), body, sigHeader.ToString())))
            || AuthorSignatureHelper.VerifySignatureAny(adminPublicKeys, "sync_creators", sigHeader.ToString())
            || AuthorSignatureHelper.VerifySignatureAny(adminPublicKeys, body, sigHeader.ToString())
        ));

    if (!isAuth)
    {
        return Results.Json(new { Message = "Unauthorized cluster sync request." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var creatorsList = JsonSerializer.Deserialize<List<CreatorSyncDto>>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    if (creatorsList != null)
    {
        foreach (var creator in creatorsList)
        {
            if (string.IsNullOrWhiteSpace(creator.PublicKey) || string.IsNullOrWhiteSpace(creator.Username)) continue;

            string slug = creator.Username.ToLowerInvariant().Replace(" ", "-");
            var existingLock = db.Get<JsonDocument>("name_locks", slug);
            if (existingLock == null)
            {
                var lockDoc = JsonSerializer.SerializeToDocument(new
                {
                    username = creator.Username,
                    owner_public_key = creator.PublicKey,
                    registered_at = creator.RegisteredAt ?? DateTime.UtcNow
                });
                db.Upsert("name_locks", slug, lockDoc);
            }

            var existingCreator = db.Get<JsonDocument>("creators", creator.PublicKey);
            if (existingCreator == null)
            {
                var creatorDoc = JsonSerializer.SerializeToDocument(new
                {
                    username = creator.Username,
                    public_key = creator.PublicKey,
                    donation_link = creator.DonationLink ?? "",
                    contact_info = creator.ContactInfo ?? "",
                    registered_at = creator.RegisteredAt ?? DateTime.UtcNow
                });
                db.Upsert("creators", creator.PublicKey, creatorDoc);
            }
        }
    }

    return Results.Ok(new { Status = "Creators Synchronized", Count = creatorsList?.Count ?? 0 });
});

app.MapPost("/api/publish_map/upload_asset", async (HttpRequest request, DataStoreService db, ContentAddressableStorage cas) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("Expected multipart/form-data");
        
    var form = await request.ReadFormAsync();
    
    string hash = form["Hash"].ToString();
    string signature = form["Signature"].ToString();
    string authorUsername = form["AuthorUsername"].ToString();
    string publicKey = form["PublicKey"].ToString();
    string mapTitle = form.TryGetValue("MapTitle", out var mt) ? mt.ToString() : "";
    string mapVersion = form.TryGetValue("MapVersion", out var mv) ? mv.ToString() : "1.0";
    string sessionId = form.TryGetValue("SessionId", out var sid) ? sid.ToString() : "";
    
    bool isSessionValid = !string.IsNullOrWhiteSpace(sessionId) && db.Get<JsonDocument>("publish_sessions", sessionId) != null;
    if (!isSessionValid && !string.IsNullOrEmpty(mapTitle))
    {
        var stats = MapStatsHelper.GetStats(db, mapTitle, mapVersion, publicKey);
        if (stats == null || !stats.IsGreenlit)
        {
            return Results.Json(new { Message = $"Cannot upload asset: map '{mapTitle}' is not greenlit." }, statusCode: StatusCodes.Status403Forbidden);
        }
    }
    
    string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(hash);
    if (string.IsNullOrEmpty(normalizedHash) || normalizedHash.Contains('/') || normalizedHash.Contains('\\')) return Results.BadRequest("Invalid Hash.");

    try {
        byte[] pubKeyBytes = Convert.FromBase64String(publicKey);
        byte[] sigBytes = Convert.FromBase64String(signature);
        var pubKey = NSec.Cryptography.PublicKey.Import(NSec.Cryptography.SignatureAlgorithm.Ed25519, pubKeyBytes, NSec.Cryptography.KeyBlobFormat.RawPublicKey);
        byte[] hashBytes = Encoding.UTF8.GetBytes(hash);
        byte[] normalizedHashBytes = Encoding.UTF8.GetBytes(normalizedHash);
        if (!NSec.Cryptography.SignatureAlgorithm.Ed25519.Verify(pubKey, hashBytes, sigBytes) &&
            !NSec.Cryptography.SignatureAlgorithm.Ed25519.Verify(pubKey, normalizedHashBytes, sigBytes)) {
            return Results.BadRequest("Invalid asset signature.");
        }
    } catch {
        return Results.BadRequest("Invalid asset signature format.");
    }
    
    var existingMeta = db.Get<JsonDocument>("asset_signatures", normalizedHash) ?? db.Get<JsonDocument>("asset_signatures", hash);
    if (existingMeta == null) {
        var metaDoc = JsonSerializer.SerializeToDocument(new { 
            Signature = signature, 
            AuthorUsername = authorUsername,
            PublicKey = publicKey,
            Hash = normalizedHash
        });
        db.Upsert("asset_signatures", normalizedHash, metaDoc);
        db.Upsert("asset_signatures", hash, metaDoc);
    }
    
    var file = form.Files.GetFile("File");
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest("Asset file payload is missing or empty.");
    }

    if (file.Length > ContentAddressableStorage.MaximumAssetSizeBytes)
    {
        double sizeMb = (double)file.Length / (1024 * 1024);
        double maxMb = (double)ContentAddressableStorage.MaximumAssetSizeBytes / (1024 * 1024);
        return Results.BadRequest($"Asset '{file.FileName}' ({sizeMb:F2} MB) exceeds maximum allowed size of {maxMb:F0} MB per asset.");
    }

    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);
    byte[] fileBytes = ms.ToArray();
    string ext = Path.GetExtension(file.FileName);

    string computedBlake3 = RealmMetadataHelper.ComputeBlake3(fileBytes, ext);
    string computedNorm = ContentAddressableStorage.NormalizeBlake3Hash(computedBlake3);
    if (!string.Equals(computedNorm, normalizedHash, StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { Message = $"Asset content hash mismatch: expected {normalizedHash}, got {computedNorm}." });
    }

    var storeResult = cas.StoreAsset(fileBytes, ext, null, publicKey, signature, precomputedBlake3: normalizedHash);
    if (!storeResult.Success)
    {
        return Results.BadRequest($"Failed to store asset in CAS: {storeResult.Message}");
    }

    return Results.Ok(new { Status = "Asset registered" });
});

app.MapGet("/api/publish_map/asset_author/{hash}", (string hash, DataStoreService db) => {
    var existingMeta = db.Get<JsonDocument>("asset_signatures", hash);
    if (existingMeta != null) {
        return Results.Ok(existingMeta);
    }
    return Results.NotFound();
});

app.MapPost("/api/creators/register", async (HttpRequest request, RegisterCreatorRequest req, DataStoreService db, ClusterEventService clusterEvents, PeerRegistry registeredPeers, IHttpClientFactory httpClientFactory) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.PublicKey) || string.IsNullOrWhiteSpace(req.Signature))
    {
        return Results.BadRequest(new { Message = "Username, PublicKey, and Signature are required." });
    }

    string username = req.Username.Trim();
    string pubKey = req.PublicKey.Trim();
    string signature = req.Signature.Trim();

    bool sigValid = AuthorSignatureHelper.VerifySignature(pubKey, $"{username}:{pubKey}", signature)
        || AuthorSignatureHelper.VerifySignature(pubKey, username, signature);

    if (!sigValid)
    {
        return Results.BadRequest(new { Message = "Invalid cryptographic signature for public key." });
    }

    string slug = username.ToLowerInvariant().Replace(" ", "-");
    var existingLock = db.Get<JsonDocument>("name_locks", slug);
    if (existingLock != null)
    {
        var root = existingLock.RootElement;
        string owner = root.TryGetProperty("owner_public_key", out var op) ? op.GetString() ?? "" : "";
        if (!string.Equals(owner, pubKey, StringComparison.OrdinalIgnoreCase))
        {
            string? bypassToken = request.Headers["X-Admin-Bypass"];
            bool bypassValid = adminPublicKeys.Count > 0 &&
                               AdminBypassAuth.VerifyBypassToken(adminPublicKeys, "admin", "override", bypassToken);

            if (!bypassValid)
            {
                return Results.Conflict(new { Message = "This username is already officially registered to another creator key." });
            }
        }
    }

    var lockDoc = JsonSerializer.SerializeToDocument(new
    {
        username = username,
        owner_public_key = pubKey,
        registered_at = DateTime.UtcNow
    });
    db.Upsert("name_locks", slug, lockDoc);

    var creatorDoc = JsonSerializer.SerializeToDocument(new
    {
        username = username,
        public_key = pubKey,
        donation_link = req.DonationLink ?? "",
        contact_info = req.ContactInfo ?? "",
        registered_at = DateTime.UtcNow
    });
    db.Upsert("creators", pubKey, creatorDoc);

    var creatorEvent = new ClusterEventDto
    {
        EventType = "creator_registered",
        PublicKey = pubKey,
        Signature = signature,
        PayloadJson = JsonSerializer.Serialize(new CreatorRegisteredEventPayload
        {
            Username = username,
            PublicKey = pubKey,
            Signature = signature,
            DonationLink = req.DonationLink,
            ContactInfo = req.ContactInfo,
            AdminBypassToken = request.Headers["X-Admin-Bypass"].ToString()
        })
    };
    clusterEvents.MarkEventProcessed(creatorEvent.EventId, db);
    clusterEvents.BroadcastEvent(creatorEvent, registeredPeers, httpClientFactory);

    return Results.Ok(new { Status = "Registered", Username = username, PublicKey = pubKey });
});

app.MapGet("/api/creators/check/{pubKey}", (string pubKey, DataStoreService db) =>
{
    var creator = db.Get<JsonDocument>("creators", pubKey);
    if (creator != null)
    {
        var root = creator.RootElement;
        string username = root.TryGetProperty("username", out var uProp) ? uProp.GetString() ?? "" : "";
        return Results.Ok(new { Registered = true, Username = username, PublicKey = pubKey });
    }
    return Results.NotFound(new { Registered = false, Message = "Creator key not registered." });
});

app.MapGet("/api/creators", (DataStoreService db) =>
{
    var creators = db.GetAll<JsonDocument>("creators").Select(doc =>
    {
        var root = doc.RootElement;
        return new
        {
            PublicKey = root.TryGetProperty("public_key", out var pk) ? pk.GetString() ?? "" : "",
            Username = root.TryGetProperty("username", out var un) ? un.GetString() ?? "" : "",
            DonationLink = root.TryGetProperty("donation_link", out var dl) ? dl.GetString() ?? "" : "",
            ContactInfo = root.TryGetProperty("contact_info", out var ci) ? ci.GetString() ?? "" : ""
        };
    }).Where(c => !string.IsNullOrEmpty(c.PublicKey)).ToList();

    return Results.Ok(creators);
});

app.MapGet("/api/creators/{pubKey}", (string pubKey, DataStoreService db) =>
{
    var creator = db.Get<JsonDocument>("creators", pubKey);
    return creator != null ? Results.Ok(creator) : Results.NotFound();
});

app.MapGet("/api/creators/{pubKey}/portfolio", (string pubKey, DataStoreService db) =>
{
    var creator = db.Get<JsonDocument>("creators", pubKey);
    string creatorName = "";
    if (creator != null && creator.RootElement.TryGetProperty("username", out var uProp))
    {
        creatorName = uProp.GetString() ?? "";
    }

    var assets = new List<object>();
    var allSignatures = db.GetAll<JsonDocument>("asset_signatures");
    foreach (var sig in allSignatures)
    {
        var root = sig.RootElement;
        string sigPubKey = root.TryGetProperty("PublicKey", out var pkProp) ? pkProp.GetString() ?? "" : "";
        string author = root.TryGetProperty("AuthorUsername", out var aProp) ? aProp.GetString() ?? "" : "";
        string hash = root.TryGetProperty("Hash", out var hProp) ? hProp.GetString() ?? "" : "";

        if (string.Equals(sigPubKey, pubKey, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(creatorName) && string.Equals(author, creatorName, StringComparison.OrdinalIgnoreCase)))
        {
            assets.Add(new
            {
                Hash = hash,
                AuthorUsername = author
            });
        }
    }

    return Results.Ok(assets);
});

app.MapGet("/api/discovery/maps", (DataStoreService db, ContentAddressableStorage cas) =>
{
    var discoveryList = new List<DiscoveryMapDto>();
    var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    static string FormatByteSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    var publishedDocs = db.GetAll<JsonDocument>("published_maps");
    foreach (var doc in publishedDocs)
    {
        try
        {
            var root = doc.RootElement;
            string mapTitle = root.TryGetProperty("MapName", out var mn) ? mn.GetString() ?? "" : "";
            string mapVersion = root.TryGetProperty("Version", out var ver) ? ver.GetString() ?? "1.0.0" : "1.0.0";
            if (string.IsNullOrWhiteSpace(mapTitle)) continue;

            string compositeKey = $"{mapTitle}_{mapVersion}";
            if (seenKeys.Contains(compositeKey)) continue;
            seenKeys.Add(compositeKey);

            var stats = MapStatsHelper.GetStats(db, mapTitle, mapVersion, null);
            bool isGreenlit = stats != null && stats.IsGreenlit;

            string creator = root.TryGetProperty("Author", out var auth) ? auth.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(creator) && root.TryGetProperty("Contributors", out var conts) && conts.ValueKind == JsonValueKind.Array && conts.GetArrayLength() > 0)
            {
                creator = conts[0].GetString() ?? "";
            }
            if (string.IsNullOrEmpty(creator))
            {
                var ownerKey = db.Get<string>("map_ownership", mapTitle);
                if (!string.IsNullOrEmpty(ownerKey))
                {
                    var creatorDoc = db.Get<JsonDocument>("creators", ownerKey);
                    if (creatorDoc != null && creatorDoc.RootElement.TryGetProperty("username", out var un))
                    {
                        creator = un.GetString() ?? "";
                    }
                }
            }
            if (string.IsNullOrEmpty(creator)) creator = "Realm Builder";

            string description = root.TryGetProperty("Description", out var d) ? d.GetString() ?? "" : "";
            var tags = new List<string>();
            if (root.TryGetProperty("Tags", out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tagsProp.EnumerateArray())
                {
                    string? s = t.GetString();
                    if (!string.IsNullOrEmpty(s)) tags.Add(s);
                }
            }

            long totalBytes = 0;
            string thumbnailHash = "";
            var screenshotHashes = new List<string>();
            string? metadataFileHash = null;

            if (root.TryGetProperty("Files", out var filesProp) && filesProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var fileProp in filesProp.EnumerateObject())
                {
                    string path = fileProp.Name;
                    string rawHash = fileProp.Value.GetString() ?? "";
                    string norm = ContentAddressableStorage.NormalizeBlake3Hash(rawHash);

                    if (path.Equals("metadata.json", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith("/metadata.json", StringComparison.OrdinalIgnoreCase))
                    {
                        metadataFileHash = norm;
                    }

                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext is ".png" or ".jpg" or ".jpeg" or ".webp")
                    {
                        if (string.IsNullOrEmpty(thumbnailHash) && (path.Contains("thumb", StringComparison.OrdinalIgnoreCase) || path.Contains("icon", StringComparison.OrdinalIgnoreCase) || path.Contains("cover", StringComparison.OrdinalIgnoreCase) || path.Contains("minimap", StringComparison.OrdinalIgnoreCase)))
                        {
                            thumbnailHash = norm;
                        }
                        else
                        {
                            screenshotHashes.Add(norm);
                        }
                    }

                    string? assetPath = cas.FindAssetFilePath(norm);
                    if (assetPath != null && File.Exists(assetPath))
                    {
                        totalBytes += new FileInfo(assetPath).Length;
                    }
                }
            }

            string engineVersion = "Godot Realm Engine v1.0";
            string maxPlayers = "8 Players";
            string genre = tags.Count > 0 ? tags[0] : "Custom Map";

            if (!string.IsNullOrEmpty(metadataFileHash))
            {
                string? metaFilePath = cas.FindAssetFilePath(metadataFileHash);
                if (metaFilePath != null && File.Exists(metaFilePath))
                {
                    try
                    {
                        var metaDoc = JsonDocument.Parse(File.ReadAllText(metaFilePath));
                        var metaRoot = metaDoc.RootElement;
                        if (metaRoot.TryGetProperty("MapProperties", out var mp) && mp.ValueKind == JsonValueKind.Object)
                        {
                            if (string.IsNullOrEmpty(description) && mp.TryGetProperty("MapDescription", out var md))
                            {
                                string? mdStr = md.GetString();
                                if (!string.IsNullOrEmpty(mdStr)) description = mdStr;
                            }
                            if (string.IsNullOrEmpty(description) && mp.TryGetProperty("HowToPlayObjective", out var htp))
                            {
                                string? htpStr = htp.GetString();
                                if (!string.IsNullOrEmpty(htpStr)) description = htpStr;
                            }
                            if (mp.TryGetProperty("Tags", out var metaTags) && metaTags.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var t in metaTags.EnumerateArray())
                                {
                                    string? s = t.GetString();
                                    if (!string.IsNullOrEmpty(s) && !tags.Contains(s)) tags.Add(s);
                                }
                            }
                            if (mp.TryGetProperty("PlayerSlots", out var slotsProp) && slotsProp.ValueKind == JsonValueKind.Array && slotsProp.GetArrayLength() > 0)
                            {
                                maxPlayers = $"{slotsProp.GetArrayLength()} Players";
                            }
                            else if (mp.TryGetProperty("SuggestedPlayers", out var sp) && !string.IsNullOrEmpty(sp.GetString()))
                            {
                                maxPlayers = sp.GetString()!;
                            }
                            if (mp.TryGetProperty("MapType", out var mt) && !string.IsNullOrEmpty(mt.GetString()))
                            {
                                genre = mt.GetString()!;
                            }
                        }

                        if (metaRoot.TryGetProperty("CustomUnits", out var cu) && cu.ValueKind == JsonValueKind.Array && cu.GetArrayLength() > 0)
                        {
                            if (!tags.Contains("Custom Units")) tags.Add("Custom Units");
                        }
                        if (metaRoot.TryGetProperty("CustomBuildings", out var cb) && cb.ValueKind == JsonValueKind.Array && cb.GetArrayLength() > 0)
                        {
                            if (!tags.Contains("Custom Buildings")) tags.Add("Custom Buildings");
                        }
                        if (metaRoot.TryGetProperty("CustomAbilities", out var ca) && ca.ValueKind == JsonValueKind.Array && ca.GetArrayLength() > 0)
                        {
                            if (!tags.Contains("Custom Abilities")) tags.Add("Custom Abilities");
                        }
                        if (metaRoot.TryGetProperty("CustomWeapons", out var cw) && cw.ValueKind == JsonValueKind.Array && cw.GetArrayLength() > 0)
                        {
                            if (!tags.Contains("Custom Weapons")) tags.Add("Custom Weapons");
                        }
                        if (metaRoot.TryGetProperty("CustomUpgrades", out var cup) && cup.ValueKind == JsonValueKind.Array && cup.GetArrayLength() > 0)
                        {
                            if (!tags.Contains("Custom Upgrades")) tags.Add("Custom Upgrades");
                        }
                        if (metaRoot.TryGetProperty("CustomProps", out var cpr) && cpr.ValueKind == JsonValueKind.Array && cpr.GetArrayLength() > 0)
                        {
                            if (!tags.Contains("Custom Props")) tags.Add("Custom Props");
                        }
                        if (metaRoot.TryGetProperty("CustomItems", out var ci) && ci.ValueKind == JsonValueKind.Array && ci.GetArrayLength() > 0)
                        {
                            if (!tags.Contains("Custom Items")) tags.Add("Custom Items");
                        }
                        if (metaRoot.TryGetProperty("CustomAttachments", out var cat) && cat.ValueKind == JsonValueKind.Array && cat.GetArrayLength() > 0)
                        {
                            if (!tags.Contains("Custom Attachments")) tags.Add("Custom Attachments");
                        }
                        if (metaRoot.TryGetProperty("CustomVfx", out var cvfx) && cvfx.ValueKind == JsonValueKind.Array && cvfx.GetArrayLength() > 0)
                        {
                            if (!tags.Contains("Custom VFX")) tags.Add("Custom VFX");
                        }
                        if (metaRoot.TryGetProperty("EngineVersion", out var ev) && !string.IsNullOrEmpty(ev.GetString()))
                        {
                            engineVersion = ev.GetString()!;
                        }
                        else if (metaRoot.TryGetProperty("GameBuildNumber", out var gb) && !string.IsNullOrEmpty(gb.GetString()))
                        {
                            engineVersion = $"Build {gb.GetString()}";
                        }
                    }
                    catch { }
                }
            }

            if (string.IsNullOrEmpty(thumbnailHash) && screenshotHashes.Count > 0)
            {
                thumbnailHash = screenshotHashes[0];
            }

            float ratingStars = stats != null && stats.AverageRating > 0 ? (float)stats.AverageRating : 4.5f;
            int totalReviews = stats != null ? stats.ReviewsCount : 0;
            int verifiedReviews = stats != null ? stats.VerifiedGoodReviewsCount : 0;
            int playtimeMinutes = stats != null ? (int)stats.TotalPlaytimeMinutes : 0;
            int gamesPlayed = stats != null ? stats.TotalGamesPlayed : 0;

            var awards = new List<string>();
            if (isGreenlit) awards.Add("res://Assets/UI/victory_flag.png");
            if (ratingStars >= 4.5f) awards.Add("res://Assets/UI/gold_coin.png");
            if (verifiedReviews >= 5 || totalReviews >= 20) awards.Add("res://Assets/UI/battle_shield.png");
            if (gamesPlayed >= 10 || playtimeMinutes >= 30) awards.Add("res://Assets/UI/battle_axe.png");
            if (awards.Count == 0) awards.Add("res://Assets/UI/gold_coin.png");

            discoveryList.Add(new DiscoveryMapDto
            {
                MapId = compositeKey,
                Title = mapTitle,
                Version = mapVersion,
                Creator = creator,
                Description = description,
                Genre = genre,
                ThumbnailHash = thumbnailHash,
                Screenshots = screenshotHashes,
                Features = tags,
                Tags = tags,
                RatingStars = ratingStars,
                TotalReviews = totalReviews,
                VerifiedGoodReviews = verifiedReviews,
                AverageRating = ratingStars,
                PlaytimeMinutes = playtimeMinutes,
                GamesPlayed = gamesPlayed,
                TotalSizeBytes = totalBytes,
                FileSizeFormatted = FormatByteSize(totalBytes),
                EngineVersion = engineVersion,
                MaxPlayers = maxPlayers,
                IsGreenlit = isGreenlit,
                Awards = awards
            });
        }
        catch { }
    }

    var manifestDir = Path.Combine(cas.RootDirectory, "manifests");
    if (Directory.Exists(manifestDir))
    {
        foreach (var file in Directory.EnumerateFiles(manifestDir, "*_manifest.json"))
        {
            try
            {
                string json = File.ReadAllText(file);
                var manifest = MapManifest.LoadFromJson(json);
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.MapName)) continue;

                string compositeKey = $"{manifest.MapName}_{manifest.Version}";
                if (seenKeys.Contains(compositeKey)) continue;
                seenKeys.Add(compositeKey);

                var stats = MapStatsHelper.GetStats(db, manifest.MapName, manifest.Version, null);
                bool isGreenlit = stats != null && stats.IsGreenlit;

                long totalBytes = 0;
                string thumbnailHash = "";
                var screenshotHashes = new List<string>();
                string? metadataFileHash = null;

                foreach (var pair in manifest.Files)
                {
                    string path = pair.Key;
                    string norm = ContentAddressableStorage.NormalizeBlake3Hash(pair.Value);

                    if (path.Equals("metadata.json", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith("/metadata.json", StringComparison.OrdinalIgnoreCase))
                    {
                        metadataFileHash = norm;
                    }

                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext is ".png" or ".jpg" or ".jpeg" or ".webp")
                    {
                        if (string.IsNullOrEmpty(thumbnailHash) && (path.Contains("thumb", StringComparison.OrdinalIgnoreCase) || path.Contains("icon", StringComparison.OrdinalIgnoreCase) || path.Contains("cover", StringComparison.OrdinalIgnoreCase) || path.Contains("minimap", StringComparison.OrdinalIgnoreCase)))
                        {
                            thumbnailHash = norm;
                        }
                        else
                        {
                            screenshotHashes.Add(norm);
                        }
                    }

                    string? assetPath = cas.FindAssetFilePath(norm);
                    if (assetPath != null && File.Exists(assetPath))
                    {
                        totalBytes += new FileInfo(assetPath).Length;
                    }
                }

                if (string.IsNullOrEmpty(thumbnailHash) && screenshotHashes.Count > 0)
                {
                    thumbnailHash = screenshotHashes[0];
                }

                var tags = manifest.Tags != null ? new List<string>(manifest.Tags) : new List<string>();
                string description = manifest.Description;
                string engineVersion = "Godot Realm Engine v1.0";
                string maxPlayers = "8 Players";
                string genre = tags.Count > 0 ? tags[0] : "Custom Map";

                if (!string.IsNullOrEmpty(metadataFileHash))
                {
                    string? metaFilePath = cas.FindAssetFilePath(metadataFileHash);
                    if (metaFilePath != null && File.Exists(metaFilePath))
                    {
                        try
                        {
                            var metaDoc = JsonDocument.Parse(File.ReadAllText(metaFilePath));
                            var metaRoot = metaDoc.RootElement;
                            if (metaRoot.TryGetProperty("MapProperties", out var mp) && mp.ValueKind == JsonValueKind.Object)
                            {
                                if (string.IsNullOrEmpty(description) && mp.TryGetProperty("MapDescription", out var md))
                                {
                                    string? mdStr = md.GetString();
                                    if (!string.IsNullOrEmpty(mdStr)) description = mdStr;
                                }
                                if (string.IsNullOrEmpty(description) && mp.TryGetProperty("HowToPlayObjective", out var htp))
                                {
                                    string? htpStr = htp.GetString();
                                    if (!string.IsNullOrEmpty(htpStr)) description = htpStr;
                                }
                                if (mp.TryGetProperty("Tags", out var metaTags) && metaTags.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var t in metaTags.EnumerateArray())
                                    {
                                        string? s = t.GetString();
                                        if (!string.IsNullOrEmpty(s) && !tags.Contains(s)) tags.Add(s);
                                    }
                                }
                                if (mp.TryGetProperty("PlayerSlots", out var slotsProp) && slotsProp.ValueKind == JsonValueKind.Array && slotsProp.GetArrayLength() > 0)
                                {
                                    maxPlayers = $"{slotsProp.GetArrayLength()} Players";
                                }
                                else if (mp.TryGetProperty("SuggestedPlayers", out var sp) && !string.IsNullOrEmpty(sp.GetString()))
                                {
                                    maxPlayers = sp.GetString()!;
                                }
                                if (mp.TryGetProperty("MapType", out var mt) && !string.IsNullOrEmpty(mt.GetString()))
                                {
                                    genre = mt.GetString()!;
                                }
                            }

                            if (metaRoot.TryGetProperty("CustomUnits", out var cu) && cu.ValueKind == JsonValueKind.Array && cu.GetArrayLength() > 0)
                            {
                                if (!tags.Contains("Custom Units")) tags.Add("Custom Units");
                            }
                            if (metaRoot.TryGetProperty("CustomBuildings", out var cb) && cb.ValueKind == JsonValueKind.Array && cb.GetArrayLength() > 0)
                            {
                                if (!tags.Contains("Custom Buildings")) tags.Add("Custom Buildings");
                            }
                            if (metaRoot.TryGetProperty("CustomAbilities", out var ca) && ca.ValueKind == JsonValueKind.Array && ca.GetArrayLength() > 0)
                            {
                                if (!tags.Contains("Custom Abilities")) tags.Add("Custom Abilities");
                            }
                            if (metaRoot.TryGetProperty("CustomWeapons", out var cw) && cw.ValueKind == JsonValueKind.Array && cw.GetArrayLength() > 0)
                            {
                                if (!tags.Contains("Custom Weapons")) tags.Add("Custom Weapons");
                            }
                            if (metaRoot.TryGetProperty("CustomUpgrades", out var cup) && cup.ValueKind == JsonValueKind.Array && cup.GetArrayLength() > 0)
                            {
                                if (!tags.Contains("Custom Upgrades")) tags.Add("Custom Upgrades");
                            }
                            if (metaRoot.TryGetProperty("CustomProps", out var cpr) && cpr.ValueKind == JsonValueKind.Array && cpr.GetArrayLength() > 0)
                            {
                                if (!tags.Contains("Custom Props")) tags.Add("Custom Props");
                            }
                            if (metaRoot.TryGetProperty("CustomItems", out var ci) && ci.ValueKind == JsonValueKind.Array && ci.GetArrayLength() > 0)
                            {
                                if (!tags.Contains("Custom Items")) tags.Add("Custom Items");
                            }
                            if (metaRoot.TryGetProperty("CustomAttachments", out var cat) && cat.ValueKind == JsonValueKind.Array && cat.GetArrayLength() > 0)
                            {
                                if (!tags.Contains("Custom Attachments")) tags.Add("Custom Attachments");
                            }
                            if (metaRoot.TryGetProperty("CustomVfx", out var cvfx) && cvfx.ValueKind == JsonValueKind.Array && cvfx.GetArrayLength() > 0)
                            {
                                if (!tags.Contains("Custom VFX")) tags.Add("Custom VFX");
                            }
                            if (metaRoot.TryGetProperty("EngineVersion", out var ev) && !string.IsNullOrEmpty(ev.GetString()))
                            {
                                engineVersion = ev.GetString()!;
                            }
                            else if (metaRoot.TryGetProperty("GameBuildNumber", out var gb) && !string.IsNullOrEmpty(gb.GetString()))
                            {
                                engineVersion = $"Build {gb.GetString()}";
                            }
                        }
                        catch { }
                    }
                }

                float ratingStars = stats != null && stats.AverageRating > 0 ? (float)stats.AverageRating : 4.5f;
                int totalReviews = stats != null ? stats.ReviewsCount : 0;
                int verifiedReviews = stats != null ? stats.VerifiedGoodReviewsCount : 0;
                int playtimeMinutes = stats != null ? (int)stats.TotalPlaytimeMinutes : 0;
                int gamesPlayed = stats != null ? stats.TotalGamesPlayed : 0;

                var awards = new List<string>();
                if (isGreenlit) awards.Add("res://Assets/UI/victory_flag.png");
                if (ratingStars >= 4.5f) awards.Add("res://Assets/UI/gold_coin.png");
                if (verifiedReviews >= 5 || totalReviews >= 20) awards.Add("res://Assets/UI/battle_shield.png");
                if (gamesPlayed >= 10 || playtimeMinutes >= 30) awards.Add("res://Assets/UI/battle_axe.png");
                if (awards.Count == 0) awards.Add("res://Assets/UI/gold_coin.png");

                discoveryList.Add(new DiscoveryMapDto
                {
                    MapId = compositeKey,
                    Title = manifest.MapName,
                    Version = manifest.Version,
                    Creator = !string.IsNullOrEmpty(manifest.Author) ? manifest.Author : "Realm Builder",
                    Description = description,
                    Genre = genre,
                    ThumbnailHash = thumbnailHash,
                    Screenshots = screenshotHashes,
                    Features = tags,
                    Tags = tags,
                    RatingStars = ratingStars,
                    TotalReviews = totalReviews,
                    VerifiedGoodReviews = verifiedReviews,
                    AverageRating = ratingStars,
                    PlaytimeMinutes = playtimeMinutes,
                    GamesPlayed = gamesPlayed,
                    TotalSizeBytes = totalBytes,
                    FileSizeFormatted = FormatByteSize(totalBytes),
                    EngineVersion = engineVersion,
                    MaxPlayers = maxPlayers,
                    IsGreenlit = isGreenlit,
                    Awards = awards
                });
            }
            catch { }
        }
    }

    return Results.Ok(discoveryList);
});

app.MapGet("/api/admin/info", (DataStoreService db, ContentAddressableStorage cas) =>
{
    var adminsList = new List<object>();
    string? primaryAdminUsername = null;
    string? primaryAdminPublicKey = adminPublicKeys.FirstOrDefault();

    foreach (var key in adminPublicKeys)
    {
        string username = "Admin";
        var adminCreator = db.Get<JsonDocument>("creators", key);
        if (adminCreator != null && adminCreator.RootElement.TryGetProperty("username", out var uProp))
        {
            string? name = uProp.GetString();
            if (!string.IsNullOrEmpty(name)) username = name;
        }
        if (primaryAdminUsername == null && string.Equals(key, primaryAdminPublicKey, StringComparison.OrdinalIgnoreCase))
        {
            primaryAdminUsername = username;
        }
        adminsList.Add(new { PublicKey = key, Username = username });
    }

    long casTotalBytes = cas.GetTotalUsedBytes();
    string dataDirectory = db.DataDirectory;

    return Results.Ok(new
    {
        AdminUsername = primaryAdminUsername ?? (adminsList.Count > 0 ? "Admin" : "N/A"),
        AdminPublicKey = primaryAdminPublicKey ?? "N/A",
        AdminPublicKeys = adminPublicKeys.ToList(),
        Admins = adminsList,
        IsGraduatedAdmin = adminPublicKeys.Count > 0,
        DataDirectory = dataDirectory,
        CasTotalSizeBytes = casTotalBytes,
        CasTotalSizeFormatted = ContentAddressableStorage.FormatByteSize(casTotalBytes)
    });
});

app.MapPost("/api/admin/greenlight", (AdminGreenlightRequest req, DataStoreService db, ClusterEventService clusterEvents, PeerRegistry registeredPeers, IHttpClientFactory httpClientFactory) =>
{
    if (string.IsNullOrWhiteSpace(req.MapTitle))
    {
        return Results.BadRequest(new { Message = "MapTitle is required." });
    }

    if (adminPublicKeys.Count == 0)
    {
        return Results.Json(new { Message = "Server has no AdminPublicKeys configured in servers.json or appsettings.json." }, statusCode: StatusCodes.Status403Forbidden);
    }

    if (string.IsNullOrWhiteSpace(req.AdminPublicKey) || !adminPublicKeys.Contains(req.AdminPublicKey.Trim()))
    {
        return Results.Json(new { Message = "Unauthorized admin public key." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    string mapTitle = req.MapTitle.Trim();
    string payload = $"greenlight:{mapTitle.ToLowerInvariant()}";

    bool isSigValid = AuthorSignatureHelper.VerifySignature(req.AdminPublicKey, payload, req.Signature)
        || (!string.IsNullOrEmpty(req.MapVersion) && AuthorSignatureHelper.VerifySignature(req.AdminPublicKey, $"greenlight:{mapTitle.ToLowerInvariant()}:{req.MapVersion.ToLowerInvariant()}", req.Signature))
        || (!string.IsNullOrEmpty(req.MapVersion) && AuthorSignatureHelper.VerifySignature(req.AdminPublicKey, $"{mapTitle}:{req.MapVersion}", req.Signature))
        || AuthorSignatureHelper.VerifySignature(req.AdminPublicKey, mapTitle, req.Signature)
        || AdminBypassAuth.VerifyBypassToken(req.AdminPublicKey, mapTitle, req.MapVersion ?? "1.0", req.Signature);

    if (!isSigValid)
    {
        return Results.BadRequest(new { Message = "Invalid admin signature." });
    }

    var stats = MapStatsHelper.GetStats(db, mapTitle, req.MapVersion, null) ?? new MapStats();
    stats.AdminOverrideGreenlit = true;
    db.Upsert("map_stats", mapTitle, stats);
    db.Upsert("map_stats", mapTitle.ToLowerInvariant(), stats);

    if (!string.IsNullOrWhiteSpace(req.MapVersion))
    {
        string compositeKey = $"{mapTitle}_{req.MapVersion.Trim()}";
        db.Upsert("map_stats", compositeKey, stats);
        db.Upsert("map_stats", compositeKey.ToLowerInvariant(), stats);
    }

    var greenlightEvent = new ClusterEventDto
    {
        EventType = "admin_greenlight",
        PublicKey = req.AdminPublicKey.Trim(),
        Signature = req.Signature.Trim(),
        PayloadJson = JsonSerializer.Serialize(new AdminGreenlightEventPayload
        {
            MapTitle = mapTitle,
            MapVersion = req.MapVersion,
            AdminPublicKey = req.AdminPublicKey.Trim(),
            Signature = req.Signature.Trim()
        })
    };
    clusterEvents.MarkEventProcessed(greenlightEvent.EventId, db);
    clusterEvents.BroadcastEvent(greenlightEvent, registeredPeers, httpClientFactory);

    return Results.Ok(new
    {
        Status = "Greenlit",
        MapTitle = mapTitle,
        IsGreenlit = true,
        AdminOverride = true
    });
});

app.MapGet("/api/maps/greenlight_status/{mapId}", (string mapId, DataStoreService db) =>
{
    string title = mapId;
    string? ver = null;
    string? pubKey = null;

    if (mapId.Contains('_'))
    {
        var parts = mapId.Split('_');
        if (parts.Length >= 3)
        {
            title = parts[0];
            ver = parts[1];
            pubKey = parts[2];
        }
        else if (parts.Length == 2)
        {
            title = parts[0];
            ver = parts[1];
        }
    }

    var stats = MapStatsHelper.GetStats(db, title, ver, pubKey) ?? new MapStats();

    return Results.Ok(new
    {
        MapId = mapId,
        IsGreenlit = stats.IsGreenlit,
        AdminOverride = stats.AdminOverrideGreenlit,
        VerifiedGoodReviewsCount = stats.VerifiedGoodReviewsCount,
        RequiredVerifiedGoodReviewsCount = 100,
        TotalReviewsCount = stats.ReviewsCount,
        AverageRating = stats.AverageRating
    });
});

app.MapGet("/api/data/{collection}/{id}", (string collection, string id, DataStoreService db, HttpContext context) =>
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    var item = db.Get<JsonDocument>(collection, id);
    if (item != null)
    {
        return Results.Ok(item);
    }
    return Results.NotFound(new { Message = "Not found" });
});

app.MapGet("/api/data/{collection}", (string collection, DataStoreService db, HttpContext context) =>
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    var items = db.GetAll<JsonDocument>(collection);
    return Results.Ok(items);
});

app.MapPost("/api/data/{collection}", (string collection, JsonDocument data, DataStoreService db, HttpContext context) =>
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    var id = Guid.NewGuid().ToString("N");
    db.Upsert(collection, id, data);
    return Results.Ok(new { Id = id });
});

app.MapPut("/api/data/{collection}/{id}", (string collection, string id, JsonDocument data, DataStoreService db, HttpContext context) =>
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    db.Upsert(collection, id, data);
    return Results.Ok(new { Status = "Updated" });
});

app.MapDelete("/api/data/{collection}/{id}", (string collection, string id, DataStoreService db, HttpContext context) =>
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    db.Delete(collection, id);
    return Results.Ok(new { Status = "Deleted" });
});

app.MapGet("/api/players", (DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    return Results.Ok(db.GetAll<JsonDocument>("players"));
});
app.MapGet("/api/players/{id}", (string id, DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    var p = db.Get<JsonDocument>("players", id);
    return p != null ? Results.Ok(p) : Results.NotFound();
});
app.MapPost("/api/players", (JsonDocument data, DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    var id = Guid.NewGuid().ToString("N");
    db.Upsert("players", id, data);
    return Results.Ok(new { Id = id });
});
app.MapPut("/api/players/{id}", (string id, JsonDocument data, DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    db.Upsert("players", id, data);
    return Results.Ok();
});
app.MapDelete("/api/players/{id}", (string id, DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    db.Delete("players", id);
    return Results.Ok();
});

app.MapGet("/api/maps", (DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    return Results.Ok(db.GetAll<JsonDocument>("maps"));
});
app.MapGet("/api/maps/{id}", (string id, DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    var m = db.Get<JsonDocument>("maps", id);
    return m != null ? Results.Ok(m) : Results.NotFound();
});
app.MapPost("/api/maps", (JsonDocument data, DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    var id = Guid.NewGuid().ToString("N");
    db.Upsert("maps", id, data);
    return Results.Ok(new { Id = id });
});
app.MapPut("/api/maps/{id}", (string id, JsonDocument data, DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    db.Upsert("maps", id, data);
    return Results.Ok();
});
app.MapDelete("/api/maps/{id}", (string id, DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    db.Delete("maps", id);
    return Results.Ok();
});

app.MapGet("/api/admin/bans", (DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    return Results.Ok(db.GetAll<JsonDocument>("bans"));
});
app.MapGet("/api/admin/bans/{id}", (string id, DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    var b = db.Get<JsonDocument>("bans", id);
    return b != null ? Results.Ok(b) : Results.NotFound();
});
app.MapPost("/api/admin/bans", (JsonDocument data, DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    var id = Guid.NewGuid().ToString("N");
    db.Upsert("bans", id, data);
    return Results.Ok(new { Id = id });
});
app.MapPut("/api/admin/bans/{id}", (string id, JsonDocument data, DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    db.Upsert("bans", id, data);
    return Results.Ok();
});
app.MapDelete("/api/admin/bans/{id}", (string id, DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    db.Delete("bans", id);
    return Results.Ok();
});


app.Map("/seeders/ws", async (HttpContext context, SeederRegistry registry) =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var seederId = context.Request.Query["seederId"].ToString();
        if (string.IsNullOrEmpty(seederId))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        registry.AddConnection(seederId, webSocket);
        Console.WriteLine($"[Registry] Seeder connected WebSocket for Seeder {seederId}");

        var buffer = new byte[1024 * 4];
        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Registry] Seeder {seederId} WebSocket error: {ex.Message}");
        }
        finally
        {
            registry.RemoveConnection(seederId);
            Console.WriteLine($"[Registry] Seeder disconnected WebSocket for Seeder {seederId}");
        }
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
    }
});

app.MapPost("/api/maps/report_metrics", (MapMetricsReport req, DataStoreService db, ClusterEventService clusterEvents, PeerRegistry registeredPeers, IHttpClientFactory httpClientFactory) =>
{
    if (string.IsNullOrWhiteSpace(req.MapTitle))
    {
        return Results.BadRequest(new { Message = "MapTitle is required." });
    }

    string mapTitle = req.MapTitle.Trim();
    string mapVersion = string.IsNullOrWhiteSpace(req.MapVersion) ? "1.0" : req.MapVersion.Trim();
    string authorPublicKey = req.AuthorPublicKey?.Trim() ?? "";
    string compositeKey = $"{mapTitle}_{mapVersion}";
    string authorCompositeKey = !string.IsNullOrEmpty(authorPublicKey) ? $"{mapTitle}_{mapVersion}_{authorPublicKey}" : compositeKey;

    string playerIdentifier = !string.IsNullOrWhiteSpace(req.PlayerId) ? req.PlayerId.Trim() : "Anonymous";
    bool isVerifiedAccount = false;

    if (!string.IsNullOrWhiteSpace(req.AuthToken))
    {
        var sessionDoc = db.Get<JsonDocument>("auth_sessions", req.AuthToken);
        if (sessionDoc != null)
        {
            var root = sessionDoc.RootElement;
            string? provider = root.TryGetProperty("provider", out var pProp) ? pProp.GetString() : null;
            string? user = root.TryGetProperty("username", out var uProp) ? uProp.GetString() : null;
            if (!string.IsNullOrEmpty(user))
            {
                playerIdentifier = user;
            }
            if (!string.IsNullOrEmpty(provider) && !provider.Equals("guest", StringComparison.OrdinalIgnoreCase))
            {
                isVerifiedAccount = true;
            }
        }
    }
    else if (!string.IsNullOrWhiteSpace(req.AuthProvider) && !req.AuthProvider.Equals("guest", StringComparison.OrdinalIgnoreCase))
    {
        isVerifiedAccount = true;
    }

    string engagementKey = !string.IsNullOrEmpty(authorPublicKey)
        ? $"{playerIdentifier}_{authorCompositeKey}".ToLowerInvariant()
        : $"{playerIdentifier}_{compositeKey}".ToLowerInvariant();
    var engagement = db.Get<PlayerMapEngagement>("player_engagement", engagementKey)
        ?? new PlayerMapEngagement { PlayerId = playerIdentifier };

    bool wasVerifiedGood = engagement.IsVerifiedGoodReview;
    bool hadSubmittedRating = engagement.SubmittedRating.HasValue;
    int prevRating = engagement.SubmittedRating ?? 0;

    engagement.TotalPlaytimeMinutes += Math.Max(0.0, req.PlaytimeMinutes);
    if (req.IsCompleteGame)
    {
        engagement.GamesPlayed += 1;
    }
    if (isVerifiedAccount)
    {
        engagement.IsVerifiedAccount = true;
    }

    var stats = MapStatsHelper.GetStats(db, mapTitle, mapVersion, authorPublicKey) ?? new MapStats();

    stats.TotalPlaytimeMinutes += Math.Max(0.0, req.PlaytimeMinutes);
    if (req.IsCompleteGame)
    {
        stats.TotalGamesPlayed += 1;
    }

    if (req.Stars >= 1 && req.Stars <= 5)
    {
        if (hadSubmittedRating)
        {
            stats.TotalStars += (req.Stars - prevRating);
        }
        else
        {
            stats.ReviewsCount += 1;
            stats.TotalStars += req.Stars;
        }
        engagement.SubmittedRating = req.Stars;
    }

    bool isNowVerifiedGood = engagement.IsVerifiedGoodReview;
    if (!wasVerifiedGood && isNowVerifiedGood)
    {
        stats.VerifiedGoodReviewsCount += 1;
    }
    else if (wasVerifiedGood && !isNowVerifiedGood)
    {
        stats.VerifiedGoodReviewsCount = Math.Max(0, stats.VerifiedGoodReviewsCount - 1);
    }

    db.Upsert("player_engagement", engagementKey, engagement);
    db.Upsert("map_stats", authorCompositeKey, stats);
    db.Upsert("map_stats", authorCompositeKey.ToLowerInvariant(), stats);
    if (!string.IsNullOrEmpty(authorPublicKey))
    {
        db.Upsert("map_stats", $"{mapTitle}_{authorPublicKey}", stats);
        db.Upsert("map_stats", $"{mapTitle}_{authorPublicKey}".ToLowerInvariant(), stats);
    }
    if (stats.IsGreenlit)
    {
        db.Upsert("map_stats", compositeKey, stats);
        db.Upsert("map_stats", mapTitle, stats);
        db.Upsert("map_stats", compositeKey.ToLowerInvariant(), stats);
        db.Upsert("map_stats", mapTitle.ToLowerInvariant(), stats);
    }

    var metricEvent = new ClusterEventDto
    {
        EventType = "map_metric_report",
        PayloadJson = JsonSerializer.Serialize(new MapMetricReportEventPayload
        {
            MapTitle = mapTitle,
            MapVersion = mapVersion,
            PlayerId = playerIdentifier,
            PlaytimeMinutes = Math.Max(0.0, req.PlaytimeMinutes),
            Stars = req.Stars,
            IsCompleteGame = req.IsCompleteGame,
            AuthProvider = req.AuthProvider,
            AuthorPublicKey = authorPublicKey
        })
    };
    clusterEvents.MarkEventProcessed(metricEvent.EventId, db);
    clusterEvents.BroadcastEvent(metricEvent, registeredPeers, httpClientFactory);

    return Results.Ok(new
    {
        Success = true,
        MapId = compositeKey,
        IsVerifiedAccount = engagement.IsVerifiedAccount,
        IsEligibleReviewer = engagement.IsEligibleReviewer,
        IsVerifiedGoodReview = engagement.IsVerifiedGoodReview,
        PlayerTotalPlaytime = engagement.TotalPlaytimeMinutes,
        PlayerGamesPlayed = engagement.GamesPlayed,
        MapStats = stats,
        IsGreenlit = stats.IsGreenlit
    });
});

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

app.Run();


public class MapMetricsReport
{
    public string MapTitle { get; set; } = "";
    public string MapVersion { get; set; } = "";
    public double PlaytimeMinutes { get; set; }
    public int Stars { get; set; }
    public bool IsCompleteGame { get; set; }
    public string? PlayerId { get; set; }
    public string? AuthToken { get; set; }
    public string? AuthProvider { get; set; }
    public string? AuthorPublicKey { get; set; }
}

public class PlayerMapEngagement
{
    public string PlayerId { get; set; } = "";
    public double TotalPlaytimeMinutes { get; set; }
    public int GamesPlayed { get; set; }
    public int? SubmittedRating { get; set; }
    public bool IsVerifiedAccount { get; set; }
    public bool IsEligibleReviewer => IsVerifiedAccount && TotalPlaytimeMinutes >= 30.0 && GamesPlayed >= 3;
    public bool IsVerifiedGoodReview => IsEligibleReviewer && SubmittedRating.HasValue && SubmittedRating.Value >= 3;
}

public class MapStats
{
    public double TotalPlaytimeMinutes { get; set; }
    public int TotalGamesPlayed { get; set; }
    public int ReviewsCount { get; set; }
    public int TotalStars { get; set; }
    public int VerifiedGoodReviewsCount { get; set; }
    public bool AdminOverrideGreenlit { get; set; }

    public double AverageRating => ReviewsCount > 0 ? (double)TotalStars / ReviewsCount : 0.0;

    public bool IsGreenlit => AdminOverrideGreenlit || VerifiedGoodReviewsCount >= 100;
}

public class AdminGreenlightRequest
{
    public string MapTitle { get; set; } = "";
    public string? MapVersion { get; set; }
    public string AdminPublicKey { get; set; } = "";
    public string Signature { get; set; } = "";
}

public class RegisterCreatorRequest
{
    public string Username { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string Signature { get; set; } = "";
    public string? DonationLink { get; set; }
    public string? ContactInfo { get; set; }
}
