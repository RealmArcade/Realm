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

/// <summary>The map publication pipeline: session initiation, finalisation and the direct publish path.</summary>
public static class PublishMapEndpoints
{
	public static void MapPublishMapEndpoints(WebApplication app, ServerSetup setup)
	{
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
				var pubKey = PublicKey.Import(SignatureAlgorithm.Ed25519, pubKeyBytes, KeyBlobFormat.RawPublicKey);

				byte[] mapBytes = Encoding.UTF8.GetBytes(jsonToProcess);
				string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(mapBytes, ".json");
				string mapHashStr = $"{canonicalBlake3}.json";
				byte[] mapHashBytes = Encoding.UTF8.GetBytes(mapHashStr);

				bool isSigValid = SignatureAlgorithm.Ed25519.Verify(pubKey, mapHashBytes, sigBytes)
				                  || SignatureAlgorithm.Ed25519.Verify(pubKey, Encoding.UTF8.GetBytes(canonicalBlake3), sigBytes)
				                  || SignatureAlgorithm.Ed25519.Verify(pubKey, mapBytes, sigBytes);

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

				if (!MapMaintainerHelper.IsAuthorizedMaintainer(db, mapTitle, req.PublicKey))
				{
					return Results.BadRequest(new PublishMapInitiateResponse
					{
						Success = false,
						Message = "A map with this title already exists and is owned by different maintainers."
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

				try
				{
					var manifestObj = MapManifest.LoadFromJson(jsonToProcess);
					if (manifestObj?.Files != null)
					{
						foreach (var val in manifestObj.Files.Values)
						{
							if (!string.IsNullOrWhiteSpace(val))
							{
								referencedHashes.Add(ContentAddressableStorage.NormalizeBlake3Hash(val));
							}
						}
					}
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine($"[AdminServer] could not read the referenced hashes from a manifest, so its assets may be treated as unreferenced: {ex.GetType().Name}: {ex.Message}");
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
					IsTestVersion = req.IsTestVersion,
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
				bool isTestVersion = req.IsTestVersion;
				List<string> referencedHashes = new();

				if (sessionDoc != null)
				{
					var root = sessionDoc.RootElement;
					manifestJson = root.TryGetProperty("ManifestJson", out var mj) ? mj.GetString() ?? "" : "";
					mapTitle = root.TryGetProperty("MapTitle", out var mt) ? mt.GetString() ?? mapTitle : mapTitle;
					mapVersion = root.TryGetProperty("MapVersion", out var mv) ? mv.GetString() ?? mapVersion : mapVersion;
					publicKey = root.TryGetProperty("PublicKey", out var pk) ? pk.GetString() ?? publicKey : publicKey;
					signature = root.TryGetProperty("Signature", out var sig) ? sig.GetString() ?? signature : signature;
					if (root.TryGetProperty("IsTestVersion", out var sessionTestFlag) && sessionTestFlag.ValueKind == JsonValueKind.True)
					{
						isTestVersion = true;
					}

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
				var allManifestHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var hash in referencedHashes)
				{
					if (!string.IsNullOrWhiteSpace(hash))
					{
						allManifestHashes.Add(ContentAddressableStorage.NormalizeBlake3Hash(hash));
					}
				}

				try
				{
					var manifestObj = MapManifest.LoadFromJson(manifestJson);
					if (manifestObj?.Files != null)
					{
						foreach (var val in manifestObj.Files.Values)
						{
							if (!string.IsNullOrWhiteSpace(val))
							{
								allManifestHashes.Add(ContentAddressableStorage.NormalizeBlake3Hash(val));
							}
						}
					}
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine($"[AdminServer] could not read the manifest hashes for a map, so its assets may be treated as unreferenced: {ex.GetType().Name}: {ex.Message}");
				}

				var missingHashes = new List<string>();
				foreach (var hash in allManifestHashes)
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

				string compositeKey = isTestVersion
					? MapMaintainerHelper.GetTestCompositeKey(mapTitle, mapVersion)
					: $"{mapTitle}_{mapVersion}";
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

				if (!MapMaintainerHelper.IsAuthorizedMaintainer(db, mapTitle, publicKey))
				{
					return Results.BadRequest(new PublishMapFinalizeResponse
					{
						Success = false,
						Message = "A map with this title already exists and is owned by different maintainers."
					});
				}

				MapMaintainerHelper.AddMaintainer(db, mapTitle, publicKey);

				string manifestDir = Path.Combine(cas.RootDirectory, "manifests");
				if (!Directory.Exists(manifestDir)) Directory.CreateDirectory(manifestDir);
				string manifestPath = Path.Combine(manifestDir, $"{compositeKey}_manifest.json");
				File.WriteAllText(manifestPath, manifestJson);
				if (!isTestVersion)
				{
					string defaultManifestPath = Path.Combine(manifestDir, $"{mapTitle}_manifest.json");
					File.WriteAllText(defaultManifestPath, manifestJson);
				}

				byte[] manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
				string manifestBlake3 = RealmMetadataHelper.ComputeBlake3(manifestBytes, ".json");
				cas.StoreAsset(manifestBytes, ".json", null, publicKey, signature, precomputedBlake3: manifestBlake3);

				db.Upsert("published_maps", compositeKey, PublishedMapDocumentHelper.Build(manifestJson, isTestVersion));

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
						Signature = signature,
						IsTestVersion = isTestVersion
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
			try
			{
				string jsonToProcess = !string.IsNullOrWhiteSpace(req.ManifestJson) ? req.ManifestJson : req.MapJson;
				if (string.IsNullOrWhiteSpace(jsonToProcess))
				{
					return Results.BadRequest(new { Message = "ManifestJson or MapJson is required." });
				}

				var mapDoc = JsonDocument.Parse(jsonToProcess);
				var root = mapDoc.RootElement;

				string mapTitle = "";
				string mapVersion = "1.0";

				if (root.TryGetProperty("MapName", out var mapNameProp))
				{
					mapTitle = mapNameProp.GetString() ?? "";
				}

				if (root.TryGetProperty("Version", out var versionProp))
				{
					mapVersion = versionProp.GetString() ?? "1.0";
				}

				if (string.IsNullOrEmpty(mapTitle) && root.TryGetProperty("MapProperties", out var mapProps))
				{
					if (mapProps.TryGetProperty("MapName", out var nameProp)) mapTitle = nameProp.GetString() ?? "";
					if (mapProps.TryGetProperty("MapTitle", out var titleProp)) mapTitle = titleProp.GetString() ?? mapTitle;
					if (mapProps.TryGetProperty("MapVersion", out var vProp)) mapVersion = vProp.GetString() ?? "1.0";
				}

				if (string.IsNullOrEmpty(mapTitle))
				{
					return Results.BadRequest(new { Message = "MapName or MapTitle is required in manifest." });
				}

				if (string.IsNullOrEmpty(req.Signature) || string.IsNullOrEmpty(req.PublicKey))
				{
					return Results.BadRequest(new { Message = "Map signature and public key are required." });
				}

				byte[] pubKeyBytes;
				byte[] sigBytes;
				try
				{
					pubKeyBytes = Convert.FromBase64String(req.PublicKey);
					sigBytes = Convert.FromBase64String(req.Signature);
				}
				catch
				{
					return Results.BadRequest(new { Message = "Invalid base64 encoding for signature or public key." });
				}

				var pubKey = PublicKey.Import(SignatureAlgorithm.Ed25519, pubKeyBytes, KeyBlobFormat.RawPublicKey);

				byte[] mapBytes = Encoding.UTF8.GetBytes(jsonToProcess);
				string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(mapBytes, ".json");
				string mapHashStr = $"{canonicalBlake3}.json";
				byte[] mapHashBytes = Encoding.UTF8.GetBytes(mapHashStr);

				if (!SignatureAlgorithm.Ed25519.Verify(pubKey, mapHashBytes, sigBytes))
				{
					byte[] rawHashBytes = Encoding.UTF8.GetBytes(canonicalBlake3);
					if (!SignatureAlgorithm.Ed25519.Verify(pubKey, rawHashBytes, sigBytes))
					{
						if (!SignatureAlgorithm.Ed25519.Verify(pubKey, mapBytes, sigBytes))
						{
							return Results.BadRequest(new { Message = "Invalid map signature." });
						}
					}
				}

				bool isTestVersion = req.IsTestVersion;
				if (root.TryGetProperty("IsTestVersion", out var manifestTestFlag) && manifestTestFlag.ValueKind == JsonValueKind.True)
				{
					isTestVersion = true;
				}

				string compositeKey = isTestVersion
					? MapMaintainerHelper.GetTestCompositeKey(mapTitle, mapVersion)
					: $"{mapTitle}_{mapVersion}";
				var stats = MapStatsHelper.GetStats(db, mapTitle, mapVersion, req.PublicKey);
				bool isGreenlit = stats != null && stats.IsGreenlit;
				if (!isGreenlit)
				{
					return Results.Json(new
					{
						Message = $"Map '{mapTitle}' is not greenlit for publication to the public registry. Accumulate more community playtime and ratings or request an admin override.",
						IsGreenlit = false,
						Stats = stats ?? new MapStats()
					}, statusCode: StatusCodes.Status403Forbidden);
				}

				var existingMap = db.Get<JsonDocument>("published_maps", compositeKey);
				if (existingMap != null)
				{
					return Results.BadRequest(new { Message = $"Map with Title '{mapTitle}' and Version '{mapVersion}' already exists." });
				}

				if (!MapMaintainerHelper.IsAuthorizedMaintainer(db, mapTitle, req.PublicKey))
				{
					return Results.BadRequest(new { Message = "A map with this title already exists and is owned by different maintainers." });
				}

				MapMaintainerHelper.AddMaintainer(db, mapTitle, req.PublicKey);

				var contributors = new HashSet<string>();
				if (root.TryGetProperty("Contributors", out var contProp) && contProp.ValueKind == JsonValueKind.Array)
				{
					foreach (var element in contProp.EnumerateArray())
					{
						var str = element.GetString();
						if (str != null) contributors.Add(str);
					}
				}

				if (root.TryGetProperty("Author", out var authorProp) && authorProp.ValueKind == JsonValueKind.String)
				{
					var authorStr = authorProp.GetString();
					if (!string.IsNullOrEmpty(authorStr)) contributors.Add(authorStr);
				}

				if (req.ReferencedHashes != null)
				{
					foreach (var hash in req.ReferencedHashes)
					{
						var assetMeta = db.Get<JsonDocument>("asset_signatures", hash);
						if (assetMeta != null)
						{
							var author = assetMeta.RootElement.GetProperty("AuthorUsername").GetString();
							if (!string.IsNullOrEmpty(author) && !contributors.Contains(author))
							{
								return Results.BadRequest(new { Message = $"Author '{author}' from asset '{hash}' is missing from the Contributors list." });
							}
						}
					}
				}

				string manifestDir = Path.Combine(cas.RootDirectory, "manifests");
				if (!Directory.Exists(manifestDir)) Directory.CreateDirectory(manifestDir);
				string manifestPath = Path.Combine(manifestDir, $"{compositeKey}_manifest.json");
				File.WriteAllText(manifestPath, jsonToProcess);
				if (!isTestVersion)
				{
					string defaultManifestPath = Path.Combine(manifestDir, $"{mapTitle}_manifest.json");
					File.WriteAllText(defaultManifestPath, jsonToProcess);
				}

				byte[] manifestBytes = Encoding.UTF8.GetBytes(jsonToProcess);
				string manifestBlake3 = RealmMetadataHelper.ComputeBlake3(manifestBytes, ".json");
				cas.StoreAsset(manifestBytes, ".json", null, req.PublicKey, req.Signature, precomputedBlake3: manifestBlake3);

				db.Upsert("published_maps", compositeKey, PublishedMapDocumentHelper.Build(jsonToProcess, isTestVersion));

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
						Signature = req.Signature,
						IsTestVersion = isTestVersion
					})
				};
				clusterEvents.RecordEvent(pubEvent, db);
				clusterEvents.BroadcastEvent(pubEvent, registeredPeers, httpClientFactory);

				return Results.Ok(new { Status = "Published", MapId = compositeKey });
			}
			catch (Exception ex)
			{
				return Results.BadRequest(new { Message = "Invalid JSON or request", Error = ex.Message });
			}
		});
	}

	public static void MapPublishMapAssetEndpoints(WebApplication app, ServerSetup setup)
	{
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

			try
			{
				byte[] pubKeyBytes = Convert.FromBase64String(publicKey);
				byte[] sigBytes = Convert.FromBase64String(signature);
				var pubKey = PublicKey.Import(SignatureAlgorithm.Ed25519, pubKeyBytes, KeyBlobFormat.RawPublicKey);
				byte[] hashBytes = Encoding.UTF8.GetBytes(hash);
				byte[] normalizedHashBytes = Encoding.UTF8.GetBytes(normalizedHash);
				if (!SignatureAlgorithm.Ed25519.Verify(pubKey, hashBytes, sigBytes) &&
				    !SignatureAlgorithm.Ed25519.Verify(pubKey, normalizedHashBytes, sigBytes))
				{
					return Results.BadRequest("Invalid asset signature.");
				}
			}
			catch
			{
				return Results.BadRequest("Invalid asset signature format.");
			}

			var existingMeta = db.Get<JsonDocument>("asset_signatures", normalizedHash) ?? db.Get<JsonDocument>("asset_signatures", hash);
			if (existingMeta == null)
			{
				var metaDoc = JsonSerializer.SerializeToDocument(new
				{
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

		app.MapGet("/api/publish_map/asset_author/{hash}", (string hash, DataStoreService db) =>
		{
			var existingMeta = db.Get<JsonDocument>("asset_signatures", hash);
			if (existingMeta != null)
			{
				return Results.Ok(existingMeta);
			}

			return Results.NotFound();
		});
	}
}
