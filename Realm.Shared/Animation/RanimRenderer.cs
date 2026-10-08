using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Realm.Shared.BlenderSetup;
using SkiaSharp;
using Realm.Shared.Textures;

namespace Realm.Shared.Animation;

public enum RanimOutputFormat
{
	Webp,
	Spritesheet,
	Gif
}

public class RanimRenderOptions
{
	public int Width { get; set; } = 128;
	public int Height { get; set; } = 128;
	public float Fps { get; set; } = 12.0f;
	public int? MaxFrameCount { get; set; }
	public RanimOutputFormat Format { get; set; } = RanimOutputFormat.Webp;
	public float Scale { get; set; } = 1.0f;
	public bool DrawBorder { get; set; } = true;
	public bool DrawShadow { get; set; } = true;
	public string? ModelPath { get; set; }
	public byte[]? ModelBytes { get; set; }
	public int Quality { get; set; } = 95;
	public bool Lossless { get; set; }
}

public class RanimRenderFrame
{
	public int Width { get; set; }
	public int Height { get; set; }
	public float Time { get; set; }
	public byte[] RgbaBytes { get; set; } = Array.Empty<byte>();
}

public class RanimRenderResult
{
	public List<RanimRenderFrame> Frames { get; set; } = new();
	public float Duration { get; set; }
	public float EffectiveFps { get; set; }
	public int TotalSourceFrames { get; set; }
	public int ModulusStep { get; set; } = 1;
}

public class RanimExportResult
{
	public bool Success { get; set; }
	public string InputPath { get; set; } = string.Empty;
	public string OutputPath { get; set; } = string.Empty;
	public int FrameCount { get; set; }
	public string ErrorMessage { get; set; } = string.Empty;
}

public static class RanimRenderer
{
	private struct JointHierarchyDefinition
	{
		public HumanoidBone Bone;
		public HumanoidBone Parent;
		public Vector3 RestOffset;
	}

	private static readonly JointHierarchyDefinition[] Hierarchy = new JointHierarchyDefinition[]
	{
		new JointHierarchyDefinition { Bone = HumanoidBone.Hips, Parent = HumanoidBone.Hips, RestOffset = new Vector3(0, 0.94f, 0) },
		new JointHierarchyDefinition { Bone = HumanoidBone.Spine, Parent = HumanoidBone.Hips, RestOffset = new Vector3(0, 0.14f, 0) },
		new JointHierarchyDefinition { Bone = HumanoidBone.Chest, Parent = HumanoidBone.Spine, RestOffset = new Vector3(0, 0.14f, 0) },
		new JointHierarchyDefinition { Bone = HumanoidBone.UpperChest, Parent = HumanoidBone.Chest, RestOffset = new Vector3(0, 0.12f, 0) },
		new JointHierarchyDefinition { Bone = HumanoidBone.Neck, Parent = HumanoidBone.UpperChest, RestOffset = new Vector3(0, 0.09f, 0) },
		new JointHierarchyDefinition { Bone = HumanoidBone.Head, Parent = HumanoidBone.Neck, RestOffset = new Vector3(0, 0.15f, 0) },

		new JointHierarchyDefinition { Bone = HumanoidBone.LeftShoulder, Parent = HumanoidBone.UpperChest, RestOffset = new Vector3(-0.06f, 0.04f, 0) },
		new JointHierarchyDefinition { Bone = HumanoidBone.LeftUpperArm, Parent = HumanoidBone.LeftShoulder, RestOffset = new Vector3(-0.13f, -0.02f, 0) },
		new JointHierarchyDefinition { Bone = HumanoidBone.LeftLowerArm, Parent = HumanoidBone.LeftUpperArm, RestOffset = new Vector3(-0.24f, 0, 0) },
		new JointHierarchyDefinition { Bone = HumanoidBone.LeftHand, Parent = HumanoidBone.LeftLowerArm, RestOffset = new Vector3(-0.20f, 0, 0) },

		new JointHierarchyDefinition { Bone = HumanoidBone.RightShoulder, Parent = HumanoidBone.UpperChest, RestOffset = new Vector3(0.06f, 0.04f, 0) },
		new JointHierarchyDefinition { Bone = HumanoidBone.RightUpperArm, Parent = HumanoidBone.RightShoulder, RestOffset = new Vector3(0.13f, -0.02f, 0) },
		new JointHierarchyDefinition { Bone = HumanoidBone.RightLowerArm, Parent = HumanoidBone.RightUpperArm, RestOffset = new Vector3(0.24f, 0, 0) },
		new JointHierarchyDefinition { Bone = HumanoidBone.RightHand, Parent = HumanoidBone.RightLowerArm, RestOffset = new Vector3(0.20f, 0, 0) },

		new JointHierarchyDefinition { Bone = HumanoidBone.LeftUpperLeg, Parent = HumanoidBone.Hips, RestOffset = new Vector3(-0.10f, -0.08f, 0) },
		new JointHierarchyDefinition { Bone = HumanoidBone.LeftLowerLeg, Parent = HumanoidBone.LeftUpperLeg, RestOffset = new Vector3(0, -0.40f, 0) },
		new JointHierarchyDefinition { Bone = HumanoidBone.LeftFoot, Parent = HumanoidBone.LeftLowerLeg, RestOffset = new Vector3(0, -0.38f, 0) },
		new JointHierarchyDefinition { Bone = HumanoidBone.LeftToes, Parent = HumanoidBone.LeftFoot, RestOffset = new Vector3(0, -0.04f, 0.12f) },

		new JointHierarchyDefinition { Bone = HumanoidBone.RightUpperLeg, Parent = HumanoidBone.Hips, RestOffset = new Vector3(0.10f, -0.08f, 0) },
		new JointHierarchyDefinition { Bone = HumanoidBone.RightLowerLeg, Parent = HumanoidBone.RightUpperLeg, RestOffset = new Vector3(0, -0.40f, 0) },
		new JointHierarchyDefinition { Bone = HumanoidBone.RightFoot, Parent = HumanoidBone.RightLowerLeg, RestOffset = new Vector3(0, -0.38f, 0) },
		new JointHierarchyDefinition { Bone = HumanoidBone.RightToes, Parent = HumanoidBone.RightFoot, RestOffset = new Vector3(0, -0.04f, 0.12f) }
	};

	public static RanimRenderResult RenderFrames(RealmAnimationData animData, RanimRenderOptions? renderOptions = null)
	{
		var options = renderOptions ?? new RanimRenderOptions();
		if (animData == null)
		{
			return new RanimRenderResult();
		}

		if ((options.ModelBytes != null && options.ModelBytes.Length > 0) || !string.IsNullOrEmpty(options.ModelPath))
		{
			return BlenderRanimRenderer.RenderFrames(animData, options);
		}

		var trackMap = BuildTrackMap(animData);
		float duration = animData.Duration > 0f ? animData.Duration : 1.0f;
		float sampleFps = options.Fps > 0f ? options.Fps : 12.0f;

		int totalSourceFrames = Math.Max(1, (int)MathF.Ceiling(duration * sampleFps));
		int modulusStep = CalculateModulusStep(totalSourceFrames, options.MaxFrameCount);
		var selectedTimes = GetSelectedTimes(totalSourceFrames, modulusStep, duration);

		var result = new RanimRenderResult
		{
			Duration = duration,
			TotalSourceFrames = totalSourceFrames,
			ModulusStep = modulusStep,
			EffectiveFps = selectedTimes.Count / duration
		};

		foreach (float time in selectedTimes)
		{
			using var image = RenderSkeletonFrame(trackMap, time, options);
			byte[] pixelBytes = ExtractFrameBytes(image, options.Width, options.Height);

			result.Frames.Add(new RanimRenderFrame
			{
				Width = options.Width,
				Height = options.Height,
				Time = time,
				RgbaBytes = pixelBytes
			});
		}

		return result;
	}

	private static int CalculateModulusStep(int totalSourceFrames, int? maxFrameCount)
	{
		if (maxFrameCount.HasValue && maxFrameCount.Value > 0 && totalSourceFrames > maxFrameCount.Value)
		{
			return Math.Max(1, (int)MathF.Ceiling((float)totalSourceFrames / maxFrameCount.Value));
		}
		return 1;
	}

	private static List<float> GetSelectedTimes(int totalSourceFrames, int modulusStep, float duration)
	{
		var selectedTimes = new List<float>();
		for (int frameIndex = 0; frameIndex < totalSourceFrames; frameIndex += modulusStep)
		{
			float time = (frameIndex / (float)totalSourceFrames) * duration;
			selectedTimes.Add(time);
		}

		if (selectedTimes.Count == 0)
		{
			selectedTimes.Add(0f);
		}

		return selectedTimes;
	}

	private static byte[] ExtractFrameBytes(SKBitmap image, int width, int height)
	{
		byte[] pixelBytes = new byte[width * height * 4];
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				SKColor c = image.GetPixel(x, y);
				int idx = (y * width + x) * 4;
				pixelBytes[idx] = c.Red;
				pixelBytes[idx + 1] = c.Green;
				pixelBytes[idx + 2] = c.Blue;
				pixelBytes[idx + 3] = c.Alpha;
			}
		}
		return pixelBytes;
	}

	public static RanimExportResult ExportFile(string inputPath, string? outputPath = null, RanimRenderOptions? options = null)
	{
		var renderOptions = options ?? new RanimRenderOptions();
		var exportResult = new RanimExportResult
		{
			InputPath = inputPath
		};

		if (!File.Exists(inputPath))
		{
			exportResult.Success = false;
			exportResult.ErrorMessage = $"Input file not found: {inputPath}";
			return exportResult;
		}

		try
		{
			var animData = RealmAnimationSerializer.LoadFromFile(inputPath);
			if (animData == null)
			{
				exportResult.Success = false;
				exportResult.ErrorMessage = $"Failed to parse .ranim file: {inputPath}";
				return exportResult;
			}

			string finalOutputPath = outputPath ?? string.Empty;
			if (string.IsNullOrEmpty(finalOutputPath))
			{
				string extension = renderOptions.Format switch
				{
					RanimOutputFormat.Spritesheet => ".png",
					_ => ".webp"
				};
				finalOutputPath = Path.ChangeExtension(inputPath, extension);
			}

			return ExportToFile(animData, finalOutputPath, renderOptions, inputPath);
		}
		catch (Exception ex)
		{
			exportResult.Success = false;
			exportResult.ErrorMessage = ex.Message;
			return exportResult;
		}
	}

	private static bool ShouldUseBlenderRenderer(RanimRenderOptions renderOptions)
	{
		return (renderOptions.ModelBytes != null && renderOptions.ModelBytes.Length > 0) || !string.IsNullOrEmpty(renderOptions.ModelPath);
	}

	private static void DisposeImages(List<SKBitmap> images)
	{
		foreach (var image in images)
		{
			image.Dispose();
		}
	}

	public static RanimExportResult ExportToFile(RealmAnimationData animData, string outputPath, RanimRenderOptions? options = null, string inputPath = "")
	{
		var renderOptions = options ?? new RanimRenderOptions();
		var exportResult = new RanimExportResult { InputPath = inputPath, OutputPath = outputPath };

		if (animData == null)
		{
			exportResult.Success = false;
			exportResult.ErrorMessage = "Animation data is null.";
			return exportResult;
		}

		if (ShouldUseBlenderRenderer(renderOptions))
		{
			return BlenderRanimRenderer.ExportToFile(animData, outputPath, renderOptions, inputPath);
		}

		var trackMap = BuildTrackMap(animData);
		float duration = animData.Duration > 0f ? animData.Duration : 1.0f;
		float sampleFps = renderOptions.Fps > 0f ? renderOptions.Fps : 12.0f;

		int totalSourceFrames = Math.Max(1, (int)MathF.Ceiling(duration * sampleFps));
		int modulusStep = CalculateModulusStep(totalSourceFrames, renderOptions.MaxFrameCount);
		var selectedTimes = GetSelectedTimes(totalSourceFrames, modulusStep, duration);

		exportResult.FrameCount = selectedTimes.Count;

		var frameImages = new List<SKBitmap>();
		try
		{
			foreach (float time in selectedTimes)
			{
				frameImages.Add(RenderSkeletonFrame(trackMap, time, renderOptions));
			}

			SaveRenderedFrames(frameImages, outputPath, duration, renderOptions);
			exportResult.Success = true;
			return exportResult;
		}
		catch (Exception ex)
		{
			exportResult.Success = false;
			exportResult.ErrorMessage = ex.Message;
			return exportResult;
		}
		finally
		{
			DisposeImages(frameImages);
		}
	}

	private static void SaveRenderedFrames(List<SKBitmap> frameImages, string outputPath, float duration, RanimRenderOptions renderOptions)
	{
		string? directory = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}

		if (renderOptions.Format == RanimOutputFormat.Spritesheet || (outputPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && !outputPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)))
		{
			SaveAsSpritesheet(frameImages, outputPath, renderOptions);
		}
		else
		{
			SaveAsAnimatedWebp(frameImages, outputPath, duration, renderOptions);
		}
	}

	public static int ExportDirectory(string inputDirectory, string? outputDirectory = null, RanimRenderOptions? options = null, bool recursive = false)
	{
		var renderOptions = options ?? new RanimRenderOptions();
		var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
		string[] files = Directory.GetFiles(inputDirectory, "*.ranim", searchOption);

		Console.WriteLine($"Found {files.Length} .ranim file(s) in {inputDirectory}");
		int successCount = 0;
		int failureCount = 0;

		foreach (string file in files)
		{
			string target;
			string extension = renderOptions.Format switch
			{
				RanimOutputFormat.Spritesheet => ".png",
				_ => ".webp"
			};

			if (string.IsNullOrEmpty(outputDirectory))
			{
				target = Path.ChangeExtension(file, extension);
			}
			else
			{
				string relativePath = Path.GetRelativePath(inputDirectory, file);
				target = Path.Combine(outputDirectory, Path.ChangeExtension(relativePath, extension));
			}

			var result = ExportFile(file, target, renderOptions);
			if (result.Success)
			{
				Console.WriteLine($"Successfully rendered ({result.FrameCount} frames): {file} -> {target}");
				successCount++;
			}
			else
			{
				Console.Error.WriteLine($"Failed to render {file}: {result.ErrorMessage}");
				failureCount++;
			}
		}

		Console.WriteLine($"Finished rendering. {successCount} succeeded, {failureCount} failed.");
		return failureCount > 0 ? 1 : 0;
	}

	private static void SaveAsSpritesheet(List<SKBitmap> frameImages, string outputPath, RanimRenderOptions options)
	{
		int totalWidth = options.Width * frameImages.Count;
		int totalHeight = options.Height;

		using var spritesheet = new SKBitmap(totalWidth, totalHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul);

		for (int frameIndex = 0; frameIndex < frameImages.Count; frameIndex++)
		{
			int xOffset = frameIndex * options.Width;
			var frameImage = frameImages[frameIndex];

			for (int y = 0; y < options.Height; y++)
			{
				for (int x = 0; x < options.Width; x++)
				{
					spritesheet.SetPixel(xOffset + x, y, frameImage.GetPixel(x, y));
				}
			}
		}

		if (options.Format == RanimOutputFormat.Webp || outputPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
		{
			byte[] webpBytes = TextureConverter.EncodeWebp(spritesheet, options.Lossless, options.Quality);
			File.WriteAllBytes(outputPath, webpBytes);
		}
		else
		{
			using var skImage = SKImage.FromBitmap(spritesheet);
			using var data = skImage.Encode(SKEncodedImageFormat.Png, 100);
			using var stream = File.Create(outputPath);
			data.SaveTo(stream);
		}
	}

	private static void SaveAsAnimatedWebp(List<SKBitmap> frameImages, string outputPath, float duration, RanimRenderOptions options)
	{
		AnimatedWebpEncoder.EncodeAnimatedWebp(frameImages, outputPath, duration, options.Lossless, options.Quality);
	}

	private static Dictionary<HumanoidBone, RealmAnimationBoneTrack> BuildTrackMap(RealmAnimationData animData)
	{
		var trackMap = new Dictionary<HumanoidBone, RealmAnimationBoneTrack>();
		if (animData.Tracks != null)
		{
			foreach (var track in animData.Tracks)
			{
				if (track != null && HumanoidBoneMapper.TryMapToCanonical(track.BoneName, out var bone))
				{
					trackMap[bone] = track;
				}
			}
		}
		return trackMap;
	}

	private static SKBitmap RenderSkeletonFrame(Dictionary<HumanoidBone, RealmAnimationBoneTrack> trackMap, float time, RanimRenderOptions options)
	{
		var worldPositions = CalculateWorldTransforms(trackMap, time);
		var projectedPoints = ProjectSkeletonPoints(worldPositions, options.Width, options.Height, options.Scale);
		
		var img = InitializeBackground(options, projectedPoints);
		DrawSkeleton(img, projectedPoints, options.Width);

		return img;
	}

	private static Vector3 CalculateRootTranslation(Dictionary<HumanoidBone, RealmAnimationBoneTrack> trackMap, float time)
	{
		if (trackMap.TryGetValue(HumanoidBone.Hips, out var hipsTrack) && hipsTrack.PositionKeys != null && hipsTrack.PositionKeys.Length > 0)
		{
			return SamplePosition(hipsTrack.PositionKeys, time) - SamplePosition(hipsTrack.PositionKeys, 0f);
		}
		return Vector3.Zero;
	}

	private static Quaternion CalculateLocalRotation(Dictionary<HumanoidBone, RealmAnimationBoneTrack> trackMap, HumanoidBone bone, float time)
	{
		if (trackMap.TryGetValue(bone, out var track) && track.RotationKeys != null && track.RotationKeys.Length > 0)
		{
			return SampleRotation(track.RotationKeys, time);
		}
		return Quaternion.Identity;
	}

	private static Dictionary<HumanoidBone, Vector3> CalculateWorldTransforms(Dictionary<HumanoidBone, RealmAnimationBoneTrack> trackMap, float time)
	{
		var worldPositions = new Dictionary<HumanoidBone, Vector3>();
		var worldRotations = new Dictionary<HumanoidBone, Quaternion>();

		Vector3 rootTranslation = CalculateRootTranslation(trackMap, time);

		foreach (var joint in Hierarchy)
		{
			Quaternion localRot = CalculateLocalRotation(trackMap, joint.Bone, time);

			if (joint.Bone == HumanoidBone.Hips)
			{
				worldPositions[joint.Bone] = joint.RestOffset + rootTranslation;
				worldRotations[joint.Bone] = localRot;
			}
			else
			{
				Quaternion parentRot = worldRotations.TryGetValue(joint.Parent, out var pRot) ? pRot : Quaternion.Identity;
				Vector3 parentPos = worldPositions.TryGetValue(joint.Parent, out var pPos) ? pPos : Vector3.Zero;

				Vector3 localOffsetRotated = Vector3.Transform(joint.RestOffset, parentRot);
				worldPositions[joint.Bone] = parentPos + localOffsetRotated;
				worldRotations[joint.Bone] = Quaternion.Normalize(parentRot * localRot);
			}
		}

		return worldPositions;
	}

	private static Dictionary<HumanoidBone, (int X, int Y)> ProjectSkeletonPoints(Dictionary<HumanoidBone, Vector3> worldPositions, int width, int height, float scale)
	{
		var projectedPoints = new Dictionary<HumanoidBone, (int X, int Y)>();
		foreach (var pair in worldPositions)
		{
			projectedPoints[pair.Key] = Project3DTo2D(pair.Value, width, height, scale);
		}
		return projectedPoints;
	}

	private static SKBitmap InitializeBackground(RanimRenderOptions options, Dictionary<HumanoidBone, (int X, int Y)> projectedPoints)
	{
		var img = new SKBitmap(options.Width, options.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
		img.Erase(new SKColor(20, 23, 31, 255));

		if (options.DrawBorder)
		{
			DrawCardBorder(img);
		}

		if (options.DrawShadow)
		{
			int shadowCenterX = projectedPoints.TryGetValue(HumanoidBone.Hips, out var hipsPoint) ? hipsPoint.X : (options.Width / 2);
			DrawFloorShadow(img, shadowCenterX);
		}

		return img;
	}

	private static void DrawSkeleton(SKBitmap img, Dictionary<HumanoidBone, (int X, int Y)> projectedPoints, int width)
	{
		var colorSpine = new SKColor(89, 230, 242, 255);
		var colorLeftLimb = new SKColor(64, 179, 255, 255);
		var colorRightLimb = new SKColor(250, 191, 51, 255);
		var colorJoint = new SKColor(255, 255, 255, 255);

		int lineThickness = Math.Max(1, (int)MathF.Round(2.0f * (width / 128.0f)));

		DrawBoneChain(img, projectedPoints, new[] { HumanoidBone.Hips, HumanoidBone.Spine, HumanoidBone.Chest, HumanoidBone.UpperChest, HumanoidBone.Neck, HumanoidBone.Head }, colorSpine, lineThickness);
		DrawBoneChain(img, projectedPoints, new[] { HumanoidBone.UpperChest, HumanoidBone.LeftShoulder, HumanoidBone.LeftUpperArm, HumanoidBone.LeftLowerArm, HumanoidBone.LeftHand }, colorLeftLimb, lineThickness);
		DrawBoneChain(img, projectedPoints, new[] { HumanoidBone.UpperChest, HumanoidBone.RightShoulder, HumanoidBone.RightUpperArm, HumanoidBone.RightLowerArm, HumanoidBone.RightHand }, colorRightLimb, lineThickness);
		DrawBoneChain(img, projectedPoints, new[] { HumanoidBone.Hips, HumanoidBone.LeftUpperLeg, HumanoidBone.LeftLowerLeg, HumanoidBone.LeftFoot, HumanoidBone.LeftToes }, colorLeftLimb, lineThickness);
		DrawBoneChain(img, projectedPoints, new[] { HumanoidBone.Hips, HumanoidBone.RightUpperLeg, HumanoidBone.RightLowerLeg, HumanoidBone.RightFoot, HumanoidBone.RightToes }, colorRightLimb, lineThickness);

		int headRadius = Math.Max(2, (int)MathF.Round(5.0f * (width / 128.0f)));
		int torsoRadius = Math.Max(1, (int)MathF.Round(3.0f * (width / 128.0f)));
		int limbRadius = Math.Max(1, (int)MathF.Round(2.0f * (width / 128.0f)));

		foreach (var pair in projectedPoints)
		{
			int radius = (pair.Key == HumanoidBone.Head) ? headRadius : ((pair.Key == HumanoidBone.Hips || pair.Key == HumanoidBone.Chest) ? torsoRadius : limbRadius);
			DrawFilledCircle(img, pair.Value.X, pair.Value.Y, radius, colorJoint);
		}
	}

	private static (int X, int Y) Project3DTo2D(Vector3 worldPos, int width, int height, float scale)
	{
		float targetCenterX = 0f;
		float targetCenterY = 0.85f;
		float targetCenterZ = 0f;

		float dx = worldPos.X - targetCenterX;
		float dy = worldPos.Y - targetCenterY;
		float dz = worldPos.Z - targetCenterZ;

		float cosAz = MathF.Cos(0.42f);
		float sinAz = MathF.Sin(0.42f);
		float cosEl = MathF.Cos(0.18f);
		float sinEl = MathF.Sin(0.18f);

		float camX = dx * cosAz - dz * sinAz;
		float camZ = dx * sinAz + dz * cosAz;
		float camY = dy * cosEl - camZ * sinEl;

		float pixelsPerMeter = 56.0f * (width / 128.0f) * scale;
		int u = (int)MathF.Round((width * 0.5f) + camX * pixelsPerMeter);
		int v = (int)MathF.Round((height * 0.515625f) - camY * pixelsPerMeter);

		return (u, v);
	}

	private static void DrawBoneChain(SKBitmap img, Dictionary<HumanoidBone, (int X, int Y)> points, HumanoidBone[] chain, SKColor color, int thickness)
	{
		for (int i = 0; i < chain.Length - 1; i++)
		{
			if (points.TryGetValue(chain[i], out var p0) && points.TryGetValue(chain[i + 1], out var p1))
			{
				DrawThickLine(img, p0.X, p0.Y, p1.X, p1.Y, color, thickness);
			}
		}
	}

	private static void DrawCardBorder(SKBitmap img)
	{
		int width = img.Width;
		int height = img.Height;
		var borderColor = new SKColor(61, 66, 82, 230);

		for (int x = 0; x < width; x++)
		{
			img.SetPixel(x, 0, borderColor);
			img.SetPixel(x, height - 1, borderColor);
		}
		for (int y = 0; y < height; y++)
		{
			img.SetPixel(0, y, borderColor);
			img.SetPixel(width - 1, y, borderColor);
		}
	}

	private static void DrawFloorShadow(SKBitmap img, int centerX)
	{
		int width = img.Width;
		int height = img.Height;
		int groundY = (int)MathF.Round(height * (118f / 128f));
		int radiusX = Math.Max(2, (int)MathF.Round(26f * (width / 128f)));
		int radiusY = Math.Max(1, (int)MathF.Round(7f * (height / 128f)));
		var shadowColor = new SKColor(10, 13, 18, 178);

		for (int y = -radiusY; y <= radiusY; y++)
		{
			for (int x = -radiusX; x <= radiusX; x++)
			{
				DrawFloorShadowPixel(img, centerX, groundY, x, y, radiusX, radiusY, shadowColor, width, height);
			}
		}
	}

	private static void DrawFloorShadowPixel(SKBitmap img, int centerX, int groundY, int x, int y, int radiusX, int radiusY, SKColor shadowColor, int width, int height)
	{
		float normX = (float)x / radiusX;
		float normY = (float)y / radiusY;
		if (normX * normX + normY * normY > 1.0f)
		{
			return;
		}

		int px = centerX + x;
		int py = groundY + y;
		if (px < 0 || px >= width || py < 0 || py >= height)
		{
			return;
		}

		img.SetPixel(px, py, AlphaBlend(img.GetPixel(px, py), shadowColor));
	}

	private static SKColor AlphaBlend(SKColor background, SKColor foreground)
	{
		float srcA = foreground.Alpha / 255.0f;
		float dstA = background.Alpha / 255.0f;
		float outA = srcA + dstA * (1.0f - srcA);
		if (outA <= 0.0001f)
		{
			return new SKColor(0, 0, 0, 0);
		}

		float r = (foreground.Red * srcA + background.Red * dstA * (1.0f - srcA)) / outA;
		float g = (foreground.Green * srcA + background.Green * dstA * (1.0f - srcA)) / outA;
		float b = (foreground.Blue * srcA + background.Blue * dstA * (1.0f - srcA)) / outA;

		return new SKColor(
			(byte)Math.Clamp(MathF.Round(r), 0, 255),
			(byte)Math.Clamp(MathF.Round(g), 0, 255),
			(byte)Math.Clamp(MathF.Round(b), 0, 255),
			(byte)Math.Clamp(MathF.Round(outA * 255.0f), 0, 255)
		);
	}

	private static void DrawThickLine(SKBitmap img, int x0, int y0, int x1, int y1, SKColor color, int thickness)
	{
		int dx = Math.Abs(x1 - x0);
		int dy = Math.Abs(y1 - y0);
		int sx = x0 < x1 ? 1 : -1;
		int sy = y0 < y1 ? 1 : -1;
		int err = dx - dy;
		int halfThickness = thickness / 2;
		int width = img.Width;
		int height = img.Height;

		while (true)
		{
			DrawThickLinePixels(img, x0, y0, halfThickness, color, width, height);

			if (x0 == x1 && y0 == y1)
			{
				break;
			}

			int e2 = 2 * err;
			if (e2 > -dy)
			{
				err -= dy;
				x0 += sx;
			}
			if (e2 < dx)
			{
				err += dx;
				y0 += sy;
			}
		}
	}

	private static void DrawThickLinePixels(SKBitmap img, int x0, int y0, int halfThickness, SKColor color, int width, int height)
	{
		for (int ty = -halfThickness; ty <= halfThickness; ty++)
		{
			for (int tx = -halfThickness; tx <= halfThickness; tx++)
			{
				DrawThickLinePixel(img, x0, y0, tx, ty, color, width, height);
			}
		}
	}

	private static void DrawThickLinePixel(SKBitmap img, int x0, int y0, int tx, int ty, SKColor color, int width, int height)
	{
		int px = x0 + tx;
		int py = y0 + ty;
		if (px < 0 || px >= width || py < 0 || py >= height)
		{
			return;
		}
		img.SetPixel(px, py, color);
	}

	private static void DrawFilledCircle(SKBitmap img, int cx, int cy, int radius, SKColor color)
	{
		int r2 = radius * radius;
		int width = img.Width;
		int height = img.Height;

		for (int y = -radius; y <= radius; y++)
		{
			for (int x = -radius; x <= radius; x++)
			{
				DrawFilledCirclePixel(img, cx, cy, x, y, r2, color, width, height);
			}
		}
	}

	private static void DrawFilledCirclePixel(SKBitmap img, int cx, int cy, int x, int y, int r2, SKColor color, int width, int height)
	{
		if (x * x + y * y > r2)
		{
			return;
		}

		int px = cx + x;
		int py = cy + y;
		if (px < 0 || px >= width || py < 0 || py >= height)
		{
			return;
		}

		img.SetPixel(px, py, color);
	}

	private static Vector3 SamplePosition(RealmKeyframeVector3[] keys, float time)
	{
		if (keys == null || keys.Length == 0)
		{
			return Vector3.Zero;
		}
		if (keys.Length == 1 || time <= keys[0].Time)
		{
			return new Vector3(keys[0].X, keys[0].Y, keys[0].Z);
		}
		if (time >= keys[^1].Time)
		{
			return new Vector3(keys[^1].X, keys[^1].Y, keys[^1].Z);
		}

		return InterpolatePositionKeys(keys, time);
	}

	private static Vector3 InterpolatePositionKeys(RealmKeyframeVector3[] keys, float time)
	{
		for (int i = 0; i < keys.Length - 1; i++)
		{
			if (time >= keys[i].Time && time <= keys[i + 1].Time)
			{
				float segDuration = keys[i + 1].Time - keys[i].Time;
				float t = segDuration > 0.0001f ? (time - keys[i].Time) / segDuration : 0f;
				var p0 = new Vector3(keys[i].X, keys[i].Y, keys[i].Z);
				var p1 = new Vector3(keys[i + 1].X, keys[i + 1].Y, keys[i + 1].Z);
				return Vector3.Lerp(p0, p1, t);
			}
		}
		return new Vector3(keys[0].X, keys[0].Y, keys[0].Z);
	}

	private static Quaternion SampleRotation(RealmKeyframeQuaternion[] keys, float time)
	{
		if (keys == null || keys.Length == 0)
		{
			return Quaternion.Identity;
		}
		if (keys.Length == 1 || time <= keys[0].Time)
		{
			var q = new Quaternion(keys[0].X, keys[0].Y, keys[0].Z, keys[0].W);
			return q.LengthSquared() > 0.0001f ? Quaternion.Normalize(q) : Quaternion.Identity;
		}
		if (time >= keys[^1].Time)
		{
			var q = new Quaternion(keys[^1].X, keys[^1].Y, keys[^1].Z, keys[^1].W);
			return q.LengthSquared() > 0.0001f ? Quaternion.Normalize(q) : Quaternion.Identity;
		}

		return InterpolateRotationKeys(keys, time);
	}

	private static Quaternion InterpolateRotationKeys(RealmKeyframeQuaternion[] keys, float time)
	{
		for (int i = 0; i < keys.Length - 1; i++)
		{
			if (time >= keys[i].Time && time <= keys[i + 1].Time)
			{
				float segDuration = keys[i + 1].Time - keys[i].Time;
				float t = segDuration > 0.0001f ? (time - keys[i].Time) / segDuration : 0f;
				var q0 = Quaternion.Normalize(new Quaternion(keys[i].X, keys[i].Y, keys[i].Z, keys[i].W));
				var q1 = Quaternion.Normalize(new Quaternion(keys[i + 1].X, keys[i + 1].Y, keys[i + 1].Z, keys[i + 1].W));
				return Quaternion.Slerp(q0, q1, t);
			}
		}
		return Quaternion.Identity;
	}
}
