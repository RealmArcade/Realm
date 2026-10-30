using System.Security.Cryptography;
using System.Text;

namespace Realm.Shared.Network;

public static class RealmProtocol
{
	public const string ReadyLinePrefix = "REALM_SERVER_READY ";

	public const int TokenCharacters = 64;

	public const int ChallengeCharacters = 64;

	public static string CreateToken() => RandomNumberGenerator.GetHexString(TokenCharacters, lowercase: false);

	public static string CreateChallenge() => RandomNumberGenerator.GetHexString(ChallengeCharacters, lowercase: false);

	public static string ProveToken(string token, string challenge)
	{
		byte[] key = Encoding.UTF8.GetBytes(token);
		byte[] message = Encoding.UTF8.GetBytes(challenge);
		return Convert.ToHexString(HMACSHA256.HashData(key, message));
	}

	public static int CreateSeed() => RandomNumberGenerator.GetInt32(1, int.MaxValue);
}
