using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Arch.Core;
using Godot;
using NSec.Cryptography;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Client.VFX;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.EditorAPI;
using Realm.Shared.Distribution;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Environment = System.Environment;
using HttpClient = System.Net.Http.HttpClient;
using Key = NSec.Cryptography.Key;
using Timer = System.Threading.Timer;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Services;

public partial class EditorService : IEditorAPI
{
	public (bool Success, string ErrorMessage, IReadOnlyList<string> ExtractedAnimationFiles, string FileName) ImportGlbAssetFromExtensionDomain(string sourceFilePath, string category, string wsPath, Action<string, string, string, string> updateMetadataAction)
	{
		string fileName = Path.GetFileName(sourceFilePath);
		string subCat = category.ToLowerInvariant();
		var importResult = MixamoAnimationImporter.ImportMixamoGlb(sourceFilePath, wsPath, subCat);
		if (!importResult.Success)
		{
			return (false, importResult.ErrorMessage, null, fileName);
		}

		byte[] glbBytes = File.ReadAllBytes(importResult.StrippedGlbPath);
		string glbBlake3 = RealmMetadataHelper.ComputeBlake3(glbBytes, ".glb");
		updateMetadataAction("glb", fileName, glbBlake3, subCat);
		foreach (var animFile in importResult.ExtractedAnimationFiles)
		{
			string animPath = Path.Combine(wsPath, "Assets", "animations", animFile);
			if (File.Exists(animPath))
			{
				byte[] animBytes = File.ReadAllBytes(animPath);
				string animBlake3 = RealmMetadataHelper.ComputeBlake3(animBytes, ".ranim");
				updateMetadataAction("animations", animFile, animBlake3, null);
			}
		}

		GameHost.Instance?.LoadUnitMetadata(wsPath);
		return (true, null, importResult.ExtractedAnimationFiles, fileName);
	}

	public string ResolveAnimationFileName(string sourceFilePath, string? preferredFileName)
	{
		string originalFileName = !string.IsNullOrWhiteSpace(preferredFileName) && !AssetIndexService.IsHexHash(Path.GetFileNameWithoutExtension(preferredFileName))
			? Path.GetFileNameWithoutExtension(preferredFileName)
			: (GameContext.AssetIndexService?.ResolvePrettyFileName(sourceFilePath, preferredFileName) ?? Path.GetFileNameWithoutExtension(sourceFilePath));
		return Path.GetFileNameWithoutExtension(originalFileName);
	}

	public (int ImportedCount, int SkippedCount, List<(string FileName, string Blake3)>? NewAnims) Import3DAnimationFileDomain(string sourceFilePath, string originalFileName, string animsDir)
	{
		var extracted = MixamoAnimationImporter.ExtractAnimationsFromFile(sourceFilePath, originalFileName);
		if (extracted.Count == 0)
		{
			return (0, 0, null);
		}

		int importedCount = 0;
		int skippedCount = 0;
		var newAnims = new List<(string FileName, string Blake3)>();

		foreach (var (animName, animData) in extracted)
		{
			var (savedFileName, blake3, alreadyExisted) = MixamoAnimationImporter.SaveAnimationWithDeduplication(animsDir, animName, animData);
			newAnims.Add((savedFileName, blake3));
			if (alreadyExisted) skippedCount++;
			else importedCount++;
		}

		return (importedCount, skippedCount, newAnims);
	}


	public (bool Success, string FinalFileName, string NewHash) ImportRanimFileDomain(string sourceFilePath, string? preferredFileName, string animsDir)
	{
		string resolvedName = !string.IsNullOrWhiteSpace(preferredFileName) && !AssetIndexService.IsHexHash(Path.GetFileNameWithoutExtension(preferredFileName))
			? preferredFileName
			: (GameContext.AssetIndexService?.ResolvePrettyFileName(sourceFilePath, preferredFileName) ?? Path.GetFileName(sourceFilePath));
		string fileName = resolvedName;
		byte[] sourceBytes = File.ReadAllBytes(sourceFilePath);
		string newHash = RealmMetadataHelper.ComputeBlake3(sourceBytes, ".ranim");

		string baseName = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
		string finalFileName = $"{baseName}.ranim";
		string targetPath = Path.Combine(animsDir, finalFileName);

		if (File.Exists(targetPath))
		{
			finalFileName = HandleExistingRanimFileDomain(targetPath, animsDir, baseName, newHash, sourceBytes);
			if (string.IsNullOrEmpty(finalFileName)) return (false, Path.GetFileName(targetPath), newHash);
		}
		else
		{
			File.WriteAllBytes(targetPath, sourceBytes);
		}
		
		return (true, finalFileName, newHash);
	}

	private string HandleExistingRanimFileDomain(string targetPath, string animsDir, string baseName, string newHash, byte[] sourceBytes)
	{
		string existingHash = RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(targetPath), ".ranim");
		if (existingHash.Equals(newHash, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		for (int i = 1; i <= 9999; i++)
		{
			string varName = $"{baseName}_{i}.ranim";
			string varPath = Path.Combine(animsDir, varName);
			if (!File.Exists(varPath))
			{
				File.WriteAllBytes(varPath, sourceBytes);
				return varName;
			}
			else
			{
				string varHash = RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(varPath), ".ranim");
				if (varHash.Equals(newHash, StringComparison.OrdinalIgnoreCase))
				{
					return varName;
				}
			}
		}

		return null;
	}

	public (bool Success, string Message, string FileName, string Blake3) ImportDecalAssetDomain(string sourceFilePath, string wsPath)
	{
		try
		{
			string baseName = Path.GetFileNameWithoutExtension(sourceFilePath);
			string fileName = baseName + ".png";
			string decalsDir = Path.Combine(wsPath, "Assets", "decals");
			Directory.CreateDirectory(decalsDir);
			string targetPath = Path.Combine(decalsDir, fileName);

			var img = Godot.Image.LoadFromFile(sourceFilePath);
			if (img != null)
			{
				img.SavePng(targetPath);
			}
			else
			{
				File.Copy(sourceFilePath, targetPath, true);
			}

			byte[] bytes = File.ReadAllBytes(targetPath);
			string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, ".png");
			return (true, "", fileName, blake3);
		}
		catch (Exception ex)
		{
			return (false, ex.Message, "", "");
		}
	}

	public string PrepareAnimationsDirectory(string wsPath)
	{
		string animsDir = Path.Combine(wsPath, "Assets", "animations");
		Directory.CreateDirectory(animsDir);
		return animsDir;
	}

	public byte[]? GetRtexWebpBytes(string localRtex)
	{
		try
		{
			byte[] bytes = File.ReadAllBytes(localRtex);
			return RtexFile.GetLayer(bytes, 0);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to read Rtex bytes: {ex.Message}");
			return null;
		}
	}

	public async Task<string> ProcessCreatorRegistrationAsync(string username, string pubKeyStr, Key key)
	{
		if (!Realm.Shared.NameNormalizationHelper.ValidateUsername(username, out var validationError))
		{
			return JsonSerializer.Serialize(new { Message = validationError ?? "Invalid username." });
		}

		string errText = await RegisterCreatorAsync(username, pubKeyStr, key);
		if (string.IsNullOrEmpty(errText))
		{
			string keyDir = ProjectSettings.GlobalizePath("user://appdata/keys/");
			AuthorshipKeyHelper.UpdateUserName(keyDir, username);
		}
		
		return errText;
	}

	public async Task<string> RegisterCreatorAsync(string username, string pubKeyStr, Key key)
	{
		byte[] payloadBytes = Encoding.UTF8.GetBytes(username + ":" + pubKeyStr);
		byte[] sigBytes = SignatureAlgorithm.Ed25519.Sign(key, payloadBytes);
		string signatureStr = Convert.ToBase64String(sigBytes);

		var regPayload = new { Username = username, PublicKey = pubKeyStr, Signature = signatureStr };
		string seedServerUrl = GameHost.Instance != null && GodotObject.IsInstanceValid(LobbyManager.Instance) ? LobbyManager.Instance.RegistryServerUrl : ServersConfigHelper.GetDefaultServerUrl();

		using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
		var content = new StringContent(JsonSerializer.Serialize(regPayload), Encoding.UTF8, "application/json");
		var res = await httpClient.PostAsync(seedServerUrl + "/api/creators/register", content);

		if (res.IsSuccessStatusCode)
		{
			return null;
		}
		else
		{
			return await res.Content.ReadAsStringAsync();
		}
	}

	public bool IsTargetUnit(string unitId, string targetId)
	{
		if (string.IsNullOrEmpty(targetId)) return true;
		if (string.Equals(unitId, targetId, StringComparison.OrdinalIgnoreCase)) return true;

		string unitFileName = Path.GetFileNameWithoutExtension(unitId);
		string targetFileName = Path.GetFileNameWithoutExtension(targetId);

		return string.Equals(unitFileName, targetId, StringComparison.OrdinalIgnoreCase) ||
			   string.Equals(unitId, targetFileName, StringComparison.OrdinalIgnoreCase);
	}

	public bool IsTargetProp(string propId, string targetId)
	{
		return string.IsNullOrEmpty(targetId)
			   || string.Equals(propId, targetId, StringComparison.OrdinalIgnoreCase)
			   || string.Equals(Path.GetFileNameWithoutExtension(propId), targetId, StringComparison.OrdinalIgnoreCase)
			   || string.Equals(propId, Path.GetFileNameWithoutExtension(targetId), StringComparison.OrdinalIgnoreCase);
	}

	public void ScalePropEntitiesOnly(float scaleX, float scaleZ, Func<Entity, bool> hasProp3D)
	{
		if (GameContext.World != null)
		{
			var allPropEntitiesQuery = QueryCache.AllPropIdentityAndPositionQuery;
			GameContext.World.Query(in allPropEntitiesQuery, (Entity entity, ref Position posComp) =>
			{
				if (!hasProp3D(entity))
				{
					GameContext.World.Set(entity, new Position(new Vector3(posComp.Value.X * scaleX, posComp.Value.Y, posComp.Value.Z * scaleZ)));
				}
			});
		}
	}

	public void ResetTerrainSplatMap(TerrainSplatWeights[,] splatMap, int width, int depth)
	{
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				splatMap[x, z] = TerrainSplatWeights.CreateSolid(0);
			}
		}
	}

	public void ScaleCoordinateBounds(float scaleX, float scaleZ)
	{
		for (int i = 0; i < this.EditorCoordinates.Count; i++)
		{
			var coord = this.EditorCoordinates[i];
			coord.MinX *= scaleX;
			coord.MaxX *= scaleX;
			coord.MinZ *= scaleZ;
			coord.MaxZ *= scaleZ;
		}
	}

	public void UpdateScaleMetadata(int newWidth, int newDepth)
	{
		string scaleWsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string scaleMetaPath = MetadataService.ResolveMetadataPath(scaleWsPath);

		if (File.Exists(scaleMetaPath))
		{
			try
			{
				GameContext.MetadataService.UpdateMetadata(scaleWsPath, meta =>
				{
					meta.MapProperties.MapWidth = newWidth;
					meta.MapProperties.MapHeight = newDepth;
				});
			}
			catch (Exception ex)
			{
				GD.PrintErr($"Failed to update metadata.json map dimensions during scale: {ex.Message}");
			}
		}
	}

	public Entity EnsureDecalEntity(Entity currentEntity, string decalId, Vector3 position, float rotationY, float scaleX)
	{
		if (!GameContext.World.IsAlive(currentEntity))
		{
			var newEnt = GameContext.World.Create();
			GameContext.World.Add(newEnt, new DecalIdentity(decalId));
			GameContext.World.Add(newEnt, new Position(position));
			GameContext.World.Add(newEnt, new RotationY(rotationY));
			GameContext.World.Add(newEnt, new ModelScale(scaleX));
			return newEnt;
		}

		return currentEntity;
	}

	public void UpdateEntityEcsPosition(Entity entity, Vector3 newPos)
	{
		if (GameContext.World == null) return;
		if (GameContext.World.IsAlive(entity) && GameContext.World.Has<Position>(entity))
		{
			GameContext.World.Set(entity, new Position(newPos));
		}
	}

	public void ClearEditorCoordinates()
	{
		EditorCoordinates.Clear();
	}

	public void ResetPivotToMapCenterDomain()
	{
		var pivot = Vector2.Zero;
		GameHost.Instance.EditorSymmetryPivot = pivot;
		GameHost.Instance.GroundTerrain.SetPolarCenter(pivot);
		GameHost.Instance.UpdateSymmetryPivotVisuals();
		GameHost.Instance.InvalidateSelectionHighlightMesh();
	}

	public (int cx, int cz) SetPivotDomain(Vector3 hitPos)
	{
		var pivot = new Vector2(hitPos.X, hitPos.Z);
		GameHost.Instance.EditorSymmetryPivot = pivot;
		GameHost.Instance.GroundTerrain.SetPolarCenter(pivot);
		GameHost.Instance.UpdateSymmetryPivotVisuals();
		return WorldPosToCellCoords(new Godot.Vector3(hitPos.X, hitPos.Y, hitPos.Z));
	}

	public void SetPolarSpokesDomain(int spokes)
	{
		if (GameHost.Instance == null) return;
		EditorPolarSpokeFolds = spokes;
		EditorPolarRadialStep = 360.0f / spokes;
		GameHost.Instance.GroundTerrain?.SetPolarRadialStep(EditorPolarRadialStep);
	}

	public bool IsApplyGroundTextureEnabledDomain()
	{
		return GameHost.Instance != null && GameHost.Instance.ActiveEditorTool == EditorTool.PaintTexture;
	}

	public bool ImportTerrainFromMinimapPathDomain(string selectedPath, out float[,] smoothedHeights, out TerrainSplatWeights[,] splatMap, out List<(float x, float y, float z, float rot, float scale)> treePositions)
	{
		smoothedHeights = null;
		splatMap = null;
		treePositions = null;
		if (GameHost.Instance?.GroundTerrain == null) return false;
		GameHost.Instance.ClearMapEntirely();
		return GameHost.Instance.ImportTerrainFromMinimap(selectedPath, out smoothedHeights, out splatMap, out treePositions);
	}

	public void ResetToBlankMapDomain()
	{
		if (IsSyncing) return;
		IsSyncing = true;
		IsPaused = true;
		try
		{
			GameHost.Instance?.ClearMapEntirely();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapEditorHUD] ResetToBlankMap error: {ex.Message}");
		}
		finally
		{
			UpdateWatchedFileTimestamps();
			IsPaused = false;
			IsSyncing = false;
		}
	}

	public void EnsureDefaultTemplateAnimationsSafeDomain()
	{
		try
		{
			RealmDefaultAnimations.EnsureDefaultTemplateAnimations(_tempWorkspacePath);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[EditorService] Pre-warming default animations error: {ex.Message}");
		}
	}

	public bool HasCoordinate(string coordinateName)
	{
		if (string.IsNullOrEmpty(coordinateName)) return false;
		string searchName = coordinateName.Trim();
		foreach (var r in EditorCoordinates)
		{
			if (r.Name.Equals(searchName, StringComparison.OrdinalIgnoreCase))
				return true;
		}

		return false;
	}

	public Vector3 GetCoordinateMin(string coordinateName)
	{
		if (string.IsNullOrEmpty(coordinateName)) return Vector3.Zero;
		string searchName = coordinateName.Trim();
		foreach (var r in EditorCoordinates)
		{
			if (r.Name.Equals(searchName, StringComparison.OrdinalIgnoreCase))
				return new Vector3(r.MinX, 0f, r.MinZ);
		}

		return Vector3.Zero;
	}

	public Vector3 GetCoordinateMax(string coordinateName)
	{
		if (string.IsNullOrEmpty(coordinateName)) return Vector3.Zero;
		string searchName = coordinateName.Trim();
		foreach (var r in EditorCoordinates)
		{
			if (r.Name.Equals(searchName, StringComparison.OrdinalIgnoreCase))
				return new Vector3(r.MaxX, 0f, r.MaxZ);
		}

		return Vector3.Zero;
	}

	public bool IsPositionInCoordinate(Vector3 position, string coordinateName)
	{
		if (string.IsNullOrEmpty(coordinateName)) return false;
		string searchName = coordinateName.Trim();
		foreach (var r in EditorCoordinates)
		{
			if (r.Name.Equals(searchName, StringComparison.OrdinalIgnoreCase))
			{
				float minX = Math.Min(r.MinX, r.MaxX);
				float maxX = Math.Max(r.MinX, r.MaxX);
				float minZ = Math.Min(r.MinZ, r.MaxZ);
				float maxZ = Math.Max(r.MinZ, r.MaxZ);
				return position.X >= minX && position.X <= maxX && position.Z >= minZ && position.Z <= maxZ;
			}
		}

		return false;
	}

	public Godot.Vector3 LastRaycastPos { get; set; } = new Godot.Vector3(float.MinValue, float.MinValue, float.MinValue);
	public static readonly float[] PolarRingSpacingOptions = new[] { 4.0f, 8.0f, 16.0f, 32.0f };
	public bool IsSyncing { get; set; } = false;

	public string GetWorkspacePath()
	{
		return MapWorkspaceService.GetActiveWorkspacePath();
	}

	public void SaveCustomUnitAnimations(string unitId, Dictionary<string, List<UnitAnimationEntry>> animations)
	{
		if (string.IsNullOrEmpty(unitId)) return;

		if (GameHost.UnitRegistry.TryGetValue(unitId, out var uMeta))
		{
			uMeta.Animations = animations;
			GameHost.UnitRegistry[unitId] = uMeta;
		}

		string wsPath = GetWorkspacePath();
		string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
		if (!File.Exists(metadataPath)) return;

		GameContext.MetadataService.UpdateMetadata(wsPath, meta =>
		{
			meta.UpdateUnit(unitId, u =>
			{
				u.Animations = animations;
				return u;
			});
		});
		this.LastMetadataSyncTime = File.Exists(metadataPath) ? File.GetLastWriteTimeUtc(metadataPath).Ticks : 0;
	}

	public void SaveCustomAbilityVfxToMetadata(string abilityId, string visualEffect, string castSound, string iconPath, float aoeRadius)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		string wsPath = GetWorkspacePath();
		string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
		if (!File.Exists(metadataPath)) return;

		GameContext.MetadataService.UpdateMetadata(wsPath, meta =>
		{
			meta.UpdateAbility(abilityId, ab =>
			{
				ab.VisualEffect = visualEffect;
				ab.CastSound = castSound;
				ab.IconPath = iconPath;
				ab.AreaOfEffectRadius = aoeRadius;
				return ab;
			});
		});
		this.LastMetadataSyncTime = File.Exists(metadataPath) ? File.GetLastWriteTimeUtc(metadataPath).Ticks : 0;
	}

	public void SaveEntityModelPathToMetadata(string entityId, string fieldName, string domain, string newModelPath)
	{
		if (string.IsNullOrEmpty(entityId)) return;

		fieldName = string.IsNullOrEmpty(fieldName) ? "ModelPath" : fieldName;
		domain = string.IsNullOrEmpty(domain) ? "units" : domain;

		UpdateLocalRegistryForModelPath(entityId, fieldName, domain, newModelPath);
		UpdateMetadataFileForModelPath(entityId, fieldName, domain, newModelPath);
	}

	private void UpdateLocalRegistryForModelPath(string entityId, string fieldName, string domain, string newModelPath)
	{
		switch (domain.ToLowerInvariant())
		{
			case "units":
				if (!GameHost.UnitRegistry.TryGetValue(entityId, out var uMeta)) return;
				if (fieldName == "PortraitModelPath") uMeta.PortraitModelPath = newModelPath;
				else uMeta.ModelPath = newModelPath;
				GameHost.UnitRegistry[entityId] = uMeta;
				break;
			case "buildings":
				if (!GameHost.BuildingRegistry.TryGetValue(entityId, out var bMeta)) return;
				if (fieldName == "PortraitModelPath") bMeta.PortraitModelPath = newModelPath;
				else bMeta.ModelPath = newModelPath;
				GameHost.BuildingRegistry[entityId] = bMeta;
				break;
			case "resources":
				if (!GameHost.ResourceRegistry.TryGetValue(entityId, out var rMeta)) return;
				if (fieldName == "PortraitModelPath") rMeta.PortraitModelPath = newModelPath;
				else rMeta.ModelPath = newModelPath;
				GameHost.ResourceRegistry[entityId] = rMeta;
				break;
			case "props":
				if (!GameHost.PropRegistry.TryGetValue(entityId, out var pMeta)) return;
				pMeta.ModelPath = newModelPath;
				GameHost.PropRegistry[entityId] = pMeta;
				break;
		}
	}

	private void UpdateMetadataFileForModelPath(string entityId, string fieldName, string domain, string newModelPath)
	{
		string wsPath = GetWorkspacePath();
		string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
		if (!File.Exists(metadataPath)) return;

		GameContext.MetadataService.UpdateMetadata(wsPath, meta =>
		{
			switch (domain.ToLowerInvariant())
			{
				case "units":
					meta.UpdateUnit(entityId, u =>
					{
						if (fieldName == "PortraitModelPath") u.PortraitModelPath = newModelPath;
						else u.ModelPath = newModelPath;
						return u;
					});
					break;
				case "buildings":
					meta.UpdateBuilding(entityId, b =>
					{
						if (fieldName == "PortraitModelPath") b.PortraitModelPath = newModelPath;
						else b.ModelPath = newModelPath;
						return b;
					});
					break;
				case "resources":
					meta.UpdateResource(entityId, r =>
					{
						if (fieldName == "PortraitModelPath") r.PortraitModelPath = newModelPath;
						else r.ModelPath = newModelPath;
						return r;
					});
					break;
				case "props":
					meta.UpdateProp(entityId, p =>
					{
						p.ModelPath = newModelPath;
						return p;
					});
					break;
			}
		});
		this.LastMetadataSyncTime = File.Exists(metadataPath) ? File.GetLastWriteTimeUtc(metadataPath).Ticks : 0;
	}

	public bool IsMapEditorMode { get; set; }

	public bool IsTestMode { get; set; } = false;
	public bool ReturningFromTest { get; set; } = false;
	public long LastMapNameCacheTicks { get; set; } = 0;
	public string? CachedMapName { get; set; }
	public string? CachedMapVersion { get; set; }
	public string CurrentDirectoryBlake3 { get; set; } = string.Empty;
	public long LastTerrainSyncTime { get; set; } = 0;
	public long LastMetadataSyncTime { get; set; } = 0;
	public Godot.Vector3 SavedCameraPosition { get; set; }
	public float SavedTargetHeight { get; set; }
	public float SavedCurrentHeight { get; set; }
	public float SavedTargetYaw { get; set; }
	public float SavedCurrentYaw { get; set; }
	public float SavedTargetPitch { get; set; }
	public int ScaleDialogTargetWidth { get; set; }

	public bool DecreaseScaleDialogWidth()
	{
		if (ScaleDialogTargetWidth > 32)
		{
			ScaleDialogTargetWidth = Math.Max(32, (ScaleDialogTargetWidth % 32 == 0 ? ScaleDialogTargetWidth - 32 : (ScaleDialogTargetWidth / 32) * 32));
			return true;
		}

		return false;
	}

	public bool IncreaseScaleDialogWidth()
	{
		if (ScaleDialogTargetWidth < 512)
		{
			ScaleDialogTargetWidth = Math.Min(512, (ScaleDialogTargetWidth / 32 + 1) * 32);
			return true;
		}

		return false;
	}

	public int DecreaseScaleDialogDepth(int currentDepth)
	{
		if (currentDepth > 32)
		{
			return Math.Max(32, (currentDepth % 32 == 0 ? currentDepth - 32 : (currentDepth / 32) * 32));
		}

		return currentDepth;
	}

	public int IncreaseScaleDialogDepth(int currentDepth)
	{
		if (currentDepth < 512)
		{
			return Math.Min(512, (currentDepth / 32 + 1) * 32);
		}

		return currentDepth;
	}

	public void SelectCliffTexture(int index)
	{
		EditorCliffPaintTextureIndex = index;
	}

	public bool IsPolarGridActive(GridOverlayMode mode, MirrorMode mirrorMode)
	{
		return mirrorMode == MirrorMode.Rotational || mode == GridOverlayMode.Polar || mode == GridOverlayMode.Both;
	}


	public float SavedCurrentPitch { get; set; }
	public bool SavedIsTopDown { get; set; }
	public float SavedYawSwing { get; set; }
	public float SavedPitchSwing { get; set; }
	public GridOverlayMode SavedGridMode { get; set; } = GridOverlayMode.Off;

	public double AutoBackupElapsedSeconds { get; set; } = 0;

	public bool CheckAndUpdateRaycast(Godot.Vector3 pos)
	{
		if ((pos - LastRaycastPos).LengthSquared() > 0.01f)
		{
			LastRaycastPos = pos;
			return true;
		}

		return false;
	}

	public bool CheckAutoBackup(int intervalMins, double delta)
	{
		if (intervalMins <= 0) return false;
		AutoBackupElapsedSeconds += delta;
		double targetSeconds = intervalMins * 60.0;
		if (AutoBackupElapsedSeconds >= targetSeconds)
		{
			AutoBackupElapsedSeconds = 0;
			return true;
		}

		return false;
	}


	public bool TryGetValidCurrentSourceFolder(out string folder)
	{
		folder = CurrentSourceFolder;
		return !string.IsNullOrEmpty(folder) && Directory.Exists(folder);
	}

	public bool IsNestedInExistingMap(string fullSelectedPath)
	{
		var parentDir = Directory.GetParent(fullSelectedPath);
		while (parentDir != null)
		{
			if (File.Exists(Path.Combine(parentDir.FullName, "manifest.json")))
			{
				return true;
			}

			parentDir = parentDir.Parent;
		}

		return false;
	}

	public bool ContainsNestedMap(string fullSelectedPath)
	{
		if (!Directory.Exists(fullSelectedPath)) return false;
		try
		{
			foreach (string subDir in Directory.EnumerateDirectories(fullSelectedPath))
			{
				if (File.Exists(Path.Combine(subDir, "manifest.json")))
				{
					return true;
				}
			}
		}
		catch
		{
		}

		return false;
	}

	public bool IsValidMapFolder(string folder)
	{
		return File.Exists(Path.Combine(folder, "metadata.json"))
			   && File.Exists(Path.Combine(folder, "manifest.json"));
	}

	public string TempWorkspaceGodotPath { get; set; } = MapWorkspaceService.DefaultWorkspaceGodotPath;
	public string _tempWorkspacePath { get; set; } = MapWorkspaceService.GetDefaultWorkspaceGlobalPath();

	public int LastSelectionMinX { get; set; } = -1;
	public int LastSelectionMinZ { get; set; } = -1;
	public int LastSelectionMaxX { get; set; } = -1;
	public int LastSelectionMaxZ { get; set; } = -1;
	public bool LastSelectionBrushIsSquare { get; set; } = true;

	public int CurrentPasteAnchorIndex { get; set; } = 0;

	public List<string> SwatchPaths { get; set; } = new List<string>();
	public List<string> SwatchDisplayNames { get; set; } = new List<string>();
	public List<Color> SwatchColors { get; set; } = new List<Color>();

	public void ClearSwatchData()
	{
		SwatchPaths.Clear();
		SwatchDisplayNames.Clear();
		SwatchColors.Clear();
	}

	public string GetActiveWorkspacePathFromSettings()
	{
		return MapWorkspaceService.GetActiveWorkspacePath();
	}

	public void PopulateSwatchData(SwatchSlotInfo[] slots, string wsPath)
	{
		for (int i = 0; i < TextureSwatchSlots.MaxSlots; i++)
		{
			var slot = slots[i];
			if (!slot.IsFiller && !string.IsNullOrEmpty(slot.BaseName))
			{
				string resolvedPath = ResolveTexturePath(slot, wsPath);
				SwatchDisplayNames.Add(slot.BaseName);
				SwatchPaths.Add(resolvedPath);
				SwatchColors.Add(new Color(0.6f, 0.6f, 0.6f));
			}
			else
			{
				SwatchDisplayNames.Add($"Slot {i} (Empty)");
				SwatchPaths.Add("");
				SwatchColors.Add(new Color(0.2f, 0.2f, 0.2f, 0.5f));
			}
		}
	}

	private string ResolveTexturePath(SwatchSlotInfo slot, string wsPath)
	{
		string rtexName = GetRtexName(slot);

		string[] pathsToTry =
		{
			Path.Combine(wsPath, "Assets", "textures", rtexName),
			Path.Combine(wsPath, rtexName)
		};

		foreach (var path in pathsToTry)
		{
			if (File.Exists(path)) return path;
		}

		string? found = TryFindRtexPath(rtexName);
		if (!string.IsNullOrEmpty(found)) return found;

		return string.Empty;
	}

	private string GetRtexName(SwatchSlotInfo slot)
	{
		string rtexName = slot.MetadataNode?.TexturePath ?? slot.FileName ?? (slot.BaseName + ".rtex");
		if (!rtexName.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			rtexName += ".rtex";
		}

		return Path.GetFileName(rtexName);
	}

	private string? TryFindRtexPath(string rtexName)
	{
		string? found = PathUtils.FindPath($"Assets/textures/{rtexName}");
		if (!string.IsNullOrEmpty(found) && File.Exists(found)) return found;

		string? foundTemplate = PathUtils.FindPath($"MapTemplate/Assets/textures/{rtexName}");
		if (!string.IsNullOrEmpty(foundTemplate) && File.Exists(foundTemplate)) return foundTemplate;

		return null;
	}

	public string GetSwatchName(Color color)
	{
		float epsilon = 0.01f;
		for (int i = 1; i <= this.SwatchColors.Count; i++)
		{
			Color c = GetSwatchColor(i);
			if (Mathf.Abs(color.R - c.R) < epsilon &&
				Mathf.Abs(color.G - c.G) < epsilon &&
				Mathf.Abs(color.B - c.B) < epsilon)
			{
				string texName = (i >= 1 && i <= this.SwatchDisplayNames.Count) ? this.SwatchDisplayNames[i - 1] : "Unknown";
				return texName;
			}
		}

		return "Custom";
	}

	public Color GetSwatchColor(int index)
	{
		if (index >= 1 && index <= this.SwatchColors.Count)
		{
			return this.SwatchColors[index - 1];
		}

		return new Color(1, 1, 1);
	}

	public static readonly string[] PasteAnchorNames = new string[]
	{
		"CENTER",
		"TOP-LEFT",
		"TOP-RIGHT",
		"BOTTOM-RIGHT",
		"BOTTOM-LEFT"
	};

	public Rect2I? TerrainFlushRegion { get; set; }
	public float TerrainMeshRebuildPeriodMs { get; set; } = 33.3f;
	public Dictionary<string, DecalAssetData> DecalAssetCache { get; set; } = new();
	public Dictionary<(int, int), ImageTexture> DecalOrmCache { get; set; } = new();
	public Dictionary<string, ImageTexture> DecalNormalCache { get; set; } = new();
	public List<EditorCoordinate> EditorCoordinates { get; set; } = new();
	public int PendingCoordinateMinX { get; set; }
	public int PendingCoordinateMinZ { get; set; }
	public int PendingCoordinateMaxX { get; set; }
	public int PendingCoordinateMaxZ { get; set; }
	public bool ModelYOffsetSavePending { get; set; } = false;
	public bool ModelCollisionCircleSavePending { get; set; } = false;
	public readonly Dictionary<string, string> NormalizedAssetKeyCache = new(StringComparer.OrdinalIgnoreCase);


	public float EditorClumpCount { get; set; } = 5.0f;
	public float EditorClumpScale { get; set; } = 0.3f;
	public bool EditorClumpMode { get; set; } = false;
	public bool EditorRandomRotation { get; set; } = false;
	public bool EditorRandomScale { get; set; } = false;
	public float EditorExactHeight { get; set; } = 0.0f;
	public float EditorWaterHeight { get; set; } = 0.9f;
	public string EditorPreviewType { get; set; } = "";

	public float EditorPlacementRotation { get; set; } = 0.0f;
	public float EditorPlacementScale { get; set; } = 1.0f;
	public GridOverlayMode EditorGridMode { get; set; } = GridOverlayMode.Off;
	public bool EditorCameraBoundsVisible { get; set; } = false;
	public bool EditorDisableShadows { get; set; } = false;
	public int EditorPolarSpokeFolds { get; set; } = 4;
	public bool EditorPolarOverlayVisible { get; set; } = false;
	public float EditorPolarRingSpacing { get; set; } = 8.0f;
	public float EditorPolarRadialStep { get; set; } = 90.0f;
	public Godot.Vector3? EditorTapeMeasureStart { get; set; }

	public bool SavedDisableShadows { get; set; } = false;
	public string SavedEntityCategory { get; set; } = "";
	public bool IsWaterRemoveAction { get; set; } = false;
	public float SavedBrushRadius { get; set; } = 2f;
	public float SavedBrushStrength { get; set; } = 0.5f;
	public bool PlaceUnitIsEnemy { get; set; } = false;
	public float EditorBrushRadius { get; set; } = 2.0f;
	public float EditorBrushStrength { get; set; } = 3.0f;
	public int EditorPaintTextureIndex { get; set; } = 3;
	public int EditorCliffPaintTextureIndex { get; set; } = 1;

	public (string terrainName, string cliffName) GetTextureNamesDomain()
	{
		int terrainIdx = EditorPaintTextureIndex;
		int cliffIdx = EditorCliffPaintTextureIndex;

		string terrainName = (terrainIdx >= 0 && terrainIdx < SwatchDisplayNames.Count) ? SwatchDisplayNames[terrainIdx] : "Unknown";
		string cliffName = (cliffIdx >= 0 && cliffIdx < SwatchDisplayNames.Count) ? SwatchDisplayNames[cliffIdx] : "Unknown";

		return (terrainName, cliffName);
	}

	public EditorTool SavedActiveTool { get; set; } = EditorTool.Raise;
	public EditorTool ActiveEditorTool { get; set; } = EditorTool.None;
	public string SavedActivePlaceId { get; set; } = "";
	public bool SavedCameraBoundsVisible { get; set; } = false;
	public float SavedTextureIntensity { get; set; } = 10f;
	public string? LastUsedFolder { get; set; } = null;
	public string? CurrentSourceFolder { get; set; } = null;
	public string? PendingCasSourceDirectory { get; set; } = null;
	public string? PendingDefaultSaveFolder { get; set; } = null;

	public const float MIN_BRUSH_RADIUS = 1.0f;
	public const float MAX_BRUSH_RADIUS = 20.0f;
	public const float MIN_BRUSH_STRENGTH = 0.0f;
	public const float MAX_BRUSH_STRENGTH = 10.0f;
	public const float MIN_PLACEMENT_SCALE = 0.25f;
	public const float MAX_PLACEMENT_SCALE = 5.0f;

	public bool _lastBrushShapeIsSquare { get; set; }
	public bool _hasLastBrushShape { get; set; }

	public bool TrySetBrushShapeExternal(bool isSquare)
	{
		if (_hasLastBrushShape && _lastBrushShapeIsSquare == isSquare) return false;
		_hasLastBrushShape = true;
		_lastBrushShapeIsSquare = isSquare;
		return true;
	}

	public float _lastRotationExternal { get; set; } = float.NaN;
	public float _lastPasteRotationExternal { get; set; } = float.NaN;
	public PasteReflection _lastPasteReflectionExternal { get; set; } = (PasteReflection)(-1);
	public float _lastScaleExternal { get; set; } = float.NaN;
	public float _lastBrushSizeExternal { get; set; } = float.NaN;

	public void UpdatePasteReflectionExternalDomain(PasteReflection reflection)
	{
		if (_lastPasteReflectionExternal == reflection) return;
		_lastPasteReflectionExternal = reflection;
	}

	public int GetPasteAnchorZ(int index, int depth)
	{
		return index switch
		{
			0 => depth / 2,
			1 => 0,
			2 => 0,
			3 => Math.Max(0, depth - 1),
			4 => Math.Max(0, depth - 1),
			_ => depth / 2
		};
	}

	public void CyclePasteAnchorDomain()
	{
		if (!HasCopiedArea) return;
		CurrentPasteAnchorIndex = (CurrentPasteAnchorIndex + 1) % PasteAnchorNames.Length;
		int w = CopiedAreaWidth;
		int d = CopiedAreaDepth;
		int ax = GetPasteAnchorX(CurrentPasteAnchorIndex, w);
		int az = GetPasteAnchorZ(CurrentPasteAnchorIndex, d);
		SetCopiedAreaAnchor(ax, az);
	}

	public bool SetLastRotationExternal(float angle)
	{
		if (Mathf.IsEqualApprox(_lastRotationExternal, angle)) return false;
		_lastRotationExternal = angle;
		return true;
	}

	public bool SetLastPasteRotationExternal(float angle)
	{
		if (Mathf.IsEqualApprox(_lastPasteRotationExternal, angle)) return false;
		_lastPasteRotationExternal = angle;
		return true;
	}

	public bool SetLastScaleExternal(float scale)
	{
		if (Mathf.IsEqualApprox(_lastScaleExternal, scale)) return false;
		_lastScaleExternal = scale;
		return true;
	}

	public bool SetLastBrushSizeExternal(float size)
	{
		if (Mathf.IsEqualApprox(_lastBrushSizeExternal, size)) return false;
		_lastBrushSizeExternal = size;
		return true;
	}

	public static readonly Dictionary<EditorTool, string> ToolInfoTexts = new()
	{
		{ EditorTool.Raise, "TOOL: Raise Heights\n\nDrag left click on the map ground to elevate terrain. Adjust size and strength in settings." },
		{ EditorTool.Ramp, "TOOL: Ramping\n\nLeft-click once on the terrain to set the Ramp Start Point. Left-click again to set the Ramp End Point. The tool will smoothly interpolate heights between the two points. Press Right-click or Escape to cancel." },
		{ EditorTool.Lower, "TOOL: Lower Heights\n\nDrag left click on the map ground to depress terrain. Adjust size and strength in settings." },
		{ EditorTool.Height, "TOOL: Exact Height\n\nDrag left click on the map ground to set terrain to exact block height. Adjust height in settings." },
		{ EditorTool.Plateau, "TOOL: Plateau\n\nDrag left click to flatten terrain to the elevation of your initial click point." },
		{ EditorTool.Smooth, "TOOL: Smooth Terrain\n\nDrag left click to average neighbor vertex heights and smooth out rugged elevations." },
		{ EditorTool.PaintTexture, "TOOL: Texture Painting\n\nDrag left click to paint texture layers onto the vertices of the terrain mesh." },
		{ EditorTool.FloodFill, "TOOL: Flood Fill\n\nClick once on the terrain map to flood-fill an area sharing the same texture color until hitting a boundary (cliff or different texture). Uses selected texture swatch." },
		{ EditorTool.SelectArea, "TOOL: Area Select\n\nDrag left click to select a rectangular area of the map. Press Ctrl+C to copy the area." },
		{ EditorTool.PasteArea, "TOOL: Area Paste\n\nClick on the terrain to paste the copied area. Use the Affected Layers checkboxes to filter what is pasted (Textures, HeightMap, Units / Props, Pathing)." },
		{ EditorTool.PlaceDecal, "TOOL: Place Decal\n\nLeft-click on the ground to project a decorative decal. Snapping, scaling, and rotation apply." },
		{ EditorTool.DeleteObject, "TOOL: Object Eraser\n\nLeft-click directly on any unit or prop in 3D scene to erase and remove it from the map." },
		{ EditorTool.SelectMove, "TOOL: Select / Move\n\nLeft-click directly on any unit, prop, or decal to select it. Hold and drag left click to move it. Use Alt + MouseWheel to scale, or Delete to delete." },
		{ EditorTool.Eyedropper, "TOOL: Eyedropper / Picker\n\nLeft-click directly on any unit, prop, or decal to copy and select it as the active placement tool. Click on terrain to copy its texture color, or hold Shift to copy its height." },
		{ EditorTool.Noise, "TOOL: Roughen Terrain\n\nDrag left-click to apply random height variations/noise to ruggedize the terrain surface. Adjust size and strength in settings." },
		{ EditorTool.Water, "TOOL: Water Flood Fill\n\nClick on terrain to flood-fill water bounded by cliff walls. Uses selected Water Profile or toggles water." },
		{ EditorTool.PaintPathing, "TOOL: Pathing Layer Painting\n\nDrag left click to paint pathing properties (ground, flying, water, etc.) onto the map. Use checkboxes to select layers, and Mode to Add/Remove." },
		{ EditorTool.FloodFillPathing, "TOOL: Flood Fill Pathing\n\nClick once on the terrain map to flood-fill pathing properties (ground, flying, water, etc.) across an area sharing the same texture color until hitting a boundary. Use checkboxes to select layers, and Mode to Add/Remove." },
		{ EditorTool.Measure, "TOOL: Tape Measure\n\nClick on the terrain to set measurement origin. Move the cursor or click again to lock the endpoint. Displays live Euclidean distance, Manhattan distance, grid delta, and slope." },
		{ EditorTool.None, "Select a tool from the panels to begin terrain modification." }
	};

	public static readonly int[] SpokeCountOptions = new[] { 2, 3, 4, 5, 6, 8, 12, 16 };

	public const float MIN_CLUMP_COUNT = 1.0f;
	public const float MAX_CLUMP_COUNT = 20.0f;
	public const float MIN_CLUMP_SCALE = 0.0f;
	public const float MAX_CLUMP_SCALE = 1.0f;
	public const float MAX_CLUMP_DENSITY = MAX_CLUMP_COUNT;


	private TerrainSplatWeights[,] _terrainSplatMap;
	private TerrainSplatWeights[,] _terrainCliffSplatMap;

	private float _clumpSpawnCooldown;
	private bool _isDrawingClump;
	private readonly List<IEditorAction> _clumpSpawnActionsInSession = new();
	private readonly List<(Godot.Vector3 Position, float Radius)> _cachedSessionPoints = new();

	private readonly Dictionary<Vector2I, long> _paintCoordLastTimeMs = new();

	public void ResetPaintThrottle()
	{
		_paintCoordLastTimeMs.Clear();
	}

	private bool ShouldPaintCoord(int x, int z, long nowMs)
	{
		Vector2I coord = new Vector2I(x, z);
		if (_paintCoordLastTimeMs.TryGetValue(coord, out long lastTime))
		{
			if (nowMs - lastTime < 250)
			{
				return false;
			}
		}

		_paintCoordLastTimeMs[coord] = nowMs;
		return true;
	}

	private bool _hasBlockTargetHeight;
	private float _activeBlockTargetHeight;
	private WaterType _activeBlockTargetWaterMode = WaterType.None;
	private byte _activeBlockTargetWaterProfile = 0;
	private float _activeBlockTargetWaterHeight = 0f;
	private float? _activePlateauHeight;
	private WaterType _activePlateauWaterMode = WaterType.None;
	private byte _activePlateauWaterProfile = 0;
	private float _activePlateauWaterHeight = 0f;

	private TerrainCell[,] _terrainCellsBefore;
	private TerrainSplatWeights[,] _terrainSplatMapBefore;
	private TerrainSplatWeights[,] _terrainCliffSplatMapBefore;
	private int[,] _terrainPathingBefore;
	private bool _isDrawingTerrain;
	private int _drawMinX;
	private int _drawMinZ;
	private int _drawMaxX;
	private int _drawMaxZ;

	private CopiedAreaTemplate? _copiedArea;

	private Vector2I? _selectionStart;
	private Vector2I? _selectionEnd;
	private bool _isSelectingArea;

	private float _cachedRandomRotation;
	private float _cachedRandomScale = 1.0f;
	private bool _hasCachedRandom;
	private bool _isPastingObject;

	private CopiedObjectTemplate? _copiedObject;
	private Godot.Vector3? _rampStartPos;


	public bool PasteOptionTextures { get; set; } = true;
	public bool PasteOptionHeights { get; set; } = true;
	public Godot.Vector3 DragObjectStartHitPos { get; set; }
	public Vector2 DragStartMousePos { get; set; }
	public Godot.Vector3 DragStartGroundPos { get; set; }
	public bool DragObjectHasMoved { get; set; }

	public bool IsSwatchCompatibleToolDomain(EditorTool tool) => tool switch
	{
		EditorTool.Raise => true,
		EditorTool.Lower => true,
		EditorTool.Height => true,
		EditorTool.Smooth => true,
		EditorTool.Plateau => true,
		EditorTool.Noise => true,
		EditorTool.Ramp => true,
		EditorTool.Water => true,
		EditorTool.PaintTexture => true,
		EditorTool.FloodFill => true,
		EditorTool.Eyedropper => true,
		EditorTool.SelectArea => true,
		_ => false
	};

	public EditorService()
	{
	}

	public void SetTerrainSplatMap(TerrainSplatWeights[,] splatMap, TerrainSplatWeights[,] cliffSplatMap = null)
	{
		_terrainSplatMap = splatMap;
		if (cliffSplatMap != null)
		{
			_terrainCliffSplatMap = cliffSplatMap;
		}
		else if (GameHost.Instance?.GroundTerrain?.CliffSplatMap != null)
		{
			_terrainCliffSplatMap = GameHost.Instance.GroundTerrain.CliffSplatMap;
		}

		if (splatMap != null)
		{
			AlignSplatMapSlots(0, 0, splatMap.GetLength(0) - 1, splatMap.GetLength(1) - 1);
		}
	}

	public bool IsPastingObject => _isPastingObject;
	public bool HasCachedRandom => _hasCachedRandom;
	public float CachedRandomRotation => _cachedRandomRotation;
	public float CachedRandomScale => _cachedRandomScale;
	public Vector2I? SelectionStart => _selectionStart;
	public Vector2I? SelectionEnd => _selectionEnd;
	public bool IsSelectingArea => _isSelectingArea;

	public CopiedAreaTemplate CopiedArea
	{
		get => _copiedArea;
		set => _copiedArea = value;
	}

	public bool HasCopiedArea => _copiedArea != null;
	public int CopiedAreaWidth => _copiedArea?.Width ?? 0;
	public int CopiedAreaDepth => _copiedArea?.Depth ?? 0;
	public int CopiedAreaAnchorX => _copiedArea?.AnchorTileX ?? 0;
	public int CopiedAreaAnchorZ => _copiedArea?.AnchorTileZ ?? 0;
	public int CopiedAreaSourceMinX => _copiedArea?.SourceMinX ?? 0;
	public int CopiedAreaSourceMinZ => _copiedArea?.SourceMinZ ?? 0;
	public int CopiedAreaSourceMaxX => _copiedArea?.SourceMaxX ?? 0;
	public int CopiedAreaSourceMaxZ => _copiedArea?.SourceMaxZ ?? 0;
	public bool HasCopiedAreaMask => _copiedArea?.Mask != null;
	public bool IsCopiedCellMasked(int srcX, int srcZ) => _copiedArea == null || _copiedArea.Mask == null || (srcX >= 0 && srcX < _copiedArea.Width && srcZ >= 0 && srcZ < _copiedArea.Depth && _copiedArea.Mask[srcX, srcZ]);


	public int GetPasteAnchorX(int index, int width)
	{
		return index switch
		{
			0 => width / 2,
			1 => 0,
			2 => Math.Max(0, width - 1),
			3 => Math.Max(0, width - 1),
			4 => 0,
			_ => width / 2
		};
	}

	public void SetCopiedAreaAnchor(int anchorTileX, int anchorTileZ)
	{
		if (_copiedArea != null)
		{
			_copiedArea.AnchorTileX = Mathf.Clamp(anchorTileX, 0, Math.Max(0, _copiedArea.Width - 1));
			_copiedArea.AnchorTileZ = Mathf.Clamp(anchorTileZ, 0, Math.Max(0, _copiedArea.Depth - 1));
		}
	}

	private Vector2 _symmetryPivot = Vector2.Zero;
	private int _symmetryFolds = 4;

	public Vector2 SymmetryPivot
	{
		get => _symmetryPivot;
		set => _symmetryPivot = value;
	}

	public int SymmetryFolds
	{
		get => _symmetryFolds;
		set => _symmetryFolds = Math.Clamp(value, 2, 32);
	}

	public Godot.Vector3? RampStartPos => _rampStartPos;
	public bool IsDrawingTerrain => _isDrawingTerrain;
	public bool IsDrawingClump => _isDrawingClump;

	public CopiedObjectTemplate? GetCopiedObject() => _copiedObject;

	public void SetCopiedObject(CopiedObjectTemplate? template)
	{
		_copiedObject = template;
	}

	public void CopyEditorPropDomain(string propId, float rotation, float scale)
	{
		SetCopiedObject(new CopiedObjectTemplate
		{
			Type = "prop",
			Id = propId,
			Rotation = rotation,
			Scale = scale,
			IsEnemy = false
		});
	}

	public void CopyEditorDecalDomain(string decalId, float rotation, float scale)
	{
		SetCopiedObject(new CopiedObjectTemplate
		{
			Type = "decal",
			Id = decalId,
			Rotation = rotation,
			Scale = scale,
			IsEnemy = false
		});
	}

	public void ApplyCopiedObjectStateDomain(CopiedObjectTemplate copiedObj, out EditorTool newTool)
	{
		SetIsPastingObject(true);
		EditorPlacementRotation = copiedObj.Rotation;

		newTool = EditorTool.None;
		switch (copiedObj.Type)
		{
			case "unit":
				newTool = EditorTool.PlaceUnit;
				PlaceUnitIsEnemy = copiedObj.IsEnemy;
				break;
			case "prop":
				newTool = EditorTool.PlaceProp;
				break;
			case "decal":
				newTool = EditorTool.PlaceDecal;
				break;
		}
	}

	public void SetIsPastingObject(bool value)
	{
		_isPastingObject = value;
	}

	public void SetRampStartPos(Godot.Vector3? pos)
	{
		_rampStartPos = pos;
	}

	public void SetSelectionStart(Vector2I? value)
	{
		_selectionStart = value;
	}

	public void SetSelectionEnd(Vector2I? value)
	{
		_selectionEnd = value;
	}

	public void SetIsSelectingArea(bool value)
	{
		_isSelectingArea = value;
	}

	public void GenerateNewRandomPlacementRotationAndScale()
	{
		_cachedRandomRotation = (float)(GD.Randf() * 360.0);
		_cachedRandomScale = 0.2f + (float)(GD.Randf() * 2.8);
		_hasCachedRandom = true;
	}

	public void InvalidateCachedRandom()
	{
		_hasCachedRandom = false;
	}

	private void SetGridNodeHeight(ref TerrainState terrain, int gx, int gz, float height)
	{
		var cells = terrain.Cells;
		if (cells == null) return;
		int w = terrain.Width;
		int d = terrain.Depth;
		if (gx > 0 && gz > 0 && gx - 1 < w && gz - 1 < d)
		{
			ref var c = ref cells[gx - 1, gz - 1];
			c.Y_SE = height;
		}

		if (gx < w && gz > 0 && gz - 1 < d)
		{
			ref var c = ref cells[gx, gz - 1];
			c.Y_SW = height;
		}

		if (gx > 0 && gz < d && gx - 1 < w)
		{
			ref var c = ref cells[gx - 1, gz];
			c.Y_NE = height;
		}

		if (gx < w && gz < d)
		{
			ref var c = ref cells[gx, gz];
			c.Y_NW = height;
		}
	}

	private float GetGridNodeHeight(in TerrainState terrain, int gx, int gz)
	{
		var cells = terrain.Cells;
		if (cells == null) return 0f;
		int w = terrain.Width;
		int d = terrain.Depth;
		int cellX = Math.Clamp(gx, 0, w - 1);
		int cellZ = Math.Clamp(gz, 0, d - 1);
		if (gx < w && gz < d) return cells[cellX, cellZ].Y_NW;
		if (gx == w && gz < d) return cells[w - 1, cellZ].Y_NE;
		if (gx < w && gz == d) return cells[cellX, d - 1].Y_SW;
		return cells[w - 1, d - 1].Y_SE;
	}

	public float GetTerrainHeightAt(Godot.Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		var cells = terrain.Cells;
		if (cells == null) return 0.0f;

		int cellW = cells.GetLength(0);
		int cellD = cells.GetLength(1);
		if (cellW <= 0 || cellD <= 0) return 0.0f;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		float fx = worldPos.X / quadSize + width / 2.0f;
		float fz = worldPos.Z / quadSize + depth / 2.0f;

		int x = Math.Clamp((int)Math.Floor(fx), 0, cellW - 1);
		int z = Math.Clamp((int)Math.Floor(fz), 0, cellD - 1);
		float tx = Math.Clamp(fx - x, 0f, 1f);
		float tz = Math.Clamp(fz - z, 0f, 1f);

		var cell = cells[x, z];
		float hNW = cell.Y_NW;
		float hNE = cell.Y_NE;
		float hSW = cell.Y_SW;
		float hSE = cell.Y_SE;

		return (1 - tx) * (1 - tz) * hNW + tx * (1 - tz) * hNE + (1 - tx) * tz * hSW + tx * tz * hSE;
	}

	public WaterType GetWaterModeAt(Godot.Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		var cells = terrain.Cells;
		if (cells == null) return WaterType.None;

		int cellW = cells.GetLength(0);
		int cellD = cells.GetLength(1);
		if (cellW <= 0 || cellD <= 0) return WaterType.None;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		int x = Math.Clamp((int)Math.Floor(worldPos.X / quadSize + width / 2.0f), 0, cellW - 1);
		int z = Math.Clamp((int)Math.Floor(worldPos.Z / quadSize + depth / 2.0f), 0, cellD - 1);

		return cells[x, z].WaterMode;
	}

	public byte GetWaterProfileIndexAt(Godot.Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		var cells = terrain.Cells;
		if (cells == null) return 0;

		int cellW = cells.GetLength(0);
		int cellD = cells.GetLength(1);
		if (cellW <= 0 || cellD <= 0) return 0;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		int x = Math.Clamp((int)Math.Floor(worldPos.X / quadSize + width / 2.0f), 0, cellW - 1);
		int z = Math.Clamp((int)Math.Floor(worldPos.Z / quadSize + depth / 2.0f), 0, cellD - 1);

		return cells[x, z].WaterProfileIndex;
	}

	public float GetWaterHeightAt(Godot.Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		var cells = terrain.Cells;
		if (cells == null) return 0f;

		int cellW = cells.GetLength(0);
		int cellD = cells.GetLength(1);
		if (cellW <= 0 || cellD <= 0) return 0f;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		int x = Math.Clamp((int)Math.Floor(worldPos.X / quadSize + width / 2.0f), 0, cellW - 1);
		int z = Math.Clamp((int)Math.Floor(worldPos.Z / quadSize + depth / 2.0f), 0, cellD - 1);

		return cells[x, z].WaterHeight;
	}

	public TerrainEditResult ApplyContinuousTerrainEditing(
		Godot.Vector3 worldPos, float delta, EditorTool activeTool,
		float brushRadius, float brushStrength, bool brushIsSquare, bool blockMode,
		float blockLevelHeight, int paintTextureIndex, int cliffPaintTextureIndex,
		int pathingMask, bool pathingAdd, bool isFirstClick = false,
		bool applyGroundTexture = true, bool applyCliffTexture = true)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null || !EnsureSplatMapsInitialized()) return default;

		bool isHeights = IsHeightTool(activeTool);
		bool isPaint = activeTool == EditorTool.PaintTexture;
		bool isPathing = activeTool == EditorTool.PaintPathing;

		if (!isHeights && !isPaint && !isPathing) return default;
		if (isFirstClick) ResetPaintThrottle();

		var ctx = new ContinuousEditContext
		{
			WorldPos = worldPos,
			Delta = delta,
			ActiveTool = activeTool,
			BrushRadius = brushRadius,
			BrushStrength = brushStrength,
			BrushIsSquare = brushIsSquare,
			PaintTextureIndex = paintTextureIndex,
			CliffPaintTextureIndex = cliffPaintTextureIndex,
			PathingMask = pathingMask,
			PathingAdd = pathingAdd,
			ApplyGroundTexture = applyGroundTexture,
			ApplyCliffTexture = applyCliffTexture,
			Width = terrain.Width,
			Depth = terrain.Depth,
			QuadSize = terrain.QuadSize,
			ModMinX = terrain.Width,
			ModMinZ = terrain.Depth,
			ModMaxX = -1,
			ModMaxZ = -1,
			NowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
			IsHeights = isHeights
		};

		bool isBlock = (blockMode || activeTool == EditorTool.Height) && activeTool != EditorTool.Noise && activeTool != EditorTool.Smooth;

		if (isBlock)
		{
			ApplyBlockModeEditing(ref terrain, ref ctx, blockLevelHeight);
		}
		else
		{
			ApplyContinuousModeEditing(ref terrain, ref ctx);
		}

		return FinalizeTerrainEditResult(ctx, isHeights, isPaint, isPathing);
	}


	private bool IsHeightTool(EditorTool tool)
	{
		return tool == EditorTool.Raise || tool == EditorTool.Lower ||
			   tool == EditorTool.Height || tool == EditorTool.Smooth ||
			   tool == EditorTool.Plateau || tool == EditorTool.Noise;
	}


	private void ApplyBlockModeEditing(ref TerrainState terrain, ref ContinuousEditContext ctx, float blockLevelHeight)
	{
		if (ctx.ActiveTool == EditorTool.PaintTexture)
		{
			ProcessBlockPaint(ref terrain, ref ctx, -1, -1); // Let ProcessBlockPaint handle its own bounds
			return;
		}

		int cx = Mathf.Clamp((int)Math.Floor(ctx.WorldPos.X / ctx.QuadSize + ctx.Width / 2.0f), 0, ctx.Width - 1);
		int cz = Mathf.Clamp((int)Math.Floor(ctx.WorldPos.Z / ctx.QuadSize + ctx.Depth / 2.0f), 0, ctx.Depth - 1);
		int N = Mathf.Max(1, (int)Mathf.Round(ctx.BrushRadius));
		int quadMinX = cx - (N - 1) / 2;
		int quadMaxX = cx + N / 2;
		int quadMinZ = cz - (N - 1) / 2;
		int quadMaxZ = cz + N / 2;

		if (ctx.IsHeights) InitializeBlockHeights(ctx.ActiveTool, ctx.WorldPos, blockLevelHeight);

		float targetHeight = Math.Clamp(_activeBlockTargetHeight, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
		sbyte targetMacroTier = (sbyte)Math.Clamp((int)MathF.Round(targetHeight / TerrainCell.TIER_HEIGHT), TerrainCell.MIN_MACRO_TIER, TerrainCell.MAX_MACRO_TIER);

		for (int z = quadMinZ; z <= quadMaxZ; z++)
		{
			for (int x = quadMinX; x <= quadMaxX; x++)
			{
				if (x >= 0 && x < ctx.Width && z >= 0 && z < ctx.Depth)
				{
					if (IsPointInBrush(x, z, quadMinX, quadMinZ, N, ctx.BrushIsSquare))
					{
						ProcessBlockCell(ref terrain, ref ctx, x, z, targetHeight, targetMacroTier);
					}
				}
			}
		}
	}

	private void InitializeBlockHeights(EditorTool tool, Godot.Vector3 worldPos, float blockLevelHeight)
	{
		if (_hasBlockTargetHeight) return;
		float startHeight = GetTerrainHeightAt(worldPos);
		if (tool == EditorTool.Height) _activeBlockTargetHeight = blockLevelHeight;
		else if (tool == EditorTool.Raise) _activeBlockTargetHeight = startHeight + blockLevelHeight;
		else if (tool == EditorTool.Lower) _activeBlockTargetHeight = startHeight - blockLevelHeight;
		else if (tool == EditorTool.Plateau)
		{
			_activeBlockTargetHeight = (float)MathF.Round(startHeight / TerrainCell.TIER_HEIGHT) * TerrainCell.TIER_HEIGHT;
			_activeBlockTargetWaterMode = GetWaterModeAt(worldPos);
			_activeBlockTargetWaterProfile = GetWaterProfileIndexAt(worldPos);
			_activeBlockTargetWaterHeight = GetWaterHeightAt(worldPos);
		}

		_activeBlockTargetHeight = Math.Clamp(_activeBlockTargetHeight, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
		_hasBlockTargetHeight = true;
	}

	private bool IsPointInBrush(int x, int z, int quadMinX, int quadMinZ, int N, bool brushIsSquare)
	{
		if (brushIsSquare) return true;
		float dx = (x + 0.5f) - (quadMinX + N / 2.0f);
		float dz = (z + 0.5f) - (quadMinZ + N / 2.0f);
		return (dx * dx + dz * dz) <= (N / 2.0f * N / 2.0f);
	}

	private void ProcessBlockCell(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float targetHeight, sbyte targetMacroTier)
	{
		if (ctx.IsHeights) ProcessBlockHeights(ref terrain, ref ctx, x, z, targetHeight, targetMacroTier);
		else if (ctx.ActiveTool == EditorTool.PaintPathing) ProcessBlockPathing(ref terrain, ref ctx, x, z);
	}

	private void ProcessBlockHeights(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float targetHeight, sbyte targetMacroTier)
	{
		if (ctx.ActiveTool == EditorTool.Smooth)
		{
			ApplyBlockSmooth(ref terrain, ref ctx, x, z);
			return;
		}

		ref var cell = ref terrain.Cells![x, z];
		bool waterChanged = UpdateCellWater(ref cell, ctx.ActiveTool);

		if (MathF.Abs(cell.CenterHeight - targetHeight) > 0.001f || waterChanged)
		{
			UpdateBlockCellHeightAndPathing(ref terrain, ref ctx, x, z, targetHeight, targetMacroTier, ref cell);
			ApplyBlockTextures(ref terrain, ref ctx, x, z, targetHeight);
			UpdateContextBounds(ref ctx, x, z);
		}
	}

	private bool UpdateCellWater(ref TerrainCell cell, EditorTool tool)
	{
		if (tool == EditorTool.Raise || tool == EditorTool.Height)
		{
			if (cell.WaterMode == WaterType.None) return false;
			cell.WaterMode = WaterType.None;
			cell.WaterHeight = 0f;
			return true;
		}

		if (tool == EditorTool.Plateau)
		{
			if (cell.WaterMode == _activeBlockTargetWaterMode && cell.WaterProfileIndex == _activeBlockTargetWaterProfile && cell.WaterHeight == _activeBlockTargetWaterHeight) return false;
			cell.WaterMode = _activeBlockTargetWaterMode;
			cell.WaterProfileIndex = _activeBlockTargetWaterProfile;
			cell.WaterHeight = _activeBlockTargetWaterHeight;
			return true;
		}

		return false;
	}

	private void UpdateBlockCellHeightAndPathing(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float targetHeight, sbyte targetMacroTier, ref TerrainCell cell)
	{
		if (terrain.PathingCodes != null)
		{
			if (cell.WaterMode != WaterType.None)
			{
				var waterProf = RuntimeTerrain.Instance?.GetWaterProfile(cell.WaterProfileIndex);
				terrain.PathingCodes[x, z] = waterProf != null ? waterProf.DefaultPathingCode : EditableTerrain.GetDefaultPathingCode(cell.WaterMode);
				ctx.PathingModified = true;
				float wY = (targetMacroTier * TerrainCell.TIER_HEIGHT) + (cell.WaterHeight > 0.001f ? cell.WaterHeight : RuntimeTerrain.WATER_DELTA);
				SpawnWaterProceduralBombing(x, z, cell.WaterProfileIndex, wY, ctx.QuadSize, ctx.Width, ctx.Depth);
			}
			else
			{
				ClearWaterProceduralObjects(x, z);
				if (terrain.PathingCodes[x, z] == EditableTerrain.GetDefaultPathingCode(cell))
				{
					terrain.PathingCodes[x, z] = EditableTerrain.GetDefaultPathingCode(cell);
					ctx.PathingModified = true;
				}
			}
		}

		SetGridNodeHeight(ref terrain, x, z, targetHeight);
		SetGridNodeHeight(ref terrain, x + 1, z, targetHeight);
		SetGridNodeHeight(ref terrain, x + 1, z + 1, targetHeight);
		SetGridNodeHeight(ref terrain, x, z + 1, targetHeight);
	}

	private void ApplyBlockTextures(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float targetHeight)
	{
		if (ctx.ApplyGroundTexture && _terrainSplatMap != null)
		{
			for (int gz = z; gz <= z + 1; gz++)
			for (int gx = x; gx <= x + 1; gx++)
				if (gx >= 0 && gx < _terrainSplatMap.GetLength(0) && gz >= 0 && gz < _terrainSplatMap.GetLength(1))
					_terrainSplatMap[gx, gz] = TerrainSplatWeights.CreateSolid(ctx.PaintTextureIndex);
			UpdatePathingFromMetadata(ref terrain, ref ctx, x, z);
			SpawnTerrainProceduralBombing(x, z, ctx.PaintTextureIndex, targetHeight, ctx.QuadSize, ctx.Width, ctx.Depth);
		}

		if (ctx.ApplyCliffTexture && _terrainCliffSplatMap != null)
		{
			for (int nz = z - 1; nz <= z + 2; nz++)
			for (int nx = x - 1; nx <= x + 2; nx++)
				if (nx >= 0 && nx < _terrainCliffSplatMap.GetLength(0) && nz >= 0 && nz < _terrainCliffSplatMap.GetLength(1))
					_terrainCliffSplatMap[nx, nz] = TerrainSplatWeights.CreateSolid(ctx.CliffPaintTextureIndex);
		}
	}

	private void ApplyBlockSmooth(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z)
	{
		int sum = 0, count = 0;
		for (int nz = -1; nz <= 1; nz++)
		{
			for (int nx = -1; nx <= 1; nx++)
			{
				if (x + nx >= 0 && x + nx < ctx.Width && z + nz >= 0 && z + nz < ctx.Depth)
				{
					sum += terrain.Cells![x + nx, z + nz].MacroTier;
					count++;
				}
			}
		}

		sbyte avgMacro = (sbyte)Math.Clamp((int)MathF.Round((float)sum / count), -16, 16);
		float targetH = avgMacro * TerrainCell.TIER_HEIGHT;
		if (MathF.Abs(terrain.Cells![x, z].CenterHeight - targetH) > 0.001f)
		{
			SetGridNodeHeight(ref terrain, x, z, targetH);
			SetGridNodeHeight(ref terrain, x + 1, z, targetH);
			SetGridNodeHeight(ref terrain, x + 1, z + 1, targetH);
			SetGridNodeHeight(ref terrain, x, z + 1, targetH);
			UpdateContextBounds(ref ctx, x, z);
		}
	}

	private void ProcessBlockPaint(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z)
	{
		int splatW = _terrainSplatMap!.GetLength(0);
		int splatD = _terrainSplatMap.GetLength(1);
		int intensityLevel = Math.Clamp((int)MathF.Round(ctx.BrushStrength), 0, 10);

		int minPaintX = Math.Clamp((int)Math.Floor((ctx.WorldPos.X - ctx.BrushRadius) / ctx.QuadSize + ctx.Width / 2.0f), 0, splatW - 1);
		int maxPaintX = Math.Clamp((int)Math.Ceiling((ctx.WorldPos.X + ctx.BrushRadius) / ctx.QuadSize + ctx.Width / 2.0f), 0, splatW - 1);
		int minPaintZ = Math.Clamp((int)Math.Floor((ctx.WorldPos.Z - ctx.BrushRadius) / ctx.QuadSize + ctx.Depth / 2.0f), 0, splatD - 1);
		int maxPaintZ = Math.Clamp((int)Math.Ceiling((ctx.WorldPos.Z + ctx.BrushRadius) / ctx.QuadSize + ctx.Depth / 2.0f), 0, splatD - 1);

		for (int pz = minPaintZ; pz <= maxPaintZ; pz++)
		{
			for (int px = minPaintX; px <= maxPaintX; px++)
			{
				if (IsPaintPointInBrush(px, pz, ref ctx) && ShouldPaintCoord(px, pz, ctx.NowMs))
				{
					ApplyPaintToCell(ref terrain, ref ctx, px, pz, intensityLevel);
				}
			}
		}
	}

	private bool IsPaintPointInBrush(int px, int pz, ref ContinuousEditContext ctx)
	{
		float vx = (px - ctx.Width / 2.0f) * ctx.QuadSize;
		float vz = (pz - ctx.Depth / 2.0f) * ctx.QuadSize;
		if (ctx.BrushIsSquare) return Mathf.Abs(vx - ctx.WorldPos.X) <= ctx.BrushRadius && Mathf.Abs(vz - ctx.WorldPos.Z) <= ctx.BrushRadius;
		return new Vector2(vx - ctx.WorldPos.X, vz - ctx.WorldPos.Z).Length() <= ctx.BrushRadius;
	}

	private void ApplyPaintToCell(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, int intensityLevel)
	{
		if (ctx.ApplyGroundTexture)
		{
			_terrainSplatMap![x, z] = TerrainSplatWeights.PaintVertexWeighted(_terrainSplatMap[x, z], ctx.PaintTextureIndex, intensityLevel);
			UpdatePathingFromMetadata(ref terrain, ref ctx, x, z);
			if (terrain.Cells != null && x < ctx.Width && z < ctx.Depth) SpawnTerrainProceduralBombing(x, z, ctx.PaintTextureIndex, terrain.Cells[x, z].CenterHeight, ctx.QuadSize, ctx.Width, ctx.Depth);
		}

		if (ctx.ApplyCliffTexture && _terrainCliffSplatMap != null && x < _terrainCliffSplatMap.GetLength(0) && z < _terrainCliffSplatMap.GetLength(1))
		{
			_terrainCliffSplatMap[x, z] = TerrainSplatWeights.PaintVertexWeighted(_terrainCliffSplatMap[x, z], ctx.CliffPaintTextureIndex, intensityLevel);
		}

		UpdateContextBoundsTight(ref ctx, x, z);
	}

	private void UpdatePathingFromMetadata(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z)
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		if (!string.IsNullOrEmpty(wsPath) && GameContext.MetadataService.TryLoadMetadata(wsPath, out var metaRoot) && metaRoot != null)
		{
			string? swatchName = null;
			var list = GameHost.Instance?.GroundTerrain?.LoadedTextureList;
			if (list != null && ctx.PaintTextureIndex >= 0 && ctx.PaintTextureIndex < list.Count)
			{
				swatchName = list[ctx.PaintTextureIndex];
			}

			if (!string.IsNullOrEmpty(swatchName) && terrain.PathingCodes != null && x < ctx.Width && z < ctx.Depth)
			{
				var prof = metaRoot.GetTerrainProfile(swatchName);
				if (prof != null)
				{
					terrain.PathingCodes[x, z] = prof.DefaultPathingCode;
					ctx.PathingModified = true;
				}
			}
		}
	}

	private void ProcessBlockPathing(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z)
	{
		if (ctx.PathingAdd) terrain.PathingCodes![x, z] |= ctx.PathingMask;
		else terrain.PathingCodes![x, z] &= ~ctx.PathingMask;
		UpdateContextBoundsTight(ref ctx, x, z);
	}

	private void ApplyContinuousModeEditing(ref TerrainState terrain, ref ContinuousEditContext ctx)
	{
		int minGX = Math.Clamp((int)Math.Floor((ctx.WorldPos.X - ctx.BrushRadius) / ctx.QuadSize + ctx.Width / 2.0f), 0, ctx.Width);
		int maxGX = Math.Clamp((int)Math.Ceiling((ctx.WorldPos.X + ctx.BrushRadius) / ctx.QuadSize + ctx.Width / 2.0f), 0, ctx.Width);
		int minGZ = Math.Clamp((int)Math.Floor((ctx.WorldPos.Z - ctx.BrushRadius) / ctx.QuadSize + ctx.Depth / 2.0f), 0, ctx.Depth);
		int maxGZ = Math.Clamp((int)Math.Ceiling((ctx.WorldPos.Z + ctx.BrushRadius) / ctx.QuadSize + ctx.Depth / 2.0f), 0, ctx.Depth);

		for (int z = minGZ; z <= maxGZ; z++)
		{
			for (int x = minGX; x <= maxGX; x++)
			{
				float dist = CalculateBrushDistance(x, z, ref ctx);
				if (dist <= ctx.BrushRadius)
				{
					ProcessContinuousCell(ref terrain, ref ctx, x, z, dist);
				}
			}
		}
	}

	private float CalculateBrushDistance(int x, int z, ref ContinuousEditContext ctx)
	{
		float vx = (x - ctx.Width / 2.0f) * ctx.QuadSize;
		float vz = (z - ctx.Depth / 2.0f) * ctx.QuadSize;
		if (ctx.BrushIsSquare) return Mathf.Max(Mathf.Abs(vx - ctx.WorldPos.X), Mathf.Abs(vz - ctx.WorldPos.Z));
		return new Vector2(vx - ctx.WorldPos.X, vz - ctx.WorldPos.Z).Length();
	}

	private void ProcessContinuousCell(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float dist)
	{
		if (ctx.IsHeights)
		{
			float falloff = Mathf.Sin((1.0f - (dist / ctx.BrushRadius)) * Mathf.Pi / 2.0f);
			ProcessContinuousHeight(ref terrain, ref ctx, x, z, falloff, dist);
		}
		else if (ctx.ActiveTool == EditorTool.PaintTexture)
		{
			int splatW = _terrainSplatMap!.GetLength(0);
			int splatD = _terrainSplatMap.GetLength(1);
			if (x >= 0 && x < splatW && z >= 0 && z < splatD && ShouldPaintCoord(x, z, ctx.NowMs))
			{
				int intensityLevel = Math.Clamp((int)MathF.Round(ctx.BrushStrength), 0, 10);
				ApplyPaintToCell(ref terrain, ref ctx, x, z, intensityLevel);
			}
		}
		else if (ctx.ActiveTool == EditorTool.PaintPathing && x < ctx.Width && z < ctx.Depth)
		{
			float vx = (x - ctx.Width / 2.0f) * ctx.QuadSize;
			float vz = (z - ctx.Depth / 2.0f) * ctx.QuadSize;
			int icx = Mathf.Clamp((int)Math.Round(vx / ctx.QuadSize + ctx.Width / 2.0f), 0, ctx.Width - 1);
			int icz = Mathf.Clamp((int)Math.Round(vz / ctx.QuadSize + ctx.Depth / 2.0f), 0, ctx.Depth - 1);
			ProcessBlockPathing(ref terrain, ref ctx, icx, icz);
		}
	}

	private void ProcessContinuousHeight(ref TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float falloff, float dist)
	{
		float oldH = GetGridNodeHeight(in terrain, x, z);
		float newH = CalculateNewContinuousHeight(in terrain, ref ctx, x, z, oldH, falloff, dist);

		if (Mathf.Abs(newH - oldH) > 0.001f)
		{
			if (ctx.ActiveTool == EditorTool.Plateau && terrain.PathingCodes != null)
			{
				int cellX = Math.Clamp(x, 0, ctx.Width - 1);
				int cellZ = Math.Clamp(z, 0, ctx.Depth - 1);
				int defaultPathBefore = EditableTerrain.GetDefaultPathingCode(terrain.Cells![cellX, cellZ]);
				if (terrain.PathingCodes[cellX, cellZ] == defaultPathBefore)
				{
					terrain.PathingCodes[cellX, cellZ] = EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
					ctx.PathingModified = true;
				}
			}

			SetGridNodeHeight(ref terrain, x, z, newH);
			if (ctx.ActiveTool == EditorTool.Raise || ctx.ActiveTool == EditorTool.Lower || ctx.ActiveTool == EditorTool.Plateau)
			{
				ApplyContinuousCliffSplat(ref ctx, x, z);
			}

			UpdateContextBoundsTight(ref ctx, x, z);
		}
	}

	private float CalculateNewContinuousHeight(in TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float oldH, float falloff, float dist)
	{
		if (ctx.ActiveTool == EditorTool.Raise) return Mathf.Clamp(oldH + ctx.BrushStrength * falloff * ctx.Delta, -10.0f, 50.0f);
		if (ctx.ActiveTool == EditorTool.Lower) return Mathf.Clamp(oldH - ctx.BrushStrength * falloff * ctx.Delta, -10.0f, 50.0f);
		if (ctx.ActiveTool == EditorTool.Plateau) return CalculatePlateauHeight(in terrain, ref ctx, x, z, oldH, falloff);
		if (ctx.ActiveTool == EditorTool.Smooth) return CalculateSmoothHeight(in terrain, ref ctx, x, z, oldH, falloff);
		if (ctx.ActiveTool == EditorTool.Noise) return CalculateNoiseHeight(oldH, x, z, dist, ref ctx);
		return oldH;
	}

	private float CalculatePlateauHeight(in TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float oldH, float falloff)
	{
		if (!_activePlateauHeight.HasValue)
		{
			_activePlateauHeight = GetTerrainHeightAt(ctx.WorldPos);
			_activePlateauWaterMode = GetWaterModeAt(ctx.WorldPos);
			_activePlateauWaterProfile = GetWaterProfileIndexAt(ctx.WorldPos);
			_activePlateauWaterHeight = GetWaterHeightAt(ctx.WorldPos);
		}

		float newH = Mathf.Clamp(Mathf.Lerp(oldH, _activePlateauHeight.Value, falloff), -10.0f, 50.0f);

		int cellX = Math.Clamp(x, 0, ctx.Width - 1);
		int cellZ = Math.Clamp(z, 0, ctx.Depth - 1);
		if (terrain.Cells != null && (terrain.Cells[cellX, cellZ].WaterMode != _activePlateauWaterMode || terrain.Cells[cellX, cellZ].WaterProfileIndex != _activePlateauWaterProfile || terrain.Cells[cellX, cellZ].WaterHeight != _activePlateauWaterHeight))
		{
			terrain.Cells[cellX, cellZ].WaterMode = _activePlateauWaterMode;
			terrain.Cells[cellX, cellZ].WaterProfileIndex = _activePlateauWaterProfile;
			terrain.Cells[cellX, cellZ].WaterHeight = _activePlateauWaterHeight;
			if (terrain.PathingCodes != null)
			{
				var waterProf = RuntimeTerrain.Instance?.GetWaterProfile(terrain.Cells[cellX, cellZ].WaterProfileIndex);
				terrain.PathingCodes[cellX, cellZ] = waterProf != null ? waterProf.DefaultPathingCode : EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
				ctx.PathingModified = true;
			}
		}

		return newH;
	}

	private float CalculateSmoothHeight(in TerrainState terrain, ref ContinuousEditContext ctx, int x, int z, float oldH, float falloff)
	{
		float avg = 0f;
		int count = 0;
		for (int nz = -1; nz <= 1; nz++)
		for (int nx = -1; nx <= 1; nx++)
			if (x + nx >= 0 && x + nx <= ctx.Width && z + nz >= 0 && z + nz <= ctx.Depth)
			{
				avg += GetGridNodeHeight(in terrain, x + nx, z + nz);
				count++;
			}

		avg /= count;
		float effectiveStrength = ctx.BrushStrength * 15.0f + 5.0f;
		return Mathf.Clamp(Mathf.Lerp(oldH, avg, Mathf.Clamp(effectiveStrength * falloff * ctx.Delta, 0.0f, 1.0f)), -10.0f, 50.0f);
	}

	private float CalculateNoiseHeight(float oldH, int x, int z, float dist, ref ContinuousEditContext ctx)
	{
		float dRatio = Mathf.Clamp(dist / ctx.BrushRadius, 0.0f, 1.0f);
		float falloffWeight = 1.0f - (dRatio * dRatio * (3.0f - 2.0f * dRatio));
		float noiseValue = (SimplexNoise.Simplex2D(x * 0.05f, z * 0.05f) * 1.0f + SimplexNoise.Simplex2D(x * 0.2f, z * 0.2f) * 0.35f) / 1.35f;
		return Mathf.Clamp(oldH + noiseValue * (ctx.BrushStrength * 0.25f + 8.25f) * falloffWeight * ctx.Delta * 0.5f, -10.0f, 50.0f);
	}

	private void ApplyContinuousCliffSplat(ref ContinuousEditContext ctx, int x, int z)
	{
		if (_terrainCliffSplatMap == null) return;
		for (int cz = z - 1; cz <= z; cz++)
		{
			for (int cx = x - 1; cx <= x; cx++)
			{
				if (cx >= 0 && cx < ctx.Width && cz >= 0 && cz < ctx.Depth) ApplyCliffSplatToNeighbors(ref ctx, cx, cz);
			}
		}
	}

	private void ApplyCliffSplatToNeighbors(ref ContinuousEditContext ctx, int cx, int cz)
	{
		for (int nz = cz - 1; nz <= cz + 1; nz++)
		{
			for (int nx = cx - 1; nx <= cx + 1; nx++)
			{
				if (nx >= 0 && nx < ctx.Width && nz >= 0 && nz < ctx.Depth) _terrainCliffSplatMap![nx, nz] = TerrainSplatWeights.CreateSolid(ctx.CliffPaintTextureIndex);
			}
		}
	}

	private void UpdateContextBounds(ref ContinuousEditContext ctx, int x, int z)
	{
		int minX = Math.Max(0, x - 1);
		int maxX = Math.Min(ctx.Width - 1, x + 1);
		int minZ = Math.Max(0, z - 1);
		int maxZ = Math.Min(ctx.Depth - 1, z + 1);
		if (minX < ctx.ModMinX) ctx.ModMinX = minX;
		if (maxX > ctx.ModMaxX) ctx.ModMaxX = maxX;
		if (minZ < ctx.ModMinZ) ctx.ModMinZ = minZ;
		if (maxZ > ctx.ModMaxZ) ctx.ModMaxZ = maxZ;
		ctx.Modified = true;
	}

	private void UpdateContextBoundsTight(ref ContinuousEditContext ctx, int x, int z)
	{
		if (x < ctx.ModMinX) ctx.ModMinX = x;
		if (x > ctx.ModMaxX) ctx.ModMaxX = x;
		if (z < ctx.ModMinZ) ctx.ModMinZ = z;
		if (z > ctx.ModMaxZ) ctx.ModMaxZ = z;
		ctx.Modified = true;
	}

	private TerrainEditResult FinalizeTerrainEditResult(ContinuousEditContext ctx, bool isHeights, bool isPaint, bool isPathing)
	{
		var result = new TerrainEditResult { PathingModified = ctx.PathingModified };
		if (!ctx.Modified) return result;

		result.HeightsModified = isHeights;
		result.SplatModified = isPaint || (isHeights && ctx.ActiveTool != EditorTool.Smooth && ctx.ActiveTool != EditorTool.Noise);
		result.PathingModified = result.PathingModified || isPathing;

		if (ctx.ModMinX < _drawMinX) _drawMinX = ctx.ModMinX;
		if (ctx.ModMaxX > _drawMaxX) _drawMaxX = ctx.ModMaxX;
		if (ctx.ModMinZ < _drawMinZ) _drawMinZ = ctx.ModMinZ;
		if (ctx.ModMaxZ > _drawMaxZ) _drawMaxZ = ctx.ModMaxZ;

		result.MinX = ctx.ModMinX;
		result.MinZ = ctx.ModMinZ;
		result.MaxX = ctx.ModMaxX;
		result.MaxZ = ctx.ModMaxZ;
		if (result.SplatModified) AlignSplatMapSlots(ctx.ModMinX - 2, ctx.ModMinZ - 2, ctx.ModMaxX + 2, ctx.ModMaxZ + 2);

		return result;
	}

	public void BeginTerrainDraw(
		Godot.Vector3 hitPos,
		EditorTool activeTool,
		bool blockMode,
		float blockLevelHeight,
		float[,] currentHeights = null,
		TerrainSplatWeights[,] currentSplatMap = null,
		int[,] currentPathing = null,
		TerrainSplatWeights[,] currentCliffSplatMap = null)
	{
		ref var terrain = ref GetTerrainState();
		_isDrawingTerrain = true;
		_drawMinX = int.MaxValue;
		_drawMinZ = int.MaxValue;
		_drawMaxX = -1;
		_drawMaxZ = -1;
		_terrainCellsBefore = terrain.Cells != null ? (TerrainCell[,])terrain.Cells.Clone() : null;
		_terrainSplatMapBefore = currentSplatMap != null ? (TerrainSplatWeights[,])currentSplatMap.Clone() : null;
		_terrainCliffSplatMapBefore = currentCliffSplatMap != null ? (TerrainSplatWeights[,])currentCliffSplatMap.Clone() : null;
		_terrainPathingBefore = currentPathing != null ? (int[,])currentPathing.Clone() : null;

		float startHeight = GetTerrainHeightAt(hitPos);

		if (activeTool == EditorTool.Plateau)
		{
			_activePlateauHeight = blockMode ? (float)MathF.Round(startHeight / TerrainCell.TIER_HEIGHT) * TerrainCell.TIER_HEIGHT : startHeight;
			_activePlateauWaterMode = GetWaterModeAt(hitPos);
			_activePlateauWaterProfile = GetWaterProfileIndexAt(hitPos);
			_activePlateauWaterHeight = GetWaterHeightAt(hitPos);
		}

		if (blockMode || activeTool == EditorTool.Height)
		{
			SetBlockTargetHeight(activeTool, startHeight, blockLevelHeight, hitPos);
		}
	}

	private void SetBlockTargetHeight(EditorTool activeTool, float startHeight, float blockLevelHeight, Godot.Vector3 hitPos)
	{
		if (activeTool == EditorTool.Height)
		{
			_activeBlockTargetHeight = Math.Clamp(blockLevelHeight, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
			_hasBlockTargetHeight = true;
		}
		else if (activeTool == EditorTool.Raise)
		{
			_activeBlockTargetHeight = Math.Clamp(startHeight + blockLevelHeight, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
			_hasBlockTargetHeight = true;
		}
		else if (activeTool == EditorTool.Lower)
		{
			_activeBlockTargetHeight = Math.Clamp(startHeight - blockLevelHeight, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
			_hasBlockTargetHeight = true;
		}
		else if (activeTool == EditorTool.Plateau)
		{
			_activeBlockTargetHeight = Math.Clamp((float)MathF.Round(startHeight / TerrainCell.TIER_HEIGHT) * TerrainCell.TIER_HEIGHT, TerrainCell.MIN_Y, TerrainCell.MAX_Y);
			_activeBlockTargetWaterMode = GetWaterModeAt(hitPos);
			_activeBlockTargetWaterProfile = GetWaterProfileIndexAt(hitPos);
			_activeBlockTargetWaterHeight = GetWaterHeightAt(hitPos);
			_hasBlockTargetHeight = true;
		}
	}

	private bool CheckCellsChanged(TerrainCell[,] currentCells, int w, int d)
	{
		if (_terrainCellsBefore == null || currentCells == null) return false;
		for (int z = 0; z < d; z++)
		{
			for (int x = 0; x < w; x++)
			{
				if (CheckSingleCellChanged(currentCells, x, z)) return true;
			}
		}

		return false;
	}

	private bool CheckSingleCellChanged(TerrainCell[,] currentCells, int x, int z)
	{
		int mapX = _drawMinX + x;
		int mapZ = _drawMinZ + z;
		if (mapX >= _terrainCellsBefore.GetLength(0) || mapZ >= _terrainCellsBefore.GetLength(1) ||
			mapX >= currentCells.GetLength(0) || mapZ >= currentCells.GetLength(1)) return false;

		var b = _terrainCellsBefore[mapX, mapZ];
		var a = currentCells[mapX, mapZ];

		return b.Y_NW != a.Y_NW || b.Y_NE != a.Y_NE || b.Y_SE != a.Y_SE || b.Y_SW != a.Y_SW ||
			   b.WaterMode != a.WaterMode || b.WaterProfileIndex != a.WaterProfileIndex;
	}

	private bool CheckPathingChanged(int[,] currentPathing, int w, int d)
	{
		if (_terrainPathingBefore == null || currentPathing == null) return false;
		for (int z = 0; z < d; z++)
		{
			for (int x = 0; x < w; x++)
			{
				int mapX = _drawMinX + x;
				int mapZ = _drawMinZ + z;
				if (mapX >= _terrainPathingBefore.GetLength(0) || mapZ >= _terrainPathingBefore.GetLength(1) ||
					mapX >= currentPathing.GetLength(0) || mapZ >= currentPathing.GetLength(1)) continue;
				if (_terrainPathingBefore[mapX, mapZ] != currentPathing[mapX, mapZ]) return true;
			}
		}

		return false;
	}

	private bool CheckSplatChanged(TerrainSplatWeights[,] currentSplatMap, TerrainSplatWeights[,] currentCliffSplatMap, int splatNodeW, int splatNodeD)
	{
		if (CheckSplatMapChanged(_terrainSplatMapBefore, currentSplatMap, splatNodeW, splatNodeD))
			return true;

		if (CheckSplatMapChanged(_terrainCliffSplatMapBefore, currentCliffSplatMap, splatNodeW, splatNodeD))
			return true;

		return false;
	}

	private bool CheckSplatMapChanged(TerrainSplatWeights[,]? mapBefore, TerrainSplatWeights[,]? currentMap, int splatNodeW, int splatNodeD)
	{
		if (mapBefore == null || currentMap == null) return false;

		for (int z = 0; z < splatNodeD; z++)
		{
			for (int x = 0; x < splatNodeW; x++)
			{
				if (CheckSingleSplatChanged(mapBefore, currentMap, x, z)) return true;
			}
		}

		return false;
	}

	private bool CheckSingleSplatChanged(TerrainSplatWeights[,] mapBefore, TerrainSplatWeights[,] currentMap, int x, int z)
	{
		int mapX = _drawMinX + x;
		int mapZ = _drawMinZ + z;

		if (mapX >= mapBefore.GetLength(0) || mapZ >= mapBefore.GetLength(1) ||
			mapX >= currentMap.GetLength(0) || mapZ >= currentMap.GetLength(1)) return false;

		var b = mapBefore[mapX, mapZ];
		var a = currentMap[mapX, mapZ];

		return b.Index0 != a.Index0 || b.Index1 != a.Index1 || b.Index2 != a.Index2 || b.Index3 != a.Index3 ||
			   b.Weight0 != a.Weight0 || b.Weight1 != a.Weight1 || b.Weight2 != a.Weight2 || b.Weight3 != a.Weight3;
	}

	private void BuildChangedCells(TerrainCell[,] currentCells, int w, int d, out TerrainCell[,] beforeC, out TerrainCell[,] afterC)
	{
		beforeC = new TerrainCell[w, d];
		afterC = new TerrainCell[w, d];
		for (int z = 0; z < d; z++)
		{
			for (int x = 0; x < w; x++)
			{
				int mapX = _drawMinX + x;
				int mapZ = _drawMinZ + z;
				if (_terrainCellsBefore != null && mapX < _terrainCellsBefore.GetLength(0) && mapZ < _terrainCellsBefore.GetLength(1))
					beforeC[x, z] = _terrainCellsBefore[mapX, mapZ];
				if (currentCells != null && mapX < currentCells.GetLength(0) && mapZ < currentCells.GetLength(1))
					afterC[x, z] = currentCells[mapX, mapZ];
			}
		}
	}

	private void BuildChangedPathing(int[,] currentPathing, int w, int d, out int[,] beforeP, out int[,] afterP)
	{
		beforeP = new int[w, d];
		afterP = new int[w, d];
		for (int z = 0; z < d; z++)
		{
			for (int x = 0; x < w; x++)
			{
				int mapX = _drawMinX + x;
				int mapZ = _drawMinZ + z;
				if (_terrainPathingBefore != null && mapX < _terrainPathingBefore.GetLength(0) && mapZ < _terrainPathingBefore.GetLength(1))
					beforeP[x, z] = _terrainPathingBefore[mapX, mapZ];
				if (currentPathing != null && mapX < currentPathing.GetLength(0) && mapZ < currentPathing.GetLength(1))
					afterP[x, z] = currentPathing[mapX, mapZ];
			}
		}
	}

	private void BuildChangedSplat(TerrainSplatWeights[,] currentSplatMap, TerrainSplatWeights[,] currentCliffSplatMap, int splatNodeW, int splatNodeD, out TerrainSplatWeights[,] beforeS, out TerrainSplatWeights[,] afterS, out TerrainSplatWeights[,] beforeCliffS, out TerrainSplatWeights[,] afterCliffS)
	{
		beforeS = null;
		afterS = null;
		beforeCliffS = null;
		afterCliffS = null;
		if (currentSplatMap != null)
		{
			beforeS = new TerrainSplatWeights[splatNodeW, splatNodeD];
			afterS = new TerrainSplatWeights[splatNodeW, splatNodeD];
			for (int z = 0; z < splatNodeD; z++)
			{
				for (int x = 0; x < splatNodeW; x++)
				{
					int mapX = _drawMinX + x;
					int mapZ = _drawMinZ + z;
					if (_terrainSplatMapBefore != null && mapX < _terrainSplatMapBefore.GetLength(0) && mapZ < _terrainSplatMapBefore.GetLength(1))
						beforeS[x, z] = _terrainSplatMapBefore[mapX, mapZ];
					if (mapX < currentSplatMap.GetLength(0) && mapZ < currentSplatMap.GetLength(1))
						afterS[x, z] = currentSplatMap[mapX, mapZ];
				}
			}
		}

		if (currentCliffSplatMap != null)
		{
			beforeCliffS = new TerrainSplatWeights[splatNodeW, splatNodeD];
			afterCliffS = new TerrainSplatWeights[splatNodeW, splatNodeD];
			for (int z = 0; z < splatNodeD; z++)
			{
				for (int x = 0; x < splatNodeW; x++)
				{
					int mapX = _drawMinX + x;
					int mapZ = _drawMinZ + z;
					if (_terrainCliffSplatMapBefore != null && mapX < _terrainCliffSplatMapBefore.GetLength(0) && mapZ < _terrainCliffSplatMapBefore.GetLength(1))
						beforeCliffS[x, z] = _terrainCliffSplatMapBefore[mapX, mapZ];
					if (mapX < currentCliffSplatMap.GetLength(0) && mapZ < currentCliffSplatMap.GetLength(1))
						afterCliffS[x, z] = currentCliffSplatMap[mapX, mapZ];
				}
			}
		}
	}

	public TerrainModifyAction EndTerrainDraw(
		float[,] currentHeights = null,
		TerrainSplatWeights[,] currentSplatMap = null,
		int[,] currentPathing = null,
		TerrainSplatWeights[,] currentCliffSplatMap = null)
	{
		_isDrawingTerrain = false;
		_hasBlockTargetHeight = false;
		_activePlateauHeight = null;
		_activePlateauWaterMode = WaterType.None;
		_activePlateauWaterProfile = 0;
		_activeBlockTargetWaterMode = WaterType.None;
		_activeBlockTargetWaterProfile = 0;

		ref var terrain = ref GetTerrainState();
		var currentCells = terrain.Cells;

		if (_drawMinX <= _drawMaxX && _drawMinZ <= _drawMaxZ)
		{
			bool cellsChanged = false;
			bool pathingChanged = false;
			bool splatChanged = false;

			int w = _drawMaxX - _drawMinX + 1;
			int d = _drawMaxZ - _drawMinZ + 1;

			if (_terrainCellsBefore != null && currentCells != null)
			{
				for (int z = 0; z < d && !cellsChanged; z++)
				{
					for (int x = 0; x < w && !cellsChanged; x++)
					{
						int mapX = _drawMinX + x;
						int mapZ = _drawMinZ + z;
						if (mapX < _terrainCellsBefore.GetLength(0) && mapZ < _terrainCellsBefore.GetLength(1) &&
							mapX < currentCells.GetLength(0) && mapZ < currentCells.GetLength(1))
						{
							var b = _terrainCellsBefore[mapX, mapZ];
							var a = currentCells[mapX, mapZ];
							if (b.Y_NW != a.Y_NW || b.Y_NE != a.Y_NE || b.Y_SE != a.Y_SE || b.Y_SW != a.Y_SW || b.WaterMode != a.WaterMode || b.WaterProfileIndex != a.WaterProfileIndex)
							{
								cellsChanged = true;
							}
						}
					}
				}
			}

			if (_terrainPathingBefore != null && currentPathing != null)
			{
				for (int z = 0; z < d && !pathingChanged; z++)
				{
					for (int x = 0; x < w && !pathingChanged; x++)
					{
						int mapX = _drawMinX + x;
						int mapZ = _drawMinZ + z;
						if (mapX < _terrainPathingBefore.GetLength(0) && mapZ < _terrainPathingBefore.GetLength(1) &&
							mapX < currentPathing.GetLength(0) && mapZ < currentPathing.GetLength(1))
						{
							if (_terrainPathingBefore[mapX, mapZ] != currentPathing[mapX, mapZ])
							{
								pathingChanged = true;
							}
						}
					}
				}
			}

			int splatW = currentSplatMap != null ? currentSplatMap.GetLength(0) : terrain.Width + 1;
			int splatD = currentSplatMap != null ? currentSplatMap.GetLength(1) : terrain.Depth + 1;
			int splatNodeW = Math.Min(w + 1, splatW - _drawMinX);
			int splatNodeD = Math.Min(d + 1, splatD - _drawMinZ);

			if (_terrainSplatMapBefore != null && currentSplatMap != null)
			{
				for (int z = 0; z < splatNodeD && !splatChanged; z++)
				{
					for (int x = 0; x < splatNodeW && !splatChanged; x++)
					{
						int mapX = _drawMinX + x;
						int mapZ = _drawMinZ + z;
						if (mapX < _terrainSplatMapBefore.GetLength(0) && mapZ < _terrainSplatMapBefore.GetLength(1) &&
							mapX < currentSplatMap.GetLength(0) && mapZ < currentSplatMap.GetLength(1))
						{
							var b = _terrainSplatMapBefore[mapX, mapZ];
							var a = currentSplatMap[mapX, mapZ];
							if (b.Index0 != a.Index0 || b.Index1 != a.Index1 || b.Index2 != a.Index2 || b.Index3 != a.Index3 ||
								b.Weight0 != a.Weight0 || b.Weight1 != a.Weight1 || b.Weight2 != a.Weight2 || b.Weight3 != a.Weight3)
							{
								splatChanged = true;
							}
						}
					}
				}
			}

			if (!splatChanged && _terrainCliffSplatMapBefore != null && currentCliffSplatMap != null)
			{
				for (int z = 0; z < splatNodeD && !splatChanged; z++)
				{
					for (int x = 0; x < splatNodeW && !splatChanged; x++)
					{
						int mapX = _drawMinX + x;
						int mapZ = _drawMinZ + z;
						if (mapX < _terrainCliffSplatMapBefore.GetLength(0) && mapZ < _terrainCliffSplatMapBefore.GetLength(1) &&
							mapX < currentCliffSplatMap.GetLength(0) && mapZ < currentCliffSplatMap.GetLength(1))
						{
							var b = _terrainCliffSplatMapBefore[mapX, mapZ];
							var a = currentCliffSplatMap[mapX, mapZ];
							if (b.Index0 != a.Index0 || b.Index1 != a.Index1 || b.Index2 != a.Index2 || b.Index3 != a.Index3 ||
								b.Weight0 != a.Weight0 || b.Weight1 != a.Weight1 || b.Weight2 != a.Weight2 || b.Weight3 != a.Weight3)
							{
								splatChanged = true;
							}
						}
					}
				}
			}

			TerrainCell[,] beforeC = null;
			TerrainCell[,] afterC = null;
			if (cellsChanged)
			{
				beforeC = new TerrainCell[w, d];
				afterC = new TerrainCell[w, d];
				for (int z = 0; z < d; z++)
				{
					for (int x = 0; x < w; x++)
					{
						int mapX = _drawMinX + x;
						int mapZ = _drawMinZ + z;
						if (_terrainCellsBefore != null && mapX < _terrainCellsBefore.GetLength(0) && mapZ < _terrainCellsBefore.GetLength(1))
						{
							beforeC[x, z] = _terrainCellsBefore[mapX, mapZ];
						}

						if (currentCells != null && mapX < currentCells.GetLength(0) && mapZ < currentCells.GetLength(1))
						{
							afterC[x, z] = currentCells[mapX, mapZ];
						}
					}
				}
			}

			int[,] beforeP = null;
			int[,] afterP = null;
			if (pathingChanged)
			{
				beforeP = new int[w, d];
				afterP = new int[w, d];
				for (int z = 0; z < d; z++)
				{
					for (int x = 0; x < w; x++)
					{
						int mapX = _drawMinX + x;
						int mapZ = _drawMinZ + z;
						if (_terrainPathingBefore != null && mapX < _terrainPathingBefore.GetLength(0) && mapZ < _terrainPathingBefore.GetLength(1))
						{
							beforeP[x, z] = _terrainPathingBefore[mapX, mapZ];
						}

						if (currentPathing != null && mapX < currentPathing.GetLength(0) && mapZ < currentPathing.GetLength(1))
						{
							afterP[x, z] = currentPathing[mapX, mapZ];
						}
					}
				}
			}

			TerrainSplatWeights[,] beforeS = null;
			TerrainSplatWeights[,] afterS = null;
			TerrainSplatWeights[,] beforeCliffS = null;
			TerrainSplatWeights[,] afterCliffS = null;
			if (splatChanged)
			{
				if (currentSplatMap != null)
				{
					beforeS = new TerrainSplatWeights[splatNodeW, splatNodeD];
					afterS = new TerrainSplatWeights[splatNodeW, splatNodeD];
					for (int z = 0; z < splatNodeD; z++)
					{
						for (int x = 0; x < splatNodeW; x++)
						{
							int mapX = _drawMinX + x;
							int mapZ = _drawMinZ + z;
							if (_terrainSplatMapBefore != null && mapX < _terrainSplatMapBefore.GetLength(0) && mapZ < _terrainSplatMapBefore.GetLength(1))
							{
								beforeS[x, z] = _terrainSplatMapBefore[mapX, mapZ];
							}

							if (mapX < currentSplatMap.GetLength(0) && mapZ < currentSplatMap.GetLength(1))
							{
								afterS[x, z] = currentSplatMap[mapX, mapZ];
							}
						}
					}
				}

				if (currentCliffSplatMap != null)
				{
					beforeCliffS = new TerrainSplatWeights[splatNodeW, splatNodeD];
					afterCliffS = new TerrainSplatWeights[splatNodeW, splatNodeD];
					for (int z = 0; z < splatNodeD; z++)
					{
						for (int x = 0; x < splatNodeW; x++)
						{
							int mapX = _drawMinX + x;
							int mapZ = _drawMinZ + z;
							if (_terrainCliffSplatMapBefore != null && mapX < _terrainCliffSplatMapBefore.GetLength(0) && mapZ < _terrainCliffSplatMapBefore.GetLength(1))
							{
								beforeCliffS[x, z] = _terrainCliffSplatMapBefore[mapX, mapZ];
							}

							if (mapX < currentCliffSplatMap.GetLength(0) && mapZ < currentCliffSplatMap.GetLength(1))
							{
								afterCliffS[x, z] = currentCliffSplatMap[mapX, mapZ];
							}
						}
					}
				}
			}

			return new TerrainModifyAction(_drawMinX, _drawMinZ, w, d, beforeC, afterC, beforeS, afterS, beforeP, afterP, beforeCliffS, afterCliffS);
		}

		return new TerrainModifyAction(
			_terrainCellsBefore, currentCells,
			_terrainSplatMapBefore, currentSplatMap,
			_terrainPathingBefore, currentPathing,
			_terrainCliffSplatMapBefore, currentCliffSplatMap);
	}

	public void ResetDrawState()
	{
		_hasBlockTargetHeight = false;
		_activePlateauHeight = null;
		_activePlateauWaterMode = WaterType.None;
		_activeBlockTargetWaterMode = WaterType.None;
	}

	public void ResetAllState()
	{
		_selectionStart = null;
		_selectionEnd = null;
		_copiedArea = null;
		_hasBlockTargetHeight = false;
		_activeBlockTargetHeight = 0.0f;
		_activePlateauHeight = null;
		_activePlateauWaterMode = WaterType.None;
		_activeBlockTargetWaterMode = WaterType.None;
		_terrainSplatMap = null;
		_terrainCliffSplatMap = null;
		_clumpSpawnCooldown = 0.0f;
	}

	public void TickClumpCooldown(float delta)
	{
		if (_clumpSpawnCooldown > 0.0f)
			_clumpSpawnCooldown -= delta;
	}

	public bool CanSpawnClump() => _clumpSpawnCooldown <= 0.0f;

	public void BeginClumpSession()
	{
		_isDrawingClump = true;
		_clumpSpawnActionsInSession.Clear();
		_cachedSessionPoints.Clear();
	}

	public Entity SpawnPropDomain(string propId, System.Numerics.Vector3 position, float rotationY, float scale, string cleanId, float defaultAmount, float terrainHeight, float collisionRadius)
	{
		var entity = GameContext.World.Create();
		GameContext.World.Add(entity, new PropIdentity(propId));
		GameContext.RegistryService.TryAddResourceNodeComponent(entity, propId, cleanId, defaultAmount);

		GameContext.World.Add(entity, new Prop());
		GameContext.World.Add(entity, new Position(new System.Numerics.Vector3(position.X, terrainHeight, position.Z)));
		GameContext.World.Add(entity, new RotationY(rotationY));
		GameContext.World.Add(entity, new ModelScale(scale));
		GameContext.World.Add(entity, new CollisionScale(scale));
		GameContext.World.Add(entity, new CollisionRadius(collisionRadius));
		return entity;
	}

	public void RecordClumpSpawnAction(IEditorAction action)
	{
		_clumpSpawnActionsInSession.Add(action);
		if (action is ObjectSpawnAction objAct && GodotObject.IsInstanceValid(objAct.SpawnedNode))
		{
			float baseRadius = 0.5f;
			_cachedSessionPoints.Add((objAct.Position, baseRadius * objAct.Scale));
		}
	}

	public void SetClumpCooldown(float value)
	{
		_clumpSpawnCooldown = value;
	}

	public CompositeAction EndClumpSession()
	{
		_isDrawingClump = false;
		if (_clumpSpawnActionsInSession.Count > 0)
		{
			return new CompositeAction(_clumpSpawnActionsInSession);
		}

		return null;
	}

	public List<EntitySpawnRequest> BuildClumpSpawnRequests(
		Godot.Vector3 centerPos,
		EditorTool activeTool,
		string activePlaceId,
		bool placeUnitIsEnemy,
		float placementScale,
		float clumpCount,
		float clumpScale,
		float brushRadius,
		bool brushIsSquare,
		bool randomRotation,
		bool randomScale,
		float placementRotation,
		MirrorMode mirrorMode,
		float assetBaseCollisionRadius = 0.5f)
	{
		if (string.IsNullOrEmpty(activePlaceId)) return new List<EntitySpawnRequest>();

		var requests = new List<EntitySpawnRequest>();
		int spawnCount = Mathf.Max(1, (int)Math.Round(clumpCount));

		ref var terrain = ref GetTerrainState();
		float quadSize = terrain.Cells != null ? terrain.QuadSize : 1f;
		int terrainWidth = terrain.Cells != null ? terrain.Width : 0;
		int terrainDepth = terrain.Cells != null ? terrain.Depth : 0;
		float halfW = terrainWidth / 2.0f * quadSize;
		float halfD = terrainDepth / 2.0f * quadSize;

		float baseRadius = Mathf.Max(0.1f, assetBaseCollisionRadius);
		float autoClumpSpacing = Mathf.Clamp(brushRadius / (1.5f * Mathf.Sqrt(Mathf.Max(1, spawnCount))), 0.5f, 4.0f);

		int maxAttemptsPerObject = 30;

		for (int i = 0; i < spawnCount; i++)
		{
			bool placed = false;
			for (int attempt = 0; attempt < maxAttemptsPerObject; attempt++)
			{
				float dx = 0.0f;
				float dz = 0.0f;
				if (brushIsSquare)
				{
					dx = (float)(GD.Randf() * 2.0 - 1.0) * brushRadius;
					dz = (float)(GD.Randf() * 2.0 - 1.0) * brushRadius;
				}
				else
				{
					float r = Mathf.Sqrt((float)GD.Randf()) * brushRadius;
					float theta = (float)(GD.Randf() * Mathf.Pi * 2.0);
					dx = r * Mathf.Cos(theta);
					dz = r * Mathf.Sin(theta);
				}

				Godot.Vector3 spawnPos = new Godot.Vector3(centerPos.X + dx, centerPos.Y, centerPos.Z + dz);
				if (terrain.Cells != null)
				{
					if (Mathf.Abs(spawnPos.X) > halfW || Mathf.Abs(spawnPos.Z) > halfD) continue;
				}

				spawnPos.Y = GetTerrainHeightAt(spawnPos);

				float offsetRange = clumpScale * placementScale;
				float minScale = Mathf.Max(0.01f, placementScale - offsetRange);
				float maxScale = placementScale + offsetRange;
				float scaleVal = minScale + (float)GD.Randf() * (maxScale - minScale);

				float candidateRadius = baseRadius * scaleVal;

				bool collision = false;
				foreach (var req in requests)
				{
					float reqRadius = baseRadius * req.Scale;
					float minDist = (candidateRadius + reqRadius) * autoClumpSpacing;
					float distSq = (spawnPos.X - req.Position.X) * (spawnPos.X - req.Position.X) +
								   (spawnPos.Z - req.Position.Z) * (spawnPos.Z - req.Position.Z);
					if (distSq < minDist * minDist)
					{
						collision = true;
						break;
					}
				}

				if (collision) continue;

				foreach (var sessionPt in _cachedSessionPoints)
				{
					float minDist = (candidateRadius + sessionPt.Radius) * autoClumpSpacing;
					float distSq = (spawnPos.X - sessionPt.Position.X) * (spawnPos.X - sessionPt.Position.X) +
								   (spawnPos.Z - sessionPt.Position.Z) * (spawnPos.Z - sessionPt.Position.Z);
					if (distSq < minDist * minDist)
					{
						collision = true;
						break;
					}
				}

				if (collision) continue;

				float rotY = randomRotation ? (float)(GD.Randf() * 360.0) : placementRotation;
				string spawnType = activeTool == EditorTool.PlaceUnit ? "unit"
					: activeTool == EditorTool.PlaceProp ? "prop"
					: "decal";
				bool isEnemy = activeTool == EditorTool.PlaceUnit && placeUnitIsEnemy;

				requests.Add(new EntitySpawnRequest
				{
					Type = spawnType,
					Id = activePlaceId,
					Position = spawnPos,
					Rotation = rotY,
					Scale = scaleVal,
					IsEnemy = isEnemy
				});

				if (mirrorMode != MirrorMode.None)
				{
					AddMirroredRequests(requests, spawnType, activePlaceId, spawnPos, rotY, scaleVal, isEnemy, mirrorMode);
				}

				placed = true;
				break;
			}

			if (!placed && requests.Count > 0)
			{
			}
		}

		return requests;
	}

	private void ProcessRampCell(Godot.Vector3 start, Godot.Vector3 end, float brushRadius, float segmentLengthSquared, int width, int depth, float quadSize, int gridX, int gridZ, ref TerrainState terrain, ref bool modified, HashSet<Vector2I> modifiedCells)
	{
		float worldX = (gridX - width / 2.0f) * quadSize;
		float worldZ = (gridZ - depth / 2.0f) * quadSize;
		float interpolationFactor = ((worldX - start.X) * (end.X - start.X) + (worldZ - start.Z) * (end.Z - start.Z)) / segmentLengthSquared;
		interpolationFactor = Mathf.Clamp(interpolationFactor, 0.0f, 1.0f);
		float projectedX = start.X + interpolationFactor * (end.X - start.X);
		float projectedZ = start.Z + interpolationFactor * (end.Z - start.Z);
		float distanceToProjected = Mathf.Sqrt((worldX - projectedX) * (worldX - projectedX) + (worldZ - projectedZ) * (worldZ - projectedZ));
		if (distanceToProjected > brushRadius) return;

		float targetHeight = Mathf.Lerp(start.Y, end.Y, interpolationFactor);
		float innerRadius = brushRadius * 0.7f;
		float oldHeight = GetGridNodeHeight(in terrain, gridX, gridZ);
		float newHeight = targetHeight;

		if (distanceToProjected > innerRadius)
		{
			float edgeFactor = 1.0f - ((distanceToProjected - innerRadius) / Math.Max(0.001f, brushRadius - innerRadius));
			edgeFactor = Mathf.Sin(edgeFactor * Mathf.Pi / 2.0f);
			newHeight = Mathf.Lerp(oldHeight, targetHeight, edgeFactor);
		}

		if (Mathf.Abs(newHeight - oldHeight) <= 0.001f) return;

		int cellX = Math.Clamp(gridX, 0, width - 1);
		int cellZ = Math.Clamp(gridZ, 0, depth - 1);
		if (terrain.PathingCodes != null)
		{
			int defaultPathBefore = EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
			if (terrain.PathingCodes[cellX, cellZ] == defaultPathBefore)
			{
				terrain.PathingCodes[cellX, cellZ] = EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
			}
		}

		SetGridNodeHeight(ref terrain, gridX, gridZ, newHeight);
		int rampPaintIdx = GameHost.Instance != null ? GameContext.EditorService.EditorPaintTextureIndex : 0;
		int rampCliffIdx = GameHost.Instance != null ? GameContext.EditorService.EditorCliffPaintTextureIndex : 1;
		SetGridNodeSplat(gridX, gridZ, new TerrainSplatWeights { Index0 = rampPaintIdx, Weight0 = 1.0f }, new TerrainSplatWeights { Index0 = rampPaintIdx, Weight0 = 1.0f }, new TerrainSplatWeights { Index0 = rampPaintIdx, Weight0 = 1.0f }, new TerrainSplatWeights { Index0 = rampPaintIdx, Weight0 = 1.0f });
		SetGridNodeCliffSplat(gridX, gridZ, new TerrainSplatWeights { Index0 = rampCliffIdx, Weight0 = 1.0f }, new TerrainSplatWeights { Index0 = rampCliffIdx, Weight0 = 1.0f }, new TerrainSplatWeights { Index0 = rampCliffIdx, Weight0 = 1.0f }, new TerrainSplatWeights { Index0 = rampCliffIdx, Weight0 = 1.0f });
		modified = true;
		modifiedCells.Add(new Vector2I(gridX, gridZ));
	}

	public bool ApplyRamp(Godot.Vector3 start, Godot.Vector3 end, float brushRadius, bool blockMode, float blockLevelHeight)
	{
		ref var terrain = ref GetTerrainState();
		var cells = terrain.Cells;
		if (cells == null) return false;

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;
		bool modified = false;

		float segmentLengthSquared = (end.X - start.X) * (end.X - start.X) + (end.Z - start.Z) * (end.Z - start.Z);
		if (segmentLengthSquared <= 0.0001f) return false;

		float minWorldX = Mathf.Min(start.X, end.X) - brushRadius;
		float maxWorldX = Mathf.Max(start.X, end.X) + brushRadius;
		float minWorldZ = Mathf.Min(start.Z, end.Z) - brushRadius;
		float maxWorldZ = Mathf.Max(start.Z, end.Z) + brushRadius;

		int minGridX = Mathf.Clamp(Mathf.FloorToInt(minWorldX / quadSize + width / 2.0f), 0, width);
		int maxGridX = Mathf.Clamp(Mathf.CeilToInt(maxWorldX / quadSize + width / 2.0f), 0, width);
		int minGridZ = Mathf.Clamp(Mathf.FloorToInt(minWorldZ / quadSize + depth / 2.0f), 0, depth);
		int maxGridZ = Mathf.Clamp(Mathf.CeilToInt(maxWorldZ / quadSize + depth / 2.0f), 0, depth);

		var modifiedCells = new HashSet<Vector2I>();

		for (int gridZ = minGridZ; gridZ <= maxGridZ; gridZ++)
		{
			for (int gridX = minGridX; gridX <= maxGridX; gridX++)
			{
				float worldX = (gridX - width / 2.0f) * quadSize;
				float worldZ = (gridZ - depth / 2.0f) * quadSize;
				float interpolationFactor = ((worldX - start.X) * (end.X - start.X) + (worldZ - start.Z) * (end.Z - start.Z)) / segmentLengthSquared;
				interpolationFactor = Mathf.Clamp(interpolationFactor, 0.0f, 1.0f);
				float projectedX = start.X + interpolationFactor * (end.X - start.X);
				float projectedZ = start.Z + interpolationFactor * (end.Z - start.Z);
				float distanceToProjected = Mathf.Sqrt((worldX - projectedX) * (worldX - projectedX) + (worldZ - projectedZ) * (worldZ - projectedZ));
				if (distanceToProjected <= brushRadius)
				{
					float targetHeight = Mathf.Lerp(start.Y, end.Y, interpolationFactor);
					float innerRadius = brushRadius * 0.7f;
					float oldHeight = GetGridNodeHeight(in terrain, gridX, gridZ);
					float newHeight;

					if (distanceToProjected <= innerRadius)
					{
						newHeight = targetHeight;
					}
					else
					{
						float edgeFactor = 1.0f - ((distanceToProjected - innerRadius) / Math.Max(0.001f, brushRadius - innerRadius));
						edgeFactor = Mathf.Sin(edgeFactor * Mathf.Pi / 2.0f);
						newHeight = Mathf.Lerp(oldHeight, targetHeight, edgeFactor);
					}

					if (Mathf.Abs(newHeight - oldHeight) > 0.001f)
					{
						int cellX = Math.Clamp(gridX, 0, width - 1);
						int cellZ = Math.Clamp(gridZ, 0, depth - 1);
						if (terrain.PathingCodes != null)
						{
							int defaultPathBefore = EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
							if (terrain.PathingCodes[cellX, cellZ] == defaultPathBefore)
							{
								terrain.PathingCodes[cellX, cellZ] = EditableTerrain.GetDefaultPathingCode(terrain.Cells[cellX, cellZ]);
							}
						}

						SetGridNodeHeight(ref terrain, gridX, gridZ, newHeight);
						int rampPaintIdx = GameHost.Instance != null ? GameContext.EditorService.EditorPaintTextureIndex : 0;
						int rampCliffIdx = GameHost.Instance != null ? GameContext.EditorService.EditorCliffPaintTextureIndex : 1;

						if (_terrainSplatMap == null && GameHost.Instance?.GroundTerrain != null)
						{
							_terrainSplatMap = GameHost.Instance.GroundTerrain.SplatMap;
						}

						if (_terrainSplatMap != null)
						{
							for (int cz = gridZ - 1; cz <= gridZ; cz++)
							{
								for (int cx = gridX - 1; cx <= gridX; cx++)
								{
									if (cx >= 0 && cx < width && cz >= 0 && cz < depth)
									{
										_terrainSplatMap[cx, cz] = TerrainSplatWeights.CreateSolid(rampPaintIdx);
										modifiedCells.Add(new Vector2I(cx, cz));
									}
								}
							}
						}

						if (_terrainCliffSplatMap == null && GameHost.Instance?.GroundTerrain != null)
						{
							_terrainCliffSplatMap = GameHost.Instance.GroundTerrain.CliffSplatMap;
						}

						if (_terrainCliffSplatMap != null)
						{
							for (int cz = gridZ - 1; cz <= gridZ; cz++)
							{
								for (int cx = gridX - 1; cx <= gridX; cx++)
								{
									if (cx >= 0 && cx < width && cz >= 0 && cz < depth)
									{
										for (int nz = cz - 1; nz <= cz + 1; nz++)
										{
											for (int nx = cx - 1; nx <= cx + 1; nx++)
											{
												if (nx >= 0 && nx < width && nz >= 0 && nz < depth)
												{
													_terrainCliffSplatMap[nx, nz] = TerrainSplatWeights.CreateSolid(rampCliffIdx);
												}
											}
										}
									}
								}
							}
						}

						modified = true;
					}
				}
			}
		}

		if (modified)
		{
			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
			TerrainSwatchProfileData? rampProf = null;
			int rampTexIdx = GameHost.Instance != null ? GameContext.EditorService.EditorPaintTextureIndex : 0;
			if (!string.IsNullOrEmpty(wsPath) && GameContext.MetadataService.TryLoadMetadata(wsPath, out var metaRoot) && metaRoot != null)
			{
				string? swatchName = null;
				if (GameHost.Instance?.GroundTerrain != null && rampTexIdx >= 0 && rampTexIdx < GameHost.Instance.GroundTerrain.LoadedTextureList.Count)
				{
					swatchName = GameHost.Instance.GroundTerrain.LoadedTextureList[rampTexIdx];
				}

				if (!string.IsNullOrEmpty(swatchName))
				{
					rampProf = metaRoot.GetTerrainProfile(swatchName);
				}
			}

			foreach (var cellPos in modifiedCells)
			{
				if (rampProf != null && terrain.PathingCodes != null && cellPos.X < width && cellPos.Y < depth)
				{
					terrain.PathingCodes[cellPos.X, cellPos.Y] = rampProf.DefaultPathingCode;
				}

				if (terrain.Cells != null && cellPos.X < width && cellPos.Y < depth)
				{
					float tY = terrain.Cells[cellPos.X, cellPos.Y].CenterHeight;
					SpawnTerrainProceduralBombing(cellPos.X, cellPos.Y, rampTexIdx, tY, quadSize, width, depth);
				}
			}

			AlignSplatMapSlots(minGridX - 2, minGridZ - 2, maxGridX + 2, maxGridZ + 2);
		}

		return modified;
	}

	public float GetMinHeightInBrushBounds(Godot.Vector3 worldPos, float brushRadius, bool brushIsSquare)
	{
		float result = GetMinHeightInBrushBoundsInternal(worldPos, brushRadius, brushIsSquare);
		return result;
	}

	public void CopyArea(int minX, int minZ, int maxX, int maxZ, List<CopiedEntityInfo> entities, bool isSquare = true)
	{
		ref var terrain = ref GetTerrainState();
		var cells = terrain.Cells;
		if (cells == null) return;

		if (_terrainSplatMap == null && GameHost.Instance?.GroundTerrain != null)
		{
			_terrainSplatMap = GameHost.Instance.GroundTerrain.SplatMap;
		}

		if (_terrainCliffSplatMap == null && GameHost.Instance?.GroundTerrain != null)
		{
			_terrainCliffSplatMap = GameHost.Instance.GroundTerrain.CliffSplatMap;
		}

		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
		var copiedCells = new TerrainCell[selWidth, selDepth];
		var splatMap = _terrainSplatMap != null ? new TerrainSplatWeights[selWidth + 1, selDepth + 1] : null;
		var cliffSplatMap = _terrainCliffSplatMap != null ? new TerrainSplatWeights[selWidth + 1, selDepth + 1] : null;
		var pathing = new int[selWidth, selDepth];
		var mask = isSquare ? null : new bool[selWidth, selDepth];

		float selCenterX = (minX + maxX) * 0.5f;
		float selCenterZ = (minZ + maxZ) * 0.5f;
		float rx = Math.Max(0.5f, (maxX - minX) * 0.5f);
		float rz = Math.Max(0.5f, (maxZ - minZ) * 0.5f);

		for (int sz = 0; sz < selDepth; sz++)
		{
			for (int sx = 0; sx < selWidth; sx++)
			{
				int sourceX = Math.Clamp(minX + sx, 0, terrain.Width - 1);
				int sourceZ = Math.Clamp(minZ + sz, 0, terrain.Depth - 1);
				bool inBounds = true;
				if (!isSquare)
				{
					float cellCenterX = minX + sx + 0.5f;
					float cellCenterZ = minZ + sz + 0.5f;
					float ndx = (cellCenterX - selCenterX) / rx;
					float ndz = (cellCenterZ - selCenterZ) / rz;
					inBounds = (ndx * ndx + ndz * ndz <= 1.05f);
				}

				if (mask != null)
				{
					mask[sx, sz] = inBounds;
				}

				if (inBounds)
				{
					copiedCells[sx, sz] = cells[sourceX, sourceZ];
					if (terrain.PathingCodes != null && sourceX < terrain.PathingCodes.GetLength(0) && sourceZ < terrain.PathingCodes.GetLength(1))
					{
						pathing[sx, sz] = terrain.PathingCodes[sourceX, sourceZ];
					}
				}
			}
		}

		if (_terrainSplatMap != null && splatMap != null)
		{
			int mapW = _terrainSplatMap.GetLength(0);
			int mapD = _terrainSplatMap.GetLength(1);
			for (int vz = 0; vz <= selDepth; vz++)
			{
				for (int vx = 0; vx <= selWidth; vx++)
				{
					int sourceVx = Math.Clamp(minX + vx, 0, mapW - 1);
					int sourceVz = Math.Clamp(minZ + vz, 0, mapD - 1);
					splatMap[vx, vz] = _terrainSplatMap[sourceVx, sourceVz];
				}
			}
		}

		if (_terrainCliffSplatMap != null && cliffSplatMap != null)
		{
			int mapCW = _terrainCliffSplatMap.GetLength(0);
			int mapCD = _terrainCliffSplatMap.GetLength(1);
			for (int vz = 0; vz <= selDepth; vz++)
			{
				for (int vx = 0; vx <= selWidth; vx++)
				{
					int sourceVx = Math.Clamp(minX + vx, 0, mapCW - 1);
					int sourceVz = Math.Clamp(minZ + vz, 0, mapCD - 1);
					cliffSplatMap[vx, vz] = _terrainCliffSplatMap[sourceVx, sourceVz];
				}
			}
		}

		_copiedArea = new CopiedAreaTemplate
		{
			Width = selWidth,
			Depth = selDepth,
			AnchorTileX = selWidth / 2,
			AnchorTileZ = selDepth / 2,
			SourceMinX = minX,
			SourceMinZ = minZ,
			SourceMaxX = maxX,
			SourceMaxZ = maxZ,
			Cells = copiedCells,
			SplatMap = splatMap,
			CliffSplatMap = cliffSplatMap,
			Pathing = pathing,
			Entities = entities,
			Mask = mask
		};
	}

	public List<CopiedEntityInfo> BuildCopiedEntityList(
		int minX, int minZ, int maxX, int maxZ,
		IEnumerable<Node3D> sceneChildren,
		bool isSquare = true)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return new List<CopiedEntityInfo>();

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		float minWorldX = (minX - width / 2.0f) * quadSize - quadSize * 0.5f;
		float maxWorldX = (maxX - width / 2.0f) * quadSize + quadSize * 0.5f;
		float minWorldZ = (minZ - depth / 2.0f) * quadSize - quadSize * 0.5f;
		float maxWorldZ = (maxZ - depth / 2.0f) * quadSize + quadSize * 0.5f;
		float centerWorldX = (minWorldX + maxWorldX) * 0.5f;
		float centerWorldZ = (minWorldZ + maxWorldZ) * 0.5f;
		float rxWorld = Math.Max(quadSize * 0.5f, (maxWorldX - minWorldX) * 0.5f);
		float rzWorld = Math.Max(quadSize * 0.5f, (maxWorldZ - minWorldZ) * 0.5f);
		Godot.Vector3 origin = new Godot.Vector3((minX - width / 2.0f) * quadSize, 0.0f, (minZ - depth / 2.0f) * quadSize);

		var entities = new List<CopiedEntityInfo>();
		foreach (var n3d in sceneChildren)
		{
			if (!GodotObject.IsInstanceValid(n3d)) continue;
			Godot.Vector3 pos = n3d.Position;
			if (pos.X >= minWorldX && pos.X <= maxWorldX && pos.Z >= minWorldZ && pos.Z <= maxWorldZ)
			{
				if (!isSquare)
				{
					float ndx = (pos.X - centerWorldX) / rxWorld;
					float ndz = (pos.Z - centerWorldZ) / rzWorld;
					if (ndx * ndx + ndz * ndz > 1.0f) continue;
				}

				if (n3d is Unit3D unit)
				{
					entities.Add(new CopiedEntityInfo
					{
						Type = "unit",
						Id = unit.UnitId,
						RelativePos = pos - origin,
						Rotation = unit.RotationDegrees.Y,
						Scale = unit.Scale.X,
						IsEnemy = unit.IsEnemy
					});
				}
				else if (n3d is Prop3D prop)
				{
					entities.Add(new CopiedEntityInfo
					{
						Type = "prop",
						Id = prop.PropId,
						RelativePos = pos - origin,
						Rotation = prop.RotationDegrees.Y,
						Scale = prop.Scale.X,
						IsEnemy = false
					});
				}
				else if (n3d is Decal decal)
				{
					string decalId = decal is Decal3D decal3D ? decal3D.DecalId : "logo";
					entities.Add(new CopiedEntityInfo
					{
						Type = "decal",
						Id = decalId,
						RelativePos = pos - origin,
						Rotation = decal.RotationDegrees.Y,
						Scale = decal.Scale.X,
						IsEnemy = false
					});
				}
			}
		}

		if (GameContext.World != null)
		{
			var propQuery = QueryCache.AllPropIdentityAndPositionQuery;
			var world = GameContext.World;
			world.Query(in propQuery, (Entity entity, ref PropIdentity propIdComp, ref Position posComp) =>
			{
				if (GameHost.EntityToProp3D.ContainsKey(entity)) return;

				Godot.Vector3 worldPos = new Godot.Vector3(posComp.Value.X, posComp.Value.Y, posComp.Value.Z);
				if (worldPos.X >= minWorldX && worldPos.X <= maxWorldX && worldPos.Z >= minWorldZ && worldPos.Z <= maxWorldZ)
				{
					if (!isSquare)
					{
						float ndx = (worldPos.X - centerWorldX) / rxWorld;
						float ndz = (worldPos.Z - centerWorldZ) / rzWorld;
						if (ndx * ndx + ndz * ndz > 1.0f) return;
					}

					float rotY = world.Has<RotationY>(entity) ? world.Get<RotationY>(entity).Value : 0f;
					float scale = world.Has<ModelScale>(entity) ? world.Get<ModelScale>(entity).Value : 1f;

					entities.Add(new CopiedEntityInfo
					{
						Type = "prop",
						Id = propIdComp.PropId,
						RelativePos = worldPos - origin,
						Rotation = rotY,
						Scale = scale,
						IsEnemy = false
					});
				}
			});
		}

		return entities;
	}

	public PasteAreaResult BuildPasteAreaResult(
		int startX,
		int startZ,
		bool pasteHeights,
		bool pasteTextures,
		bool pasteEntities,
		bool pastePathing,
		MirrorMode mirrorMode,
		float rotationDegrees,
		PasteReflection pasteReflection = PasteReflection.None)
	{
		var result = new PasteAreaResult();
		result.SpawnRequests = new List<EntitySpawnRequest>();

		if (_copiedArea == null) return result;

		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return result;

		if (_terrainSplatMap == null && GameHost.Instance?.GroundTerrain != null)
		{
			_terrainSplatMap = GameHost.Instance.GroundTerrain.SplatMap;
		}

		if (_terrainCliffSplatMap == null && GameHost.Instance?.GroundTerrain != null)
		{
			_terrainCliffSplatMap = GameHost.Instance.GroundTerrain.CliffSplatMap;
		}

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		int pasteWidth = _copiedArea.Width;
		int pasteDepth = _copiedArea.Depth;
		bool modified = false;
		bool pathingModified = false;

		float r = rotationDegrees % 360.0f;
		if (r < 0) r += 360.0f;
		int rotSteps = (int)Math.Round(r / 90.0f) % 4;
		int targetWidth = (rotSteps == 1 || rotSteps == 3) ? pasteDepth : pasteWidth;
		int targetDepth = (rotSteps == 1 || rotSteps == 3) ? pasteWidth : pasteDepth;

		PasteBlock(startX, startZ, rotSteps, pasteReflection, MirrorMode.None, 0, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);

		if (mirrorMode != MirrorMode.None)
		{
			Godot.Vector3 centerPos = new Godot.Vector3((startX + targetWidth / 2.0f - width / 2.0f) * quadSize, 0, (startZ + targetDepth / 2.0f - depth / 2.0f) * quadSize);
			var transforms = GetMirroredTransforms(centerPos, 0.0f, mirrorMode);
			if (mirrorMode == MirrorMode.Horizontal)
			{
				if (transforms.Count > 0)
				{
					var (rcx, rcz) = WorldPosToCellCoords(transforms[0].Position);
					int rStartX = rcx - targetWidth / 2;
					int rStartZ = rcz - targetDepth / 2;
					PasteBlock(rStartX, rStartZ, rotSteps, pasteReflection, MirrorMode.Horizontal, 0, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);
				}
			}
			else if (mirrorMode == MirrorMode.Vertical)
			{
				if (transforms.Count > 0)
				{
					var (rcx, rcz) = WorldPosToCellCoords(transforms[0].Position);
					int rStartX = rcx - targetWidth / 2;
					int rStartZ = rcz - targetDepth / 2;
					PasteBlock(rStartX, rStartZ, rotSteps, pasteReflection, MirrorMode.Vertical, 0, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);
				}
			}
			else if (mirrorMode == MirrorMode.Both)
			{
				if (transforms.Count >= 3)
				{
					var (rcx0, rcz0) = WorldPosToCellCoords(transforms[0].Position);
					int rStartX0 = rcx0 - targetWidth / 2;
					int rStartZ0 = rcz0 - targetDepth / 2;
					PasteBlock(rStartX0, rStartZ0, rotSteps, pasteReflection, MirrorMode.Horizontal, 0, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);

					var (rcx1, rcz1) = WorldPosToCellCoords(transforms[1].Position);
					int rStartX1 = rcx1 - targetWidth / 2;
					int rStartZ1 = rcz1 - targetDepth / 2;
					PasteBlock(rStartX1, rStartZ1, rotSteps, pasteReflection, MirrorMode.Vertical, 0, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);

					var (rcx2, rcz2) = WorldPosToCellCoords(transforms[2].Position);
					int rStartX2 = rcx2 - targetWidth / 2;
					int rStartZ2 = rcz2 - targetDepth / 2;
					PasteBlock(rStartX2, rStartZ2, rotSteps, pasteReflection, MirrorMode.Both, 0, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);
				}
			}
			else if (mirrorMode == MirrorMode.Rotational)
			{
				foreach (var t in transforms)
				{
					int rotKSteps = ((int)Math.Round(t.Rotation / 90.0f) % 4 + 4) % 4;
					int curTargetWidth = (rotKSteps == 1 || rotKSteps == 3) ? targetDepth : targetWidth;
					int curTargetDepth = (rotKSteps == 1 || rotKSteps == 3) ? targetWidth : targetDepth;
					var (rcx, rcz) = WorldPosToCellCoords(t.Position);
					int rStartX = rcx - curTargetWidth / 2;
					int rStartZ = rcz - curTargetDepth / 2;
					PasteBlock(rStartX, rStartZ, rotSteps, pasteReflection, MirrorMode.None, rotKSteps, width, depth, pasteHeights, pasteTextures, pastePathing, ref terrain, ref modified, ref pathingModified);
				}
			}
		}

		if (modified && pasteHeights)
		{
			SanitizeCornerHeights(ref terrain);
		}

		if (modified && pasteTextures)
		{
			AlignSplatMapSlots(0, 0, width, depth);
		}

		result.TerrainModified = modified;
		result.HeightsModified = pasteHeights && modified;
		result.PathingModified = pathingModified;

		if (pasteEntities)
		{
			Godot.Vector3 pasteCenter = new Godot.Vector3((startX + (targetWidth - 1) / 2.0f - width / 2.0f) * quadSize, 0, (startZ + (targetDepth - 1) / 2.0f - depth / 2.0f) * quadSize);

			float rad = rotationDegrees * Mathf.Pi / 180.0f;
			float cosR = Mathf.Cos(rad);
			float sinR = Mathf.Sin(rad);

			Godot.Vector3 originalCenterOffset = new Godot.Vector3((pasteWidth - 1) / 2.0f * quadSize, 0, (pasteDepth - 1) / 2.0f * quadSize);

			foreach (var ent in _copiedArea.Entities)
			{
				Godot.Vector3 relPos = ent.RelativePos;
				float entRot = ent.Rotation;

				if (pasteReflection == PasteReflection.Horizontal)
				{
					relPos = new Godot.Vector3((pasteWidth - 1) * quadSize - relPos.X, relPos.Y, relPos.Z);
					entRot = -entRot;
				}
				else if (pasteReflection == PasteReflection.Vertical)
				{
					relPos = new Godot.Vector3(relPos.X, relPos.Y, (pasteDepth - 1) * quadSize - relPos.Z);
					entRot = 180.0f - entRot;
				}

				Godot.Vector3 relativeToCenter = relPos - originalCenterOffset;

				float rx = relativeToCenter.X * cosR - relativeToCenter.Z * sinR;
				float rz = relativeToCenter.X * sinR + relativeToCenter.Z * cosR;

				Godot.Vector3 rotatedRelative = new Godot.Vector3(rx, 0, rz);
				Godot.Vector3 destPos = pasteCenter + rotatedRelative;

				destPos.Y = GetTerrainHeightAt(destPos);

				float finalRot = entRot - rotationDegrees;

				result.SpawnRequests.Add(new EntitySpawnRequest
				{
					Type = ent.Type,
					Id = ent.Id,
					Position = destPos,
					Rotation = finalRot,
					Scale = ent.Scale,
					IsEnemy = ent.IsEnemy
				});

				AddMirroredRequests(result.SpawnRequests, ent.Type, ent.Id, destPos, finalRot, ent.Scale, ent.IsEnemy, mirrorMode);
			}
		}

		return result;
	}

	private void ProcessEraseHeightsAndTextures(int minX, int minZ, int selWidth, int selDepth, bool isSquare, float selCenterX, float selCenterZ, float rx, float rz, int width, int depth, bool pasteHeights, bool pasteTextures, bool pastePathing, ref TerrainState terrain, ref bool terrainModified, ref bool pathingModified)
	{
		for (int sz = 0; sz < selDepth; sz++)
		{
			for (int sx = 0; sx < selWidth; sx++)
			{
				if (!isSquare)
				{
					float cellCenterX = minX + sx + 0.5f;
					float cellCenterZ = minZ + sz + 0.5f;
					float ndx = (cellCenterX - selCenterX) / rx;
					float ndz = (cellCenterZ - selCenterZ) / rz;
					if (ndx * ndx + ndz * ndz > 1.05f) continue;
				}

				int targetX = minX + sx;
				int targetZ = minZ + sz;
				if (targetX >= 0 && targetX < width && targetZ >= 0 && targetZ < depth)
				{
					if (pasteHeights && terrain.Cells != null)
					{
						SetGridNodeHeight(ref terrain, targetX, targetZ, 0f);
						SetGridNodeHeight(ref terrain, targetX + 1, targetZ, 0f);
						SetGridNodeHeight(ref terrain, targetX + 1, targetZ + 1, 0f);
						SetGridNodeHeight(ref terrain, targetX, targetZ + 1, 0f);
						terrain.Cells[targetX, targetZ] = default;
					}

					if (pasteTextures)
					{
						var eraseSplat = new TerrainSplatWeights { Index0 = 0, Weight0 = 1.0f };
						SetGridNodeSplat(targetX, targetZ, in eraseSplat, in eraseSplat, in eraseSplat, in eraseSplat);
						var eraseCliff = new TerrainSplatWeights { Index0 = 1, Weight0 = 1.0f };
						SetGridNodeCliffSplat(targetX, targetZ, in eraseCliff, in eraseCliff, in eraseCliff, in eraseCliff);
					}

					if (pastePathing && terrain.PathingCodes != null && terrain.Cells != null)
					{
						terrain.PathingCodes[targetX, targetZ] = EditableTerrain.GetDefaultPathingCode(terrain.Cells[targetX, targetZ]);
						pathingModified = true;
					}

					terrainModified = true;
				}
			}
		}
	}

	private void ProcessEraseEntities(IEnumerable<Node3D> sceneChildren, Node3D previewNode, bool isSquare, float selCenterX, float selCenterZ, float rx, float rz, float quadSize, List<Node3D> nodesToDelete)
	{
		foreach (var n in sceneChildren)
		{
			if (n == previewNode || !n.IsInsideTree()) continue;
			if (n is Unit3D or Prop3D or Decal3D or ProceduralVfxInstance3D)
			{
				var pos = n.GlobalPosition;
				bool inBounds = false;
				if (isSquare)
				{
					float dx = Mathf.Abs(pos.X - selCenterX);
					float dz = Mathf.Abs(pos.Z - selCenterZ);
					inBounds = (dx <= rx * quadSize && dz <= rz * quadSize);
				}
				else
				{
					float dx = (pos.X - selCenterX * quadSize) / (rx * quadSize);
					float dz = (pos.Z - selCenterZ * quadSize) / (rz * quadSize);
					inBounds = (dx * dx + dz * dz <= 1.0f);
				}

				if (inBounds) nodesToDelete.Add(n);
			}
		}
	}

	public EraseAreaResult BuildEraseAreaResult(
		int minX, int minZ, int maxX, int maxZ,
		bool pasteHeights,
		bool pasteTextures,
		bool pasteEntities,
		bool pastePathing,
		IEnumerable<Node3D> sceneChildren,
		Node3D previewNode,
		bool isSquare = true)
	{
		var result = new EraseAreaResult { NodesToDelete = new List<Node3D>() };
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return result;

		EnsureSplatMapsInitialized();

		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
		bool terrainModified = false;
		bool pathingModified = false;

		if (pasteHeights || pasteTextures || (pastePathing && terrain.PathingCodes != null))
		{
			ProcessTerrainErase(ref terrain, minX, minZ, maxX, maxZ, selWidth, selDepth, isSquare, pasteHeights, pasteTextures, pastePathing, ref terrainModified, ref pathingModified);
		}

		result.TerrainModified = terrainModified;
		result.HeightsModified = pasteHeights && terrainModified;
		result.PathingModified = pathingModified;

		if (pasteEntities)
		{
			ProcessEntityErase(minX, minZ, maxX, maxZ, terrain.Width, terrain.Depth, terrain.QuadSize, isSquare, sceneChildren, previewNode, result.NodesToDelete);
		}

		return result;
	}


	private void ProcessTerrainErase(ref TerrainState terrain, int minX, int minZ, int maxX, int maxZ, int selWidth, int selDepth, bool isSquare, bool pasteHeights, bool pasteTextures, bool pastePathing, ref bool terrainModified, ref bool pathingModified)
	{
		float selCenterX = (minX + maxX) * 0.5f;
		float selCenterZ = (minZ + maxZ) * 0.5f;
		float rx = Math.Max(0.5f, (maxX - minX) * 0.5f);
		float rz = Math.Max(0.5f, (maxZ - minZ) * 0.5f);

		for (int sz = 0; sz < selDepth; sz++)
		{
			for (int sx = 0; sx < selWidth; sx++)
			{
				if (!IsPointInEllipse(minX + sx + 0.5f, minZ + sz + 0.5f, selCenterX, selCenterZ, rx, rz, isSquare, 1.05f)) continue;
				ProcessSingleEraseCell(ref terrain, minX + sx, minZ + sz, pasteHeights, pasteTextures, pastePathing, ref terrainModified, ref pathingModified);
			}
		}

		if (terrainModified && pasteHeights) SanitizeCornerHeights(ref terrain);
		if (terrainModified && pasteTextures) AlignSplatMapSlots(minX - 2, minZ - 2, maxX + 2, maxZ + 2);
	}

	private void ProcessSingleEraseCell(ref TerrainState terrain, int targetX, int targetZ, bool pasteHeights, bool pasteTextures, bool pastePathing, ref bool terrainModified, ref bool pathingModified)
	{
		if (targetX < 0 || targetX >= terrain.Width || targetZ < 0 || targetZ >= terrain.Depth) return;

		if (pasteHeights && terrain.Cells != null)
		{
			SetGridNodeHeight(ref terrain, targetX, targetZ, 0f);
			SetGridNodeHeight(ref terrain, targetX + 1, targetZ, 0f);
			SetGridNodeHeight(ref terrain, targetX + 1, targetZ + 1, 0f);
			SetGridNodeHeight(ref terrain, targetX, targetZ + 1, 0f);
			terrain.Cells[targetX, targetZ] = default;
		}

		if (pasteTextures)
		{
			var defaultGround = TerrainSplatWeights.CreateSolid(0);
			var defaultCliff = TerrainSplatWeights.CreateSolid(1);
			SetGridNodeSplat(targetX, targetZ, in defaultGround, in defaultGround, in defaultGround, in defaultGround);
			SetGridNodeCliffSplat(targetX, targetZ, in defaultCliff, in defaultCliff, in defaultCliff, in defaultCliff);
		}

		if (pastePathing && terrain.PathingCodes != null)
		{
			terrain.PathingCodes[targetX, targetZ] = EditableTerrain.PATHING_GROUND | EditableTerrain.PATHING_FLYING;
			pathingModified = true;
		}

		terrainModified = true;
	}

	private void ProcessEntityErase(int minX, int minZ, int maxX, int maxZ, int width, int depth, float quadSize, bool isSquare, IEnumerable<Node3D> sceneChildren, Node3D previewNode, List<Node3D> nodesToDelete)
	{
		float minWorldX = (minX - width / 2.0f) * quadSize - quadSize * 0.5f;
		float maxWorldX = (maxX - width / 2.0f) * quadSize + quadSize * 0.5f;
		float minWorldZ = (minZ - depth / 2.0f) * quadSize - quadSize * 0.5f;
		float maxWorldZ = (maxZ - depth / 2.0f) * quadSize + quadSize * 0.5f;
		float centerWorldX = (minWorldX + maxWorldX) * 0.5f;
		float centerWorldZ = (minWorldZ + maxWorldZ) * 0.5f;
		float rxWorld = Math.Max(quadSize * 0.5f, (maxWorldX - minWorldX) * 0.5f);
		float rzWorld = Math.Max(quadSize * 0.5f, (maxWorldZ - minWorldZ) * 0.5f);

		foreach (var n3d in sceneChildren)
		{
			if (!GodotObject.IsInstanceValid(n3d) || n3d == previewNode) continue;
			if (n3d.Position.X >= minWorldX && n3d.Position.X <= maxWorldX && n3d.Position.Z >= minWorldZ && n3d.Position.Z <= maxWorldZ)
			{
				if (!IsPointInEllipse(n3d.Position.X, n3d.Position.Z, centerWorldX, centerWorldZ, rxWorld, rzWorld, isSquare, 1.0f)) continue;
				if (n3d is Unit3D || n3d is Prop3D || n3d is Decal) nodesToDelete.Add(n3d);
			}
		}

		ProcessEcsEntityErase(minWorldX, maxWorldX, minWorldZ, maxWorldZ, centerWorldX, centerWorldZ, rxWorld, rzWorld, isSquare);
	}

	private void ProcessEcsEntityErase(float minWorldX, float maxWorldX, float minWorldZ, float maxWorldZ, float centerWorldX, float centerWorldZ, float rxWorld, float rzWorld, bool isSquare)
	{
		var world = GameContext.World;
		if (world == null) return;

		var staticPropsToDestroy = new List<(Entity Entity, string PropId)>();
		var propQuery = QueryCache.AllPropIdentityAndPositionQuery;
		world.Query(in propQuery, (Entity entity, ref PropIdentity propIdComp, ref Position posComp) =>
		{
			if (GameHost.EntityToProp3D.ContainsKey(entity)) return;
			if (posComp.Value.X >= minWorldX && posComp.Value.X <= maxWorldX && posComp.Value.Z >= minWorldZ && posComp.Value.Z <= maxWorldZ)
			{
				if (!IsPointInEllipse(posComp.Value.X, posComp.Value.Z, centerWorldX, centerWorldZ, rxWorld, rzWorld, isSquare, 1.0f)) return;
				staticPropsToDestroy.Add((entity, propIdComp.PropId));
			}
		});

		foreach (var sp in staticPropsToDestroy)
		{
			if (world.IsAlive(sp.Entity)) world.Destroy(sp.Entity);
			PropMultiMeshManager.Instance?.MarkDirty(sp.PropId);
		}
	}

	private bool IsPointInEllipse(float x, float z, float centerX, float centerZ, float rx, float rz, bool isSquare, float threshold)
	{
		if (isSquare) return true;
		float ndx = (x - centerX) / rx;
		float ndz = (z - centerZ) / rz;
		return ndx * ndx + ndz * ndz <= threshold;
	}

	private int GetDominantTextureIndex(TerrainSplatWeights splat)
	{
		return splat.GetDominantIndex();
	}

	private void ProcessFloodFillNeighbor(int currX, int currZ, int nextX, int nextZ, int splatW, int splatD, bool[,] visited, TerrainSplatWeights[,] splatBefore, int startDominantIndex, bool isCliff, ref TerrainState terrain, Queue<(int x, int z)> queue)
	{
		if (nextX < 0 || nextX >= splatW || nextZ < 0 || nextZ >= splatD) return;
		if (visited[nextX, nextZ]) return;
		if (splatBefore[nextX, nextZ].GetDominantIndex() != startDominantIndex) return;

		if (!isCliff)
		{
			float hCurrent = GetGridNodeHeight(in terrain, currX, currZ);
			float hNext = GetGridNodeHeight(in terrain, nextX, nextZ);
			if (Mathf.Abs(hNext - hCurrent) >= 1.0f) return;
		}

		visited[nextX, nextZ] = true;
		queue.Enqueue((nextX, nextZ));
	}

	private List<Vector2I> GetFloodFillCells(Godot.Vector3 clickPos, TerrainSplatWeights[,] splatBefore, int width, int depth, float quadSize, bool[,] visited, bool isCliff)
	{
		ref var terrain = ref GetTerrainState();
		var cells = new List<Vector2I>();

		int splatW = splatBefore.GetLength(0);
		int splatD = splatBefore.GetLength(1);

		float startFx = clickPos.X / quadSize + width / 2.0f;
		float startFz = clickPos.Z / quadSize + depth / 2.0f;
		int clickX = Mathf.Clamp((int)Math.Round(startFx), 0, splatW - 1);
		int clickZ = Mathf.Clamp((int)Math.Round(startFz), 0, splatD - 1);
		int startDominantIndex = splatBefore[clickX, clickZ].GetDominantIndex();

		var queue = new Queue<(int x, int z)>();
		if (!visited[clickX, clickZ])
		{
			queue.Enqueue((clickX, clickZ));
			visited[clickX, clickZ] = true;
		}

		while (queue.Count > 0)
		{
			var (currX, currZ) = queue.Dequeue();
			cells.Add(new Vector2I(currX, currZ));

			int[] dx = { 0, 0, -1, 1 };
			int[] dz = { -1, 1, 0, 0 };
			for (int i = 0; i < 4; i++)
			{
				ProcessFloodFillNeighbor(currX, currZ, currX + dx[i], currZ + dz[i], splatW, splatD, visited, splatBefore, startDominantIndex, isCliff, ref terrain, queue);
			}
		}

		return cells;
	}

	private List<Vector2I> GetFloodFillArea(
		Godot.Vector3 clickPos,
		TerrainSplatWeights[,] splatBefore,
		int width,
		int depth,
		float quadSize,
		bool[,] visited,
		MirrorMode mirrorMode,
		bool isCliff,
		Func<int, int, bool> shouldFillCell)
	{
		var cells = new List<Vector2I>();
		int splatW = splatBefore.GetLength(0);
		int splatD = splatBefore.GetLength(1);

		float startFx = clickPos.X / quadSize + width / 2.0f;
		float startFz = clickPos.Z / quadSize + depth / 2.0f;
		int startX = Mathf.Clamp((int)Math.Round(startFx), 0, splatW - 1);
		int startZ = Mathf.Clamp((int)Math.Round(startFz), 0, splatD - 1);

		if (shouldFillCell(startX, startZ))
		{
			cells.AddRange(GetFloodFillCells(clickPos, splatBefore, width, depth, quadSize, visited, isCliff));
		}

		if (mirrorMode != MirrorMode.None)
		{
			var mirrors = GetMirroredPositions(clickPos, mirrorMode);
			foreach (var m in mirrors)
			{
				float mFx = m.X / quadSize + width / 2.0f;
				float mFz = m.Z / quadSize + depth / 2.0f;
				int mX = Mathf.Clamp((int)Math.Round(mFx), 0, splatW - 1);
				int mZ = Mathf.Clamp((int)Math.Round(mFz), 0, splatD - 1);
				if (shouldFillCell(mX, mZ))
				{
					cells.AddRange(GetFloodFillCells(m, splatBefore, width, depth, quadSize, visited, isCliff));
				}
			}
		}

		return cells;
	}

	public (float[,]? Heights, TerrainSplatWeights[,]? SplatMap, bool IsCliff) PerformFloodFill(
		Godot.Vector3 clickPos,
		int fillTextureIndex,
		int cliffTextureIndex,
		MirrorMode mirrorMode,
		bool isCliff = false)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return (null, null, isCliff);

		if (_terrainSplatMap == null && GameHost.Instance?.GroundTerrain != null)
		{
			_terrainSplatMap = GameHost.Instance.GroundTerrain.SplatMap;
		}

		if (_terrainCliffSplatMap == null && GameHost.Instance?.GroundTerrain != null)
		{
			_terrainCliffSplatMap = GameHost.Instance.GroundTerrain.CliffSplatMap;
		}

		if (_terrainSplatMap == null) return (null, null, isCliff);

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		TerrainSplatWeights[,] targetSplatMap = (isCliff && _terrainCliffSplatMap != null) ? _terrainCliffSplatMap : _terrainSplatMap;
		int targetTextureIndex = isCliff ? cliffTextureIndex : fillTextureIndex;

		int splatW = targetSplatMap.GetLength(0);
		int splatD = targetSplatMap.GetLength(1);

		var splatBefore = (TerrainSplatWeights[,])targetSplatMap.Clone();
		var visited = new bool[splatW, splatD];

		var cells = GetFloodFillArea(
			clickPos,
			splatBefore,
			width,
			depth,
			quadSize,
			visited,
			mirrorMode,
			isCliff,
			(x, z) => !splatBefore[x, z].IsSolid(targetTextureIndex)
		);

		if (cells.Count == 0) return (null, null, isCliff);

		foreach (var cell in cells)
		{
			targetSplatMap[cell.X, cell.Y] = TerrainSplatWeights.CreateSolid(targetTextureIndex);
		}

		int minGridX = splatW;
		int maxGridX = 0;
		int minGridZ = splatD;
		int maxGridZ = 0;
		foreach (var cell in cells)
		{
			if (cell.X < minGridX) minGridX = cell.X;
			if (cell.X > maxGridX) maxGridX = cell.X;
			if (cell.Y < minGridZ) minGridZ = cell.Y;
			if (cell.Y > maxGridZ) maxGridZ = cell.Y;
		}

		AlignSplatMapSlots(minGridX - 2, minGridZ - 2, maxGridX + 2, maxGridZ + 2);

		return (null, (TerrainSplatWeights[,])targetSplatMap.Clone(), isCliff);
	}

	private List<Vector2I> GetFloodFillPathingCells(
		Godot.Vector3 clickPos, TerrainSplatWeights[,] splatBefore, int width, int depth, float quadSize, bool[,] visited)
	{
		ref var terrain = ref GetTerrainState();
		var cells = new List<Vector2I>();

		int clickX = Mathf.Clamp((int)Math.Floor(clickPos.X / quadSize + width / 2.0f), 0, width - 1);
		int clickZ = Mathf.Clamp((int)Math.Floor(clickPos.Z / quadSize + depth / 2.0f), 0, depth - 1);

		int startDominantIndex = GetStartDominantIndex(clickX, clickZ, splatBefore);
		WaterType startWaterType = terrain.Cells != null ? terrain.Cells[clickX, clickZ].WaterMode : WaterType.None;
		float startHeight = terrain.Cells != null ? terrain.Cells[clickX, clickZ].CenterHeight : clickPos.Y;

		var queue = new Queue<(int x, int z)>();
		if (!visited[clickX, clickZ])
		{
			queue.Enqueue((clickX, clickZ));
			visited[clickX, clickZ] = true;
		}

		ProcessFloodFillQueue(queue, cells, width, depth, visited, splatBefore, startDominantIndex, terrain.Cells, startWaterType, startHeight);

		return cells;
	}

	private int GetStartDominantIndex(int clickX, int clickZ, TerrainSplatWeights[,]? splatBefore)
	{
		if (splatBefore == null) return 0;
		int splatW = splatBefore.GetLength(0);
		int splatD = splatBefore.GetLength(1);
		return (clickX < splatW && clickZ < splatD) ? splatBefore[clickX, clickZ].GetDominantIndex() : 0;
	}

	private void ProcessFloodFillQueue(Queue<(int x, int z)> queue, List<Vector2I> cells, int width, int depth, bool[,] visited, TerrainSplatWeights[,]? splatBefore, int startDominantIndex, TerrainCell[,]? terrainCells, WaterType startWaterType, float startHeight)
	{
		int[] dx = { 0, 0, -1, 1 };
		int[] dz = { -1, 1, 0, 0 };
		int splatW = splatBefore != null ? splatBefore.GetLength(0) : 0;
		int splatD = splatBefore != null ? splatBefore.GetLength(1) : 0;

		while (queue.Count > 0)
		{
			var (currX, currZ) = queue.Dequeue();
			cells.Add(new Vector2I(currX, currZ));
			ProcessFloodFillNeighbors(currX, currZ, dx, dz, queue, width, depth, visited, splatBefore, splatW, splatD, startDominantIndex, terrainCells, startWaterType, startHeight);
		}
	}

	private void ProcessFloodFillNeighbors(int currX, int currZ, int[] dx, int[] dz, Queue<(int x, int z)> queue, int width, int depth, bool[,] visited, TerrainSplatWeights[,]? splatBefore, int splatW, int splatD, int startDominantIndex, TerrainCell[,]? terrainCells, WaterType startWaterType, float startHeight)
	{
		for (int i = 0; i < 4; i++)
		{
			int nextX = currX + dx[i];
			int nextZ = currZ + dz[i];
			if (nextX < 0 || nextX >= width || nextZ < 0 || nextZ >= depth || visited[nextX, nextZ]) continue;
			if (!ShouldIncludeNeighbor(nextX, nextZ, splatBefore, splatW, splatD, startDominantIndex, terrainCells, startWaterType, startHeight)) continue;

			visited[nextX, nextZ] = true;
			queue.Enqueue((nextX, nextZ));
		}
	}

	private bool ShouldIncludeNeighbor(int nextX, int nextZ, TerrainSplatWeights[,]? splatBefore, int splatW, int splatD, int startDominantIndex, TerrainCell[,]? terrainCells, WaterType startWaterType, float startHeight)
	{
		if (splatBefore != null && nextX < splatW && nextZ < splatD)
		{
			if (splatBefore[nextX, nextZ].GetDominantIndex() != startDominantIndex) return false;
		}

		if (terrainCells != null)
		{
			if (startWaterType != terrainCells[nextX, nextZ].WaterMode) return false;
			float hNext = terrainCells[nextX, nextZ].CenterHeight;
			if (Mathf.Abs(hNext - startHeight) >= 3.0f) return false;
		}

		return true;
	}

	private List<Vector2I> GetFloodFillPathingArea(
		Godot.Vector3 clickPos,
		TerrainSplatWeights[,] splatBefore,
		int width,
		int depth,
		float quadSize,
		bool[,] visited,
		MirrorMode mirrorMode,
		Func<int, int, bool> shouldFillCell)
	{
		var cells = new List<Vector2I>();

		float startFx = clickPos.X / quadSize + width / 2.0f;
		float startFz = clickPos.Z / quadSize + depth / 2.0f;
		int startX = Mathf.Clamp((int)Math.Floor(startFx), 0, width - 1);
		int startZ = Mathf.Clamp((int)Math.Floor(startFz), 0, depth - 1);

		if (shouldFillCell(startX, startZ))
		{
			cells.AddRange(GetFloodFillPathingCells(clickPos, splatBefore, width, depth, quadSize, visited));
		}

		if (mirrorMode != MirrorMode.None)
		{
			var mirrors = GetMirroredPositions(clickPos, mirrorMode);
			foreach (var m in mirrors)
			{
				float mFx = m.X / quadSize + width / 2.0f;
				float mFz = m.Z / quadSize + depth / 2.0f;
				int mX = Mathf.Clamp((int)Math.Floor(mFx), 0, width - 1);
				int mZ = Mathf.Clamp((int)Math.Floor(mFz), 0, depth - 1);
				if (shouldFillCell(mX, mZ))
				{
					cells.AddRange(GetFloodFillPathingCells(m, splatBefore, width, depth, quadSize, visited));
				}
			}
		}

		return cells;
	}

	private bool CheckPathingCondition(int[,] pathingBefore, int x, int z, int pathingMask, bool pathingAdd)
	{
		int current = pathingBefore[x, z] & pathingMask;
		if (pathingAdd) return current != pathingMask;
		return current != 0;
	}

	public (int[,]? Before, int[,]? After) PerformFloodFillPathing(Godot.Vector3 clickPos, int pathingMask, bool pathingAdd, MirrorMode mirrorMode)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null || terrain.PathingCodes == null) return (null, null);

		if (_terrainSplatMap == null && GameHost.Instance?.GroundTerrain != null)
		{
			_terrainSplatMap = GameHost.Instance.GroundTerrain.SplatMap;
		}

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;
		var splatBefore = _terrainSplatMap != null ? (TerrainSplatWeights[,])_terrainSplatMap.Clone() : null;
		var pathingBefore = (int[,])terrain.PathingCodes.Clone();
		var visited = new bool[width, depth];

		var pathingCodes = terrain.PathingCodes;

		var cells = GetFloodFillPathingArea(
			clickPos,
			splatBefore,
			width,
			depth,
			quadSize,
			visited,
			mirrorMode,
			(x, z) => CheckPathingCondition(pathingBefore, x, z, pathingMask, pathingAdd)
		);

		if (cells.Count == 0) return (null, null);

		foreach (var cell in cells)
		{
			if (pathingAdd)
				pathingCodes[cell.X, cell.Y] |= pathingMask;
			else
				pathingCodes[cell.X, cell.Y] &= ~pathingMask;
		}

		return (pathingBefore, (int[,])pathingCodes.Clone());
	}

	private void ProcessWaterFloodFillNeighbor(TerrainCell currCell, TerrainCell nextCell, int nextX, int nextZ, bool isRemoveAction, float stepCliffThreshold, float baseWaterLevel, bool[,] visited, Queue<(int x, int z)> queue)
	{
		if (isRemoveAction)
		{
			if (nextCell.WaterMode != WaterType.None)
			{
				visited[nextX, nextZ] = true;
				queue.Enqueue((nextX, nextZ));
			}

			return;
		}

		float currHeight = currCell.CenterHeight;
		float nextHeight = nextCell.CenterHeight;
		float deltaH = nextHeight - currHeight;

		if (deltaH <= 0.05f)
		{
			visited[nextX, nextZ] = true;
			queue.Enqueue((nextX, nextZ));
			return;
		}

		float nextMaxH = Mathf.Max(Mathf.Max(nextCell.Y_NW, nextCell.Y_NE), Mathf.Max(nextCell.Y_SW, nextCell.Y_SE));
		float nextMinH = Mathf.Min(Mathf.Min(nextCell.Y_NW, nextCell.Y_NE), Mathf.Min(nextCell.Y_SW, nextCell.Y_SE));
		float nextCellInternalSpan = nextMaxH - nextMinH;

		bool isCliffStep = (nextCell.MacroTier - currCell.MacroTier >= 1) || deltaH >= stepCliffThreshold || nextCellInternalSpan >= stepCliffThreshold;
		if (!isCliffStep)
		{
			visited[nextX, nextZ] = true;
			queue.Enqueue((nextX, nextZ));
			return;
		}

		float obstacleHeight = Mathf.Max(nextMaxH, (float)nextCell.MacroTier * TerrainCell.TIER_HEIGHT);
		if (baseWaterLevel >= obstacleHeight)
		{
			visited[nextX, nextZ] = true;
			queue.Enqueue((nextX, nextZ));
		}
	}

	private List<Vector2I> GetWaterFloodFillCells(
		Godot.Vector3 clickPos,
		TerrainCell[,] cellsBefore,
		int width,
		int depth,
		float quadSize,
		bool[,] visited,
		float waterHeight,
		bool isRemoveAction = false)
	{
		var resultCells = new List<Vector2I>();

		int clickX = Mathf.Clamp((int)Math.Floor(clickPos.X / quadSize + width / 2.0f), 0, width - 1);
		int clickZ = Mathf.Clamp((int)Math.Floor(clickPos.Z / quadSize + depth / 2.0f), 0, depth - 1);

		if (isRemoveAction && cellsBefore[clickX, clickZ].WaterMode == WaterType.None) return resultCells;

		var queue = new Queue<(int x, int z)>();
		if (!visited[clickX, clickZ])
		{
			queue.Enqueue((clickX, clickZ));
			visited[clickX, clickZ] = true;
		}

		float baseWaterLevel = CalculateBaseWaterLevel(clickPos, cellsBefore[clickX, clickZ], waterHeight);
		float stepCliffThreshold = TerrainCell.TIER_HEIGHT * 0.70f;

		ProcessWaterFloodFillQueue(queue, resultCells, cellsBefore, width, depth, visited, isRemoveAction, stepCliffThreshold, baseWaterLevel);

		return resultCells;
	}

	private float CalculateBaseWaterLevel(Godot.Vector3 clickPos, TerrainCell startCell, float waterHeight)
	{
		float effectiveWaterHeight = waterHeight > 0.001f ? waterHeight : 0.9f;
		float startTerrainHeight = float.IsNaN(clickPos.Y) || float.IsInfinity(clickPos.Y) ? startCell.CenterHeight : clickPos.Y;
		return startTerrainHeight + effectiveWaterHeight;
	}

	private void ProcessWaterFloodFillQueue(Queue<(int x, int z)> queue, List<Vector2I> resultCells, TerrainCell[,] cellsBefore, int width, int depth, bool[,] visited, bool isRemoveAction, float stepCliffThreshold, float baseWaterLevel)
	{
		int[] dx = { 0, 0, -1, 1 };
		int[] dz = { -1, 1, 0, 0 };

		while (queue.Count > 0)
		{
			var (currX, currZ) = queue.Dequeue();
			resultCells.Add(new Vector2I(currX, currZ));
			var currCell = cellsBefore[currX, currZ];

			for (int i = 0; i < 4; i++)
			{
				int nextX = currX + dx[i];
				int nextZ = currZ + dz[i];

				if (nextX >= 0 && nextX < width && nextZ >= 0 && nextZ < depth && !visited[nextX, nextZ])
				{
					var nextCell = cellsBefore[nextX, nextZ];
					ProcessWaterFloodFillNeighbor(currCell, nextCell, nextX, nextZ, isRemoveAction, stepCliffThreshold, baseWaterLevel, visited, queue);
				}
			}
		}
	}

	private List<Vector2I> GetWaterFloodFillArea(
		Godot.Vector3 clickPos,
		TerrainCell[,] cellsBefore,
		int width,
		int depth,
		float quadSize,
		bool[,] visited,
		MirrorMode mirrorMode,
		float waterHeight,
		bool isRemoveAction = false)
	{
		var areaCells = new List<Vector2I>();

		areaCells.AddRange(GetWaterFloodFillCells(clickPos, cellsBefore, width, depth, quadSize, visited, waterHeight, isRemoveAction));

		if (mirrorMode != MirrorMode.None)
		{
			var mirrors = GetMirroredPositions(clickPos, mirrorMode);
			foreach (var m in mirrors)
			{
				areaCells.AddRange(GetWaterFloodFillCells(m, cellsBefore, width, depth, quadSize, visited, waterHeight, isRemoveAction));
			}
		}

		return areaCells;
	}

	public (TerrainCell[,]? BeforeCells, TerrainCell[,]? AfterCells, int[,]? BeforePathing, int[,]? AfterPathing, bool WasAdded) PerformWaterFloodFill(
		Godot.Vector3 clickPos,
		WaterType activeWaterMode,
		byte activeWaterProfile,
		float waterHeight,
		MirrorMode mirrorMode,
		bool isRemoveAction = false)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null || terrain.PathingCodes == null) return (null, null, null, null, false);

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		var cells = terrain.Cells;

		bool isRemoving = isRemoveAction;
		WaterType targetWaterMode = isRemoving ? WaterType.None : (activeWaterMode != WaterType.None ? activeWaterMode : WaterType.Shallow);
		byte targetWaterProfile = isRemoving ? (byte)0 : activeWaterProfile;
		float targetWaterHeight = isRemoving ? 0f : (waterHeight > 0.001f ? waterHeight : 0.9f);

		var beforeCells = (TerrainCell[,])cells.Clone();
		var beforePathing = (int[,])terrain.PathingCodes.Clone();

		var visited = new bool[width, depth];
		var filledCells = GetWaterFloodFillArea(clickPos, beforeCells, width, depth, quadSize, visited, mirrorMode, waterHeight, isRemoveAction);

		if (filledCells.Count == 0) return (null, null, null, null, false);

		foreach (var cellPos in filledCells)
		{
			int x = cellPos.X;
			int z = cellPos.Y;

			cells[x, z].WaterMode = targetWaterMode;
			cells[x, z].WaterProfileIndex = targetWaterProfile;
			cells[x, z].WaterHeight = targetWaterHeight;

			if (targetWaterMode != WaterType.None)
			{
				var waterProf = RuntimeTerrain.Instance?.GetWaterProfile(targetWaterProfile);
				terrain.PathingCodes[x, z] = waterProf != null ? waterProf.DefaultPathingCode : EditableTerrain.GetDefaultPathingCode(targetWaterMode);
				float wY = (cells[x, z].MacroTier * TerrainCell.TIER_HEIGHT) + (targetWaterHeight > 0.001f ? targetWaterHeight : RuntimeTerrain.WATER_DELTA);
				SpawnWaterProceduralBombing(x, z, targetWaterProfile, wY, quadSize, width, depth);
			}
			else
			{
				ClearWaterProceduralObjects(x, z);
				terrain.PathingCodes[x, z] = EditableTerrain.GetDefaultPathingCode(cells[x, z]);
			}
		}

		var afterCells = (TerrainCell[,])cells.Clone();
		var afterPathing = (int[,])terrain.PathingCodes.Clone();

		return (beforeCells, afterCells, beforePathing, afterPathing, !isRemoving);
	}

	public List<MirroredTransform> GetMirroredTransforms(
		Godot.Vector3 pos,
		float rotation,
		MirrorMode mirrorMode,
		Vector2? pivot = null,
		int? folds = null)
	{
		var list = new List<MirroredTransform>();
		if (mirrorMode == MirrorMode.None) return list;

		Vector2 p = pivot ?? _symmetryPivot;
		int n = folds ?? _symmetryFolds;
		if (n < 2) n = 2;

		float dx = pos.X - p.X;
		float dz = pos.Z - p.Y;

		bool isHoriz = mirrorMode == MirrorMode.Horizontal || mirrorMode == MirrorMode.Both;
		bool isVert = mirrorMode == MirrorMode.Vertical || mirrorMode == MirrorMode.Both;
		if (isHoriz) list.Add(new MirroredTransform { Position = new Godot.Vector3(p.X - dx, pos.Y, p.Y + dz), Rotation = 180.0f - rotation });
		if (isVert) list.Add(new MirroredTransform { Position = new Godot.Vector3(p.X + dx, pos.Y, p.Y - dz), Rotation = -rotation });
		if (mirrorMode == MirrorMode.Both) list.Add(new MirroredTransform { Position = new Godot.Vector3(p.X - dx, pos.Y, p.Y - dz), Rotation = rotation + 180.0f });
		if (mirrorMode == MirrorMode.Rotational)
		{
			for (int k = 1; k < n; k++)
			{
				float angleRad = k * (Mathf.Tau / n);
				float cosA = Mathf.Cos(angleRad);
				float sinA = Mathf.Sin(angleRad);
				float rx = p.X + dx * cosA - dz * sinA;
				float rz = p.Y + dx * sinA + dz * cosA;
				float rotK = (rotation + k * (360.0f / n)) % 360.0f;
				if (rotK < 0) rotK += 360.0f;
				list.Add(new MirroredTransform { Position = new Godot.Vector3(rx, pos.Y, rz), Rotation = rotK });
			}
		}

		return list;
	}

	public (int startX, int startZ, int targetWidth, int targetDepth) GetAnchoredPasteBounds(int targetX, int targetZ, float rotationDegrees, PasteReflection reflection = PasteReflection.None)
	{
		if (_copiedArea == null) return (targetX, targetZ, 0, 0);

		float r = rotationDegrees % 360.0f;
		if (r < 0) r += 360.0f;
		int rotSteps = (int)Math.Round(r / 90.0f) % 4;

		int pasteWidth = _copiedArea.Width;
		int pasteDepth = _copiedArea.Depth;
		int targetWidth = pasteWidth;
		int targetDepth = pasteDepth;
		if (rotSteps == 1 || rotSteps == 3)
		{
			targetWidth = pasteDepth;
			targetDepth = pasteWidth;
		}

		int anchorX = _copiedArea.AnchorTileX;
		int anchorZ = _copiedArea.AnchorTileZ;

		if (reflection == PasteReflection.Horizontal) anchorX = pasteWidth - 1 - anchorX;
		else if (reflection == PasteReflection.Vertical) anchorZ = pasteDepth - 1 - anchorZ;

		int rotAnchorX = anchorX;
		int rotAnchorZ = anchorZ;
		if (rotSteps == 1)
		{
			rotAnchorX = pasteDepth - 1 - anchorZ;
			rotAnchorZ = anchorX;
		}
		else if (rotSteps == 2)
		{
			rotAnchorX = pasteWidth - 1 - anchorX;
			rotAnchorZ = pasteDepth - 1 - anchorZ;
		}
		else if (rotSteps == 3)
		{
			rotAnchorX = anchorZ;
			rotAnchorZ = pasteWidth - 1 - anchorX;
		}

		return (targetX - rotAnchorX, targetZ - rotAnchorZ, targetWidth, targetDepth);
	}

	public (int minX, int minZ, int maxX, int maxZ) GetCurrentSelectionBounds()
	{
		if (_selectionStart == null || _selectionEnd == null) return (0, 0, 0, 0);
		int minX = Mathf.Min(_selectionStart.Value.X, _selectionEnd.Value.X);
		int minZ = Mathf.Min(_selectionStart.Value.Y, _selectionEnd.Value.Y);
		int maxX = Mathf.Max(_selectionStart.Value.X, _selectionEnd.Value.X);
		int maxZ = Mathf.Max(_selectionStart.Value.Y, _selectionEnd.Value.Y);
		return (minX, minZ, maxX, maxZ);
	}

	public (int cx, int cz) WorldPosToCellCoords(Godot.Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;
		float fx = worldPos.X / quadSize + width / 2.0f;
		float fz = worldPos.Z / quadSize + depth / 2.0f;
		int cx = Mathf.Clamp(Mathf.FloorToInt(fx), 0, width - 1);
		int cz = Mathf.Clamp(Mathf.FloorToInt(fz), 0, depth - 1);
		return (cx, cz);
	}

	public Godot.Vector3 SnapToGrid(Godot.Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return worldPos;
		float quadSize = terrain.QuadSize;
		int width = terrain.Width;
		int depth = terrain.Depth;
		float fx = Mathf.Round(worldPos.X / quadSize + width / 2.0f);
		worldPos.X = (Mathf.Clamp(fx, 0, width) - width / 2.0f) * quadSize;
		float fz = Mathf.Round(worldPos.Z / quadSize + depth / 2.0f);
		worldPos.Z = (Mathf.Clamp(fz, 0, depth) - depth / 2.0f) * quadSize;
		return worldPos;
	}

	public Godot.Vector3 SnapToVertex(Godot.Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return worldPos;
		float quadSize = terrain.QuadSize;
		int width = terrain.Width;
		int depth = terrain.Depth;
		float fx = Mathf.Round(worldPos.X / quadSize + width / 2.0f);
		int vx = Mathf.Clamp((int)fx, 0, width);
		float fz = Mathf.Round(worldPos.Z / quadSize + depth / 2.0f);
		int vz = Mathf.Clamp((int)fz, 0, depth);
		float worldX = (vx - width / 2.0f) * quadSize;
		float worldZ = (vz - depth / 2.0f) * quadSize;
		float height = RuntimeTerrain.GetGridNodeHeight(vx, vz, terrain.Cells, width, depth);
		return new Godot.Vector3(worldX, height, worldZ);
	}

	public (int vx, int vz) WorldPosToVertexCoords(Godot.Vector3 worldPos)
	{
		ref var terrain = ref GetTerrainState();
		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;
		float fx = worldPos.X / quadSize + width / 2.0f;
		float fz = worldPos.Z / quadSize + depth / 2.0f;
		int vx = Mathf.Clamp((int)Math.Round(fx), 0, width);
		int vz = Mathf.Clamp((int)Math.Round(fz), 0, depth);
		return (vx, vz);
	}

	private ref TerrainState GetTerrainState()
	{
		var worldQuery = QueryCache.AllTerrainStateQuery;
		Entity worldEntity = Entity.Null;
		GameContext.World.Query(in worldQuery, (Entity entity) => worldEntity = entity);
		if (worldEntity != Entity.Null && GameContext.World.IsAlive(worldEntity))
		{
			return ref GameContext.World.Get<TerrainState>(worldEntity);
		}

		throw new InvalidOperationException("TerrainState entity not found in ECS world.");
	}

	private float GetMinHeightInBrushBoundsInternal(Godot.Vector3 worldPos, float brushRadius = -1f, bool brushIsSquare = false)
	{
		ref var terrain = ref GetTerrainState();
		if (terrain.Cells == null) return GetTerrainHeightAt(worldPos);

		float quadSize = terrain.QuadSize;
		int width = terrain.Width;
		int depth = terrain.Depth;
		float minHeight = float.MaxValue;
		bool foundAny = false;

		for (int z = 0; z <= depth; z++)
		{
			for (int x = 0; x <= width; x++)
			{
				float vx = (x - width / 2.0f) * quadSize;
				float vz = (z - depth / 2.0f) * quadSize;

				bool inBounds = false;
				if (brushRadius < 0)
				{
					inBounds = true;
				}
				else if (brushIsSquare)
				{
					float dx = Mathf.Abs(vx - worldPos.X);
					float dz = Mathf.Abs(vz - worldPos.Z);
					inBounds = dx <= brushRadius && dz <= brushRadius;
				}
				else
				{
					float dist = new Vector2(vx - worldPos.X, vz - worldPos.Z).Length();
					inBounds = dist <= brushRadius;
				}

				if (inBounds)
				{
					float h = GetGridNodeHeight(in terrain, x, z);
					if (h < minHeight)
					{
						minHeight = h;
						foundAny = true;
					}
				}
			}
		}

		return foundAny ? minHeight : GetTerrainHeightAt(worldPos);
	}

	private void AddMirroredRequests(
		List<EntitySpawnRequest> requests,
		string type, string id,
		Godot.Vector3 pos, float rotation, float scale,
		bool isEnemy,
		MirrorMode mirrorMode)
	{
		var mirrored = GetMirroredTransforms(pos, rotation, mirrorMode);
		foreach (var m in mirrored)
		{
			Godot.Vector3 mPos = m.Position;
			mPos.Y = GetTerrainHeightAt(mPos);
			requests.Add(new EntitySpawnRequest { Type = type, Id = id, Position = mPos, Rotation = m.Rotation, Scale = scale, IsEnemy = isEnemy });
		}
	}

	public List<Godot.Vector3> GetMirroredPositions(Godot.Vector3 pos, MirrorMode mirrorMode)
	{
		var list = new List<Godot.Vector3>();
		var transforms = GetMirroredTransforms(pos, 0.0f, mirrorMode);
		foreach (var t in transforms)
		{
			list.Add(t.Position);
		}

		return list;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static TerrainCell RotateCell(in TerrainCell cell, int rotSteps)
	{
		rotSteps = (rotSteps % 4 + 4) % 4;
		switch (rotSteps)
		{
			case 1:
				return new TerrainCell(cell.Y_SW, cell.Y_NW, cell.Y_NE, cell.Y_SE, cell.WaterMode, cell.WaterProfileIndex, cell.WaterHeight);
			case 2:
				return new TerrainCell(cell.Y_SE, cell.Y_SW, cell.Y_NW, cell.Y_NE, cell.WaterMode, cell.WaterProfileIndex, cell.WaterHeight);
			case 3:
				return new TerrainCell(cell.Y_NE, cell.Y_SE, cell.Y_SW, cell.Y_NW, cell.WaterMode, cell.WaterProfileIndex, cell.WaterHeight);
			case 0:
			default:
				return cell;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static TerrainCell MirrorCell(in TerrainCell cell, MirrorMode mode)
	{
		switch (mode)
		{
			case MirrorMode.Horizontal:
				return new TerrainCell(cell.Y_NE, cell.Y_NW, cell.Y_SW, cell.Y_SE, cell.WaterMode, cell.WaterProfileIndex, cell.WaterHeight);
			case MirrorMode.Vertical:
				return new TerrainCell(cell.Y_SW, cell.Y_SE, cell.Y_NE, cell.Y_NW, cell.WaterMode, cell.WaterProfileIndex, cell.WaterHeight);
			case MirrorMode.Both:
				return new TerrainCell(cell.Y_SE, cell.Y_SW, cell.Y_NW, cell.Y_NE, cell.WaterMode, cell.WaterProfileIndex, cell.WaterHeight);
			case MirrorMode.None:
			default:
				return cell;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static (TerrainSplatWeights NW, TerrainSplatWeights NE, TerrainSplatWeights SE, TerrainSplatWeights SW) MirrorSplatQuad(
		in TerrainSplatWeights nw, in TerrainSplatWeights ne, in TerrainSplatWeights se, in TerrainSplatWeights sw, MirrorMode mode)
	{
		switch (mode)
		{
			case MirrorMode.Horizontal:
				return (ne, nw, sw, se);
			case MirrorMode.Vertical:
				return (sw, se, ne, nw);
			case MirrorMode.Both:
				return (se, sw, nw, ne);
			case MirrorMode.None:
			default:
				return (nw, ne, se, sw);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static (TerrainSplatWeights NW, TerrainSplatWeights NE, TerrainSplatWeights SE, TerrainSplatWeights SW) RotateSplatQuad(
		in TerrainSplatWeights nw, in TerrainSplatWeights ne, in TerrainSplatWeights se, in TerrainSplatWeights sw, int rotSteps)
	{
		rotSteps = (rotSteps % 4 + 4) % 4;
		switch (rotSteps)
		{
			case 1:
				return (sw, nw, ne, se);
			case 2:
				return (se, sw, nw, ne);
			case 3:
				return (ne, se, sw, nw);
			case 0:
			default:
				return (nw, ne, se, sw);
		}
	}

	private void TrySetSplat(TerrainSplatWeights[,] map, int x, int z, int w, int d, in TerrainSplatWeights val)
	{
		if (x >= 0 && x < w && z >= 0 && z < d) map[x, z] = val;
	}

	private void SetGridNodeSplat(int gx, int gz, in TerrainSplatWeights nw, in TerrainSplatWeights ne, in TerrainSplatWeights se, in TerrainSplatWeights sw)
	{
		if (_terrainSplatMap == null) return;
		int w = _terrainSplatMap.GetLength(0);
		int d = _terrainSplatMap.GetLength(1);
		TrySetSplat(_terrainSplatMap, gx, gz, w, d, in nw);
		TrySetSplat(_terrainSplatMap, gx + 1, gz, w, d, in ne);
		TrySetSplat(_terrainSplatMap, gx + 1, gz + 1, w, d, in se);
		TrySetSplat(_terrainSplatMap, gx, gz + 1, w, d, in sw);
	}

	private void SetGridNodeCliffSplat(int gx, int gz, in TerrainSplatWeights nw, in TerrainSplatWeights ne, in TerrainSplatWeights se, in TerrainSplatWeights sw)
	{
		if (_terrainCliffSplatMap == null) return;
		int w = _terrainCliffSplatMap.GetLength(0);
		int d = _terrainCliffSplatMap.GetLength(1);
		TrySetSplat(_terrainCliffSplatMap, gx, gz, w, d, in nw);
		TrySetSplat(_terrainCliffSplatMap, gx + 1, gz, w, d, in ne);
		TrySetSplat(_terrainCliffSplatMap, gx + 1, gz + 1, w, d, in se);
		TrySetSplat(_terrainCliffSplatMap, gx, gz + 1, w, d, in sw);
	}

	private void SanitizeCellCorners(TerrainCell[,] cells, int gx, int gz, int w, int d, float h)
	{
		bool xm1 = gx > 0 && gx - 1 < w;
		bool zm1 = gz > 0 && gz - 1 < d;
		bool x0 = gx < w;
		bool z0 = gz < d;

		if (xm1 && zm1) cells[gx - 1, gz - 1].Y_SE = h;
		if (x0 && zm1) cells[gx, gz - 1].Y_SW = h;
		if (xm1 && z0) cells[gx - 1, gz].Y_NE = h;
		if (x0 && z0) cells[gx, gz].Y_NW = h;
	}

	private void SanitizeCornerHeights(ref TerrainState terrain)
	{
		var cells = terrain.Cells;
		if (cells == null) return;

		int w = terrain.Width;
		int d = terrain.Depth;

		for (int gz = 0; gz <= d; gz++)
		{
			for (int gx = 0; gx <= w; gx++)
			{
				SanitizeCellCorners(cells, gx, gz, w, d, GetGridNodeHeight(in terrain, gx, gz));
			}
		}
	}

	private void ProcessPasteBlockCell(
		int sx, int sz, int pasteWidth, int pasteDepth, int targetWidth, int targetDepth,
		int blockStartX, int blockStartZ,
		int rotSteps, PasteReflection pasteReflection, MirrorMode symmetryMirror, int foldRotSteps,
		int width, int depth, bool pasteHeights, bool pasteTextures, bool pastePathing,
		TerrainCell[,]? srcCells, TerrainCell[,]? cells,
		ref TerrainState terrain, ref bool modified, ref bool pathingModified)
	{
		int srcX = pasteReflection == PasteReflection.Horizontal ? pasteWidth - 1 - sx : sx;
		int srcZ = pasteReflection == PasteReflection.Vertical ? pasteDepth - 1 - sz : sz;

		if (_copiedArea.Mask != null && !_copiedArea.Mask[srcX, srcZ]) return;

		int rotX = sx;
		int rotZ = sz;
		if (rotSteps == 1)
		{
			rotX = pasteDepth - 1 - sz;
			rotZ = sx;
		}
		else if (rotSteps == 2)
		{
			rotX = pasteWidth - 1 - sx;
			rotZ = pasteDepth - 1 - sz;
		}
		else if (rotSteps == 3)
		{
			rotX = sz;
			rotZ = pasteWidth - 1 - sx;
		}

		TerrainCell curCell = default;
		if (pasteHeights && srcCells != null && srcX < pasteWidth && srcZ < pasteDepth)
		{
			curCell = srcCells[srcX, srcZ];
			if (pasteReflection == PasteReflection.Horizontal) curCell = MirrorCell(in curCell, MirrorMode.Horizontal);
			else if (pasteReflection == PasteReflection.Vertical) curCell = MirrorCell(in curCell, MirrorMode.Vertical);
			curCell = RotateCell(in curCell, rotSteps);
		}

		TerrainSplatWeights sNW = default, sNE = default, sSE = default, sSW = default;
		TerrainSplatWeights cS_NW = default, cS_NE = default, cS_SE = default, cS_SW = default;
		if (pasteTextures)
		{
			if (_copiedArea.SplatMap != null && srcX + 1 < _copiedArea.SplatMap.GetLength(0) && srcZ + 1 < _copiedArea.SplatMap.GetLength(1))
			{
				sNW = _copiedArea.SplatMap[srcX, srcZ];
				sNE = _copiedArea.SplatMap[srcX + 1, srcZ];
				sSE = _copiedArea.SplatMap[srcX + 1, srcZ + 1];
				sSW = _copiedArea.SplatMap[srcX, srcZ + 1];
				if (pasteReflection == PasteReflection.Horizontal) (sNW, sNE, sSE, sSW) = MirrorSplatQuad(in sNW, in sNE, in sSE, in sSW, MirrorMode.Horizontal);
				else if (pasteReflection == PasteReflection.Vertical) (sNW, sNE, sSE, sSW) = MirrorSplatQuad(in sNW, in sNE, in sSE, in sSW, MirrorMode.Vertical);
				(sNW, sNE, sSE, sSW) = RotateSplatQuad(in sNW, in sNE, in sSE, in sSW, rotSteps);
			}

			if (_copiedArea.CliffSplatMap != null && srcX + 1 < _copiedArea.CliffSplatMap.GetLength(0) && srcZ + 1 < _copiedArea.CliffSplatMap.GetLength(1))
			{
				cS_NW = _copiedArea.CliffSplatMap[srcX, srcZ];
				cS_NE = _copiedArea.CliffSplatMap[srcX + 1, srcZ];
				cS_SE = _copiedArea.CliffSplatMap[srcX + 1, srcZ + 1];
				cS_SW = _copiedArea.CliffSplatMap[srcX, srcZ + 1];
				if (pasteReflection == PasteReflection.Horizontal) (cS_NW, cS_NE, cS_SE, cS_SW) = MirrorSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, MirrorMode.Horizontal);
				else if (pasteReflection == PasteReflection.Vertical) (cS_NW, cS_NE, cS_SE, cS_SW) = MirrorSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, MirrorMode.Vertical);
				(cS_NW, cS_NE, cS_SE, cS_SW) = RotateSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, rotSteps);
			}
			else if (_copiedArea.SplatMap != null)
			{
				cS_NW = sNW;
				cS_NE = sNE;
				cS_SE = sSE;
				cS_SW = sSW;
			}
		}

		int finalX = rotX;
		int finalZ = rotZ;
		if (symmetryMirror == MirrorMode.Horizontal)
		{
			finalX = targetWidth - 1 - rotX;
			curCell = MirrorCell(in curCell, MirrorMode.Horizontal);
			(sNW, sNE, sSE, sSW) = MirrorSplatQuad(in sNW, in sNE, in sSE, in sSW, MirrorMode.Horizontal);
			(cS_NW, cS_NE, cS_SE, cS_SW) = MirrorSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, MirrorMode.Horizontal);
		}
		else if (symmetryMirror == MirrorMode.Vertical)
		{
			finalZ = targetDepth - 1 - rotZ;
			curCell = MirrorCell(in curCell, MirrorMode.Vertical);
			(sNW, sNE, sSE, sSW) = MirrorSplatQuad(in sNW, in sNE, in sSE, in sSW, MirrorMode.Vertical);
			(cS_NW, cS_NE, cS_SE, cS_SW) = MirrorSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, MirrorMode.Vertical);
		}
		else if (symmetryMirror == MirrorMode.Both)
		{
			finalX = targetWidth - 1 - rotX;
			finalZ = targetDepth - 1 - rotZ;
			curCell = MirrorCell(in curCell, MirrorMode.Both);
			(sNW, sNE, sSE, sSW) = MirrorSplatQuad(in sNW, in sNE, in sSE, in sSW, MirrorMode.Both);
			(cS_NW, cS_NE, cS_SE, cS_SW) = MirrorSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, MirrorMode.Both);
		}
		else if (foldRotSteps != 0)
		{
			if (foldRotSteps == 1)
			{
				finalX = targetDepth - 1 - rotZ;
				finalZ = rotX;
			}
			else if (foldRotSteps == 2)
			{
				finalX = targetWidth - 1 - rotX;
				finalZ = targetDepth - 1 - rotZ;
			}
			else if (foldRotSteps == 3)
			{
				finalX = rotZ;
				finalZ = targetWidth - 1 - rotX;
			}

			curCell = RotateCell(in curCell, foldRotSteps);
			(sNW, sNE, sSE, sSW) = RotateSplatQuad(in sNW, in sNE, in sSE, in sSW, foldRotSteps);
			(cS_NW, cS_NE, cS_SE, cS_SW) = RotateSplatQuad(in cS_NW, in cS_NE, in cS_SE, in cS_SW, foldRotSteps);
		}

		int targetX = blockStartX + finalX;
		int targetZ = blockStartZ + finalZ;

		if (targetX >= 0 && targetX < width && targetZ >= 0 && targetZ < depth)
		{
			if (pasteHeights && srcCells != null && cells != null)
			{
				SetGridNodeHeight(ref terrain, targetX, targetZ, curCell.Y_NW);
				SetGridNodeHeight(ref terrain, targetX + 1, targetZ, curCell.Y_NE);
				SetGridNodeHeight(ref terrain, targetX + 1, targetZ + 1, curCell.Y_SE);
				SetGridNodeHeight(ref terrain, targetX, targetZ + 1, curCell.Y_SW);
				cells[targetX, targetZ].WaterMode = curCell.WaterMode;
				cells[targetX, targetZ].WaterProfileIndex = curCell.WaterProfileIndex;
				cells[targetX, targetZ].WaterHeight = curCell.WaterHeight;
			}

			if (pasteTextures)
			{
				SetGridNodeSplat(targetX, targetZ, in sNW, in sNE, in sSE, in sSW);
				SetGridNodeCliffSplat(targetX, targetZ, in cS_NW, in cS_NE, in cS_SE, in cS_SW);
			}

			if (pastePathing && _copiedArea.Pathing != null && terrain.PathingCodes != null && srcX < pasteWidth && srcZ < pasteDepth)
			{
				terrain.PathingCodes[targetX, targetZ] = _copiedArea.Pathing[srcX, srcZ];
				pathingModified = true;
			}

			modified = true;
		}
	}

	private void PasteBlock(
		int blockStartX, int blockStartZ,
		int rotSteps,
		PasteReflection pasteReflection,
		MirrorMode symmetryMirror,
		int foldRotSteps,
		int width, int depth,
		bool pasteHeights, bool pasteTextures, bool pastePathing,
		ref TerrainState terrain,
		ref bool modified,
		ref bool pathingModified)
	{
		int pasteWidth = _copiedArea.Width;
		int pasteDepth = _copiedArea.Depth;
		int targetWidth = (rotSteps == 1 || rotSteps == 3) ? pasteDepth : pasteWidth;
		int targetDepth = (rotSteps == 1 || rotSteps == 3) ? pasteWidth : pasteDepth;

		var cells = terrain.Cells;
		var srcCells = _copiedArea.Cells;

		for (int sz = 0; sz < pasteDepth; sz++)
		{
			for (int sx = 0; sx < pasteWidth; sx++)
			{
				ProcessPasteBlockCell(sx, sz, pasteWidth, pasteDepth, targetWidth, targetDepth, blockStartX, blockStartZ, rotSteps, pasteReflection, symmetryMirror, foldRotSteps, width, depth, pasteHeights, pasteTextures, pastePathing, srcCells, cells, ref terrain, ref modified, ref pathingModified);
			}
		}
	}

	public bool GetBlockMode(Entity worldEntity)
	{
		return GameContext.World.GetFieldOrDefault<EditorState, bool>(worldEntity, s => s.BlockMode, true);
	}

	public void SetBlockMode(Entity worldEntity, bool value)
	{
		GameContext.World.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.BlockMode = value);
	}

	public float GetBlockLevelHeight(Entity worldEntity)
	{
		return GameContext.World.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.BlockLevelHeight, 3.0f);
	}

	public void SetBlockLevelHeight(Entity worldEntity, float value)
	{
		float clamped = Math.Clamp(value, 0.0f, 50.0f);
		GameContext.World.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.BlockLevelHeight = clamped);
	}

	public WaterType GetWaterMode(Entity worldEntity)
	{
		return GameContext.World.GetFieldOrDefault<EditorState, WaterType>(worldEntity, s => s.WaterMode, WaterType.None);
	}

	public void SetWaterMode(Entity worldEntity, WaterType value)
	{
		if (GameContext.World != null && GameContext.World.IsAlive(worldEntity))
		{
			if (GameContext.World.Has<EditorState>(worldEntity))
			{
				ref var state = ref GameContext.World.Get<EditorState>(worldEntity);
				state.WaterMode = value;
			}
			else
			{
				GameContext.World.Add(worldEntity, new EditorState(true, 3.0f, -95.0f, 95.0f, -95.0f, 125.0f, "", false, MirrorMode.None, value, 0));
			}
		}
	}

	public byte GetWaterProfileIndex(Entity worldEntity)
	{
		return GameContext.World.GetFieldOrDefault<EditorState, byte>(worldEntity, s => s.WaterProfileIndex, (byte)0);
	}

	public void SetWaterProfileIndex(Entity worldEntity, byte value)
	{
		if (GameContext.World != null && GameContext.World.IsAlive(worldEntity))
		{
			if (GameContext.World.Has<EditorState>(worldEntity))
			{
				ref var state = ref GameContext.World.Get<EditorState>(worldEntity);
				state.WaterProfileIndex = value;
			}
			else
			{
				GameContext.World.Add(worldEntity, new EditorState(true, 3.0f, -95.0f, 95.0f, -95.0f, 125.0f, "", false, MirrorMode.None, WaterType.None, value));
			}
		}
	}

	private readonly Dictionary<Vector2I, List<Node>> _waterProceduralObjects = new();
	private readonly Dictionary<Vector2I, List<Node>> _terrainProceduralObjects = new();

	private void ClearWaterProceduralObjects(int x, int z)
	{
		var key = new Vector2I(x, z);
		if (!_waterProceduralObjects.TryGetValue(key, out var list)) return;

		foreach (var node in list)
		{
			if (GodotObject.IsInstanceValid(node))
			{
				ClearSingleWaterProceduralObject(node);
			}
		}

		_waterProceduralObjects.Remove(key);
	}

	private void ClearSingleWaterProceduralObject(Node node)
	{
		if (node is Decal3D d)
		{
			GameHost.Instance?.AllDecals.Remove(d);
			if (GameContext.World != null && GameContext.World.IsAlive(d.Entity)) GameContext.World.Destroy(d.Entity);
		}
		else if (node is ProceduralVfxInstance3D vfx)
		{
			GameHost.Instance?.AllVfx.Remove(vfx);
			if (GameContext.World != null && GameContext.World.IsAlive(vfx.Entity)) GameContext.World.Destroy(vfx.Entity);
		}

		node.QueueFree();
	}

	private void ClearTerrainProceduralObjects(int x, int z)
	{
		var key = new Vector2I(x, z);
		if (!_terrainProceduralObjects.TryGetValue(key, out var list)) return;

		foreach (var node in list)
		{
			if (GodotObject.IsInstanceValid(node))
			{
				switch (node)
				{
					case Decal3D d:
						GameHost.Instance?.AllDecals.Remove(d);
						if (GameContext.World != null && GameContext.World.IsAlive(d.Entity)) GameContext.World.Destroy(d.Entity);
						break;
					case ProceduralVfxInstance3D vfx:
						GameHost.Instance?.AllVfx.Remove(vfx);
						if (GameContext.World != null && GameContext.World.IsAlive(vfx.Entity)) GameContext.World.Destroy(vfx.Entity);
						break;
				}

				node.QueueFree();
			}
		}

		_terrainProceduralObjects.Remove(key);
	}

	private void ProcessWaterDecalRules(WaterProfileSaveData prof, float quadSize, float cellCenterX, float waterY, float cellCenterZ, List<Node> spawnedList)
	{
		if (prof.DecalBombingRules == null || prof.DecalBombingRules.Count == 0) return;
		foreach (var rule in prof.DecalBombingRules)
		{
			if (string.IsNullOrWhiteSpace(rule.DecalId)) continue;
			if (Random.Shared.NextSingle() > (rule.Density * 0.05f)) continue;
			float jx = (Random.Shared.NextSingle() - 0.5f) * quadSize * 0.8f;
			float jz = (Random.Shared.NextSingle() - 0.5f) * quadSize * 0.8f;
			float scale = Mathf.Lerp(rule.MinScale, rule.MaxScale, Random.Shared.NextSingle());
			float rotY = Random.Shared.NextSingle() * 360f;
			var pos = new Godot.Vector3(cellCenterX + jx, waterY, cellCenterZ + jz);
			var decal = GameHost.Instance?.SpawnDecalExternalWithParams(rule.DecalId, pos, new Godot.Vector3(0, rotY, 0), scale);
			if (decal != null) spawnedList.Add(decal);
		}
	}

	private void ProcessWaterVfxRules(WaterProfileSaveData prof, float quadSize, float cellCenterX, float waterY, float cellCenterZ, List<Node> spawnedList)
	{
		if (prof.VfxBombingRules == null || prof.VfxBombingRules.Count == 0) return;
		foreach (var rule in prof.VfxBombingRules)
		{
			if (string.IsNullOrWhiteSpace(rule.VfxId)) continue;
			if (Random.Shared.NextSingle() > (rule.Density * 0.05f)) continue;
			float jx = (Random.Shared.NextSingle() - 0.5f) * quadSize * 0.8f;
			float jz = (Random.Shared.NextSingle() - 0.5f) * quadSize * 0.8f;
			float scale = Mathf.Lerp(rule.MinScale, rule.MaxScale, Random.Shared.NextSingle());
			float rotY = Random.Shared.NextSingle() * 360f;
			var pos = new Godot.Vector3(cellCenterX + jx, waterY, cellCenterZ + jz);
			var vfx = GameHost.Instance?.SpawnVfxExternalWithParams(rule.VfxId, pos, new Godot.Vector3(0, rotY, 0), new Godot.Vector3(scale, scale, scale));
			if (vfx != null) spawnedList.Add(vfx);
		}
	}

	private void SpawnWaterProceduralBombing(int x, int z, byte profileIndex, float waterY, float quadSize, int width, int depth)
	{
		ClearWaterProceduralObjects(x, z);
		if (RuntimeTerrain.Instance == null) return;
		var prof = RuntimeTerrain.Instance.GetWaterProfile(profileIndex);
		if (prof == null) return;

		float cellCenterX = (x + 0.5f - width / 2.0f) * quadSize;
		float cellCenterZ = (z + 0.5f - depth / 2.0f) * quadSize;
		var spawnedList = new List<Node>();

		ProcessWaterDecalRules(prof, quadSize, cellCenterX, waterY, cellCenterZ, spawnedList);
		ProcessWaterVfxRules(prof, quadSize, cellCenterX, waterY, cellCenterZ, spawnedList);

		if (spawnedList.Count > 0)
		{
			_waterProceduralObjects[new Vector2I(x, z)] = spawnedList;
		}
	}

	private void SpawnTerrainProceduralBombing(int x, int z, int textureIndex, float terrainY, float quadSize, int width, int depth)
	{
		ClearTerrainProceduralObjects(x, z);
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		if (string.IsNullOrEmpty(wsPath)) return;
		if (!GameContext.MetadataService.TryLoadMetadata(wsPath, out var metaRoot) || metaRoot == null) return;

		string? swatchName = null;
		if (GameHost.Instance?.GroundTerrain != null && textureIndex >= 0 && textureIndex < GameHost.Instance.GroundTerrain.LoadedTextureList.Count)
		{
			swatchName = GameHost.Instance.GroundTerrain.LoadedTextureList[textureIndex];
		}

		if (string.IsNullOrEmpty(swatchName)) return;

		var prof = metaRoot.GetTerrainProfile(swatchName);
		if (prof == null) return;

		float cellCenterX = (x + 0.5f - width / 2.0f) * quadSize;
		float cellCenterZ = (z + 0.5f - depth / 2.0f) * quadSize;
		var spawnedList = new List<Node>();

		if (prof.DecalBombingRules != null && prof.DecalBombingRules.Count > 0)
		{
			foreach (var rule in prof.DecalBombingRules)
			{
				if (string.IsNullOrWhiteSpace(rule.DecalId)) continue;
				if (Random.Shared.NextSingle() <= (rule.Density * 0.05f))
				{
					float jx = (Random.Shared.NextSingle() - 0.5f) * quadSize * 0.8f;
					float jz = (Random.Shared.NextSingle() - 0.5f) * quadSize * 0.8f;
					float scale = Mathf.Lerp(rule.MinScale, rule.MaxScale, Random.Shared.NextSingle());
					float rotY = Random.Shared.NextSingle() * 360f;
					var pos = new Godot.Vector3(cellCenterX + jx, terrainY, cellCenterZ + jz);
					var decal = GameHost.Instance?.SpawnDecalExternalWithParams(rule.DecalId, pos, new Godot.Vector3(0, rotY, 0), scale);
					if (decal != null) spawnedList.Add(decal);
				}
			}
		}

		if (prof.VfxBombingRules != null && prof.VfxBombingRules.Count > 0)
		{
			foreach (var rule in prof.VfxBombingRules)
			{
				if (string.IsNullOrWhiteSpace(rule.VfxId)) continue;
				if (Random.Shared.NextSingle() <= (rule.Density * 0.05f))
				{
					float jx = (Random.Shared.NextSingle() - 0.5f) * quadSize * 0.8f;
					float jz = (Random.Shared.NextSingle() - 0.5f) * quadSize * 0.8f;
					float scale = Mathf.Lerp(rule.MinScale, rule.MaxScale, Random.Shared.NextSingle());
					float rotY = Random.Shared.NextSingle() * 360f;
					var pos = new Godot.Vector3(cellCenterX + jx, terrainY, cellCenterZ + jz);
					var vfx = GameHost.Instance?.SpawnVfxExternalWithParams(rule.VfxId, pos, new Godot.Vector3(0, rotY, 0), new Godot.Vector3(scale, scale, scale));
					if (vfx != null) spawnedList.Add(vfx);
				}
			}
		}

		if (spawnedList.Count > 0)
		{
			_terrainProceduralObjects[new Vector2I(x, z)] = spawnedList;
		}
	}

	public float GetCameraBoundsLeft(Entity worldEntity)
	{
		return GameContext.World.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.CameraBoundsLeft, -95.0f);
	}

	public void SetCameraBoundsLeft(Entity worldEntity, float value)
	{
		GameContext.World.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.CameraBoundsLeft = value);
	}

	public float GetCameraBoundsRight(Entity worldEntity)
	{
		return GameContext.World.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.CameraBoundsRight, 95.0f);
	}

	public void SetCameraBoundsRight(Entity worldEntity, float value)
	{
		GameContext.World.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.CameraBoundsRight = value);
	}

	public float GetCameraBoundsTop(Entity worldEntity)
	{
		return GameContext.World.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.CameraBoundsTop, -95.0f);
	}

	public void SetCameraBoundsTop(Entity worldEntity, float value)
	{
		GameContext.World.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.CameraBoundsTop = value);
	}

	public float GetCameraBoundsBottom(Entity worldEntity)
	{
		return GameContext.World.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.CameraBoundsBottom, 125.0f);
	}

	public void SetCameraBoundsBottom(Entity worldEntity, float value)
	{
		GameContext.World.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.CameraBoundsBottom = value);
	}

	public string GetSkyboxPath(Entity worldEntity)
	{
		return GameContext.World.GetFieldOrDefault<EditorState, string>(worldEntity, s => s.SkyboxPath, "");
	}

	public void SetSkyboxPath(Entity worldEntity, string value)
	{
		GameContext.World.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.SkyboxPath = value);
	}

	public bool GetHasUnsavedChanges(Entity worldEntity)
	{
		return GameContext.World.GetFieldOrDefault<EditorState, bool>(worldEntity, s => s.HasUnsavedChanges, false);
	}

	public void SetHasUnsavedChanges(Entity worldEntity, bool value)
	{
		GameContext.World.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.HasUnsavedChanges = value);
	}

	public string GetTerrainStatusString(Godot.Vector3 pos, string toolName, string activePlaceId)
	{
		string formattedToolName = toolName.ToUpper();
		if (!string.IsNullOrEmpty(activePlaceId))
		{
			string shortId = Path.GetFileName(activePlaceId).ToUpper();
			formattedToolName += $" ({shortId})";
		}

		string status = $"ACTIVE TOOL: {formattedToolName} | Pos: {pos.X:F1}, {pos.Z:F1}, {pos.Y:F1}";

		ref var terrain = ref GetTerrainState();

		if (terrain.Cells != null && toolName.Equals("PaintPathing", StringComparison.OrdinalIgnoreCase))
		{
			float fx = pos.X / terrain.QuadSize + terrain.Width / 2.0f;
			float fz = pos.Z / terrain.QuadSize + terrain.Depth / 2.0f;
			int cx = Mathf.Clamp((int)Mathf.Round(fx), 0, terrain.Width - 1);
			int cz = Mathf.Clamp((int)Mathf.Round(fz), 0, terrain.Depth - 1);

			if (terrain.PathingCodes != null)
			{
				int code = terrain.PathingCodes[cx, cz];
				var layers = new List<string>();
				if ((code & EditableTerrain.PATHING_GROUND) != 0)
				{
					layers.Add("Ground");
				}

				if ((code & EditableTerrain.PATHING_FLYING) != 0)
				{
					layers.Add("Flying");
				}

				if ((code & EditableTerrain.PATHING_SHALLOW_WATER) != 0)
				{
					layers.Add("Shallow Water");
				}

				if ((code & EditableTerrain.PATHING_DEEP_WATER) != 0)
				{
					layers.Add("Deep Water");
				}

				if ((code & EditableTerrain.PATHING_BUILDABLE) != 0)
				{
					layers.Add("Buildable");
				}

				string layersStr = layers.Count > 0 ? string.Join(", ", layers) : "None";
				status += $" | Path: {layersStr}";
			}
		}

		return status;
	}

	public void AlignSplatMapSlots(int minX, int minZ, int maxX, int maxZ)
	{
		if (_terrainSplatMap != null) AlignSplatMapMatrix(_terrainSplatMap, minX, minZ, maxX, maxZ);
		if (_terrainCliffSplatMap == null && GameHost.Instance?.GroundTerrain != null)
		{
			_terrainCliffSplatMap = GameHost.Instance.GroundTerrain.CliffSplatMap;
		}

		if (_terrainCliffSplatMap != null) AlignSplatMapMatrix(_terrainCliffSplatMap, minX, minZ, maxX, maxZ);
	}

	private void ProcessSplatNeighbors(TerrainSplatWeights[,] splatMatrix, int x, int z, int width, int depth)
	{
		for (int dz = -1; dz <= 1; dz++)
		{
			for (int dx = -1; dx <= 1; dx++)
			{
				if (dx == 0 && dz == 0) continue;
				int nx = x + dx;
				int nz = z + dz;
				if (nx >= 0 && nx < width && nz >= 0 && nz < depth)
				{
					ProcessSingleNeighborSplat(splatMatrix, x, z, nx, nz);
				}
			}
		}
	}

	private void ProcessSingleNeighborSplat(TerrainSplatWeights[,] splatMatrix, int x, int z, int nx, int nz)
	{
		var neighbor = splatMatrix[nx, nz];
		if (neighbor.Weight0 > 0.001f) TryAddIndexToUnusedSlot(splatMatrix, x, z, neighbor.Index0);
		if (neighbor.Weight1 > 0.001f) TryAddIndexToUnusedSlot(splatMatrix, x, z, neighbor.Index1);
		if (neighbor.Weight2 > 0.001f) TryAddIndexToUnusedSlot(splatMatrix, x, z, neighbor.Index2);
		if (neighbor.Weight3 > 0.001f) TryAddIndexToUnusedSlot(splatMatrix, x, z, neighbor.Index3);
	}

	private void AlignSplatMapMatrix(TerrainSplatWeights[,] splatMatrix, int minX, int minZ, int maxX, int maxZ)
	{
		int width = splatMatrix.GetLength(0);
		int depth = splatMatrix.GetLength(1);

		minX = Math.Max(0, minX);
		minZ = Math.Max(0, minZ);
		maxX = Math.Min(width - 1, maxX);
		maxZ = Math.Min(depth - 1, maxZ);

		for (int z = minZ; z <= maxZ; z++)
		{
			for (int x = minX; x <= maxX; x++)
			{
				ProcessSplatNeighbors(splatMatrix, x, z, width, depth);
			}
		}
	}

	private void TryAddIndexToUnusedSlot(TerrainSplatWeights[,] splatMatrix, int x, int z, int index)
	{
		var current = splatMatrix[x, z];

		if (HasIndex(ref current, index)) return;

		if (TryAssignPreferredSlot(ref current, index))
		{
			splatMatrix[x, z] = current;
			return;
		}

		if (TryAssignAnyAvailableSlot(ref current, index))
		{
			splatMatrix[x, z] = current;
		}
	}

	private bool HasIndex(ref TerrainSplatWeights current, int index)
	{
		return current.Index0 == index || current.Index1 == index || current.Index2 == index || current.Index3 == index;
	}

	private bool TryAssignPreferredSlot(ref TerrainSplatWeights current, int index)
	{
		int preferredSlot = index % 4;
		bool slotAvailable = preferredSlot switch
		{
			0 => current.Weight0 <= 0.001f,
			1 => current.Weight1 <= 0.001f,
			2 => current.Weight2 <= 0.001f,
			3 => current.Weight3 <= 0.001f,
			_ => false
		};

		if (!slotAvailable) return false;

		switch (preferredSlot)
		{
			case 0:
				current.Index0 = index;
				current.Weight0 = 0.0f;
				break;
			case 1:
				current.Index1 = index;
				current.Weight1 = 0.0f;
				break;
			case 2:
				current.Index2 = index;
				current.Weight2 = 0.0f;
				break;
			case 3:
				current.Index3 = index;
				current.Weight3 = 0.0f;
				break;
		}

		return true;
	}

	private bool TryAssignAnyAvailableSlot(ref TerrainSplatWeights current, int index)
	{
		if (current.Weight0 <= 0.001f)
		{
			current.Index0 = index;
			current.Weight0 = 0.0f;
			return true;
		}

		if (current.Weight1 <= 0.001f)
		{
			current.Index1 = index;
			current.Weight1 = 0.0f;
			return true;
		}

		if (current.Weight2 <= 0.001f)
		{
			current.Index2 = index;
			current.Weight2 = 0.0f;
			return true;
		}

		if (current.Weight3 <= 0.001f)
		{
			current.Index3 = index;
			current.Weight3 = 0.0f;
			return true;
		}

		return false;
	}

	private FileSystemWatcher? _workspaceWatcher;
	private string? _watchedDirectory;
	private Timer? _debounceTimer;
	private readonly object _watcherLock = new();
	private long _lastProcessedMetadataWriteTime;
	private long _lastProcessedTerrainWriteTime;
	public static DateTime LastInternalSaveTimeUtc { get; set; } = DateTime.MinValue;
	public bool PasteOptionEntities { get; set; } = true;
	public bool PasteOptionPathing { get; set; } = true;
	public float EditorPasteRotation { get; set; } = 0.0f;
	public PasteReflection EditorPasteReflection { get; set; } = PasteReflection.None;

	private bool _isPaused;

	public void ClearHistoryAndState(ref bool hasUnsavedChanges)
	{
		EditorHistoryManager.Clear();
		hasUnsavedChanges = false;
	}

	public bool IsPaused
	{
		get => _isPaused;
		set
		{
			_isPaused = value;
			if (value)
			{
				lock (_watcherLock)
				{
					_debounceTimer?.Dispose();
					_debounceTimer = null;
				}
			}
		}
	}

	public void UpdateWatchedFileTimestamps()
	{
		lock (_watcherLock)
		{
			if (!string.IsNullOrEmpty(_watchedDirectory) && Directory.Exists(_watchedDirectory))
			{
				string metaPath = Path.Combine(_watchedDirectory, "metadata.json");
				string terrainPath = Path.Combine(_watchedDirectory, "terrain.json");
				if (File.Exists(metaPath)) _lastProcessedMetadataWriteTime = File.GetLastWriteTimeUtc(metaPath).Ticks;
				if (File.Exists(terrainPath)) _lastProcessedTerrainWriteTime = File.GetLastWriteTimeUtc(terrainPath).Ticks;
			}
		}
	}

	public void StartWorkspaceWatcher(string directory, Action? onMetadataChanged = null, Action? onTerrainChanged = null)
	{
		lock (_watcherLock)
		{
			StopWorkspaceWatcher();

			if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
			{
				return;
			}

			_watchedDirectory = directory;
			try
			{
				string metaPath = Path.Combine(directory, "metadata.json");
				string terrainPath = Path.Combine(directory, "terrain.json");
				if (File.Exists(metaPath)) _lastProcessedMetadataWriteTime = File.GetLastWriteTimeUtc(metaPath).Ticks;
				if (File.Exists(terrainPath)) _lastProcessedTerrainWriteTime = File.GetLastWriteTimeUtc(terrainPath).Ticks;

				_workspaceWatcher = new FileSystemWatcher(directory)
				{
					NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
					IncludeSubdirectories = false,
					EnableRaisingEvents = true
				};

				_workspaceWatcher.Changed += (s, e) => OnWorkspaceFileChanged(e.FullPath, onMetadataChanged, onTerrainChanged);
				_workspaceWatcher.Created += (s, e) => OnWorkspaceFileChanged(e.FullPath, onMetadataChanged, onTerrainChanged);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[EditorService] Failed to start FileSystemWatcher on {directory}: {ex.Message}");
			}
		}
	}

	public void StopWorkspaceWatcher()
	{
		lock (_watcherLock)
		{
			if (_workspaceWatcher != null)
			{
				_workspaceWatcher.EnableRaisingEvents = false;
				_workspaceWatcher.Dispose();
				_workspaceWatcher = null;
			}

			_debounceTimer?.Dispose();
			_debounceTimer = null;
		}
	}

	private void OnWorkspaceFileChanged(string fullPath, Action? onMetadataChanged, Action? onTerrainChanged)
	{
		if (IsPaused || (MapEditorHUD.Instance != null && GameContext.EditorService.IsSyncing))
		{
			return;
		}

		string fileName = Path.GetFileName(fullPath).ToLowerInvariant();
		if (fileName != "metadata.json" && fileName != "manifest.json" && fileName != "terrain.json")
		{
			return;
		}

		lock (_watcherLock)
		{
			_debounceTimer?.Dispose();
			_debounceTimer = new Timer(_ => { ProcessDebouncedFileChange(fullPath, fileName, onMetadataChanged, onTerrainChanged); }, null, 150, Timeout.Infinite);
		}
	}

	private void ProcessDebouncedFileChange(string fullPath, string fileName, Action? onMetadataChanged, Action? onTerrainChanged)
	{
		try
		{
			if (!ShouldProcessFileChange(fullPath)) return;

			long writeTime = File.GetLastWriteTimeUtc(fullPath).Ticks;
			bool isMetadata = fileName == "metadata.json" || fileName == "manifest.json";

			if (isMetadata)
			{
				ProcessMetadataChange(fullPath, writeTime, onMetadataChanged);
			}
			else if (fileName == "terrain.json")
			{
				ProcessTerrainChange(fullPath, writeTime, onTerrainChanged);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[EditorService] ProcessDebouncedFileChange error: {ex.Message}");
		}
	}

	private bool ShouldProcessFileChange(string fullPath)
	{
		MapEditorHUD tempQualifier = MapEditorHUD.Instance;
		if (IsPaused || ((tempQualifier != null ? GameContext.EditorService.IsSyncing : (bool?)null) ?? false) || !File.Exists(fullPath)) return false;
		if ((DateTime.UtcNow - LastInternalSaveTimeUtc).TotalMilliseconds < 1500) return false;
		return true;
	}

	private void ProcessMetadataChange(string fullPath, long writeTime, Action? onMetadataChanged)
	{
		if (writeTime > _lastProcessedMetadataWriteTime)
		{
			_lastProcessedMetadataWriteTime = writeTime;
			Callable.From(() => HandleExternalMetadataChange(fullPath, onMetadataChanged)).CallDeferred();
		}
	}

	private void ProcessTerrainChange(string fullPath, long writeTime, Action? onTerrainChanged)
	{
		if (writeTime > _lastProcessedTerrainWriteTime)
		{
			_lastProcessedTerrainWriteTime = writeTime;
			Callable.From(() => HandleExternalTerrainChange(fullPath, onTerrainChanged)).CallDeferred();
		}
	}

	private void HandleExternalMetadataChange(string fullPath, Action? customCallback)
	{
		if (FloatingDialogBase.HasAnyDialogOpen)
		{
			return;
		}

		ExecuteMetadataReload(fullPath, customCallback);
	}

	private void ExecuteMetadataReload(string fullPath, Action? customCallback)
	{
		try
		{
			string name = Path.GetFileName(fullPath);
			MapEditorHUD.Instance?.ReadMetadataAndRefreshTextures();
			MapEditorHUD.Instance?.ShowFeedback(string.Format(TranslationServer.Translate("{0} updated externally — reloaded."), name));
			customCallback?.Invoke();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[EditorService] ExecuteMetadataReload error: {ex.Message}");
		}
	}

	private void HandleExternalTerrainChange(string fullPath, Action? customCallback)
	{
		if (FloatingDialogBase.HasAnyDialogOpen)
		{
			return;
		}

		try
		{
			GameHost.Instance?.LoadMapFromFile(fullPath);
			MapEditorHUD.Instance?.ShowFeedback(TranslationServer.Translate("terrain.json updated externally — reloaded."));
			customCallback?.Invoke();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[EditorService] HandleExternalTerrainChange error: {ex.Message}");
		}
	}

	public bool CommitCoordinate(string name, int minCellX, int minCellZ, int maxCellX, int maxCellZ)
	{
		return GameHost.Instance?.CommitCoordinateExternal(name, minCellX, minCellZ, maxCellX, maxCellZ) ?? false;
	}

	public bool DeleteCoordinate(string name)
	{
		if (GameHost.Instance == null) return false;
		GameHost.Instance.DeleteCoordinateExternal(name);
		return true;
	}

	public void SelectCoordinate(string name)
	{
		GameHost.Instance?.SelectCoordinateExternal(name);
	}

	private bool EnsureSplatMapsInitialized()
	{
		if (_terrainSplatMap == null && GameHost.Instance?.GroundTerrain != null)
			_terrainSplatMap = GameHost.Instance.GroundTerrain.SplatMap;
		if (_terrainCliffSplatMap == null && GameHost.Instance?.GroundTerrain != null)
			_terrainCliffSplatMap = GameHost.Instance.GroundTerrain.CliffSplatMap;
		return _terrainSplatMap != null;
	}

	public bool ResetScaleSelectedObjectDomain()
	{
		if (GameHost.Instance == null) return false;
		var selected = GameHost.Instance.SelectedEditorObject;
		if (GodotObject.IsInstanceValid(selected))
		{
			var node3D = selected as Node3D;
			if (node3D == null) return false;

			Godot.Vector3 oldScale = node3D.Scale;
			Godot.Vector3 newScale = Godot.Vector3.One;
			bool isUnit = selected is Unit3D;
			bool isEnemy = isUnit ? (selected as Unit3D).IsEnemy : false;
			var action = new ObjectTransformAction(
				node3D,
				node3D.Position, node3D.Position,
				node3D.RotationDegrees, node3D.RotationDegrees,
				oldScale, newScale,
				isEnemy, isEnemy
			);
			node3D.Scale = newScale;
			if (selected is Prop3D propReset)
			{
				PropMultiMeshManager.Instance?.MarkDirty(propReset.PropId);
			}

			EditorHistoryManager.RecordAction(action);
			return true;
		}

		return false;
	}

	public (bool success, float newScaleVal) ScaleSelectedObjectDomain(float scaleDelta)
	{
		if (GameHost.Instance == null) return (false, 1.0f);
		var selected = GameHost.Instance.SelectedEditorObject;
		if (GodotObject.IsInstanceValid(selected))
		{
			var node3D = selected as Node3D;
			if (node3D == null) return (false, 1.0f);

			Godot.Vector3 oldScale = node3D.Scale;
			float newScaleVal = Mathf.Clamp(oldScale.X + scaleDelta, 0.2f, 4.0f);
			Godot.Vector3 newScale = Godot.Vector3.One * newScaleVal;
			bool isUnit = selected is Unit3D;
			bool isEnemy = isUnit ? (selected as Unit3D).IsEnemy : false;
			var action = new ObjectTransformAction(
				node3D,
				node3D.Position, node3D.Position,
				node3D.RotationDegrees, node3D.RotationDegrees,
				oldScale, newScale,
				isEnemy, isEnemy
			);
			node3D.Scale = newScale;
			if (selected is Prop3D propScale)
			{
				PropMultiMeshManager.Instance?.MarkDirty(propScale.PropId);
			}

			EditorHistoryManager.RecordAction(action);
			return (true, newScaleVal);
		}

		return (false, 1.0f);
	}

	public (bool success, float newRotY) RotateSelectedObjectDomain(float angleDelta)
	{
		if (GameHost.Instance == null) return (false, 0f);
		var selected = GameHost.Instance.SelectedEditorObject;
		if (GodotObject.IsInstanceValid(selected))
		{
			var node3D = selected as Node3D;
			if (node3D == null) return (false, 0f);

			Godot.Vector3 oldRot = node3D.RotationDegrees;
			Godot.Vector3 newRot = oldRot;
			newRot.Y = (newRot.Y + angleDelta + 360.0f) % 360.0f;
			bool isUnit = selected is Unit3D;
			bool isEnemy = isUnit ? (selected as Unit3D).IsEnemy : false;
			var action = new ObjectTransformAction(
				node3D,
				node3D.Position, node3D.Position,
				oldRot, newRot,
				node3D.Scale, node3D.Scale,
				isEnemy, isEnemy
			);
			node3D.RotationDegrees = newRot;
			if (selected is Prop3D propRot)
			{
				PropMultiMeshManager.Instance?.MarkDirty(propRot.PropId);
			}

			EditorHistoryManager.RecordAction(action);
			return (true, newRot.Y);
		}

		return (false, 0f);
	}

	public (bool success, string error) DeleteSelectedObjectDomain()
	{
		if (GameHost.Instance == null)
		{
			return (false, "[Debug] DeleteSelectedObject: Realm.Client.Core.GameHost.Instance is NULL!");
		}

		var selected = GameHost.Instance.SelectedEditorObject;
		if (GodotObject.IsInstanceValid(selected))
		{
			Godot.Vector3 pos = (selected is Node3D n) ? n.Position : Godot.Vector3.Zero;
			GameHost.Instance.SelectedEditorObject = null;
			var action = GameHost.Instance.DeleteObjectAtWithUndo(selected, pos);
			if (action != null)
			{
				EditorHistoryManager.RecordAction(action);
			}
			else
			{
				GameHost.Instance.DeleteNodeExternal(selected);
			}

			return (true, "");
		}

		return (false, "");
	}

	public PasteReflection CyclePasteReflectionDomain()
	{
		var current = this.EditorPasteReflection;
		var next = current switch
		{
			PasteReflection.None => PasteReflection.Horizontal,
			PasteReflection.Horizontal => PasteReflection.Vertical,
			PasteReflection.Vertical => PasteReflection.None,
			_ => PasteReflection.None
		};
		this.EditorPasteReflection = next;
		return next;
	}

	public void RequestOpenFromCasDomain(string casSourceDirectory, string? defaultSaveFolder)
	{
		this.PendingCasSourceDirectory = casSourceDirectory;
		this.PendingDefaultSaveFolder = defaultSaveFolder;
	}

	public void UpdateLastMetadataSyncTimeDomain(long writeTimeTicks)
	{
		this.LastMetadataSyncTime = Math.Max(writeTimeTicks, DateTime.UtcNow.Ticks);
	}

	public void PerformAutoBackupDomain(long terrainWriteTime, long metadataWriteTime)
	{
		this.LastTerrainSyncTime = terrainWriteTime;
		this.LastMetadataSyncTime = metadataWriteTime;
	}

	public void InitializeTempWorkspaceDomain(long terrainTime, long metadataTime)
	{
		this.LastTerrainSyncTime = terrainTime;
		this.LastMetadataSyncTime = metadataTime;
		this.StartWorkspaceWatcher(this._tempWorkspacePath);
	}

	public (string? casDir, string? defaultSave) ConsumePendingCasSourceDomain()
	{
		string? casDir = this.PendingCasSourceDirectory;
		string? defaultSave = this.PendingDefaultSaveFolder;
		this.PendingCasSourceDirectory = null;
		this.PendingDefaultSaveFolder = null;
		return (casDir, defaultSave);
	}

	public void TryCreateTempWorkspaceDirectoryDomain()
	{
		Directory.CreateDirectory(this._tempWorkspacePath);
		MapWorkspaceService.SetupWorkspace(this._tempWorkspacePath, "MapScript");
	}

	public void CleanTempWorkspaceRmapFilesDomain()
	{
		if (!Directory.Exists(this._tempWorkspacePath)) return;
		foreach (var rmapFile in Directory.GetFiles(this._tempWorkspacePath, "*.rmap", SearchOption.TopDirectoryOnly))
		{
			try
			{
				File.Delete(rmapFile);
			}
			catch
			{
			}
		}
	}

	public void RedoDomain()
	{
		EditorHistoryManager.Redo();
	}

	public void SetPendingCoordinatesDomain(int minX, int minZ, int maxX, int maxZ)
	{
		PendingCoordinateMinX = minX;
		PendingCoordinateMinZ = minZ;
		PendingCoordinateMaxX = maxX;
		PendingCoordinateMaxZ = maxZ;
	}

	public void SetPlaceUnitIsEnemyDomain(bool isEnemy)
	{
		PlaceUnitIsEnemy = isEnemy;
	}

	public void UndoDomain()
	{
		EditorHistoryManager.Undo();
	}

	public bool TryGetSymmetryVisibilityState(out bool isRotational, out GridOverlayMode gridMode)
	{
		isRotational = false;
		gridMode = EditorGridMode;

		if (GameHost.Instance == null)
			return false;

		isRotational = GameHost.Instance.EditorMirrorMode == MirrorMode.Rotational;
		return true;
	}

	public void LoadMapProperties()
	{
		RuntimeTerrain.Instance?.ReloadWaterProfiles();
	}

	public TerrainSplatWeights[,] GenerateDefaultCliffSplatMap(int width, int depth)
	{
		int cliffW = width + 1;
		int cliffD = depth + 1;
		var splatMap = new TerrainSplatWeights[cliffW, cliffD];
		for (int z = 0; z < cliffD; z++)
		{
			for (int x = 0; x < cliffW; x++)
			{
				splatMap[x, z] = TerrainSplatWeights.CreateSolid(1);
			}
		}

		return splatMap;
	}

	public void CacheDecalAsset(string cacheKey, DecalAssetData assetData)
	{
		DecalAssetCache[cacheKey] = assetData;
	}

	public DecalMetadata? FindDecalMetadata(MapMetadata metadata, string decalId, string filename, string baseKey)
	{
		if (metadata?.Decals == null) return null;
		if (metadata.Decals.TryGetValue(decalId, out var d0)) return d0;
		if (metadata.Decals.TryGetValue(filename, out var d1)) return d1;
		if (metadata.Decals.TryGetValue(baseKey, out var d2)) return d2;
		if (metadata.Decals.TryGetValue($"{baseKey}.rtex", out var d3)) return d3;
		if (metadata.Decals.TryGetValue($"{baseKey}.png", out var d4)) return d4;
		return null;
	}

	public VfxMetadata? FindVfxMetadata(MapMetadata metadata, string decalId, string filename, string baseKey)
	{
		if (metadata?.VfxSpritesheets == null) return null;
		if (metadata.VfxSpritesheets.TryGetValue(decalId, out var v0)) return v0;
		if (metadata.VfxSpritesheets.TryGetValue(filename, out var v1)) return v1;
		if (metadata.VfxSpritesheets.TryGetValue(baseKey, out var v2)) return v2;
		return null;
	}

	public (int cols, int rows, float fps, bool blend, string? explicitPath) ParseDecalMetadata(string decalId, string filename, string baseKey, string wsPath)
	{
		int cols = 1, rows = 1;
		float fps = 12.0f;
		bool blend = true;
		string? explicitPath = null;

		try
		{
			var metadata = MapFileService.LoadMetadata(wsPath);
			if (metadata != null)
			{
				var meta = FindDecalMetadata(metadata, decalId, filename, baseKey);
				if (meta != null && !string.IsNullOrWhiteSpace(meta.TexturePath)) explicitPath = meta.TexturePath;

				var vmeta = FindVfxMetadata(metadata, decalId, filename, baseKey);
				if (vmeta != null)
				{
					if (vmeta.Columns > 0) cols = vmeta.Columns;
					if (vmeta.Rows > 0) rows = vmeta.Rows;
					if (vmeta.Fps > 0.001f) fps = vmeta.Fps;
					blend = vmeta.SubframeBlend;
				}
			}
		}
		catch
		{
		}

		return (cols, rows, fps, blend, explicitPath);
	}

	public List<string> GetDecalCandidatePaths(string decalId, string? explicitTexturePath, string wsPath)
	{
		var candidatePaths = new List<string>();

		void AddCandidates(string pathOrName)
		{
			if (string.IsNullOrWhiteSpace(pathOrName)) return;
			string fName = Path.GetFileName(pathOrName);
			if (Path.IsPathRooted(pathOrName))
			{
				candidatePaths.Add(pathOrName);
				return;
			}

			candidatePaths.Add(Path.Combine(wsPath, "Assets", "decals", pathOrName));
			candidatePaths.Add(Path.Combine(wsPath, "Assets", "decals", fName));
			candidatePaths.Add(Path.Combine(wsPath, pathOrName));
			if (!fName.Contains('.'))
			{
				candidatePaths.Add(Path.Combine(wsPath, "Assets", "decals", fName + ".rtex"));
				candidatePaths.Add(Path.Combine(wsPath, "Assets", "decals", fName + ".webp"));
				candidatePaths.Add(Path.Combine(wsPath, "Assets", "decals", fName + ".png"));
			}
		}

		if (!string.IsNullOrEmpty(explicitTexturePath)) AddCandidates(explicitTexturePath);
		AddCandidates(decalId);
		return candidatePaths;
	}

	public void ParseRtexDecalMetadata(string customJson, ref int cols, ref int rows)
	{
		if (string.IsNullOrEmpty(customJson)) return;
		try
		{
			if (JsonNode.Parse(customJson) is not JsonObject jNode) return;

			if (TryGetPositiveIntFromJson(jNode, "columns", out int c))
				cols = c;

			if (TryGetPositiveIntFromJson(jNode, "rows", out int r))
				rows = r;
		}
		catch
		{
		}
	}

	public void ParseExternalDecalMetadata(string meta, ref int cols, ref int rows)
	{
		if (string.IsNullOrEmpty(meta)) return;
		try
		{
			if (JsonNode.Parse(meta) is not JsonObject jNode) return;

			if (TryGetPositiveIntFromJson(jNode, "columns", out int c))
				cols = c;

			if (TryGetPositiveIntFromJson(jNode, "rows", out int r))
				rows = r;
		}
		catch
		{
		}
	}

	private bool TryGetPositiveIntFromJson(JsonObject obj, string key, out int value)
	{
		value = 0;
		if (!obj.TryGetPropertyValue(key, out var node)) return false;
		if (node == null) return false;
		if (!int.TryParse(node.ToString(), out int parsed)) return false;
		if (parsed <= 0) return false;
		value = parsed;
		return true;
	}

	public void SetMirrorModeDomain(MirrorMode mode)
	{
		if (GameHost.Instance == null) return;
		GameHost.Instance.EditorMirrorMode = mode;
		GameHost.Instance.UpdateSymmetryPivotVisuals();
		GameHost.Instance.InvalidateSelectionHighlightMesh();
	}

	public void SetSymmetrySpokesDomain(int spokes)
	{
		if (GameHost.Instance == null) return;
		GameHost.Instance.EditorSymmetryFolds = spokes;
		GameHost.Instance.InvalidateSelectionHighlightMesh();
	}

	public void SetPolarRingSpacingDomain(float spacing)
	{
		if (GameHost.Instance == null) return;
		EditorPolarRingSpacing = spacing;
		GameHost.Instance.GroundTerrain?.SetPolarRingSpacing(spacing);
	}

	public bool ImportTextureAssetFromExtensionDomain(string sourceFilePath, int slotIndex, string wsPath, GameHost? gameHost, out string cleanBaseName, out string outputRtex, Action<string, string, string, int> updateMetadata)
	{
		cleanBaseName = Path.GetFileNameWithoutExtension(sourceFilePath).ToLowerInvariant().Replace(" ", "_") + ".rtex";
		string texDir = Path.Combine(wsPath, "Assets", "textures");
		Directory.CreateDirectory(texDir);
		outputRtex = Path.Combine(texDir, cleanBaseName);

		string ext = Path.GetExtension(sourceFilePath);
		if (ext.Equals(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			File.Copy(sourceFilePath, outputRtex, true);
		}
		else if (gameHost != null && gameHost.GroundTerrain != null)
		{
			gameHost.GroundTerrain.ProcessAndSaveRawTexture(sourceFilePath, outputRtex);
		}

		if (File.Exists(outputRtex))
		{
			byte[] rtexBytes = File.ReadAllBytes(outputRtex);
			string blake3 = RealmMetadataHelper.ComputeBlake3(rtexBytes, ".rtex");
			updateMetadata?.Invoke("textures", cleanBaseName, blake3, slotIndex);
			return true;
		}

		return false;
	}

	public void UpdateTextureMetadataAndReload(int index, string cleanBaseName, string outputRtex, GameHost? gameHost, Action<string, string, string, int> updateMetadata)
	{
		if (File.Exists(outputRtex))
		{
			byte[] rtexBytes = File.ReadAllBytes(outputRtex);
			string blake3 = RealmMetadataHelper.ComputeBlake3(rtexBytes, ".rtex");
			updateMetadata?.Invoke("textures", cleanBaseName, blake3, index);
		}

		if (gameHost != null && gameHost.GroundTerrain != null)
		{
			gameHost.GroundTerrain.ReloadTerrainTextures(true);
		}
	}

	public void CopyOrProcessTexture(string imagePath, string outputRtex, GameHost? gameHost)
	{
		string ext = Path.GetExtension(imagePath);
		if (ext.Equals(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			File.Copy(imagePath, outputRtex, true);
		}
		else if (gameHost != null && gameHost.GroundTerrain != null)
		{
			gameHost.GroundTerrain.ProcessAndSaveRawTexture(imagePath, outputRtex);
		}
	}

	public string PrepareTextureOutputDir(string cleanBaseName)
	{
		string wsPath = string.IsNullOrEmpty(this._tempWorkspacePath)
			? ProjectSettings.GlobalizePath(this.TempWorkspaceGodotPath)
			: this._tempWorkspacePath;
		string texDir = Path.Combine(wsPath, "Assets", "textures");
		Directory.CreateDirectory(texDir);
		return Path.Combine(texDir, cleanBaseName);
	}

	public string ResolveTextureFileName(string imagePath, string? preferredFileName)
	{
		string resolvedName = !string.IsNullOrWhiteSpace(preferredFileName) && !AssetIndexService.IsHexHash(Path.GetFileNameWithoutExtension(preferredFileName))
			? preferredFileName
			: (GameContext.AssetIndexService?.ResolvePrettyFileName(imagePath, preferredFileName) ?? Path.GetFileName(imagePath));
		return Path.GetFileNameWithoutExtension(resolvedName).ToLowerInvariant().Replace(" ", "_") + ".rtex";
	}

	public string GetManifestBlake3(string workspacePath, string mapTitle, string mapVersion, string author)
	{
		try
		{
			string manifestJsonPath = Path.Combine(workspacePath, "manifest.json");
			if (File.Exists(manifestJsonPath))
			{
				string manifestBlake3 = RealmMetadataHelper.ComputeBlake3(manifestJsonPath);
				if (!string.IsNullOrEmpty(manifestBlake3))
				{
					return manifestBlake3;
				}
			}

			if (Directory.Exists(workspacePath))
			{
				var manifest = MapManifest.CreateFromDirectory(workspacePath, mapTitle, author, mapVersion);
				return manifest.ComputeManifestBlake3();
			}
		}
		catch
		{
		}

		return string.Empty;
	}

	public string GetMapVersionFromMetadata(bool forceReload, string workspacePath)
	{
		long now = Environment.TickCount64;
		if (!forceReload && !string.IsNullOrEmpty(this.CachedMapVersion) && (now - this.LastMapNameCacheTicks < 2000))
		{
			return this.CachedMapVersion;
		}

		try
		{
			var manifest = MapFileService.LoadManifest(workspacePath);
			if (!string.IsNullOrWhiteSpace(manifest.Version))
			{
				this.CachedMapVersion = manifest.Version.Trim();
				return this.CachedMapVersion;
			}

			var metadata = MapFileService.LoadMetadata(workspacePath);
			if (!string.IsNullOrWhiteSpace(metadata.MapProperties?.Version))
			{
				this.CachedMapVersion = metadata.MapProperties.Version.Trim();
				return this.CachedMapVersion;
			}

			this.CachedMapVersion = "1.0.0";
			return "1.0.0";
		}
		catch
		{
			this.CachedMapVersion = "1.0.0";
			return "1.0.0";
		}
	}


	public string SanitizeMapName(string? candidateName, string defaultWorkspaceFolder)
	{
		if (string.IsNullOrWhiteSpace(candidateName))
		{
			return "Untitled Map";
		}

		string sanitized = candidateName.Replace(defaultWorkspaceFolder, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
		return string.IsNullOrEmpty(sanitized) ? "Untitled Map" : sanitized;
	}

	public bool TryGetMapNameFromManifest(string workspacePath, out string mapName)
	{
		var manifest = MapFileService.LoadManifest(workspacePath);
		return TrySanitizeCandidate(manifest?.MapName, out mapName);
	}

	public bool TryGetMapNameFromMetadata(string workspacePath, out string mapName)
	{
		var metadata = MapFileService.LoadMetadata(workspacePath);
		return TrySanitizeCandidate(metadata?.MapProperties?.MapName, out mapName);
	}

	public bool TryGetMapNameFromActiveHost(out string mapName)
	{
		mapName = string.Empty;
		if (!string.IsNullOrEmpty(ECSContext.ActiveMapName))
		{
			string candidate = Path.GetFileNameWithoutExtension(ECSContext.ActiveMapName);
			return TrySanitizeCandidate(candidate, out mapName);
		}

		return false;
	}

	public string UpdateCachedMapName(string mapName, long nowTicks)
	{
		this.CachedMapName = mapName;
		this.LastMapNameCacheTicks = nowTicks;
		return mapName;
	}


	public static bool TrySanitizeCandidate(string? candidateName, out string sanitizedMapName)
	{
		sanitizedMapName = string.Empty;
		if (string.IsNullOrWhiteSpace(candidateName))
		{
			return false;
		}

		string cleaned = candidateName.Replace(MapWorkspaceService.DefaultWorkspaceFolder, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
		if (string.IsNullOrEmpty(cleaned))
		{
			return false;
		}

		sanitizedMapName = cleaned;
		return true;
	}


	public string RemoveUnitObjectAttachmentDomain(string unitId, string socket, string attachmentId, string? parentAttachmentId = null)
	{
		if (string.IsNullOrEmpty(unitId) || string.IsNullOrEmpty(attachmentId)) return unitId;

		var registryService = GameContext.RegistryService;
		var unitRegistry = registryService.UnitRegistry;
		var buildingRegistry = registryService.BuildingRegistry;

		string regKey = unitId;
		bool isBuildingMeta = false;

		if (unitRegistry.ContainsKey((StringName)unitId))
		{
			regKey = unitId;
		}
		else if (buildingRegistry != null && buildingRegistry.ContainsKey((StringName)unitId))
		{
			regKey = unitId;
			isBuildingMeta = true;
		}
		else
		{
			string cleanId = Path.GetFileNameWithoutExtension(unitId);
			if (unitRegistry.ContainsKey((StringName)cleanId))
			{
				regKey = cleanId;
			}
			else if (buildingRegistry != null && buildingRegistry.ContainsKey((StringName)cleanId))
			{
				regKey = cleanId;
				isBuildingMeta = true;
			}
			else
			{
				foreach (var k in unitRegistry.Keys)
				{
					string kStr = k.ToString();
					if (kStr.Equals(unitId, StringComparison.OrdinalIgnoreCase) ||
						kStr.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
						Path.GetFileNameWithoutExtension(kStr).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
					{
						regKey = kStr;
						break;
					}
				}

				if (!isBuildingMeta && buildingRegistry != null)
				{
					foreach (var k in buildingRegistry.Keys)
					{
						string kStr = k.ToString();
						if (kStr.Equals(unitId, StringComparison.OrdinalIgnoreCase) ||
							kStr.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
							Path.GetFileNameWithoutExtension(kStr).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
						{
							regKey = kStr;
							isBuildingMeta = true;
							break;
						}
					}
				}
			}
		}

		if (isBuildingMeta && buildingRegistry != null && buildingRegistry.TryGetValue((StringName)regKey, out var bMeta))
		{
			bMeta.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId);
			buildingRegistry[(StringName)regKey] = bMeta;
		}
		else if (unitRegistry.TryGetValue((StringName)regKey, out var uMeta))
		{
			uMeta.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId);
			unitRegistry[(StringName)regKey] = uMeta;
		}

		string wsPath = string.IsNullOrEmpty(this._tempWorkspacePath)
			? ProjectSettings.GlobalizePath(this.TempWorkspaceGodotPath)
			: this._tempWorkspacePath;
		string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
		if (!File.Exists(metadataPath)) return regKey;

		GameContext.MetadataService.UpdateMetadata(wsPath, meta =>
		{
			bool updated = meta.UpdateUnit(unitId, u =>
			{
				u.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId);
				return u;
			});
			if (!updated)
				updated = meta.UpdateBuilding(unitId, b =>
				{
					b.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId);
					return b;
				});
			if (!updated)
				updated = meta.UpdateUnit(regKey, u =>
				{
					u.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId);
					return u;
				});
			if (!updated)
				meta.UpdateBuilding(regKey, b =>
				{
					b.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId);
					return b;
				});
		});

		long getWriteTimeSafe = File.Exists(metadataPath) ? File.GetLastWriteTimeUtc(metadataPath).Ticks : 0;
		this.LastMetadataSyncTime = getWriteTimeSafe;

		return regKey;
	}

	public string RestoreUnitObjectAttachmentsDomain(string targetId, UnitObjectAttachments? snapshot)
	{
		if (string.IsNullOrEmpty(targetId)) return targetId;

		var registryService = GameContext.RegistryService;
		var unitRegistry = registryService.UnitRegistry;
		var buildingRegistry = registryService.BuildingRegistry;

		string regKey = targetId;
		bool isBuildingMeta = false;

		if (unitRegistry.ContainsKey((StringName)targetId))
		{
			regKey = targetId;
		}
		else if (buildingRegistry != null && buildingRegistry.ContainsKey((StringName)targetId))
		{
			regKey = targetId;
			isBuildingMeta = true;
		}
		else
		{
			string cleanId = Path.GetFileNameWithoutExtension(targetId);
			if (unitRegistry.ContainsKey((StringName)cleanId))
			{
				regKey = cleanId;
			}
			else if (buildingRegistry != null && buildingRegistry.ContainsKey((StringName)cleanId))
			{
				regKey = cleanId;
				isBuildingMeta = true;
			}
			else
			{
				foreach (var k in unitRegistry.Keys)
				{
					string kStr = k.ToString();
					if (kStr.Equals(targetId, StringComparison.OrdinalIgnoreCase) ||
						kStr.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
						Path.GetFileNameWithoutExtension(kStr).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
					{
						regKey = kStr;
						break;
					}
				}

				if (!isBuildingMeta && buildingRegistry != null)
				{
					foreach (var k in buildingRegistry.Keys)
					{
						string kStr = k.ToString();
						if (kStr.Equals(targetId, StringComparison.OrdinalIgnoreCase) ||
							kStr.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
							Path.GetFileNameWithoutExtension(kStr).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
						{
							regKey = kStr;
							isBuildingMeta = true;
							break;
						}
					}
				}
			}
		}

		if (isBuildingMeta && buildingRegistry != null && buildingRegistry.TryGetValue((StringName)regKey, out var bMeta))
		{
			bMeta.ObjectAttachments = (snapshot != null && snapshot.HasAny()) ? snapshot?.Clone() : null;
			buildingRegistry[(StringName)regKey] = bMeta;
		}
		else if (unitRegistry.TryGetValue((StringName)regKey, out var uMeta))
		{
			uMeta.ObjectAttachments = (snapshot != null && snapshot.HasAny()) ? snapshot?.Clone() : null;
			unitRegistry[(StringName)regKey] = uMeta;
		}

		string wsPath = string.IsNullOrEmpty(this._tempWorkspacePath)
			? ProjectSettings.GlobalizePath(this.TempWorkspaceGodotPath)
			: this._tempWorkspacePath;
		string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
		if (File.Exists(metadataPath))
		{
			GameContext.MetadataService.UpdateMetadata(wsPath, meta =>
			{
				var atts = (snapshot != null && snapshot.HasAny()) ? snapshot?.Clone() : null;
				bool updated = meta.UpdateUnit(targetId, u =>
				{
					u.ObjectAttachments = atts;
					return u;
				});
				if (!updated)
					updated = meta.UpdateBuilding(targetId, b =>
					{
						b.ObjectAttachments = atts;
						return b;
					});
				if (!updated)
					updated = meta.UpdateUnit(regKey, u =>
					{
						u.ObjectAttachments = atts;
						return u;
					});
				if (!updated)
					meta.UpdateBuilding(regKey, b =>
					{
						b.ObjectAttachments = atts;
						return b;
					});
			});

			long getWriteTimeSafe = File.Exists(metadataPath) ? File.GetLastWriteTimeUtc(metadataPath).Ticks : 0;
			this.LastMetadataSyncTime = getWriteTimeSafe;
		}

		return regKey;
	}

	public (bool Success, string Message, string FileName, string Blake3) ImportSkyboxAsset(string sourceFilePath, string wsPath)
	{
		try
		{
			string fileName = Path.GetFileName(sourceFilePath);
			string skyboxesDir = Path.Combine(wsPath, "Assets", "skyboxes");
			Directory.CreateDirectory(skyboxesDir);
			string targetPath = Path.Combine(skyboxesDir, fileName);

			File.Copy(sourceFilePath, targetPath, true);

			byte[] bytes = File.ReadAllBytes(targetPath);
			string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, fileName);

			return (true, "", fileName, blake3);
		}
		catch (Exception ex)
		{
			return (false, ex.Message, null, null);
		}
	}

	public (bool Success, string Message, string FileName, string Blake3) ImportSpritesheetAsset(string sourceFilePath, string wsPath)
	{
		try
		{
			string baseName = Path.GetFileNameWithoutExtension(sourceFilePath);
			string fileName = baseName + ".png";
			string vfxDir = Path.Combine(wsPath, "Assets", "vfx");
			Directory.CreateDirectory(vfxDir);
			string targetPath = Path.Combine(vfxDir, fileName);

			var img = Image.LoadFromFile(sourceFilePath);
			if (img != null)
			{
				img.SavePng(targetPath);
			}
			else
			{
				File.Copy(sourceFilePath, targetPath, true);
			}

			byte[] bytes = File.ReadAllBytes(targetPath);
			string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, ".png");

			return (true, "", fileName, blake3);
		}
		catch (Exception ex)
		{
			return (false, ex.Message, null, null);
		}
	}

	public (bool Success, string Message, string FileName, string Blake3) ImportIconAsset(string sourceFilePath, string wsPath)
	{
		try
		{
			string baseName = Path.GetFileNameWithoutExtension(sourceFilePath);
			string fileName = baseName + ".png";
			string iconsDir = Path.Combine(wsPath, "Assets", "icons");
			Directory.CreateDirectory(iconsDir);
			string targetPath = Path.Combine(iconsDir, fileName);

			var img = Image.LoadFromFile(sourceFilePath);
			if (img != null)
			{
				img.SavePng(targetPath);
			}
			else
			{
				File.Copy(sourceFilePath, targetPath, true);
			}

			byte[] bytes = File.ReadAllBytes(targetPath);
			string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, ".png");

			return (true, "", fileName, blake3);
		}
		catch (Exception ex)
		{
			return (false, ex.Message, null, null);
		}
	}

	public long GetLastWriteTimeSafe(string path)
	{
		if (!File.Exists(path)) return 0;
		return File.GetLastWriteTimeUtc(path).Ticks;
	}

	public long GetMaxTerrainWriteTime(string baseTerrainJsonPath)
	{
		long maxTime = GetLastWriteTimeSafe(baseTerrainJsonPath);
		string dir = Path.GetDirectoryName(baseTerrainJsonPath);
		if (!string.IsNullOrEmpty(dir))
		{
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(Path.Combine(dir, "terrain_heights.exr")));
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(Path.Combine(dir, "terrain_water.exr")));
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(Path.Combine(dir, "terrain_splat_indices.exr")));
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(Path.Combine(dir, "terrain_splat_weights.exr")));
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(Path.Combine(dir, "terrain_cliff_splat_indices.exr")));
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(Path.Combine(dir, "terrain_cliff_splat_weights.exr")));
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(Path.Combine(dir, "terrain_splat_indices.png")));
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(Path.Combine(dir, "terrain_splat_weights.png")));
			maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(Path.Combine(dir, "terrain_pathing.png")));
		}

		return maxTime;
	}

	public void ClearDirectoryReadOnly(string targetDir)
	{
		foreach (var file in Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories))
		{
			var attrs = File.GetAttributes(file);
			if ((attrs & FileAttributes.ReadOnly) != 0)
			{
				File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
			}
		}
	}

	public void ClearTempWorkspaceExternalDomain(Action<string> onError)
	{
		if (string.IsNullOrEmpty(this._tempWorkspacePath) || !Directory.Exists(this._tempWorkspacePath)) return;
		try
		{
			ClearDirectoryReadOnly(this._tempWorkspacePath);
			foreach (var file in Directory.GetFiles(this._tempWorkspacePath, "*", SearchOption.AllDirectories))
			{
				var fileAttributes = File.GetAttributes(file);
				if ((fileAttributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
				{
					File.SetAttributes(file, fileAttributes & ~FileAttributes.ReadOnly);
				}

				File.Delete(file);
			}

			foreach (var directory in Directory.GetDirectories(this._tempWorkspacePath))
			{
				Directory.Delete(directory, true);
			}
		}
		catch (Exception ex)
		{
			onError?.Invoke(ex.Message);
		}

		this.LastTerrainSyncTime = 0;
		this.LastMetadataSyncTime = 0;
	}

	public void PreLoadTempWorkspaceMapDomain()
	{
		MapWorkspaceService.EnsureGlbAssetsOptimized(this._tempWorkspacePath);
		MapWorkspaceService.EnsurePngAssetsConverted(this._tempWorkspacePath);
	}

	public void PostLoadTempWorkspaceMapDomain()
	{
		string terrainPath = Path.Combine(this._tempWorkspacePath, "terrain.json");
		if (GameHost.Instance != null && File.Exists(terrainPath))
		{
			GameHost.Instance.LoadMapFromFile(terrainPath);
		}

		this.LastTerrainSyncTime = GetMaxTerrainWriteTime(terrainPath);
		this.LastMetadataSyncTime = GetLastWriteTimeSafe(Path.Combine(this._tempWorkspacePath, "metadata.json"));
	}

	public void GenerateMapDirectoryDomain(string mapName, string mapDir, string projectRoot, string templateDirSearch)
	{
		Directory.CreateDirectory(mapDir);

		string templateDir = templateDirSearch;
		if (!Directory.Exists(templateDir))
		{
			templateDir = Path.Combine(projectRoot, "..", "MapTemplate");
		}

		string templateScriptPath = Path.Combine(templateDir, "MapScript.cs");

		string scriptContent;
		if (File.Exists(templateScriptPath))
		{
			scriptContent = File.ReadAllText(templateScriptPath).Replace("__MAP_NAME__", mapName);
		}
		else
		{
			scriptContent = $@"namespace Realm.Maps;

using Realm.MapAPI;

public class {mapName} : IMapScript
{{
    public void Initialize(IGameAPI api)
    {{
    }}

    public void Update(IGameAPI api, float delta)
    {{
    }}
}}
";
		}

		File.WriteAllText(Path.Combine(mapDir, "MapScript.cs"), scriptContent);
		Realm.Client.Utils.MapJsonFormatter.SaveFormattedJson(Path.Combine(mapDir, "metadata.json"), "{}");
		Realm.Client.Utils.MapJsonFormatter.SaveFormattedJson(Path.Combine(mapDir, "terrain.json"), "{}");
	}

	public void GenerateVSCodeFilesExternalDomain()
	{
		if (string.IsNullOrEmpty(this._tempWorkspacePath)) return;
		string unitsPath = Path.Combine(this._tempWorkspacePath, "metadata.json");
		Directory.CreateDirectory(this._tempWorkspacePath);
		MapWorkspaceService.SetupWorkspace(this._tempWorkspacePath, "MapScript");
		string terrainPath = Path.Combine(this._tempWorkspacePath, "terrain.json");
		this.LastTerrainSyncTime = GetMaxTerrainWriteTime(terrainPath);
		this.LastMetadataSyncTime = GetLastWriteTimeSafe(unitsPath);
	}

	public bool CheckAndHandleEditorClosure()
	{
		string cleanExitFile = ProjectSettings.GlobalizePath("user://editor_clean_exit.txt");
		if (File.Exists(cleanExitFile))
		{
			try
			{
				File.Delete(cleanExitFile);
			}
			catch
			{
			}

			return true;
		}

		return false;
	}

	public void SyncWasmFiles()
	{
		try
		{
			var wasmPath = Path.Combine(this._tempWorkspacePath, "src", "bin", "Release", "net10.0", "wasi-wasm", "native", "Realm.MapProject.wasm");
			var wasmDest = Path.Combine(this._tempWorkspacePath, "Map.wasm");
			if (File.Exists(wasmPath))
			{
				var fileInfo = new FileInfo(wasmPath);
				if (fileInfo.LastWriteTimeUtc.Ticks > this.LastMetadataSyncTime)
				{
					File.Copy(wasmPath, wasmDest, true);
					this.LastMetadataSyncTime = fileInfo.LastWriteTimeUtc.Ticks;
				}
			}
		}
		catch
		{
		}
	}

	public void UpdateWorkspaceModTime()
	{
		try
		{
			var wsDir = new DirectoryInfo(this._tempWorkspacePath);
			wsDir.LastWriteTimeUtc = DateTime.UtcNow;
		}
		catch
		{
		}
	}

	public bool HasDecalOverrides(int overrideCols, int overrideRows, float overrideFps, bool? overrideSubframeBlend)
	{
		return overrideCols > 0 || overrideRows > 0 || overrideFps > 0.001f || overrideSubframeBlend.HasValue;
	}

	public string ResolveDecalKeyFromMetadata(string decalId, string wsPath)
	{
		try
		{
			var metadata = MapFileService.LoadMetadata(wsPath);
			if (metadata?.Decals != null)
			{
				string filename = Path.GetFileName(decalId);
				string baseKey = Path.GetFileNameWithoutExtension(decalId);
				DecalMetadata meta = null;

				if (metadata.Decals.TryGetValue(decalId, out var d0)) meta = d0;
				else if (metadata.Decals.TryGetValue(filename, out var d1)) meta = d1;
				else if (metadata.Decals.TryGetValue(baseKey, out var d2)) meta = d2;

				if (meta != null && !string.IsNullOrWhiteSpace(meta.TexturePath))
				{
					return meta.TexturePath;
				}
			}
		}
		catch
		{
		}

		return decalId;
	}

	public string FindDecalFile(string targetKey, string wsPath)
	{
		string targetFilename = Path.GetFileName(targetKey);
		string candidate1 = Path.Combine(wsPath, "Assets", "decals", targetFilename);
		if (File.Exists(candidate1)) return candidate1;

		if (!targetFilename.Contains('.'))
		{
			string candidate2 = Path.Combine(wsPath, "Assets", "decals", targetFilename + ".png");
			if (File.Exists(candidate2)) return candidate2;
			string candidateRtex = Path.Combine(wsPath, "Assets", "decals", targetFilename + ".rtex");
			if (File.Exists(candidateRtex)) return candidateRtex;
		}

		if (Path.IsPathRooted(targetKey) && File.Exists(targetKey)) return targetKey;

		string candidate3 = Path.Combine(wsPath, targetKey);
		if (File.Exists(candidate3)) return candidate3;

		return "res://icon.svg";
	}

	public void RestoreLoadedCoordinates()
	{
		var loadedCoordinates = GameContext.SaveLoadService.GetLastLoadedCoordinates();
		EditorCoordinates.Clear();
		EditorCoordinates.AddRange(loadedCoordinates.Select(r => new EditorCoordinate { Name = r.Name, MinX = r.MinX, MinZ = r.MinZ, MaxX = r.MaxX, MaxZ = r.MaxZ }));
	}

	private string ApplyModelAssetExtension(string filename)
	{
		if (filename.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || filename.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
		{
			return Path.GetFileNameWithoutExtension(filename) + ".rmesh";
		}

		if (filename.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) || filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || filename.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
		{
			return filename;
		}

		if (!filename.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
		{
			return filename + ".rmesh";
		}

		return filename;
	}

	public string NormalizeModelAssetKey(string pathOrId)
	{
		if (string.IsNullOrEmpty(pathOrId)) return "";
		if (NormalizedAssetKeyCache.TryGetValue(pathOrId, out string cached))
		{
			return cached;
		}

		string trimmed = pathOrId.Trim().Replace('\\', '/');
		if (trimmed.Contains('/'))
		{
			string prefix = trimmed.Substring(0, trimmed.IndexOf('/'));
			if (AssetIndexService.ValidAssetPrefixes.Contains(prefix))
			{
				string lower = trimmed.ToLowerInvariant();
				NormalizedAssetKeyCache[pathOrId] = lower;
				return lower;
			}
		}

		string filename = Path.GetFileName(trimmed);
		filename = ApplyModelAssetExtension(filename);

		string result = filename.ToLowerInvariant();
		NormalizedAssetKeyCache[pathOrId] = result;
		return result;
	}

	public bool ConsumeModelOffsetSavePending()
	{
		if (ModelYOffsetSavePending || ModelCollisionCircleSavePending)
		{
			ModelYOffsetSavePending = false;
			ModelCollisionCircleSavePending = false;
			return true;
		}
		return false;
	}

	public string GetInitialDirectoryDomain(string lastOpenedFolder, string documentsDir, Func<string, bool> isRestrictedSaveDirectory)
	{
		if (!string.IsNullOrEmpty(LastUsedFolder) && !isRestrictedSaveDirectory(LastUsedFolder) && Directory.Exists(LastUsedFolder))
		{
			return LastUsedFolder;
		}

		if (!string.IsNullOrEmpty(CurrentSourceFolder) && !isRestrictedSaveDirectory(CurrentSourceFolder) && Directory.Exists(CurrentSourceFolder))
		{
			return CurrentSourceFolder;
		}

		if (!string.IsNullOrEmpty(lastOpenedFolder) && !isRestrictedSaveDirectory(lastOpenedFolder) && Directory.Exists(lastOpenedFolder))
		{
			return lastOpenedFolder;
		}

		return documentsDir;
	}

	public bool IsRestrictedSaveDirectoryDomain(string fullPath, string tempWorkspace, string mapBackups, string userDir, string resDir)
	{
		if (fullPath.Equals(tempWorkspace, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(tempWorkspace + "/", StringComparison.OrdinalIgnoreCase) ||
			fullPath.Equals(mapBackups, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(mapBackups + "/", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		if (fullPath.Equals(userDir, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(userDir + "/", StringComparison.OrdinalIgnoreCase) ||
			fullPath.Equals(resDir, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(resDir + "/", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return false;
	}

	public string GetDefaultDevelopmentMapDirectoryDomain(string mapName, string docsDir)
	{
		string cleanName = string.Join("_", mapName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
		if (string.IsNullOrEmpty(cleanName) || cleanName.Equals("Untitled Map", StringComparison.OrdinalIgnoreCase)) cleanName = "new_map";

		return Path.Combine(docsDir, cleanName);
	}

	public (NSec.Cryptography.Key key, Realm.Shared.Metadata.AuthorshipKeyData data, string keyPath, bool createdNew) GetOrGenerateAuthorshipKeyDomain(string defaultUsername, string keyDir)
	{
		return Realm.Shared.Distribution.AuthorshipKeyHelper.GetOrGenerateKeyInfo(keyDir, defaultUsername);
	}

	public async Task<bool> PerformCompilationStepAsyncDomain(string workspace, Func<string, string> findProjectFile, Func<string, bool> tryBypassCompilation, Func<string, bool> checkMapApiDependency, Func<string, string, Task<bool>> executeDotnetPublishAsync, Action<string> onStatusError, Action<string> onLog)
	{
		try
		{
			if (!Directory.Exists(workspace)) return true; // Nothing to compile

			Realm.Client.Services.MapWorkspaceService.EnsureWitFile(workspace);
			Realm.Client.Services.MapWorkspaceService.EnsureWasmEntryPoint(workspace);
			Realm.Client.Services.MapWorkspaceService.EnsureCsproj(workspace, Path.GetFileName(workspace));

			string csproj = findProjectFile(workspace);
			if (string.IsNullOrEmpty(csproj)) return false;

			if (tryBypassCompilation(workspace)) return true;

			if (!checkMapApiDependency(workspace)) return false;

			return await executeDotnetPublishAsync(workspace, csproj);
		}
		catch (Exception ex)
		{
			onStatusError(ex.Message);
			onLog(ex.ToString());
			return false;
		}
	}

	public string FindProjectFileDomain(string workspace, Action<string> onErrorMessage)
	{
		var csprojFiles = Directory.GetFiles(workspace, "*.csproj", SearchOption.TopDirectoryOnly);
		if (csprojFiles.Length == 0)
		{
			onErrorMessage("[MapEditorHUD] ERROR: No .csproj found in workspace, cannot compile map script");
			return null;
		}

		return System.Linq.Enumerable.FirstOrDefault(csprojFiles, f => Path.GetFileName(f).Equals("MapScript.csproj", StringComparison.OrdinalIgnoreCase)) ?? csprojFiles[0];
	}

	public bool TryBypassCompilationDomain(string workspace, Func<string, string> findExistingWasmFile, Func<string, DateTime, bool> hasNewerSourceFiles, Action onBypassSuccess)
	{
		string existingWasm = findExistingWasmFile(workspace);
		if (string.IsNullOrEmpty(existingWasm) || !File.Exists(existingWasm)) return false;

		DateTime wasmTime = File.GetLastWriteTimeUtc(existingWasm);
		if (hasNewerSourceFiles(workspace, wasmTime)) return false;

		string targetWasmInTemp = Path.Combine(workspace, "bin", Path.GetFileName(existingWasm));
		if (!File.Exists(targetWasmInTemp))
		{
			Directory.CreateDirectory(Path.GetDirectoryName(targetWasmInTemp));
			File.Copy(existingWasm, targetWasmInTemp, true);
		}

		onBypassSuccess();
		return true;
	}
	public void RestoreEditorStateDomain(float waterHeight, int paintTextureIndex, int cliffPaintTextureIndex, CopiedAreaTemplate copiedArea, System.Numerics.Vector3? selectionStart, System.Numerics.Vector3? selectionEnd)
	{
		this.EditorWaterHeight = waterHeight;
		this.EditorPaintTextureIndex = paintTextureIndex;
		this.EditorCliffPaintTextureIndex = cliffPaintTextureIndex;
		this.CopiedArea = copiedArea;
		SetSelectionStart(selectionStart != null ? new Godot.Vector2I((int)selectionStart.Value.X, (int)selectionStart.Value.Z) : (Godot.Vector2I?)null);
		SetSelectionEnd(selectionEnd != null ? new Godot.Vector2I((int)selectionEnd.Value.X, (int)selectionEnd.Value.Z) : (Godot.Vector2I?)null);
	}

	public void InitializeDefaultTerrainStateDomain(int defaultWidth, int defaultDepth)
	{
		if (!GameContext.World.Has<Realm.Ecs.Components.Terrain.TerrainState>(ECSContext.WorldEntity)) return;

		float[,] defaultHeights = new float[defaultWidth, defaultDepth];
		int[,] defaultPathing = new int[defaultWidth, defaultDepth];

		for (int z = 0; z < defaultDepth; z++)
		{
			for (int x = 0; x < defaultWidth; x++)
			{
				defaultHeights[x, z] = 0.0f;
				defaultPathing[x, z] = Realm.Client.EditableTerrain.GetDefaultPathingCode(Realm.Ecs.Components.Terrain.WaterType.None);
			}
		}

		ref var ts = ref GameContext.World.Get<Realm.Ecs.Components.Terrain.TerrainState>(ECSContext.WorldEntity);
		ts.SetHeights(defaultHeights);
		ts.PathingCodes = defaultPathing;
		GameContext.World.Set(ECSContext.WorldEntity, ts);
	}

	public void UpdateMetadataJsonAssetDomain(string category, string fileName, string blake3Hash, string subCategory = null, int columns = 0, int rows = 0, int targetSlot = -1)
	{
		try
		{
			string wsPath = string.IsNullOrEmpty(_tempWorkspacePath)
				? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath)
				: _tempWorkspacePath;

			var metadata = MapFileService.LoadMetadata(wsPath);

			if (category == "glb" && GameHost.Instance != null)
			{
				string normKey = NormalizeModelAssetKey(fileName);
				if (!GameContext.ModelOverrideService.ModelObstacleRadii.ContainsKey(normKey))
				{
					string modelPath = Path.Combine(wsPath, "Assets", "models", subCategory ?? "", fileName);
					Node3D modelNode = ModelCache.GetModel(modelPath) as Node3D;
					if (modelNode != null)
					{
						float radius = GameHost.Instance.MeasureModelRadius(modelNode);
						modelNode.Free();
						if (radius > 0f)
						{
							float rounded = (float)Math.Round(radius, 2);
							GameContext.ModelOverrideService.ModelObstacleRadii[normKey] = rounded;
							metadata.SetModelObstacleRadius(normKey, rounded);
						}
					}
				}
			}

			if (!string.IsNullOrEmpty(subCategory) && category == "glb")
			{
				float defaultScale = subCategory.ToLowerInvariant() switch
				{
					"resources" => 2.75f,
					"buildings" => 1.5f,
					"props" => 1.25f,
					"units" => 1.0f,
					_ => 1.0f
				};

				string modelFullPath = Path.Combine(wsPath, "Assets", "models", subCategory, fileName);
				if (!File.Exists(modelFullPath))
				{
					modelFullPath = Path.Combine(wsPath, "Assets", "glb", subCategory, fileName);
				}

				var (minY, autoYOffset) = ModelCache.CalculateModelBounds(modelFullPath, defaultScale);
				string subLower = subCategory.ToLowerInvariant();
				bool isPropOrRes = subLower == "props" || subLower == "resources" || subLower == "attachments" || subLower == "weapons" || subLower == "items" || subLower == "projectiles";

				metadata.SetModelYOffset(fileName, autoYOffset);
				metadata.SetModelScale(fileName, defaultScale);

				GameHost.Instance?.SetModelYOffset(fileName, autoYOffset);
				GameHost.Instance?.SetModelScale(fileName, defaultScale);

				string unitId = Path.GetFileNameWithoutExtension(fileName);
				if (subLower == "buildings")
				{
					var b = metadata.FindBuilding(unitId);
					if (b == null)
					{
						b = new UnitMetadata
						{
							TemplateID = unitId,
							Name = unitId,
							Scale = defaultScale,
							YOffset = autoYOffset,
							PathingType = (int)TerrainPathingFlags.Buildable,
							ModelPath = fileName,
							NormalizeLuminance = true,
							IgnorePlayerColor = isPropOrRes
						};
						metadata.AddOrUpdateBuilding(b);
					}
					else if (autoYOffset != 0f)
					{
						b.YOffset = autoYOffset;
					}
				}
				else if (subLower == "props")
				{
					var p = metadata.FindProp(unitId);
					if (p == null)
					{
						p = new PropMetadata
						{
							TemplateID = unitId,
							Name = unitId,
							Scale = defaultScale,
							YOffset = autoYOffset,
							PathingType = 0xFF,
							ModelPath = fileName,
							NormalizeLuminance = true,
							IgnorePlayerColor = isPropOrRes
						};
						metadata.AddOrUpdateProp(p);
					}
					else if (autoYOffset != 0f)
					{
						p.YOffset = autoYOffset;
					}
				}
				else if (subLower == "resources")
				{
					var r = metadata.FindResource(unitId);
					if (r == null)
					{
						r = new ResourceMetadata
						{
							TemplateID = unitId,
							Name = unitId,
							Scale = defaultScale,
							YOffset = autoYOffset,
							PathingType = 0xFF,
							ModelPath = fileName,
							NormalizeLuminance = true,
							IgnorePlayerColor = isPropOrRes
						};
						metadata.AddOrUpdateResource(r);
					}
					else if (autoYOffset != 0f)
					{
						r.YOffset = autoYOffset;
					}
				}
				else
				{
					var u = metadata.FindUnit(unitId);
					if (u == null)
					{
						u = new UnitMetadata
						{
							TemplateID = unitId,
							Name = unitId,
							Scale = defaultScale,
							YOffset = autoYOffset,
							PathingType = (int)(TerrainPathingFlags.Ground | TerrainPathingFlags.ShallowWater),
							ModelPath = fileName,
							NormalizeLuminance = true,
							IgnorePlayerColor = isPropOrRes
						};
						metadata.AddOrUpdateUnit(u);
					}
					else if (autoYOffset != 0f)
					{
						u.YOffset = autoYOffset;
					}
				}
			}

			if (category == "textures" || category == "terrain" || category == "Terrain")
			{
				string slug = TemplateIDHelper.GenerateSlug(fileName);
				string terrainTemplateId = TemplateIDHelper.NormalizeTemplateID("terrain", slug);
				int swatchIdx = targetSlot >= 0 ? targetSlot : 0;
				metadata.Textures ??= new System.Collections.Generic.Dictionary<string, TextureMetadata>(StringComparer.OrdinalIgnoreCase);
				if (!metadata.Textures.TryGetValue(terrainTemplateId, out var texMeta) || texMeta == null)
				{
					if (!metadata.Textures.TryGetValue(fileName, out texMeta) || texMeta == null)
					{
						texMeta = new TextureMetadata();
					}
					else
					{
						metadata.Textures.Remove(fileName);
					}

					metadata.Textures[terrainTemplateId] = texMeta;
				}

				texMeta.TexturePath = fileName;
				texMeta.SwatchIndex = swatchIdx;

				string texPath = Path.Combine(wsPath, "Assets", "textures", fileName);
				if (File.Exists(texPath))
				{
					texMeta.ScaleFactor = TextureConverter.CalculateLuminanceScaleFactor(texPath);
				}
			}

			MapFileService.SaveMetadata(wsPath, metadata);
			MapAssetHelper.UpdateManifestAsset(wsPath, category, fileName, blake3Hash);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to update metadata.json asset: {ex.Message}");
		}
	}
}
