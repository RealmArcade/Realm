using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace Realm.Ecs;

/// <summary>
/// Owns the single table that every process uses to translate content identifiers into the values they stand for,
/// starting with text and structured to hold further payload kinds in the same identifier space.
/// </summary>
/// <remarks>
/// Identifiers are content hashes rather than allocation sequence numbers, so the host, the client and the map
/// script sandbox agree on the identifier for a given payload without sharing any state. Two payloads of the same
/// kind that hash to the same primary identifier are resolved at registration time: the later one receives an
/// identifier from the reserved fallback range so no two values alias. Colliding payloads are reported through
/// <see cref="CollisionCount"/> and <see cref="CollisionSummary"/> because the fallback decision is only reachable
/// once both payloads have been registered in the same process.
/// <para>
/// Registration is serialized on the thread that registers, because resolving a collision has to be atomic with the
/// lookup that found it. Reading a single entry is not: entries are immutable once stored, so they are published into a
/// concurrent table and a snapshot lane resolves identifiers per unit per codec without ever acquiring the registration
/// lock. Only the ordered cursor walk takes it, because that walk has to be consistent with the list it walks.
/// </para>
/// </remarks>
public static class ContentIdStore
{
	/// <summary>
	/// The kind tag of the entries whose payload is text.
	/// </summary>
	public const byte TextKind = 0;

	/// <summary>
	/// The kind tag of the entries whose payload is an ordered list of text identifiers.
	/// </summary>
	public const byte TextListKind = 1;

	/// <summary>
	/// The kind tag of the entries whose payload is an ordered list of blittable weapon records.
	/// </summary>
	public const byte WeaponListKind = 2;

	/// <summary>
	/// The kind tag of the entries whose payload is an ordered list of blittable zone boundaries.
	/// </summary>
	public const byte ZoneListKind = 3;

	/// <summary>
	/// First identifier of the reserved fallback range used when a primary content hash is already taken.
	/// </summary>
	public const int FallbackIdBase = 0x40000000;

	/// <summary>
	/// The version of the identifier scheme that maps content to identifiers.
	/// </summary>
	/// <remarks>
	/// The host, the client and the map script sandbox must derive the same identifier for the same content, so the
	/// scheme is frozen: text is a 32 bit FNV-1a hash over its UTF-16 code units, taken in low byte then high byte
	/// order, and a structured payload is the same hash over its kind byte followed by its payload bytes, masked to
	/// 31 bits with zero remapped to one. Colliding content of the same kind moves into <see cref="FallbackIdBase"/>.
	/// Raising this version means every peer and every saved payload must migrate, not that hashes shift silently.
	/// </remarks>
	public const int SchemeVersion = 1;

	private const uint PrimaryHashSeed = 2166136261u;
	private const uint FallbackHashSeed = 0x811C9DC5u ^ 0x5BD1E995u;

	private static readonly ConcurrentDictionary<int, ContentIdEntry> _entryById = new ConcurrentDictionary<int, ContentIdEntry>();
	private static readonly Dictionary<string, int> _textIdByText = new Dictionary<string, int>(StringComparer.Ordinal);
	private static readonly List<ContentIdEntry> _registeredEntries = new List<ContentIdEntry>();
	private static readonly List<ContentIdEntry> _registeredTextEntries = new List<ContentIdEntry>();
	private static readonly object _lock = new object();

	private static int _collisionCount;
	private static int _registeredEntryCount;
	private static int _registeredTextEntryCount;
	private static string _collisionSummary = string.Empty;

	/// <summary>
	/// Gets the number of payload registrations that had to move into the reserved fallback range because another
	/// payload already owned their primary content hash.
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
	/// Gets a human readable summary of the first few hash collisions, listing both colliding payloads.
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
	/// Gets the number of entries that have been registered or received in this process, in registration order.
	/// </summary>
	public static int EntryCount => Volatile.Read(ref _registeredEntryCount);

	/// <summary>
	/// Gets the number of text entries that have been registered or received in this process, which is the number
	/// of entries a peer's text cursor walks past.
	/// </summary>
	public static int TextEntryCount => Volatile.Read(ref _registeredTextEntryCount);

	/// <summary>
	/// Registers a text in the table and returns its stable identifier.
	/// </summary>
	/// <param name="text">The text to identify.</param>
	/// <returns>
	/// Zero for null or empty text, the text's content hash otherwise, or an identifier from the reserved
	/// fallback range when another text already owns that hash.
	/// </returns>
	public static int GetOrAddText(string? text)
	{
		if (string.IsNullOrEmpty(text)) return 0;

		lock (_lock)
		{
			if (_textIdByText.TryGetValue(text, out int existingId)) return existingId;

			int primaryId = ComputeTextId(text);
			if (_entryById.TryGetValue(primaryId, out ContentIdEntry occupant))
			{
				if (occupant.Kind == TextKind && string.Equals(occupant.Text, text, StringComparison.Ordinal)) return primaryId;
				return RegisterTextFallback(text, primaryId, occupant.Text);
			}

			Store(new ContentIdEntry(primaryId, TextKind, text));
			return primaryId;
		}
	}

	/// <summary>
	/// Retrieves the text registered for an identifier in this process.
	/// </summary>
	/// <param name="id">The identifier to resolve.</param>
	/// <param name="text">The registered text when the identifier is known to this process.</param>
	/// <returns>True when the identifier is present in the table and carries text.</returns>
	public static bool TryGetText(int id, out string text)
	{
		if (id == 0)
		{
			text = string.Empty;
			return false;
		}

		if (_entryById.TryGetValue(id, out ContentIdEntry entry) && entry.Kind == TextKind)
		{
			text = entry.Text;
			return true;
		}

		text = string.Empty;
		return false;
	}

	/// <summary>
	/// Stores text that was registered by another peer, so identifiers this process never created can still be rendered.
	/// </summary>
	/// <param name="id">The identifier chosen by the owning peer.</param>
	/// <param name="text">The text that identifier stands for.</param>
	/// <returns>True when the table gained the entry, false when the identifier was already known.</returns>
	public static bool RegisterRemoteText(int id, string? text)
	{
		if (id == 0 || string.IsNullOrEmpty(text)) return false;

		lock (_lock)
		{
			if (_entryById.ContainsKey(id)) return false;

			Store(new ContentIdEntry(id, TextKind, text));
			return true;
		}
	}

	/// <summary>
	/// Registers an ordered list of text identifiers and returns its stable identifier.
	/// </summary>
	/// <param name="textIds">The text identifiers the list contains, in order.</param>
	/// <returns>Zero for an empty list, the list's content hash otherwise.</returns>
	public static int GetOrAddTextList(ReadOnlySpan<int> textIds)
	{
		if (textIds.IsEmpty) return 0;

		byte[] payload = new byte[sizeof(int) + (textIds.Length * sizeof(int))];
		BinaryPrimitives.WriteInt32LittleEndian(payload, textIds.Length);
		for (int index = 0; index < textIds.Length; index++)
		{
			BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(sizeof(int) + (index * sizeof(int)), sizeof(int)), textIds[index]);
		}

		return GetOrAddPayload(TextListKind, payload);
	}

	/// <summary>
	/// Gets the number of text identifiers stored in a list payload.
	/// </summary>
	/// <param name="id">The identifier of the list payload.</param>
	/// <returns>The entry count, or zero when the identifier is unknown or does not carry a list.</returns>
	public static int GetTextListCount(int id)
	{
		if (id == 0) return 0;

		return _entryById.TryGetValue(id, out ContentIdEntry entry) && entry.Kind == TextListKind && entry.Bytes != null
			? ReadTextListCount(entry.Bytes)
			: 0;
	}

	/// <summary>
	/// Gets the text identifier stored at a position in a list payload.
	/// </summary>
	/// <param name="id">The identifier of the list payload.</param>
	/// <param name="index">The zero-based position to read.</param>
	/// <returns>The text identifier at that position, or zero when the position is outside the list.</returns>
	public static int GetTextListItemId(int id, int index)
	{
		if (id == 0 || index < 0) return 0;

		if (!_entryById.TryGetValue(id, out ContentIdEntry entry) || entry.Kind != TextListKind || entry.Bytes == null) return 0;

		int count = ReadTextListCount(entry.Bytes);
		if (index >= count) return 0;

		return BinaryPrimitives.ReadInt32LittleEndian(entry.Bytes.AsSpan(sizeof(int) + (index * sizeof(int)), sizeof(int)));
	}

	/// <summary>
	/// Gets the kind and payload bytes stored for an identifier.
	/// </summary>
	/// <param name="id">The identifier to inspect.</param>
	/// <param name="kind">The kind tag of the stored payload.</param>
	/// <param name="payload">The immutable payload bytes, empty for text entries.</param>
	/// <returns>True when the identifier is present in the table.</returns>
	public static bool TryGetPayload(int id, out byte kind, out ReadOnlySpan<byte> payload)
	{
		if (_entryById.TryGetValue(id, out ContentIdEntry entry))
		{
			kind = entry.Kind;
			payload = entry.Bytes ?? ReadOnlySpan<byte>.Empty;
			return true;
		}

		kind = 0;
		payload = ReadOnlySpan<byte>.Empty;
		return false;
	}

	/// <summary>
	/// Registers an opaque blittable payload and returns its stable identifier.
	/// </summary>
	/// <param name="kind">The kind tag that tells readers how to interpret the bytes.</param>
	/// <param name="payload">The immutable payload bytes.</param>
	/// <returns>Zero for an empty payload, the payload's content hash otherwise.</returns>
	public static int GetOrAddBytes(byte kind, ReadOnlySpan<byte> payload)
	{
		if (payload.IsEmpty) return 0;

		return GetOrAddPayload(kind, payload.ToArray());
	}

	/// <summary>
	/// Computes the stable identifier a text would receive, without registering it.
	/// </summary>
	/// <param name="text">The text to identify.</param>
	/// <returns>Zero for null or empty text, the text's content hash otherwise.</returns>
	public static int ComputeTextId(string? text)
	{
		if (string.IsNullOrEmpty(text)) return 0;

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

	/// <summary>
	/// Copies the text entries a peer has not received yet into a caller supplied buffer, and reports the cursor
	/// that peer should pass next time.
	/// </summary>
	/// <remarks>
	/// The cursor counts text entries only, so registering a payload of another kind between two texts never
	/// advances it past a text the peer has not received.
	/// </remarks>
	/// <param name="cursor">The cursor previously returned for this peer, or zero to copy every text entry.</param>
	/// <param name="destination">The list that receives the entries.</param>
	/// <returns>The cursor that stands for the end of the text table after this copy.</returns>
	public static int GetTextEntriesSince(int cursor, List<(int Id, string Text)> destination)
	{
		lock (_lock)
		{
			int start = Math.Clamp(cursor, 0, _registeredTextEntries.Count);
			for (int index = start; index < _registeredTextEntries.Count; index++)
			{
				ContentIdEntry entry = _registeredTextEntries[index];
				destination.Add((entry.Id, entry.Text));
			}

			return _registeredTextEntries.Count;
		}
	}

	/// <summary>
	/// Gets the kind tag stored for an identifier.
	/// </summary>
	/// <param name="id">The identifier to inspect.</param>
	/// <returns>True when the identifier is present in the table.</returns>
	/// <param name="kind">The kind tag of the stored payload.</param>
	public static bool TryGetKind(int id, out byte kind)
	{
		if (_entryById.TryGetValue(id, out ContentIdEntry entry))
		{
			kind = entry.Kind;
			return true;
		}

		kind = 0;
		return false;
	}

	/// <summary>
	/// Builds a human readable dump of the table for diagnosing an identifier that renders as the wrong value or not at all.
	/// </summary>
	/// <param name="maximumEntries">The maximum number of entries to include, taken from the most recently registered end.</param>
	/// <returns>One line per entry with its identifier, kind and text, framed by a summary of the table and its collisions.</returns>
	public static string DescribeTable(int maximumEntries = 200)
	{
		lock (_lock)
		{
			var builder = new StringBuilder();
			builder.AppendLine($"content id table: {_registeredEntries.Count} entries, {_collisionCount} collisions");

			int start = Math.Max(0, _registeredEntries.Count - Math.Max(0, maximumEntries));
			if (start > 0) builder.AppendLine($"showing the last {_registeredEntries.Count - start} entries");

			for (int index = start; index < _registeredEntries.Count; index++)
			{
				ContentIdEntry entry = _registeredEntries[index];
				builder.AppendLine(entry.Bytes == null
					? $"id={entry.Id} kind={DescribeKind(entry.Kind)} text='{entry.Text}'"
					: $"id={entry.Id} kind={DescribeKind(entry.Kind)} count={ReadTextListCount(entry.Bytes)}");
			}

			return builder.ToString();
		}
	}

	/// <summary>
	/// Clears every entry while keeping identifiers stable, since identifiers are content hashes and not table slots.
	/// </summary>
	public static void Reset()
	{
		lock (_lock)
		{
			_entryById.Clear();
			_textIdByText.Clear();
			_registeredEntries.Clear();
			_registeredTextEntries.Clear();
			_collisionCount = 0;
			_registeredEntryCount = 0;
			_registeredTextEntryCount = 0;
			_collisionSummary = string.Empty;
		}
	}

	/// <summary>
	/// Gets the lower case name of a kind tag for logs and dumps.
	/// </summary>
	/// <param name="kind">The kind tag to describe.</param>
	/// <returns>The kind name, or the numeric tag when the kind has no name yet.</returns>
	public static string DescribeKind(byte kind)
	{
		return kind switch
		{
			TextKind => "string",
			TextListKind => "text-list",
			WeaponListKind => "weapon-list",
			ZoneListKind => "zone-list",
			_ => kind.ToString(CultureInfo.InvariantCulture)
		};
	}

	private static int GetOrAddPayload(byte kind, byte[] payload)
	{
		lock (_lock)
		{
			int primaryId = ComputePayloadId(kind, payload);
			if (_entryById.TryGetValue(primaryId, out ContentIdEntry occupant))
			{
				if (occupant.Kind == kind && occupant.Bytes != null && occupant.Bytes.AsSpan().SequenceEqual(payload)) return primaryId;
				return RegisterPayloadFallback(kind, payload, primaryId, occupant);
			}

			Store(new ContentIdEntry(primaryId, kind, string.Empty, payload));
			return primaryId;
		}
	}

	private static int RegisterPayloadFallback(byte kind, byte[] payload, int primaryId, ContentIdEntry occupant)
	{
		_collisionCount++;
		if (_collisionSummary.Length < 512)
		{
			_collisionSummary += string.Format(
				CultureInfo.InvariantCulture,
				"a {0} payload collides with a {1} payload at {2}; ",
				DescribeKind(kind),
				DescribeKind(occupant.Kind),
				primaryId);
		}

		int fallbackId = ComputePayloadFallbackId(kind, payload);
		if (_entryById.TryGetValue(fallbackId, out ContentIdEntry fallbackOccupant))
		{
			if (fallbackOccupant.Kind == kind && fallbackOccupant.Bytes != null && fallbackOccupant.Bytes.AsSpan().SequenceEqual(payload)) return fallbackId;
			return fallbackId;
		}

		Store(new ContentIdEntry(fallbackId, kind, string.Empty, payload));
		return fallbackId;
	}

	private static int ComputePayloadId(byte kind, ReadOnlySpan<byte> payload)
	{
		uint hash = PrimaryHashSeed;
		hash = Combine(hash, kind);
		for (int index = 0; index < payload.Length; index++)
		{
			hash = Combine(hash, payload[index]);
		}

		int id = (int)(hash & 0x7FFFFFFFu);
		return id == 0 ? 1 : id;
	}

	private static int ComputePayloadFallbackId(byte kind, ReadOnlySpan<byte> payload)
	{
		uint hash = FallbackHashSeed;
		hash = Combine(hash, kind);
		for (int index = payload.Length - 1; index >= 0; index--)
		{
			hash = Combine(hash, payload[index]);
		}

		int offset = (int)(hash & (uint)(FallbackIdBase - 1));
		return FallbackIdBase | offset;
	}

	private static int ReadTextListCount(ReadOnlySpan<byte> payload)
	{
		if (payload.Length < sizeof(int)) return 0;

		int count = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0, sizeof(int)));
		if (count < 0 || payload.Length < sizeof(int) + (count * sizeof(int))) return 0;

		return count;
	}

	private static int RegisterTextFallback(string text, int primaryId, string occupant)
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

		int fallbackId = ComputeTextFallbackId(text);
		if (_entryById.TryGetValue(fallbackId, out ContentIdEntry fallbackOccupant))
		{
			if (fallbackOccupant.Kind == TextKind && string.Equals(fallbackOccupant.Text, text, StringComparison.Ordinal)) return fallbackId;
			return fallbackId;
		}

		Store(new ContentIdEntry(fallbackId, TextKind, text));
		return fallbackId;
	}

	private static void Store(ContentIdEntry entry)
	{
		_entryById[entry.Id] = entry;
		_registeredEntries.Add(entry);
		Volatile.Write(ref _registeredEntryCount, _registeredEntries.Count);
		if (entry.Kind != TextKind) return;

		_textIdByText[entry.Text] = entry.Id;
		_registeredTextEntries.Add(entry);
		Volatile.Write(ref _registeredTextEntryCount, _registeredTextEntries.Count);
	}

	private static int ComputeTextFallbackId(string text)
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

	private readonly struct ContentIdEntry
	{
		public ContentIdEntry(int id, byte kind, string text, byte[]? bytes = null)
		{
			Id = id;
			Kind = kind;
			Text = text;
			Bytes = bytes;
		}

		public int Id { get; }
		public byte Kind { get; }
		public string Text { get; }
		public byte[]? Bytes { get; }
	}
}
