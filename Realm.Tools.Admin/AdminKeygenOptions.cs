using CommandLine;

namespace Realm.Tools.Admin;

[Verb("keygen", HelpText = "Generate an encrypted Ed25519 admin key pair for authorizing registry admin actions.")]
public class AdminKeygenOptions
{
	[Option('p', "password", Required = true, HelpText = "Strong password used to encrypt the admin private key. Never stored on disk.")]
	public string Password { get; set; } = string.Empty;

	[Option('u', "username", Required = false, HelpText = "Display name / username to associate with this admin key pair.")]
	public string? Username { get; set; }

	[Option('o', "output", Required = false, HelpText = "Directory to write server_admin_key_DO-NOT-SHARE.rkey. Defaults to the standard admin key directory used by this tool.")]
	public string? Output { get; set; }
}
