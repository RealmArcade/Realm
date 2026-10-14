using CommandLine;

namespace Realm.Tools.Cli;

[Verb("texture_convert", HelpText = "Convert textures between standard image formats and .rtex format.")]
public class TextureConvertOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to input image / .rtex file or directory.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory.")]
	public string? Output { get; set; }

	[Option('t', "type", Required = false, HelpText = "Asset type for textures: Decal, Icon, Noise, Ribbon, Skybox, Spritesheet, Terrain, vfx_radial, vfx_vertical. If omitted, attempts to read type from image metadata.")]
	public string? AssetType { get; set; }

	[Option("columns", Required = false, HelpText = "Number of grid columns for spritesheets or animated decals (default 4 for spritesheets, 1 for decals).")]
	public int? Columns { get; set; }

	[Option("rows", Required = false, HelpText = "Number of grid rows for spritesheets or animated decals (default 4 for spritesheets, 1 for decals).")]
	public int? Rows { get; set; }

	[Option("in-place", Required = false, Default = false, HelpText = "Write output alongside input file.")]
	public bool InPlace { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }
}