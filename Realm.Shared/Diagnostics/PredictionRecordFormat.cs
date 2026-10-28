namespace Realm.Shared.Diagnostics;

/// <summary>
///     The binary shape of the records a match writes for its prediction report: the stream a client writes as it
///     predicts, and the stream the authority writes as it publishes. Both processes have to agree on it, so it lives
///     beside the other contracts they share rather than inside either one of them.
/// </summary>
public static class PredictionRecordFormat
{
	/// <summary>Identifies a prediction record stream, so a file that is not one is refused rather than misread.</summary>
	public const uint Magic = 0x50445243;

	/// <summary>Which layout of the records a stream holds. A reader refuses a stream it was not built for.</summary>
	public const int Version = 3;

	/// <summary>How many waypoints of a unit's remaining route one record carries.</summary>
	public const int MaximumRecordedWaypoints = 16;

	/// <summary>An order or a description the authority refused, and why.</summary>
	public const byte KindRejection = 4;

	/// <summary>What the authority knew about each unit on one published sequence.</summary>
	public const byte KindHostSnapshot = 20;
}
