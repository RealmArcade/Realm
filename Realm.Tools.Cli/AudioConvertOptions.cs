using CommandLine;

namespace Realm.Tools.Cli;

[Verb("audio_convert", HelpText = "Convert audio files (mp3, wav, flac, aac, ogg) to .raud format (or extract .raud to .ogg).")]
public class AudioConvertOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to input audio file or directory containing audio files.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory.")]
	public string? Output { get; set; }

	[Option('t', "type", Required = false, HelpText = "Asset type for audio: Music, SoundEffect.")]
	public string? AssetType { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }
}