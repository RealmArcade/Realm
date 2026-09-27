using Assimp;
using System;
using System.Collections.Generic;
using System.IO;
using Realm.Shared.Metadata;

namespace Realm.Shared.Animation;

public class MixamoFbxConversionResult
{
	public bool Success { get; set; }
	public string InputPath { get; set; } = string.Empty;
	public string OutputPath { get; set; } = string.Empty;
	public List<string> ConvertedAnimationNames { get; set; } = new();
	public string ErrorMessage { get; set; } = string.Empty;
}

public static class MixamoFbxConverter
{
	public static List<(string AnimationName, RealmAnimationData Data)> ExtractAnimationsFromFbx(string fbxPath, string? originalFileName = null)
	{
		var result = new List<(string AnimationName, RealmAnimationData Data)>();
		if (!File.Exists(fbxPath)) return result;

		using var importer = new AssimpContext();
		var scene = importer.ImportFile(fbxPath, PostProcessSteps.None);
		if (scene == null || scene.AnimationCount == 0)
		{
			return result;
		}

		var preRotationMap = new Dictionary<string, System.Numerics.Quaternion>(StringComparer.OrdinalIgnoreCase);
		var postRotationMap = new Dictionary<string, System.Numerics.Quaternion>(StringComparer.OrdinalIgnoreCase);

		void IndexNodeTransforms(Node node)
		{
			int assimpIndex = node.Name.IndexOf("_$AssimpFbx$_", StringComparison.OrdinalIgnoreCase);
			if (assimpIndex >= 0)
			{
				string baseName = node.Name.Substring(0, assimpIndex);
				string suffix = node.Name.Substring(assimpIndex + "_$AssimpFbx$_".Length);
				if (suffix.Equals("PreRotation", StringComparison.OrdinalIgnoreCase))
				{
					node.Transform.Decompose(out _, out var rotation, out _);
					var quat = new System.Numerics.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
					if (quat.LengthSquared() > 0.0001f)
					{
						preRotationMap[baseName] = System.Numerics.Quaternion.Normalize(quat);
					}
				}
				else if (suffix.Equals("PostRotation", StringComparison.OrdinalIgnoreCase))
				{
					node.Transform.Decompose(out _, out var rotation, out _);
					var quat = new System.Numerics.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
					if (quat.LengthSquared() > 0.0001f)
					{
						postRotationMap[baseName] = System.Numerics.Quaternion.Normalize(quat);
					}
				}
			}

			foreach (var child in node.Children)
			{
				IndexNodeTransforms(child);
			}
		}

		if (scene.RootNode != null)
		{
			IndexNodeTransforms(scene.RootNode);
		}

		string fileBaseName = !string.IsNullOrEmpty(originalFileName)
			? Path.GetFileNameWithoutExtension(originalFileName)
			: Path.GetFileNameWithoutExtension(fbxPath);

		for (int i = 0; i < scene.AnimationCount; i++)
		{
			var assimpAnim = scene.Animations[i];
			string animName = SanitizeAnimationName(assimpAnim.Name, fileBaseName, scene.AnimationCount);
			double ticksPerSecond = assimpAnim.TicksPerSecond > 0 ? assimpAnim.TicksPerSecond : 30.0;
			float duration = (float)(assimpAnim.DurationInTicks / ticksPerSecond);

			var realmAnim = new RealmAnimationData
			{
				FormatVersion = 1,
				Name = animName,
				Duration = duration,
				FrameRate = (float)ticksPerSecond,
				LoopMode = RealmAnimationLoopMode.Linear
			};

			var trackList = new List<RealmAnimationBoneTrack>();
			var boneTrackDict = new Dictionary<string, RealmAnimationBoneTrack>(StringComparer.OrdinalIgnoreCase);

			foreach (var channel in assimpAnim.NodeAnimationChannels)
			{
				string rawBoneName = HumanoidBoneMapper.CleanBoneName(channel.NodeName);
				if (string.IsNullOrEmpty(rawBoneName)) continue;

				string canonicalName = rawBoneName;
				if (HumanoidBoneMapper.TryMapToCanonical(rawBoneName, out var canonicalBone))
				{
					canonicalName = canonicalBone.ToString();
				}

				if (!boneTrackDict.TryGetValue(canonicalName, out var boneTrack))
				{
					boneTrack = new RealmAnimationBoneTrack
					{
						BoneName = canonicalName
					};
					boneTrackDict[canonicalName] = boneTrack;
					trackList.Add(boneTrack);
				}

				string rawNodeBase = channel.NodeName;
				int assimpIndex = rawNodeBase.IndexOf("_$AssimpFbx$_", StringComparison.OrdinalIgnoreCase);
				string baseNodeName = assimpIndex >= 0 ? rawNodeBase.Substring(0, assimpIndex) : rawNodeBase;

				var preRotation = preRotationMap.TryGetValue(baseNodeName, out var pr) ? pr : System.Numerics.Quaternion.Identity;
				var postRotation = postRotationMap.TryGetValue(baseNodeName, out var po) ? po : System.Numerics.Quaternion.Identity;
				bool hasPreOrPostRotation = preRotation != System.Numerics.Quaternion.Identity || postRotation != System.Numerics.Quaternion.Identity;

				if (channel.HasPositionKeys && channel.PositionKeyCount > 0)
				{
					bool isRootOrCustom = canonicalName.Equals("Hips", StringComparison.OrdinalIgnoreCase) || !HumanoidBoneMapper.TryMapToCanonical(rawBoneName, out _);
					if (isRootOrCustom && (boneTrack.PositionKeys == null || channel.PositionKeyCount > boneTrack.PositionKeys.Length))
					{
						var posKeys = new RealmKeyframeVector3[channel.PositionKeyCount];
						for (int k = 0; k < channel.PositionKeyCount; k++)
						{
							var key = channel.PositionKeys[k];
							float time = (float)(key.Time / ticksPerSecond);
							posKeys[k] = new RealmKeyframeVector3(time, key.Value.X * 0.01f, key.Value.Y * 0.01f, key.Value.Z * 0.01f);
						}
						boneTrack.PositionKeys = posKeys;
					}
				}

				if (channel.HasRotationKeys && channel.RotationKeyCount > 0)
				{
					if (boneTrack.RotationKeys == null || channel.RotationKeyCount > boneTrack.RotationKeys.Length)
					{
						var rotKeys = new RealmKeyframeQuaternion[channel.RotationKeyCount];
						for (int k = 0; k < channel.RotationKeyCount; k++)
						{
							var key = channel.RotationKeys[k];
							float time = (float)(key.Time / ticksPerSecond);
							var quat = new System.Numerics.Quaternion(key.Value.X, key.Value.Y, key.Value.Z, key.Value.W);
							if (hasPreOrPostRotation)
							{
								quat = System.Numerics.Quaternion.Normalize(preRotation * quat * postRotation);
							}
							else if (quat.LengthSquared() > 0.0001f)
							{
								quat = System.Numerics.Quaternion.Normalize(quat);
							}
							rotKeys[k] = new RealmKeyframeQuaternion(time, quat.X, quat.Y, quat.Z, quat.W);
						}
						boneTrack.RotationKeys = rotKeys;
					}
				}

				if (channel.HasScalingKeys && channel.ScalingKeyCount > 0)
				{
					if (boneTrack.ScaleKeys == null || channel.ScalingKeyCount > boneTrack.ScaleKeys.Length)
					{
						var scaleKeys = new RealmKeyframeVector3[channel.ScalingKeyCount];
						for (int k = 0; k < channel.ScalingKeyCount; k++)
						{
							var key = channel.ScalingKeys[k];
							float time = (float)(key.Time / ticksPerSecond);
							scaleKeys[k] = new RealmKeyframeVector3(time, key.Value.X, key.Value.Y, key.Value.Z);
						}
						boneTrack.ScaleKeys = scaleKeys;
					}
				}
			}

			realmAnim.Tracks = trackList.ToArray();
			if (realmAnim.Tracks.Length > 0)
			{
				result.Add((animName, realmAnim));
			}
		}

		return result;
	}

	public static MixamoFbxConversionResult ConvertFbxFile(string fbxPath, string? outputPath = null)
	{
		var result = new MixamoFbxConversionResult { InputPath = fbxPath };
		try
		{
			if (!File.Exists(fbxPath))
			{
				result.Success = false;
				result.ErrorMessage = $"Input file not found: {fbxPath}";
				return result;
			}

			var anims = ExtractAnimationsFromFbx(fbxPath);
			if (anims.Count == 0)
			{
				result.Success = false;
				result.ErrorMessage = "No animation tracks found in FBX file.";
				return result;
			}

			string originalBaseName = Path.GetFileNameWithoutExtension(fbxPath);
			string targetBase = string.IsNullOrEmpty(outputPath)
				? Path.ChangeExtension(fbxPath, ".ranim")
				: outputPath;

			if (Directory.Exists(targetBase) || (string.IsNullOrEmpty(Path.GetExtension(targetBase)) && anims.Count > 1))
			{
				Directory.CreateDirectory(targetBase);
				for (int i = 0; i < anims.Count; i++)
				{
					string outName = anims.Count == 1
						? $"{originalBaseName}.ranim"
						: $"{originalBaseName}_{i}.ranim";
					string fullOut = Path.Combine(targetBase, outName);
					RealmAnimationSerializer.SaveToFile(fullOut, anims[i].Data);
					RealmMetadataHelper.SyncBlake3Metadata(fullOut);
					result.ConvertedAnimationNames.Add(anims[i].AnimationName);
					result.OutputPath = fullOut;
				}
			}
			else
			{
				if (anims.Count == 1)
				{
					RealmAnimationSerializer.SaveToFile(targetBase, anims[0].Data);
					RealmMetadataHelper.SyncBlake3Metadata(targetBase);
					result.ConvertedAnimationNames.Add(anims[0].AnimationName);
					result.OutputPath = targetBase;
				}
				else
				{
					string dir = Path.GetDirectoryName(targetBase) ?? "";
					for (int i = 0; i < anims.Count; i++)
					{
						string outName = $"{originalBaseName}_{i}.ranim";
						string fullOut = Path.Combine(dir, outName);
						RealmAnimationSerializer.SaveToFile(fullOut, anims[i].Data);
						RealmMetadataHelper.SyncBlake3Metadata(fullOut);
						result.ConvertedAnimationNames.Add(anims[i].AnimationName);
						result.OutputPath = fullOut;
					}
				}
			}

			result.Success = true;
			return result;
		}
		catch (Exception ex)
		{
			result.Success = false;
			result.ErrorMessage = ex.Message;
			return result;
		}
	}

	public static int ConvertFbxDirectory(string inputDir, string? outputDir, bool recursive)
	{
		var searchOpt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
		string[] files = Directory.GetFiles(inputDir, "*.fbx", searchOpt);
		Console.WriteLine($"Found {files.Length} .fbx file(s) in {inputDir}");

		int successCount = 0;
		int failCount = 0;

		foreach (var file in files)
		{
			string? target;
			if (string.IsNullOrEmpty(outputDir))
			{
				target = Path.ChangeExtension(file, ".ranim");
			}
			else
			{
				string rel = Path.GetRelativePath(inputDir, file);
				target = Path.Combine(outputDir, Path.ChangeExtension(rel, ".ranim"));
			}

			var res = ConvertFbxFile(file, target);
			if (res.Success)
			{
				Console.WriteLine($"Converted: {file} -> {res.OutputPath} ({string.Join(", ", res.ConvertedAnimationNames)})");
				successCount++;
			}
			else
			{
				Console.Error.WriteLine($"Failed to convert {file}: {res.ErrorMessage}");
				failCount++;
			}
		}

		Console.WriteLine($"Finished FBX conversion. {successCount} succeeded, {failCount} failed.");
		return failCount > 0 ? 1 : 0;
	}

	private static string SanitizeAnimationName(string assimpAnimName, string fileBaseName, int totalAnims)
	{
		if (string.IsNullOrEmpty(assimpAnimName) || totalAnims <= 1)
		{
			return !string.IsNullOrEmpty(fileBaseName) ? fileBaseName : (assimpAnimName ?? "anim");
		}

		string name = assimpAnimName;
		if (name.Contains('|'))
		{
			string[] parts = name.Split('|', StringSplitOptions.RemoveEmptyEntries);
			name = parts[^1];
		}

		string cleanCheck = name.Replace(':', '_').Replace('.', '_');
		if (cleanCheck.StartsWith("mixamo", StringComparison.OrdinalIgnoreCase) ||
			cleanCheck.Equals("Layer0", StringComparison.OrdinalIgnoreCase) ||
			cleanCheck.Equals("default", StringComparison.OrdinalIgnoreCase) ||
			cleanCheck.StartsWith("Take", StringComparison.OrdinalIgnoreCase) ||
			cleanCheck.Equals("Animation", StringComparison.OrdinalIgnoreCase))
		{
			return fileBaseName;
		}

		return name;
	}
}
