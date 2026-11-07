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

/// <summary>Content-addressed asset reads, writes, bundles and published map manifests.</summary>
internal static class AssetEndpoints
{
	internal static void MapAssetEndpoints(WebApplication app, ServerSetup setup)
	{
		app.MapMethods("/api/assets/{hash}", new[] { "HEAD" }, (string hash, ContentAddressableStorage cas) =>
		{
			string normalized = ContentAddressableStorage.NormalizeBlake3Hash(hash);
			return cas.HasAsset(normalized) ? Results.Ok() : Results.NotFound();
		});

		app.MapPost("/api/assets/bundle", async (HttpContext context, ContentAddressableStorage cas) =>
		{
			if (!setup.ServesClientDownloads)
			{
				context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
				await context.Response.WriteAsJsonAsync(new { Error = ServerHost.ContentNotServedError }, context.RequestAborted);
				return;
			}

			var syncIoFeature = context.Features.Get<IHttpBodyControlFeature>();
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
			catch (Exception ex)
			{
				Console.Error.WriteLine($"[AdminServer] the asset-bundle request body was not valid JSON: {ex.GetType().Name}: {ex.Message}");
			}

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
			if (!setup.ServesClientDownloads) return Results.Json(new { Error = ServerHost.ContentNotServedError }, statusCode: StatusCodes.Status503ServiceUnavailable);

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
			if (!DistributionSharding.SeederAcceptsHash(setup.SeederId, setup.CapacityPercent, normalized))
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
					if (manifest != null && !manifest.IsTestVersion)
					{
						if (string.Equals(manifest.MapName, mapId, StringComparison.OrdinalIgnoreCase) ||
						    string.Equals(manifest.MapName?.Replace(' ', '_'), mapId.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase))
						{
							return Results.File(File.ReadAllBytes(file), "application/json");
						}
					}
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine($"[AdminServer] skipped an unreadable map manifest while resolving a map id: {ex.GetType().Name}: {ex.Message}");
				}
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
				bool bypassValid = setup.AdminPublicKeys.Count > 0 &&
				                   AdminBypassAuth.VerifyBypassToken(setup.AdminPublicKeys, manifest.MapName, manifest.Version, bypassToken);
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
	}
}
