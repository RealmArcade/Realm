using System.Collections.Generic;
using System.Threading;

namespace Realm.Client.Network;

public class HostTransferSession
{
	public string TransferId { get; set; } = string.Empty;
	public int PeerId { get; set; }
	public string MapName { get; set; } = string.Empty;
	public string MapVersion { get; set; } = string.Empty;
	public List<List<HostAssetTransferItem>> ChunkItems { get; set; } = new();
	public int CurrentChunkIndex { get; set; } = 0;
	public int TotalChunks => ChunkItems.Count;
	public long TotalRawBytes { get; set; }
	public CancellationTokenSource Cts { get; set; } = new();
}