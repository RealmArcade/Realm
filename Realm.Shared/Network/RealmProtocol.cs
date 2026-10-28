using System.Security.Cryptography;

namespace Realm.Shared.Network;

public static class RealmProtocol
{
	public const int Version = 2;

	public const string ReadyLinePrefix = "REALM_SERVER_READY ";

	public const int TokenCharacters = 64;    public static string CreateToken() => RandomNumberGenerator.GetHexString(TokenCharacters, lowercase: false);

    public static int CreateSeed() => RandomNumberGenerator.GetInt32(1, int.MaxValue);
}
