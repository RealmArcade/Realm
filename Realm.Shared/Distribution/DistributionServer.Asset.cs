using System.Net;
using System.Text;
using System.Text.Json;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

/// <summary>the asset endpoint in all three verbs.</summary>
public partial class DistributionServer
	{
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

			string? parsedExt = Path.GetExtension(parsedForm.FileName);
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
}
