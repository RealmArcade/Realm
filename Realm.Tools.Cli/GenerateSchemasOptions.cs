using CommandLine;

namespace Realm.Tools.Cli;

[Verb("generate_schemas", HelpText = "Generate all map JSON schemas (manifest, metadata, terrain).")]
public class GenerateSchemasOptions
{
	[Option('o', "output", Required = false, HelpText = "Target directory to write all generated schema files.")]
	public string? Output { get; set; }
}