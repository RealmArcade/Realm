using CommandLine;

namespace Realm.Tools.Cli;

[Verb("generate_terrain_schema", HelpText = "Generate the JSON Schema for MapTerrain (terrain.json).")]
public class GenerateTerrainSchemaOptions
{
	[Option('o', "output", Required = false, HelpText = "Output path to write terrain.schema.json.")]
	public string? Output { get; set; }
}