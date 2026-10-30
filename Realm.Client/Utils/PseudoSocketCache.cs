using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;
using Realm.Shared.Metadata;

namespace Realm.Client.Utils;

public enum PseudoSocketSource
{
	Generated,
	AuthorDefined,
	RealSocket
}

public readonly struct PseudoSocketPoint
{
	public string SocketId { get; init; }
	public string DisplayName { get; init; }
	public Vector3 LocalPosition { get; init; }
	public Vector3 LocalRotationDegrees { get; init; }
	public PseudoSocketSource Source { get; init; }

	public PseudoSocketPoint(string socketId, string displayName, Vector3 localPosition, Vector3 localRotationDegrees, PseudoSocketSource source)
	{
		SocketId = socketId;
		DisplayName = displayName;
		LocalPosition = localPosition;
		LocalRotationDegrees = localRotationDegrees;
		Source = source;
	}
}

/// <summary>
///     The socket points a model exposes, derived from its bounds and from the anchors its author placed, cached per
///     model path. Every entry is a pure function of the model and the anchors it was asked about, so an entry is only
///     wrong when the model changes, and <see cref="Invalidate" /> is what says one did.
///     <para>
///         The cache is owned by the client's main thread: every caller is a unit view resolving an attachment or an
///         editor dialog previewing one, and both are main-thread code. The lock is therefore not contention protection
///         but a guard around the dictionaries, so that an import thread added later cannot corrupt them. A method that
///         needs the model's bounds computes them before taking it, so no Godot call is made while it is held, and no
///         other lock is taken beneath it.
///     </para>
/// </summary>
public static class PseudoSocketCache
{
	public const float OverheadMargin = 0.3f;

	public static readonly string[] HumanoidSocketIds =
	{
		"RightHand",
		"LeftHand",
		"Chest",
		"Hips",
		"Head",
		"LeftFoot",
		"RightFoot"
	};

	private static readonly object _cacheLock = new();
	private static readonly Dictionary<string, Aabb> _modelBoundsCache = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, IReadOnlyList<PseudoSocketPoint>> _generatedSocketCache = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, IReadOnlyList<PseudoSocketPoint>> _mergedSocketCache = new(StringComparer.OrdinalIgnoreCase);
	private static readonly HashSet<string> _customSocketIds = new(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, string> _socketAliases = new(StringComparer.OrdinalIgnoreCase)
	{
		{ "ground", "Base" },
		{ "footprint", "Base" },
		{ "base", "Base" },
		{ "center", "Center" },
		{ "centerofmass", "Center" },
		{ "middle", "Center" },
		{ "top", "Top" },
		{ "overhead", "Top" },
		{ "roof", "Top" },
		{ "crown", "Top" },
		{ "pivot", "Pivot" },
		{ "origin", "Pivot" },
		{ "front", "Front" },
		{ "back", "Back" },
		{ "left", "Left" },
		{ "right", "Right" },
		{ "topfront", "TopFront" },
		{ "topback", "TopBack" },
		{ "topleft", "TopLeft" },
		{ "topright", "TopRight" }
	};

	public static IReadOnlyList<PseudoSocketPoint> GetGeneratedSockets(Node3D modelRoot, string modelPath)
	{
		string cacheKey = GetModelCacheKey(modelRoot, modelPath);

		lock (_cacheLock)
		{
			if (_generatedSocketCache.TryGetValue(cacheKey, out var cached))
			{
				return cached;
			}
		}

		Aabb bounds = ResolveModelBounds(modelRoot, cacheKey);
		var points = BuildGeneratedSockets(bounds);

		lock (_cacheLock)
		{
			_generatedSocketCache[cacheKey] = points;
		}

		return points;
	}

	public static IReadOnlyList<PseudoSocketPoint> GetSockets(Node3D modelRoot, string modelPath, IReadOnlyList<PseudoSocketAnchor>? authorAnchors = null, IReadOnlyList<string>? realSocketIds = null)
	{
		var generated = GetGeneratedSockets(modelRoot, modelPath);

		if (authorAnchors != null)
		{
			RegisterCustomSocketIds(authorAnchors);
		}

		string mergedKey = GetModelCacheKey(modelRoot, modelPath) + "|" + BuildAnchorSignature(authorAnchors) + "|" + BuildIdSignature(realSocketIds);

		lock (_cacheLock)
		{
			if (_mergedSocketCache.TryGetValue(mergedKey, out var cached))
			{
				return cached;
			}
		}

		var merged = MergeSockets(generated, authorAnchors, realSocketIds);

		lock (_cacheLock)
		{
			_mergedSocketCache[mergedKey] = merged;
		}

		return merged;
	}

	public static bool TryGetSocket(Node3D modelRoot, string modelPath, IReadOnlyList<PseudoSocketAnchor>? authorAnchors, IReadOnlyList<string>? realSocketIds, string socketId, out PseudoSocketPoint point)
	{
		point = default;
		if (string.IsNullOrWhiteSpace(socketId)) return false;

		var sockets = GetSockets(modelRoot, modelPath, authorAnchors, realSocketIds);
		string canonicalId = NormalizeSocketId(socketId) ?? socketId;

		for (int index = 0; index < sockets.Count; index++)
		{
			if (sockets[index].SocketId.Equals(canonicalId, StringComparison.OrdinalIgnoreCase))
			{
				point = sockets[index];
				return true;
			}
		}

		return false;
	}

	public static string? NormalizeSocketId(string? socketId)
	{
		if (string.IsNullOrWhiteSpace(socketId)) return null;

		string normalized = NormalizeKey(socketId);
		if (_socketAliases.TryGetValue(normalized, out var canonicalId))
		{
			return canonicalId;
		}

		lock (_cacheLock)
		{
			foreach (var customId in _customSocketIds)
			{
				if (NormalizeKey(customId) == normalized) return customId;
			}
		}

		return null;
	}

	public static bool IsRealSocket(string socketId, IReadOnlyList<string>? realSocketIds)
	{
		if (realSocketIds == null || realSocketIds.Count == 0 || string.IsNullOrWhiteSpace(socketId)) return false;

		string normalized = NormalizeKey(socketId);
		for (int index = 0; index < realSocketIds.Count; index++)
		{
			if (NormalizeKey(realSocketIds[index]) == normalized) return true;
		}

		return false;
	}

	public static void RegisterCustomSocketIds(IReadOnlyList<PseudoSocketAnchor>? authorAnchors)
	{
		if (authorAnchors == null || authorAnchors.Count == 0) return;

		lock (_cacheLock)
		{
			for (int index = 0; index < authorAnchors.Count; index++)
			{
				string socketId = authorAnchors[index]?.SocketId ?? string.Empty;
				if (!string.IsNullOrWhiteSpace(socketId))
				{
					_customSocketIds.Add(socketId);
				}
			}
		}
	}

	public static void Invalidate(string modelPath)
	{
		if (string.IsNullOrEmpty(modelPath)) return;

		string cacheKey = GetModelCacheKey(null, modelPath);

		lock (_cacheLock)
		{
			_modelBoundsCache.Remove(cacheKey);
			_generatedSocketCache.Remove(cacheKey);
			RemoveMergedEntries(cacheKey);
		}
	}

	public static void Clear()
	{
		lock (_cacheLock)
		{
			_modelBoundsCache.Clear();
			_generatedSocketCache.Clear();
			_mergedSocketCache.Clear();
			_customSocketIds.Clear();
		}
	}

	private static void RemoveMergedEntries(string cacheKey)
	{
		var keysToRemove = new List<string>();
		foreach (var key in _mergedSocketCache.Keys)
		{
			if (key.StartsWith(cacheKey + "|", StringComparison.OrdinalIgnoreCase))
			{
				keysToRemove.Add(key);
			}
		}

		foreach (var key in keysToRemove)
		{
			_mergedSocketCache.Remove(key);
		}
	}

	private static Aabb ResolveModelBounds(Node3D modelRoot, string cacheKey)
	{
		lock (_cacheLock)
		{
			if (_modelBoundsCache.TryGetValue(cacheKey, out var cachedBounds))
			{
				return cachedBounds;
			}
		}

		Aabb bounds = modelRoot.CalculateLocalBounds();

		lock (_cacheLock)
		{
			_modelBoundsCache[cacheKey] = bounds;
		}

		return bounds;
	}

	private static IReadOnlyList<PseudoSocketPoint> BuildGeneratedSockets(Aabb bounds)
	{
		Vector3 center = bounds.GetCenter();
		Vector3 bottomCenter = new Vector3(center.X, bounds.Position.Y, center.Z);
		Vector3 topCenter = new Vector3(center.X, bounds.End.Y, center.Z);
		Vector3 frontCenter = new Vector3(center.X, center.Y, bounds.End.Z);
		Vector3 backCenter = new Vector3(center.X, center.Y, bounds.Position.Z);
		Vector3 leftCenter = new Vector3(bounds.Position.X, center.Y, center.Z);
		Vector3 rightCenter = new Vector3(bounds.End.X, center.Y, center.Z);

		return new List<PseudoSocketPoint>(12)
		{
			new("Pivot", "Model Root / Pivot (0, 0, 0)", Vector3.Zero, Vector3.Zero, PseudoSocketSource.Generated),
			new("Base", "Model Base / Ground", bottomCenter, Vector3.Zero, PseudoSocketSource.Generated),
			new("Center", "Model Center", center, Vector3.Zero, PseudoSocketSource.Generated),
			new("Top", "Model Top / Roof", topCenter, Vector3.Zero, PseudoSocketSource.Generated),
			new("Front", "Front Edge", frontCenter, Vector3.Zero, PseudoSocketSource.Generated),
			new("Back", "Back Edge", backCenter, Vector3.Zero, PseudoSocketSource.Generated),
			new("Left", "Left Edge", leftCenter, Vector3.Zero, PseudoSocketSource.Generated),
			new("Right", "Right Edge", rightCenter, Vector3.Zero, PseudoSocketSource.Generated),
			new("TopFront", "Top Front Corner", new Vector3(center.X, bounds.End.Y, bounds.End.Z), Vector3.Zero, PseudoSocketSource.Generated),
			new("TopBack", "Top Back Corner", new Vector3(center.X, bounds.End.Y, bounds.Position.Z), Vector3.Zero, PseudoSocketSource.Generated),
			new("TopLeft", "Top Left Corner", new Vector3(bounds.Position.X, bounds.End.Y, center.Z), Vector3.Zero, PseudoSocketSource.Generated),
			new("TopRight", "Top Right Corner", new Vector3(bounds.End.X, bounds.End.Y, center.Z), Vector3.Zero, PseudoSocketSource.Generated)
		};
	}

	private static List<PseudoSocketPoint> MergeSockets(IReadOnlyList<PseudoSocketPoint> generated, IReadOnlyList<PseudoSocketAnchor>? authorAnchors, IReadOnlyList<string>? realSocketIds)
	{
		var merged = new List<PseudoSocketPoint>(generated.Count + (authorAnchors?.Count ?? 0));
		var authorIndex = new Dictionary<string, PseudoSocketPoint>(StringComparer.OrdinalIgnoreCase);

		if (authorAnchors != null)
		{
			for (int index = 0; index < authorAnchors.Count; index++)
			{
				var anchor = authorAnchors[index];
				if (anchor == null || string.IsNullOrWhiteSpace(anchor.SocketId)) continue;

				authorIndex[anchor.SocketId] = new PseudoSocketPoint(
					anchor.SocketId,
					$"Custom: {anchor.SocketId}",
					anchor.Position.ToGodotVector3(),
					anchor.RotationDegrees.ToGodotVector3(),
					PseudoSocketSource.AuthorDefined);
			}
		}

		for (int index = 0; index < generated.Count; index++)
		{
			var generatedPoint = generated[index];
			if (IsRealSocket(generatedPoint.SocketId, realSocketIds)) continue;

			merged.Add(authorIndex.TryGetValue(generatedPoint.SocketId, out var overridePoint) ? overridePoint : generatedPoint);
			authorIndex.Remove(generatedPoint.SocketId);
		}

		foreach (var authorPoint in authorIndex.Values)
		{
			if (IsRealSocket(authorPoint.SocketId, realSocketIds)) continue;
			merged.Add(authorPoint);
		}

		return merged;
	}

	private static string GetModelCacheKey(Node3D modelRoot, string modelPath)
	{
		string resolved = ModelCache.ResolveModelPath(modelPath);
		if (!string.IsNullOrEmpty(resolved)) return resolved;

		if (!string.IsNullOrEmpty(modelPath)) return modelPath;

		return modelRoot?.GetInstanceId().ToString(CultureInfo.InvariantCulture) ?? "unknown";
	}

	private static string BuildAnchorSignature(IReadOnlyList<PseudoSocketAnchor>? authorAnchors)
	{
		if (authorAnchors == null || authorAnchors.Count == 0) return "none";

		var builder = new StringBuilder();
		for (int index = 0; index < authorAnchors.Count; index++)
		{
			var anchor = authorAnchors[index];
			if (anchor == null) continue;

			builder.Append(anchor.SocketId).Append(':')
				.Append(anchor.Position.X).Append(',')
				.Append(anchor.Position.Y).Append(',')
				.Append(anchor.Position.Z).Append(',')
				.Append(anchor.RotationDegrees.X).Append(',')
				.Append(anchor.RotationDegrees.Y).Append(',')
				.Append(anchor.RotationDegrees.Z).Append(';');
		}

		return builder.ToString();
	}

	private static string BuildIdSignature(IReadOnlyList<string>? socketIds)
	{
		if (socketIds == null || socketIds.Count == 0) return "none";

		var builder = new StringBuilder();
		for (int index = 0; index < socketIds.Count; index++)
		{
			builder.Append(socketIds[index]).Append(';');
		}

		return builder.ToString();
	}

	private static string NormalizeKey(string socketId)
	{
		return socketId.Trim().ToLowerInvariant().Replace("_", string.Empty).Replace(" ", string.Empty);
	}
}
