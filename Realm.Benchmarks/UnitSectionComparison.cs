namespace Realm.Benchmarks;

/// <summary>
///     What one peer's units cost on the wire three ways: as one record per unit, which is what a delta carried before
///     the section became columnar; the same values laid out field by field but still as floats, which is the layout
///     change on its own; and as the quantized columnar section a peer is actually sent now. Separating the last two
///     says how much of the frame's saving is layout and how much is precision the screen cannot show.
/// </summary>
public sealed record UnitSectionComparison(
	double Units,
	double RecordSerializedBytes,
	double RecordFrameBytes,
	double RecordEncodeMicros,
	double FieldMajorBytes,
	double FieldMajorFrameBytes,
	double SectionSerializedBytes,
	double SectionFrameBytes,
	double SectionEncodeMicros)
{
	/// <summary>How much smaller the frame a peer receives now is than the one it received before.</summary>
	public double FrameSaving => RecordFrameBytes <= 0 ? 0 : 1.0 - SectionFrameBytes / RecordFrameBytes;

	/// <summary>How much smaller the field-major section is than one record per unit, as a fraction of the frame.</summary>
	public double LayoutSaving => RecordFrameBytes <= 0 ? 0 : 1.0 - FieldMajorFrameBytes / RecordFrameBytes;
}
