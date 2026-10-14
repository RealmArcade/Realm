using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Realm.Shared.Distribution;

namespace Realm.Client.Network;

public static class HostTransferSessionHelper
{
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
