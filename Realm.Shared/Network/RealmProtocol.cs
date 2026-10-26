using System.Security.Cryptography;

namespace Realm.Shared.Network;

public static class RealmProtocol
{
	public const int Version = 1;

	public const string ReadyLinePrefix = "REALM_SERVER_READY ";

	public const int TokenCharacters = 64;

	public const int FrameLengthPrefixBytes = 4;

	public const int FrameKindBytes = 1;

	public const int FrameHeaderBytes = FrameLengthPrefixBytes + FrameKindBytes;

	public const int MaximumFrameBytes = 256 * 1024 * 1024;

	public const int HandshakeTimeoutMilliseconds = 15000;

	public static string CreateToken() => RandomNumberGenerator.GetHexString(TokenCharacters, lowercase: false);
}
