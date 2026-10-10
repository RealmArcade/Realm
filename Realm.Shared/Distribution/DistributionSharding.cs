using System.IO.Hashing;
using System.Text;

namespace Realm.Shared.Distribution;

public static class DistributionSharding
{
    public static ulong ComputeShardHash(string seederIdentifier, string blake3Hash)
    {
        string normalizedHash = blake3Hash.Split('.')[0].Trim().ToLowerInvariant();
        byte[] seederBytes = Encoding.UTF8.GetBytes(seederIdentifier);
        byte[] hashBytes = Encoding.UTF8.GetBytes(normalizedHash);
        
        byte[] combined = new byte[seederBytes.Length + hashBytes.Length];
        Buffer.BlockCopy(seederBytes, 0, combined, 0, seederBytes.Length);
        Buffer.BlockCopy(hashBytes, 0, combined, seederBytes.Length, hashBytes.Length);

        return XxHash64.HashToUInt64(combined);
    }

    public static bool SeederAcceptsHash(string seederIdentifier, int capacityPercentage, string blake3Hash)
    {
        if (capacityPercentage >= 100)
        {
            return true;
        }

        if (capacityPercentage <= 0)
        {
            return false;
        }

        ulong shardHash = ComputeShardHash(seederIdentifier, blake3Hash);
        return (shardHash % 100) < (ulong)capacityPercentage;
    }
}
