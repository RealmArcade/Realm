using CommandLine;

namespace Realm.Tools.Admin;

[Verb("info", HelpText = "Display server information")]
public class AdminInfoOptions
{
	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}