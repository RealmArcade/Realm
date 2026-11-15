using CommandLine;

namespace Realm.Tools.Cli;

[Verb("image_sheet", HelpText = "Compose images into one labelled grid and write a JSON index mapping every cell to its source file.")]
public class ImageSheetOptions
{
	[Option('i', "input", Required = true, Separator = ';', HelpText = "An image file or a directory of images. Several paths may be given separated by ';'; directories are expanded.")]
	public IEnumerable<string> Input { get; set; } = Array.Empty<string>();

	[Option('o', "output", Required = true, HelpText = "Path of the PNG sheet to write.")]
	public string Output { get; set; } = string.Empty;

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Expand directories recursively.")]
	public bool Recursive { get; set; }

	[Option("columns", Required = false, Default = 4, HelpText = "Number of cells per row (default 4).")]
	public int Columns { get; set; } = 4;

	[Option("cell", Required = false, Default = 256, HelpText = "Width and height of the image area of one cell in pixels (default 256).")]
	public int Cell { get; set; } = 256;

	[Option("padding", Required = false, Default = 8, HelpText = "Gap between cells and around the sheet in pixels (default 8).")]
	public int Padding { get; set; } = 8;

	[Option("background", Required = false, Default = "#14171F", HelpText = "Sheet background as #RRGGBB.")]
	public string Background { get; set; } = "#14171F";

	[Option("label", Required = false, Default = "name", HelpText = "What each cell is labelled with: name (default), index, path, none.")]
	public string Label { get; set; } = "name";

	[Option("title", Required = false, HelpText = "Optional title drawn above the grid.")]
	public string? Title { get; set; }

	[Option("index", Required = false, HelpText = "Path of the JSON index to write. Defaults to the sheet path with a .json extension.")]
	public string? IndexPath { get; set; }
}
