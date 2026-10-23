using System;
using System.Collections.Generic;
using System.Globalization;

namespace Realm.MapAPI;

/// <summary>
/// Maps text to stable integer identifiers that every process computes identically, and keeps a
/// text table so a peer can render identifiers it never registered itself.
/// </summary>
/// <remarks>
/// Identifiers are content hashes rather than allocation sequence numbers, so the host, the client and the
/// map script sandbox agree on the identifier for a given text without sharing any state. Two strings that
/// hash to the same primary identifier are resolved at registration time: the later one receives an
/// identifier from the reserved fallback range so no two strings alias. Colliding texts are reported through
/// <see cref="CollisionCount"/> and <see cref="CollisionSummary"/> because the fallback decision is only
/// reachable once both strings have been registered in the same process.
/// </remarks>
public static class StringRegistry
{
	/// <summary>
	/// First identifier of the reserved fallback range used when a primary content hash is already taken.
	/// </summary>
	public const int FallbackIdBase = 0x40000000;

	private const uint PrimaryHashSeed = 2166136261u;
	private const uint FallbackHashSeed = 0x811C9DC5u ^ 0x5BD1E995u;

	private static readonly Dictionary<int, string> _textById = new Dictionary<int, string>();
	private static readonly Dictionary<string, int> _idByText = new Dictionary<string, int>(StringComparer.Ordinal);
	private static readonly List<(int Id, string Text)> _registeredEntries = new List<(int Id, string Text)>();
	private static readonly object _lock = new object();

	private static int _collisionCount;
	private static string _collisionSummary = string.Empty;

	/// <summary>
	/// Gets the number of text registrations that had to move into the reserved fallback range because another
	/// string already owned their primary content hash.
	/// </summary>
	public static int CollisionCount
	{
		get
		{
			lock (_lock)
			{
				return _collisionCount;
			}
		}
	}

	/// <summary>
	/// Gets a human readable summary of the first few hash collisions, listing both colliding texts.
	/// </summary>
	public static string CollisionSummary
	{
		get
		{
			lock (_lock)
			{
				return _collisionSummary;
			}
		}
	}

	/// <summary>
	/// Gets the number of text entries that have been registered or received in this process, in registration order.
	/// </summary>
	public static int EntryCount
	{
		get
		{
			lock (_lock)
			{
				return _registeredEntries.Count;
			}
		}
	}

	/// <summary>
	/// Registers a text in the table and returns its stable identifier.
	/// </summary>
	/// <param name="text">The text to identify.</param>
	/// <returns>
	/// Zero for null or empty text, the text's content hash otherwise, or an identifier from the reserved
	/// fallback range when another string already owns that hash.
	/// </returns>
	public static int GetOrAdd(string? text)
	{
		if (string.IsNullOrEmpty(text)) return 0;

		lock (_lock)
		{
			if (_idByText.TryGetValue(text, out int existingId)) return existingId;

			int primaryId = ComputePrimaryId(text);
			if (_textById.TryGetValue(primaryId, out string? occupant))
			{
				if (string.Equals(occupant, text, StringComparison.Ordinal)) return primaryId;
				return RegisterFallback(text, primaryId, occupant);
			}

			Store(primaryId, text);
			return primaryId;
		}
	}

	/// <summary>
	/// Retrieves the text registered for an identifier.
	/// </summary>
	/// <param name="id">The identifier to resolve.</param>
	/// <returns>The registered text, or an empty string when this process has never seen the identifier.</returns>
	public static string Get(int id)
	{
		if (id == 0) return string.Empty;
		lock (_lock)
		{
			return _textById.TryGetValue(id, out string? text) ? text : string.Empty;
		}
	}

	/// <summary>
	/// Attempts to retrieve the text registered for an identifier without allocating a fallback string.
	/// </summary>
	/// <param name="id">The identifier to resolve.</param>
	/// <param name="text">The registered text when the identifier is known to this process.</param>
	/// <returns>True when the identifier is present in the text table.</returns>
	public static bool TryGetText(int id, out string text)
	{
		if (id == 0)
		{
			text = string.Empty;
			return false;
		}

		lock (_lock)
		{
			if (_textById.TryGetValue(id, out string? found))
			{
				text = found;
				return true;
			}

			text = string.Empty;
			return false;
		}
	}

	/// <summary>
	/// Stores text that was registered by another peer, so identifiers this process never created can still be rendered.
	/// </summary>
	/// <param name="id">The identifier chosen by the owning peer.</param>
	/// <param name="text">The text that identifier stands for.</param>
	/// <returns>True when the table gained the entry, false when the identifier was already known.</returns>
	public static bool RegisterRemote(int id, string? text)
	{
		if (id == 0 || string.IsNullOrEmpty(text)) return false;

		lock (_lock)
		{
			if (_textById.ContainsKey(id)) return false;

			Store(id, text);
			return true;
		}
	}

	/// <summary>
	/// Computes the stable identifier a text would receive, without registering it.
	/// </summary>
	/// <param name="text">The text to identify.</param>
	/// <returns>Zero for null or empty text, the text's content hash otherwise.</returns>
	public static int ComputeId(string? text)
	{
		if (string.IsNullOrEmpty(text)) return 0;
		return ComputePrimaryId(text);
	}

	/// <summary>
	/// Copies registered entries into a caller supplied buffer for replication.
	/// </summary>
	/// <param name="destination">The list that receives the entries.</param>
	/// <param name="startIndex">The first entry index to copy.</param>
	public static void CopyEntries(List<(int Id, string Text)> destination, int startIndex = 0)
	{
		lock (_lock)
		{
			int start = Math.Clamp(startIndex, 0, _registeredEntries.Count);
			for (int index = start; index < _registeredEntries.Count; index++)
			{
				destination.Add(_registeredEntries[index]);
			}
		}
	}

	/// <summary>
	/// Clears every text entry while keeping identifiers stable, since identifiers are content hashes and not table slots.
	/// </summary>
	public static void Reset()
	{
		lock (_lock)
		{
			_textById.Clear();
			_idByText.Clear();
			_registeredEntries.Clear();
			_collisionCount = 0;
			_collisionSummary = string.Empty;
		}
	}

	private static int RegisterFallback(string text, int primaryId, string occupant)
	{
		_collisionCount++;
		if (_collisionSummary.Length < 512)
		{
			_collisionSummary += string.Format(
				CultureInfo.InvariantCulture,
				"'{0}' collides with '{1}' at {2}; ",
				text,
				occupant,
				primaryId);
		}

		int fallbackId = ComputeFallbackId(text);
		if (_textById.TryGetValue(fallbackId, out string? fallbackOccupant))
		{
			if (string.Equals(fallbackOccupant, text, StringComparison.Ordinal)) return fallbackId;
			return fallbackId;
		}

		Store(fallbackId, text);
		return fallbackId;
	}

	private static void Store(int id, string text)
	{
		_textById[id] = text;
		_idByText[text] = id;
		_registeredEntries.Add((id, text));
	}

	private static int ComputePrimaryId(string text)
	{
		uint hash = PrimaryHashSeed;
		for (int index = 0; index < text.Length; index++)
		{
			char character = text[index];
			hash = Combine(hash, (byte)character);
			hash = Combine(hash, (byte)(character >> 8));
		}

		int id = (int)(hash & 0x7FFFFFFFu);
		return id == 0 ? 1 : id;
	}

	private static int ComputeFallbackId(string text)
	{
		uint hash = FallbackHashSeed;
		for (int index = 0; index < text.Length; index++)
		{
			char character = text[index];
			hash = Combine(hash, (byte)(character >> 8));
			hash = Combine(hash, (byte)character);
		}

		int offset = (int)(hash & (uint)(FallbackIdBase - 1));
		return FallbackIdBase | offset;
	}

	private static uint Combine(uint hash, byte value)
	{
		unchecked
		{
			hash ^= value;
			hash *= 16777619u;
			return hash;
		}
	}
}
