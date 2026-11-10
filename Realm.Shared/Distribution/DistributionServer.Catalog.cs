using System.Net;
using System.Text;
using System.Text.Json;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

/// <summary>the catalog and bloom header endpoints.</summary>
public partial class DistributionServer
	{
	private async Task HandleCatalogEndpointAsync(HttpListenerResponse response)
	{
		var hashes = _storage.GetAllStoredHashes().ToList();
		var catalog = new SeederCatalogResponseDto
		{
			SeederId = _seederId,
			CapacityPercentage = _capacityPercentage,
			AssetHashes = hashes
		};

		byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(catalog));
		response.ContentType = "application/json";
		response.StatusCode = (int)HttpStatusCode.OK;
		await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
		response.Close();
	}

	private async Task HandleBloomHeadersEndpointAsync(HttpListenerResponse response)
	{
		var hashes = _storage.GetAllStoredHashes().ToList();
		var bloomFilter = new BloomFilter(Math.Max(100, hashes.Count), 0.01);

		foreach (string hash in hashes)
		{
			string? metadataJson = _storage.GetAssetMetadata(hash);
			string key = BloomFilter.CreateHeaderKey(hash, metadataJson);
			bloomFilter.Add(key);
		}

		var dto = new BloomHeadersResponseDto
		{
			SeederId = _seederId,
			BitCount = bloomFilter.BitCount,
			HashCount = bloomFilter.HashCount,
			ItemCount = bloomFilter.ItemCount,
			FilterDataBase64 = Convert.ToBase64String(bloomFilter.BitArray)
		};

		byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto));
		response.ContentType = "application/json";
		response.StatusCode = (int)HttpStatusCode.OK;
		await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
		response.Close();
	}
}
