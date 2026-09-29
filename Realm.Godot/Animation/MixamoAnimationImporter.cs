using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Realm.Shared.Metadata;
using GAnimation = global::Godot.Animation;

namespace Realm.Godot.Animation;

public class MixamoImportResult
{
	public bool Success { get; set; }
	public string StrippedGlbPath { get; set; } = string.Empty;
	public List<string> ExtractedAnimationFiles { get; set; } = new();
	public List<string> ExtractedAnimationNames { get; set; } = new();
	public string ErrorMessage { get; set; } = string.Empty;
}

public static class MixamoAnimationImporter
{
	public static List<(string AnimationName, RealmAnimationData Data)> ExtractAnimationsFromGlb(string glbPath)
	{
		return ExtractAnimationsFromFile(glbPath);
	}

	public static List<(string AnimationName, RealmAnimationData Data)> ExtractAnimationsFromFile(string filePath, string originalFileName = null)
	{
		var result = new List<(string AnimationName, RealmAnimationData Data)>();
		if (!File.Exists(filePath)) return result;

		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		Node rootNode = null;

		if (ext == ".fbx")
		{
			var doc = new FbxDocument();
			var state = new FbxState();
			var err = doc.AppendFromFile(filePath, state);
			if (err == Error.Ok)
			{
				rootNode = doc.GenerateScene(state);
			}
			else
			{
				GD.PrintErr($"[MixamoAnimationImporter] Failed to parse FBX: {filePath}, error: {err}");
			}
		}
		else
		{
			var doc = new GltfDocument();
			var state = new GltfState();
			var err = doc.AppendFromFile(filePath, state);
			if (err == Error.Ok)
			{
				rootNode = doc.GenerateScene(state);
			}
			else
			{
				GD.PrintErr($"[MixamoAnimationImporter] Failed to parse GLB/GLTF: {filePath}, error: {err}");
			}
		}

		if (rootNode == null) return result;

		try
		{
			var players = FindAllAnimationPlayers(rootNode);
			string fileBaseName = !string.IsNullOrEmpty(originalFileName) 
				? Path.GetFileNameWithoutExtension(originalFileName) 
				: Path.GetFileNameWithoutExtension(filePath);

			foreach (var player in players)
			{
				var animList = player.GetAnimationList();
				foreach (var animName in animList)
				{
					var godotAnim = player.GetAnimation(animName);
					if (godotAnim == null) continue;

					string sanitizedName = SanitizeAnimationName(animName.ToString(), fileBaseName, animList.Length);
					var animData = ConvertGodotAnimationToRealm(godotAnim, sanitizedName);
					if (animData != null && animData.Tracks.Length > 0)
					{
						result.Add((sanitizedName, animData));
					}
				}
			}
		}
		finally
		{
			rootNode.Free();
		}

		return result;
	}

	public static (string SavedFileName, string Blake3Hash, bool AlreadyExisted) SaveAnimationWithDeduplication(string outputDir, string baseAnimName, RealmAnimationData animData)
	{
		if (!Directory.Exists(outputDir))
		{
			Directory.CreateDirectory(outputDir);
		}

		byte[] newBytes = RealmAnimationSerializer.Serialize(animData);
		string newHash = RealmMetadataHelper.ComputeBlake3(newBytes, ".ranim");

		string cleanBase = baseAnimName.ToLowerInvariant().Replace(' ', '_');
		if (cleanBase.EndsWith(".ranim"))
		{
			cleanBase = cleanBase.Substring(0, cleanBase.Length - ".ranim".Length);
		}

		string targetFileName = $"{cleanBase}.ranim";
		string targetPath = Path.Combine(outputDir, targetFileName);

		if (!File.Exists(targetPath))
		{
			File.WriteAllBytes(targetPath, newBytes);
			return (targetFileName, newHash, false);
		}

		byte[] existingBytes = File.ReadAllBytes(targetPath);
		string existingHash = RealmMetadataHelper.ComputeBlake3(existingBytes, ".ranim");
		if (existingHash.Equals(newHash, StringComparison.OrdinalIgnoreCase))
		{
			return (targetFileName, newHash, true);
		}

		for (int i = 1; i <= 9999; i++)
		{
			string varFileName = $"{cleanBase}_{i}.ranim";
			string varPath = Path.Combine(outputDir, varFileName);
			if (!File.Exists(varPath))
			{
				File.WriteAllBytes(varPath, newBytes);
				return (varFileName, newHash, false);
			}

			byte[] varBytes = File.ReadAllBytes(varPath);
			string varHash = RealmMetadataHelper.ComputeBlake3(varBytes, ".ranim");
			if (varHash.Equals(newHash, StringComparison.OrdinalIgnoreCase))
			{
				return (varFileName, newHash, true);
			}
		}

		return (targetFileName, newHash, false);
	}

	public static RealmAnimationData ConvertGodotAnimationToRealm(GAnimation godotAnim, string animationName)
	{
		if (godotAnim == null) return null;

		var data = new RealmAnimationData
		{
			FormatVersion = 1,
			Name = animationName,
			Duration = (float)godotAnim.Length,
			FrameRate = godotAnim.Step > 0 ? (float)(1.0 / godotAnim.Step) : 30.0f,
			LoopMode = godotAnim.LoopMode switch
			{
				GAnimation.LoopModeEnum.Linear => RealmAnimationLoopMode.Linear,
				GAnimation.LoopModeEnum.Pingpong => RealmAnimationLoopMode.PingPong,
				_ => RealmAnimationLoopMode.None
			}
		};

		var trackList = new List<RealmAnimationBoneTrack>();
		var boneTrackDict = new Dictionary<string, RealmAnimationBoneTrack>(StringComparer.OrdinalIgnoreCase);

		int trackCount = godotAnim.GetTrackCount();
		for (int t = 0; t < trackCount; t++)
		{
			var trackType = godotAnim.TrackGetType(t);
			if (trackType != GAnimation.TrackType.Position3D &&
				trackType != GAnimation.TrackType.Rotation3D &&
				trackType != GAnimation.TrackType.Scale3D)
			{
				continue;
			}

			NodePath path = godotAnim.TrackGetPath(t);
			string pathStr = path.ToString();
			string rawBoneName = ExtractBoneNameFromTrackPath(pathStr);
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

			int keyCount = godotAnim.TrackGetKeyCount(t);
			if (trackType == GAnimation.TrackType.Position3D)
			{
				var posKeys = new RealmKeyframeVector3[keyCount];
				for (int k = 0; k < keyCount; k++)
				{
					float time = (float)godotAnim.TrackGetKeyTime(t, k);
					Vector3 val = godotAnim.PositionTrackInterpolate(t, time);
					posKeys[k] = new RealmKeyframeVector3(time, val.X, val.Y, val.Z);
				}
				boneTrack.PositionKeys = posKeys;
			}
			else if (trackType == GAnimation.TrackType.Rotation3D)
			{
				var rotKeys = new RealmKeyframeQuaternion[keyCount];
				for (int k = 0; k < keyCount; k++)
				{
					float time = (float)godotAnim.TrackGetKeyTime(t, k);
					Quaternion val = godotAnim.RotationTrackInterpolate(t, time);
					rotKeys[k] = new RealmKeyframeQuaternion(time, val.X, val.Y, val.Z, val.W);
				}
				boneTrack.RotationKeys = rotKeys;
			}
			else if (trackType == GAnimation.TrackType.Scale3D)
			{
				var scaleKeys = new RealmKeyframeVector3[keyCount];
				for (int k = 0; k < keyCount; k++)
				{
					float time = (float)godotAnim.TrackGetKeyTime(t, k);
					Vector3 val = godotAnim.ScaleTrackInterpolate(t, time);
					scaleKeys[k] = new RealmKeyframeVector3(time, val.X, val.Y, val.Z);
				}
				boneTrack.ScaleKeys = scaleKeys;
			}
		}

		data.Tracks = trackList.ToArray();
		return data;
	}

	public static bool StripAnimationsFromGlb(string sourceGlbPath, string destGlbPath)
	{
		try
		{
			byte[] glbBytes = File.ReadAllBytes(sourceGlbPath);
			if (glbBytes.Length < 20)
			{
				File.Copy(sourceGlbPath, destGlbPath, true);
				return false;
			}

			uint magic = BitConverter.ToUInt32(glbBytes, 0);
			if (magic != 0x46546C67)
			{
				File.Copy(sourceGlbPath, destGlbPath, true);
				return false;
			}

			uint version = BitConverter.ToUInt32(glbBytes, 4);
			uint totalLength = BitConverter.ToUInt32(glbBytes, 8);

			uint jsonChunkLength = BitConverter.ToUInt32(glbBytes, 12);
			uint jsonChunkType = BitConverter.ToUInt32(glbBytes, 16);
			if (jsonChunkType != 0x4E4F534A)
			{
				File.Copy(sourceGlbPath, destGlbPath, true);
				return false;
			}

			string jsonString = Encoding.UTF8.GetString(glbBytes, 20, (int)jsonChunkLength);
			var jsonNode = JsonNode.Parse(jsonString);
			if (jsonNode is not JsonObject rootObj)
			{
				File.Copy(sourceGlbPath, destGlbPath, true);
				return false;
			}

			if (!rootObj.ContainsKey("animations"))
			{
				File.Copy(sourceGlbPath, destGlbPath, true);
				return true;
			}

			rootObj.Remove("animations");
			string strippedJson = rootObj.ToJsonString();
			byte[] strippedJsonBytes = Encoding.UTF8.GetBytes(strippedJson);

			int paddedJsonLength = (strippedJsonBytes.Length + 3) & ~3;
			byte[] paddedJson = new byte[paddedJsonLength];
			Array.Copy(strippedJsonBytes, paddedJson, strippedJsonBytes.Length);
			for (int i = strippedJsonBytes.Length; i < paddedJsonLength; i++)
			{
				paddedJson[i] = 0x20;
			}

			int binChunkStart = 20 + (int)jsonChunkLength;
			int binChunkTotalLength = glbBytes.Length - binChunkStart;

			uint newTotalLength = 12 + 8 + (uint)paddedJsonLength + (uint)Math.Max(0, binChunkTotalLength);

			string destDir = Path.GetDirectoryName(destGlbPath);
			if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
			{
				Directory.CreateDirectory(destDir);
			}

			if (File.Exists(destGlbPath))
			{
				var attrs = File.GetAttributes(destGlbPath);
				if ((attrs & FileAttributes.ReadOnly) != 0)
				{
					File.SetAttributes(destGlbPath, attrs & ~FileAttributes.ReadOnly);
				}
				File.Delete(destGlbPath);
			}

			using var fs = new FileStream(destGlbPath, FileMode.Create, System.IO.FileAccess.Write);
			using var writer = new BinaryWriter(fs);

			writer.Write(magic);
			writer.Write(version);
			writer.Write(newTotalLength);

			writer.Write((uint)paddedJsonLength);
			writer.Write(0x4E4F534A);
			writer.Write(paddedJson);

			if (binChunkTotalLength > 0)
			{
				writer.Write(glbBytes, binChunkStart, binChunkTotalLength);
			}

			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MixamoAnimationImporter] Error stripping animations from GLB: {ex.Message}");
			File.Copy(sourceGlbPath, destGlbPath, true);
			return false;
		}
	}

	public static MixamoImportResult ImportMixamoGlb(
		string sourceGlbPath,
		string workspacePath,
		string category = "units")
	{
		var result = new MixamoImportResult();
		try
		{
			if (!File.Exists(sourceGlbPath))
			{
				result.Success = false;
				result.ErrorMessage = $"Source file not found: {sourceGlbPath}";
				return result;
			}

			string ws = string.IsNullOrEmpty(workspacePath)
				? ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath)
				: workspacePath;

			string fileName = Path.GetFileName(sourceGlbPath);
			string baseName = Path.GetFileNameWithoutExtension(sourceGlbPath);
			string subCat = category.ToLowerInvariant();

			string modelsDir = Path.Combine(ws, "Assets", "models", subCat);
			Directory.CreateDirectory(modelsDir);
			string targetGlbPath = Path.Combine(modelsDir, fileName);

			string animsDir = Path.Combine(ws, "Assets", "animations");
			Directory.CreateDirectory(animsDir);

			var extractedAnims = ExtractAnimationsFromGlb(sourceGlbPath);
			foreach (var (animName, animData) in extractedAnims)
			{
				string animFileName = $"{animName.ToLowerInvariant()}.ranim";
				string animFilePath = Path.Combine(animsDir, animFileName);
				RealmAnimationSerializer.SaveToFile(animFilePath, animData);
				result.ExtractedAnimationFiles.Add(animFileName);
				result.ExtractedAnimationNames.Add(animName);
			}

			StripAnimationsFromGlb(sourceGlbPath, targetGlbPath);
			result.StrippedGlbPath = targetGlbPath;
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

	private static string SanitizeAnimationName(string godotAnimName, string fileBaseName, int totalAnims)
	{
		if (string.IsNullOrEmpty(godotAnimName) || totalAnims <= 1)
		{
			return !string.IsNullOrEmpty(fileBaseName) ? fileBaseName : (godotAnimName ?? "anim");
		}

		string name = godotAnimName;
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

	private static string ExtractBoneNameFromTrackPath(string trackPath)
	{
		if (string.IsNullOrEmpty(trackPath)) return string.Empty;
		int colonIdx = trackPath.LastIndexOf(':');
		if (colonIdx >= 0)
		{
			return trackPath.Substring(colonIdx + 1);
		}
		int slashIdx = trackPath.LastIndexOf('/');
		if (slashIdx >= 0)
		{
			return trackPath.Substring(slashIdx + 1);
		}
		return trackPath;
	}

	private static List<AnimationPlayer> FindAllAnimationPlayers(Node root)
	{
		var list = new List<AnimationPlayer>();
		FindAllAnimationPlayersRecursive(root, list);
		return list;
	}

	private static void FindAllAnimationPlayersRecursive(Node node, List<AnimationPlayer> list)
	{
		if (node == null) return;
		if (node is AnimationPlayer player)
		{
			list.Add(player);
		}
		int count = node.GetChildCount();
		for (int i = 0; i < count; i++)
		{
			FindAllAnimationPlayersRecursive(node.GetChild(i), list);
		}
	}
}
