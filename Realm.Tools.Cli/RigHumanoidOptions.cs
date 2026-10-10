using CommandLine;

namespace Realm.Tools.Cli;

[Verb("rig_humanoid", HelpText = "Auto-rig a humanoid 3D model with a Mixamo skeleton using the Make-It-Animatable pipeline and optimize into .rmesh.")]
public class RigHumanoidOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to input .rmesh or .glb file or directory containing model files.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory.")]
	public string? Output { get; set; }

	[Option("in-place", Required = false, Default = false, HelpText = "Overwrite the source file directly.")]
	public bool InPlace { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }

	[Option('t', "type", Required = false, HelpText = "Asset type for model: Character, Building, Prop, Item.")]
	public string? AssetType { get; set; }

	[Option("no-fingers", Required = false, Default = true, HelpText = "Model does not have ten separate fingers (default: true).")]
	public bool NoFingers { get; set; } = true;

	[Option("use-normals", Required = false, Default = false, HelpText = "Use normals to improve skinning when limbs are close together (default: false).")]
	public bool UseNormals { get; set; } = false;

	[Option("weight-postprocess", Required = false, Default = true, HelpText = "Apply empirical post-processing to blend weights (default: true).")]
	public bool WeightPostprocess { get; set; } = true;

	[Option("mia-dir", Required = false, HelpText = "Path to the ComfyUI_Make-It-Animatable directory. Falls back to MIA_DIR env var, then searches common relative paths.")]
	public string? MiaDir { get; set; }
}