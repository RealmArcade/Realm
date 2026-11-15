using CommandLine;

namespace Realm.Tools.Cli;

[Verb("mesh_render", HelpText = "Render .rmesh or .glb models into deterministic turnaround images for visual review by an agent or a person.")]
public class MeshRenderOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to a .rmesh or .glb file, or a directory containing them.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output image file (sheet mode) or output directory (views mode). Defaults to beside the input. Views mode writes each model's views into its own folder named after the model.")]
	public string? Output { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }

	[Option("mode", Required = false, Default = "sheet", HelpText = "sheet writes one image per model holding every view; views writes one image per view.")]
	public string Mode { get; set; } = "sheet";

	[Option("size", Required = false, Default = 512, HelpText = "Width and height of each view in pixels (default 512).")]
	public int Size { get; set; } = 512;

	[Option("views", Required = false, Default = "front,three_quarter,side,back", HelpText = "Comma separated views drawn left to right. Known views: front, three_quarter, side, back, left.")]
	public string Views { get; set; } = "front,three_quarter,side,back";

	[Option("background", Required = false, Default = "#14171F", HelpText = "Card background as #RRGGBB, or 'transparent' to keep the alpha channel.")]
	public string Background { get; set; } = "#14171F";

	[Option("engine", Required = false, Default = "auto", HelpText = "Render engine: auto (EEVEE with a Workbench fallback), eevee, workbench.")]
	public string Engine { get; set; } = "auto";

	[Option("samples", Required = false, Default = 32, HelpText = "EEVEE render samples (default 32).")]
	public int Samples { get; set; } = 32;

	[Option("elevation", Required = false, Default = 18.0f, HelpText = "Camera elevation above the horizon in degrees (default 18).")]
	public float Elevation { get; set; } = 18.0f;

	[Option("no-labels", Required = false, Default = false, HelpText = "Do not draw the view name under each image.")]
	public bool NoLabels { get; set; }

	[Option("despill", Required = false, Default = false, HelpText = "Remove the baked player colour from the model before rendering, so the render matches what the game draws.")]
	public bool Despill { get; set; }

	[Option("chroma-key", Required = false, Default = "#FF00FF", HelpText = "Chroma key colour used by --despill, as #RRGGBB or 'auto'.")]
	public string ChromaKey { get; set; } = "#FF00FF";

	[Option("no-cache", Required = false, Default = false, HelpText = "Always render, ignoring previously cached renders of the same content and settings.")]
	public bool NoCache { get; set; }

	[Option("cache-dir", Required = false, HelpText = "Directory holding cached renders. Defaults to %LOCALAPPDATA%/Realm/asset_review/mesh_render.")]
	public string? CacheDir { get; set; }
}
