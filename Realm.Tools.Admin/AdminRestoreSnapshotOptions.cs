using CommandLine;

namespace Realm.Tools.Admin;

[Verb("restore-snapshot", HelpText = "Restore a database state snapshot file to a target node")]
public class AdminRestoreSnapshotOptions
{
	[Option('i', "in", Required = true, HelpText = "Input JSON snapshot file path.")]
	public string InputFile { get; set; } = string.Empty;

	[Option('k', "key", Required = false, HelpText = "Path to admin .rkey key file. If omitted, defaults to %appdata%\\Godot\\app_userdata\\Realm\\appdata\\keys\\authorship_key_DO-NOT-SHARE.rkey")]
	public string? Key { get; set; }

	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}