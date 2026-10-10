using NUnit.Framework;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using System.Text;

namespace Realm.Distribution.Tests;

[TestFixture]
public class DistributionScenarioTests
{
    private string _scenarioRoot = string.Empty;
    private List<DistributionServer> _runningServers = new();

    [SetUp]
    public void Setup()
    {
        _scenarioRoot = Path.Combine(Path.GetTempPath(), "RealmScenario_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scenarioRoot);
        _runningServers = new List<DistributionServer>();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var server in _runningServers)
        {
            try
            {
                server.Stop();
            }
            catch
            {
            }
        }

        try
        {
            if (Directory.Exists(_scenarioRoot))
            {
                Directory.Delete(_scenarioRoot, true);
            }
        }
        catch
        {
        }
    }

    private int GetAvailablePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Test]
    public async Task Scenario1_HostCreatesLobby_ClientDownloadsAssetsFromHostAndSeeders()
    {
        string hostStorageDir = Path.Combine(_scenarioRoot, "host_cas");
        string seederStorageDir = Path.Combine(_scenarioRoot, "seeder_cas");
        string clientStorageDir = Path.Combine(_scenarioRoot, "client_cas");

        var hostCas = new ContentAddressableStorage(hostStorageDir);
        var seederCas = new ContentAddressableStorage(seederStorageDir);
        var clientCas = new ContentAddressableStorage(clientStorageDir);

        var manifest = new MapManifest
        {
            MapName = "test_arena",
            Author = "Tester",
            Version = "1.0.0"
        };

        var createdHashes = new List<string>();
        for (int i = 0; i < 6; i++)
        {
            byte[] fileBytes = Encoding.UTF8.GetBytes($"Asset content payload sample number {i}");
            string hash = RealmMetadataHelper.ComputeBlake3(fileBytes, ".bin");
            hostCas.StoreAsset(fileBytes, ".bin");

            if (i % 2 == 0)
            {
                seederCas.StoreAsset(fileBytes, ".bin");
            }

            string virtualPath = $"assets/file_{i}.bin";
            manifest.Files[virtualPath] = $"{hash}.bin";
            manifest.FileSizes![virtualPath] = fileBytes.Length;
            createdHashes.Add(ContentAddressableStorage.NormalizeBlake3Hash(hash));
        }

        int hostPort = GetAvailablePort();
        int seederPort = GetAvailablePort();

        var hostServer = new DistributionServer(hostCas, "host_node", 100);
        hostServer.Start(hostPort);
        _runningServers.Add(hostServer);

        var seederServer = new DistributionServer(seederCas, "seeder_node", 100);
        seederServer.Start(seederPort);
        _runningServers.Add(seederServer);

        var seederList = new List<SeederNodeDto>
        {
            new SeederNodeDto { SeederId = "seeder_node", IP = "127.0.0.1", Port = seederPort, CapacityPercentage = 100 }
        };

        var client = new DistributionClient($"http://127.0.0.1:{hostPort}");

        float latestProgress = 0f;
        bool success = await client.DownloadMissingAssetsMultiThreadedAsync(
            manifest,
            clientCas,
            seederList,
            fallbackHostUrl: $"http://127.0.0.1:{hostPort}",
            progressCallback: progress => latestProgress = progress,
            maximumConcurrency: 3);

        Assert.That(success, Is.True);
        Assert.That(latestProgress, Is.EqualTo(1.0f));

        foreach (string hash in createdHashes)
        {
            Assert.That(clientCas.HasAsset(hash), Is.True, $"Client missing hash: {hash}");
            byte[]? clientBytes = clientCas.GetAssetBytes(hash);
            Assert.That(clientBytes, Is.Not.Null);
        }
    }

    [Test]
    public async Task Scenario2_HostPublishesManifestToSeedNode_RequiresAdminBypass_AndUploadsMissingAssets()
    {
        string serverStorageDir = Path.Combine(_scenarioRoot, "server_cas");
        string hostStorageDir = Path.Combine(_scenarioRoot, "host_cas");

        var (adminPriv, adminPub) = AdminBypassAuth.GenerateAdminKeyPair();

        var serverCas = new ContentAddressableStorage(serverStorageDir);
        var hostCas = new ContentAddressableStorage(hostStorageDir);

        int serverPort = GetAvailablePort();
        var seedNodeServer = new DistributionServer(serverCas, "seed_node_server", 100, null, adminPub, greenlightChecker: null);
        seedNodeServer.Start(serverPort);
        _runningServers.Add(seedNodeServer);

        string serverUrl = $"http://127.0.0.1:{serverPort}";
        var client = new DistributionClient(serverUrl);

        var manifest = new MapManifest
        {
            MapName = "custom_map",
            Author = "AuthorA",
            Version = "1.0.0"
        };

        var assetList = new List<(byte[] Bytes, string Hash, string Ext)>();
        for (int i = 0; i < 4; i++)
        {
            byte[] bytes = Encoding.UTF8.GetBytes($"Custom map asset data {i}");
            string h = RealmMetadataHelper.ComputeBlake3(bytes, ".bin");
            hostCas.StoreAsset(bytes, ".bin");
            manifest.Files[$"data_{i}.bin"] = $"{h}.bin";
            assetList.Add((bytes, ContentAddressableStorage.NormalizeBlake3Hash(h), ".bin"));
        }

        var unauthPublishResult = await client.PublishManifestAsync(manifest);
        Assert.That(unauthPublishResult.Success, Is.False, "Expected rejection for non-greenlit map without token.");

        string bypassToken = AdminBypassAuth.CreateBypassToken(adminPriv, manifest.MapName, manifest.Version);
        var authPublishResult = await client.PublishManifestAsync(manifest, bypassToken);

        Assert.That(authPublishResult.Success, Is.True, $"Publish failed: {authPublishResult.Message}");
        Assert.That(authPublishResult.MissingAssetHashes.Count, Is.EqualTo(4));

        foreach (var asset in assetList)
        {
            var uploadResult = await client.UploadAssetAsync(serverUrl, asset.Bytes, asset.Ext);
            Assert.That(uploadResult.Success, Is.True);
            Assert.That(uploadResult.Deduplicated, Is.False);
        }

        foreach (var asset in assetList)
        {
            Assert.That(serverCas.HasAsset(asset.Hash), Is.True);
        }

        var secondPublishResult = await client.PublishManifestAsync(manifest, bypassToken);
        Assert.That(secondPublishResult.Success, Is.True);
        Assert.That(secondPublishResult.MissingAssetHashes.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task Scenario3_ClientDownloadsManifestAndAssetsFromShardedSeeders()
    {
        string seeder1StorageDir = Path.Combine(_scenarioRoot, "seeder1_cas");
        string seeder2StorageDir = Path.Combine(_scenarioRoot, "seeder2_cas");
        string clientStorageDir = Path.Combine(_scenarioRoot, "client_cas");

        var seeder1Cas = new ContentAddressableStorage(seeder1StorageDir);
        var seeder2Cas = new ContentAddressableStorage(seeder2StorageDir);
        var clientCas = new ContentAddressableStorage(clientStorageDir);

        string seeder1Id = "seeder_node_1";
        string seeder2Id = "seeder_node_2";

        int seeder1Port = GetAvailablePort();
        int seeder2Port = GetAvailablePort();

        var seeder1Server = new DistributionServer(seeder1Cas, seeder1Id, 100);
        seeder1Server.Start(seeder1Port);
        _runningServers.Add(seeder1Server);

        var seeder2Server = new DistributionServer(seeder2Cas, seeder2Id, 100);
        seeder2Server.Start(seeder2Port);
        _runningServers.Add(seeder2Server);

        var manifest = new MapManifest
        {
            MapName = "sharded_map",
            Author = "AuthorB",
            Version = "1.0.0"
        };

        var allHashes = new List<string>();
        for (int i = 0; i < 8; i++)
        {
            byte[] bytes = Encoding.UTF8.GetBytes($"Sharded file payload {i}");
            string h = RealmMetadataHelper.ComputeBlake3(bytes, ".bin");
            string normalized = ContentAddressableStorage.NormalizeBlake3Hash(h);

            if (i % 2 == 0)
            {
                seeder1Cas.StoreAsset(bytes, ".bin");
            }
            else
            {
                seeder2Cas.StoreAsset(bytes, ".bin");
            }

            manifest.Files[$"part_{i}.bin"] = $"{h}.bin";
            allHashes.Add(normalized);
        }

        var seedersList = new List<SeederNodeDto>
        {
            new SeederNodeDto { SeederId = seeder1Id, IP = "127.0.0.1", Port = seeder1Port, CapacityPercentage = 100 },
            new SeederNodeDto { SeederId = seeder2Id, IP = "127.0.0.1", Port = seeder2Port, CapacityPercentage = 100 }
        };

        var client = new DistributionClient($"http://127.0.0.1:{seeder1Port}");
        bool downloadSuccess = await client.DownloadMissingAssetsMultiThreadedAsync(
            manifest,
            clientCas,
            seedersList,
            maximumConcurrency: 4);

        Assert.That(downloadSuccess, Is.True);
        foreach (string hash in allHashes)
        {
            Assert.That(clientCas.HasAsset(hash), Is.True, $"Hash {hash} was not downloaded to client CAS.");
        }
    }

    [Test]
    public async Task Scenario4_SeedersPropagateFilesToEachOtherAutomatically()
    {
        string registryStorageDir = Path.Combine(_scenarioRoot, "reg_cas");
        string seeder1StorageDir = Path.Combine(_scenarioRoot, "s1_cas");
        string seeder2StorageDir = Path.Combine(_scenarioRoot, "s2_cas");

        var regCas = new ContentAddressableStorage(registryStorageDir);
        var s1Cas = new ContentAddressableStorage(seeder1StorageDir);
        var s2Cas = new ContentAddressableStorage(seeder2StorageDir);

        int regPort = GetAvailablePort();
        int s1Port = GetAvailablePort();
        int s2Port = GetAvailablePort();

        var regServer = new DistributionServer(regCas, "reg_server", 100);
        regServer.Start(regPort);
        _runningServers.Add(regServer);

        var s1Server = new DistributionServer(s1Cas, "seeder_1", 100);
        s1Server.Start(s1Port);
        _runningServers.Add(s1Server);

        var s2Server = new DistributionServer(s2Cas, "seeder_2", 100);
        s2Server.Start(s2Port);
        _runningServers.Add(s2Server);

        byte[] s1FileBytes = Encoding.UTF8.GetBytes("Exclusive file originally only on Seeder 1");
        string s1Hash = s1Cas.StoreAsset(s1FileBytes, ".bin").Blake3Hash;

        byte[] s2FileBytes = Encoding.UTF8.GetBytes("Exclusive file originally only on Seeder 2");
        string s2Hash = s2Cas.StoreAsset(s2FileBytes, ".bin").Blake3Hash;

        Assert.That(s1Cas.HasAsset(s1Hash), Is.True);
        Assert.That(s1Cas.HasAsset(s2Hash), Is.False);

        Assert.That(s2Cas.HasAsset(s2Hash), Is.True);
        Assert.That(s2Cas.HasAsset(s1Hash), Is.False);

        string regUrl = $"http://127.0.0.1:{regPort}";
        using var httpClient = new HttpClient();

        var seeder1Engine = new SeederPropagationEngine("seeder_1", 100, s1Cas, regUrl, httpClient);
        var seeder2Engine = new SeederPropagationEngine("seeder_2", 100, s2Cas, regUrl, httpClient);

        int s1Downloaded = await seeder1Engine.RunPropagationCycleAsync();
        int s2Downloaded = await seeder2Engine.RunPropagationCycleAsync();

        bool s1HasS2Asset = await PullDirectlyFromPeerAsync(s1Cas, "127.0.0.1", s2Port, s2Hash);
        bool s2HasS1Asset = await PullDirectlyFromPeerAsync(s2Cas, "127.0.0.1", s1Port, s1Hash);

        Assert.That(s1HasS2Asset, Is.True);
        Assert.That(s2HasS1Asset, Is.True);

        Assert.That(s1Cas.HasAsset(s2Hash), Is.True);
        Assert.That(s2Cas.HasAsset(s1Hash), Is.True);
    }

    [Test]
    public async Task Scenario5_FullAssetPackage_EndToEndDistribution()
    {
        string assetPackageDirectory = Directory.Exists(@"C:\temp\Asset_Pack") ? @"C:\temp\Asset_Pack" : @"C:\temp\asset_package";
        string manifestPath = Path.Combine(assetPackageDirectory, "manifest.json");

        if (!Directory.Exists(assetPackageDirectory) || !File.Exists(manifestPath))
        {
            Assert.Ignore("Asset package directory or manifest.json not found.");
            return;
        }

        var manifest = MapManifest.LoadFromFile(manifestPath);
        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest!.Files.Count, Is.GreaterThan(70));

        string hostStorageDir = Path.Combine(_scenarioRoot, "host_cas");
        string serverStorageDir = Path.Combine(_scenarioRoot, "server_cas");
        string clientStorageDir = Path.Combine(_scenarioRoot, "client_cas");

        var hostCas = new ContentAddressableStorage(hostStorageDir);
        var serverCas = new ContentAddressableStorage(serverStorageDir);
        var clientCas = new ContentAddressableStorage(clientStorageDir);

        var (adminPriv, adminPub) = AdminBypassAuth.GenerateAdminKeyPair();

        int serverPort = GetAvailablePort();
        var seedNodeServer = new DistributionServer(serverCas, "seed_node_server", 100, null, adminPub, null);
        seedNodeServer.Start(serverPort);
        _runningServers.Add(seedNodeServer);

        string serverUrl = $"http://127.0.0.1:{serverPort}";
        var hostClient = new DistributionClient(serverUrl);

        string bypassToken = AdminBypassAuth.CreateBypassToken(adminPriv, manifest.MapName, manifest.Version);
        var publishResult = await hostClient.PublishManifestAsync(manifest, bypassToken);

        Assert.That(publishResult.Success, Is.True, $"Publishing asset_package failed: {publishResult.Message}");
        Assert.That(publishResult.MissingAssetHashes.Count, Is.EqualTo(manifest.Files.Count));

        foreach (var filePair in manifest.Files)
        {
            string relativePath = filePair.Key;
            string assetKey = filePair.Value;
            string fullSourcePath = Path.Combine(assetPackageDirectory, relativePath);

            if (File.Exists(fullSourcePath))
            {
                byte[] bytes = await File.ReadAllBytesAsync(fullSourcePath);
                string ext = Path.GetExtension(fullSourcePath);
                string? meta = RealmMetadataHelper.ExtractMetadata(fullSourcePath);

                var uploadResult = await hostClient.UploadAssetAsync(serverUrl, bytes, ext, meta);
                Assert.That(uploadResult.Success, Is.True, $"Failed uploading asset {relativePath}: {uploadResult.Message}");
            }
        }

        var secondPublishResult = await hostClient.PublishManifestAsync(manifest, bypassToken);
        Assert.That(secondPublishResult.Success, Is.True);
        Assert.That(secondPublishResult.MissingAssetHashes.Count, Is.EqualTo(0));

        var discoveryClient = new DistributionClient(serverUrl);
        var seedersList = new List<SeederNodeDto>
        {
            new SeederNodeDto { SeederId = "seed_node_server", IP = "127.0.0.1", Port = serverPort, CapacityPercentage = 100 }
        };

        float finalProgress = 0f;
        bool clientDownloadSuccess = await discoveryClient.DownloadMissingAssetsMultiThreadedAsync(
            manifest,
            clientCas,
            seedersList,
            fallbackHostUrl: serverUrl,
            progressCallback: p => finalProgress = p,
            maximumConcurrency: 6);

        Assert.That(clientDownloadSuccess, Is.True);
        Assert.That(finalProgress, Is.EqualTo(1.0f));

        foreach (var filePair in manifest.Files)
        {
            string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(filePair.Value);
            Assert.That(clientCas.HasAsset(normalizedHash), Is.True, $"Client is missing downloaded asset {filePair.Key} ({normalizedHash})");
        }

        Assert.That(Directory.Exists(clientCas.SidecarCacheDirectory), Is.True);
        clientCas.DeleteSidecarCache();
        clientCas.RebuildSidecarCache();

        foreach (var filePair in manifest.Files)
        {
            string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(filePair.Value);
            Assert.That(clientCas.HasAsset(normalizedHash), Is.True);
        }
    }

    private async Task<bool> PullDirectlyFromPeerAsync(ContentAddressableStorage storage, string ip, int port, string hash)
    {
        using var client = new HttpClient();
        string url = $"http://{ip}:{port}/api/assets/{hash}";
        var resp = await client.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return false;
        byte[] bytes = await resp.Content.ReadAsByteArrayAsync();
        var res = storage.StoreAsset(bytes, ".bin");
        return res.Success;
    }

    [Test]
    public async Task Scenario6_BloomFilterSidecarHeaderSync_MergesHeadersBetweenSeeders()
    {
        string seeder1StorageDir = Path.Combine(_scenarioRoot, "s1_cas");
        string seeder2StorageDir = Path.Combine(_scenarioRoot, "s2_cas");

        var s1Cas = new ContentAddressableStorage(seeder1StorageDir);
        var s2Cas = new ContentAddressableStorage(seeder2StorageDir);

        int s1Port = GetAvailablePort();
        int s2Port = GetAvailablePort();

        var s1Server = new DistributionServer(s1Cas, "seeder_1", 100);
        s1Server.Start(s1Port);
        _runningServers.Add(s1Server);

        var s2Server = new DistributionServer(s2Cas, "seeder_2", 100);
        s2Server.Start(s2Port);
        _runningServers.Add(s2Server);

        byte[] sharedFileBytes = Encoding.UTF8.GetBytes("Shared canonical asset payload across seeders");
        string s1Meta = "{\"tags\":[\"grass\"],\"author\":\"Alice\"}";
        string s2Meta = "{\"tags\":[\"rock\"],\"description\":\"Terrain\"}";

        var s1StoreResult = s1Cas.StoreAsset(sharedFileBytes, ".bin", s1Meta);
        var s2StoreResult = s2Cas.StoreAsset(sharedFileBytes, ".bin", s2Meta);

        Assert.That(s1StoreResult.Success, Is.True);
        Assert.That(s2StoreResult.Success, Is.True);
        Assert.That(s1StoreResult.Blake3Hash, Is.EqualTo(s2StoreResult.Blake3Hash));

        string assetHash = s1StoreResult.Blake3Hash;

        using var httpClient = new HttpClient();
        var s1Engine = new SeederPropagationEngine("seeder_1", 100, s1Cas, $"http://127.0.0.1:{s1Port}", httpClient);

        var s2BloomFilter = await s1Engine.FetchPeerHeaderBloomFilterAsync("127.0.0.1", s2Port);
        Assert.That(s2BloomFilter, Is.Not.Null);

        string s1HeaderKey = BloomFilter.CreateHeaderKey(assetHash, s1Cas.GetAssetMetadata(assetHash));
        Assert.That(s2BloomFilter!.Contains(s1HeaderKey), Is.False);

        var peerList = new List<SeederNodeDto>
        {
            new SeederNodeDto { SeederId = "seeder_2", IP = "127.0.0.1", Port = s2Port, CapacityPercentage = 100 }
        };

        int syncedCount = await s1Engine.RunBloomFilterHeaderSyncWithSeedersAsync(peerList);
        Assert.That(syncedCount, Is.GreaterThanOrEqualTo(1));

        string? s1FinalMeta = s1Cas.GetAssetMetadata(assetHash);
        string? s2FinalMeta = s2Cas.GetAssetMetadata(assetHash);

        Assert.That(s1FinalMeta, Is.Not.Null);
        Assert.That(s2FinalMeta, Is.Not.Null);

        Assert.That(s1FinalMeta, Does.Contain("grass"));
        Assert.That(s1FinalMeta, Does.Contain("rock"));
        Assert.That(s1FinalMeta, Does.Contain("Alice"));
        Assert.That(s1FinalMeta, Does.Contain("Terrain"));

        Assert.That(s2FinalMeta, Does.Contain("grass"));
        Assert.That(s2FinalMeta, Does.Contain("rock"));
        Assert.That(s2FinalMeta, Does.Contain("Alice"));
        Assert.That(s2FinalMeta, Does.Contain("Terrain"));

        var s2UpdatedBloomFilter = await s1Engine.FetchPeerHeaderBloomFilterAsync("127.0.0.1", s2Port);
        Assert.That(s2UpdatedBloomFilter, Is.Not.Null);

        string s1FinalHeaderKey = BloomFilter.CreateHeaderKey(assetHash, s1FinalMeta);
        Assert.That(s2UpdatedBloomFilter!.Contains(s1FinalHeaderKey), Is.True);

        int secondCycleSynced = await s1Engine.RunBloomFilterHeaderSyncWithSeedersAsync(peerList);
        Assert.That(secondCycleSynced, Is.EqualTo(0));
    }

    [Test]
    public async Task Scenario7_BloomFilterHeaderSync_AuthorSignatureOverwritesExistingHeaders()
    {
        string seeder1StorageDir = Path.Combine(_scenarioRoot, "s1_cas");
        string seeder2StorageDir = Path.Combine(_scenarioRoot, "s2_cas");

        var s1Cas = new ContentAddressableStorage(seeder1StorageDir);
        var s2Cas = new ContentAddressableStorage(seeder2StorageDir);

        int s1Port = GetAvailablePort();
        int s2Port = GetAvailablePort();

        var s1Server = new DistributionServer(s1Cas, "seeder_1", 100);
        s1Server.Start(s1Port);
        _runningServers.Add(s1Server);

        var s2Server = new DistributionServer(s2Cas, "seeder_2", 100);
        s2Server.Start(s2Port);
        _runningServers.Add(s2Server);

        var (authorPriv, authorPub) = AuthorSignatureHelper.GenerateKeyPair();
        byte[] fileBytes = Encoding.UTF8.GetBytes("Author signature verified payload");
        string assetHash = ContentAddressableStorage.NormalizeBlake3Hash(RealmMetadataHelper.ComputeBlake3(fileBytes, ".bin"));
        string signature = AuthorSignatureHelper.SignMessage(authorPriv, assetHash);

        string initialMeta = "{\"tags\":[\"old_tag\"],\"version\":\"1.0\"}";
        s2Cas.StoreAsset(fileBytes, ".bin", initialMeta, authorPub, signature);

        string updatedMeta = "{\"tags\":[\"new_author_tag\"],\"version\":\"2.0\"}";
        string updatedSignature = AuthorSignatureHelper.SignMessage(authorPriv, assetHash);
        s1Cas.StoreAsset(fileBytes, ".bin", updatedMeta, authorPub, updatedSignature);

        using var httpClient = new HttpClient();
        var s1Engine = new SeederPropagationEngine("seeder_1", 100, s1Cas, $"http://127.0.0.1:{s1Port}", httpClient);

        var peerList = new List<SeederNodeDto>
        {
            new SeederNodeDto { SeederId = "seeder_2", IP = "127.0.0.1", Port = s2Port, CapacityPercentage = 100 }
        };

        int synced = await s1Engine.RunBloomFilterHeaderSyncWithSeedersAsync(peerList);
        Assert.That(synced, Is.GreaterThanOrEqualTo(1));

        string? s2UpdatedMeta = s2Cas.GetAssetMetadata(assetHash);
        Assert.That(s2UpdatedMeta, Is.Not.Null);
        Assert.That(s2UpdatedMeta, Does.Contain("new_author_tag"));
        Assert.That(s2UpdatedMeta, Does.Contain("2.0"));
        Assert.That(s2UpdatedMeta, Does.Not.Contain("old_tag"));
    }

    [Test]
    public async Task Scenario8_AssetPack_PublishLifecycle_ReplicationAndDiagnostics()
    {
        string assetPackageDirectory = Directory.Exists(@"C:\temp\Asset_Pack") ? @"C:\temp\Asset_Pack" : @"C:\temp\asset_package";
        bool isActualDirectory = Directory.Exists(assetPackageDirectory);

        string testWorkspace = isActualDirectory
            ? assetPackageDirectory
            : Path.Combine(_scenarioRoot, "mock_asset_pack");

        if (!isActualDirectory)
        {
            Directory.CreateDirectory(testWorkspace);
            Directory.CreateDirectory(Path.Combine(testWorkspace, "Assets", "models", "props"));
            Directory.CreateDirectory(Path.Combine(testWorkspace, "Assets", "textures"));

            File.WriteAllText(Path.Combine(testWorkspace, "metadata.json"), "{\"MapTitle\":\"Asset_Pack\",\"MapVersion\":\"1.0.0\"}");
            File.WriteAllText(Path.Combine(testWorkspace, "terrain.json"), "{}");
            File.WriteAllBytes(Path.Combine(testWorkspace, "Assets", "models", "props", "rock.rmesh"), Encoding.UTF8.GetBytes("sample_rock_rmesh_bytes"));
            File.WriteAllBytes(Path.Combine(testWorkspace, "Assets", "textures", "stone.rtex"), Encoding.UTF8.GetBytes("sample_stone_rtex_bytes"));
            File.WriteAllBytes(Path.Combine(testWorkspace, "test ASSET_PACK_1.0.1_4cc8.7z"), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(testWorkspace, "empty_dummy.bin"), Array.Empty<byte>());
        }

        Console.WriteLine($"[DIAGNOSTICS] Testing workspace directory: {testWorkspace}");
        var allDiskFiles = Directory.GetFiles(testWorkspace, "*.*", SearchOption.AllDirectories);
        Console.WriteLine($"[DIAGNOSTICS] Total files on disk: {allDiskFiles.Length}");

        var zeroByteFiles = allDiskFiles.Where(f => new FileInfo(f).Length == 0).ToList();
        Console.WriteLine($"[DIAGNOSTICS] Zero-byte files found on disk ({zeroByteFiles.Count}):");
        foreach (var zf in zeroByteFiles)
        {
            Console.WriteLine($"  - {Path.GetRelativePath(testWorkspace, zf)} (0 bytes)");
        }

        var (authorshipKey, keyData, keyPath, _) = AuthorshipKeyHelper.GetOrGenerateKeyInfo(defaultUserName: "Realm");
        string authorPublicKey = Convert.ToBase64String(authorshipKey.PublicKey.Export(NSec.Cryptography.KeyBlobFormat.RawPublicKey));
        Console.WriteLine($"[DIAGNOSTICS] Authorship Key Path: {keyPath}");
        Console.WriteLine($"[DIAGNOSTICS] Authorship Key UserName: {keyData.UserName}, PublicKey: {authorPublicKey}");

        var manifest = MapManifest.CreateFromDirectory(
            testWorkspace,
            "Asset_Pack",
            keyData.UserName ?? "Realm",
            "1.0.0",
            "Asset Pack",
            new List<string> { "AssetPack" }
        );

        Console.WriteLine($"[DIAGNOSTICS] Files indexed into manifest: {manifest.Files.Count}");
        foreach (var zf in zeroByteFiles)
        {
            string rel = Path.GetRelativePath(testWorkspace, zf).Replace('\\', '/');
            Assert.That(manifest.Files.ContainsKey(rel), Is.False, $"Zero-byte file '{rel}' must NOT be included in manifest.Files.");
        }

        foreach (var filePair in manifest.Files)
        {
            string relPath = filePair.Key;
            string assetKey = filePair.Value;
            string fullPath = Path.Combine(testWorkspace, relPath);

            Assert.That(File.Exists(fullPath), Is.True, $"Manifest file '{relPath}' must exist on disk.");
            long length = new FileInfo(fullPath).Length;
            Assert.That(length, Is.GreaterThan(0), $"Manifest file '{relPath}' must have non-zero size.");
            Assert.That(relPath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase), Is.False, $"Archive file '{relPath}' must not be in manifest.");
            Assert.That(relPath.EndsWith(".rkey", StringComparison.OrdinalIgnoreCase), Is.False, $"Key file '{relPath}' must not be in manifest.");
        }

        string serverCasDir = Path.Combine(_scenarioRoot, "server_cas");
        var serverCas = new ContentAddressableStorage(serverCasDir);
        int serverPort = GetAvailablePort();

        var (adminPriv, adminPub) = AdminBypassAuth.GenerateAdminKeyPair();
        var server = new DistributionServer(serverCas, "registry_seed_node", 100, null, adminPub, null);
        server.Start(serverPort);
        _runningServers.Add(server);

        string serverUrl = $"http://127.0.0.1:{serverPort}";
        var client = new DistributionClient(serverUrl);

        var testManifest = manifest;
        if (manifest.Files.Count > 50)
        {
            testManifest = new MapManifest
            {
                MapName = manifest.MapName,
                Author = manifest.Author,
                Version = manifest.Version,
                Description = manifest.Description,
                Tags = manifest.Tags
            };
            foreach (var pair in manifest.Files.Take(50))
            {
                testManifest.Files[pair.Key] = pair.Value;
                testManifest.FileSizes![pair.Key] = manifest.FileSizes!.TryGetValue(pair.Key, out long sz) ? sz : 0;
            }
        }

        string bypassToken = AdminBypassAuth.CreateBypassToken(adminPriv, testManifest.MapName, testManifest.Version);
        var initialPublishResult = await client.PublishManifestAsync(testManifest, bypassToken);

        Console.WriteLine($"[DIAGNOSTICS] Initial publish initiate result: Success={initialPublishResult.Success}, MissingHashes={initialPublishResult.MissingAssetHashes.Count}");
        Assert.That(initialPublishResult.Success, Is.True, $"Initial publish failed: {initialPublishResult.Message}");
        Assert.That(initialPublishResult.MissingAssetHashes.Count, Is.EqualTo(testManifest.Files.Count));

        var hashToRelativePath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in testManifest.Files)
        {
            string norm = ContentAddressableStorage.NormalizeBlake3Hash(pair.Value);
            hashToRelativePath[norm] = pair.Key;
            hashToRelativePath[pair.Value] = pair.Key;
        }

        var (uploadSuccess, failedAsset, errorMsg) = await client.UploadMissingAssetsMultiThreadedAsync(
            testWorkspace,
            initialPublishResult.MissingAssetHashes,
            hashToRelativePath,
            keyData.UserName ?? "Realm",
            authorPublicKey,
            authorshipKey,
            testManifest.MapName,
            testManifest.Version,
            sessionId: null,
            maximumConcurrency: 8
        );

        Console.WriteLine($"[DIAGNOSTICS] Upload result: Success={uploadSuccess}, FailedAsset={failedAsset}, ErrorMsg={errorMsg}");
        Assert.That(uploadSuccess, Is.True, $"UploadMissingAssets failed for '{failedAsset}': {errorMsg}");

        var finalizePublishResult = await client.PublishManifestAsync(testManifest, bypassToken);
        Console.WriteLine($"[DIAGNOSTICS] Finalize publish result: Success={finalizePublishResult.Success}, MissingHashes={finalizePublishResult.MissingAssetHashes.Count}, Message={finalizePublishResult.Message}");

        Assert.That(finalizePublishResult.Success, Is.True, $"Finalize publish failed: {finalizePublishResult.Message}");
        Assert.That(finalizePublishResult.MissingAssetHashes.Count, Is.EqualTo(0), $"Expected 0 missing assets on finalize but got: {string.Join(", ", finalizePublishResult.MissingAssetHashes)}");
    }
}
