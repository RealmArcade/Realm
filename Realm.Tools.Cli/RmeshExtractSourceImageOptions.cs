using CommandLine;

namespace Realm.Tools.Cli;

[Verb("rmesh_extract_source_image", HelpText = "Extract the source reference image embedded in Realm .rmesh containers as WebP.")]
public class RmeshExtractSourceImageOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to a .rmesh file or directory containing .rmesh files.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory. Defaults to writing <name>.source.webp beside each input.")]
	public string? Output { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }
}
