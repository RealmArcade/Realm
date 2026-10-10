using CommandLine;

namespace Realm.Tools.Cli;

[Verb("metadata", HelpText = "Manage embedded Realm metadata (read, add, remove) in .rmesh, .raud, .rtex, .ranim, .glb, or .ogg files.")]
public class MetadataOptions
{
	[Option('m', "mode", Required = false, Default = "read", HelpText = "Operation mode: read (default), add, update, remove.")]
	public string Mode { get; set; } = "read";

	[Option('i', "input", Required = true, HelpText = "Path to asset file or directory containing asset files.")]
	public string Input { get; set; } = string.Empty;

	[Option('d', "data", Required = false, HelpText = "JSON string or path to JSON file containing metadata to embed (for add/update mode).")]
	public string? Data { get; set; }

	[Option('t', "type", Required = false, HelpText = "Asset type to embed: Character, Building, Prop, Item, Decal, Icon, Noise, Ribbon, Skybox, Spritesheet, Terrain, vfx_radial, vfx_vertical, Animation, Music, SoundEffect.")]
	public string? AssetType { get; set; }

	[Option('o', "output", Required = false, HelpText = "Output destination file to write extracted JSON (for read mode).")]
	public string? Output { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }
}