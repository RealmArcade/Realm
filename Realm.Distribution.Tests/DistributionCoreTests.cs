using NUnit.Framework;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using System.Text;

namespace Realm.Distribution.Tests;

[TestFixture]
public class DistributionCoreTests
{
    private string _testDirectory = string.Empty;

    [SetUp]
    public void Setup()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "RealmDistTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
        catch
        {
        }
    }

    [Test]
    public void GenerateAndSaveAdminKeys()
    {
        var (privateKey, publicKey) = AdminBypassAuth.GenerateAdminKeyPair();
        Assert.That(privateKey, Is.Not.Null.And.Not.Empty);
        Assert.That(publicKey, Is.Not.Null.And.Not.Empty);

        string privateKeyFilePath = Path.Combine(_testDirectory, "admin_private.key");
        File.WriteAllText(privateKeyFilePath, privateKey);

        string appSettingsPath = Path.Combine(_testDirectory, "appsettings.json");
        string appSettingsJson = $"{{\n  \"AdminPublicKey\": \"{publicKey}\",\n  \"StorageDirectory\": \".data/cas\",\n  \"CapacityPercentage\": 100\n}}\n";
        File.WriteAllText(appSettingsPath, appSettingsJson);

        string bypassToken = AdminBypassAuth.CreateBypassToken(privateKey, "asset_package", "1.0.0");
        bool verified = AdminBypassAuth.VerifyBypassToken(publicKey, "asset_package", "1.0.0", bypassToken);
        Assert.That(verified, Is.True);
    }

    [Test]
    public void GenerateAssetPackageManifest()
    {
        string assetPackageDirectory = Directory.Exists(@"C:\temp\Asset_Pack") ? @"C:\temp\Asset_Pack" : @"C:\temp\asset_package";
        if (!Directory.Exists(assetPackageDirectory))
        {
            Assert.Ignore("Asset package directory does not exist on this machine.");
            return;
        }

        var manifest = MapManifest.CreateFromDirectory(
            assetPackageDirectory,
            "Asset_Pack",
            "Realm",
            "1.0.0",
            "Asset Package containing categorized 2D, 3D, animations, and audio dependencies.",
            new List<string> { "AssetPack" });

        Assert.That(manifest.Files.Count, Is.GreaterThan(70));
        string manifestPath = Path.Combine(assetPackageDirectory, "manifest.json");
        manifest.SaveToFile(manifestPath);

        Assert.That(File.Exists(manifestPath), Is.True);
        var loadedManifest = MapManifest.LoadFromFile(manifestPath);
        Assert.That(loadedManifest, Is.Not.Null);
        Assert.That(loadedManifest!.Files.Count, Is.EqualTo(manifest.Files.Count));
        Assert.That(loadedManifest.Tags, Contains.Item("AssetPack"));
    }

    [Test]
    public void MapManifest_Excludes_Archives_Backups_Keys_And_ZeroByteFiles()
    {
        string mockDir = Path.Combine(_testDirectory, "mock_workspace");
        Directory.CreateDirectory(mockDir);
        Directory.CreateDirectory(Path.Combine(mockDir, "Assets", "models"));
        Directory.CreateDirectory(Path.Combine(mockDir, ".backups"));

        File.WriteAllText(Path.Combine(mockDir, "terrain.json"), "{}");
        File.WriteAllBytes(Path.Combine(mockDir, "Assets", "models", "tree.rmesh"), Encoding.UTF8.GetBytes("tree_mesh_data"));
        File.WriteAllBytes(Path.Combine(mockDir, "archive.7z"), Encoding.UTF8.GetBytes("7z_data"));
        File.WriteAllBytes(Path.Combine(mockDir, "pack.zip"), Encoding.UTF8.GetBytes("zip_data"));
        File.WriteAllBytes(Path.Combine(mockDir, "backup.bak"), Encoding.UTF8.GetBytes("bak_data"));
        File.WriteAllBytes(Path.Combine(mockDir, "authorship_key_DO-NOT-SHARE.rkey"), Encoding.UTF8.GetBytes("key_data"));
        File.WriteAllBytes(Path.Combine(mockDir, "authorship_key.pem"), Encoding.UTF8.GetBytes("pem_data"));
        File.WriteAllBytes(Path.Combine(mockDir, ".backups", "old_terrain.json"), Encoding.UTF8.GetBytes("old_data"));
        File.WriteAllBytes(Path.Combine(mockDir, "empty.bin"), Array.Empty<byte>());

        var manifest = MapManifest.CreateFromDirectory(mockDir, "test_map", "Author", "1.0.0");

        Assert.That(manifest.Files.ContainsKey("terrain.json"), Is.True);
        Assert.That(manifest.Files.ContainsKey("Assets/models/tree.rmesh"), Is.True);
        Assert.That(manifest.Files.ContainsKey("archive.7z"), Is.False);
        Assert.That(manifest.Files.ContainsKey("pack.zip"), Is.False);
        Assert.That(manifest.Files.ContainsKey("backup.bak"), Is.False);
        Assert.That(manifest.Files.ContainsKey("authorship_key_DO-NOT-SHARE.rkey"), Is.False);
        Assert.That(manifest.Files.ContainsKey("authorship_key.pem"), Is.False);
        Assert.That(manifest.Files.ContainsKey(".backups/old_terrain.json"), Is.False);
        Assert.That(manifest.Files.ContainsKey("empty.bin"), Is.False);
    }

    [Test]
    public void ShardingAlgorithm_PercentageDistribution()
    {
        string seeder100 = "seeder_full_100";
        string seeder50 = "seeder_half_50";
        string seeder1 = "seeder_tiny_1";

        int count100 = 0;
        int count50 = 0;
        int count1 = 0;

        int totalSamples = 1000;
        for (int i = 0; i < totalSamples; i++)
        {
            string fakeHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

            if (DistributionSharding.SeederAcceptsHash(seeder100, 100, fakeHash))
            {
                count100++;
            }

            if (DistributionSharding.SeederAcceptsHash(seeder50, 50, fakeHash))
            {
                count50++;
            }

            if (DistributionSharding.SeederAcceptsHash(seeder1, 1, fakeHash))
            {
                count1++;
            }
        }

        Assert.That(count100, Is.EqualTo(totalSamples));
        Assert.That(count50, Is.InRange(400, 600));
        Assert.That(count1, Is.InRange(1, 35));
    }

    [Test]
    public void ContentAddressableStorage_Deduplication_And_HeaderMerging()
    {
        var storage = new ContentAddressableStorage(_testDirectory);
        byte[] assetBytes = new byte[1024];
        new Random(42).NextBytes(assetBytes);

        var (authorPriv, authorPub) = AuthorSignatureHelper.GenerateKeyPair();
        var (otherPriv, otherPub) = AuthorSignatureHelper.GenerateKeyPair();

        string initialMetadata = "{\"tags\":[\"rock\",\"nature\"],\"brightness\":0.5}";
        var firstStore = storage.StoreAsset(assetBytes, ".glb", initialMetadata, authorPub, AuthorSignatureHelper.SignMessage(authorPriv, ContentAddressableStorage.NormalizeBlake3Hash(RealmMetadataHelper.ComputeBlake3(assetBytes, ".glb"))));
        Assert.That(firstStore.Success, Is.True);
        Assert.That(firstStore.Deduplicated, Is.False);

        string unauthorizedMetadata = "{\"tags\":[\"foliage\"],\"brightness\":0.9}";
        var secondStore = storage.StoreAsset(assetBytes, ".glb", unauthorizedMetadata, otherPub, AuthorSignatureHelper.SignMessage(otherPriv, firstStore.Blake3Hash));
        Assert.That(secondStore.Success, Is.True);
        Assert.That(secondStore.Deduplicated, Is.True);
        Assert.That(secondStore.Merged, Is.True);

        string? mergedMeta = storage.GetAssetMetadata(firstStore.Blake3Hash);
        Assert.That(mergedMeta, Is.Not.Null);
        Assert.That(mergedMeta!, Does.Contain("rock"));
        Assert.That(mergedMeta, Does.Contain("nature"));
        Assert.That(mergedMeta, Does.Contain("foliage"));
        Assert.That(mergedMeta, Does.Contain("0.5"));

        string authorizedUpdate = "{\"tags\":[\"custom\"],\"brightness\":1.0}";
        var thirdStore = storage.StoreAsset(assetBytes, ".glb", authorizedUpdate, authorPub, AuthorSignatureHelper.SignMessage(authorPriv, firstStore.Blake3Hash));
        Assert.That(thirdStore.Success, Is.True);
        Assert.That(thirdStore.Deduplicated, Is.True);
        Assert.That(thirdStore.Merged, Is.True);

        string? finalMeta = storage.GetAssetMetadata(firstStore.Blake3Hash);
        Assert.That(finalMeta, Is.Not.Null);
        Assert.That(finalMeta!, Does.Contain("custom"));
        Assert.That(finalMeta, Does.Contain("1"));
        Assert.That(finalMeta, Does.Not.Contain("rock"));
    }

    [Test]
    public void Storage_RejectsAssetsOver15MB()
    {
        var storage = new ContentAddressableStorage(_testDirectory);
        byte[] oversizedBytes = new byte[ContentAddressableStorage.MaximumAssetSizeBytes + 10];

        var result = storage.StoreAsset(oversizedBytes, ".glb");
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("maximum size limit"));
    }

    [Test]
    public async Task Isolated_RanimUpload_MatchesBlake3()
    {
        string filePath = @"C:\temp\Asset_Pack\Assets\animations\180 Turn W Briefcase - Female 180 Turn With Briefcase.ranim";
        if (!File.Exists(filePath))
        {
            filePath = @"C:\temp\asset_package\Animations\180 Turn W Briefcase - Female 180 Turn With Briefcase.ranim";
        }
        if (!File.Exists(filePath))
        {
            Assert.Ignore("File not found");
        }

        byte[] originalBytes = File.ReadAllBytes(filePath);
        string originalBlake3 = RealmMetadataHelper.ComputeBlake3(originalBytes, ".ranim");
        string? metadata = RealmMetadataHelper.ExtractMetadata(filePath);

        var serverCas = new ContentAddressableStorage(Path.Combine(_testDirectory, "server_cas"));
        var server = new DistributionServer(serverCas, "test_seeder", 100);
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        server.Start(port);

        try
        {
            var client = new DistributionClient($"http://127.0.0.1:{port}");
            var uploadWithMeta = await client.UploadAssetAsync($"http://127.0.0.1:{port}", originalBytes, ".ranim", metadata);

            Assert.That(uploadWithMeta.Success, Is.True);
            Assert.That(uploadWithMeta.Blake3Hash, Is.EqualTo(ContentAddressableStorage.NormalizeBlake3Hash(originalBlake3)));
            Assert.That(serverCas.HasAsset(originalBlake3), Is.True);
        }
        finally
        {
            server.Stop();
        }
    }

    [Test]
    public async Task DistributionClient_UploadAssetAsync_RejectsOversizedPayload()
    {
        var client = new DistributionClient("http://127.0.0.1:9999");
        byte[] oversizedBytes = new byte[ContentAddressableStorage.MaximumAssetSizeBytes + 512];

        var response = await client.UploadAssetAsync("http://127.0.0.1:9999", oversizedBytes, ".glb");
        Assert.That(response.Success, Is.False);
        Assert.That(response.Message, Does.Contain("exceeds maximum allowed size"));
    }

    [Test]
    public async Task DistributionClient_UploadMissingAssetsMultiThreadedAsync_RejectsOversizedFiles()
    {
        string mockWorkspace = Path.Combine(_testDirectory, "oversized_ws");
        Directory.CreateDirectory(mockWorkspace);
        string bigFilePath = Path.Combine(mockWorkspace, "huge_texture.png");

        byte[] fakeOversizedData = new byte[ContentAddressableStorage.MaximumAssetSizeBytes + 1024];
        await File.WriteAllBytesAsync(bigFilePath, fakeOversizedData);

        var (authorKey, keyData, _, _) = AuthorshipKeyHelper.GetOrGenerateKeyInfo(Path.Combine(_testDirectory, "keys"), "TestUser");
        string authorPub = Convert.ToBase64String(authorKey.PublicKey.Export(NSec.Cryptography.KeyBlobFormat.RawPublicKey));

        var client = new DistributionClient("http://127.0.0.1:9999");
        string fakeHash = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890";
        var missingHashes = new List<string> { fakeHash };
        var mapping = new Dictionary<string, string> { [fakeHash] = "huge_texture.png" };

        var (success, failedAsset, errorMsg) = await client.UploadMissingAssetsMultiThreadedAsync(
            mockWorkspace,
            missingHashes,
            mapping,
            "TestUser",
            authorPub,
            authorKey,
            "TestMap",
            "1.0.0"
        );

        Assert.That(success, Is.False);
        Assert.That(failedAsset, Is.EqualTo("huge_texture.png"));
        Assert.That(errorMsg, Does.Contain("exceeds maximum allowed size"));
    }
}

