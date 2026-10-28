using System.Globalization;

namespace Realm.Benchmarks;

public sealed record BenchmarkOptions(
	int Units,
	int Players,
	int Ticks,
	int WarmupTicks,
	double ReferenceDerate,
	double TickDeltaSeconds,
	int Workers,
	bool Probe)
{
	public const int DefaultUnits = 300;
	public const int DefaultPlayers = 24;
	public const int DefaultTicks = 600;
	public const int DefaultWarmupTicks = 120;
	public const double DefaultReferenceDerate = 0.65;
	public const double DefaultTickDeltaSeconds = 1.0 / 30.0;
	public const int DefaultWorkers = 0;

	public static string Usage =>
		"Realm.Benchmarks [--units 300] [--players 24] [--ticks 600] [--warmup 120] [--derate 0.75] [--tick-delta 0.0333] [--workers 0]";

	public static bool TryParse(string[] arguments, out BenchmarkOptions options, out string error)
	{
		options = new BenchmarkOptions(DefaultUnits, DefaultPlayers, DefaultTicks, DefaultWarmupTicks, DefaultReferenceDerate, DefaultTickDeltaSeconds, DefaultWorkers, false);
		error = string.Empty;

		for (int index = 0; index < arguments.Length; index++)
		{
			string name = arguments[index];
			if (name is "--help" or "-h")
			{
				error = Usage;
				return false;
			}

			if (name is "--probe")
			{
				options = options with { Probe = true };
				continue;
			}

			if (index + 1 >= arguments.Length)
			{
				error = $"'{name}' needs a value.";
				return false;
			}

			string value = arguments[index + 1];
			index++;

			switch (name)
			{
				case "--units":
					if (!TryReadInt(value, 1, 4096, out int units)) return Fail(out error, name, value);
					options = options with { Units = units };
					break;
				case "--players":
					if (!TryReadInt(value, 1, 24, out int players)) return Fail(out error, name, value);
					options = options with { Players = players };
					break;
				case "--ticks":
					if (!TryReadInt(value, 1, 200000, out int ticks)) return Fail(out error, name, value);
					options = options with { Ticks = ticks };
					break;
				case "--warmup":
					if (!TryReadInt(value, 0, 200000, out int warmup)) return Fail(out error, name, value);
					options = options with { WarmupTicks = warmup };
					break;
				case "--workers":
					if (!TryReadInt(value, 0, 64, out int workers)) return Fail(out error, name, value);
					options = options with { Workers = workers };
					break;
				case "--derate":
					if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double derate) || derate <= 0.05 || derate > 1.0) return Fail(out error, name, value);
					options = options with { ReferenceDerate = derate };
					break;
				case "--tick-delta":
					if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double delta) || delta <= 0.0 || delta > 0.5) return Fail(out error, name, value);
					options = options with { TickDeltaSeconds = delta };
					break;
				default:
					error = $"unknown option '{name}'.\n{Usage}";
					return false;
			}
		}

		return true;
	}

	private static bool TryReadInt(string value, int minimum, int maximum, out int parsed)
	{
		return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
		       && parsed >= minimum
		       && parsed <= maximum;
	}

	private static bool Fail(out string error, string name, string value)
	{
		error = $"'{name}' does not accept '{value}'.\n{Usage}";
		return false;
	}
}
