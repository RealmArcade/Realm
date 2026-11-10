using System.Numerics;
using System.Text.Json.Nodes;
using Assimp;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>writing the mask into the orm texture.</summary>
public static partial class GlbPlayerColorProcessor
	{
	private static void ApplyMaskToOrm(SKBitmap ormImg, float[] globalMask, int texW, int texH)
	{
		int ormW = ormImg.Width;
		int ormH = ormImg.Height;

		for (int y = 0; y < ormH; y++)
		{
			for (int x = 0; x < ormW; x++)
			{
				int srcX = Math.Clamp((int)((x / (float)ormW) * texW), 0, texW - 1);
				int srcY = Math.Clamp((int)((y / (float)ormH) * texH), 0, texH - 1);
				float maskValue = globalMask[srcY * texW + srcX];
				byte maskByte = (byte)Math.Clamp((int)(maskValue * 255f + 0.5f), 0, 255);
				var pixel = ormImg.GetPixel(x, y);
				ormImg.SetPixel(x, y, new SKColor(maskByte, pixel.Green, pixel.Blue, pixel.Alpha));
			}
		}
	}

	private static SKBitmap CreateDefaultOrm(int width, int height)
	{
		var img = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
		img.Erase(new SKColor(0, 255, 0, 255));
		return img;
	}
}
