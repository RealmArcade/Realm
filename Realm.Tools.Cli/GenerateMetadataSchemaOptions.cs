using CommandLine;

namespace Realm.Tools.Cli;

[Verb("generate_metadata_schema", HelpText = "Generate the JSON Schema for MapMetadata (metadata.json).")]
public class GenerateMetadataSchemaOptions
{
	[Option('o', "output", Required = false, HelpText = "Output path to write metadata.schema.json.")]
	public string? Output { get; set; }
}