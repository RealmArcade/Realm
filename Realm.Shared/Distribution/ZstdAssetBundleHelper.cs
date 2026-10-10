using System.Text;
using ZstdSharp;

namespace Realm.Shared.Distribution;

public class ZstdAssetBundleHelper
{
    public const int ChunkSize = 64 * 1024;
    public const int PacketChunkSize = 256 * 1024;
    public const int MaxBundleChunkSize = 100 * 1024 * 1024;

    public static List<List<(string AssetKey, byte[] Data, string? Metadata)>> PartitionAssetsIntoChunks(
        IEnumerable<(string AssetKey, byte[] Data, string? Metadata)> assets,
        long maxChunkBytes = MaxBundleChunkSize)
    {
        var result = new List<List<(string AssetKey, byte[] Data, string? Metadata)>>();
        var currentChunk = new List<(string AssetKey, byte[] Data, string? Metadata)>();
        long currentChunkBytes = 0;

        foreach (var item in assets)
        {
            long estimatedAssetBytes = EstimateAssetBytes(item);
            if (currentChunk.Count > 0 && currentChunkBytes + estimatedAssetBytes > maxChunkBytes)
            {
                result.Add(currentChunk);
                currentChunk = new List<(string AssetKey, byte[] Data, string? Metadata)>();
                currentChunkBytes = 0;
            }

            currentChunk.Add(item);
            currentChunkBytes += estimatedAssetBytes;
        }

        if (currentChunk.Count > 0)
        {
            result.Add(currentChunk);
        }

        return result;
    }

    private static long EstimateAssetBytes((string AssetKey, byte[] Data, string? Metadata) item)
    {
        long dataLength = item.Data?.Length ?? 0;
        long keyLength = (item.AssetKey?.Length ?? 0) * 2;
        long metadataLength = (item.Metadata?.Length ?? 0) * 2;

        return dataLength + keyLength + metadataLength + 16;
    }

    public static void CreateBundleToFile(
        string destinationFilePath,
        IEnumerable<(string AssetKey, byte[] Data, string? Metadata)> assets,
        int compressionLevel = 1)
    {
        string? dir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fileStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, ChunkSize);
        using var zstdStream = new CompressionStream(fileStream, compressionLevel);
        using var writer = new BinaryWriter(zstdStream, Encoding.UTF8, leaveOpen: true);

        var assetList = assets as IList<(string, byte[], string?)> ?? new List<(string, byte[], string?)>(assets);
        writer.Write(assetList.Count);

        foreach (var (assetKey, data, metadata) in assetList)
        {
            writer.Write(assetKey);
            writer.Write(metadata ?? string.Empty);
            writer.Write(data.Length);
            writer.Write(data);
        }

        writer.Flush();
        zstdStream.Flush();
    }

    public static List<(string AssetKey, byte[] Data, string? Metadata)> ExtractBundleFromFile(string sourceFilePath)
    {
        using var fileStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize);
        using var zstdStream = new DecompressionStream(fileStream);
        using var bufferedStream = new BufferedStream(zstdStream, ChunkSize);
        using var reader = new BinaryReader(bufferedStream, Encoding.UTF8, leaveOpen: true);

        int count = reader.ReadInt32();
        var result = new List<(string AssetKey, byte[] Data, string? Metadata)>(count);

        for (int i = 0; i < count; i++)
        {
            string assetKey = reader.ReadString();
            string metadata = reader.ReadString();
            int dataLength = reader.ReadInt32();
            byte[] data = ReadExactBytes(bufferedStream, dataLength);

            result.Add((assetKey, data, string.IsNullOrEmpty(metadata) ? null : metadata));
        }

        return result;
    }

    public static byte[] CreateBundleBytes(
        IEnumerable<(string AssetKey, byte[] Data, string? Metadata)> assets,
        int compressionLevel = 1)
    {
        using var memoryStream = new MemoryStream();
        using (var zstdStream = new CompressionStream(memoryStream, compressionLevel, leaveOpen: true))
        using (var writer = new BinaryWriter(zstdStream, Encoding.UTF8, leaveOpen: true))
        {
            var assetList = assets as IList<(string, byte[], string?)> ?? new List<(string, byte[], string?)>(assets);
            writer.Write(assetList.Count);

            foreach (var (assetKey, data, metadata) in assetList)
            {
                writer.Write(assetKey);
                writer.Write(metadata ?? string.Empty);
                writer.Write(data.Length);
                writer.Write(data);
            }
        }

        return memoryStream.ToArray();
    }

    public static List<(string AssetKey, byte[] Data, string? Metadata)> ExtractBundleBytes(byte[] compressedBytes)
    {
        using var memoryStream = new MemoryStream(compressedBytes);
        using var zstdStream = new DecompressionStream(memoryStream);
        using var bufferedStream = new BufferedStream(zstdStream, ChunkSize);
        using var reader = new BinaryReader(bufferedStream, Encoding.UTF8, leaveOpen: true);

        int count = reader.ReadInt32();
        var result = new List<(string AssetKey, byte[] Data, string? Metadata)>(count);

        for (int i = 0; i < count; i++)
        {
            string assetKey = reader.ReadString();
            string metadata = reader.ReadString();
            int dataLength = reader.ReadInt32();
            byte[] data = ReadExactBytes(bufferedStream, dataLength);

            result.Add((assetKey, data, string.IsNullOrEmpty(metadata) ? null : metadata));
        }

        return result;
    }

    private static byte[] ReadExactBytes(Stream stream, int count)
    {
        if (count == 0) return Array.Empty<byte>();
        byte[] buffer = new byte[count];
        stream.ReadExactly(buffer, 0, count);
        return buffer;
    }

    /// <summary>
    /// Streams asset files from disk directly into a Zstandard-compressed bundle output stream.
    /// </summary>
    public static async Task StreamAssetsToBundleAsync(
        Stream outputStream,
        IEnumerable<(string AssetKey, string FilePath, string? Metadata)> assetFileInfos,
        int compressionLevel = 1,
        CancellationToken cancellationToken = default)
    {
        var validAssets = assetFileInfos.Where(a => !string.IsNullOrEmpty(a.FilePath) && File.Exists(a.FilePath)).ToList();

        using var zstdStream = new CompressionStream(outputStream, compressionLevel, leaveOpen: true);
        using var writer = new BinaryWriter(zstdStream, Encoding.UTF8, leaveOpen: true);

        writer.Write(validAssets.Count);
        writer.Flush();

        byte[] buffer = new byte[ChunkSize];

        foreach (var (assetKey, filePath, metadata) in validAssets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            writer.Write(assetKey);
            writer.Write(metadata ?? string.Empty);

            var fileInfo = new FileInfo(filePath);
            int length = (int)fileInfo.Length;
            writer.Write(length);
            writer.Flush();

            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, useAsync: true);
            int bytesRemaining = length;
            while (bytesRemaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int bytesToRead = Math.Min(buffer.Length, bytesRemaining);
                int read = await fileStream.ReadAsync(buffer.AsMemory(0, bytesToRead), cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException($"Unexpected end of stream while reading asset file '{filePath}'.");
                }
                await zstdStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                bytesRemaining -= read;
            }

            await zstdStream.FlushAsync(cancellationToken);
        }

        await zstdStream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Decompresses and extracts asset bundle entries from an input stream on the fly.
    /// </summary>
    public static async Task ExtractBundleFromStreamAsync(
        Stream inputStream,
        Func<string, string?, byte[], Task> onAssetExtractedAsync,
        CancellationToken cancellationToken = default)
    {
        using var zstdStream = new DecompressionStream(inputStream);
        using var bufferedStream = new BufferedStream(zstdStream, ChunkSize);
        using var reader = new BinaryReader(bufferedStream, Encoding.UTF8, leaveOpen: true);

        int count = reader.ReadInt32();

        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string assetKey = reader.ReadString();
            string metadata = reader.ReadString();
            int dataLength = reader.ReadInt32();

            byte[] data = await ReadExactBytesAsync(bufferedStream, dataLength, cancellationToken);

            await onAssetExtractedAsync(assetKey, string.IsNullOrEmpty(metadata) ? null : metadata, data);
        }
    }

    /// <summary>
    /// Asynchronously reads an exact count of bytes from a stream.
    /// </summary>
    public static async Task<byte[]> ReadExactBytesAsync(Stream stream, int count, CancellationToken cancellationToken = default)
    {
        if (count == 0) return Array.Empty<byte>();
        byte[] buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken);
            if (read == 0) throw new EndOfStreamException("Unexpected end of stream.");
            offset += read;
        }
        return buffer;
    }
}
