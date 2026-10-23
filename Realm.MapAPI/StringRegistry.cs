using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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
/// reachable once both strings have been registered in the same process. The table itself lives in
/// <see cref="InternedValueStore"/>, which shares one identifier space between text and the structured
/// payload kinds that are still being migrated onto it.
/// </remarks>
public static class StringRegistry
{
	/// <summary>
	/// First identifier of the reserved fallback range used when a primary content hash is already taken.
	/// </summary>
	public const int FallbackIdBase = InternedValueStore.FallbackIdBase;

	/// <summary>
	/// Gets the number of text registrations that had to move into the reserved fallback range because another
	/// string already owned their primary content hash.
	/// </summary>
	public static int CollisionCount => InternedValueStore.CollisionCount;

	/// <summary>
	/// Gets a human readable summary of the first few hash collisions, listing both colliding texts.
	/// </summary>
	public static string CollisionSummary => InternedValueStore.CollisionSummary;

	/// <summary>
	/// Gets the number of text entries that have been registered or received in this process, in registration order.
	/// </summary>
	public static int EntryCount => InternedValueStore.TextEntryCount;

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
		return InternedValueStore.GetOrAddText(text);
	}

	/// <summary>
	/// Retrieves the text registered for an identifier, asking the host for text this process has never seen.
	/// </summary>
	/// <param name="id">The identifier to resolve.</param>
	/// <returns>The registered text, or an empty string when no process knows the identifier.</returns>
	/// <remarks>
	/// Inside the map script sandbox a miss is forwarded to the host through <see cref="IGameAPI.GetStringText"/> and
	/// cached locally, so an identifier written by the host into shared component memory becomes readable even when
	/// the script never registered that text. Because identifiers are content hashes the sandbox can verify what it
	/// receives: a host answer whose hash does not match the requested identifier is discarded.
	/// </remarks>
	public static string Get(int id)
	{
		if (TryGetText(id, out string known)) return known;

		return FetchFromHost(id);
	}

	private static string FetchFromHost(int id)
	{
		if (RuntimeInformation.ProcessArchitecture != Architecture.Wasm) return string.Empty;

		IGameAPI? gameApi = GameAPI.Current;
		if (gameApi == null) return string.Empty;

		string fetched = gameApi.GetStringText(id);
		if (string.IsNullOrEmpty(fetched)) return string.Empty;
		if (ComputeId(fetched) != id) return string.Empty;

		RegisterRemote(id, fetched);
		return fetched;
	}

	/// <summary>
	/// Attempts to retrieve the text registered for an identifier without allocating a fallback string.
	/// </summary>
	/// <param name="id">The identifier to resolve.</param>
	/// <param name="text">The registered text when the identifier is known to this process.</param>
	/// <returns>True when the identifier is present in the text table.</returns>
	public static bool TryGetText(int id, out string text)
	{
		return InternedValueStore.TryGetText(id, out text);
	}

	/// <summary>
	/// Stores text that was registered by another peer, so identifiers this process never created can still be rendered.
	/// </summary>
	/// <param name="id">The identifier chosen by the owning peer.</param>
	/// <param name="text">The text that identifier stands for.</param>
	/// <returns>True when the table gained the entry, false when the identifier was already known.</returns>
	public static bool RegisterRemote(int id, string? text)
	{
		return InternedValueStore.RegisterRemoteText(id, text);
	}

	/// <summary>
	/// Computes the stable identifier a text would receive, without registering it.
	/// </summary>
	/// <param name="text">The text to identify.</param>
	/// <returns>Zero for null or empty text, the text's content hash otherwise.</returns>
	public static int ComputeId(string? text)
	{
		return InternedValueStore.ComputeTextId(text);
	}

	/// <summary>
	/// Copies the entries a peer has not received yet into a caller supplied buffer, and reports the cursor
	/// that peer should pass next time.
	/// </summary>
	/// <param name="cursor">The cursor previously returned for this peer, or zero to copy every entry.</param>
	/// <param name="destination">The list that receives the entries.</param>
	/// <returns>The cursor that stands for the end of the table after this copy.</returns>
	public static int GetEntriesSince(int cursor, List<(int Id, string Text)> destination)
	{
		return InternedValueStore.GetTextEntriesSince(cursor, destination);
	}

	/// <summary>
	/// Copies registered entries into a caller supplied buffer for replication.
	/// </summary>
	/// <param name="destination">The list that receives the entries.</param>
	/// <param name="startIndex">The first entry index to copy.</param>
	public static void CopyEntries(List<(int Id, string Text)> destination, int startIndex = 0)
	{
		GetEntriesSince(startIndex, destination);
	}

	/// <summary>
	/// Builds a human readable dump of the table for diagnosing an identifier that renders as the wrong text or not at all.
	/// </summary>
	/// <param name="maximumEntries">The maximum number of entries to include, taken from the most recently registered end.</param>
	/// <returns>One line per entry with its identifier, kind and text, framed by a summary of the table and its collisions.</returns>
	public static string DescribeTable(int maximumEntries = 200)
	{
		return InternedValueStore.DescribeTable(maximumEntries);
	}

	/// <summary>
	/// Clears every text entry while keeping identifiers stable, since identifiers are content hashes and not table slots.
	/// </summary>
	public static void Reset()
	{
		InternedValueStore.Reset();
	}
}
