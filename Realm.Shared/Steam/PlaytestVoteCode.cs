using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Realm.Shared.Steam;

/// <summary>
///     The two answers a playtester can give about a map. A downvote is feedback the author reads; it never subtracts from
///     the number of testers who played the map and never blocks a publication.
/// </summary>
public enum PlaytestVote : byte
{
	/// <summary>The tester played the map and does not think it is ready for strangers yet.</summary>
	NotReady = 0,

	/// <summary>The tester played the map and thinks it holds up in front of other players.</summary>
	Playtested = 1
}

/// <summary>One vote as it was read back out of a code.</summary>
/// <param name="SteamId">The SteamID64 of the account that played the map, as the authority asserted it.</param>
/// <param name="MapKey">The map the vote is about, so a code minted for one map is refused by another.</param>
/// <param name="Vote">The answer the tester gave.</param>
/// <param name="IssuedAt">When the vote was recorded, which is provenance only: no code is ever refused for its age.</param>
public readonly record struct PlaytestVoteRecord(ulong SteamId, string MapKey, PlaytestVote Vote, DateTimeOffset IssuedAt);

/// <summary>
///     The code a tester hands to a map's author, and the envelope both sides read it with.
///     <para>
///         The wire form is <c>base64url(version ‖ nonce ‖ tag ‖ ciphertext)</c>: one leading format byte, a 12-byte random
///         nonce, a 16-byte authentication tag and the encrypted JSON payload <c>{steamId, mapKey, vote, issuedAt}</c>.
///         Editing any byte of a code fails the tag and the code is refused rather than read as a different vote, which
///         is what makes this tamper <em>resistant</em> rather than tamper proof: the key below ships in every build, so
///         anyone who reads this source can mint a code for any SteamID. The design accepts that on purpose. The bar this
///         feature defends is a player who did not realise their map was unfinished, and Steam's own floor - an account
///         that has spent money - is what stands between the catalogue and a determined spammer. Nothing here is a
///         security boundary, nothing is verified, and nothing is sent to Realm.
///     </para>
///     <para>
///         The reason for a code at all, rather than plain text in a chat channel, is that a code is safe to paste where
///         people are watching: it states a SteamID and a lukewarm opinion, and both are things a tester may not want
///         attached to their name. That is why the payload travels encrypted and why the SteamID is never printed beside
///         the code.
///     </para>
/// </summary>
public static class PlaytestVoteCode
{
	/// <summary>The leading byte of the envelope, so a later format is never misread as this one.</summary>
	public const byte FormatVersion = 1;

	private const int NonceBytes = 12;

	private const int TagBytes = 16;

	private const int MinimumEnvelopeBytes = 1 + NonceBytes + TagBytes + 1;

	private const int MaximumEnvelopeBytes = 4096;

	private const string KeyMaterial = "Realm playtest ledger envelope v1 - not a secret, ships in every build";

	private static readonly byte[] Key = SHA256.HashData(Encoding.UTF8.GetBytes(KeyMaterial));

	private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("realm.playtest.vote");

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.Never,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	/// <summary>
	///     Turns one vote into the code its tester can copy and paste into a chat channel.
	/// </summary>
	/// <param name="steamId">The SteamID64 the authority seated the voter as.</param>
	/// <param name="mapKey">The key of the map that was played.</param>
	/// <param name="vote">The answer the tester gave.</param>
	/// <param name="issuedAt">When the vote was recorded.</param>
	/// <returns>The opaque code, safe to paste in a public channel.</returns>
	public static string Create(ulong steamId, string mapKey, PlaytestVote vote, DateTimeOffset issuedAt)
	{
		var payload = new Envelope
		{
			SteamId = steamId,
			MapKey = mapKey ?? string.Empty,
			Vote = (int)vote,
			IssuedAt = issuedAt.ToUnixTimeSeconds()
		};

		byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
		byte[] nonce = RandomNumberGenerator.GetBytes(NonceBytes);
		byte[] ciphertext = new byte[plaintext.Length];
		byte[] tag = new byte[TagBytes];

		using (var aes = new AesGcm(Key, TagBytes))
		{
			aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData);
		}

		byte[] envelope = new byte[1 + NonceBytes + TagBytes + ciphertext.Length];
		envelope[0] = FormatVersion;
		nonce.CopyTo(envelope, 1);
		tag.CopyTo(envelope, 1 + NonceBytes);
		ciphertext.CopyTo(envelope, 1 + NonceBytes + TagBytes);

		return ToBase64Url(envelope);
	}

	/// <summary>
	///     Reads a code back, refusing it rather than guessing when its bytes were edited, it belongs to a later format or
	///     it is not a code at all.
	/// </summary>
	/// <param name="code">The code as it was pasted.</param>
	/// <param name="record">The vote, when the code opened.</param>
	/// <param name="failure">Why the code was refused, empty on success.</param>
	/// <returns>Whether the code opened.</returns>
	public static bool TryRead(string? code, out PlaytestVoteRecord record, out string failure)
	{
		record = default;
		failure = string.Empty;

		if (string.IsNullOrWhiteSpace(code))
		{
			failure = "The code is empty.";
			return false;
		}

		byte[] envelope;
		try
		{
			envelope = FromBase64Url(code.Trim());
		}
		catch (FormatException)
		{
			failure = "That is not a vote code.";
			return false;
		}

		if (envelope.Length < MinimumEnvelopeBytes || envelope.Length > MaximumEnvelopeBytes)
		{
			failure = "That is not a vote code.";
			return false;
		}

		if (envelope[0] != FormatVersion)
		{
			failure = "That vote code was made by a newer build of the game.";
			return false;
		}

		ReadOnlySpan<byte> nonce = envelope.AsSpan(1, NonceBytes);
		ReadOnlySpan<byte> tag = envelope.AsSpan(1 + NonceBytes, TagBytes);
		ReadOnlySpan<byte> ciphertext = envelope.AsSpan(1 + NonceBytes + TagBytes);
		byte[] plaintext = new byte[ciphertext.Length];

		try
		{
			using var aes = new AesGcm(Key, TagBytes);
			aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData);
		}
		catch (CryptographicException)
		{
			failure = "That vote code was damaged and was not counted.";
			return false;
		}

		Envelope? payload;
		try
		{
			payload = JsonSerializer.Deserialize<Envelope>(plaintext, JsonOptions);
		}
		catch (JsonException)
		{
			failure = "That vote code could not be read.";
			return false;
		}

		if (payload == null || payload.SteamId == 0 || string.IsNullOrEmpty(payload.MapKey))
		{
			failure = "That vote code could not be read.";
			return false;
		}

		if (payload.Vote != (int)PlaytestVote.Playtested && payload.Vote != (int)PlaytestVote.NotReady)
		{
			failure = "That vote code could not be read.";
			return false;
		}

		record = new PlaytestVoteRecord(
			payload.SteamId,
			payload.MapKey,
			(PlaytestVote)payload.Vote,
			DateTimeOffset.FromUnixTimeSeconds(payload.IssuedAt));
		return true;
	}

	/// <summary>
	///     Reads a code and refuses it when it was minted for a different map, which is what stops a tester's vote for one
	///     map from counting towards another.
	/// </summary>
	/// <param name="code">The code as it was pasted.</param>
	/// <param name="mapKey">The map the author is publishing.</param>
	/// <param name="record">The vote, when the code opened and names this map.</param>
	/// <param name="failure">Why the code was refused, empty on success.</param>
	/// <returns>Whether the code counts for this map.</returns>
	public static bool TryReadFor(string? code, string mapKey, out PlaytestVoteRecord record, out string failure)
	{
		if (!TryRead(code, out record, out failure)) return false;

		if (!string.Equals(record.MapKey, mapKey, StringComparison.Ordinal))
		{
			failure = "That code is for a different map.";
			record = default;
			return false;
		}

		return true;
	}

	private static string ToBase64Url(byte[] bytes)
	{
		return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	}

	private static byte[] FromBase64Url(string code)
	{
		string padded = code.Replace('-', '+').Replace('_', '/');
		switch (padded.Length % 4)
		{
			case 2: padded += "=="; break;
			case 3: padded += "="; break;
		}

		return Convert.FromBase64String(padded);
	}

	private sealed class Envelope
	{
		public ulong SteamId { get; set; }

		public string MapKey { get; set; } = string.Empty;

		public int Vote { get; set; }

		public long IssuedAt { get; set; }
	}
}
