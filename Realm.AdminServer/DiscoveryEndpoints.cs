using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using NSec.Cryptography;
using Realm.AdminServer.Accounts;
using Realm.AdminServer.Models;
using Realm.AdminServer.Services;
using Realm.Shared;
using Realm.Shared.Accounts;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using Realm.AdminServer.Helpers;

namespace Realm.AdminServer;

/// <summary>The public map discovery feed.</summary>
public static class DiscoveryEndpoints
{
	public static void MapDiscoveryEndpoints(WebApplication app, ServerSetup setup)
	{
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

				DiscoveryMapDto CreateDiscoveryMapDto(string compositeKey, string title, string version, string creator, string desc, string gen, string thumb, List<string> screens, List<string> fAndT, long bytes, string eVer, string mPlayers, bool greenlit, bool isTestVersion, List<string> attributionSourceMaps, MapStats? s)
				{
					float rStars = s != null && s.AverageRating > 0 ? (float)s.AverageRating : 4.5f;
					int tReviews = s != null ? s.ReviewsCount : 0;
					int vReviews = s != null ? s.VerifiedGoodReviewsCount : 0;
					int pMinutes = s != null ? (int)s.TotalPlaytimeMinutes : 0;
					int gPlayed = s != null ? s.TotalGamesPlayed : 0;

					return new DiscoveryMapDto
					{
						MapId = compositeKey,
						Title = title,
						Version = version,
						Creator = creator,
						Description = desc,
						Genre = gen,
						ThumbnailHash = thumb,
						Screenshots = screens,
						Features = fAndT,
						Tags = fAndT,
						RatingStars = rStars,
						TotalReviews = tReviews,
						VerifiedGoodReviews = vReviews,
						AverageRating = rStars,
						PlaytimeMinutes = pMinutes,
						GamesPlayed = gPlayed,
						TotalSizeBytes = bytes,
						FileSizeFormatted = FormatByteSize(bytes),
						EngineVersion = eVer,
						MaxPlayers = mPlayers,
						IsGreenlit = greenlit,
						IsTestVersion = isTestVersion,
						Awards = MapAwardsHelper.GetAwards(s),
						Attribution_SourceMap = attributionSourceMaps ?? new List<string>()
					};
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

					bool isTestVersionDocument = root.TryGetProperty("IsTestVersion", out var testVersionProp) && testVersionProp.ValueKind == JsonValueKind.True;
					string compositeKey = isTestVersionDocument
						? MapMaintainerHelper.GetTestCompositeKey(mapTitle, mapVersion)
						: $"{mapTitle}_{mapVersion}";
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

					var attributionSourceMaps = new List<string>();
					if (root.TryGetProperty("Attribution_SourceMap", out var attributionProp) && attributionProp.ValueKind == JsonValueKind.Array)
					{
						foreach (var entry in attributionProp.EnumerateArray())
						{
							string? sourceMapName = entry.GetString();
							if (!string.IsNullOrWhiteSpace(sourceMapName)) attributionSourceMaps.Add(sourceMapName);
						}
					}

					long totalBytes = 0;
					string thumbnailHash = "";
					var screenshotHashes = new List<string>();
					string? metadataFileHash = null;

					Dictionary<string, string>? manifestFiles = null;
					if (root.TryGetProperty("Files", out var filesProp) && filesProp.ValueKind == JsonValueKind.Object)
					{
						manifestFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
						foreach (var fileProp in filesProp.EnumerateObject())
						{
							manifestFiles[fileProp.Name] = fileProp.Value.GetString() ?? "";
						}
					}
					else
					{
						try
						{
							var manifestObj = MapManifest.LoadFromJson(doc.RootElement.GetRawText());
							manifestFiles = manifestObj?.Files;
						}
						catch (Exception ex)
						{
							Console.Error.WriteLine($"[AdminServer] could not parse a published map manifest into its file list: {ex.GetType().Name}: {ex.Message}");
						}
					}

					if (manifestFiles != null)
					{
						foreach (var fileProp in manifestFiles)
						{
							string path = fileProp.Key;
							string rawHash = fileProp.Value;
							string norm = ContentAddressableStorage.NormalizeBlake3Hash(rawHash);

							MapMetadataHelper.ParseManifestFileDetails(
								cas, path, norm, ref metadataFileHash, ref thumbnailHash, screenshotHashes, ref totalBytes);
						}
					}

					MapMetadataHelper.ParseMapMetadataDetails(
						cas, metadataFileHash, ref description, tags,
						out string engineVersion, out string maxPlayers, out string genre);

					if (string.IsNullOrEmpty(thumbnailHash) && screenshotHashes.Count > 0)
					{
						thumbnailHash = screenshotHashes[0];
					}							discoveryList.Add(CreateDiscoveryMapDto(
								compositeKey, mapTitle, mapVersion, creator, description, genre,
								thumbnailHash, screenshotHashes, tags, totalBytes, engineVersion, maxPlayers, isGreenlit, isTestVersionDocument,
								attributionSourceMaps, stats));
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine($"[AdminServer] skipped a map in the discovery list because its manifest document could not be read: {ex.GetType().Name}: {ex.Message}");
				}
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
						if (manifest.IsTestVersion) continue;

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

							MapMetadataHelper.ParseManifestFileDetails(
								cas, path, norm, ref metadataFileHash, ref thumbnailHash, screenshotHashes, ref totalBytes);
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

						MapMetadataHelper.ParseMapMetadataDetails(
							cas, metadataFileHash ?? "", ref description, tags,
							out engineVersion, out maxPlayers, out genre);

											discoveryList.Add(CreateDiscoveryMapDto(
								compositeKey, manifest.MapName, manifest.Version,
								!string.IsNullOrEmpty(manifest.Author) ? manifest.Author : "Realm Builder",
								description, genre, thumbnailHash, screenshotHashes, tags, totalBytes,
								engineVersion, maxPlayers, isGreenlit, false,
								manifest.Attribution_SourceMap ?? new List<string>(), stats));
					}
					catch (Exception ex)
					{
						Console.Error.WriteLine($"[AdminServer] skipped a map in the discovery list because its manifest file could not be read: {ex.GetType().Name}: {ex.Message}");
					}
				}
			}

			return Results.Ok(discoveryList);
		});
	}
}
