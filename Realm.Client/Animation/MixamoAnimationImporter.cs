using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using GAnimation = global::Godot.Animation;

namespace Realm.Client.Animation;

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
		Node rootNode = LoadSceneDocument(filePath, ext);

		if (rootNode == null) return result;

		try
		{
			var players = FindAllAnimationPlayers(rootNode);
			string fileBaseName = !string.IsNullOrEmpty(originalFileName) 
				? Path.GetFileNameWithoutExtension(originalFileName) 
				: Path.GetFileNameWithoutExtension(filePath);

			ExtractAnimationsFromPlayers(players, fileBaseName, result);
		}
		finally
		{
			rootNode.Free();
		}

		return result;
	}

	private static Node LoadSceneDocument(string filePath, string ext)
	{
		if (ext == ".fbx")
		{
			var doc = new FbxDocument();
			var state = new FbxState();
			var err = doc.AppendFromFile(filePath, state);
			if (err == Error.Ok) return doc.GenerateScene(state);
			
			GD.PrintErr($"[MixamoAnimationImporter] Failed to parse FBX: {filePath}, error: {err}");
			return null;
		}
		else
		{
			var doc = new GltfDocument();
			var state = new GltfState();
			var err = doc.AppendFromFile(filePath, state);
			if (err == Error.Ok) return doc.GenerateScene(state);
			
			GD.PrintErr($"[MixamoAnimationImporter] Failed to parse GLB/GLTF: {filePath}, error: {err}");
			return null;
		}
	}

	private static void ExtractAnimationsFromPlayers(List<AnimationPlayer> players, string fileBaseName, List<(string AnimationName, RealmAnimationData Data)> result)
	{
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
			ProcessTrack(godotAnim, t, trackList, boneTrackDict);
		}

		data.Tracks = trackList.ToArray();
		return data;
	}

	private static void ProcessTrack(GAnimation godotAnim, int t, List<RealmAnimationBoneTrack> trackList, Dictionary<string, RealmAnimationBoneTrack> boneTrackDict)
	{
		var trackType = godotAnim.TrackGetType(t);
		if (trackType != GAnimation.TrackType.Position3D &&
			trackType != GAnimation.TrackType.Rotation3D &&
			trackType != GAnimation.TrackType.Scale3D)
		{
			return;
		}

		NodePath path = godotAnim.TrackGetPath(t);
		string pathStr = path.ToString();
		string rawBoneName = ExtractBoneNameFromTrackPath(pathStr);
		if (string.IsNullOrEmpty(rawBoneName)) return;

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
			boneTrack.PositionKeys = ExtractPositionKeys(godotAnim, t, keyCount);
		}
		else if (trackType == GAnimation.TrackType.Rotation3D)
		{
			boneTrack.RotationKeys = ExtractRotationKeys(godotAnim, t, keyCount);
		}
		else if (trackType == GAnimation.TrackType.Scale3D)
		{
			boneTrack.ScaleKeys = ExtractScaleKeys(godotAnim, t, keyCount);
		}
	}

	private static RealmKeyframeVector3[] ExtractPositionKeys(GAnimation godotAnim, int trackIndex, int keyCount)
	{
		var posKeys = new RealmKeyframeVector3[keyCount];
		for (int k = 0; k < keyCount; k++)
		{
			float time = (float)godotAnim.TrackGetKeyTime(trackIndex, k);
			Vector3 val = godotAnim.PositionTrackInterpolate(trackIndex, time);
			posKeys[k] = new RealmKeyframeVector3(time, val.X, val.Y, val.Z);
		}
		return posKeys;
	}

	private static RealmKeyframeQuaternion[] ExtractRotationKeys(GAnimation godotAnim, int trackIndex, int keyCount)
	{
		var rotKeys = new RealmKeyframeQuaternion[keyCount];
		for (int k = 0; k < keyCount; k++)
		{
			float time = (float)godotAnim.TrackGetKeyTime(trackIndex, k);
			Quaternion val = godotAnim.RotationTrackInterpolate(trackIndex, time);
			rotKeys[k] = new RealmKeyframeQuaternion(time, val.X, val.Y, val.Z, val.W);
		}
		return rotKeys;
	}

	private static RealmKeyframeVector3[] ExtractScaleKeys(GAnimation godotAnim, int trackIndex, int keyCount)
	{
		var scaleKeys = new RealmKeyframeVector3[keyCount];
		for (int k = 0; k < keyCount; k++)
		{
			float time = (float)godotAnim.TrackGetKeyTime(trackIndex, k);
			Vector3 val = godotAnim.ScaleTrackInterpolate(trackIndex, time);
			scaleKeys[k] = new RealmKeyframeVector3(time, val.X, val.Y, val.Z);
		}
		return scaleKeys;
	}

	public static bool StripAnimationsFromGlb(string sourceGlbPath, string destGlbPath)
	{
		try
		{
			byte[] glbBytes = File.ReadAllBytes(sourceGlbPath);
			if (!IsValidGlbHeader(glbBytes, out uint magic, out uint version, out uint jsonChunkLength))
			{
				File.Copy(sourceGlbPath, destGlbPath, true);
				return false;
			}

			if (!TryStripAnimationsFromJson(glbBytes, jsonChunkLength, out byte[] paddedJson, out int paddedJsonLength, out bool noAnimationsPresent))
			{
				File.Copy(sourceGlbPath, destGlbPath, true);
				return noAnimationsPresent;
			}

			int binChunkStart = 20 + (int)jsonChunkLength;
			int binChunkTotalLength = glbBytes.Length - binChunkStart;
			uint newTotalLength = 12 + 8 + (uint)paddedJsonLength + (uint)Math.Max(0, binChunkTotalLength);

			WriteStrippedGlb(destGlbPath, magic, version, newTotalLength, paddedJsonLength, paddedJson, glbBytes, binChunkStart, binChunkTotalLength);
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MixamoAnimationImporter] Error stripping animations from GLB: {ex.Message}");
			File.Copy(sourceGlbPath, destGlbPath, true);
			return false;
		}
	}

	private static bool IsValidGlbHeader(byte[] glbBytes, out uint magic, out uint version, out uint jsonChunkLength)
	{
		magic = 0;
		version = 0;
		jsonChunkLength = 0;

		if (glbBytes.Length < 20) return false;

		magic = BitConverter.ToUInt32(glbBytes, 0);
		if (magic != 0x46546C67) return false;

		version = BitConverter.ToUInt32(glbBytes, 4);
		jsonChunkLength = BitConverter.ToUInt32(glbBytes, 12);
		
		uint jsonChunkType = BitConverter.ToUInt32(glbBytes, 16);
		return jsonChunkType == 0x4E4F534A;
	}

	private static bool TryStripAnimationsFromJson(byte[] glbBytes, uint jsonChunkLength, out byte[] paddedJson, out int paddedJsonLength, out bool noAnimationsPresent)
	{
		paddedJson = null;
		paddedJsonLength = 0;
		noAnimationsPresent = false;

		string jsonString = Encoding.UTF8.GetString(glbBytes, 20, (int)jsonChunkLength);
		var jsonNode = JsonNode.Parse(jsonString);
		if (jsonNode is not JsonObject rootObj) return false;

		if (!rootObj.ContainsKey("animations"))
		{
			noAnimationsPresent = true;
			return false;
		}

		rootObj.Remove("animations");
		string strippedJson = rootObj.ToJsonString();
		byte[] strippedJsonBytes = Encoding.UTF8.GetBytes(strippedJson);

		paddedJsonLength = (strippedJsonBytes.Length + 3) & ~3;
		paddedJson = new byte[paddedJsonLength];
		Array.Copy(strippedJsonBytes, paddedJson, strippedJsonBytes.Length);
		for (int i = strippedJsonBytes.Length; i < paddedJsonLength; i++)
		{
			paddedJson[i] = 0x20;
		}

		return true;
	}

	private static void WriteStrippedGlb(string destGlbPath, uint magic, uint version, uint newTotalLength, int paddedJsonLength, byte[] paddedJson, byte[] glbBytes, int binChunkStart, int binChunkTotalLength)
	{
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
				? ProjectSettings.GlobalizePath(UI.MapEditorHUD.TempWorkspaceGodotPath)
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
		return IsGenericAnimationName(cleanCheck) ? fileBaseName : name;
	}

	private static bool IsGenericAnimationName(string cleanCheck)
	{
		return cleanCheck.StartsWith("mixamo", StringComparison.OrdinalIgnoreCase) ||
			cleanCheck.Equals("Layer0", StringComparison.OrdinalIgnoreCase) ||
			cleanCheck.Equals("default", StringComparison.OrdinalIgnoreCase) ||
			cleanCheck.StartsWith("Take", StringComparison.OrdinalIgnoreCase) ||
			cleanCheck.Equals("Animation", StringComparison.OrdinalIgnoreCase);
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
