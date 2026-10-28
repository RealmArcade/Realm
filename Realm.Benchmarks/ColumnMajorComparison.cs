namespace Realm.Benchmarks;

/// <summary>
///     What the same delta costs when its unit records are laid out entity by entity as they are today, against the
///     same values laid out field by field across the peer's units. A wire that groups like values next to each other
///     gives the encoder homogeneous columns of floats to find redundancy in, which is the part of sending raw arrays
///     that is worth having; this measures that gain without pretending the arrays themselves could be sent.
/// </summary>
public sealed record ColumnMajorComparison(
	double Units,
	double RowMajorSerializedBytes,
	double RowMajorFrameBytes,
	double RowMajorEncodeMicros,
	double ColumnMajorBytes,
	double ColumnMajorFrameBytes,
	double ColumnMajorEncodeMicros)
{
	/// <summary>How much smaller the field-major frame is than the entity-major one, as a fraction of the latter.</summary>
	public double FrameSaving => RowMajorFrameBytes <= 0 ? 0 : 1.0 - ColumnMajorFrameBytes / RowMajorFrameBytes;
}
