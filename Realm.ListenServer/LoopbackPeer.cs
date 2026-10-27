using System.Net.Sockets;
using Realm.Shared.Network;

namespace Realm.ListenServer;

public class LoopbackPeer : IClientTransport
{
	private readonly TcpClient _client;
	private readonly NetworkStream _stream;
	private readonly int _peerId;

	private readonly object _sendLock = new();

	private byte[]? _receiveBuffer;
	private bool _closed;

	public LoopbackPeer(TcpClient client, int peerId)
	{
		_peerId = peerId;
		_client = client;
		_client.NoDelay = true;
		_stream = client.GetStream();
	}

	public int PeerId => _peerId;

	public bool IsConnected => !_closed && _client.Connected;

	public void Send(NetworkFrameKind kind, ReadOnlySpan<byte> payload)
	{
		if (_closed) return;

		// A peer is written to by the thread reading its frames and by the tick thread describing the world, and two
		// frames interleaved into one stream would be one corrupt frame rather than two good ones.
		lock (_sendLock)
		{
			if (_closed) return;
			TransportFrameCodec.WriteFrame(_stream, kind, payload);
		}
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
