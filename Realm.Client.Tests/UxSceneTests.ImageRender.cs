using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Realm.Client.UI;
using Realm.Client.Utils;
using Environment = System.Environment;
using Realm.Client.Services;
namespace Realm.Client.Tests;

/// <summary>cropping, scaling and rendering a texture into an image.</summary>
public partial class UxSceneTests
	{
	private static void RecordTextureUsage(Texture2D texture, Vector2 drawnSize, Dictionary<string, ImageRenderUsage> usages, bool hasTextureMargins, HashSet<string> nineSliceFileNames)
	{
		if (texture == null)
		{
			return;
		}

		string resourcePath = ResolveTextureResourcePath(texture);
		if (!IsUxImagePath(resourcePath))
		{
			return;
		}

		string fileName = Path.GetFileName(resourcePath);

		if (hasTextureMargins)
		{
			nineSliceFileNames.Add(fileName);
		}

		int width = Mathf.RoundToInt(drawnSize.X);
		int height = Mathf.RoundToInt(drawnSize.Y);
		if (width < 1 || height < 1)
		{
			return;
		}

		float area = (float)width * height;

		if (usages.TryGetValue(fileName, out ImageRenderUsage existingUsage) && existingUsage.Area >= area)
		{
			return;
		}

		usages[fileName] = new ImageRenderUsage
		{
			ResourcePath = resourcePath,
			Width = width,
			Height = height,
			Area = area
		};
	}

	private static string ResolveTextureResourcePath(Texture2D texture)
	{
		if (TrimmedTextureLoader.TryGetTextureSourcePath(texture, out string recordedPath))
		{
			return recordedPath;
		}

		return texture.ResourcePath;
	}

	private static Rect2I ComputeSourceCropRect(TrimmedTextureLoadRecord record)
	{
		int sourceWidth = Mathf.Max(1, record.SourceWidth);
		int sourceHeight = Mathf.Max(1, record.SourceHeight);
		int scaledWidth = record.ScaledWidth > 0 ? record.ScaledWidth : sourceWidth;
		int scaledHeight = record.ScaledHeight > 0 ? record.ScaledHeight : sourceHeight;

		float scaleX = sourceWidth / (float)scaledWidth;
		float scaleY = sourceHeight / (float)scaledHeight;

		int usedX = Mathf.Clamp(Mathf.RoundToInt(record.UsedRectX * scaleX), 0, sourceWidth - 1);
		int usedY = Mathf.Clamp(Mathf.RoundToInt(record.UsedRectY * scaleY), 0, sourceHeight - 1);
		int usedWidth = Mathf.Clamp(Mathf.RoundToInt(record.UsedWidth * scaleX), 1, sourceWidth - usedX);
		int usedHeight = Mathf.Clamp(Mathf.RoundToInt(record.UsedHeight * scaleY), 1, sourceHeight - usedY);

		return new Rect2I(usedX, usedY, usedWidth, usedHeight);
	}

	private static Vector2I ComputeFitSize(int sourceWidth, int sourceHeight, Vector2I targetSize)
	{
		float scale = Mathf.Min(1.0f, Mathf.Min(targetSize.X / (float)sourceWidth, targetSize.Y / (float)sourceHeight));
		return new Vector2I(Mathf.Max(1, Mathf.RoundToInt(sourceWidth * scale)), Mathf.Max(1, Mathf.RoundToInt(sourceHeight * scale)));
	}

	private static Image TrimToRect(Image sourceImage, Rect2I cropRect)
	{
		if (cropRect.Position.X == 0 && cropRect.Position.Y == 0 && cropRect.Size.X == sourceImage.GetWidth() && cropRect.Size.Y == sourceImage.GetHeight())
		{
			return sourceImage;
		}

		return sourceImage.GetRegion(cropRect);
	}

	private static Image ScaleToSize(Image sourceImage, Vector2I outputSize)
	{
		if (sourceImage.GetWidth() == outputSize.X && sourceImage.GetHeight() == outputSize.Y)
		{
			return sourceImage;
		}

		sourceImage.Resize(outputSize.X, outputSize.Y, Image.Interpolation.Lanczos);
		return sourceImage;
	}

	private static Error RenderUxImage(string globalPath, Rect2I sourceCropRect, Vector2I outputSize, string outputPath, string cwebpPath)
	{
		Image sourceImage = Image.LoadFromFile(globalPath);
		if (sourceImage == null || sourceImage.IsEmpty())
		{
			return Error.CantOpen;
		}

		Image? trimmedImage = null;
		Image? outputImage = null;

		try
		{
			trimmedImage = TrimToRect(sourceImage, sourceCropRect);
			outputImage = ScaleToSize(trimmedImage, outputSize);
			return SaveAsWebp(outputImage, outputPath, cwebpPath);
		}
		finally
		{
			if (outputImage != null && !ReferenceEquals(outputImage, trimmedImage) && !ReferenceEquals(outputImage, sourceImage))
			{
				outputImage.Dispose();
			}

			if (trimmedImage != null && !ReferenceEquals(trimmedImage, sourceImage))
			{
				trimmedImage.Dispose();
			}

			sourceImage.Dispose();
		}
	}
}
