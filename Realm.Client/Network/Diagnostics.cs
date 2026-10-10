using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Realm.Client.Network;

public class Diagnostics
{
	private const int MagicHeader = 0x44494147; // "DIAG"
	private const int PacketCount = 50;
	private const int DelayBetweenPacketsMs = 100; // 50 * 100ms = 5 seconds

	public class DiagnosticResult
	{
		public float MinRtt { get; set; }
		public float MaxRtt { get; set; }
		public float AvgRtt { get; set; }
		public float Jitter { get; set; }
		public float LossPercentage { get; set; }
		public int MaxConsecutiveLoss { get; set; }
	}

	private static CancellationTokenSource? _serverCts;

	public static void StartHostListener(int diagPort)
	{
		StopHostListener();
		_serverCts = new CancellationTokenSource();
		var token = _serverCts.Token;

		Task.Run(async () => await RunHostListenerAsync(diagPort, token), token);
	}
    
	private static async Task RunHostListenerAsync(int diagPort, CancellationToken token)
	{
		using var udp = new UdpClient();
		udp.ExclusiveAddressUse = false;
		udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        
		try
		{
			udp.Client.Bind(new IPEndPoint(IPAddress.Any, diagPort));
			GD.Print($"[Diagnostics] Host listening on UDP port {diagPort}");

			while (!token.IsCancellationRequested)
			{
				var receiveResult = await udp.ReceiveAsync(token);
				await ProcessHostPacketAsync(udp, receiveResult);
			}
		}
		catch (OperationCanceledException) { /* Clean shutdown */ }
		catch (Exception ex)
		{
			GD.PrintErr($"[Diagnostics] Host listener exception: {ex.Message}");
		}
	}

	private static async Task ProcessHostPacketAsync(UdpClient udp, UdpReceiveResult receiveResult)
	{
		var data = receiveResult.Buffer;
		if (data.Length < 16) return;
        
		int magic = BitConverter.ToInt32(data, 0);
		if (magic != MagicHeader) return;
        
		await udp.SendAsync(data, data.Length, receiveResult.RemoteEndPoint);
	}

	public static void StopHostListener()
	{
		_serverCts?.Cancel();
		_serverCts = null;
	}

	public static async Task<DiagnosticResult> RunClientDiagnosticsAsync(string hostIp, int diagPort, Action<DiagnosticResult> onProgressUpdate)
	{
		GD.Print($"[Diagnostics] Starting diagnostic test to {hostIp}:{diagPort}...");
        
		using var udp = new UdpClient();
		udp.ExclusiveAddressUse = false;
		udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
		udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0)); // bind to any local port

		var remoteEp = new IPEndPoint(IPAddress.Parse(hostIp), diagPort);
        
		var receivedRtts = new List<float>();
		var received = new bool[PacketCount];

		var stopwatch = Stopwatch.StartNew();

		using var cts = new CancellationTokenSource();
        
		var receiveTask = Task.Run(() => ReceiveDiagnosticPacketsAsync(udp, stopwatch, received, receivedRtts, onProgressUpdate, cts.Token));

		await SendDiagnosticPacketsAsync(udp, remoteEp, stopwatch);

		await Task.Delay(500);
		cts.Cancel();
		try { await receiveTask; } catch { /* Ignore task cancellation error */ }

		stopwatch.Stop();
        
		var finalResult = ComputeMetrics(received, receivedRtts);
		GD.Print($"[Diagnostics] Final results - Loss: {finalResult.LossPercentage}%, Avg RTT: {finalResult.AvgRtt}ms, Jitter: {finalResult.Jitter}ms");
		return finalResult;
	}
    
	private static async Task ReceiveDiagnosticPacketsAsync(UdpClient udp, Stopwatch stopwatch, bool[] received, List<float> receivedRtts, Action<DiagnosticResult> onProgressUpdate, CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			try
			{
				var receiveResult = await udp.ReceiveAsync(token);
				ProcessDiagnosticPacket(receiveResult.Buffer, stopwatch, received, receivedRtts, onProgressUpdate);
			}
			catch (OperationCanceledException) { break; }
			catch (Exception ex)
			{
				GD.PrintErr($"[Diagnostics] Client receive error: {ex.Message}");
				break;
			}
		}
	}

	private static void ProcessDiagnosticPacket(byte[] data, Stopwatch stopwatch, bool[] received, List<float> receivedRtts, Action<DiagnosticResult> onProgressUpdate)
	{
		if (data.Length < 16) return;

		int magic = BitConverter.ToInt32(data, 0);
		int seqNum = BitConverter.ToInt32(data, 4);
		long clientTime = BitConverter.ToInt64(data, 8);

		if (magic != MagicHeader || seqNum < 0 || seqNum >= PacketCount) return;

		long now = stopwatch.ElapsedMilliseconds;
		float rtt = now - clientTime;
        
		lock (receivedRtts)
		{
			if (received[seqNum]) return;
            
			received[seqNum] = true;
			receivedRtts.Add(rtt);
		}

		var currentResult = ComputeMetrics(received, receivedRtts);
		onProgressUpdate?.Invoke(currentResult);
	}
    
	private static async Task SendDiagnosticPacketsAsync(UdpClient udp, IPEndPoint remoteEp, Stopwatch stopwatch)
	{
		for (int i = 0; i < PacketCount; i++)
		{
			byte[] packet = new byte[16];
			long sendTime = stopwatch.ElapsedMilliseconds;

			Array.Copy(BitConverter.GetBytes(MagicHeader), 0, packet, 0, 4);
			Array.Copy(BitConverter.GetBytes(i), 0, packet, 4, 4);
			Array.Copy(BitConverter.GetBytes(sendTime), 0, packet, 8, 8);

			try
			{
				await udp.SendAsync(packet, packet.Length, remoteEp);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[Diagnostics] Client send error: {ex.Message}");
			}

			await Task.Delay(DelayBetweenPacketsMs);
		}
	}

	private static DiagnosticResult ComputeMetrics(bool[] received, List<float> receivedRtts)
	{
		var result = new DiagnosticResult();
		int receivedCount = 0;

		lock (receivedRtts)
		{
			receivedCount = receivedRtts.Count;
			ComputeRttMetrics(result, receivedRtts, receivedCount);
		}

		result.LossPercentage = (float)(PacketCount - receivedCount) / PacketCount * 100.0f;
		result.MaxConsecutiveLoss = ComputeMaxConsecutiveLoss(received);

		return result;
	}

	private static void ComputeRttMetrics(DiagnosticResult result, List<float> receivedRtts, int receivedCount)
	{
		if (receivedCount <= 0)
		{
			result.MinRtt = 0;
			result.MaxRtt = 0;
			result.AvgRtt = 0;
			result.Jitter = 0;
			return;
		}

		float min = float.MaxValue;
		float max = float.MinValue;
		float sum = 0f;

		foreach (var rtt in receivedRtts)
		{
			if (rtt < min) min = rtt;
			if (rtt > max) max = rtt;
			sum += rtt;
		}

		result.MinRtt = min;
		result.MaxRtt = max;
		result.AvgRtt = sum / receivedCount;
        
		result.Jitter = ComputeJitter(receivedRtts);
	}
    
	private static float ComputeJitter(List<float> receivedRtts)
	{
		if (receivedRtts.Count <= 1) return 0;

		float jitterSum = 0f;
		for (int i = 1; i < receivedRtts.Count; i++)
		{
			jitterSum += Math.Abs(receivedRtts[i] - receivedRtts[i - 1]);
		}
		return jitterSum / (receivedRtts.Count - 1);
	}

	private static int ComputeMaxConsecutiveLoss(bool[] received)
	{
		int maxConsecutive = 0;
		int currentConsecutive = 0;
        
		for (int i = 0; i < PacketCount; i++)
		{
			if (!received[i])
			{
				currentConsecutive++;
				if (currentConsecutive > maxConsecutive)
				{
					maxConsecutive = currentConsecutive;
				}
			}
			else
			{
				currentConsecutive = 0;
			}
		}
        
		return maxConsecutive;
	}
}