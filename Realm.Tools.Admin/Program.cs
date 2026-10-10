using CommandLine;
using NSec.Cryptography;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Realm.Tools.Admin;

public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string[] originalArgs = args;
        string[] sanitizedArgs = CommandLineArgsHelper.SanitizeArgs(
            args,
            typeof(AdminGreenlightOptions),
            typeof(AdminStatusOptions),
            typeof(AdminUnlockNameOptions),
            typeof(AdminInfoOptions),
            typeof(AdminExportEventsOptions),
            typeof(AdminPruneCasOptions),
            typeof(AdminDigestOptions),
            typeof(AdminSnapshotOptions),
            typeof(AdminRestoreSnapshotOptions),
            typeof(AdminRemoveManifestOptions),
            typeof(AdminAddMaintainerOptions),
            typeof(AdminRemoveMaintainerOptions),
            typeof(AdminListMaintainersOptions));

        return Parser.Default.ParseArguments<AdminGreenlightOptions, AdminStatusOptions, AdminUnlockNameOptions, AdminInfoOptions, AdminExportEventsOptions, AdminPruneCasOptions, AdminDigestOptions, AdminSnapshotOptions, AdminRestoreSnapshotOptions, AdminRemoveManifestOptions, AdminAddMaintainerOptions, AdminRemoveMaintainerOptions, AdminListMaintainersOptions>(sanitizedArgs)
            .WithParsed(options => CommandLineArgsHelper.ApplyBooleanOverrides(options, originalArgs))
            .MapResult(
                (AdminGreenlightOptions options) => ExecuteGreenlight(options),
                (AdminStatusOptions options) => ExecuteStatus(options),
                (AdminUnlockNameOptions options) => ExecuteUnlockName(options),
                (AdminInfoOptions options) => ExecuteInfo(options),
                (AdminExportEventsOptions options) => ExecuteExportEvents(options),
                (AdminPruneCasOptions options) => ExecutePruneCas(options),
                (AdminDigestOptions options) => ExecuteDigest(options),
                (AdminSnapshotOptions options) => ExecuteSnapshot(options),
                (AdminRestoreSnapshotOptions options) => ExecuteRestoreSnapshot(options),
                (AdminRemoveManifestOptions options) => ExecuteRemoveManifest(options),
                (AdminAddMaintainerOptions options) => ExecuteAddMaintainer(options),
                (AdminRemoveMaintainerOptions options) => ExecuteRemoveMaintainer(options),
                (AdminListMaintainersOptions options) => ExecuteListMaintainers(options),
                errors => 1);
    }

    private static string ResolveServerUrl(string? explicitServer)
    {
        if (!string.IsNullOrWhiteSpace(explicitServer))
        {
            return explicitServer.Trim();
        }

        return ServersConfigHelper.GetDefaultServerUrl();
    }

    private static (string privateKeyBase64, string publicKeyBase64)? ParseAdminKey(string? keyInput)
    {
        string keyPath = string.IsNullOrWhiteSpace(keyInput) ? AuthorshipKeyHelper.GetDefaultKeyPath() : keyInput.Trim();

        if (!File.Exists(keyPath))
        {
            Console.Error.WriteLine(string.IsNullOrWhiteSpace(keyInput)
                ? $"Error: Default key file '{keyPath}' does not exist. Please generate a key or supply a key file with --key <path>."
                : $"Error: Key file '{keyPath}' not found. Direct key strings are not allowed; please provide a path to a .rkey file.");
            return null;
        }

        try
        {
            return TryParseAdminKeyInternal(keyPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error reading key file '{keyPath}': {ex.Message}");
            return null;
        }
    }

    private static (string privateKeyBase64, string publicKeyBase64)? TryParseAdminKeyInternal(string keyPath)
    {
        byte[] fileBytes = File.ReadAllBytes(keyPath);

        if (RkeyFile.IsRkeyBytes(fileBytes))
        {
            return ParseRkeyData(fileBytes, keyPath);
        }

        if (fileBytes.Length == 32)
        {
            return ParseRawBytesKey(fileBytes);
        }

        string textContent = File.ReadAllText(keyPath).Trim();
        if (textContent.StartsWith("{"))
        {
            var jsonResult = ParseJsonAdminKey(textContent);
            if (jsonResult != null) return jsonResult;
        }

        return ParsePemAdminKey(textContent);
    }

    private static (string privateKeyBase64, string publicKeyBase64)? ParseRkeyData(byte[] fileBytes, string keyPath)
    {
        var keyData = RkeyFile.ParseKeyData(fileBytes);
        if (keyData == null || string.IsNullOrWhiteSpace(keyData.PrivateKey))
        {
            Console.Error.WriteLine($"Error: No private key found in key file '{keyPath}'.");
            return null;
        }

        string privBase64 = keyData.PrivateKey;
        string pubBase64 = !string.IsNullOrWhiteSpace(keyData.PublicKey)
            ? keyData.PublicKey
            : AuthorSignatureHelper.GetPublicKey(privBase64);

        return (privBase64, pubBase64);
    }

    private static (string privateKeyBase64, string publicKeyBase64) ParseRawBytesKey(byte[] fileBytes)
    {
        string privBase64 = Convert.ToBase64String(fileBytes);
        string pubBase64 = AuthorSignatureHelper.GetPublicKey(privBase64);
        return (privBase64, pubBase64);
    }

    private static (string privateKeyBase64, string publicKeyBase64)? ParseJsonAdminKey(string textContent)
    {
        var keyData = JsonSerializer.Deserialize<AuthorshipKeyData>(textContent);
        if (keyData == null || string.IsNullOrWhiteSpace(keyData.PrivateKey)) return null;

        string privBase64 = keyData.PrivateKey;
        string pubBase64 = !string.IsNullOrWhiteSpace(keyData.PublicKey)
            ? keyData.PublicKey
            : AuthorSignatureHelper.GetPublicKey(privBase64);
        return (privBase64, pubBase64);
    }

    private static (string privateKeyBase64, string publicKeyBase64) ParsePemAdminKey(string textContent)
    {
        textContent = textContent.Replace("-----BEGIN PRIVATE KEY-----", "")
            .Replace("-----END PRIVATE KEY-----", "")
            .Replace("\r", "")
            .Replace("\n", "")
            .Trim();

        byte[] privateBytes = Convert.FromBase64String(textContent);
        using var key = Key.Import(SignatureAlgorithm.Ed25519, privateBytes, KeyBlobFormat.RawPrivateKey, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        byte[] publicBytes = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);

        return (Convert.ToBase64String(privateBytes), Convert.ToBase64String(publicBytes));
    }

    private static string? ParsePublicKeyFromFile(string targetKeyInput)
    {
        if (string.IsNullOrWhiteSpace(targetKeyInput))
        {
            Console.Error.WriteLine("Error: --target-key is required.");
            return null;
        }

        string targetPath = targetKeyInput.Trim();
        if (!File.Exists(targetPath))
        {
            Console.Error.WriteLine($"Error: Target key file '{targetPath}' not found. Direct key strings are not allowed; please provide a path to a .rkey file.");
            return null;
        }

        try
        {
            return TryParsePublicKeyFromFileInternal(targetPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error reading target key file '{targetPath}': {ex.Message}");
            return null;
        }
    }

    private static string? TryParsePublicKeyFromFileInternal(string targetPath)
    {
        byte[] fileBytes = File.ReadAllBytes(targetPath);

        if (RkeyFile.IsRkeyBytes(fileBytes))
        {
            return ParseRkeyPublicKey(fileBytes);
        }

        if (fileBytes.Length == 32)
        {
            return AuthorSignatureHelper.GetPublicKey(Convert.ToBase64String(fileBytes));
        }

        string textContent = File.ReadAllText(targetPath).Trim();
        if (textContent.StartsWith("{"))
        {
            var jsonResult = ParseJsonPublicKey(textContent);
            if (jsonResult != null) return jsonResult;
        }

        return ParsePemPublicKey(textContent, targetPath);
    }

    private static string? ParseRkeyPublicKey(byte[] fileBytes)
    {
        var keyData = RkeyFile.ParseKeyData(fileBytes);
        if (keyData == null) return null;

        if (!string.IsNullOrWhiteSpace(keyData.PublicKey)) return keyData.PublicKey;
        if (!string.IsNullOrWhiteSpace(keyData.PrivateKey)) return AuthorSignatureHelper.GetPublicKey(keyData.PrivateKey);

        return null;
    }

    private static string? ParseJsonPublicKey(string textContent)
    {
        var keyData = JsonSerializer.Deserialize<AuthorshipKeyData>(textContent);
        if (keyData == null) return null;

        if (!string.IsNullOrWhiteSpace(keyData.PublicKey)) return keyData.PublicKey;
        if (!string.IsNullOrWhiteSpace(keyData.PrivateKey)) return AuthorSignatureHelper.GetPublicKey(keyData.PrivateKey);

        return null;
    }

    private static string? ParsePemPublicKey(string textContent, string targetPath)
    {
        textContent = textContent.Replace("-----BEGIN PUBLIC KEY-----", "")
            .Replace("-----END PUBLIC KEY-----", "")
            .Replace("-----BEGIN PRIVATE KEY-----", "")
            .Replace("-----END PRIVATE KEY-----", "")
            .Replace("\r", "")
            .Replace("\n", "")
            .Trim();

        byte[] rawBytes = Convert.FromBase64String(textContent);
        if (rawBytes.Length == 32) return textContent;

        Console.Error.WriteLine($"Error: Unable to extract public key from '{targetPath}'.");
        return null;
    }

    private static int CompareVersions(string? versionA, string? versionB)
    {
        if (string.IsNullOrWhiteSpace(versionA) && string.IsNullOrWhiteSpace(versionB)) return 0;
        if (string.IsNullOrWhiteSpace(versionA)) return -1;
        if (string.IsNullOrWhiteSpace(versionB)) return 1;

        string cleanA = versionA.Trim().TrimStart('v', 'V');
        string cleanB = versionB.Trim().TrimStart('v', 'V');

        if (Version.TryParse(cleanA, out var parsedA) && Version.TryParse(cleanB, out var parsedB))
        {
            return parsedA.CompareTo(parsedB);
        }

        return CompareVersionParts(cleanA, cleanB, versionA, versionB);
    }

    private static int CompareVersionParts(string cleanA, string cleanB, string originalA, string originalB)
    {
        var partsA = cleanA.Split('.');
        var partsB = cleanB.Split('.');
        int maxLength = Math.Max(partsA.Length, partsB.Length);

        for (int index = 0; index < maxLength; index++)
        {
            int numA = ParseVersionPart(partsA, index);
            int numB = ParseVersionPart(partsB, index);
            if (numA != numB) return numA.CompareTo(numB);
        }

        return string.Compare(originalA, originalB, StringComparison.OrdinalIgnoreCase);
    }

    private static int ParseVersionPart(string[] parts, int index)
    {
        if (index >= parts.Length) return 0;
        return int.TryParse(parts[index], out int parsedNum) ? parsedNum : 0;
    }

    private static int ExecuteGreenlight(AdminGreenlightOptions options)
    {
        string serverUrl = ResolveServerUrl(options.Server);
        var keyPair = ParseAdminKey(options.Key);
        if (keyPair == null) return 1;

        string mapTitle = options.Map.Trim();
        string payload = $"greenlight:{mapTitle.ToLowerInvariant()}";
        string signature = AuthorSignatureHelper.SignMessage(keyPair.Value.privateKeyBase64, payload);

        Console.WriteLine("=================================================");
        Console.WriteLine("Realm Admin Tool - Map Greenlight Override");
        Console.WriteLine("=================================================");
        Console.WriteLine($"Server:     {serverUrl}");
        Console.WriteLine($"Map Title:  {mapTitle}");
        Console.WriteLine($"Admin Key:  {keyPair.Value.publicKeyBase64}");
        Console.WriteLine();

        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var requestBody = new
            {
                MapTitle = mapTitle,
                AdminPublicKey = keyPair.Value.publicKeyBase64,
                Signature = signature
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = httpClient.PostAsync($"{serverUrl.TrimEnd('/')}/api/admin/greenlight", content).GetAwaiter().GetResult();
            string responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[SUCCESS] Map '{mapTitle}' has been greenlit!");
                Console.WriteLine("The map creator may now publish new versions of this map directly through the Map Editor.");
                return 0;
            }
            else
            {
                Console.Error.WriteLine($"[FAILED] HTTP {(int)response.StatusCode}: {responseText}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Connection failed: {ex.Message}");
            return 1;
        }
    }

    private static int ExecuteStatus(AdminStatusOptions options)
    {
        string serverUrl = ResolveServerUrl(options.Server);
        string mapTitle = options.Map.Trim();
        string? mapVersion = ResolveMapVersion(options.Version, serverUrl, mapTitle);

        string endpoint = string.IsNullOrWhiteSpace(mapVersion)
            ? $"{serverUrl.TrimEnd('/')}/api/maps/greenlight_status/{Uri.EscapeDataString(mapTitle)}"
            : $"{serverUrl.TrimEnd('/')}/api/maps/greenlight_status/{Uri.EscapeDataString(mapTitle)}_{Uri.EscapeDataString(mapVersion)}";

        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = httpClient.GetAsync(endpoint).GetAwaiter().GetResult();
            string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"[FAILED] HTTP {(int)response.StatusCode}: {json}");
                return 1;
            }

            return PrintGreenlightStatus(json, mapTitle, mapVersion);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Connection failed: {ex.Message}");
            return 1;
        }
    }

    private static string? ResolveMapVersion(string? optionsVersion, string serverUrl, string mapTitle)
    {
        if (!string.IsNullOrWhiteSpace(optionsVersion)) return optionsVersion.Trim();

        try
        {
            var client = new DistributionClient(serverUrl);
            var discoveryMaps = client.GetDiscoveryMapsAsync().GetAwaiter().GetResult();

            var matchingMap = discoveryMaps
                .Where(m => IsMatchingMap(m, mapTitle))
                .OrderByDescending(m => m.Version, Comparer<string>.Create(CompareVersions))
                .FirstOrDefault();

            if (matchingMap != null && !string.IsNullOrWhiteSpace(matchingMap.Version))
            {
                return matchingMap.Version;
            }
        }
        catch
        {
            // Ignore resolution errors
        }
        return null;
    }

    private static bool IsMatchingMap(DiscoveryMapDto m, string mapTitle)
    {
        return string.Equals(m.Title, mapTitle, StringComparison.OrdinalIgnoreCase) ||
               m.MapId.StartsWith(mapTitle + "_", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(m.MapId, mapTitle, StringComparison.OrdinalIgnoreCase);
    }

    private static int PrintGreenlightStatus(string json, string mapTitle, string? mapVersion)
    {
        var node = JsonNode.Parse(json);
        if (node == null)
        {
            Console.Error.WriteLine("Error parsing status response.");
            return 1;
        }

        bool isGreenlit = GetBool(node, "isGreenlit");
        bool adminOverride = GetBool(node, "adminOverride");
        int verifiedGoodReviews = GetInt(node, "verifiedGoodReviewsCount");
        int totalReviews = GetInt(node, "totalReviewsCount");
        double avgRating = GetDouble(node, "averageRating");

        string titleHeader = !string.IsNullOrWhiteSpace(mapVersion)
            ? $"Greenlight Status for '{mapTitle}' v{mapVersion}"
            : $"Greenlight Status for '{mapTitle}'";

        Console.WriteLine("=================================================");
        Console.WriteLine(titleHeader);
        Console.WriteLine("=================================================");
        Console.WriteLine($"Overall Greenlit:             {(isGreenlit ? "YES (Approved for Discovery)" : "NO (In Beta-Testing)")}");
        Console.WriteLine($"Admin Override:               {(adminOverride ? "ACTIVE" : "None")}");
        Console.WriteLine();
        Console.WriteLine("Greenlight Criteria:");
        Console.WriteLine($"  - Total Verified Good Reviews: {verifiedGoodReviews} / 100 (Required: >= 100)");
        Console.WriteLine();
        Console.WriteLine("Public Discovery Statistics:");
        Console.WriteLine($"  - Public Total Reviews:        {totalReviews}");
        Console.WriteLine($"  - Public Average Rating:       {avgRating:F1} / 5.0");
        Console.WriteLine("=================================================");
        return 0;
    }

    private static bool GetBool(JsonNode node, string key)
    {
        return node[key]?.GetValue<bool>() ?? false;
    }

    private static int GetInt(JsonNode node, string key)
    {
        return node[key]?.GetValue<int>() ?? 0;
    }

    private static double GetDouble(JsonNode node, string key)
    {
        return node[key]?.GetValue<double>() ?? 0.0;
    }

    private static int ExecuteUnlockName(AdminUnlockNameOptions options)
    {
        string serverUrl = ResolveServerUrl(options.Server);
        var keyPair = ParseAdminKey(options.Key);
        if (keyPair == null) return 1;

        string username = options.Username.Trim();
        string? targetKey = ParsePublicKeyFromFile(options.TargetKey);
        if (string.IsNullOrWhiteSpace(targetKey)) return 1;

        string signature = AuthorSignatureHelper.SignMessage(keyPair.Value.privateKeyBase64, $"{username}:{targetKey}");

        Console.WriteLine("=================================================");
        Console.WriteLine("Realm Admin Tool - Unlock / Reassign Creator Name");
        Console.WriteLine("=================================================");
        Console.WriteLine($"Server:     {serverUrl}");
        Console.WriteLine($"Username:   {username}");
        Console.WriteLine($"Target Key: {targetKey}");
        Console.WriteLine();

        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            string bypassToken = AdminBypassAuth.CreateBypassToken(keyPair.Value.privateKeyBase64, "admin", "override");
            httpClient.DefaultRequestHeaders.Add("X-Admin-Bypass", bypassToken);

            var requestBody = new
            {
                Username = username,
                PublicKey = targetKey,
                Signature = signature
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = httpClient.PostAsync($"{serverUrl.TrimEnd('/')}/api/creators/register", content).GetAwaiter().GetResult();
            string responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[SUCCESS] Username '{username}' has been successfully reassigned to public key '{targetKey}'.");
                return 0;
            }
            else
            {
                Console.Error.WriteLine($"[FAILED] HTTP {(int)response.StatusCode}: {responseText}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Connection failed: {ex.Message}");
            return 1;
        }
    }

    private static int ExecuteInfo(AdminInfoOptions options)
    {
        string serverUrl = ResolveServerUrl(options.Server);
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = httpClient.GetAsync($"{serverUrl.TrimEnd('/')}/api/admin/info").GetAwaiter().GetResult();
            string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"[FAILED] HTTP {(int)response.StatusCode}: {json}");
                return 1;
            }

            PrintAdminInfo(json);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Connection failed: {ex.Message}");
            return 1;
        }
    }

    private static void PrintAdminInfo(string json)
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("Realm Registry Server Admin Info");
        Console.WriteLine("=================================================");
        var node = JsonNode.Parse(json);

        string adminUsername = ExtractAdminUsername(node);
        string adminPublicKey = ExtractAdminPublicKey(node);

        Console.WriteLine($"Admin Username:      {adminUsername}");
        Console.WriteLine($"Admin Public Key:    {adminPublicKey}");
        Console.WriteLine($"Is Graduated Admin:  {node?["isGraduatedAdmin"]?.ToString() ?? "N/A"}");
        Console.WriteLine($"Data Directory:      {node?["dataDirectory"]?.ToString() ?? "N/A"}");

        Console.WriteLine($"CAS Used Disk Space: {ExtractCasDiskSpaceText(node)}");
        Console.WriteLine("=================================================");
    }

    private static string ExtractAdminUsername(JsonNode? node)
    {
        if (node?["adminUsername"] != null) return node["adminUsername"]!.ToString();

        if (node?["admins"] is JsonArray adminArr && adminArr.Count > 0)
        {
            if (adminArr[0]?["username"] != null) return adminArr[0]!["username"]!.ToString();
            if (adminArr[0]?["Username"] != null) return adminArr[0]!["Username"]!.ToString();
        }

        return "N/A";
    }

    private static string ExtractAdminPublicKey(JsonNode? node)
    {
        if (node is not JsonObject jsonObj) return "N/A";

        if (jsonObj.TryGetPropertyValue("adminPublicKey", out JsonNode? adminPublicKey) && adminPublicKey != null)
        {
            return adminPublicKey.ToString();
        }

        if (TryExtractFromAdminPublicKeys(jsonObj, out string key))
        {
            return key;
        }

        if (TryExtractFromAdminsArray(jsonObj, out key))
        {
            return key;
        }

        return "N/A";
    }

    private static bool TryExtractFromAdminPublicKeys(JsonObject node, out string key)
    {
        key = string.Empty;
        if (!node.TryGetPropertyValue("adminPublicKeys", out JsonNode? arrNode)) return false;
        if (arrNode is not JsonArray keyArr) return false;
        if (keyArr.Count == 0) return false;

        JsonNode? firstKey = keyArr[0];
        if (firstKey == null) return false;

        key = firstKey.ToString();
        return true;
    }

    private static bool TryExtractFromAdminsArray(JsonObject node, out string key)
    {
        key = string.Empty;
        if (!node.TryGetPropertyValue("admins", out JsonNode? arrNode)) return false;
        if (arrNode is not JsonArray adminArr) return false;
        if (adminArr.Count == 0) return false;

        if (adminArr[0] is not JsonObject firstAdminObj) return false;

        if (firstAdminObj.TryGetPropertyValue("publicKey", out JsonNode? pubKey) && pubKey != null)
        {
            key = pubKey.ToString();
            return true;
        }

        if (firstAdminObj.TryGetPropertyValue("PublicKey", out JsonNode? pubKey2) && pubKey2 != null)
        {
            key = pubKey2.ToString();
            return true;
        }

        return false;
    }

    private static string ExtractCasDiskSpaceText(JsonNode? node)
    {
        string? casFormatted = node?["casTotalSizeFormatted"]?.ToString();

        long? casBytes = null;
        if (node?["casTotalSizeBytes"] != null && long.TryParse(node["casTotalSizeBytes"]!.ToString(), out long parsedBytes))
        {
            casBytes = parsedBytes;
        }

        if (!string.IsNullOrWhiteSpace(casFormatted) && casBytes.HasValue) return $"{casFormatted} ({casBytes.Value:N0} bytes)";
        if (!string.IsNullOrWhiteSpace(casFormatted)) return casFormatted;
        if (casBytes.HasValue) return $"{ContentAddressableStorage.FormatByteSize(casBytes.Value)} ({casBytes.Value:N0} bytes)";
        return "N/A";
    }

    private static int ExecuteExportEvents(AdminExportEventsOptions options)
    {
        string serverUrl = ResolveServerUrl(options.Server);
        DateTime? sinceUtc = null;
        if (!string.IsNullOrWhiteSpace(options.Since) && DateTime.TryParse(options.Since, out var parsed))
        {
            sinceUtc = parsed.ToUniversalTime();
        }

        Console.WriteLine("=================================================");
        Console.WriteLine("Realm Admin Tool - Export Cluster Events");
        Console.WriteLine("=================================================");
        Console.WriteLine($"Server:    {serverUrl}");
        Console.WriteLine($"Since:     {(sinceUtc.HasValue ? sinceUtc.Value.ToString("o") : "All History")}");
        Console.WriteLine($"Limit:     {options.Limit}");
        Console.WriteLine($"Output:    {options.OutputFile}");
        Console.WriteLine();

        try
        {
            var client = new DistributionClient(serverUrl);
            var events = client.GetClusterEventsAsync(sinceUtc, options.Limit).GetAwaiter().GetResult();

            string json = JsonSerializer.Serialize(events, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(options.OutputFile, json);

            Console.WriteLine($"[SUCCESS] Exported {events.Count} cluster events to '{options.OutputFile}'.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Failed to export events: {ex.Message}");
            return 1;
        }
    }

    private static int ExecutePruneCas(AdminPruneCasOptions options)
    {
        string serverUrl = ResolveServerUrl(options.Server);
        var keyPair = ParseAdminKey(options.Key);
        if (keyPair == null) return 1;

        Console.WriteLine("=================================================");
        Console.WriteLine("Realm Admin Tool - Server-Side CAS Prune");
        Console.WriteLine("=================================================");
        Console.WriteLine($"Server:    {serverUrl}");
        Console.WriteLine($"Admin Key: {keyPair.Value.publicKeyBase64}");
        Console.WriteLine();

        try
        {
            var client = new DistributionClient(serverUrl);
            var result = client.PruneServerCasAsync(keyPair.Value.privateKeyBase64).GetAwaiter().GetResult();

            if (result.Success)
            {
                Console.WriteLine($"[SUCCESS] {result.Message}");
                if (result.TotalScanned > 0 || result.OrphansPruned > 0 || result.BytesFreed > 0)
                {
                    Console.WriteLine($"Total Scanned:   {result.TotalScanned}");
                    Console.WriteLine($"Orphans Pruned:  {result.OrphansPruned}");
                    Console.WriteLine($"Corrupt Pruned:  {result.CorruptPruned}");
                    Console.WriteLine($"Bytes Freed:     {result.BytesFreed} bytes");
                }
                return 0;
            }
            else
            {
                Console.Error.WriteLine($"[FAILED] {result.Message}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] CAS prune failed: {ex.Message}");
            return 1;
        }
    }

    private static int ExecuteDigest(AdminDigestOptions options)
    {
        string serverUrl = ResolveServerUrl(options.Server);
        Console.WriteLine("=================================================");
        Console.WriteLine("Realm Admin Tool - Database State Digest");
        Console.WriteLine("=================================================");
        Console.WriteLine($"Server: {serverUrl}");
        Console.WriteLine();

        try
        {
            var client = new DistributionClient(serverUrl);
            var digest = client.GetStateDigestAsync().GetAwaiter().GetResult();

            if (digest != null)
            {
                Console.WriteLine($"Root State Digest: {digest.StateDigest}");
                Console.WriteLine($"Server Timestamp:  {digest.ServerTimestampUtc:o}");
                Console.WriteLine();
                Console.WriteLine("Collection Hashes & Counts:");
                foreach (var col in digest.CollectionHashes.Keys.OrderBy(k => k))
                {
                    int count = digest.CollectionCounts.TryGetValue(col, out var c) ? c : 0;
                    Console.WriteLine($"  {col,-20} [{count,4} items] Hash: {digest.CollectionHashes[col]}");
                }
                Console.WriteLine("=================================================");
                return 0;
            }
            else
            {
                Console.Error.WriteLine("[FAILED] Could not retrieve state digest from server.");
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Connection failed: {ex.Message}");
            return 1;
        }
    }

    private static int ExecuteSnapshot(AdminSnapshotOptions options)
    {
        string serverUrl = ResolveServerUrl(options.Server);
        Console.WriteLine("=================================================");
        Console.WriteLine("Realm Admin Tool - Export Database Snapshot");
        Console.WriteLine("=================================================");
        Console.WriteLine($"Server: {serverUrl}");
        Console.WriteLine($"Output: {options.OutputFile}");
        Console.WriteLine();

        try
        {
            var client = new DistributionClient(serverUrl);
            var snapshot = client.GetSnapshotAsync().GetAwaiter().GetResult();

            if (snapshot != null)
            {
                string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(options.OutputFile, json);

                Console.WriteLine($"[SUCCESS] Snapshot exported to '{options.OutputFile}'.");
                Console.WriteLine($"State Digest:   {snapshot.StateDigest}");
                Console.WriteLine($"Creators:       {snapshot.Creators.Count}");
                Console.WriteLine($"Published Maps: {snapshot.PublishedMaps.Count}");
                Console.WriteLine($"Map Stats:      {snapshot.MapStats.Count}");
                Console.WriteLine($"Generated At:   {snapshot.GeneratedAtUtc:o}");
                return 0;
            }
            else
            {
                Console.Error.WriteLine("[FAILED] Could not retrieve snapshot from server.");
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Connection failed: {ex.Message}");
            return 1;
        }
    }

    private static int ExecuteRestoreSnapshot(AdminRestoreSnapshotOptions options)
    {
        string serverUrl = ResolveServerUrl(options.Server);
        var keyPair = ParseAdminKey(options.Key);
        if (keyPair == null) return 1;

        if (!File.Exists(options.InputFile))
        {
            Console.Error.WriteLine($"[ERROR] File '{options.InputFile}' not found.");
            return 1;
        }

        Console.WriteLine("=================================================");
        Console.WriteLine("Realm Admin Tool - Restore Database Snapshot");
        Console.WriteLine("=================================================");
        Console.WriteLine($"Server:    {serverUrl}");
        Console.WriteLine($"Input:     {options.InputFile}");
        Console.WriteLine($"Admin Key: {keyPair.Value.publicKeyBase64}");
        Console.WriteLine();

        try
        {
            string json = File.ReadAllText(options.InputFile);
            var snapshot = JsonSerializer.Deserialize<ClusterSnapshotDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (snapshot == null)
            {
                Console.Error.WriteLine("[ERROR] Could not parse snapshot JSON.");
                return 1;
            }

            var client = new DistributionClient(serverUrl);
            var result = client.ApplySnapshotAsync(snapshot, keyPair.Value.privateKeyBase64).GetAwaiter().GetResult();

            if (result.Success)
            {
                Console.WriteLine($"[SUCCESS] Snapshot applied: {result.Message}");
                Console.WriteLine($"Computed Digest: {result.ComputedStateDigest}");
                Console.WriteLine($"Expected Digest: {result.ExpectedStateDigest}");
                Console.WriteLine($"Creators:        {result.CreatorsImported}");
                Console.WriteLine($"Maps:            {result.MapsImported}");
                Console.WriteLine($"Stats:           {result.StatsImported}");
                return 0;
            }
            else
            {
                Console.Error.WriteLine($"[FAILED] {result.Message}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Restore failed: {ex.Message}");
            return 1;
        }
    }

    private static int ExecuteRemoveManifest(AdminRemoveManifestOptions options)
    {
        string serverUrl = ResolveServerUrl(options.Server);
        var keyPair = ParseAdminKey(options.Key);
        if (keyPair == null) return 1;

        string mapTitle = options.Map.Trim();
        string? mapVersion = string.IsNullOrWhiteSpace(options.Version) ? null : options.Version.Trim();
        bool isAllVersions = mapVersion == null;

        PrintRemoveManifestHeader(serverUrl, mapTitle, mapVersion, isAllVersions, keyPair.Value.publicKeyBase64);

        try
        {
            return ProcessRemoveManifest(options, serverUrl, keyPair.Value.privateKeyBase64, mapTitle, mapVersion, isAllVersions);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Remove manifest failed: {ex.Message}");
            return 1;
        }
    }

    private static void PrintRemoveManifestHeader(string serverUrl, string mapTitle, string? mapVersion, bool isAllVersions, string publicKeyBase64)
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("Realm Admin Tool - Remove Published Map Manifest");
        Console.WriteLine("=================================================");
        Console.WriteLine($"Server:     {serverUrl}");
        Console.WriteLine($"Map Title:  {mapTitle}");
        Console.WriteLine($"Version:    {(isAllVersions ? "[ALL VERSIONS]" : mapVersion)}");
        Console.WriteLine($"Admin Key:  {publicKeyBase64}");
        Console.WriteLine();
    }

    private static int ProcessRemoveManifest(AdminRemoveManifestOptions options, string serverUrl, string privateKey, string mapTitle, string? mapVersion, bool isAllVersions)
    {
        var client = new DistributionClient(serverUrl);
        var result = isAllVersions
            ? client.RemoveAllManifestVersionsAsync(mapTitle, privateKey).GetAwaiter().GetResult()
            : client.RemoveManifestVersionAsync(mapTitle, mapVersion!, privateKey).GetAwaiter().GetResult();

        if (!result.Success)
        {
            Console.Error.WriteLine($"[FAILED] {result.Message}");
            return 1;
        }

        PrintRemoveManifestSuccess(result);

        if (options.Prune)
        {
            ProcessSubsequentPrune(client, privateKey);
        }

        Console.WriteLine("=================================================");
        return 0;
    }

    private static void PrintRemoveManifestSuccess(RemoveManifestResponseDto result)
    {
        Console.WriteLine($"[SUCCESS] {result.Message}");
        Console.WriteLine($"Manifests Deleted:   {result.ManifestsDeleted}");
        Console.WriteLine($"DB Records Removed:  {result.DbRecordsRemoved}");
        if (result.DeletedManifestFiles.Count > 0)
        {
            Console.WriteLine($"Deleted Files:       {string.Join(", ", result.DeletedManifestFiles)}");
        }
    }

    private static void ProcessSubsequentPrune(DistributionClient client, string privateKey)
    {
        Console.WriteLine();
        Console.WriteLine("Running subsequent CAS prune...");
        var pruneResult = client.PruneServerCasAsync(privateKey).GetAwaiter().GetResult();

        if (!pruneResult.Success)
        {
            Console.Error.WriteLine($"[WARNING] Subsequent CAS prune failed: {pruneResult.Message}");
            return;
        }

        Console.WriteLine($"[SUCCESS] {pruneResult.Message}");
        if (pruneResult.TotalScanned > 0 || pruneResult.OrphansPruned > 0 || pruneResult.BytesFreed > 0)
        {
            Console.WriteLine($"Total Scanned:   {pruneResult.TotalScanned}");
            Console.WriteLine($"Orphans Pruned:  {pruneResult.OrphansPruned}");
            Console.WriteLine($"Corrupt Pruned:  {pruneResult.CorruptPruned}");
            Console.WriteLine($"Bytes Freed:     {pruneResult.BytesFreed} bytes");
        }
    }

    private static string? ResolveTargetPublicKey(string? targetKeyPath, string? targetPubKey)
    {
        if (!string.IsNullOrWhiteSpace(targetPubKey))
        {
            return targetPubKey.Trim();
        }

        if (!string.IsNullOrWhiteSpace(targetKeyPath))
        {
            return ParsePublicKeyFromFile(targetKeyPath);
        }

        Console.Error.WriteLine("Error: Please specify either --target-key <path to .rkey> or -p/--pubkey <base64 public key>.");
        return null;
    }

    private static int ExecuteAddMaintainer(AdminAddMaintainerOptions options)
    {
        string serverUrl = ResolveServerUrl(options.Server);
        var keyPair = ParseAdminKey(options.Key);
        if (keyPair == null) return 1;

        string mapTitle = options.Map.Trim();
        string? targetPubKey = ResolveTargetPublicKey(options.TargetKey, options.PubKey);
        if (string.IsNullOrWhiteSpace(targetPubKey)) return 1;

        string payload = $"add_maintainer:{mapTitle.ToLowerInvariant()}:{targetPubKey.Trim()}";
        string signature = AuthorSignatureHelper.SignMessage(keyPair.Value.privateKeyBase64, payload);

        Console.WriteLine("=================================================");
        Console.WriteLine("Realm Admin Tool - Add Map Maintainer");
        Console.WriteLine("=================================================");
        Console.WriteLine($"Server:          {serverUrl}");
        Console.WriteLine($"Map:             {mapTitle}");
        Console.WriteLine($"Maintainer Key:  {targetPubKey}");
        Console.WriteLine($"Requester Key:   {keyPair.Value.publicKeyBase64}");
        Console.WriteLine();

        try
        {
            var client = new DistributionClient(serverUrl);
            var request = new AddMapMaintainerRequest
            {
                MapTitle = mapTitle,
                MaintainerPublicKey = targetPubKey,
                RequesterPublicKey = keyPair.Value.publicKeyBase64,
                Signature = signature
            };

            var response = client.AddMapMaintainerAsync(request).GetAwaiter().GetResult();
            if (response.Success)
            {
                Console.WriteLine($"[SUCCESS] {response.Message}");
                Console.WriteLine($"Owner:              {response.OwnerPublicKey}");
                Console.WriteLine($"Total Maintainers:  {response.Maintainers.Count}");
                for (int i = 0; i < response.Maintainers.Count; i++)
                {
                    Console.WriteLine($"  [{i + 1}] {response.Maintainers[i]}");
                }
                Console.WriteLine("=================================================");
                return 0;
            }
            else
            {
                Console.Error.WriteLine($"[FAILED] {response.Message}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Add maintainer failed: {ex.Message}");
            return 1;
        }
    }

    private static int ExecuteRemoveMaintainer(AdminRemoveMaintainerOptions options)
    {
        string serverUrl = ResolveServerUrl(options.Server);
        var keyPair = ParseAdminKey(options.Key);
        if (keyPair == null) return 1;

        string mapTitle = options.Map.Trim();
        string? targetPubKey = ResolveTargetPublicKey(options.TargetKey, options.PubKey);
        if (string.IsNullOrWhiteSpace(targetPubKey)) return 1;

        string payload = $"remove_maintainer:{mapTitle.ToLowerInvariant()}:{targetPubKey.Trim()}";
        string signature = AuthorSignatureHelper.SignMessage(keyPair.Value.privateKeyBase64, payload);

        Console.WriteLine("=================================================");
        Console.WriteLine("Realm Admin Tool - Remove Map Maintainer");
        Console.WriteLine("=================================================");
        Console.WriteLine($"Server:          {serverUrl}");
        Console.WriteLine($"Map:             {mapTitle}");
        Console.WriteLine($"Maintainer Key:  {targetPubKey}");
        Console.WriteLine($"Requester Key:   {keyPair.Value.publicKeyBase64}");
        Console.WriteLine();

        try
        {
            var client = new DistributionClient(serverUrl);
            var request = new RemoveMapMaintainerRequest
            {
                MapTitle = mapTitle,
                MaintainerPublicKey = targetPubKey,
                RequesterPublicKey = keyPair.Value.publicKeyBase64,
                Signature = signature
            };

            var response = client.RemoveMapMaintainerAsync(request).GetAwaiter().GetResult();
            if (response.Success)
            {
                Console.WriteLine($"[SUCCESS] {response.Message}");
                Console.WriteLine($"Owner:              {response.OwnerPublicKey}");
                Console.WriteLine($"Total Maintainers:  {response.Maintainers.Count}");
                for (int i = 0; i < response.Maintainers.Count; i++)
                {
                    Console.WriteLine($"  [{i + 1}] {response.Maintainers[i]}");
                }
                Console.WriteLine("=================================================");
                return 0;
            }
            else
            {
                Console.Error.WriteLine($"[FAILED] {response.Message}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Remove maintainer failed: {ex.Message}");
            return 1;
        }
    }

    private static int ExecuteListMaintainers(AdminListMaintainersOptions options)
    {
        string serverUrl = ResolveServerUrl(options.Server);
        string mapTitle = options.Map.Trim();

        Console.WriteLine("=================================================");
        Console.WriteLine("Realm Admin Tool - List Map Maintainers");
        Console.WriteLine("=================================================");
        Console.WriteLine($"Server:  {serverUrl}");
        Console.WriteLine($"Map:     {mapTitle}");
        Console.WriteLine();

        try
        {
            var client = new DistributionClient(serverUrl);
            var response = client.GetMapMaintainersAsync(mapTitle).GetAwaiter().GetResult();
            if (response.Success)
            {
                Console.WriteLine($"Owner:              {(!string.IsNullOrEmpty(response.OwnerPublicKey) ? response.OwnerPublicKey : "(None)")}");
                Console.WriteLine($"Total Maintainers:  {response.Maintainers.Count}");
                for (int i = 0; i < response.Maintainers.Count; i++)
                {
                    Console.WriteLine($"  [{i + 1}] {response.Maintainers[i]}");
                }
                Console.WriteLine("=================================================");
                return 0;
            }
            else
            {
                Console.Error.WriteLine($"[FAILED] {response.Message}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] List maintainers failed: {ex.Message}");
            return 1;
        }
    }
}
