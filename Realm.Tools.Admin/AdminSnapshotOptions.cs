using CommandLine;

namespace Realm.Tools.Admin;

[Verb("snapshot", HelpText = "Download a complete database state snapshot from a node to a local JSON file.")]
public class AdminSnapshotOptions
{
	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }

	[Option('o', "out", Required = false, Default = "cluster_snapshot.json", HelpText = "Output JSON snapshot file path.")]
	public string OutputFile { get; set; } = "cluster_snapshot.json";
}