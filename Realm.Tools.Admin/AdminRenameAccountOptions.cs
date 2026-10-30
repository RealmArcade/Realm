using CommandLine;

namespace Realm.Tools.Admin;

[Verb("rename-account", HelpText = "Rename any Realm account with a signed administrator proof")]
public class AdminRenameAccountOptions
{
	[Option("account-id", Required = true, HelpText = "The account to rename, as its decimal AccountId.")]
	public long AccountId { get; set; }

	[Option('n', "name", Required = true, HelpText = "The new DisplayName. It is canonicalised before signing.")]
	public string DisplayName { get; set; } = string.Empty;

	[Option('k', "key", Required = false, HelpText = "Path to admin .rkey file.")]
	public string? Key { get; set; }

	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}
