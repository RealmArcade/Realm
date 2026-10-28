using System.Globalization;
using Realm.Benchmarks;

if (!BenchmarkOptions.TryParse(args, out BenchmarkOptions options, out string error))
{
	Console.Error.WriteLine(error);
	return 64;
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
Console.WriteLine("baseline burst (one baseline per peer on the same tick)");
WriteRow("all peers", result.BaselineBytesPerPeer * result.Players / 1_000_000.0, " MB");

return 0;

string RuntimeInformation() => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

static double Megabits(double bytesPerSecond) => bytesPerSecond * 8.0 / 1_000_000.0;

static void WriteRow(string label, double value, string suffix = " ms")
{
	Console.WriteLine($"  {label,-26}{value.ToString("F2", CultureInfo.InvariantCulture).PadLeft(12)}{suffix}");
}

static void WriteHeaderRow(string label, string first, string second)
{
	Console.WriteLine($"  {label,-26}{first,14}{second,20}");
}
