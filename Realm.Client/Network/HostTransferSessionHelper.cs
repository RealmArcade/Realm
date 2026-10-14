using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Realm.Shared.Distribution;

namespace Realm.Client.Network;

public static class HostTransferSessionHelper
{
	public static List<List<HostAssetTransferItem>> ChunkTransferItems(List<HostAssetTransferItem> itemsToPack)
	{
		var chunkList = new List<List<HostAssetTransferItem>>();
		var currentChunk = new List<HostAssetTransferItem>();
		long currentChunkBytes = 0;

		foreach (var item in itemsToPack)
		{
			long estimated = item.Size + (item.AssetKey?.Length ?? 0) * 2 + (item.Metadata?.Length ?? 0) * 2 + 16;
			if (currentChunk.Count > 0 && currentChunkBytes + estimated > ZstdAssetBundleHelper.MaxBundleChunkSize)
			{
				chunkList.Add(currentChunk);
				currentChunk = new List<HostAssetTransferItem>();
				currentChunkBytes = 0;
			}

			currentChunk.Add(item);
			currentChunkBytes += estimated;
		}

		if (currentChunk.Count > 0)
		{
			chunkList.Add(currentChunk);
		}

		return chunkList;
	}

	public static Task<byte[]> CreateCompressedChunkAsync(HostTransferSession session, int chunkIndex)
	{
		return Task.Run(() =>
		{
			var chunkItems = session.ChunkItems[chunkIndex];
			var chunkAssets = new List<(string AssetKey, byte[] Data, string? Metadata)>(chunkItems.Count);
			foreach (var item in chunkItems)
			{
				if (session.Cts.IsCancellationRequested) break;
				if (File.Exists(item.SourceFilePath))
				{
					byte[] data = File.ReadAllBytes(item.SourceFilePath);
					chunkAssets.Add((item.AssetKey, data, item.Metadata));
				}
			}

			return ZstdAssetBundleHelper.CreateBundleBytes(chunkAssets, 1);
		}, session.Cts.Token);
	}
}
