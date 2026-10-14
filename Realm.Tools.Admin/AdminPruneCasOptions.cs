using CommandLine;

namespace Realm.Tools.Admin;

[Verb("prune-cas", HelpText = "Trigger server-side CAS integrity verification and orphan asset pruning.")]
public class AdminPruneCasOptions
{
	[Option('k', "key", Required = false, HelpText = "Path to admin .rkey key file. If omitted, defaults to %appdata%\\Godot\\app_userdata\\Realm\\appdata\\keys\\authorship_key_DO-NOT-SHARE.rkey")]
	public string? Key { get; set; }

	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}