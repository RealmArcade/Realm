using System.Net;
using System.Text;
using System.Text.Json;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

/// <summary>posting and deleting manifests, including the delete authorisation.</summary>
public partial class DistributionServer
	{
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
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[DistributionServer] the admin request could not be parsed, so its admin fields were ignored: {ex.GetType().Name}: {ex.Message}");
		}

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
				catch (Exception ex)
				{
					Console.Error.WriteLine($"[DistributionServer] skipping an unreadable map manifest while matching a version to delete: {ex.GetType().Name}: {ex.Message}");
				}
			}

			if (shouldDel)
			{
				try
				{
					File.Delete(file);
					manifestsDeleted++;
					deletedFiles.Add(fileName);
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine($"[DistributionServer] could not delete the manifest {fileName}: {ex.GetType().Name}: {ex.Message}");
				}
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
			catch (Exception ex)
			{
				Console.Error.WriteLine($"[DistributionServer] could not delete the manifest {fileName}: {ex.GetType().Name}: {ex.Message}");
			}
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
}
