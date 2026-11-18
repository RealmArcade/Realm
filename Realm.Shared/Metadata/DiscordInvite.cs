using System;

namespace Realm.Shared.Metadata;

/// <summary>
///     The one creator link the game shows beyond Steam: an optional permanent Discord server invite the creator writes
///     into their map. It accepts only an https invite on a Discord invite host and normalises the short form, so a map
///     can never carry a link that opens a file, a script or somewhere the creator did not mean. It deliberately does not
///     resolve the invite: an invite URL carries no expiry, so a lapsed invite is the creator's to correct.
/// </summary>
public static class DiscordInvite
{
	/// <summary>How long an invite link may be, which is far more than any Discord invite needs.</summary>
	public const int MaximumLength = 200;

	private const string ShortHost = "discord.gg";
	private const string LongHost = "discord.com";
	private const string LegacyLongHost = "discordapp.com";

	/// <summary>
	///     Reads a link a creator typed and answers the canonical form to store, or refuses it with the reason.
	/// </summary>
	/// <param name="candidate">What the creator pasted, which may be a bare <c>discord.gg/code</c>.</param>
	/// <param name="normalized">The https invite to store, empty when the link was refused.</param>
	/// <returns>Whether the link is a Discord invite this game may open.</returns>
	public static bool TryNormalize(string? candidate, out string normalized)
	{
		normalized = string.Empty;
		if (string.IsNullOrWhiteSpace(candidate)) return false;

		string trimmed = candidate.Trim();
		if (trimmed.Length > MaximumLength) return false;

		string withScheme = trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase)
			? trimmed
			: $"https://{trimmed}";

		if (!Uri.TryCreate(withScheme, UriKind.Absolute, out Uri? uri)) return false;
		if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
		if (uri.UserInfo.Length > 0) return false;

		string host = uri.Host.ToLowerInvariant();
		string path = uri.AbsolutePath.TrimEnd('/');

		if (string.Equals(host, ShortHost, StringComparison.Ordinal))
		{
			if (!IsInviteCode(path.TrimStart('/'))) return false;
			normalized = $"https://{ShortHost}{path}";
			return true;
		}

		if (string.Equals(host, LongHost, StringComparison.Ordinal) || string.Equals(host, LegacyLongHost, StringComparison.Ordinal))
		{
			string invitePath = path.StartsWith("/invite/", StringComparison.OrdinalIgnoreCase) ? path["/invite/".Length..] : string.Empty;
			if (!IsInviteCode(invitePath)) return false;

			normalized = $"https://{host}/invite/{invitePath}";
			return true;
		}

		return false;
	}

	/// <summary>Whether a stored link is still one this game may open, which is checked before it is drawn.</summary>
	/// <param name="stored">The link as the map states it.</param>
	/// <returns>Whether the link is a Discord invite.</returns>
	public static bool IsValid(string? stored) => TryNormalize(stored, out _);

	private static bool IsInviteCode(string code)
	{
		if (code.Length == 0 || code.Length > 64) return false;

		foreach (char character in code)
		{
			if (char.IsAsciiLetterOrDigit(character) || character == '-' || character == '_') continue;
			return false;
		}

		return true;
	}
}
