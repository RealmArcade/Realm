using CommandLine;

namespace Realm.Tools.Admin;

[Verb("greenlight", HelpText = "Greenlight override for a map, bound to the creator who may publish it")]
public class AdminGreenlightOptions
{
	[Option('m', "map", Required = true, HelpText = "Map title or package name")]
	public string Map { get; set; } = string.Empty;

	[Option('c', "creator", Required = true, HelpText = "Creator username the override belongs to. Their name lock decides the key that may publish the map.")]
	public string Creator { get; set; } = string.Empty;

	[Option('k', "key", Required = false, HelpText = "Path to admin .rkey file.")]
	public string? Key { get; set; }

	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}
