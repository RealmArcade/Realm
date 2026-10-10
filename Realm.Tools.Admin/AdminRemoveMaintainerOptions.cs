using CommandLine;

namespace Realm.Tools.Admin;

[Verb("remove-maintainer", HelpText = "Revoke an authorized maintainer public key from a greenlit map")]
public class AdminRemoveMaintainerOptions
{
	[Option('m', "map", Required = true, HelpText = "Map title or package name")]
	public string Map { get; set; } = string.Empty;

	[Option("target-key", Required = false, HelpText = "Path to the maintainer's .rkey file.")]
	public string? TargetKey { get; set; }

	[Option('p', "pubkey", Required = false, HelpText = "Maintainer's Ed25519 public key in Base64 to remove.")]
	public string? PubKey { get; set; }

	[Option('k', "key", Required = false, HelpText = "Path to author or admin .rkey file. If omitted, defaults to %appdata%\\Godot\\app_userdata\\Realm\\appdata\\keys\\authorship_key_DO-NOT-SHARE.rkey")]
	public string? Key { get; set; }

	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}