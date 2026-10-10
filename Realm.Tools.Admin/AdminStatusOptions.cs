using CommandLine;

namespace Realm.Tools.Admin;

[Verb("status", HelpText = "Check the greenlight status and community metrics of a map")]
public class AdminStatusOptions
{
	[Option('m', "map", Required = true, HelpText = "Map title or package name")]
	public string Map { get; set; } = string.Empty;

	[Option('v', "version", Required = false, Default = null, HelpText = "Map version to inspect")]
	public string? Version { get; set; }

	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}