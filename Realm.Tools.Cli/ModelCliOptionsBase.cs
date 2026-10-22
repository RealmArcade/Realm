using CommandLine;

namespace Realm.Tools.Cli;

public abstract class ModelCliOptionsBase
{
	[Option('i', "input", Required = true, HelpText = "Path to .rmesh or .glb file or directory containing model files.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory.")]
	public string? Output { get; set; }

	[Option("in-place", Required = false, Default = false, HelpText = "Overwrite the source file directly.")]
	public bool InPlace { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }

	[Option('t', "type", Required = false, HelpText = "Asset type for model: Character, Building, Prop, Item.")]
	public string? AssetType { get; set; }
}
