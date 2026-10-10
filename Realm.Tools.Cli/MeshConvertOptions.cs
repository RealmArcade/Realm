using CommandLine;

namespace Realm.Tools.Cli;

[Verb("mesh_convert", HelpText = "Convert and optimize 3D models (GLB, RMESH, OBJ, FBX) into Realm .rmesh format (or export .rmesh to .glb).")]
public class MeshConvertOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to 3D model file (.glb, .rmesh, .obj, .fbx) or directory containing assets.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory.")]
	public string? Output { get; set; }

	[Option("in-place", Required = false, Default = false, HelpText = "Modify files in-place.")]
	public bool InPlace { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }

	[Option('f', "force", Required = false, Default = false, HelpText = "Force re-optimization even if already optimized.")]
	public bool Force { get; set; }

	[Option('t', "type", Required = false, HelpText = "Asset type for model: Character, Building, Prop, Item.")]
	public string? AssetType { get; set; }

	[Option("chroma_key", Required = false, HelpText = "Chroma key color in hex (e.g. #FF00FF) or 'auto' to automatically detect the dominant vibrant chroma key.")]
	public string? ChromaKey { get; set; }
}