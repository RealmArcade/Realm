using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;
using Realm.Shared.ModelOptimization;

namespace Realm.Client.Services.ModelOptimization;

public partial class GltfDocumentExtensionMsftLod : GltfDocumentExtension
{
	private static bool _isRegistered = false;
	private static readonly object _lock = new object();

	public static void RegisterExtension()
	{
		lock (_lock)
		{
			if (!_isRegistered)
			{
				GltfDocument.RegisterGltfDocumentExtension(new GltfDocumentExtensionMsftLod(), true);
				_isRegistered = true;
			}
		}
	}

	public override string[] _GetSupportedExtensions()
	{
		return new string[] { "MSFT_lod" };
	}

	public override Error _ImportPreflight(GltfState state, string[] extensions)
	{
		return Error.Ok;
	}

	public override Error _ImportPostParse(GltfState state)
	{
		EnsureAllNodesInRootOrChildren(state);
		return Error.Ok;
	}

	private static void EnsureAllNodesInRootOrChildren(GltfState state)
	{
		if (state == null) return;
		var gltfNodes = state.GetNodes();
		if (gltfNodes == null || gltfNodes.Count == 0) return;

		var rootNodes = state.GetRootNodes();
		var existingRoots = rootNodes != null ? new HashSet<int>(rootNodes) : new HashSet<int>();
		var childNodes = new HashSet<int>();

		for (int i = 0; i < gltfNodes.Count; i++)
		{
			var node = gltfNodes[i];
			if (node == null) continue;
			var children = node.Children;
			if (children != null)
			{
				foreach (int childIdx in children)
				{
					childNodes.Add(childIdx);
				}
			}
		}

		var unparentedIndices = new List<int>();
		for (int i = 0; i < gltfNodes.Count; i++)
		{
			if (!existingRoots.Contains(i) && !childNodes.Contains(i))
			{
				unparentedIndices.Add(i);
			}
		}

		if (unparentedIndices.Count == 0) return;

		if (rootNodes != null && rootNodes.Length > 0)
		{
			int primaryRootIdx = rootNodes[0];
			if (primaryRootIdx >= 0 && primaryRootIdx < gltfNodes.Count && gltfNodes[primaryRootIdx] is GltfNode primaryRootNode)
			{
				var currentChildren = new List<int>(primaryRootNode.Children ?? System.Array.Empty<int>());
				currentChildren.AddRange(unparentedIndices);
				primaryRootNode.Children = currentChildren.ToArray();
				return;
			}
		}

		var newRoots = new List<int>(existingRoots);
		newRoots.AddRange(unparentedIndices);
		state.SetRootNodes(newRoots.ToArray());
	}

	public override Error _ParseNodeExtensions(GltfState state, GltfNode gltfNode, Dictionary extensions)
	{
		if (extensions != null && extensions.ContainsKey("MSFT_lod"))
		{
			gltfNode.SetAdditionalData("MSFT_lod", extensions["MSFT_lod"]);
		}

		return Error.Ok;
	}


	public static void ProcessImportedScene(GltfState state, Node root)
	{
		if (root == null || state == null)
		{
			return;
		}

		try
		{
			var gltfNodes = state.GetNodes();
			var gltfMeshes = state.GetMeshes();
			var meshInstances = new List<MeshInstance3D>();
			CollectMeshInstances(root, meshInstances);

			var masterGltfNodes = new System.Collections.Generic.Dictionary<string, (int NodeIndex, GltfNode Node)>();
			var companionLodNodes = new System.Collections.Generic.Dictionary<string, SortedDictionary<int, (int NodeIndex, GltfNode Node)>>();
			CategorizeGltfNodes(gltfNodes, masterGltfNodes, companionLodNodes);

			foreach (var kvp in masterGltfNodes)
			{
				ProcessMasterNode(kvp.Key, kvp.Value, meshInstances, companionLodNodes, gltfMeshes, root);
			}

			ConfigureExistingLodNodes(meshInstances);
		}
		catch
		{
		}
	}

	private static void CategorizeGltfNodes(
		Array<GltfNode> gltfNodes, System.Collections.Generic.Dictionary<string, (int NodeIndex, GltfNode Node)> masterGltfNodes, System.Collections.Generic.Dictionary<string, SortedDictionary<int, (int NodeIndex, GltfNode Node)>> companionLodNodes)
	{
		if (gltfNodes == null) return;

		for (int n = 0; n < gltfNodes.Count; n++)
		{
			var gNode = gltfNodes[n];
			if (gNode == null) continue;
			string rawName = !string.IsNullOrEmpty(gNode.OriginalName) ? gNode.OriginalName : gNode.ResourceName;
			if (string.IsNullOrEmpty(rawName))
				continue;

			var info = TryParseLodInfo(rawName);
			if (info == null)
				continue;

			if (info.Value.Level == 0)
			{
				masterGltfNodes[info.Value.Prefix] = (n, gNode);
			}
			else
			{
				if (!companionLodNodes.TryGetValue(info.Value.Prefix, out var dict))
				{
					dict = new SortedDictionary<int, (int NodeIndex, GltfNode Node)>();
					companionLodNodes[info.Value.Prefix] = dict;
				}

				dict[info.Value.Level] = (n, gNode);
			}
		}
	}

	private static void ProcessMasterNode(
		string prefix,
		(int NodeIndex, GltfNode Node) masterData,
		List<MeshInstance3D> meshInstances, System.Collections.Generic.Dictionary<string, SortedDictionary<int, (int NodeIndex, GltfNode Node)>> companionLodNodes,
		Array<GltfMesh> gltfMeshes,
		Node root)
	{
		string masterNodeName = !string.IsNullOrEmpty(masterData.Node.OriginalName) ? masterData.Node.OriginalName : masterData.Node.ResourceName;
		MeshInstance3D masterInstance = FindMeshInstanceByNameOrIndex(meshInstances, masterNodeName, masterData.NodeIndex);

		if (masterInstance == null)
		{
			masterInstance = FindByLod0(meshInstances) ?? (meshInstances.Count > 0 ? meshInstances[0] : null);
		}

		if (masterInstance == null)
			return;

		Node parentNode = masterInstance.GetParent() ?? root;

		if (companionLodNodes.TryGetValue(prefix, out var lodDict))
		{
			foreach (var lodKvp in lodDict)
			{
				ProcessCompanionLodNode(prefix, lodKvp.Key, lodKvp.Value, masterInstance, meshInstances, gltfMeshes, parentNode, root);
			}
		}
	}

	private static void ProcessCompanionLodNode(
		string prefix,
		int lodLevel,
		(int NodeIndex, GltfNode Node) lodData,
		MeshInstance3D masterInstance,
		List<MeshInstance3D> meshInstances,
		Array<GltfMesh> gltfMeshes,
		Node parentNode,
		Node root)
	{
		string lodNodeName = !string.IsNullOrEmpty(lodData.Node.OriginalName) ? lodData.Node.OriginalName : lodData.Node.ResourceName;
		if (string.IsNullOrEmpty(lodNodeName))
		{
			lodNodeName = $"{prefix}_LOD{lodLevel}";
		}

		MeshInstance3D existingLodInstance = FindMeshInstanceByNameOrIndex(meshInstances, lodNodeName, lodData.NodeIndex);
		if (existingLodInstance != null && existingLodInstance != masterInstance)
			return;

		int meshIndex = lodData.Node.Mesh;
		if (meshIndex < 0 || meshIndex >= gltfMeshes.Count)
			return;

		var gltfMesh = gltfMeshes[meshIndex];
		Mesh importedMesh = gltfMesh.Mesh?.GetMesh();
		if (importedMesh == null)
			return;

		CreateLodInstance(lodNodeName, importedMesh, masterInstance, meshInstances, parentNode, root);
	}

	private static void CreateLodInstance(
		string lodNodeName,
		Mesh importedMesh,
		MeshInstance3D masterInstance,
		List<MeshInstance3D> meshInstances,
		Node parentNode,
		Node root)
	{
		var newLodInstance = new MeshInstance3D
		{
			Name = lodNodeName,
			Mesh = importedMesh,
			Transform = masterInstance.Transform
		};

		int surfaceCount = importedMesh.GetSurfaceCount();
		for (int s = 0; s < surfaceCount; s++)
		{
			var mat = masterInstance.GetSurfaceOverrideMaterial(s) ?? masterInstance.Mesh?.SurfaceGetMaterial(s);
			if (mat != null)
			{
				newLodInstance.SetSurfaceOverrideMaterial(s, mat);
			}
		}

		if (masterInstance.MaterialOverride != null)
		{
			newLodInstance.MaterialOverride = masterInstance.MaterialOverride;
		}

		if (masterInstance.Skin != null)
		{
			newLodInstance.Skin = masterInstance.Skin;
			newLodInstance.Skeleton = masterInstance.Skeleton;
		}

		parentNode.AddChild(newLodInstance);
		newLodInstance.Owner = root;
		meshInstances.Add(newLodInstance);
	}

	private static void CollectMeshInstances(Node current, List<MeshInstance3D> list)
	{
		if (current is MeshInstance3D mi && mi.Mesh != null)
		{
			list.Add(mi);
		}

		int childCount = current.GetChildCount();
		for (int i = 0; i < childCount; i++)
		{
			CollectMeshInstances(current.GetChild(i), list);
		}
	}

	private static MeshInstance3D FindMeshInstanceByNameOrIndex(List<MeshInstance3D> instances, string name, int index)
	{
		if (instances == null || instances.Count == 0)
			return null;

		if (!string.IsNullOrEmpty(name))
		{
			var byName = FindByName(instances, name);
			if (byName != null) return byName;
		}

		return null;
	}

	private static MeshInstance3D FindByName(List<MeshInstance3D> instances, string name)
	{
		foreach (var mi in instances)
		{
			string miName = mi.Name.ToString();
			if (miName == name || miName.StartsWith(name, StringComparison.OrdinalIgnoreCase) || name.StartsWith(miName, StringComparison.OrdinalIgnoreCase))
			{
				return mi;
			}
		}

		return null;
	}

	private static MeshInstance3D FindByLod0(List<MeshInstance3D> instances)
	{
		foreach (var mi in instances)
		{
			string miName = mi.Name.ToString();
			if (miName.EndsWith("LOD0", StringComparison.OrdinalIgnoreCase) || miName.Contains("LOD0", StringComparison.OrdinalIgnoreCase))
			{
				return mi;
			}
		}

		return null;
	}

	private static (string Prefix, int Level)? TryParseLodInfo(string name)
	{
		if (string.IsNullOrEmpty(name)) return null;

		if (name.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase))
			return (name.Substring(0, name.Length - 5), 0);
		if (name.EndsWith("LOD0", StringComparison.OrdinalIgnoreCase))
			return (name.Substring(0, name.Length - 4), 0);

		int lodIdx = name.LastIndexOf("_LOD", StringComparison.OrdinalIgnoreCase);
		int tagLen = 4;
		if (lodIdx < 0)
		{
			lodIdx = name.LastIndexOf("LOD", StringComparison.OrdinalIgnoreCase);
			tagLen = 3;
		}

		if (lodIdx >= 0 && lodIdx + tagLen < name.Length && int.TryParse(name.Substring(lodIdx + tagLen), out int level))
		{
			return (name.Substring(0, lodIdx), level);
		}

		return null;
	}

	private const string GltfExtrasMetadataKey = "extras";
	private const string VisibilityRangeBeginExtraKey = "visibility_range_begin";
	private const string VisibilityRangeEndExtraKey = "visibility_range_end";

	private static void ConfigureExistingLodNodes(List<MeshInstance3D> meshInstances)
	{
		ApplyLodSettingsToAll(meshInstances, 1.0f);
	}

	private static void ApplyLodSettingsToAll(List<MeshInstance3D> meshInstances, float scaleMultiplier)
	{
		var maxLodLevelPerPrefix = ComputeMaxLodLevelPerPrefix(meshInstances);

		foreach (var meshInstance in meshInstances)
		{
			var lodInfo = TryParseLodInfo(meshInstance.Name.ToString());
			if (!lodInfo.HasValue)
			{
				continue;
			}

			int lodLevel = lodInfo.Value.Level;
			int maxLodLevel = maxLodLevelPerPrefix.TryGetValue(lodInfo.Value.Prefix, out int prefixMaxLodLevel) ? prefixMaxLodLevel : lodLevel;
			ApplyLodSettings(meshInstance, lodLevel, lodLevel >= maxLodLevel, scaleMultiplier);
		}
	}

	private static System.Collections.Generic.Dictionary<string, int> ComputeMaxLodLevelPerPrefix(List<MeshInstance3D> meshInstances)
	{
		var maxLodLevelPerPrefix = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (var meshInstance in meshInstances)
		{
			var lodInfo = TryParseLodInfo(meshInstance.Name.ToString());
			if (!lodInfo.HasValue)
			{
				continue;
			}

			if (!maxLodLevelPerPrefix.TryGetValue(lodInfo.Value.Prefix, out int currentMaxLevel) || lodInfo.Value.Level > currentMaxLevel)
			{
				maxLodLevelPerPrefix[lodInfo.Value.Prefix] = lodInfo.Value.Level;
			}
		}

		return maxLodLevelPerPrefix;
	}

	public static void ApplyLodSettings(MeshInstance3D meshInstance, int lodLevel, bool isLastLod = false, float scaleMultiplier = 1.0f)
	{
		if (meshInstance == null)
		{
			return;
		}

		float scale = Math.Max(0.01f, scaleMultiplier);
		float margin = 2.0f * scale;

		meshInstance.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
		meshInstance.VisibilityRangeBeginMargin = margin;
		meshInstance.VisibilityRangeEndMargin = margin;
		meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;

		var (rangeBeginUnits, rangeEndUnits) = GetLodRangeUnits(meshInstance, lodLevel);
		meshInstance.VisibilityRangeBegin = rangeBeginUnits * scale;
		meshInstance.VisibilityRangeEnd = isLastLod ? 0f : rangeEndUnits * scale;
	}

	private static (float RangeBeginUnits, float RangeEndUnits) GetLodRangeUnits(Node lodNode, int lodLevel)
	{
		if (TryGetAuthoredLodRangeUnits(lodNode, out float authoredRangeBegin, out float authoredRangeEnd))
		{
			return (authoredRangeBegin, authoredRangeEnd);
		}

		int clampedLodLevel = Math.Clamp(lodLevel, 0, GlbLodGenerator.DefaultVisibilityBegins.Length - 1);
		return (GlbLodGenerator.DefaultVisibilityBegins[clampedLodLevel], GlbLodGenerator.DefaultVisibilityEnds[clampedLodLevel]);
	}

	private static bool TryGetAuthoredLodRangeUnits(Node lodNode, out float rangeBeginUnits, out float rangeEndUnits)
	{
		rangeBeginUnits = 0f;
		rangeEndUnits = 0f;

		if (!lodNode.HasMeta(GltfExtrasMetadataKey))
		{
			return false;
		}

		var extras = lodNode.GetMeta(GltfExtrasMetadataKey).AsGodotDictionary();
		if (extras == null || !extras.TryGetValue(VisibilityRangeBeginExtraKey, out Variant rangeBeginValue) || !extras.TryGetValue(VisibilityRangeEndExtraKey, out Variant rangeEndValue))
		{
			return false;
		}

		rangeBeginUnits = rangeBeginValue.AsSingle();
		rangeEndUnits = rangeEndValue.AsSingle();
		return true;
	}

	public static void UpdateLodVisibilityRanges(Node rootNode, float scaleMultiplier)
	{
		if (rootNode == null || scaleMultiplier <= 0f)
		{
			return;
		}

		var meshInstances = new List<MeshInstance3D>();
		CollectMeshInstances(rootNode, meshInstances);
		ApplyLodSettingsToAll(meshInstances, scaleMultiplier);
	}
}