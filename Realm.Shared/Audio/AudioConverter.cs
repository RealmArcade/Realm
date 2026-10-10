using System.Text.Json.Nodes;
using Realm.Shared.Metadata;

namespace Realm.Shared.Audio;

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
			byte[]? oggBytes = GetOggBytes(fullInput, ext, out string? existingMeta, out string errorMessage);
			if (oggBytes == null)
			{
				result.Success = false;
				result.ErrorMessage = errorMessage;
				return result;
			}

			JsonObject metaObj = ParseMetadataObj(existingMeta);
			string effectiveAssetType = GetEffectiveAssetType(assetType, fullInput, metaObj);

			PopulateRaudMetadata(metaObj, effectiveAssetType, fullInput, author, oggBytes);

			byte[] finalRaudBytes = RaudFile.Build(metaObj.ToJsonString(), [oggBytes]);

			EnsureDirectoryExists(targetRaud);

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

	private static void PopulateRaudMetadata(JsonObject metaObj, string effectiveAssetType, string fullInput, string? author, byte[] oggBytes)
	{
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
	}

	private static byte[]? GetOggBytes(string fullInput, string ext, out string? existingMeta, out string errorMessage)
	{
		existingMeta = null;
		errorMessage = string.Empty;

		if (ext == ".raud")
		{
			byte[] raudBytes = File.ReadAllBytes(fullInput);
			var (parsedMeta, tracks, _) = RaudFile.Parse(raudBytes);
			existingMeta = parsedMeta;
			if (tracks.Count == 0 || tracks[0].Length == 0)
			{
				errorMessage = "Input RAUD file contains no audio tracks.";
				return null;
			}
			return tracks[0];
		}
		
		if (ext == ".ogg")
		{
			return File.ReadAllBytes(fullInput);
		}

		string tempOgg = Path.Combine(Path.GetTempPath(), $"realm_aud_{Guid.NewGuid():N}.ogg");
		try
		{
			var oggRes = ConvertToOgg(fullInput, tempOgg);
			if (!oggRes.Success || !File.Exists(tempOgg))
			{
				errorMessage = oggRes.ErrorMessage;
				return null;
			}
			return File.ReadAllBytes(tempOgg);
		}
		finally
		{
			SafeDeleteTempFile(tempOgg);
		}
	}

	private static void SafeDeleteTempFile(string filePath)
	{
		if (!File.Exists(filePath)) return;
		try { File.Delete(filePath); } catch { }
	}

	private static JsonObject ParseMetadataObj(string? existingMeta)
	{
		if (string.IsNullOrWhiteSpace(existingMeta)) return new JsonObject();
		try { return JsonNode.Parse(existingMeta)?.AsObject() ?? new JsonObject(); }
		catch { return new JsonObject(); }
	}

	private static string GetEffectiveAssetType(string? assetType, string fullInput, JsonObject metaObj)
	{
		if (!string.IsNullOrEmpty(assetType)) return assetType;

		string? existingType = ExtractExistingAssetType(metaObj);
		if (IsValidExistingAssetType(existingType, out string canonical))
			return canonical;

		return GuessAssetTypeFromPath(fullInput);
	}

	private static string? ExtractExistingAssetType(JsonObject metaObj)
	{
		return metaObj["asset_type"]?.ToString()
			?? metaObj["default_asset_type"]?.ToString()
			?? metaObj["type"]?.ToString();
	}

	private static bool IsValidExistingAssetType(string? existingType, out string canonical)
	{
		canonical = string.Empty;
		return !string.IsNullOrEmpty(existingType)
			&& RealmMetadataHelper.IsValidAssetTypeForExtension(".raud", existingType, out canonical, out _);
	}

	private static string GuessAssetTypeFromPath(string fullInput)
	{
		string lower = fullInput.ToLowerInvariant().Replace('\\', '/');
		bool isMusic = lower.Contains("/music/") || lower.Contains("/theme/") || lower.Contains("/bgm/");
		return isMusic ? "Music" : "SoundEffect";
	}

	private static void EnsureDirectoryExists(string filePath)
	{
		string? targetDir = Path.GetDirectoryName(filePath);
		if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
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

			EnsureDirectoryExists(targetOgg);

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

		EnsureDirectoryExists(targetOgg);

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

