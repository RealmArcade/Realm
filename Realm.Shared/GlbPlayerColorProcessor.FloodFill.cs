using System.Numerics;
using System.Text.Json.Nodes;
using Assimp;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>the flood fill over the mask.</summary>
public static partial class GlbPlayerColorProcessor
	{
	private static bool[] RunFloodFill(
		int faceCount, HashSet<int> validatedSeedFaces,
		bool[] isFloodCandidateFace, Dictionary<int, HashSet<int>> smoothAdjacency)
	{
		var visited = new bool[faceCount];
		var floodQueue = new Queue<int>();

		foreach (int f in validatedSeedFaces)
		{
			floodQueue.Enqueue(f);
			visited[f] = true;
		}

		while (floodQueue.Count > 0)
		{
			int current = floodQueue.Dequeue();

			foreach (int neighbor in smoothAdjacency[current])
			{
				if (visited[neighbor]) continue;

				if (isFloodCandidateFace[neighbor])
				{
					visited[neighbor] = true;
					floodQueue.Enqueue(neighbor);
				}
			}
		}

		return visited;
	}

	private static void TraverseFloodComponent(int i, bool[] visited, bool[] componentVisited, Dictionary<int, HashSet<int>> smoothAdjacency, HashSet<int> confirmedFaces, int minClusterFaces)
	{
		var component = new List<int>();
		var queue = new Queue<int>();
		queue.Enqueue(i);
		componentVisited[i] = true;

		while (queue.Count > 0)
		{
			int curr = queue.Dequeue();
			component.Add(curr);

			foreach (int neighbor in smoothAdjacency[curr])
			{
				if (!visited[neighbor] || componentVisited[neighbor]) continue;

				componentVisited[neighbor] = true;
				queue.Enqueue(neighbor);
			}
		}

		if (component.Count >= minClusterFaces)
		{
			foreach (int f in component)
			{
				confirmedFaces.Add(f);
			}
		}
	}

	private static HashSet<int> ExtractFloodComponents(
		int faceCount, bool[] visited, Dictionary<int, HashSet<int>> smoothAdjacency, int minClusterFaces)
	{
		var confirmedFaces = new HashSet<int>();
		var componentVisited = new bool[faceCount];

		for (int i = 0; i < faceCount; i++)
		{
			if (!visited[i] || componentVisited[i]) continue;

			TraverseFloodComponent(i, visited, componentVisited, smoothAdjacency, confirmedFaces, minClusterFaces);
		}

		return confirmedFaces;
	}
}
