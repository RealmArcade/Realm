using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Godot;
using Environment = System.Environment;

namespace Realm.Client.Utils;

public static class ProcessPerformanceOptimizer
{
	private const int RelationProcessorCore = 0;
	private const int ProcessPowerThrottling = 4;
	private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

	[StructLayout(LayoutKind.Sequential)]
	private struct PROCESS_POWER_THROTTLING_STATE
	{
		public uint Version;
		public uint ControlMask;
		public uint StateMask;
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool SetProcessInformation(
		IntPtr hProcess,
		int processInformationClass,
		ref PROCESS_POWER_THROTTLING_STATE processInformation,
		uint processInformationSize);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GetLogicalProcessorInformationEx(
		int relationshipType,
		IntPtr buffer,
		ref int returnedLength);

	private static bool _initialized;

	public static bool IsHybridCpu { get; private set; }
	public static long PerformanceCoreAffinityMask { get; private set; }
	public static long EfficiencyCoreAffinityMask { get; private set; }
	public static bool EcoQoSDisabled { get; private set; }
	public static ProcessPriorityClass CurrentPriority { get; private set; } = ProcessPriorityClass.Normal;

	public static void EnforceOptimalPerformance(bool force = false)
	{
		if (_initialized && !force) return;
		_initialized = true;

		if (!OperatingSystem.IsWindows())
		{
			return;
		}

		string? disableEnv = Environment.GetEnvironmentVariable("REALM_DISABLE_PCORE_AFFINITY");
		bool affinityDisabledByEnv = disableEnv == "1" || string.Equals(disableEnv, "true", StringComparison.OrdinalIgnoreCase);

		DisableEcoQoS();

		ElevateProcessPriority();

		if (!affinityDisabledByEnv)
		{
			ConfigureCpuAffinity();
		}
		else
		{
			Log("[Performance] P-core affinity enforcement skipped via REALM_DISABLE_PCORE_AFFINITY.");
		}
	}

	private static void DisableEcoQoS()
	{
		try
		{
			var state = new PROCESS_POWER_THROTTLING_STATE
			{
				Version = 1,
				ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
				StateMask = 0 // Explicitly disable speed throttling
			};

			bool success = SetProcessInformation(
				Process.GetCurrentProcess().Handle,
				ProcessPowerThrottling,
				ref state,
				(uint)Marshal.SizeOf(state));

			EcoQoSDisabled = success;
			if (success)
			{
				Log("[Performance] Windows Power Throttling (EcoQoS) disabled successfully.");
			}
			else
			{
				int err = Marshal.GetLastWin32Error();
				Log($"[Performance] Notice: SetProcessInformation(ProcessPowerThrottling) returned {err}.");
			}
		}
		catch (Exception ex)
		{
			Log($"[Performance] Failed to disable EcoQoS: {ex.Message}");
		}
	}

	private static void ElevateProcessPriority()
	{
		try
		{
			var currentProcess = Process.GetCurrentProcess();
			currentProcess.PriorityClass = ProcessPriorityClass.High;
			CurrentPriority = ProcessPriorityClass.High;
			Log("[Performance] Process priority set to High.");
		}
		catch (Exception)
		{
			try
			{
				var currentProcess = Process.GetCurrentProcess();
				currentProcess.PriorityClass = ProcessPriorityClass.AboveNormal;
				CurrentPriority = ProcessPriorityClass.AboveNormal;
				Log("[Performance] Process priority set to AboveNormal.");
			}
			catch (Exception ex)
			{
				Log($"[Performance] Could not elevate process priority: {ex.Message}");
			}
		}
	}

	private static void ConfigureCpuAffinity()
	{
		try
		{
			int length = 0;
			GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref length);
			if (length <= 0)
			{
				return;
			}

			IntPtr buffer = Marshal.AllocHGlobal(length);
			try
			{
				if (!GetLogicalProcessorInformationEx(RelationProcessorCore, buffer, ref length))
				{
					return;
				}

				int offset = 0;
				byte minEff = byte.MaxValue;
				byte maxEff = 0;
				var effMasks = new Dictionary<byte, long>();

				while (offset < length)
				{
					int size = Marshal.ReadInt32(buffer, offset + 4);
					if (size <= 0) break;

					byte effClass = Marshal.ReadByte(buffer, offset + 9);
					long mask = Marshal.ReadInt64(buffer, offset + 32);

					if (effClass < minEff) minEff = effClass;
					if (effClass > maxEff) maxEff = effClass;

					if (!effMasks.ContainsKey(effClass)) effMasks[effClass] = 0;
					effMasks[effClass] |= mask;

					offset += size;
				}

				if (maxEff > minEff && effMasks.TryGetValue(maxEff, out long pCoreMask) && pCoreMask != 0)
				{
					IsHybridCpu = true;
					PerformanceCoreAffinityMask = pCoreMask;
					if (effMasks.TryGetValue(minEff, out long eCoreMask))
					{
						EfficiencyCoreAffinityMask = eCoreMask;
					}

					var process = Process.GetCurrentProcess();
					process.ProcessorAffinity = new IntPtr(pCoreMask);
					Log($"[Performance] Hybrid CPU detected ({Environment.ProcessorCount} logical threads). " +
					    $"P-Core Mask: 0x{pCoreMask:X}, E-Core Mask: 0x{EfficiencyCoreAffinityMask:X}. " +
					    $"Process affinity successfully pinned to Performance cores.");
				}
				else
				{
					IsHybridCpu = false;
					Log($"[Performance] Non-hybrid CPU detected ({Environment.ProcessorCount} logical threads). All cores treated equally.");
				}
			}
			finally
			{
				Marshal.FreeHGlobal(buffer);
			}
		}
		catch (Exception ex)
		{
			Log($"[Performance] Error configuring CPU affinity: {ex.Message}");
		}
	}

	private static void Log(string message)
	{
		try
		{
			GD.Print(message);
		}
		catch
		{
			Console.WriteLine(message);
		}
	}
}
