using System.Net;
using System.Text;
using System.Text.Json;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

/// <summary>the asset bundle endpoint.</summary>
public partial class DistributionServer
	{
	private async Task HandleAssetBundleEndpointAsync(HttpListenerContext context)
	{
		var request = context.Request;
		var response = context.Response;

		using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
		string json = await reader.ReadToEndAsync();

		AssetBundleRequestDto? req = null;
		try
		{
			req = JsonSerializer.Deserialize<AssetBundleRequestDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[DistributionServer] the asset-bundle request body was not valid JSON: {ex.GetType().Name}: {ex.Message}");
		}

		if (req == null || req.Hashes == null || req.Hashes.Count == 0)
		{
			response.StatusCode = (int)HttpStatusCode.BadRequest;
			response.Close();
			return;
		}

		var assetInfos = GetValidAssetInfos(req.Hashes);

		response.ContentType = "application/octet-stream";
		response.StatusCode = (int)HttpStatusCode.OK;

		try
		{
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource?.Token ?? CancellationToken.None);
			await ZstdAssetBundleHelper.StreamAssetsToBundleAsync(
				response.OutputStream,
				assetInfos,
				compressionLevel: 1,
				cancellationToken: cts.Token);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[DistributionServer] streaming the asset bundle to the client failed: {ex.GetType().Name}: {ex.Message}");
		}
		finally
		{
			CloseResponseSafe(response);
		}
	}

	private List<(string AssetKey, string FilePath, string? Metadata)> GetValidAssetInfos(List<string> hashes)
	{
		var assetInfos = new List<(string AssetKey, string FilePath, string? Metadata)>();
		foreach (var hash in hashes)
		{
			if (string.IsNullOrWhiteSpace(hash)) continue;
			string normalized = ContentAddressableStorage.NormalizeBlake3Hash(hash);
			string? filePath = _storage.FindAssetFilePath(normalized);
			if (filePath == null || !File.Exists(filePath)) continue;

			string? metadata = _storage.GetAssetMetadata(normalized);
			assetInfos.Add((normalized, filePath, metadata));
		}

		return assetInfos;
	}

	private void CloseResponseSafe(HttpListenerResponse response)
	{
		try
		{
			response.Close();
		}
		catch
		{
		}
	}
}
