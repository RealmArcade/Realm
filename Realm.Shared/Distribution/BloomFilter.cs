using Blake3;
using System.Buffers.Binary;
using System.Text;

namespace Realm.Shared.Distribution;

public class BloomFilter
{
    private readonly byte[] _bitArray;
    private readonly int _bitCount;
    private readonly int _hashCount;
    private int _itemCount;

    public int BitCount => _bitCount;
    public int HashCount => _hashCount;
    public int ItemCount => _itemCount;
    public byte[] BitArray => _bitArray;

    public BloomFilter(int expectedElements = 1000, double falsePositiveRate = 0.01)
    {
        if (expectedElements <= 0)
        {
            expectedElements = 100;
        }

        if (falsePositiveRate <= 0.0 || falsePositiveRate >= 1.0)
        {
            falsePositiveRate = 0.01;
        }

        double optimalBits = -((double)expectedElements * Math.Log(falsePositiveRate)) / (Math.Log(2.0) * Math.Log(2.0));
        _bitCount = Math.Max(64, (int)Math.Ceiling(optimalBits));
        int byteCount = (_bitCount + 7) / 8;
        _bitCount = byteCount * 8;
        _bitArray = new byte[byteCount];

        double optimalHashes = ((double)_bitCount / (double)expectedElements) * Math.Log(2.0);
        _hashCount = Math.Clamp((int)Math.Round(optimalHashes), 1, 30);
    }

    public BloomFilter(int bitCount, int hashCount, byte[] bitArray, int itemCount = 0)
    {
        _bitCount = bitCount;
        _hashCount = Math.Clamp(hashCount, 1, 30);
        _bitArray = bitArray ?? new byte[(bitCount + 7) / 8];
        _itemCount = itemCount;
    }

    public void Add(string item)
    {
        if (string.IsNullOrEmpty(item))
        {
            return;
        }

        byte[] utf8Bytes = Encoding.UTF8.GetBytes(item);
        Span<byte> hashSpan = stackalloc byte[32];
        Hasher.Hash(utf8Bytes, hashSpan);

        ulong hash1 = BinaryPrimitives.ReadUInt64LittleEndian(hashSpan.Slice(0, 8));
        ulong hash2 = BinaryPrimitives.ReadUInt64LittleEndian(hashSpan.Slice(8, 8));
        if (hash2 == 0)
        {
            hash2 = 1;
        }

        for (int i = 0; i < _hashCount; i++)
        {
            ulong bitIndex = (hash1 + (ulong)i * hash2) % (ulong)_bitCount;
            int byteIndex = (int)(bitIndex / 8);
            int bitOffset = (int)(bitIndex % 8);
            _bitArray[byteIndex] |= (byte)(1 << bitOffset);
        }

        _itemCount++;
    }

    public bool Contains(string item)
    {
        if (string.IsNullOrEmpty(item) || _bitCount <= 0)
        {
            return false;
        }

        byte[] utf8Bytes = Encoding.UTF8.GetBytes(item);
        Span<byte> hashSpan = stackalloc byte[32];
        Hasher.Hash(utf8Bytes, hashSpan);

        ulong hash1 = BinaryPrimitives.ReadUInt64LittleEndian(hashSpan.Slice(0, 8));
        ulong hash2 = BinaryPrimitives.ReadUInt64LittleEndian(hashSpan.Slice(8, 8));
        if (hash2 == 0)
        {
            hash2 = 1;
        }

        for (int i = 0; i < _hashCount; i++)
        {
            ulong bitIndex = (hash1 + (ulong)i * hash2) % (ulong)_bitCount;
            int byteIndex = (int)(bitIndex / 8);
            int bitOffset = (int)(bitIndex % 8);
            if ((_bitArray[byteIndex] & (1 << bitOffset)) == 0)
            {
                return false;
            }
        }

        return true;
    }

    public byte[] ToByteArray()
    {
        byte[] result = new byte[12 + _bitArray.Length];
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0, 4), _bitCount);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4, 4), _hashCount);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8, 4), _itemCount);
        Buffer.BlockCopy(_bitArray, 0, result, 12, _bitArray.Length);
        return result;
    }

    public static BloomFilter FromByteArray(byte[] data)
    {
        if (data == null || data.Length < 12)
        {
            return new BloomFilter(100);
        }

        int bitCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0, 4));
        int hashCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4, 4));
        int itemCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8, 4));

        int expectedByteCount = (bitCount + 7) / 8;
        byte[] bitArray = new byte[expectedByteCount];
        int bytesToCopy = Math.Min(expectedByteCount, data.Length - 12);
        if (bytesToCopy > 0)
        {
            Buffer.BlockCopy(data, 12, bitArray, 0, bytesToCopy);
        }

        return new BloomFilter(bitCount, hashCount, bitArray, itemCount);
    }

    public static string ComputeHeaderHash(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return "empty";
        }

        string canonicalJson = AuthorSignatureHelper.CanonicalizeJson(metadataJson);
        byte[] utf8 = Encoding.UTF8.GetBytes(canonicalJson);
        Span<byte> hashSpan = stackalloc byte[32];
        Hasher.Hash(utf8, hashSpan);
        return Convert.ToHexString(hashSpan).ToLowerInvariant();
    }

    public static string CreateManifestKey(string manifestBlake3)
    {
        return ContentAddressableStorage.NormalizeBlake3Hash(manifestBlake3);
    }

    public static string CreateHeaderKey(string blake3Hash, string? metadataJson)
    {
        string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(blake3Hash);
        string headerHash = ComputeHeaderHash(metadataJson);
        return $"{normalizedHash}:{headerHash}";
    }

    public static BloomFilter FromBase64(string base64, int bitCount, int hashCount, int itemCount = 0)
    {
        byte[] bitArray = !string.IsNullOrEmpty(base64) ? Convert.FromBase64String(base64) : new byte[(bitCount + 7) / 8];
        return new BloomFilter(bitCount, hashCount, bitArray, itemCount);
    }
}
