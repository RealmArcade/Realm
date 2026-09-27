using System;
using System.IO;
using System.Text.Json.Nodes;
using Realm.Shared.Metadata;

namespace Realm.Shared.Audio;

public class AudioConversionResult
{
	public bool Success { get; set; }
	public string InputPath { get; set; } = string.Empty;
	public string OutputPath { get; set; } = string.Empty;
	public string ErrorMessage { get; set; } = string.Empty;
	public string? AssetType { get; set; }
	public string? Author { get; set; }
	public string? PreferredFileName { get; set; }
	public byte[]? OutputBytes { get; set; }
}

public static class AudioConverter
{
	public static readonly string[] SupportedExtensions =
	[
		".mp3", ".wav", ".aiff", ".aif", ".flac", ".aac", ".m4a", ".wma", ".ogg", ".opus", ".raud"
	];

	public static bool IsAudioFile(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath)) return false;
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		return Array.Exists(SupportedExtensions, e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));
	}

	public static AudioConversionResult ConvertToRaud(
		string inputAudioPath,
		string? outputRaudPath = null,
		string? assetType = null,
		string? author = null)
	{
		string fullInput = Path.GetFullPath(inputAudioPath);
		var result = new AudioConversionResult { InputPath = fullInput };

		if (!File.Exists(fullInput))
		{
			result.Success = false;
			result.ErrorMessage = $"Input audio file not found: {inputAudioPath}";
			return result;
		}

		string targetRaud = !string.IsNullOrEmpty(outputRaudPath)
			? Path.GetFullPath(outputRaudPath)
			: Path.ChangeExtension(fullInput, ".raud");
		result.OutputPath = targetRaud;

		string ext = Path.GetExtension(fullInput).ToLowerInvariant();

		try
		{
			byte[] oggBytes;
			string? existingMeta = null;

			if (ext == ".raud")
			{
				byte[] raudBytes = File.ReadAllBytes(fullInput);
				var (parsedMeta, tracks, _) = RaudFile.Parse(raudBytes);
				existingMeta = parsedMeta;
				if (tracks.Count == 0 || tracks[0].Length == 0)
				{
					result.Success = false;
					result.ErrorMessage = "Input RAUD file contains no audio tracks.";
					return result;
				}
				oggBytes = tracks[0];
			}
			else if (ext == ".ogg")
			{
				oggBytes = File.ReadAllBytes(fullInput);
			}
			else
			{
				string tempOgg = Path.Combine(Path.GetTempPath(), $"realm_aud_{Guid.NewGuid():N}.ogg");
				try
				{
					var oggRes = ConvertToOgg(fullInput, tempOgg);
					if (!oggRes.Success || !File.Exists(tempOgg))
					{
						result.Success = false;
						result.ErrorMessage = oggRes.ErrorMessage;
						return result;
					}
					oggBytes = File.ReadAllBytes(tempOgg);
				}
				finally
				{
					if (File.Exists(tempOgg))
					{
						try { File.Delete(tempOgg); } catch { }
					}
				}
			}

			JsonObject metaObj;
			if (!string.IsNullOrWhiteSpace(existingMeta))
			{
				try
				{
					metaObj = JsonNode.Parse(existingMeta)?.AsObject() ?? new JsonObject();
				}
				catch
				{
					metaObj = new JsonObject();
				}
			}
			else
			{
				metaObj = new JsonObject();
			}

			string? effectiveAssetType = assetType;
			if (string.IsNullOrEmpty(effectiveAssetType))
			{
				string? existingType = metaObj["asset_type"]?.ToString() ?? metaObj["default_asset_type"]?.ToString() ?? metaObj["type"]?.ToString();
				if (!string.IsNullOrEmpty(existingType) && RealmMetadataHelper.IsValidAssetTypeForExtension(".raud", existingType, out string canonical, out _))
				{
					effectiveAssetType = canonical;
				}
				else
				{
					string lower = fullInput.ToLowerInvariant().Replace('\\', '/');
					effectiveAssetType = lower.Contains("/music/") || lower.Contains("/theme/") || lower.Contains("/bgm/")
						? "Music"
						: "SoundEffect";
				}
			}

			if (!metaObj.ContainsKey("created_utc") || metaObj["created_utc"] == null)
			{
				metaObj["created_utc"] = DateTime.UtcNow.ToString("O");
			}

			metaObj["format"] = "raud";
			metaObj["asset_type"] = effectiveAssetType;
			metaObj["preferred_file_name"] = Path.GetFileName(fullInput);

			if (!string.IsNullOrEmpty(author) && (!metaObj.ContainsKey("author") || string.IsNullOrWhiteSpace(metaObj["author"]?.ToString())))
			{
				metaObj["author"] = author;
			}

			string blake3Hash = RealmMetadataHelper.ComputeBlake3(oggBytes, ".ogg");
			metaObj["blake3"] = blake3Hash;

			byte[] finalRaudBytes = RaudFile.Build(metaObj.ToJsonString(), [oggBytes]);

			string? targetDir = Path.GetDirectoryName(targetRaud);
			if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
			{
				Directory.CreateDirectory(targetDir);
			}

			File.WriteAllBytes(targetRaud, finalRaudBytes);

			result.Success = true;
			result.OutputBytes = finalRaudBytes;
			result.AssetType = effectiveAssetType;
			result.Author = metaObj["author"]?.ToString();
			result.PreferredFileName = metaObj["preferred_file_name"]?.ToString();
			return result;
		}
		catch (Exception ex)
		{
			result.Success = false;
			result.ErrorMessage = ex.Message;
			return result;
		}
	}

	public static byte[]? ExtractOggFromRaud(ReadOnlySpan<byte> raudBytes, int trackIndex = 0)
	{
		return RaudFile.GetTrack(raudBytes, trackIndex);
	}

	public static AudioConversionResult ExtractOggFromRaud(string inputRaudPath, string? outputOggPath = null, int trackIndex = 0)
	{
		string fullInput = Path.GetFullPath(inputRaudPath);
		var result = new AudioConversionResult { InputPath = fullInput };

		if (!File.Exists(fullInput))
		{
			result.Success = false;
			result.ErrorMessage = $"Input RAUD file not found: {inputRaudPath}";
			return result;
		}

		string targetOgg = !string.IsNullOrEmpty(outputOggPath)
			? Path.GetFullPath(outputOggPath)
			: Path.ChangeExtension(fullInput, ".ogg");
		result.OutputPath = targetOgg;

		try
		{
			byte[] raudBytes = File.ReadAllBytes(fullInput);
			byte[]? oggBytes = ExtractOggFromRaud(raudBytes, trackIndex);
			if (oggBytes == null || oggBytes.Length == 0)
			{
				result.Success = false;
				result.ErrorMessage = $"Failed to extract track {trackIndex} from RAUD file.";
				return result;
			}

			string? targetDir = Path.GetDirectoryName(targetOgg);
			if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
			{
				Directory.CreateDirectory(targetDir);
			}

			File.WriteAllBytes(targetOgg, oggBytes);

			result.Success = true;
			result.OutputBytes = oggBytes;
			return result;
		}
		catch (Exception ex)
		{
			result.Success = false;
			result.ErrorMessage = ex.Message;
			return result;
		}
	}

	public static AudioConversionResult ConvertAudioFile(
		string inputPath,
		string? outputPath = null,
		string? assetType = null,
		string? author = null)
	{
		string fullInput = Path.GetFullPath(inputPath);
		string ext = Path.GetExtension(fullInput).ToLowerInvariant();

		if (ext == ".raud")
		{
			string targetOgg = string.IsNullOrEmpty(outputPath)
				? Path.ChangeExtension(fullInput, ".ogg")
				: Path.GetFullPath(outputPath);
			return ExtractOggFromRaud(fullInput, targetOgg);
		}

		string targetRaud = string.IsNullOrEmpty(outputPath)
			? Path.ChangeExtension(fullInput, ".raud")
			: Path.GetFullPath(outputPath);

		if (targetRaud.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
		{
			return ConvertToOgg(fullInput, targetRaud);
		}

		return ConvertToRaud(fullInput, targetRaud, assetType, author);
	}

	public static AudioConversionResult ConvertToOgg(string inputAudioPath, string? outputOggPath = null)
	{
		string fullInput = Path.GetFullPath(inputAudioPath);
		var result = new AudioConversionResult { InputPath = fullInput };

		if (!File.Exists(fullInput))
		{
			result.Success = false;
			result.ErrorMessage = $"Input audio file not found: {inputAudioPath}";
			return result;
		}

		string targetOgg = !string.IsNullOrEmpty(outputOggPath)
			? Path.GetFullPath(outputOggPath)
			: Path.ChangeExtension(fullInput, ".ogg");
		result.OutputPath = targetOgg;

		string? targetDir = Path.GetDirectoryName(targetOgg);
		if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
		{
			Directory.CreateDirectory(targetDir);
		}

		string ext = Path.GetExtension(fullInput).ToLowerInvariant();

		if (ext == ".ogg")
		{
			if (!string.Equals(fullInput, targetOgg, StringComparison.OrdinalIgnoreCase))
			{
				File.Copy(fullInput, targetOgg, true);
			}

			result.Success = true;
			return result;
		}

		string? ffmpeg = NativeToolRunner.FindFfmpegPath();
		if (string.IsNullOrEmpty(ffmpeg))
		{
			result.Success = false;
			result.ErrorMessage = "ffmpeg binary not found for audio conversion.";
			return result;
		}

		var run = NativeToolRunner.RunTool(ffmpeg, $"-y -i \"{fullInput}\" -c:a libvorbis -q:a 5 \"{targetOgg}\"");
		if (run.ExitCode == 0 && File.Exists(targetOgg) && new FileInfo(targetOgg).Length > 0)
		{
			result.Success = true;
			return result;
		}

		result.Success = false;
		result.ErrorMessage = $"ffmpeg audio conversion failed (exit code {run.ExitCode}): {run.Stderr}\n{run.Stdout}";
		return result;
	}

	public static int ConvertAudioDirectory(string inputDir, string? outputDir, bool recursive, string? assetType = null, string? author = null)
	{
		string fullInputDir = Path.GetFullPath(inputDir);
		string? fullOutputDir = !string.IsNullOrEmpty(outputDir) ? Path.GetFullPath(outputDir) : null;

		var searchOpt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
		string[] files = Directory.GetFiles(fullInputDir, "*.*", searchOpt);
		int successCount = 0;
		int failCount = 0;

		foreach (var file in files)
		{
			if (!IsAudioFile(file)) continue;

			string targetExt = file.EndsWith(".raud", StringComparison.OrdinalIgnoreCase) ? ".ogg" : ".raud";
			string target;
			if (string.IsNullOrEmpty(fullOutputDir))
			{
				target = Path.ChangeExtension(file, targetExt);
			}
			else
			{
				string rel = Path.GetRelativePath(fullInputDir, file);
				target = Path.Combine(fullOutputDir, Path.ChangeExtension(rel, targetExt));
			}

			var res = ConvertAudioFile(file, target, assetType, author);
			if (res.Success)
			{
				Console.WriteLine($"Converted: {file} -> {target}");
				successCount++;
			}
			else
			{
				Console.Error.WriteLine($"Failed to convert {file}: {res.ErrorMessage}");
				failCount++;
			}
		}

		Console.WriteLine($"Finished audio conversion. {successCount} succeeded, {failCount} failed.");
		return failCount > 0 ? 1 : 0;
	}
}

