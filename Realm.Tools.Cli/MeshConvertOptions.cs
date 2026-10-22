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

	[Option("surface_category", Required = false, HelpText = "Surface category for LOD decimation: auto (detect from mesh features), organic, or hard_edged. Defaults to auto.")]
	public string? SurfaceCategory { get; set; }

	[Option("no_uv_chart_reduction", Required = false, Default = false, HelpText = "Skip the UV chart reduction stage so LOD decimation keeps every UV seam as a hard boundary.")]
	public bool NoUvChartReduction { get; set; }

	[Option("uv_chart_tolerance", Required = false, HelpText = "Maximum UV distance in atlas space for two seam vertices to count as one chart. Defaults to 1/2048.")]
	public float UvChartTolerance { get; set; }

	[Option("chroma_key", Required = false, HelpText = "Chroma key color in hex (e.g. #FF00FF) or 'auto' to automatically detect the dominant vibrant chroma key.")]
	public string? ChromaKey { get; set; }

	[Option("source_image", Required = false, HelpText = "Path to the source reference image to embed in the produced .rmesh as lossy WebP at quality 80. Embedding is disabled by default.")]
	public string? SourceImage { get; set; }

	[Option("strip_source_image", Required = false, Default = false, HelpText = "Drop an existing embedded source reference image instead of carrying it forward when re-converting an .rmesh.")]
	public bool StripSourceImage { get; set; }

	[Option("extract_source_image", Required = false, HelpText = "Path to write the embedded source reference image (WebP) of an .rmesh input.")]
	public string? ExtractSourceImage { get; set; }

	public byte[]? SourceProfileImageWebp { get; set; }
}