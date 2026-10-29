using System.Security.Cryptography;
using System.Text;

namespace Realm.Shared.Network;

/// <summary>
///     The constants and the one piece of arithmetic both processes have to agree on before they can exchange a frame.
/// </summary>
public static class RealmProtocol
{
	public const int Version = 5;

	public const string ReadyLinePrefix = "REALM_SERVER_READY ";

	/// <summary>How many characters the ticket a match is started with has.</summary>
	public const int TokenCharacters = 64;

	/// <summary>
	///     How many characters one connection's challenge has. It is as long as the ticket because its only job is to
	///     make every answer to it different, so an answer seen on the wire is worth nothing on any other connection.
	/// </summary>
	public const int ChallengeCharacters = 64;

	public static string CreateToken() => RandomNumberGenerator.GetHexString(TokenCharacters, lowercase: false);

	/// <summary>A challenge for one connection, which is the authority's whole half of the handshake.</summary>
	public static string CreateChallenge() => RandomNumberGenerator.GetHexString(ChallengeCharacters, lowercase: false);

	/// <summary>
	///     The answer a peer gives to the challenge it was handed: an HMAC of the ticket over that challenge, so the
	///     ticket itself never crosses the wire. An answer is worthless to anyone who reads it - it proves possession of
	///     the ticket for one challenge only - which is the difference between this and sending the ticket along with
	///     the hello.
	/// </summary>
	/// <param name="token">The ticket the match was started with, or nothing when the match is open to anyone.</param>
	/// <param name="challenge">The challenge this connection was handed.</param>
	public static string ProveToken(string token, string challenge)
	{
		byte[] key = Encoding.UTF8.GetBytes(token);
		byte[] message = Encoding.UTF8.GetBytes(challenge);
		return Convert.ToHexString(HMACSHA256.HashData(key, message));
	}

	public static int CreateSeed() => RandomNumberGenerator.GetInt32(1, int.MaxValue);
}
