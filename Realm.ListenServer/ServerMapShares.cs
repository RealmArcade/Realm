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

	private readonly Dictionary<int, HashSet<string>> _heldByPeer = new();

	private int _builtChunkIndex = -1;
	private int _builtBundlePeerId;
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
			_log.Write($"the lobby serves its map from {_pathByHash.Count} file(s) in {_chunks.Count} bundle(s), carried as pieces of at most {MapTransferPackets.PieceBytes} bytes, {LobbyLimits.MaximumPiecesPerRequest} at a time");
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

	public List<MapTransferReply> Handle(int peerId, MapTransferRequest request)
	{
		if (_manifest == null) return Refused(peerId, request, "this lobby cannot serve a map it could not read");

		if (request.Kind == MapTransferRequestKind.Held) return Single(RecordHeldFiles(peerId, request));

		if (request.Kind == MapTransferRequestKind.Manifest)
		{
			// A manifest states the transfer a joiner is about to start, so what an earlier attempt said it held is given up
			// here: the archive it states next belongs to this transfer.
			_heldByPeer.Remove(peerId);

			if (ManifestBytes.Length > LobbyLimits.MaximumMapTransferFrameBytes)
			{
				return Refused(peerId, request, $"this map's manifest is {ManifestBytes.Length} bytes, larger than the {LobbyLimits.MaximumMapTransferFrameBytes} bytes one message carries");
			}

			return Single(new MapTransferReply
			{
				Kind = MapTransferReplyKind.Manifest,
				TransferId = request.TransferId,
				MapName = request.MapName,
				ManifestBytes = ManifestBytes,
				TotalChunks = _chunks.Count,
				TotalBytes = TotalBytes
			});
		}

		if (_chunks.Count == 0) return Refused(peerId, request, "this lobby's map lists no files to serve");
		if (request.ChunkIndex < 0 || request.ChunkIndex >= _chunks.Count) return Refused(peerId, request, "that bundle is not part of this transfer");

		List<MapShareFile> needed = FilesStillNeededBy(peerId, request.ChunkIndex);
		if (needed.Count == 0)
		{
			_log.Write($"peer {peerId} already holds every file in map bundle {request.ChunkIndex + 1}/{_chunks.Count}, so nothing of it is sent");

			return Single(new MapTransferReply
			{
				Kind = MapTransferReplyKind.Chunk,
				TransferId = request.TransferId,
				MapName = request.MapName,
				ChunkIndex = request.ChunkIndex,
				TotalChunks = _chunks.Count,
				TotalBytes = TotalBytes,
				Bundle = Array.Empty<byte>()
			});
		}

		byte[] bundle = BundleForChunk(peerId, request.ChunkIndex, needed);
		int packetCount = MapTransferPackets.CountFor(bundle.Length);
		if (packetCount == 0) return Refused(peerId, request, "that bundle holds no bytes");
		if (request.PacketIndex < 0 || request.PacketIndex >= packetCount) return Refused(peerId, request, "that piece is not part of this bundle");

		int window = Math.Clamp(request.PacketWindow < 1 ? 1 : request.PacketWindow, 1, LobbyLimits.MaximumPiecesPerRequest);
		int lastPiece = Math.Min(packetCount, request.PacketIndex + window);

		var replies = new List<MapTransferReply>(lastPiece - request.PacketIndex);
		for (int packetIndex = request.PacketIndex; packetIndex < lastPiece; packetIndex++)
		{
			byte[] piece = MapTransferPackets.Slice(bundle, packetIndex);
			replies.Add(new MapTransferReply
			{
				Kind = MapTransferReplyKind.Chunk,
				TransferId = request.TransferId,
				MapName = request.MapName,
				ChunkIndex = request.ChunkIndex,
				TotalChunks = _chunks.Count,
				PacketIndex = packetIndex,
				PacketCount = packetCount,
				TotalBytes = TotalBytes,
				Bundle = piece
			});
		}

		_log.Write($"peer {peerId} is served map bundle {request.ChunkIndex + 1}/{_chunks.Count} pieces {request.PacketIndex + 1}-{lastPiece}/{packetCount} ({lastPiece - request.PacketIndex} of {LobbyLimits.MaximumPiecesPerRequest} at a time, {needed.Count} of {_chunks[request.ChunkIndex].Count} file(s))");

		return replies;
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
	///     Builds one bundle and keeps it until another chunk or another peer asks for one, so the pieces of a large file
	///     are not read from disk and compressed again for every window of pieces the joiner requests.
	/// </summary>
	/// <param name="peerId">The peer the bundle is built for, whose held files it leaves out.</param>
	/// <param name="chunkIndex">The chunk to build.</param>
	/// <param name="needed">The files of that chunk this peer does not hold.</param>
	/// <returns>The bundle's bytes.</returns>
	private byte[] BundleForChunk(int peerId, int chunkIndex, List<MapShareFile> needed)
	{
		if (chunkIndex == _builtChunkIndex && peerId == _builtBundlePeerId) return _builtBundle;

		var assets = new List<(string AssetKey, byte[] Data, string? Metadata)>(needed.Count);
		foreach (MapShareFile item in needed)
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
		_builtBundlePeerId = peerId;
		return _builtBundle;
	}

	/// <summary>
	///     Records the files a joiner states it already holds, which are the ones no bundle has to carry for it. A file is
	///     named by the asset key the map's manifest states, and matched by the hash that key begins with, so a joiner whose
	///     keys carry a different extension still matches.
	/// </summary>
	/// <param name="peerId">The connection that announced them.</param>
	/// <param name="request">The announcement.</param>
	/// <returns>The answer that records them.</returns>
	private MapTransferReply RecordHeldFiles(int peerId, MapTransferRequest request)
	{
		string[] announced = request.HeldHashes ?? Array.Empty<string>();
		if (announced.Length > LobbyLimits.MaximumHeldHashesPerAnnouncement)
		{
			return RefusedSingle(peerId, request, $"that announcement names {announced.Length} files, more than the {LobbyLimits.MaximumHeldHashesPerAnnouncement} one may carry");
		}

		if (!_heldByPeer.TryGetValue(peerId, out HashSet<string>? held))
		{
			held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			_heldByPeer[peerId] = held;
		}

		foreach (string hash in announced)
		{
			string normalized = ContentAddressableStorage.NormalizeBlake3Hash(hash);
			if (normalized.Length > 0) held.Add(normalized);
		}

		_log.Write($"peer {peerId} holds {held.Count} file(s) of this map already, so they are left out of what it is served");

		return new MapTransferReply
		{
			Kind = MapTransferReplyKind.Held,
			TransferId = request.TransferId,
			MapName = request.MapName,
			TotalChunks = _chunks.Count,
			TotalBytes = TotalBytes
		};
	}

	/// <summary>
	///     The files of one chunk this peer does not hold, which is what its bundle is built from.
	/// </summary>
	/// <param name="peerId">The connection asking.</param>
	/// <param name="chunkIndex">The chunk it is asking for.</param>
	/// <returns>The files still to send.</returns>
	private List<MapShareFile> FilesStillNeededBy(int peerId, int chunkIndex)
	{
		List<MapShareFile> chunk = _chunks[chunkIndex];
		if (!_heldByPeer.TryGetValue(peerId, out HashSet<string>? held) || held.Count == 0) return chunk;

		var needed = new List<MapShareFile>(chunk.Count);
		foreach (MapShareFile file in chunk)
		{
			if (!held.Contains(file.Hash)) needed.Add(file);
		}

		return needed;
	}

	private static List<MapTransferReply> Single(MapTransferReply reply) => new(1) { reply };

	private List<MapTransferReply> Refused(int peerId, MapTransferRequest request, string reason) => Single(RefusedSingle(peerId, request, reason));

	private MapTransferReply RefusedSingle(int peerId, MapTransferRequest request, string reason)
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
