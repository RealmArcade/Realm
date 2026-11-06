using System;
using System.Text;

namespace Realm.Shared.Distribution;

/// <summary>
///     The payload of an account event the authority publishes to its peers. A peer refuses an event whose signature it cannot
///     verify against the configured account-authority keys, so only the authority can create accounts, link providers or move
///     a DisplayName.
/// </summary>
public class AccountClusterEventPayload
{
	public const string CreatedEvent = "account_created";

	public const string RenamedEvent = "account_renamed";

	public const string LinkAddedEvent = "account_link_added";

	public long AccountId { get; set; }

	public string DisplayName { get; set; } = string.Empty;

	public string UniquenessKey { get; set; } = string.Empty;

	public string? PreviousUniquenessKey { get; set; }

	public string? Provider { get; set; }

	public string? ProviderAccountToken { get; set; }

	public DateTime MutationTimestampUtc { get; set; } = DateTime.UtcNow;

	/// <summary>
	///     The exact text the authority signed. Every field that changes what a peer stores is included, so an event cannot be
	///     altered on the wire without invalidating it.
	/// </summary>
	public string BuildSignaturePayload(string eventType)
	{
		var builder = new StringBuilder();
		builder.Append(eventType).Append('|');
		builder.Append(AccountId.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('|');
		builder.Append(DisplayName).Append('|');
		builder.Append(UniquenessKey).Append('|');
		builder.Append(PreviousUniquenessKey ?? string.Empty).Append('|');
		builder.Append(Provider ?? string.Empty).Append('|');
		builder.Append(ProviderAccountToken ?? string.Empty).Append('|');
		builder.Append(MutationTimestampUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
		return builder.ToString();
	}

	public static string BuildLinkToken(string provider, string providerAccountToken)
	{
		return $"{provider.ToLowerInvariant()}:{providerAccountToken}";
	}
}
