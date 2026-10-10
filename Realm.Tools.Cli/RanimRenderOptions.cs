using CommandLine;

namespace Realm.Tools.Cli;

[Verb("ranim_render", HelpText = "Render .ranim skeletal animation files to animated WebP or PNG spritesheet.")]
public class RanimRenderOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to input .ranim file or directory.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory.")]
	public string? Output { get; set; }

	[Option("format", Required = false, Default = "auto", HelpText = "Output format: auto (default), webp, spritesheet.")]
	public string Format { get; set; } = "auto";

	[Option("fps", Required = false, Default = 12.0f, HelpText = "Target frames per second (default 12).")]
	public float Fps { get; set; } = 12.0f;

	[Option("max-frames", Required = false, HelpText = "Maximum frame count (uses modulus to skip intermediate frames).")]
	public int? MaxFrames { get; set; }

	[Option("size", Required = false, Default = 128, HelpText = "Frame width and height in pixels (default 128).")]
	public int Size { get; set; } = 128;

	[Option("scale", Required = false, Default = 1.0f, HelpText = "Model scale factor (default 1.0).")]
	public float Scale { get; set; } = 1.0f;

	[Option('m', "model", Required = false, HelpText = "Optional path to rigged humanoid .rmesh or .glb model to render instead of skeleton.")]
	public string? Model { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }

	[Option("no-border", Required = false, Default = false, HelpText = "Disable frame border.")]
	public bool NoBorder { get; set; }

	[Option("no-shadow", Required = false, Default = false, HelpText = "Disable floor shadow.")]
	public bool NoShadow { get; set; }

	[Option('q', "quality", Required = false, Default = 95, HelpText = "Encoding quality for WebP output (1-100, default 95).")]
	public int Quality { get; set; } = 95;

	[Option("lossless", Required = false, Default = false, HelpText = "Use lossless compression for WebP spritesheet output.")]
	public bool Lossless { get; set; }
}