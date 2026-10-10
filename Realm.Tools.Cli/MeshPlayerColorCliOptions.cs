using CommandLine;
using Realm.Shared.ModelOptimization;

namespace Realm.Tools.Cli;

[Verb("mesh_player_color", HelpText = "Extract chroma key color from a 3D model, isolate player-color area via face topology, pack mask into Red channel of ORM texture, and re-optimize into .rmesh.")]
public class MeshPlayerColorCliOptions
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

	[Option("chroma_key", Required = false, Default = "#FF00FF", HelpText = "Chroma key color in hex (e.g. #FF00FF) or 'auto' to automatically detect the dominant vibrant chroma key.")]
	public string ChromaKey { get; set; } = "#FF00FF";

	[Option("auto_correct_chroma_key", Required = false, Default = true, HelpText = "Auto-correct input chroma key to the closest matching color in the texture (true/false).")]
	public bool AutoCorrectChromaKey { get; set; } = true;

	[Option("core-threshold", Required = false, Default = 0.88f, HelpText = "Chromaticity dot-product threshold for high-confidence core texels (default: 0.88).")]
	public float CoreThreshold { get; set; } = 0.88f;

	[Option("fringe-threshold", Required = false, Default = 0.80f, HelpText = "Chromaticity dot-product threshold for fringe/edge expansion (default: 0.80).")]
	public float FringeThreshold { get; set; } = 0.80f;

	[Option("min-cluster-faces", Required = false, Default = 10, HelpText = "Minimum connected 3D face count to keep a cluster (default: 10).")]
	public int MinClusterFaces { get; set; } = 10;

	[Option("dilation-radius", Required = false, Default = 3, HelpText = "UV gutter dilation radius in pixels (default: 3).")]
	public int DilationRadius { get; set; } = 3;

	[Option("crease-angle", Required = false, Default = GlbMeshSmoother.DefaultCreaseAngleDegrees, HelpText = "Crease angle threshold in degrees for smooth vs flat surface partitioning (default: 60.0).")]
	public float CreaseAngleDegrees { get; set; } = GlbMeshSmoother.DefaultCreaseAngleDegrees;
}