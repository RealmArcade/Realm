using System;
using System.IO;

namespace Realm.Client.Network;

public class ClientTransferSession
{
	public string TransferId { get; set; } = string.Empty;
	public string MapName { get; set; } = string.Empty;
	public string MapVersion { get; set; } = string.Empty;
	public MapManifest Manifest { get; set; } = new();
	public long ExpectedTotalBytes { get; set; }
	public int ExpectedTotalChunks { get; set; }
	public int CurrentChunkIndex { get; set; }
	public MemoryStream CurrentChunkStream { get; set; } = new();
	public int ReceivedPacketsInCurrentChunk { get; set; }
	public long TotalReceivedBytes { get; set; }
	public int TotalAssetsInManifest { get; set; }
	public int AlreadyPresentAssets { get; set; }
	public int SessionMissingAssets { get; set; }
	public DateTime LastActivityTimeUtc { get; set; } = DateTime.UtcNow;
	public float LastEmittedProgress { get; set; } = -1.0f;
	public long LastProgressEmitTicks { get; set; } = 0;
	public Action<float>? ProgressCallback { get; set; }
}