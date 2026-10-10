using CommandLine;

namespace Realm.Tools.Admin;

[Verb("remove-manifest", HelpText = "Remove a published map manifest.json from the registry server")]
public class AdminRemoveManifestOptions
{
	[Option('m', "map", Required = true, HelpText = "Map title or package name")]
	public string Map { get; set; } = string.Empty;

	[Option('v', "version", Required = false, Default = null, HelpText = "Map version to remove. If omitted, all versions of the map are removed.")]
	public string? Version { get; set; }

	[Option('k', "key", Required = false, HelpText = "Path to admin .rkey key file. If omitted, defaults to %appdata%\\Godot\\app_userdata\\Realm\\appdata\\keys\\authorship_key_DO-NOT-SHARE.rkey")]
	public string? Key { get; set; }

	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }

	[Option('p', "prune", Required = false, Default = true, HelpText = "Automatically trigger CAS prune after removing the manifest to clear orphaned files.")]
	public bool Prune { get; set; } = true;
}