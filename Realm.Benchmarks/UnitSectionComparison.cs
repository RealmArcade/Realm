namespace Realm.Benchmarks;

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
	public double FrameSaving => RecordFrameBytes <= 0 ? 0 : 1.0 - SectionFrameBytes / RecordFrameBytes;

	public double LayoutSaving => RecordFrameBytes <= 0 ? 0 : 1.0 - FieldMajorFrameBytes / RecordFrameBytes;
}
