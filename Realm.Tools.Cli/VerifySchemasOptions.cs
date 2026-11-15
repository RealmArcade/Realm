using CommandLine;

namespace Realm.Tools.Cli;

[Verb("verify_schemas", HelpText = "Verify that the committed map JSON schemas still match the model, failing on drift.")]
public class VerifySchemasOptions
{
	[Option('o', "output", Required = false, HelpText = "Directory holding the committed manifest.schema.json, metadata.schema.json and terrain.schema.json.")]
	public string? Output { get; set; }
}
