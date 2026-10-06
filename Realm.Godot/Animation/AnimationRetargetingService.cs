using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using GAnimation = global::Godot.Animation;

namespace Realm.Godot.Animation;

public static class AnimationRetargetingService
{
	private static readonly Dictionary<string, AnimationLibrary> InMemoryRetargetedLibraries = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, RealmAnimationData> CachedRanimData = new(StringComparer.OrdinalIgnoreCase);

	public static void ClearCache()
	{
		InMemoryRetargetedLibraries.Clear();
		CachedRanimData.Clear();
	}

	public static RealmAnimationData GetOrLoadRanimData(string filePath)
	{
		if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;

		if (CachedRanimData.TryGetValue(filePath, out var cached))
		{
			return cached;
		}

		var data = RealmAnimationSerializer.LoadFromFile(filePath);
		if (data != null)
		{
			CachedRanimData[filePath] = data;
		}
		return data;
	}

	public static string ResolveAnimationFilePath(string animName, string unitId = null)
	{
		if (string.IsNullOrEmpty(animName)) return null;

		if (File.Exists(animName)) return animName;

		if (animName.StartsWith("res://") || animName.StartsWith("user://"))
		{
			string globalized = ProjectSettings.GlobalizePath(animName);
			if (File.Exists(globalized)) return globalized;
		}

		string cleanName = animName.ToLowerInvariant();
		if (!cleanName.EndsWith(".ranim")) cleanName += ".ranim";

		var candidateNames = new List<string>();
		if (!string.IsNullOrEmpty(unitId))
		{
			string uClean = unitId.ToLowerInvariant();
			candidateNames.Add($"{uClean}_{cleanName}");
			candidateNames.Add($"{unitId}_{animName}");
		}
		candidateNames.Add(animName);
		if (!animName.EndsWith(".ranim", StringComparison.OrdinalIgnoreCase))
		{
			candidateNames.Add($"{animName}.ranim");
		}
		candidateNames.Add(cleanName);

		string wsPath = !string.IsNullOrEmpty(MapWorkspaceService.GetActiveWorkspacePath())
			? MapWorkspaceService.GetActiveWorkspacePath()
			: ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		if (!string.IsNullOrEmpty(wsPath))
		{
			foreach (var candName in candidateNames)
			{
				string p = Path.Combine(wsPath, "Assets", "animations", candName);
				if (File.Exists(p)) return p;
				string directP = Path.Combine(wsPath, candName);
				if (File.Exists(directP)) return directP;
			}
		}

		string activeMap = GameHost.Instance?.ActiveMapName ?? LobbyManager.Instance?.ActiveMapName;
		if (!string.IsNullOrEmpty(activeMap))
		{
			if (Directory.Exists(activeMap))
			{
				foreach (var candName in candidateNames)
				{
					string p = Path.Combine(activeMap, "Assets", "animations", candName);
					if (File.Exists(p)) return p;
				}
			}

			string currentMapDir = GameHost.Instance?.CurrentMapDirectory;
			if (!string.IsNullOrEmpty(currentMapDir) && Directory.Exists(currentMapDir))
			{
				foreach (var candName in candidateNames)
				{
					string p = Path.Combine(currentMapDir, "Assets", "animations", candName);
					if (File.Exists(p)) return p;
				}
			}

			string mapDir = ProjectSettings.GlobalizePath($"user://maps/{activeMap}");
			foreach (var candName in candidateNames)
			{
				string p = Path.Combine(mapDir, "Assets", "animations", candName);
				if (File.Exists(p)) return p;
			}
		}

		string resDir = ProjectSettings.GlobalizePath("res://");
		foreach (var candName in candidateNames)
		{
			string p = Path.Combine(resDir, "Assets", "animations", candName);
			if (File.Exists(p)) return p;
		}

		return null;
	}

	public static AnimationPlayer FindOrCreateAnimationPlayer(Node modelRoot)
	{
		if (modelRoot == null) return null;
		if (modelRoot is AnimationPlayer existingPlayer) return existingPlayer;

		var player = FindAnimationPlayerRecursive(modelRoot);
		if (player != null) return player;

		player = new AnimationPlayer();
		player.Name = "AnimationPlayer";
		modelRoot.AddChild(player);
		player.Owner = modelRoot;
		return player;
	}

	private static AnimationPlayer FindAnimationPlayerRecursive(Node node)
	{
		if (node == null) return null;
		if (node is AnimationPlayer ap) return ap;

		int childCount = node.GetChildCount();
		for (int i = 0; i < childCount; i++)
		{
			var found = FindAnimationPlayerRecursive(node.GetChild(i));
			if (found != null) return found;
		}

		return null;
	}

	public static GAnimation RetargetAnimation(
		RealmAnimationData animData,
		Skeleton3D targetSkeleton,
		NodePath skeletonRelativePath,
		string? animationName = null)
	{
		if (animData == null || targetSkeleton == null) return null;

		var godotAnim = new GAnimation();
		godotAnim.Length = animData.Duration > 0f ? animData.Duration : 1.0f;
		godotAnim.LoopMode = animData.LoopMode switch
		{
			RealmAnimationLoopMode.Linear => GAnimation.LoopModeEnum.Linear,
			RealmAnimationLoopMode.PingPong => GAnimation.LoopModeEnum.Pingpong,
			_ => GAnimation.LoopModeEnum.None
		};
		godotAnim.Step = animData.FrameRate > 0f ? 1.0f / animData.FrameRate : 1.0f / 30.0f;

		var boneMap = targetSkeleton.BuildSkeletonBoneMap();
		string skelPathStr = skeletonRelativePath.ToString();
		if (string.IsNullOrEmpty(skelPathStr) || skelPathStr == ".")
		{
			skelPathStr = targetSkeleton.Name;
		}

		float hipHeightRatio = 1.0f;
		if (boneMap.TryGetValue(HumanoidBone.Hips, out int hipBoneIdx))
		{
			Transform3D hipRest = targetSkeleton.GetBoneRest(hipBoneIdx);
			float targetHipHeight = MathF.Abs(hipRest.Origin.Y);
			if (targetHipHeight > 0.01f)
			{
				hipHeightRatio = targetHipHeight / 1.0f;
			}
		}

		bool isDeath = !string.IsNullOrEmpty(animationName) && animationName.StartsWith("Death", StringComparison.OrdinalIgnoreCase);

		foreach (var track in animData.Tracks)
		{
			if (track == null || string.IsNullOrEmpty(track.BoneName)) continue;

			int targetBoneIndex = -1;
			string targetBoneName = string.Empty;

			if (HumanoidBoneMapper.TryMapToCanonical(track.BoneName, out var canonicalBone))
			{
				if (boneMap.TryGetValue(canonicalBone, out int mappedIdx))
				{
					targetBoneIndex = mappedIdx;
					targetBoneName = targetSkeleton.GetBoneName(mappedIdx);
				}
			}

			if (targetBoneIndex < 0)
			{
				targetBoneIndex = targetSkeleton.FindBone(track.BoneName);
				if (targetBoneIndex >= 0)
				{
					targetBoneName = targetSkeleton.GetBoneName(targetBoneIndex);
				}
			}

			if (targetBoneIndex < 0 || string.IsNullOrEmpty(targetBoneName)) continue;

			NodePath boneTrackPath = new NodePath($"{skelPathStr}:{targetBoneName}");
			bool isHips = canonicalBone == HumanoidBone.Hips;

			if (track.PositionKeys != null && track.PositionKeys.Length > 0 && isHips)
			{
				int posTrackIdx = godotAnim.AddTrack(GAnimation.TrackType.Position3D);
				godotAnim.TrackSetPath(posTrackIdx, boneTrackPath);
				godotAnim.TrackSetInterpolationType(posTrackIdx, GAnimation.InterpolationType.Linear);

				var firstKey = track.PositionKeys[0];
				var lastKey = track.PositionKeys[^1];
				float driftX = (!isDeath && track.PositionKeys.Length > 1) ? (lastKey.X - firstKey.X) : 0f;
				float driftZ = (!isDeath && track.PositionKeys.Length > 1) ? (lastKey.Z - firstKey.Z) : 0f;

				float totalDuration = lastKey.Time - firstKey.Time;
				if (totalDuration <= 0f && animData.Duration > 0f)
				{
					totalDuration = animData.Duration;
				}

				var basePos = track.PositionKeys[0];
				float posScale = hipHeightRatio;
				foreach (var key in track.PositionKeys)
				{
					float progress = totalDuration > 0.0001f ? Math.Clamp((key.Time - firstKey.Time) / totalDuration, 0f, 1f) : 0f;
					float localX = (key.X - basePos.X) - driftX * progress;
					float localZ = (key.Z - basePos.Z) - driftZ * progress;
					float dx = localX * posScale;
					float dy = (key.Y - basePos.Y) * posScale;
					float dz = localZ * posScale;
					godotAnim.PositionTrackInsertKey(posTrackIdx, key.Time, new Vector3(dx, dy, dz));
				}
			}

			if (track.RotationKeys != null && track.RotationKeys.Length > 0)
			{
				int rotTrackIdx = godotAnim.AddTrack(GAnimation.TrackType.Rotation3D);
				godotAnim.TrackSetPath(rotTrackIdx, boneTrackPath);
				godotAnim.TrackSetInterpolationType(rotTrackIdx, GAnimation.InterpolationType.Linear);

				foreach (var key in track.RotationKeys)
				{
					var quat = new Quaternion(key.X, key.Y, key.Z, key.W);
					if (quat.LengthSquared() > 0.0001f)
					{
						quat = quat.Normalized();
					}
					else
					{
						quat = Quaternion.Identity;
					}
					godotAnim.RotationTrackInsertKey(rotTrackIdx, key.Time, quat);
				}
			}

			if (track.ScaleKeys != null && track.ScaleKeys.Length > 0)
			{
				int scaleTrackIdx = godotAnim.AddTrack(GAnimation.TrackType.Scale3D);
				godotAnim.TrackSetPath(scaleTrackIdx, boneTrackPath);
				godotAnim.TrackSetInterpolationType(scaleTrackIdx, GAnimation.InterpolationType.Linear);

				foreach (var key in track.ScaleKeys)
				{
					godotAnim.ScaleTrackInsertKey(scaleTrackIdx, key.Time, new Vector3(key.X, key.Y, key.Z));
				}
			}
		}

		return godotAnim;
	}

	public static bool RetargetAndBind(
		RealmAnimationData animData,
		Node targetModel,
		string animationName,
		out string errorMessage)
	{
		errorMessage = string.Empty;

		if (animData == null)
		{
			errorMessage = "Animation data is null.";
			return false;
		}

		if (targetModel == null)
		{
			errorMessage = "Target model is null.";
			return false;
		}

		var validation = SkeletonValidator.Validate(targetModel);
		if (!validation.IsValid)
		{
			errorMessage = validation.ErrorMessage;
			return false;
		}

		var player = FindOrCreateAnimationPlayer(targetModel);
		if (player == null)
		{
			errorMessage = "Failed to create or locate AnimationPlayer on target model.";
			return false;
		}

		Node animMixerRoot = null;
		if (player.IsInsideTree() && !player.RootNode.IsEmpty)
		{
			animMixerRoot = player.GetNodeOrNull(player.RootNode);
		}
		if (animMixerRoot == null)
		{
			animMixerRoot = targetModel;
			if (player.GetParent() == targetModel)
			{
				player.RootNode = new NodePath("..");
			}
			else
			{
				player.RootNode = player.GetPathTo(targetModel);
			}
		}

		NodePath relativeSkelPath = animMixerRoot.GetPathTo(validation.Skeleton);
		var godotAnim = RetargetAnimation(animData, validation.Skeleton, relativeSkelPath, animationName);
		if (godotAnim == null)
		{
			errorMessage = "Failed to generate retargeted Godot Animation resource in RAM.";
			return false;
		}

		if (!player.HasAnimationLibrary(string.Empty))
		{
			player.AddAnimationLibrary(string.Empty, new AnimationLibrary());
		}

		var library = player.GetAnimationLibrary(string.Empty);
		StringName animStringName = new StringName(animationName);

		if (library.HasAnimation(animStringName))
		{
			library.RemoveAnimation(animStringName);
		}

		library.AddAnimation(animStringName, godotAnim);
		return true;
	}

	public static bool LoadAndBindUnitAnimations(Node modelRoot, string unitId, string modelPath)
	{
		if (modelRoot == null) return false;

		var validation = SkeletonValidator.Validate(modelRoot);
		if (!validation.IsValid)
		{
			return false;
		}

		var player = FindOrCreateAnimationPlayer(modelRoot);
		if (player == null) return false;

		var existingLibs = player.GetAnimationLibraryList();
		foreach (var libName in existingLibs)
		{
			player.RemoveAnimationLibrary(libName);
		}
		player.AddAnimationLibrary(string.Empty, new AnimationLibrary());

		Dictionary<string, List<UnitAnimationEntry>>? customAnimations = null;
		if (!string.IsNullOrEmpty(unitId) && GameHost.Instance != null && GameHost.UnitRegistry.TryGetValue(unitId, out var meta))
		{
			customAnimations = meta.Animations;
		}

		string[] standardAnimations = new[] { "Idle", "Walk", "Attack", "Death", "Labor", "Spell_Cast", "Dance" };
		foreach (var animType in standardAnimations)
		{
			if (customAnimations != null && customAnimations.TryGetValue(animType, out var animFiles) && animFiles != null && animFiles.Count > 0)
			{
				for (int i = 0; i < animFiles.Count; i++)
				{
					string animFile = animFiles[i].Animation;
					string variantName = $"{animType}_{i}";
					string filePath = ResolveAnimationFilePath(animFile, unitId);
					if (!string.IsNullOrEmpty(filePath))
					{
						var animData = GetOrLoadRanimData(filePath);
						if (animData != null)
						{
							RetargetAndBind(animData, modelRoot, variantName, out _);
							if (i == 0)
							{
								RetargetAndBind(animData, modelRoot, animType, out _);
							}
						}
					}
				}
			}
			else
			{
				string filePath = ResolveAnimationFilePath(animType, unitId);
				if (!string.IsNullOrEmpty(filePath))
				{
					var animData = GetOrLoadRanimData(filePath);
					if (animData != null)
					{
						RetargetAndBind(animData, modelRoot, animType, out _);
						RetargetAndBind(animData, modelRoot, $"{animType}_0", out _);
					}
				}
				else
				{
					RealmAnimationData? fallbackAnim = animType switch
					{
						"Idle" => GetIdleAnimationData(unitId),
						"Walk" => RealmDefaultAnimations.Walk,
						"Attack" => RealmDefaultAnimations.Attack,
						"Death" => RealmDefaultAnimations.Death,
						"Labor" => RealmDefaultAnimations.Labor,
						"Spell_Cast" => RealmDefaultAnimations.Spell_Cast,
						"Dance" => RealmDefaultAnimations.Dance,
						_ => null
					};

					if (fallbackAnim != null)
					{
						RetargetAndBind(fallbackAnim, modelRoot, animType, out _);
						RetargetAndBind(fallbackAnim, modelRoot, $"{animType}_0", out _);
					}
				}
			}
		}

		return true;
	}

	public static RealmAnimationData? GetIdleAnimationData(string? unitId = null)
	{
		if (!string.IsNullOrEmpty(unitId))
		{
			string? customPath = ResolveAnimationFilePath("Idle", unitId);
			if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
			{
				var data = GetOrLoadRanimData(customPath);
				if (data != null) return data;
			}
		}

		if (RealmDefaultAnimations.Idle != null)
		{
			return RealmDefaultAnimations.Idle;
		}

		string? filePath = ResolveAnimationFilePath("idle.ranim", unitId);
		if (string.IsNullOrEmpty(filePath))
		{
			string resPath = ProjectSettings.GlobalizePath("res://Assets/animations/idle.ranim");
			if (File.Exists(resPath))
			{
				filePath = resPath;
			}
		}

		if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
		{
			return GetOrLoadRanimData(filePath);
		}

		return null;
	}

	public static bool TryApplyRiggedIdlePose(Node scene, string? unitId = null)
	{
		try
		{
			if (scene == null || !GodotObject.IsInstanceValid(scene)) return false;

			var validation = SkeletonValidator.Validate(scene);
			if (!validation.IsValid) return false;

			var idleData = GetIdleAnimationData(unitId);
			if (idleData == null) return false;

			if (RetargetAndBind(idleData, scene, "Idle", out _))
			{
				var player = FindOrCreateAnimationPlayer(scene);
				if (player != null && player.HasAnimation("Idle"))
				{
					player.ProcessMode = Node.ProcessModeEnum.Inherit;
					player.Play("Idle");
					player.Seek(0.0, update: true);
					player.Pause();
					return true;
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to apply idle pose to rigged mesh: {ex.Message}");
		}

		return false;
	}
}
