using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NSec.Cryptography;
using Realm.Client.Network;
using Realm.Client.Utils;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;

namespace Realm.Client.Services.Publishing;

/// <summary>
/// Turns a map workspace into everything the registry needs to verify a publish: the engine stamp on
/// metadata.json, the manifest, its signature, and the hash-to-path map the uploader and the error messages
/// look names up in. It builds artefacts only - the modal, the progress bar and the console belong to the flow.
/// </summary>
public static class PublishPackageBuilder
{
	public readonly record struct Package(
		string ManifestJsonContent,
		string ManifestSigBase64,
		string PubKeyBase64,
		List<string> ReferencedHashes,
		Dictionary<string, string> HashToRelativePath);

	/// <summary>
	/// Writes metadata.json's engine version, signs the metadata, then creates, signs and stores the manifest
	/// plus the workspace files it lists.
	/// </summary>
	/// <param name="signMetadataAsync">
	/// The signing step, which reads the selected authorship key and reports through the editor's console, so it
	/// stays with the caller rather than being duplicated here.
	/// </param>
	public static async Task<Package> BuildAsync(
		string workspace, string metadataPath, string mapTitle, string mapVersion, string authorName,
		Key authorshipKey, bool isTestVersion, Func<Task> signMetadataAsync)
	{
		string pubKeyBase64 = Convert.ToBase64String(authorshipKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));

		MapWorkspaceService.NormalizeMetadataTextureEntries(workspace);

		if (File.Exists(metadataPath))
		{
			try
			{
				var metaDoc = JsonNode.Parse(File.ReadAllText(metadataPath)) as JsonObject;
				if (metaDoc != null)
				{
					metaDoc["EngineVersion"] = RealmVersion.GameBinaryVersion;
					MapJsonFormatter.SaveFormattedJson(metadataPath, metaDoc);
				}
			}
			catch
			{
			}

			GameContext.EditorService.LastMetadataSyncTime = GameContext.EditorService.GetLastWriteTimeSafe(metadataPath);
		}


		await signMetadataAsync();

		var manifest = MapManifest.CreateFromDirectory(workspace, mapTitle, authorName, mapVersion);
		manifest.Attribution_SourceMap = GameContext.MapAttributionService.CollectSortedSourceMapNames(workspace).ToList();
		manifest.IsTestVersion = isTestVersion;
		string manifestJsonPath = Path.Combine(workspace, "manifest.json");
		string manifestJsonContent = manifest.ToJson();
		File.WriteAllText(manifestJsonPath, manifestJsonContent);

		byte[] manifestBytes = Encoding.UTF8.GetBytes(manifestJsonContent);
		string manifestBlake3 = RealmMetadataHelper.ComputeBlake3(manifestBytes, ".json");
		string manifestHash = $"{manifestBlake3}.json";
		byte[] manifestHashBytes = Encoding.UTF8.GetBytes(manifestHash);
		byte[] manifestSigBytes = SignatureAlgorithm.Ed25519.Sign(authorshipKey, manifestHashBytes);
		string manifestSigBase64 = Convert.ToBase64String(manifestSigBytes);

		MapAssetManager.Storage.StoreAsset(manifestBytes, ".json", null, pubKeyBase64, manifestSigBase64, precomputedBlake3: manifestBlake3);

		var hashToRelativePath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var pair in manifest.Files)
		{
			string norm = ContentAddressableStorage.NormalizeBlake3Hash(pair.Value);
			hashToRelativePath[norm] = pair.Key;
			hashToRelativePath[pair.Value] = pair.Key;

			string localPath = Path.Combine(workspace, pair.Key);
			if (File.Exists(localPath))
			{
				byte[] fileBytes = File.ReadAllBytes(localPath);
				string ext = Path.GetExtension(localPath).ToLowerInvariant();
				MapAssetManager.Storage.StoreAsset(fileBytes, ext, null, pubKeyBase64, null, precomputedBlake3: norm);
			}
		}

		hashToRelativePath[manifestBlake3] = "manifest.json";
		hashToRelativePath[manifestHash] = "manifest.json";
		hashToRelativePath["manifest.json"] = "manifest.json";

		if (File.Exists(metadataPath))
		{
			byte[] metaBytes = File.ReadAllBytes(metadataPath);
			string metaBlake3 = RealmMetadataHelper.ComputeBlake3(metaBytes, ".json");
			string metaNorm = ContentAddressableStorage.NormalizeBlake3Hash(metaBlake3);
			string metaAssetKey = $"{metaBlake3}.json";

			hashToRelativePath[metaBlake3] = "metadata.json";
			hashToRelativePath[metaNorm] = "metadata.json";
			hashToRelativePath[metaAssetKey] = "metadata.json";
			hashToRelativePath["metadata.json"] = "metadata.json";
			MapAssetManager.Storage.StoreAsset(metaBytes, ".json", null, pubKeyBase64, null, precomputedBlake3: metaNorm);
		}

		return new Package(
			manifestJsonContent,
			manifestSigBase64,
			pubKeyBase64,
			manifest.Files.Values.ToList(),
			hashToRelativePath);
	}
}

/// <summary>The greenlight review state the registry reports for one map and author key.</summary>
public readonly record struct GreenlightStatus(bool IsGreenlit, int VerifiedGoodReviews, int TotalReviews, double AverageRating);

/// <summary>
/// Asks the registry whether a map may be published yet. A registry that cannot answer is not a refusal, so a
/// failed lookup returns no status and a warning line for the caller to log.
/// </summary>
public static class PublishGreenlightProbe
{
	public static async Task<(GreenlightStatus? Status, string? Warning)> CheckAsync(
		string seedServerUrl, string mapTitle, string mapVersion, Key authorshipKey)
	{
		try
		{
			using var checkClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
			string creatorPubKey = Convert.ToBase64String(authorshipKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));

			string statusKey = !string.IsNullOrEmpty(creatorPubKey)
				? $"{Uri.EscapeDataString(mapTitle)}_{Uri.EscapeDataString(mapVersion)}_{Uri.EscapeDataString(creatorPubKey)}"
				: $"{Uri.EscapeDataString(mapTitle)}_{Uri.EscapeDataString(mapVersion)}";
			string statusUrl = $"{seedServerUrl.TrimEnd('/')}/api/maps/greenlight_status/{statusKey}";
			var statusRes = await checkClient.GetAsync(statusUrl);
			if (!statusRes.IsSuccessStatusCode) return (null, null);

			string statusJson = await statusRes.Content.ReadAsStringAsync();
			var statusNode = JsonNode.Parse(statusJson);
			return (new GreenlightStatus(
				statusNode?["isGreenlit"]?.GetValue<bool>() ?? false,
				statusNode?["verifiedGoodReviewsCount"]?.GetValue<int>() ?? 0,
				statusNode?["totalReviewsCount"]?.GetValue<int>() ?? 0,
				statusNode?["averageRating"]?.GetValue<double>() ?? 0.0), null);
		}
		catch (Exception ex)
		{
			return (null, $"[WARN] Could not verify greenlight status: {ex.Message}");
		}
	}
}
