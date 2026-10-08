using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

public class DistributionServer
{
    private readonly ContentAddressableStorage _storage;
    private readonly string _seederId;
    private readonly int _capacityPercentage;
    private readonly TokenBucketThrottle? _throttle;
    private readonly string? _adminPublicKeyBase64;
    private readonly Func<string, string, bool>? _greenlightChecker;
    private HttpListener? _listener;
    private bool _isRunning;
    private CancellationTokenSource? _cancellationTokenSource;

    public string SeederId => _seederId;
    public int CapacityPercentage => _capacityPercentage;
    public ContentAddressableStorage Storage => _storage;
    public int BoundPort { get; private set; }

    public DistributionServer(
        ContentAddressableStorage storage,
        string seederId,
        int capacityPercentage = 100,
        TokenBucketThrottle? throttle = null,
        string? adminPublicKeyBase64 = null,
        Func<string, string, bool>? greenlightChecker = null)
    {
        _storage = storage;
        _seederId = seederId;
        _capacityPercentage = capacityPercentage;
        _throttle = throttle;
        _adminPublicKeyBase64 = adminPublicKeyBase64;
        _greenlightChecker = greenlightChecker;
    }

    public void Start(int port)
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add($"http://localhost:{port}/");

        try
        {
            _listener.Start();
            BoundPort = port;
            _isRunning = true;
            Task.Run(() => ListenLoopAsync(_listener, _cancellationTokenSource.Token));
        }
        catch (HttpListenerException)
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            BoundPort = port;
            _isRunning = true;
            Task.Run(() => ListenLoopAsync(_listener, _cancellationTokenSource.Token));
        }
    }

    public void Stop()
    {
        _isRunning = false;
        _cancellationTokenSource?.Cancel();

        try
        {
            if (_listener != null && _listener.IsListening)
            {
                _listener.Stop();
                _listener.Close();
            }
        }
        catch
        {
        }
    }

    private async Task ListenLoopAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        while (_isRunning && !cancellationToken.IsCancellationRequested)
        {
            bool continueListening = await TryProcessNextRequestAsync(listener, cancellationToken);
            if (!continueListening) break;
        }
    }

    private async Task<bool> TryProcessNextRequestAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            var context = await listener.GetContextAsync();
            _ = Task.Run(() => HandleRequestAsync(context), cancellationToken);
            return true;
        }
        catch
        {
            return _isRunning;
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        string path = request.Url?.LocalPath.TrimEnd('/') ?? string.Empty;
        string method = request.HttpMethod.ToUpperInvariant();

        try
        {
            Task? routeTask = GetRouteTask(context, path, method);
            if (routeTask != null)
            {
                await routeTask;
                return;
            }

            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.Close();
        }
        catch (Exception exception)
        {
            await HandleRequestExceptionAsync(response, exception);
        }
    }

    private Task? GetRouteTask(HttpListenerContext context, string path, string method)
    {
        string lowerPath = path.ToLowerInvariant();
        return lowerPath switch
        {
            "/api/assets/bundle" when method == "POST" => HandleAssetBundleEndpointAsync(context),
            _ when lowerPath.StartsWith("/api/assets/") => HandleAssetEndpointAsync(context, method, path.Substring("/api/assets/".Length)),
            "/api/publish_map/upload_asset" => HandleAssetEndpointAsync(context, method, string.Empty),
            _ when lowerPath.StartsWith("/api/manifests") || lowerPath == "/api/admin/remove_manifest" => HandleManifestEndpointAsync(context, method, path),
            "/api/seeders/catalog" when method == "GET" => HandleCatalogEndpointAsync(context.Response),
            "/api/seeders/bloom_headers" when method == "GET" => HandleBloomHeadersEndpointAsync(context.Response),
            "/api/seeders/sync_headers" when method == "POST" => HandleHeaderSyncEndpointAsync(context.Request, context.Response),
            _ => null
        };
    }

    private async Task HandleRequestExceptionAsync(HttpListenerResponse response, Exception exception)
    {
        try
        {
            response.StatusCode = (int)HttpStatusCode.InternalServerError;
            byte[] errorBytes = Encoding.UTF8.GetBytes(exception.Message);
            await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length);
            response.Close();
        }
        catch
        {
        }
    }

    private async Task HandleAssetBundleEndpointAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        string json = await reader.ReadToEndAsync();
        
        AssetBundleRequestDto? req = null;
        try
        {
            req = JsonSerializer.Deserialize<AssetBundleRequestDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { }

        if (req == null || req.Hashes == null || req.Hashes.Count == 0)
        {
            response.StatusCode = (int)HttpStatusCode.BadRequest;
            response.Close();
            return;
        }

        var assetInfos = GetValidAssetInfos(req.Hashes);

        response.ContentType = "application/octet-stream";
        response.StatusCode = (int)HttpStatusCode.OK;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource?.Token ?? CancellationToken.None);
            await ZstdAssetBundleHelper.StreamAssetsToBundleAsync(
                response.OutputStream,
                assetInfos,
                compressionLevel: 1,
                cancellationToken: cts.Token);
        }
        catch
        {
        }
        finally
        {
            CloseResponseSafe(response);
        }
    }

    private List<(string AssetKey, string FilePath, string? Metadata)> GetValidAssetInfos(List<string> hashes)
    {
        var assetInfos = new List<(string AssetKey, string FilePath, string? Metadata)>();
        foreach (var hash in hashes)
        {
            if (string.IsNullOrWhiteSpace(hash)) continue;
            string normalized = ContentAddressableStorage.NormalizeBlake3Hash(hash);
            string? filePath = _storage.FindAssetFilePath(normalized);
            if (filePath == null || !File.Exists(filePath)) continue;

            string? metadata = _storage.GetAssetMetadata(normalized);
            assetInfos.Add((normalized, filePath, metadata));
        }
        return assetInfos;
    }

    private void CloseResponseSafe(HttpListenerResponse response)
    {
        try { response.Close(); } catch { }
    }

    private async Task HandleAssetEndpointAsync(HttpListenerContext context, string method, string hash)
    {
        string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(hash);

        if (method == "HEAD")
        {
            HandleAssetHeadRequest(context.Response, normalizedHash);
            return;
        }

        if (method == "GET")
        {
            await HandleAssetGetRequestAsync(context.Response, normalizedHash);
            return;
        }

        if (method == "POST")
        {
            await HandleAssetPostRequestAsync(context.Request, context.Response, normalizedHash);
            return;
        }

        context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
        context.Response.Close();
    }

    private void HandleAssetHeadRequest(HttpListenerResponse response, string normalizedHash)
    {
        response.StatusCode = _storage.HasAsset(normalizedHash) ? (int)HttpStatusCode.OK : (int)HttpStatusCode.NotFound;
        response.Close();
    }

    private async Task HandleAssetGetRequestAsync(HttpListenerResponse response, string normalizedHash)
    {
        string? filePath = _storage.FindAssetFilePath(normalizedHash);
        if (filePath == null || !File.Exists(filePath))
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.Close();
            return;
        }

        byte[] fileBytes = _storage.GetAssetBytes(normalizedHash) ?? Array.Empty<byte>();
        string? metadataJson = _storage.GetAssetMetadata(normalizedHash);

        if (_throttle != null)
        {
            await _throttle.ConsumeAsync(fileBytes.Length);
        }

        response.ContentType = "application/octet-stream";
        response.ContentLength64 = fileBytes.Length;
        response.AddHeader("Content-Disposition", $"attachment; filename=\"{Path.GetFileName(filePath)}\"");

        if (!string.IsNullOrWhiteSpace(metadataJson))
        {
            response.AddHeader("X-Asset-Metadata", Convert.ToBase64String(Encoding.UTF8.GetBytes(metadataJson)));
        }

        response.StatusCode = (int)HttpStatusCode.OK;
        await response.OutputStream.WriteAsync(fileBytes, 0, fileBytes.Length);
        response.Close();
    }

    private async Task HandleAssetPostRequestAsync(HttpListenerRequest request, HttpListenerResponse response, string normalizedHash)
    {
        if (!DistributionSharding.SeederAcceptsHash(_seederId, _capacityPercentage, normalizedHash))
        {
            await RejectPostRequestAsync(response, (int)HttpStatusCode.Forbidden, "Upload rejected: Asset does not match seeder sharding partition.");
            return;
        }

        if (!_storage.CheckFreeDiskSpaceAcceptingUploads())
        {
            await RejectPostRequestAsync(response, 507, "Upload rejected: Insufficient disk space (<10% available).");
            return;
        }

        var reqData = await ParseAssetPostRequestDataAsync(request, normalizedHash);

        if (string.IsNullOrEmpty(reqData.NormalizedHash) && reqData.AssetBytes.Length > 0)
        {
            string computedBlake3 = RealmMetadataHelper.ComputeBlake3(reqData.AssetBytes, reqData.FileExtension);
            reqData.NormalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(computedBlake3);
        }

        var storeResult = _storage.StoreAsset(reqData.AssetBytes, reqData.FileExtension, reqData.MetadataJson, reqData.AuthorPublicKey, reqData.AuthorSignature);
        await SendStoreResultAsync(response, storeResult);
    }

    private class AssetPostRequestData
    {
        public byte[] AssetBytes { get; set; } = Array.Empty<byte>();
        public string? MetadataJson { get; set; }
        public string? AuthorPublicKey { get; set; }
        public string? AuthorSignature { get; set; }
        public string FileExtension { get; set; } = ".bin";
        public string NormalizedHash { get; set; } = string.Empty;
    }

    private async Task<AssetPostRequestData> ParseAssetPostRequestDataAsync(HttpListenerRequest request, string normalizedHash)
    {
        var result = new AssetPostRequestData
        {
            NormalizedHash = normalizedHash,
            MetadataJson = DecodeMetadataHeader(request.Headers["X-Asset-Metadata"]),
            AuthorPublicKey = request.Headers["X-Author-Public-Key"],
            AuthorSignature = request.Headers["X-Author-Signature"],
            FileExtension = request.Headers["X-File-Extension"] ?? ".bin"
        };

        if (request.ContentType != null && request.ContentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            var parsedForm = await ParseMultipartFormAsync(request);
            result.AssetBytes = parsedForm.FileBytes ?? Array.Empty<byte>();
            result.MetadataJson ??= parsedForm.MetadataJson;
            result.AuthorPublicKey ??= parsedForm.AuthorPublicKey;
            result.AuthorSignature ??= parsedForm.AuthorSignature;
            
            string parsedExt = Path.GetExtension(parsedForm.FileName);
            if (!string.IsNullOrEmpty(parsedExt))
            {
                result.FileExtension = parsedExt;
            }
            
            if (string.IsNullOrEmpty(result.NormalizedHash) && !string.IsNullOrEmpty(parsedForm.Hash))
            {
                result.NormalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(parsedForm.Hash);
            }
        }
        else
        {
            using var memoryStream = new MemoryStream();
            await request.InputStream.CopyToAsync(memoryStream);
            result.AssetBytes = memoryStream.ToArray();
        }

        return result;
    }

    private async Task RejectPostRequestAsync(HttpListenerResponse response, int statusCode, string message)
    {
        response.StatusCode = statusCode;
        byte[] rejectionBytes = Encoding.UTF8.GetBytes(message);
        await response.OutputStream.WriteAsync(rejectionBytes, 0, rejectionBytes.Length);
        response.Close();
    }

    private async Task SendStoreResultAsync(HttpListenerResponse response, (bool Success, string Message, bool Deduplicated, bool Merged, string Blake3Hash) storeResult)
    {
        var responseDto = new AssetUploadResponseDto
        {
            Success = storeResult.Success,
            Message = storeResult.Message,
            Deduplicated = storeResult.Deduplicated,
            Merged = storeResult.Merged,
            Blake3Hash = storeResult.Blake3Hash
        };

        byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(responseDto));
        response.ContentType = "application/json";
        response.StatusCode = storeResult.Success ? (int)HttpStatusCode.OK : (int)HttpStatusCode.BadRequest;
        await response.OutputStream.WriteAsync(jsonBytes, 0, jsonBytes.Length);
        response.Close();
    }

    private async Task HandleManifestEndpointAsync(HttpListenerContext context, string method, string path)
    {
        if (method == "GET")
        {
            await HandleManifestGetAsync(context.Response, path);
            return;
        }

        if (method == "POST" && !path.Equals("/api/admin/remove_manifest", StringComparison.OrdinalIgnoreCase))
        {
            await HandleManifestPostAsync(context.Request, context.Response);
            return;
        }

        if (method == "DELETE" || (method == "POST" && path.Equals("/api/admin/remove_manifest", StringComparison.OrdinalIgnoreCase)))
        {
            await HandleManifestDeleteAsync(context.Request, context.Response, method, path);
            return;
        }

        context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
        context.Response.Close();
    }

    private async Task HandleManifestGetAsync(HttpListenerResponse response, string path)
    {
        string rawMapId = path.Length > "/api/manifests/".Length ? path.Substring("/api/manifests/".Length) : string.Empty;
        string mapId = Uri.UnescapeDataString(rawMapId);
        string manifestFolder = Path.Combine(_storage.RootDirectory, "manifests");

        if (!Directory.Exists(manifestFolder))
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.Close();
            return;
        }

        var candidates = new List<string>
        {
            Path.Combine(manifestFolder, $"{mapId}_manifest.json"),
            Path.Combine(manifestFolder, $"{mapId}.json"),
            Path.Combine(manifestFolder, $"{mapId.Replace(' ', '_')}_manifest.json"),
            Path.Combine(manifestFolder, $"{mapId.Replace(' ', '_')}.json"),
            Path.Combine(manifestFolder, $"{mapId.Replace('_', ' ')}_manifest.json"),
            Path.Combine(manifestFolder, $"{mapId.Replace('_', ' ')}.json")
        };

        string? foundPath = candidates.FirstOrDefault(File.Exists);
        if (foundPath == null) foundPath = TryFindManifestFileByContent(manifestFolder, mapId);

        if (foundPath == null || !File.Exists(foundPath))
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.Close();
            return;
        }

        byte[] manifestBytes = await File.ReadAllBytesAsync(foundPath);
        response.ContentType = "application/json";
        response.StatusCode = (int)HttpStatusCode.OK;
        await response.OutputStream.WriteAsync(manifestBytes, 0, manifestBytes.Length);
        response.Close();
    }

    private string? TryFindManifestFileByContent(string manifestFolder, string mapId)
    {
        foreach (var file in Directory.EnumerateFiles(manifestFolder, "*.json"))
        {
            try
            {
                var mf = MapManifest.LoadFromFile(file);
                if (mf != null && (string.Equals(mf.MapName, mapId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(mf.MapName?.Replace(' ', '_'), mapId.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase)))
                {
                    return file;
                }
            }
            catch { }
        }
        return null;
    }

    private async Task HandleManifestPostAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        string manifestJson = await reader.ReadToEndAsync();
        var manifest = MapManifest.LoadFromJson(manifestJson);

        if (manifest == null || string.IsNullOrWhiteSpace(manifest.MapName))
        {
            response.StatusCode = (int)HttpStatusCode.BadRequest;
            byte[] error = Encoding.UTF8.GetBytes("Invalid manifest payload.");
            await response.OutputStream.WriteAsync(error, 0, error.Length);
            response.Close();
            return;
        }

        bool isGreenlit = _greenlightChecker?.Invoke(manifest.MapName, manifest.Version) ?? false;
        string? bypassToken = request.Headers["X-Admin-Bypass"];

        if (!isGreenlit && !IsAdminBypassValid(manifest, bypassToken))
        {
            response.StatusCode = (int)HttpStatusCode.Forbidden;
            var forbiddenDto = new MapPublishResponseDto
            {
                Success = false,
                Status = "GreenlightRequired",
                Message = "Map does not have sufficient greenlight metrics and no valid admin bypass token was provided."
            };
            byte[] forbiddenBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(forbiddenDto));
            response.ContentType = "application/json";
            await response.OutputStream.WriteAsync(forbiddenBytes, 0, forbiddenBytes.Length);
            response.Close();
            return;
        }

        string manifestDirectory = Path.Combine(_storage.RootDirectory, "manifests");
        Directory.CreateDirectory(manifestDirectory);
        string savedManifestPath = Path.Combine(manifestDirectory, $"{manifest.MapName}_manifest.json");
        await File.WriteAllTextAsync(savedManifestPath, manifestJson);

        var missingHashes = manifest.Files.Values
            .Select(ContentAddressableStorage.NormalizeBlake3Hash)
            .Where(hash => !_storage.HasAsset(hash))
            .ToList();

        var publishResponse = new MapPublishResponseDto
        {
            Success = true,
            Status = "Published",
            MapId = $"{manifest.MapName}_{manifest.Version}",
            MissingAssetHashes = missingHashes
        };

        byte[] responseBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(publishResponse));
        response.ContentType = "application/json";
        response.StatusCode = (int)HttpStatusCode.OK;
        await response.OutputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
        response.Close();
    }

    private bool IsAdminBypassValid(MapManifest manifest, string? bypassToken)
    {
        if (string.IsNullOrEmpty(_adminPublicKeyBase64) || string.IsNullOrEmpty(bypassToken)) return false;
        return AdminBypassAuth.VerifyBypassToken(_adminPublicKeyBase64, manifest.MapName, manifest.Version, bypassToken);
    }

    private async Task HandleManifestDeleteAsync(HttpListenerRequest request, HttpListenerResponse response, string method, string path)
    {
        string mapTitle = string.Empty;
        string? mapVersion = null;
        string? adminPubKey = request.Headers["X-Admin-PublicKey"];
        string? signature = request.Headers["X-Cluster-Signature"];
        string? bypassToken = request.Headers["X-Admin-Bypass"];

        if (method == "POST" && request.HasEntityBody)
        {
            (mapTitle, mapVersion, adminPubKey, signature) = await ParseAdminRemoveRequestAsync(request, mapTitle, mapVersion, adminPubKey, signature);
        }

        if (string.IsNullOrEmpty(mapTitle) && path.StartsWith("/api/manifests/", StringComparison.OrdinalIgnoreCase))
        {
            string rawMapId = path.Substring("/api/manifests/".Length);
            string unescaped = Uri.UnescapeDataString(rawMapId);
            string[] parts = unescaped.Split('/');
            mapTitle = parts[0];
            if (parts.Length > 1) mapVersion = parts[1];
        }

        if (string.IsNullOrWhiteSpace(mapTitle))
        {
            response.StatusCode = (int)HttpStatusCode.BadRequest;
            byte[] error = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new RemoveManifestResponseDto { Success = false, Message = "MapTitle is required." }));
            response.ContentType = "application/json";
            await response.OutputStream.WriteAsync(error, 0, error.Length);
            response.Close();
            return;
        }

        if (!IsDeleteAuthorized(mapTitle, mapVersion, signature, bypassToken))
        {
            response.StatusCode = (int)HttpStatusCode.Unauthorized;
            byte[] error = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new RemoveManifestResponseDto { Success = false, Message = "Unauthorized admin request." }));
            response.ContentType = "application/json";
            await response.OutputStream.WriteAsync(error, 0, error.Length);
            response.Close();
            return;
        }

        string manifestFolder = Path.Combine(_storage.RootDirectory, "manifests");
        var (manifestsDeleted, deletedFiles) = DeleteManifestFiles(manifestFolder, mapTitle, mapVersion);

        var removeDto = new RemoveManifestResponseDto
        {
            Success = true,
            MapTitle = mapTitle,
            MapVersion = mapVersion,
            AllVersionsRemoved = string.IsNullOrWhiteSpace(mapVersion),
            ManifestsDeleted = manifestsDeleted,
            DeletedManifestFiles = deletedFiles,
            Message = string.IsNullOrWhiteSpace(mapVersion)
                ? $"Successfully removed all published manifest versions for map '{mapTitle}'."
                : $"Successfully removed published manifest for map '{mapTitle}' version '{mapVersion}'."
        };

        byte[] respBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(removeDto));
        response.ContentType = "application/json";
        response.StatusCode = (int)HttpStatusCode.OK;
        await response.OutputStream.WriteAsync(respBytes, 0, respBytes.Length);
        response.Close();
    }

    private async Task<(string mapTitle, string? mapVersion, string? adminPubKey, string? signature)> ParseAdminRemoveRequestAsync(
        HttpListenerRequest request, string mapTitle, string? mapVersion, string? adminPubKey, string? signature)
    {
        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        string body = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(body)) return (mapTitle, mapVersion, adminPubKey, signature);

        try
        {
            var req = JsonSerializer.Deserialize<AdminRemoveManifestRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (req != null)
            {
                mapTitle = req.MapTitle;
                mapVersion = req.MapVersion;
                if (!string.IsNullOrWhiteSpace(req.AdminPublicKey)) adminPubKey = req.AdminPublicKey;
                if (!string.IsNullOrWhiteSpace(req.Signature)) signature = req.Signature;
            }
        }
        catch { }
        return (mapTitle, mapVersion, adminPubKey, signature);
    }

    private bool IsDeleteAuthorized(string mapTitle, string? mapVersion, string? signature, string? bypassToken)
    {
        if (string.IsNullOrEmpty(_adminPublicKeyBase64)) return false;

        string targetVerStr = mapVersion ?? "all";

        if (CheckBypassToken(mapTitle, targetVerStr, bypassToken))
        {
            return true;
        }

        return CheckSignature(mapTitle, targetVerStr, signature);
    }

    private bool CheckBypassToken(string mapTitle, string targetVerStr, string? bypassToken)
    {
        if (string.IsNullOrEmpty(bypassToken)) return false;
        
        return AdminBypassAuth.VerifyBypassToken(_adminPublicKeyBase64!, mapTitle, targetVerStr, bypassToken) ||
               AdminBypassAuth.VerifyBypassToken(_adminPublicKeyBase64!, "admin", "remove_manifest", bypassToken) ||
               AdminBypassAuth.VerifyBypassToken(_adminPublicKeyBase64!, mapTitle, "*", bypassToken);
    }

    private bool CheckSignature(string mapTitle, string targetVerStr, string? signature)
    {
        if (string.IsNullOrEmpty(signature)) return false;

        string sigPayload1 = $"remove_manifest:{mapTitle.Trim().ToLowerInvariant()}:{targetVerStr.ToLowerInvariant()}";
        string sigPayload2 = $"remove_manifest:{mapTitle.Trim().ToLowerInvariant()}";

        return AuthorSignatureHelper.VerifySignature(_adminPublicKeyBase64!, sigPayload1, signature) ||
               AuthorSignatureHelper.VerifySignature(_adminPublicKeyBase64!, sigPayload2, signature) ||
               AdminBypassAuth.VerifyBypassToken(_adminPublicKeyBase64!, mapTitle, targetVerStr, signature);
    }

    private (int manifestsDeleted, List<string> deletedFiles) DeleteManifestFiles(string manifestFolder, string mapTitle, string? mapVersion)
    {
        int manifestsDeleted = 0;
        var deletedFiles = new List<string>();

        if (!Directory.Exists(manifestFolder)) return (manifestsDeleted, deletedFiles);

        if (!string.IsNullOrWhiteSpace(mapVersion))
        {
            return DeleteSpecificVersionFiles(manifestFolder, mapTitle, mapVersion);
        }

        return DeleteAllVersionsFiles(manifestFolder, mapTitle);
    }

    private (int manifestsDeleted, List<string> deletedFiles) DeleteSpecificVersionFiles(string manifestFolder, string mapTitle, string mapVersion)
    {
        int manifestsDeleted = 0;
        var deletedFiles = new List<string>();
        string compositeKey = $"{mapTitle}_{mapVersion}";
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"{compositeKey}_manifest.json",
            $"{compositeKey}.json",
            $"{compositeKey.Replace(' ', '_')}_manifest.json",
            $"{compositeKey.Replace(' ', '_')}.json"
        };

        foreach (var file in Directory.EnumerateFiles(manifestFolder, "*.json"))
        {
            string fileName = Path.GetFileName(file);
            bool shouldDel = candidates.Contains(fileName);
            if (!shouldDel)
            {
                try
                {
                    var mf = MapManifest.LoadFromFile(file);
                    if (mf != null && string.Equals(mf.MapName, mapTitle, StringComparison.OrdinalIgnoreCase) && string.Equals(mf.Version, mapVersion, StringComparison.OrdinalIgnoreCase))
                    {
                        shouldDel = true;
                    }
                }
                catch { }
            }

            if (shouldDel)
            {
                try
                {
                    File.Delete(file);
                    manifestsDeleted++;
                    deletedFiles.Add(fileName);
                }
                catch { }
            }
        }
        return (manifestsDeleted, deletedFiles);
    }

    private (int manifestsDeleted, List<string> deletedFiles) DeleteAllVersionsFiles(string manifestFolder, string mapTitle)
    {
        int manifestsDeleted = 0;
        var deletedFiles = new List<string>();

        foreach (var file in Directory.EnumerateFiles(manifestFolder, "*.json"))
        {
            string fileName = Path.GetFileName(file);
            bool shouldDel = ShouldDeleteVersionFile(file, fileName, mapTitle);

            if (!shouldDel) continue;

            try
            {
                File.Delete(file);
                manifestsDeleted++;
                deletedFiles.Add(fileName);
            }
            catch { }
        }
        return (manifestsDeleted, deletedFiles);
    }

    private bool ShouldDeleteVersionFile(string file, string fileName, string mapTitle)
    {
        bool isNameMatch = fileName.StartsWith($"{mapTitle}_", StringComparison.OrdinalIgnoreCase) ||
                           fileName.StartsWith($"{mapTitle.Replace(' ', '_')}_", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(fileName, $"{mapTitle}.json", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(fileName, $"{mapTitle.Replace(' ', '_')}.json", StringComparison.OrdinalIgnoreCase);

        if (isNameMatch) return true;

        try
        {
            var mf = MapManifest.LoadFromFile(file);
            return mf != null && string.Equals(mf.MapName, mapTitle, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task HandleCatalogEndpointAsync(HttpListenerResponse response)
    {
        var hashes = _storage.GetAllStoredHashes().ToList();
        var catalog = new SeederCatalogResponseDto
        {
            SeederId = _seederId,
            CapacityPercentage = _capacityPercentage,
            AssetHashes = hashes
        };

        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(catalog));
        response.ContentType = "application/json";
        response.StatusCode = (int)HttpStatusCode.OK;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        response.Close();
    }

    private async Task HandleBloomHeadersEndpointAsync(HttpListenerResponse response)
    {
        var hashes = _storage.GetAllStoredHashes().ToList();
        var bloomFilter = new BloomFilter(Math.Max(100, hashes.Count), 0.01);

        foreach (string hash in hashes)
        {
            string? metadataJson = _storage.GetAssetMetadata(hash);
            string key = BloomFilter.CreateHeaderKey(hash, metadataJson);
            bloomFilter.Add(key);
        }

        var dto = new BloomHeadersResponseDto
        {
            SeederId = _seederId,
            BitCount = bloomFilter.BitCount,
            HashCount = bloomFilter.HashCount,
            ItemCount = bloomFilter.ItemCount,
            FilterDataBase64 = Convert.ToBase64String(bloomFilter.BitArray)
        };

        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto));
        response.ContentType = "application/json";
        response.StatusCode = (int)HttpStatusCode.OK;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        response.Close();
    }

    private async Task HandleHeaderSyncEndpointAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        string json = await reader.ReadToEndAsync();
        var syncRequest = JsonSerializer.Deserialize<HeaderSyncRequestDto>(json);

        if (syncRequest == null || string.IsNullOrWhiteSpace(syncRequest.Blake3Hash))
        {
            response.StatusCode = (int)HttpStatusCode.BadRequest;
            response.Close();
            return;
        }

        string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(syncRequest.Blake3Hash);
        bool updated = false;

        if (_storage.HasAsset(normalizedHash) && !string.IsNullOrWhiteSpace(syncRequest.MetadataHeadersJson))
        {
            byte[] existingBytes = _storage.GetAssetBytes(normalizedHash) ?? Array.Empty<byte>();
            string? filePath = _storage.FindAssetFilePath(normalizedHash);
            string extension = filePath != null ? Path.GetExtension(filePath) : ".bin";

            var storeResult = _storage.StoreAsset(
                existingBytes,
                extension,
                syncRequest.MetadataHeadersJson,
                syncRequest.AuthorPublicKey,
                syncRequest.AuthorSignature);

            updated = storeResult.Merged;
        }

        string currentMetadata = _storage.GetAssetMetadata(normalizedHash) ?? string.Empty;
        var syncResponse = new HeaderSyncResponseDto
        {
            Updated = updated,
            CurrentMetadataHeadersJson = currentMetadata
        };

        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(syncResponse));
        response.ContentType = "application/json";
        response.StatusCode = (int)HttpStatusCode.OK;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        response.Close();
    }

    private async Task<(byte[]? FileBytes, string? FileName, string? MetadataJson, string? AuthorPublicKey, string? AuthorSignature, string? Hash)> ParseMultipartFormAsync(HttpListenerRequest request)
    {
        using var memoryStream = new MemoryStream();
        await request.InputStream.CopyToAsync(memoryStream);
        byte[] body = memoryStream.ToArray();

        string contentType = request.ContentType ?? string.Empty;
        int boundaryIndex = contentType.IndexOf("boundary=", StringComparison.OrdinalIgnoreCase);
        if (boundaryIndex < 0) return (body, null, null, null, null, null);

        string rawBoundary = contentType.Substring(boundaryIndex + 9).Split(';')[0].Trim().Trim('"');
        byte[] boundaryBytes = Encoding.UTF8.GetBytes("--" + rawBoundary);

        byte[]? fileBytes = null;
        string? fileName = null;
        string? metadataJson = null;
        string? authorPublicKey = null;
        string? authorSignature = null;
        string? hash = null;

        foreach (var section in SplitBytesByBoundary(body, boundaryBytes))
        {
            var parsed = ParseSection(section);
            if (!parsed.HasValue) continue;

            ProcessMultipartSection(parsed.Value.Headers, parsed.Value.ContentBytes, 
                ref fileBytes, ref fileName, ref metadataJson, ref authorPublicKey, ref authorSignature, ref hash);
        }

        return (fileBytes ?? body, fileName, metadataJson, authorPublicKey, authorSignature, hash);
    }

    private void ProcessMultipartSection(string headers, byte[] contentBytes,
        ref byte[]? fileBytes, ref string? fileName, ref string? metadataJson,
        ref string? authorPublicKey, ref string? authorSignature, ref string? hash)
    {
        string? partName = ExtractHeaderParameter(headers, "name");
        string? partFileName = ExtractHeaderParameter(headers, "filename");

        if (string.Equals(partName, "file", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(partFileName))
        {
            if (!string.IsNullOrEmpty(partFileName)) fileName = partFileName;
            fileBytes = contentBytes;
        }
        else if (string.Equals(partName, "metadata", StringComparison.OrdinalIgnoreCase))
        {
            metadataJson = Encoding.UTF8.GetString(contentBytes);
        }
        else if (string.Equals(partName, "authorPublicKey", StringComparison.OrdinalIgnoreCase) || string.Equals(partName, "PublicKey", StringComparison.OrdinalIgnoreCase))
        {
            authorPublicKey = Encoding.UTF8.GetString(contentBytes);
        }
        else if (string.Equals(partName, "authorSignature", StringComparison.OrdinalIgnoreCase) || string.Equals(partName, "Signature", StringComparison.OrdinalIgnoreCase))
        {
            authorSignature = Encoding.UTF8.GetString(contentBytes);
        }
        else if (string.Equals(partName, "Hash", StringComparison.OrdinalIgnoreCase) || string.Equals(partName, "hash", StringComparison.OrdinalIgnoreCase))
        {
            hash = Encoding.UTF8.GetString(contentBytes);
        }
    }

    private static (string Headers, byte[] ContentBytes)? ParseSection(byte[] section)
    {
        int headerEnd = FindByteSequence(section, new byte[] { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' });
        int delimLength = 4;
        
        if (headerEnd < 0)
        {
            headerEnd = FindByteSequence(section, new byte[] { (byte)'\n', (byte)'\n' });
            delimLength = 2;
        }
        
        if (headerEnd < 0) return null;

        string headers = Encoding.UTF8.GetString(section, 0, headerEnd);
        int contentStart = headerEnd + delimLength;
        int contentLength = section.Length - contentStart;
        
        if (contentLength < 0) return null;

        if (contentLength >= 2 && section[section.Length - 2] == '\r' && section[section.Length - 1] == '\n')
        {
            contentLength -= 2;
        }
        else if (contentLength >= 1 && section[section.Length - 1] == '\n')
        {
            contentLength -= 1;
        }

        byte[] contentBytes = new byte[contentLength];
        Buffer.BlockCopy(section, contentStart, contentBytes, 0, contentLength);

        return (headers, contentBytes);
    }

    private static string? ExtractHeaderParameter(string headers, string parameterName)
    {
        int index = headers.IndexOf(parameterName + "=", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        int valueStart = index + parameterName.Length + 1;
        if (valueStart >= headers.Length)
        {
            return null;
        }

        if (headers[valueStart] == '"')
        {
            valueStart++;
            int end = headers.IndexOf('"', valueStart);
            return end > valueStart ? headers.Substring(valueStart, end - valueStart) : null;
        }
        else
        {
            int end = headers.IndexOfAny(new[] { ';', '\r', '\n', ' ' }, valueStart);
            return end > valueStart ? headers.Substring(valueStart, end - valueStart) : headers.Substring(valueStart).Trim();
        }
    }

    private static string? DecodeMetadataHeader(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return null;
        }

        try
        {
            byte[] decoded = Convert.FromBase64String(headerValue);
            return Encoding.UTF8.GetString(decoded);
        }
        catch
        {
            return headerValue;
        }
    }

    private static List<byte[]> SplitBytesByBoundary(byte[] source, byte[] boundary)
    {
        var result = new List<byte[]>();
        int currentIndex = 0;

        while (currentIndex < source.Length)
        {
            int nextMatch = FindByteSequence(source, boundary, currentIndex);
            if (nextMatch < 0)
            {
                if (currentIndex < source.Length)
                {
                    int tailLength = source.Length - currentIndex;
                    byte[] tail = new byte[tailLength];
                    Buffer.BlockCopy(source, currentIndex, tail, 0, tailLength);
                    result.Add(tail);
                }
                break;
            }

            if (nextMatch > currentIndex)
            {
                int segmentLength = nextMatch - currentIndex;
                byte[] segment = new byte[segmentLength];
                Buffer.BlockCopy(source, currentIndex, segment, 0, segmentLength);
                result.Add(segment);
            }

            currentIndex = nextMatch + boundary.Length;
        }

        return result;
    }

    private static int FindByteSequence(byte[] source, byte[] pattern, int startIndex = 0)
    {
        if (source.Length == 0 || pattern.Length == 0 || pattern.Length > source.Length)
        {
            return -1;
        }

        for (int i = startIndex; i <= source.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (source[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}
