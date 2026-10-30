using System.Text.Json;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Services;

namespace Realm.ListenServer;

public class ServerMapShares
{
	private readonly ServerLog _log;
	private readonly MapManifest? _manifest;
	private readonly Dictionary<string, string> _pathByHash = new(StringComparer.OrdinalIgnoreCase);

	private const int MaximumRequestedHashes = 20000;

	public ServerMapShares(string mapFolder, ServerLog log)
	{
		_log = log;
		try
		{
			_manifest = MapFileService.LoadManifest(mapFolder);
			if (_manifest.Files.Count > 0)
			{
				foreach (var pair in _manifest.Files)
				{
					string normalized = ContentAddressableStorage.NormalizeBlake3Hash(pair.Value);
					string relative = pair.Key.Replace("res://", string.Empty).TrimStart('/', '\\');
					string path = Path.Combine(mapFolder, relative);
					if (File.Exists(path)) _pathByHash[normalized] = path;
				}
			}

			_log.Write($"the lobby serves its map from {_pathByHash.Count} file(s) listed in its manifest");
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
		{
			_log.Write($"the lobby could not read the map it serves: {exception.Message}");
		}
	}

	public string ManifestJson => _manifest == null ? string.Empty : JsonSerializer.Serialize(_manifest);

	public long TotalBytes
	{
		get
		{
			long total = 0;
			foreach (string path in _pathByHash.Values)
			{
				try
				{
					total += new FileInfo(path).Length;
				}
				catch (IOException)
				{
				}
			}

			return total;
		}
	}

	public MapTransferReply Handle(int peerId, MapTransferRequest request)
	{
		if (_manifest == null) return Refused(peerId, request, "this lobby cannot serve a map it could not read");

		if (request.Kind == MapTransferRequestKind.Manifest)
		{
			return new MapTransferReply
			{
				Kind = MapTransferReplyKind.Manifest,
				TransferId = request.TransferId,
				MapName = request.MapName,
				ManifestJson = ManifestJson,
				TotalBytes = TotalBytes
			};
		}

		string[] requested = request.MissingHashes ?? Array.Empty<string>();
		if (requested.Length == 0 || requested.Length > MaximumRequestedHashes) return Refused(peerId, request, "the requested file list is not a map transfer");

		var missing = new List<(string Hash, string Path, long Size)>(requested.Length);
		foreach (string hash in requested)
		{
			string normalized = ContentAddressableStorage.NormalizeBlake3Hash(hash);
			if (!_pathByHash.TryGetValue(normalized, out string? path)) continue;

			try
			{
				missing.Add((normalized, path, new FileInfo(path).Length));
			}
			catch (IOException)
			{
			}
		}

		if (missing.Count == 0) return Refused(peerId, request, "none of the requested files belong to this lobby's map");
		missing.Sort((left, right) => string.CompareOrdinal(left.Hash, right.Hash));

		List<List<(string Hash, string Path, long Size)>> chunks = Partition(missing);
		if (request.ChunkIndex < 0 || request.ChunkIndex >= chunks.Count) return Refused(peerId, request, "that bundle is not part of this transfer");

		var assets = new List<(string AssetKey, byte[] Data, string? Metadata)>(chunks[request.ChunkIndex].Count);
		foreach (var item in chunks[request.ChunkIndex])
		{
			try
			{
				assets.Add((item.Hash, File.ReadAllBytes(item.Path), null));
			}
			catch (IOException exception)
			{
				_log.Write($"the lobby could not read '{item.Path}' while serving it to peer {peerId}: {exception.Message}");
			}
		}

		byte[] bundle = ZstdAssetBundleHelper.CreateBundleBytes(assets);
		_log.Write($"peer {peerId} is served map bundle {request.ChunkIndex + 1}/{chunks.Count} ({bundle.Length} bytes, {assets.Count} file(s))");

		return new MapTransferReply
		{
			Kind = MapTransferReplyKind.Chunk,
			TransferId = request.TransferId,
			MapName = request.MapName,
			ChunkIndex = request.ChunkIndex,
			TotalChunks = chunks.Count,
			TotalBytes = TotalBytes,
			Bundle = bundle
		};
	}

	private static List<List<(string Hash, string Path, long Size)>> Partition(List<(string Hash, string Path, long Size)> files)
	{
		var chunks = new List<List<(string Hash, string Path, long Size)>>();
		var current = new List<(string Hash, string Path, long Size)>();
		long currentBytes = 0;

		foreach (var file in files)
		{
			long estimated = file.Size + file.Hash.Length * 2 + 16;
			if (current.Count > 0 && currentBytes + estimated > LobbyLimits.MaximumMapBundleBytes)
			{
				chunks.Add(current);
				current = new List<(string Hash, string Path, long Size)>();
				currentBytes = 0;
			}

			current.Add(file);
			currentBytes += estimated;
		}

		if (current.Count > 0) chunks.Add(current);
		return chunks;
	}

	private MapTransferReply Refused(int peerId, MapTransferRequest request, string reason)
	{
		_log.Write($"peer {peerId} was refused a map transfer: {reason}");

		return new MapTransferReply
		{
			Kind = MapTransferReplyKind.Refused,
			TransferId = request.TransferId,
			MapName = request.MapName,
			Refusal = reason
		};
	}
}
