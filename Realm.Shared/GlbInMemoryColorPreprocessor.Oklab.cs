using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>the linear rgb to oklab conversions the despill works in.</summary>
public static partial class GlbInMemoryColorPreprocessor
	{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Vector3 ConvertLinearRgbToOklab(Vector3 linearRgb)
	{
		float l = linearRgb.X * 0.4122214708f + linearRgb.Y * 0.5363325363f + linearRgb.Z * 0.0514459929f;
		float m = linearRgb.X * 0.2119034982f + linearRgb.Y * 0.6806995451f + linearRgb.Z * 0.1073969566f;
		float s = linearRgb.X * 0.0883024619f + linearRgb.Y * 0.2817188376f + linearRgb.Z * 0.6299787005f;

		float lRoot = FastCbrt(MathF.Max(0.0f, l));
		float mRoot = FastCbrt(MathF.Max(0.0f, m));
		float sRoot = FastCbrt(MathF.Max(0.0f, s));

		return new Vector3(
			lRoot * 0.2104542553f + mRoot * 0.7936177850f + sRoot * -0.0040720468f,
			lRoot * 1.9779984951f + mRoot * -2.4285922050f + sRoot * 0.4505937099f,
			lRoot * 0.0259040371f + mRoot * 0.7827717662f + sRoot * -0.8086757660f);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Vector3 ConvertOklabToLinearRgb(Vector3 oklab)
	{
		float lRoot = oklab.X + oklab.Y * 0.3963377774f + oklab.Z * 0.2158037573f;
		float mRoot = oklab.X + oklab.Y * -0.1055613458f + oklab.Z * -0.0638541728f;
		float sRoot = oklab.X + oklab.Y * -0.0894841775f + oklab.Z * -1.2914855480f;

		float l = lRoot * lRoot * lRoot;
		float m = mRoot * mRoot * mRoot;
		float s = sRoot * sRoot * sRoot;

		return new Vector3(
			l * 4.0767416621f + m * -3.3077115913f + s * 0.2309699292f,
			l * -1.2684380046f + m * 2.6097574011f + s * -0.3413193965f,
			l * -0.0041960863f + m * -0.7034186147f + s * 1.7076147010f);
	}
}
