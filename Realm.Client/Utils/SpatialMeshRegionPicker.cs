using System;
using System.Collections.Generic;
using Godot;

namespace Realm.Client.Utils;

public readonly struct SpatialMeshRegionPick
{
	public SpatialMeshRegionPick(Vector3 min, Vector3 max, string meshName, int triangleCount)
	{
		Min = min;
		Max = max;
		MeshName = meshName;
		TriangleCount = triangleCount;
	}

	public Vector3 Min { get; }
	public Vector3 Max { get; }
	public string MeshName { get; }
	public int TriangleCount { get; }
}

public static class SpatialMeshRegionPicker
{
	private const float MinimumRayLength = 0.0001f;
	private const int MaximumFloodFillTriangles = 250000;

	public static bool TryPickRegion(
		Camera3D camera,
		Node3D modelRoot,
		Vector2 viewportPoint,
		float maximumCreaseAngleDegrees,
		out SpatialMeshRegionPick pick)
	{
		pick = default;
		if (camera == null || !GodotObject.IsInstanceValid(camera)) return false;
		if (modelRoot == null || !GodotObject.IsInstanceValid(modelRoot)) return false;

		Vector3 rayOrigin = camera.ProjectRayOrigin(viewportPoint);
		Vector3 rayDirection = camera.ProjectRayNormal(viewportPoint);
		if (rayDirection.LengthSquared() < MinimumRayLength) return false;

		var meshes = new List<MeshInstance3D>();
		CollectMeshInstances(modelRoot, meshes);

		float closestDistance = float.MaxValue;
		MeshInstance3D? closestMesh = null;
		int closestSurfaceIndex = -1;
		int closestTriangleIndex = -1;

		for (int meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
		{
			MeshInstance3D meshInstance = meshes[meshIndex];
			Mesh? mesh = meshInstance.Mesh;
			if (mesh == null) continue;

			Transform3D inverseTransform = meshInstance.GlobalTransform.AffineInverse();
			Vector3 localRayOrigin = inverseTransform * rayOrigin;
			Vector3 localRayDirection = inverseTransform.Basis * rayDirection;

			int surfaceCount = mesh.GetSurfaceCount();
			for (int surfaceIndex = 0; surfaceIndex < surfaceCount; surfaceIndex++)
			{
				if (!TryReadSurfaceGeometry(mesh, surfaceIndex, out Vector3[] vertices, out int[] indices)) continue;

				int triangleCount = indices.Length / 3;
				for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
				{
					Vector3 firstVertex = vertices[indices[triangleIndex * 3]];
					Vector3 secondVertex = vertices[indices[triangleIndex * 3 + 1]];
					Vector3 thirdVertex = vertices[indices[triangleIndex * 3 + 2]];

					if (!TryIntersectTriangle(localRayOrigin, localRayDirection, firstVertex, secondVertex, thirdVertex, out float localDistance)) continue;

					Vector3 localHitPoint = localRayOrigin + localRayDirection * localDistance;
					Vector3 worldHitPoint = meshInstance.GlobalTransform * localHitPoint;
					float worldDistance = worldHitPoint.DistanceTo(rayOrigin);
					if (worldDistance >= closestDistance) continue;

					closestDistance = worldDistance;
					closestMesh = meshInstance;
					closestSurfaceIndex = surfaceIndex;
					closestTriangleIndex = triangleIndex;
				}
			}
		}

		if (closestMesh == null || closestSurfaceIndex < 0 || closestTriangleIndex < 0) return false;

		Mesh? hitMesh = closestMesh.Mesh;
		if (hitMesh == null) return false;
		if (!TryReadSurfaceGeometry(hitMesh, closestSurfaceIndex, out Vector3[] hitVertices, out int[] hitIndices)) return false;

		HashSet<int> regionTriangles = FloodFillConnectedTriangles(hitVertices, hitIndices, closestTriangleIndex, maximumCreaseAngleDegrees);
		if (regionTriangles.Count == 0)
		{
			regionTriangles.Add(closestTriangleIndex);
		}

		ComputeRegionBounds(hitVertices, hitIndices, regionTriangles, out Vector3 min, out Vector3 max);
		pick = new SpatialMeshRegionPick(min, max, closestMesh.Name, regionTriangles.Count);
		return true;
	}

	private static void CollectMeshInstances(Node node, List<MeshInstance3D> meshes)
	{
		if (node is MeshInstance3D meshInstance && meshInstance.Mesh != null && meshInstance.Visible)
		{
			meshes.Add(meshInstance);
		}

		int childCount = node.GetChildCount();
		for (int childIndex = 0; childIndex < childCount; childIndex++)
		{
			CollectMeshInstances(node.GetChild(childIndex), meshes);
		}
	}

	private static bool TryReadSurfaceGeometry(Mesh mesh, int surfaceIndex, out Vector3[] vertices, out int[] indices)
	{
		vertices = Array.Empty<Vector3>();
		indices = Array.Empty<int>();

		Godot.Collections.Array arrays = mesh.SurfaceGetArrays(surfaceIndex);
		int vertexArrayIndex = (int)Mesh.ArrayType.Vertex;
		if (arrays.Count <= vertexArrayIndex) return false;

		Variant vertexVariant = arrays[vertexArrayIndex];
		if (vertexVariant.VariantType != Variant.Type.PackedVector3Array) return false;

		vertices = vertexVariant.AsVector3Array();
		if (vertices.Length < 3) return false;

		int indexArrayIndex = (int)Mesh.ArrayType.Index;
		if (arrays.Count > indexArrayIndex)
		{
			Variant indexVariant = arrays[indexArrayIndex];
			if (indexVariant.VariantType == Variant.Type.PackedInt32Array)
			{
				indices = indexVariant.AsInt32Array();
			}
		}

		if (indices.Length < 3)
		{
			indices = new int[vertices.Length];
			for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
			{
				indices[vertexIndex] = vertexIndex;
			}
		}

		return indices.Length >= 3;
	}

	private static bool TryIntersectTriangle(Vector3 rayOrigin, Vector3 rayDirection, Vector3 firstVertex, Vector3 secondVertex, Vector3 thirdVertex, out float distance)
	{
		distance = 0.0f;

		Vector3 edgeOne = secondVertex - firstVertex;
		Vector3 edgeTwo = thirdVertex - firstVertex;
		Vector3 perpendicular = rayDirection.Cross(edgeTwo);
		float determinant = edgeOne.Dot(perpendicular);
		if (MathF.Abs(determinant) < 0.0000001f) return false;

		float inverseDeterminant = 1.0f / determinant;
		Vector3 originOffset = rayOrigin - firstVertex;
		float firstBarycentric = originOffset.Dot(perpendicular) * inverseDeterminant;
		if (firstBarycentric < 0.0f || firstBarycentric > 1.0f) return false;

		Vector3 crossProduct = originOffset.Cross(edgeOne);
		float secondBarycentric = rayDirection.Dot(crossProduct) * inverseDeterminant;
		if (secondBarycentric < 0.0f || firstBarycentric + secondBarycentric > 1.0f) return false;

		float hitDistance = edgeTwo.Dot(crossProduct) * inverseDeterminant;
		if (hitDistance <= 0.00001f) return false;

		distance = hitDistance;
		return true;
	}

	private static HashSet<int> FloodFillConnectedTriangles(Vector3[] vertices, int[] indices, int seedTriangleIndex, float maximumCreaseAngleDegrees)
	{
		var regionTriangles = new HashSet<int> { seedTriangleIndex };
		int triangleCount = indices.Length / 3;
		if (triangleCount <= 1) return regionTriangles;

		Vector3[] faceNormals = BuildFaceNormals(vertices, indices, triangleCount);
		Dictionary<long, List<int>> trianglesByEdge = BuildEdgeLookup(indices, triangleCount);

		float maximumCreaseCosine = MathF.Cos(maximumCreaseAngleDegrees * (MathF.PI / 180.0f));
		var pendingTriangles = new Queue<int>();
		pendingTriangles.Enqueue(seedTriangleIndex);

		Vector3 seedNormal = faceNormals[seedTriangleIndex];

		while (pendingTriangles.Count > 0 && regionTriangles.Count < MaximumFloodFillTriangles)
		{
			int currentTriangle = pendingTriangles.Dequeue();
			Vector3 currentNormal = faceNormals[currentTriangle];

			for (int edgeIndex = 0; edgeIndex < 3; edgeIndex++)
			{
				int firstVertexIndex = indices[currentTriangle * 3 + edgeIndex];
				int secondVertexIndex = indices[currentTriangle * 3 + (edgeIndex + 1) % 3];
				long edgeKey = EncodeEdge(firstVertexIndex, secondVertexIndex);
				if (!trianglesByEdge.TryGetValue(edgeKey, out List<int>? adjacentTriangles)) continue;

				for (int adjacentIndex = 0; adjacentIndex < adjacentTriangles.Count; adjacentIndex++)
				{
					int adjacentTriangle = adjacentTriangles[adjacentIndex];
					if (adjacentTriangle == currentTriangle) continue;
					if (regionTriangles.Contains(adjacentTriangle)) continue;

					Vector3 adjacentNormal = faceNormals[adjacentTriangle];
					if (currentNormal.Dot(adjacentNormal) < maximumCreaseCosine) continue;
					if (seedNormal.Dot(adjacentNormal) < maximumCreaseCosine) continue;

					regionTriangles.Add(adjacentTriangle);
					pendingTriangles.Enqueue(adjacentTriangle);
				}
			}
		}

		return regionTriangles;
	}

	private static Vector3[] BuildFaceNormals(Vector3[] vertices, int[] indices, int triangleCount)
	{
		var faceNormals = new Vector3[triangleCount];
		for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
		{
			Vector3 firstVertex = vertices[indices[triangleIndex * 3]];
			Vector3 secondVertex = vertices[indices[triangleIndex * 3 + 1]];
			Vector3 thirdVertex = vertices[indices[triangleIndex * 3 + 2]];
			faceNormals[triangleIndex] = (secondVertex - firstVertex).Cross(thirdVertex - firstVertex).Normalized();
		}

		return faceNormals;
	}

	private static Dictionary<long, List<int>> BuildEdgeLookup(int[] indices, int triangleCount)
	{
		var trianglesByEdge = new Dictionary<long, List<int>>(triangleCount * 3);
		for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
		{
			for (int edgeIndex = 0; edgeIndex < 3; edgeIndex++)
			{
				int firstVertexIndex = indices[triangleIndex * 3 + edgeIndex];
				int secondVertexIndex = indices[triangleIndex * 3 + (edgeIndex + 1) % 3];
				long edgeKey = EncodeEdge(firstVertexIndex, secondVertexIndex);
				if (!trianglesByEdge.TryGetValue(edgeKey, out List<int>? adjacentTriangles))
				{
					adjacentTriangles = new List<int>(2);
					trianglesByEdge[edgeKey] = adjacentTriangles;
				}

				adjacentTriangles.Add(triangleIndex);
			}
		}

		return trianglesByEdge;
	}

	private static long EncodeEdge(int firstVertexIndex, int secondVertexIndex)
	{
		int minimumIndex = Math.Min(firstVertexIndex, secondVertexIndex);
		int maximumIndex = Math.Max(firstVertexIndex, secondVertexIndex);
		return ((long)minimumIndex << 32) | (uint)maximumIndex;
	}

	private static void ComputeRegionBounds(Vector3[] vertices, int[] indices, HashSet<int> regionTriangles, out Vector3 min, out Vector3 max)
	{
		min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
		max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

		foreach (int triangleIndex in regionTriangles)
		{
			for (int cornerIndex = 0; cornerIndex < 3; cornerIndex++)
			{
				Vector3 vertex = vertices[indices[triangleIndex * 3 + cornerIndex]];
				min = new Vector3(MathF.Min(min.X, vertex.X), MathF.Min(min.Y, vertex.Y), MathF.Min(min.Z, vertex.Z));
				max = new Vector3(MathF.Max(max.X, vertex.X), MathF.Max(max.Y, vertex.Y), MathF.Max(max.Z, vertex.Z));
			}
		}

		Vector3 size = max - min;
		float padding = MathF.Max(0.01f, MathF.Max(size.X, MathF.Max(size.Y, size.Z)) * 0.02f);
		min -= new Vector3(padding, padding, padding);
		max += new Vector3(padding, padding, padding);
	}
}
