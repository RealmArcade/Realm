using System.Net;
using System.Text;
using System.Text.Json;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

/// <summary>the header sync endpoint.</summary>
public partial class DistributionServer
	{
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
}
