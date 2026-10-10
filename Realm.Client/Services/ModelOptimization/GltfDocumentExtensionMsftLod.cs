using Godot;
using System;
using System.Collections.Generic;

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

	public override Error _ParseNodeExtensions(GltfState state, GltfNode gltfNode, global::Godot.Collections.Dictionary extensions)
	{
		if (extensions != null && extensions.ContainsKey("MSFT_lod"))
		{
			gltfNode.SetAdditionalData("MSFT_lod", extensions["MSFT_lod"]);
		}
		return Error.Ok;
	}

	public override Error _ImportPost(GltfState state, Node root)
	{
		ProcessImportedScene(state, root);
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

			var masterGltfNodes = new Dictionary<string, (int NodeIndex, GltfNode Node)>();
			var companionLodNodes = new Dictionary<string, SortedDictionary<int, (int NodeIndex, GltfNode Node)>>();
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
		global::Godot.Collections.Array<GltfNode> gltfNodes,
		Dictionary<string, (int NodeIndex, GltfNode Node)> masterGltfNodes,
		Dictionary<string, SortedDictionary<int, (int NodeIndex, GltfNode Node)>> companionLodNodes)
	{
		for (int n = 0; n < gltfNodes.Count; n++)
		{
			var gNode = gltfNodes[n];
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
		List<MeshInstance3D> meshInstances,
		Dictionary<string, SortedDictionary<int, (int NodeIndex, GltfNode Node)>> companionLodNodes,
		global::Godot.Collections.Array<GltfMesh> gltfMeshes,
		Node root)
	{
		string masterNodeName = !string.IsNullOrEmpty(masterData.Node.OriginalName) ? masterData.Node.OriginalName : masterData.Node.ResourceName;
		MeshInstance3D masterInstance = FindMeshInstanceByNameOrIndex(meshInstances, masterNodeName, masterData.NodeIndex);

		if (masterInstance == null && meshInstances.Count == 1)
		{
			masterInstance = meshInstances[0];
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
		global::Godot.Collections.Array<GltfMesh> gltfMeshes,
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

		if (index >= 0 && index < instances.Count)
			return instances[index];

		if (instances.Count == 1)
			return instances[0];

		var byLod0 = FindByLod0(instances);
		if (byLod0 != null) return byLod0;

		return instances[0];
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

	private static void ConfigureExistingLodNodes(List<MeshInstance3D> meshInstances)
	{
		var maxLodPerPrefix = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (var mi in meshInstances)
		{
			var info = TryParseLodInfo(mi.Name.ToString());
			if (info.HasValue)
			{
				string p = info.Value.Prefix;
				int l = info.Value.Level;
				if (!maxLodPerPrefix.TryGetValue(p, out int currMax) || l > currMax)
				{
					maxLodPerPrefix[p] = l;
				}
			}
		}

		foreach (var mi in meshInstances)
		{
			var info = TryParseLodInfo(mi.Name.ToString());
			if (info.HasValue)
			{
				int level = info.Value.Level;
				int maxLevel = maxLodPerPrefix.TryGetValue(info.Value.Prefix, out int ml) ? ml : 0;
				bool isLast = (level >= maxLevel);
				ApplyLodSettings(mi, level, isLast, 1.0f);
			}
		}
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

		if (isLastLod || lodLevel >= 3)
		{
			meshInstance.VisibilityRangeBegin = lodLevel switch
			{
				0 => 0f,
				1 => 45f * scale,
				2 => 90f * scale,
				_ => 150f * scale
			};
			meshInstance.VisibilityRangeEnd = 0f;
			return;
		}

		switch (lodLevel)
		{
			case 0:
				meshInstance.VisibilityRangeBegin = 0f;
				meshInstance.VisibilityRangeEnd = 45f * scale;
				break;
			case 1:
				meshInstance.VisibilityRangeBegin = 45f * scale;
				meshInstance.VisibilityRangeEnd = 90f * scale;
				break;
			case 2:
				meshInstance.VisibilityRangeBegin = 90f * scale;
				meshInstance.VisibilityRangeEnd = 150f * scale;
				break;
			default:
				meshInstance.VisibilityRangeBegin = 150f * scale;
				meshInstance.VisibilityRangeEnd = 0f;
				break;
		}
	}

	public static void UpdateLodVisibilityRanges(Node rootNode, float scaleMultiplier)
	{
		if (rootNode == null || scaleMultiplier <= 0f)
		{
			return;
		}

		var meshInstances = new List<MeshInstance3D>();
		CollectMeshInstances(rootNode, meshInstances);

		var maxLodPerPrefix = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (var mi in meshInstances)
		{
			var info = TryParseLodInfo(mi.Name.ToString());
			if (info.HasValue)
			{
				string p = info.Value.Prefix;
				int l = info.Value.Level;
				if (!maxLodPerPrefix.TryGetValue(p, out int currMax) || l > currMax)
				{
					maxLodPerPrefix[p] = l;
				}
			}
		}

		foreach (var mi in meshInstances)
		{
			var info = TryParseLodInfo(mi.Name.ToString());
			if (info.HasValue)
			{
				int level = info.Value.Level;
				int maxLevel = maxLodPerPrefix.TryGetValue(info.Value.Prefix, out int ml) ? ml : 0;
				bool isLast = (level >= maxLevel);
				ApplyLodSettings(mi, level, isLast, scaleMultiplier);
			}
		}
	}
}
