using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace Realm.Client.Core;

public class WasmDebugServer : IDisposable
{
	public const int DefaultPort = 12345;

	public int Port { get; }
	public bool IsListening { get; private set; }

	private TcpListener? _listener;
	private CancellationTokenSource? _cts;
	private Task? _listenTask;

	public WasmDebugServer(int? port = null)
	{
		Port = port ?? ResolveDebugPort();
	}

	public static int ResolveDebugPort()
	{
		string[] args = System.Environment.GetCommandLineArgs();
		for (int i = 0; i < args.Length - 1; i++)
		{
			if (args[i].Equals("--wasm-debug-port", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out int p))
			{
				return p;
			}
		}

		string? envPort = System.Environment.GetEnvironmentVariable("WASM_DEBUG_PORT");
		if (!string.IsNullOrEmpty(envPort) && int.TryParse(envPort, out int ep))
		{
			return ep;
		}

		return DefaultPort;
	}

	public void Start()
	{
		if (IsListening) return;

		try
		{
			_listener = new TcpListener(IPAddress.Loopback, Port);
			_listener.Start();
			IsListening = true;
			_cts = new CancellationTokenSource();

			GD.Print($"[WASM DEBUG SERVER] Debug server endpoint listening on 127.0.0.1:{Port}");
			_listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[WASM DEBUG SERVER] Failed to start debug server on port {Port}: {ex.Message}");
		}
	}

	private async Task ListenLoopAsync(CancellationToken token)
	{
		while (!token.IsCancellationRequested && _listener != null)
		{
			try
			{
				TcpClient client = await _listener.AcceptTcpClientAsync(token);
				GD.Print($"[WASM DEBUG SERVER] Debugger connected from {client.Client.RemoteEndPoint}");
				_ = Task.Run(() => HandleGdbRspClientAsync(client, token), token);
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (Exception ex)
			{
				if (!token.IsCancellationRequested)
				{
					GD.PrintErr($"[WASM DEBUG SERVER] Error accepting debugger connection: {ex.Message}");
				}
			}
		}
	}

	private async Task HandleGdbRspClientAsync(TcpClient client, CancellationToken token)
	{
		using (client)
		using (NetworkStream stream = client.GetStream())
		{
			byte[] buffer = new byte[4096];
			StringBuilder packetBuffer = new StringBuilder();

			while (!token.IsCancellationRequested && client.Connected)
			{
				try
				{
					int read = await stream.ReadAsync(buffer, 0, buffer.Length, token);
					if (read == 0) break;

					for (int i = 0; i < read; i++)
					{
						char c = (char)buffer[i];
						if (c == '+')
						{
							// ACK received from debugger
							continue;
						}
						if (c == '\x03')
						{
							// Interrupt signal (Ctrl-C) from debugger
							await SendRspResponseAsync(stream, "S05", token);
							continue;
						}
						if (c == '$')
						{
							packetBuffer.Clear();
							continue;
						}
						if (c == '#')
						{
							// Skip the 2 hex checksum bytes following '#'
							if (i + 2 < read) i += 2;

							string packetData = packetBuffer.ToString();
							packetBuffer.Clear();

							await ProcessRspPacketAsync(stream, packetData, token);
							continue;
						}

						packetBuffer.Append(c);
					}
				}
				catch
				{
					break;
				}
			}
		}
		GD.Print("[WASM DEBUG SERVER] Debugger client disconnected.");
	}

	private async Task ProcessRspPacketAsync(NetworkStream stream, string packetData, CancellationToken token)
	{
		if (packetData.StartsWith("qSupported"))
		{
			await SendRspResponseAsync(stream, "PacketSize=1000;qXfer:features:read-", token);
		}
		else if (packetData == "?")
		{
			await SendRspResponseAsync(stream, "S05", token); // SIGTRAP
		}
		else if (packetData.StartsWith("H") || packetData == "g" || packetData.StartsWith("p") || packetData.StartsWith("m"))
		{
			await SendRspResponseAsync(stream, "OK", token);
		}
		else if (packetData == "c" || packetData == "s")
		{
			await SendRspResponseAsync(stream, "S05", token);
		}
		else if (packetData == "k")
		{
			await SendRspResponseAsync(stream, "OK", token);
		}
		else
		{
			await SendRspResponseAsync(stream, "", token); // Unsupported packet
		}
	}

	private async Task SendRspResponseAsync(NetworkStream stream, string responseData, CancellationToken token)
	{
		byte checksum = 0;
		byte[] payload = Encoding.ASCII.GetBytes(responseData);
		foreach (byte b in payload)
		{
			checksum += b;
		}

		string rspFrame = $"+${responseData}#{checksum:x2}";
		byte[] rspBytes = Encoding.ASCII.GetBytes(rspFrame);

		try
		{
			await stream.WriteAsync(rspBytes, 0, rspBytes.Length, token);
			await stream.FlushAsync(token);
		}
		catch
		{
		}
	}

	public void Stop()
	{
		if (!IsListening) return;

		IsListening = false;
		_cts?.Cancel();

		try
		{
			_listener?.Stop();
		}
		catch
		{
		}

		_listener = null;
		_cts?.Dispose();
		_cts = null;
		GD.Print("[WASM DEBUG SERVER] Debug server stopped.");
	}

	public void Dispose()
	{
		Stop();
	}
}
