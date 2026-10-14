using CommandLine;

namespace Realm.Tools.Admin;

[Verb("export-events", HelpText = "Export cluster events from the node to a local JSON file.")]
public class AdminExportEventsOptions
{
	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }

	[Option('o', "out", Required = false, Default = "cluster_events.json", HelpText = "Output JSON file path.")]
	public string OutputFile { get; set; } = "cluster_events.json";

	[Option("since", Required = false, HelpText = "Optional ISO timestamp (e.g. 2026-01-01T00:00:00Z) to fetch events since.")]
	public string? Since { get; set; }

	[Option('l', "limit", Required = false, Default = 500, HelpText = "Maximum number of events to export.")]
	public int Limit { get; set; } = 500;
}