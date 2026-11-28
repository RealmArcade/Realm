using CommandLine;

namespace Realm.Tools.Cli;

[Verb("mapgen", HelpText = "Render, reconstruct and measure Realm map terrain from minimap images.")]
public class MapGenerationOptions
{
	[Value(0, MetaName = "command", Required = true, HelpText = "One of: render, reconstruct, compare, benchmark, inspect.")]
	public string Command { get; set; } = string.Empty;

	[Value(1, MetaName = "input", Required = false, HelpText = "Map folder or minimap image the command reads.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output file or folder the command writes.")]
	public string? Output { get; set; }

	[Option('w', "width", Required = false, Default = 128, HelpText = "Generated terrain width in cells.")]
	public int Width { get; set; } = 128;

	[Option('d', "depth", Required = false, Default = 128, HelpText = "Generated terrain depth in cells.")]
	public int Depth { get; set; } = 128;

	[Option('r', "resolution", Required = false, Default = 512, HelpText = "Minimap render resolution in pixels.")]
	public int Resolution { get; set; } = 512;

	[Option('t', "template", Required = false, HelpText = "Map folder whose metadata supplies the swatch and prop catalog.")]
	public string? Template { get; set; }

	[Option('s', "seed", Required = false, Default = 0, HelpText = "Deterministic seed for scatter placement.")]
	public int Seed { get; set; }

	[Option('m', "maps", Required = false, HelpText = "Map folder names inside the benchmark directory, comma separated.")]
	public string? Maps { get; set; }

	[Option("map-directory", Required = false, HelpText = "Directory holding the map folders a benchmark reads.")]
	public string? MapDirectory { get; set; }

	[Option("report", Required = false, HelpText = "Path of the JSON report a benchmark writes.")]
	public string? Report { get; set; }

	[Option("use-thumbnails", Required = false, Default = false, HelpText = "Prefer a map folder's thumbnail.webp over a rendered minimap as benchmark input.")]
	public bool UseThumbnails { get; set; }
}
