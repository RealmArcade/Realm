using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Realm.AdminServer.Accounts;

/// <summary>
///     Hashes a Realm.BasicAuth password with scrypt (RFC 7914), which is hard in memory as well as in time, and verifies it in
///     constant time. The cost parameters are stored beside the hash so a later cost increase applies to new credentials
///     without invalidating existing ones.
/// </summary>
public static class ScryptPasswordHasher
{
	private const int DefaultLogarithmicCost = 14;

	private const int DefaultBlockSize = 8;

	private const int DefaultParallelisation = 1;

	private const int DerivedKeyBytes = 32;

	private const int SaltBytes = 16;

	private const int SalsaWords = 16;

	private const string SchemeIdentifier = "scrypt";

	public static string Hash(string password)
	{
		int cost = 1 << DefaultLogarithmicCost;
		byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
		byte[] derivedKey = DeriveKey(password, salt, cost, DefaultBlockSize, DefaultParallelisation, DerivedKeyBytes);
		return string.Join('$',
			SchemeIdentifier,
			cost.ToString(System.Globalization.CultureInfo.InvariantCulture),
			DefaultBlockSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
			DefaultParallelisation.ToString(System.Globalization.CultureInfo.InvariantCulture),
			Convert.ToBase64String(salt),
			Convert.ToBase64String(derivedKey));
	}

	public static bool Verify(string password, string? storedHash)
	{
		if (string.IsNullOrEmpty(storedHash)) return false;

		string[] parts = storedHash.Split('$');
		if (parts.Length != 6 || !string.Equals(parts[0], SchemeIdentifier, StringComparison.Ordinal)) return false;
		if (!int.TryParse(parts[1], out int cost) || !int.TryParse(parts[2], out int blockSize) || !int.TryParse(parts[3], out int parallelisation)) return false;
		if (cost < 2 || blockSize < 1 || parallelisation < 1) return false;

		byte[] salt;
		byte[] expected;
		try
		{
			salt = Convert.FromBase64String(parts[4]);
			expected = Convert.FromBase64String(parts[5]);
		}
		catch (FormatException)
		{
			return false;
		}

		byte[] actual = DeriveKey(password, salt, cost, blockSize, parallelisation, expected.Length);
		return CryptographicOperations.FixedTimeEquals(actual, expected);
	}

	/// <summary>
	///     scrypt: expand the password with PBKDF2 into p blocks of 128·r bytes, mix each block through ROMix, then compress the
	///     result with PBKDF2 again.
	/// </summary>
	public static byte[] DeriveKey(string password, byte[] salt, int cost, int blockSize, int parallelisation, int derivedKeyBytes)
	{
		byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
		int blockBytes = 128 * blockSize;
		byte[] mixedBlocks = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, 1, HashAlgorithmName.SHA256, parallelisation * blockBytes);

		uint[] blockWords = new uint[blockBytes / 4];
		for (int lane = 0; lane < parallelisation; lane++)
		{
			Span<byte> laneBytes = mixedBlocks.AsSpan(lane * blockBytes, blockBytes);
			ReadWords(laneBytes, blockWords);
			MixInPlace(blockWords, cost, blockSize);
			WriteWords(blockWords, laneBytes);
		}

		return Rfc2898DeriveBytes.Pbkdf2(passwordBytes, mixedBlocks, 1, HashAlgorithmName.SHA256, derivedKeyBytes);
	}

	/// <summary>
	///     ROMix: fill N blocks with the sequentially mixed state, then mix the state with pseudo-random blocks already held in
	///     memory. Holding all N blocks at once is the memory hardness: N·128·r bytes per parallelisation lane.
	/// </summary>
	private static void MixInPlace(uint[] block, int cost, int blockSize)
	{
		int wordsPerBlock = 32 * blockSize;
		uint[] table = new uint[(long)cost * wordsPerBlock];
		uint[] mixed = new uint[block.Length];
		uint[] salsaInput = new uint[SalsaWords];

		for (int iteration = 0; iteration < cost; iteration++)
		{
			Array.Copy(block, 0, table, iteration * wordsPerBlock, wordsPerBlock);
			BlockMix(block, mixed, salsaInput, blockSize);
		}

		for (int iteration = 0; iteration < cost; iteration++)
		{
			int tableOffset = (int)(Integerify(block, blockSize) & (uint)(cost - 1)) * wordsPerBlock;
			for (int word = 0; word < wordsPerBlock; word++)
			{
				block[word] ^= table[tableOffset + word];
			}

			BlockMix(block, mixed, salsaInput, blockSize);
		}
	}

	/// <summary>
	///     BlockMix: chain Salsa20/8 over the 2r 64-byte blocks, then interleave the even outputs before the odd outputs.
	/// </summary>
	private static void BlockMix(uint[] block, uint[] mixed, uint[] salsaInput, int blockSize)
	{
		Array.Copy(block, (2 * blockSize - 1) * SalsaWords, salsaInput, 0, SalsaWords);

		for (int index = 0; index < 2 * blockSize; index++)
		{
			int sourceOffset = index * SalsaWords;
			for (int word = 0; word < SalsaWords; word++)
			{
				salsaInput[word] ^= block[sourceOffset + word];
			}

			Salsa208(salsaInput);
			Array.Copy(salsaInput, 0, mixed, sourceOffset, SalsaWords);
		}

		for (int index = 0; index < blockSize; index++)
		{
			Array.Copy(mixed, 2 * index * SalsaWords, block, index * SalsaWords, SalsaWords);
			Array.Copy(mixed, (2 * index + 1) * SalsaWords, block, (blockSize + index) * SalsaWords, SalsaWords);
		}
	}

	private static void Salsa208(uint[] state)
	{
		Span<uint> working = stackalloc uint[SalsaWords];
		for (int word = 0; word < SalsaWords; word++) working[word] = state[word];

		for (int round = 0; round < 8; round += 2)
		{
			working[4] ^= RotateLeft(working[0] + working[12], 7);
			working[8] ^= RotateLeft(working[4] + working[0], 9);
			working[12] ^= RotateLeft(working[8] + working[4], 13);
			working[0] ^= RotateLeft(working[12] + working[8], 18);

			working[9] ^= RotateLeft(working[5] + working[1], 7);
			working[13] ^= RotateLeft(working[9] + working[5], 9);
			working[1] ^= RotateLeft(working[13] + working[9], 13);
			working[5] ^= RotateLeft(working[1] + working[13], 18);

			working[14] ^= RotateLeft(working[10] + working[6], 7);
			working[2] ^= RotateLeft(working[14] + working[10], 9);
			working[6] ^= RotateLeft(working[2] + working[14], 13);
			working[10] ^= RotateLeft(working[6] + working[2], 18);

			working[3] ^= RotateLeft(working[15] + working[11], 7);
			working[7] ^= RotateLeft(working[3] + working[15], 9);
			working[11] ^= RotateLeft(working[7] + working[3], 13);
			working[15] ^= RotateLeft(working[11] + working[7], 18);

			working[1] ^= RotateLeft(working[0] + working[3], 7);
			working[2] ^= RotateLeft(working[1] + working[0], 9);
			working[3] ^= RotateLeft(working[2] + working[1], 13);
			working[0] ^= RotateLeft(working[3] + working[2], 18);

			working[6] ^= RotateLeft(working[5] + working[4], 7);
			working[7] ^= RotateLeft(working[6] + working[5], 9);
			working[4] ^= RotateLeft(working[7] + working[6], 13);
			working[5] ^= RotateLeft(working[4] + working[7], 18);

			working[11] ^= RotateLeft(working[10] + working[9], 7);
			working[8] ^= RotateLeft(working[11] + working[10], 9);
			working[9] ^= RotateLeft(working[8] + working[11], 13);
			working[10] ^= RotateLeft(working[9] + working[8], 18);

			working[12] ^= RotateLeft(working[15] + working[14], 7);
			working[13] ^= RotateLeft(working[12] + working[15], 9);
			working[14] ^= RotateLeft(working[13] + working[12], 13);
			working[15] ^= RotateLeft(working[14] + working[13], 18);
		}

		for (int word = 0; word < SalsaWords; word++) state[word] += working[word];
	}

	private static uint Integerify(uint[] block, int blockSize)
	{
		int offset = (2 * blockSize - 1) * SalsaWords;
		return (uint)(block[offset] | ((ulong)block[offset + 1] << 32));
	}

	private static uint RotateLeft(uint value, int count)
	{
		return (value << count) | (value >> (32 - count));
	}

	private static void ReadWords(ReadOnlySpan<byte> source, uint[] destination)
	{
		for (int index = 0; index < destination.Length; index++)
		{
			destination[index] = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(index * 4, 4));
		}
	}

	private static void WriteWords(uint[] source, Span<byte> destination)
	{
		for (int index = 0; index < source.Length; index++)
		{
			BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(index * 4, 4), source[index]);
		}
	}
}
