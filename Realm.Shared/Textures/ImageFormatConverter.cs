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

		string ext = Path.GetExtension(fullInput).ToLowerInvariant();
		string targetWebp = !string.IsNullOrEmpty(outputWebpPath)
			? Path.GetFullPath(outputWebpPath)
			: Path.Combine(Path.GetTempPath(), $"realm_conv_{Guid.NewGuid():N}_{Path.GetFileNameWithoutExtension(fullInput)}.webp");

		string? targetDir = Path.GetDirectoryName(targetWebp);
		if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
		{
			Directory.CreateDirectory(targetDir);
		}

		if (ext == ".webp")
		{
			if (string.Equals(fullInput, targetWebp, StringComparison.OrdinalIgnoreCase))
			{
				return fullInput;
			}
			File.Copy(fullInput, targetWebp, true);
			return targetWebp;
		}

		if (ext == ".rtex")
		{
			var extractResult = TextureConverter.ExtractWebpFromRtex(fullInput, targetWebp, layer: 0);
			if (extractResult.Success && File.Exists(targetWebp))
			{
				return targetWebp;
			}
			throw new InvalidOperationException($"Failed to extract WebP from RTEX '{inputImagePath}': {extractResult.ErrorMessage}");
		}

		using var image = SKBitmap.Decode(fullInput);
		if (image == null)
		{
			throw new InvalidOperationException($"Failed to load image file '{inputImagePath}'.");
		}
		byte[] webpBytes = TextureConverter.EncodeWebp(image, lossless: false, quality: 90);
		File.WriteAllBytes(targetWebp, webpBytes);
		return targetWebp;
	}
}
