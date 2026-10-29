using System.Diagnostics;
using System.Globalization;
using Realm.Benchmarks;

if (!BenchmarkOptions.TryParse(args, out BenchmarkOptions options, out string error))
{
	Console.Error.WriteLine(error);
	return 64;
}

if (options.Probe)
{
	RunScalingProbe();
	return 0;
}

int gen0Before = GC.CollectionCount(0);
int gen1Before = GC.CollectionCount(1);
int gen2Before = GC.CollectionCount(2);

using var benchmark = new AuthorityBenchmark(options);
BenchmarkResult result = benchmark.Run();

int gen0 = GC.CollectionCount(0) - gen0Before;
int gen1 = GC.CollectionCount(1) - gen1Before;
int gen2 = GC.CollectionCount(2) - gen2Before;

Console.WriteLine("Realm authority capacity benchmark");
Console.WriteLine($"machine            {Environment.MachineName}  {Environment.ProcessorCount} logical cores  {RuntimeInformation()}");
Console.WriteLine($"scenario           {result.Units} combat units, {result.Players} players, {result.Ticks} measured ticks after {options.WarmupTicks} warmup");
Console.WriteLine($"description workers {result.Workers}");
Console.WriteLine($"tick delta         {result.TickDeltaSeconds * 1000.0:F2} ms ({1.0 / result.TickDeltaSeconds:F0} Hz nominal)");
Console.WriteLine($"reference derate   {result.ReferenceDerate:F2} (measured micros are divided by this to model an average gaming CPU)");
Console.WriteLine($"gc during run      gen0={gen0} gen1={gen1} gen2={gen2}");
Console.WriteLine();

Console.WriteLine("authority tick (one core, one thread)");
WriteRow("cooldowns + clock", result.CooldownMicros / 1000.0);
WriteRow("movement + separation", result.MovementMicros / 1000.0);
WriteRow("combat + targeting", result.CombatMicros / 1000.0);
WriteRow("economy", result.EconomyMicros / 1000.0);
WriteRow("status + regeneration", result.StatusMicros / 1000.0);
WriteRow("tick total mean", result.MeanTickMicros / 1000.0);
WriteRow("tick total p50", result.P50TickMicros / 1000.0);
WriteRow("tick total p95", result.P95TickMicros / 1000.0);
WriteRow("tick total p99", result.P99TickMicros / 1000.0);
WriteRow("tick total max", result.MaxTickMicros / 1000.0);
Console.WriteLine();

Console.WriteLine($"snapshot per peer ({result.VisibleUnitsPerPeer:F1} units visible on average)");
WriteRow("build", result.SnapshotBuildMicros / 1000.0);
WriteRow("memorypack serialize", result.SnapshotSerializeMicros / 1000.0);
WriteRow("zstd frame", result.SnapshotCompressMicros / 1000.0);
WriteRow("snapshot total", result.SnapshotMicrosPerPeer / 1000.0);
WriteRow("phase, all peers wall clock", result.SnapshotPhaseMicros / 1000.0);
WriteRow("delta frame bytes", result.DeltaBytesPerPeer, " B");
WriteRow("baseline frame bytes", result.BaselineBytesPerPeer, " B");
WriteRow("delta bytes, shroud off", result.AllVisibleDeltaBytesPerPeer, " B");
Console.WriteLine();

Console.WriteLine($"authority cost as a share of one core (tick + {result.Players} snapshots per tick)");
WriteRow("per tick, this machine", result.PerTickMicrosAllPeers / 1000.0, " ms");
WriteRow("per tick, reference CPU", result.DeratedPerTickMicros / 1000.0, " ms");
WriteRow("duty at 20 Hz, reference", result.DeratedDutyAt(20) * 100.0, " %");
WriteRow("duty at 30 Hz, reference", result.DeratedDutyAt(30) * 100.0, " %");
WriteRow("duty at 60 Hz, reference", result.DeratedDutyAt(60) * 100.0, " %");
WriteRow("highest rate at 50% duty", result.MaximumRateAtDuty(0.50), " Hz");
Console.WriteLine();

Console.WriteLine("bandwidth per snapshot rate (delta frames only)");
WriteHeaderRow("rate", "upstream all peers", "downstream per peer");
for (int rateIndex = 0; rateIndex < 3; rateIndex++)
{
	double rate = rateIndex == 0 ? 20 : rateIndex == 1 ? 30 : 60;
	Console.WriteLine($"  {rate,7:F0} Hz  {Megabits(result.UpstreamBytesPerSecond(rate)),14:F2} Mbps  {Megabits(result.DownstreamBytesPerSecond(rate)),17:F2} Mbps");
}

Console.WriteLine();
Console.WriteLine();
Console.WriteLine($"delta frame composition ({result.Composition.UnitCount:F0} unit records, {result.Composition.ComponentCount:F0} component records)");
WriteRow("serialized", result.Composition.SerializedBytes, " B");
WriteRow("frame", result.Composition.FrameBytes, " B");
WriteRow("unit records", result.Composition.UnitBytes, " B");
WriteRow("component records", result.Composition.ComponentBytes, " B");
WriteRow("ability states", result.Composition.AbilityBytes, " B");
WriteRow("catalog", result.Composition.CatalogBytes, " B");
WriteRow("cold payloads", result.Composition.ColdPayloadBytes, " B");
Console.WriteLine();

Console.WriteLine("baseline burst (one baseline per peer on the same tick)");
WriteRow("all peers", result.BaselineBytesPerPeer * result.Players / 1_000_000.0, " MB");

if (options.ZstdSweep)
{
	ZstdSweep sweep = benchmark.MeasureZstdSweep(BenchmarkOptions.DefaultZstdSweepFrames);
	Console.WriteLine();
	Console.WriteLine($"delta framing at each zstd level ({sweep.FrameCount:F0} consecutive ticks of one peer)");
	WriteRow("unframed frame", sweep.UncompressedBytesPerFrame, " B");
	WriteRow("unframed copy", sweep.UncompressedMicrosPerFrame, " us");
	WriteRow("memorypack serialize", sweep.SerializeMedianMicros, " us");
	WriteRow("  allocated by it", sweep.SerializeBytesAllocated, " B");
	WriteRow("encode (build frame)", sweep.EncodeMedianMicros, " us");
	WriteRow("  allocated by it", sweep.EncodeBytesAllocated, " B");
	Console.WriteLine("    level     bytes     ratio    median       p95   upstream 30 Hz");

	foreach (ZstdLevelSample sample in sweep.Levels)
	{
		Console.WriteLine(
			$"  {sample.Level,-8}{sample.BytesPerFrame,10:F1}{sample.Ratio,9:F3}{sample.MedianMicrosPerFrame,10:F1}{sample.P95MicrosPerFrame,9:F1}{sample.UpstreamMbps(result.Players, 30),15:F2} Mbps");
	}

	UnitSectionComparison section = benchmark.MeasureUnitSection(BenchmarkOptions.DefaultZstdSweepFrames);
	Console.WriteLine();
	Console.WriteLine($"unit section ({section.Units:F1} units on a delta)");
	Console.WriteLine($"  {"layout",-30}{"serialized",14}{"frame",14}{"encode",12}");
	Console.WriteLine($"  {"one record per unit (before)",-30}{section.RecordSerializedBytes,14:F1}{section.RecordFrameBytes,14:F1}{section.RecordEncodeMicros,10:F1} us");
	Console.WriteLine($"  {"field by field, as floats",-30}{section.FieldMajorBytes,14:F1}{section.FieldMajorFrameBytes,14:F1}");
	Console.WriteLine($"  {"field by field, quantized",-30}{section.SectionSerializedBytes,14:F1}{section.SectionFrameBytes,14:F1}{section.SectionEncodeMicros,10:F1} us");
	WriteRow("layout alone saving", section.LayoutSaving * 100.0, " %");
	WriteRow("layout + precision saving", section.FrameSaving * 100.0, " %");
}

return 0;

string RuntimeInformation() => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

static double Megabits(double bytesPerSecond) => bytesPerSecond * 8.0 / 1_000_000.0;

static void RunScalingProbe()
{
	const int IterationsPerTask = 4_000_000;
	const int Tasks = 24;

	Console.WriteLine($"parallel scaling probe: {Tasks} tasks of {IterationsPerTask:N0} arithmetic iterations");
	double singleThread = 0;

	foreach (int workers in new[] { 1, 2, 4, 8, 12 })
	{
		var options = new ParallelOptions { MaxDegreeOfParallelism = workers };
		long start = Stopwatch.GetTimestamp();
		Parallel.For(0, Tasks, options, _ => Work(IterationsPerTask));
		double elapsed = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;

		if (workers == 1) singleThread = elapsed;
		Console.WriteLine($"  {workers,2} worker(s)  {elapsed,9:F2} ms  speedup {singleThread / elapsed,5:F2}x  {(singleThread / elapsed) / workers * 100,5:F0}% of linear");
	}

	static double Work(int iterations)
	{
		double total = 0;
		for (int index = 0; index < iterations; index++) total += Math.Sqrt(index * 0.5 + 1.0) * 0.25 - Math.Sin(index * 0.001);
		return total;
	}
}

static void WriteRow(string label, double value, string suffix = " ms")
{
	Console.WriteLine($"  {label,-26}{value.ToString("F2", CultureInfo.InvariantCulture).PadLeft(12)}{suffix}");
}

static void WriteHeaderRow(string label, string first, string second)
{
	Console.WriteLine($"  {label,-26}{first,14}{second,20}");
}
