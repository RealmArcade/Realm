using CommandLine;

namespace Realm.Tools.Admin;

[Verb("unlock-name", HelpText = "Override the public key tied to a creator's username")]
public class AdminUnlockNameOptions
{
	[Option('u', "username", Required = true, HelpText = "Creator's Username")]
	public string Username { get; set; } = string.Empty;

	[Option('k', "key", Required = false, HelpText = "Path to admin .rkey key file. If omitted, defaults to %appdata%\\Godot\\app_userdata\\Realm\\appdata\\keys\\authorship_key_DO-NOT-SHARE.rkey")]
	public string? Key { get; set; }

	[Option("target-key", Required = true, HelpText = "Path to the target user's .rkey key file.")]
	public string TargetKey { get; set; } = string.Empty;

	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}