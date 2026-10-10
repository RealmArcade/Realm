using CommandLine;

namespace Realm.Tools.Admin;

[Verb("digest", HelpText = "Query and verify BLAKE3 database state digest across one or more nodes.")]
public class AdminDigestOptions
{
	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}