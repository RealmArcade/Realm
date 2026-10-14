using System.Net;
using System.Net.Sockets;
using System.Text;
using NSec.Cryptography;
using NUnit.Framework;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;

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
}