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
	private readonly byte[] _manifestBytes = Array.Empty<byte>();
	private readonly Dictionary<string, string> _pathByHash = new(StringComparer.OrdinalIgnoreCase);

	private readonly List<List<MapShareFile>> _chunks = new();

	private int _builtChunkIndex = -1;
	private byte[] _builtBundle = Array.Empty<byte>();

	public ServerMapShares(string mapFolder, ServerLog log)
	{
		_log = log;
		try
		{
			_manifest = MapFileService.LoadManifest(mapFolder);
			// The manifest is served as the file it is, not as a re-serialization of it, so the copy a joiner writes is
			// the package this lobby advertises rather than one that reads the same and hashes differently.
			string manifestPath = Path.Combine(mapFolder, "manifest.json");
			if (File.Exists(manifestPath)) _manifestBytes = File.ReadAllBytes(manifestPath);

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

			BuildChunkPlan();
			_log.Write($"the lobby serves its map from {_pathByHash.Count} file(s) in {_chunks.Count} bundle(s), each carried as pieces of at most {MapTransferPackets.PieceBytes} bytes");
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
		{
			_log.Write($"the lobby could not read the map it serves: {exception.Message}");
		}
	}

	public byte[] ManifestBytes => _manifest == null ? Array.Empty<byte>() : _manifestBytes;

	public long TotalBytes
	{
		get
		{
			long total = 0;
			foreach (List<MapShareFile> chunk in _chunks)
			{
				foreach (MapShareFile file in chunk) total += file.Size;
			}

			return total;
		}
	}

	public MapTransferReply Handle(int peerId, MapTransferRequest request)
	{
		if (_manifest == null) return Refused(peerId, request, "this lobby cannot serve a map it could not read");

		if (request.Kind == MapTransferRequestKind.Manifest)
		{
			if (ManifestBytes.Length > LobbyLimits.MaximumMapTransferFrameBytes)
			{
				return Refused(peerId, request, $"this map's manifest is {ManifestBytes.Length} bytes, larger than the {LobbyLimits.MaximumMapTransferFrameBytes} bytes one message carries");
			}

			return new MapTransferReply
			{
				Kind = MapTransferReplyKind.Manifest,
				TransferId = request.TransferId,
				MapName = request.MapName,
				ManifestBytes = ManifestBytes,
				TotalChunks = _chunks.Count,
				TotalBytes = TotalBytes
			};
		}

		if (_chunks.Count == 0) return Refused(peerId, request, "this lobby's map lists no files to serve");
		if (request.ChunkIndex < 0 || request.ChunkIndex >= _chunks.Count) return Refused(peerId, request, "that bundle is not part of this transfer");

		byte[] bundle = BundleForChunk(request.ChunkIndex);
		int packetCount = MapTransferPackets.CountFor(bundle.Length);
		if (packetCount == 0) return Refused(peerId, request, "that bundle holds no bytes");
		if (request.PacketIndex < 0 || request.PacketIndex >= packetCount) return Refused(peerId, request, "that piece is not part of this bundle");

		byte[] piece = MapTransferPackets.Slice(bundle, request.PacketIndex);
		_log.Write($"peer {peerId} is served map bundle {request.ChunkIndex + 1}/{_chunks.Count} piece {request.PacketIndex + 1}/{packetCount} ({piece.Length} of {bundle.Length} bytes, {_chunks[request.ChunkIndex].Count} file(s))");

		return new MapTransferReply
		{
			Kind = MapTransferReplyKind.Chunk,
			TransferId = request.TransferId,
			MapName = request.MapName,
			ChunkIndex = request.ChunkIndex,
			TotalChunks = _chunks.Count,
			PacketIndex = request.PacketIndex,
			PacketCount = packetCount,
			TotalBytes = TotalBytes,
			Bundle = piece
		};
	}

	/// <summary>
	///     Lays the map's files out into the bundles a joiner walks in order. The plan is made once, from the files the
	///     lobby actually holds, so the bundle a chunk index names is the same bundle for every piece of it and for every
	///     peer that asks: a plan rebuilt per request would hand out a different set once a file had been read or replaced.
	/// </summary>
	private void BuildChunkPlan()
	{
		var files = new List<MapShareFile>(_pathByHash.Count);
		foreach (var pair in _pathByHash)
		{
			try
			{
				var info = new FileInfo(pair.Value);
				if (info.Exists) files.Add(new MapShareFile(pair.Key, pair.Value, info.Length));
			}
			catch (IOException exception)
			{
				_log.Write($"the map file {pair.Value} could not be measured, so it is left out of the transfer: {exception.GetType().Name}: {exception.Message}");
			}
		}

		files.Sort((left, right) => string.CompareOrdinal(left.Hash, right.Hash));

		var current = new List<MapShareFile>();
		long currentBytes = 0;
		foreach (MapShareFile file in files)
		{
			long estimated = file.Size + file.Hash.Length * 2 + 16;
			if (current.Count > 0 && currentBytes + estimated > LobbyLimits.MaximumMapBundleBytes)
			{
				_chunks.Add(current);
				current = new List<MapShareFile>();
				currentBytes = 0;
			}

			current.Add(file);
			currentBytes += estimated;
		}

		if (current.Count > 0) _chunks.Add(current);
	}

	/// <summary>
	///     Builds one bundle and keeps it until another chunk is asked for, so the pieces of a large file are not read
	///     from disk and compressed again for every piece the joiner requests.
	/// </summary>
	/// <param name="chunkIndex">The chunk to build.</param>
	/// <returns>The bundle's bytes.</returns>
	private byte[] BundleForChunk(int chunkIndex)
	{
		if (chunkIndex == _builtChunkIndex) return _builtBundle;

		List<MapShareFile> chunk = _chunks[chunkIndex];
		var assets = new List<(string AssetKey, byte[] Data, string? Metadata)>(chunk.Count);
		foreach (MapShareFile item in chunk)
		{
			try
			{
				assets.Add((item.Hash, File.ReadAllBytes(item.Path), null));
			}
			catch (IOException exception)
			{
				_log.Write($"the lobby could not read '{item.Path}' while serving bundle {chunkIndex + 1}: {exception.Message}");
			}
		}

		_builtBundle = ZstdAssetBundleHelper.CreateBundleBytes(assets);
		_builtChunkIndex = chunkIndex;
		return _builtBundle;
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

	private readonly record struct MapShareFile(string Hash, string Path, long Size);
}
