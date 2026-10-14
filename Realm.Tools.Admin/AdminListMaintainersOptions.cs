using CommandLine;

namespace Realm.Tools.Admin;

[Verb("list-maintainers", HelpText = "List all authorized maintainers for a map")]
public class AdminListMaintainersOptions
{
	[Option('m', "map", Required = true, HelpText = "Map title or package name")]
	public string Map { get; set; } = string.Empty;

	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}