using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Realm.Shared.Network;

/// <summary>
///     A peer's end of the loopback frame transport. It speaks exactly what the listener speaks - a length prefix, a
///     frame kind, then the payload - so a match the client started on its own machine and a match it reached over the
///     network cross the same code.
/// </summary>
public class LoopbackClientTransport : IClientTransport
{
	private readonly TcpClient _client;
	private readonly NetworkStream _stream;

	private byte[]? _receiveBuffer;
	private bool _closed;

	private LoopbackClientTransport(TcpClient client)
	{
		_client = client;
		_client.NoDelay = true;
		_stream = client.GetStream();
	}

	public int PeerId { get; private set; }

	public bool IsConnected => !_closed && _client.Connected;

	/// <summary>
	///     Records the peer identity the authority assigned this connection. Until the roster frame arrives the client
	///     does not know which peer it is, and nothing it sends depends on the answer.
	/// </summary>
	public void SeatPeer(int peerId) => PeerId = peerId;

	/// <summary>
	///     Reaches a listener on the given loopback port. A refusal reports why it failed rather than throwing, because
	///     the caller is starting a match and has to report the failure to the player.
	/// </summary>
	public static bool TryConnect(string host, int port, int timeoutMilliseconds, out LoopbackClientTransport? transport, out string failure)
	{
		transport = null;
		failure = string.Empty;

		if (port <= 0 || port > IPEndPoint.MaxPort)
		{
			failure = $"port {port} is outside the range a listener can bind";
			return false;
		}

		var client = new TcpClient();
		try
		{
			Task connectTask = client.ConnectAsync(host, port);
			if (!connectTask.Wait(timeoutMilliseconds))
			{
				client.Dispose();
				failure = $"connecting to {host}:{port} took longer than {timeoutMilliseconds}ms";
				return false;
			}

			transport = new LoopbackClientTransport(client);
			return true;
		}
		catch (IOException exception)
		{
			client.Dispose();
			failure = exception.InnerException?.Message ?? exception.Message;
			return false;
		}
		catch (SocketException exception)
		{
			client.Dispose();
			failure = exception.Message;
			return false;
		}
		catch (AggregateException exception)
		{
			client.Dispose();
			failure = exception.InnerException?.Message ?? exception.Message;
			return false;
		}
	}

	public void Send(NetworkFrameKind kind, ReadOnlySpan<byte> payload)
	{
		if (_closed) return;
		TransportFrameCodec.WriteFrame(_stream, kind, payload);
	}

	public bool TryReceive(int timeoutMilliseconds, out NetworkFrame frame)
	{
		if (_closed) return Fail(out frame);

		try
		{
			_stream.ReadTimeout = timeoutMilliseconds;
			return TransportFrameCodec.TryReadFrame(_stream, ref _receiveBuffer, out frame);
		}
		catch (ObjectDisposedException)
		{
			return Fail(out frame);
		}
		catch (IOException)
		{
			return Fail(out frame);
		}
	}

	public void Close()
	{
		if (_closed) return;
		_closed = true;
		_receiveBuffer = null;

		try
		{
			_stream.Dispose();
		}
		catch (IOException)
		{
		}
		catch (ObjectDisposedException)
		{
		}

		_client.Dispose();
	}

	public void Dispose() => Close();

	private static bool Fail(out NetworkFrame frame)
	{
		frame = default;
		return false;
	}
}
