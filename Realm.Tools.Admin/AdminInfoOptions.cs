using CommandLine;

namespace Realm.Tools.Admin;

[Verb("info", HelpText = "Show registry server admin and storage information")]
public class AdminInfoOptions
{
	[Option('k', "key", Required = false, HelpText = "Path to admin .rkey file.")]
	public string? Key { get; set; }

	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}
