using SkiaSharp;

namespace Realm.Shared.Textures;

public static class ImageFormatConverter
{
	public static readonly string[] SupportedExtensions =
	[
		".rtex", ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp",
		".dds", ".tiff", ".tif", ".svg", ".tga", ".pbm",
		".ktx2", ".exr", ".hdr"
	];

	public static bool IsImageFile(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath)) return false;
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		return Array.Exists(SupportedExtensions, e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));
	}

	public static string ConvertToWebp(string inputImagePath, string? outputWebpPath = null)
	{
		string fullInput = Path.GetFullPath(inputImagePath);
		if (!File.Exists(fullInput))
		{
			throw new FileNotFoundException($"Source image file not found: {inputImagePath}", inputImagePath);
		}

		string targetWebp = GetTargetWebpPath(fullInput, outputWebpPath);
		EnsureTargetDirectoryExists(targetWebp);

		string ext = Path.GetExtension(fullInput).ToLowerInvariant();
		return ext switch
		{
			".webp" => HandleWebp(fullInput, targetWebp),
			".rtex" => HandleRtex(fullInput, targetWebp),
			_ => EncodeToWebp(fullInput, targetWebp)
		};
	}

	private static string GetTargetWebpPath(string fullInput, string? outputWebpPath)
	{
		return !string.IsNullOrEmpty(outputWebpPath)
			? Path.GetFullPath(outputWebpPath)
			: Path.Combine(Path.GetTempPath(), $"realm_conv_{Guid.NewGuid():N}_{Path.GetFileNameWithoutExtension(fullInput)}.webp");
	}

	private static void EnsureTargetDirectoryExists(string targetWebp)
	{
		string? targetDir = Path.GetDirectoryName(targetWebp);
		if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
		{
			Directory.CreateDirectory(targetDir);
		}
	}

	private static string HandleWebp(string fullInput, string targetWebp)
	{
		if (string.Equals(fullInput, targetWebp, StringComparison.OrdinalIgnoreCase))
		{
			return fullInput;
		}
		File.Copy(fullInput, targetWebp, true);
		return targetWebp;
	}

	private static string HandleRtex(string fullInput, string targetWebp)
	{
		var extractResult = TextureConverter.ExtractWebpFromRtex(fullInput, targetWebp, layer: 0);
		if (extractResult.Success && File.Exists(targetWebp))
		{
			return targetWebp;
		}
		throw new InvalidOperationException($"Failed to extract WebP from RTEX '{fullInput}': {extractResult.ErrorMessage}");
	}

	private static string EncodeToWebp(string fullInput, string targetWebp)
	{
		using var image = SKBitmap.Decode(fullInput);
		if (image == null)
		{
			throw new InvalidOperationException($"Failed to load image file '{fullInput}'.");
		}
		byte[] webpBytes = TextureConverter.EncodeWebp(image, lossless: false, quality: 90);
		File.WriteAllBytes(targetWebp, webpBytes);
		return targetWebp;
	}
}
