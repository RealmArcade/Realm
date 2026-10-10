using CommandLine;

namespace Realm.Tools.Cli;

[Verb("generate_manifest_schema", HelpText = "Generate the JSON Schema for MapManifest assets.")]
public class GenerateManifestSchemaOptions
{
	[Option('o', "output", Required = false, HelpText = "Output path to write manifest.schema.json.")]
	public string? Output { get; set; }
}