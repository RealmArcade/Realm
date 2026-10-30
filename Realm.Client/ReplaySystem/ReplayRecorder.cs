using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using Godot;
using MemoryPack;
using Realm.Client.Network;
using Realm.Shared;
using FileAccess = System.IO.FileAccess;

namespace Realm.Client.ReplaySystem
{
	public class ReplayRecorder
	{
		private readonly string _replayPath;
		private readonly string _mapName;
		private readonly ConcurrentQueue<ReplayFrame> _queue = new();
		private readonly Thread _thread;
		private readonly CancellationTokenSource _cts = new();
		private readonly AutoResetEvent _signal = new(false);
		private readonly List<ReplayPlayerInfo> _players = new();
		private int _tickCount = 0;

		private const int MaximumQueuedFrames = 300;
		private int _queuedFrames;
		private int _droppedFrameCount;

		public int DroppedFrameCount => Volatile.Read(ref _droppedFrameCount);

		public ReplayRecorder(string replayPath, string mapName, List<PlayerInfo> players)
		{
			_replayPath = replayPath;
			_mapName = mapName;

			if (players != null && players.Count > 0)
			{
				foreach (var p in players)
				{
					_players.Add(new ReplayPlayerInfo
					{
						PeerId = p.PeerId,
						AccountId = p.AccountId,
						Name = p.Name,
						Faction = p.Faction,
						ColorR = p.Color.R,
						ColorG = p.Color.G,
						ColorB = p.Color.B
					});
				}
			}
			else
			{
				_players.Add(new ReplayPlayerInfo
				{
					PeerId = 1,
					AccountId = Realm.Client.Services.GameContext.Account.AccountId,
					Name = Realm.Client.Services.GameContext.Account.IsSignedIn ? Realm.Client.Services.GameContext.Account.DisplayName : TranslationServer.Translate("Guest"),
					Faction = "HUMAN",
					ColorR = 0.1f,
					ColorG = 0.9f,
					ColorB = 0.2f
				});
				_players.Add(new ReplayPlayerInfo
				{
					PeerId = -1,
					Name = "Enemy_AI",
					Faction = "HUMAN",
					ColorR = 0.9f,
					ColorG = 0.1f,
					ColorB = 0.2f
				});
			}

			_thread = new Thread(WriteLoop)
			{
				IsBackground = true,
				Name = "ReplayRecorderWriteThread"
			};
		}

		public void Start()
		{
			_thread.Start();
		}

		public void Stop()
		{
			_cts.Cancel();
			_signal.Set();
			if (_thread.IsAlive)
			{
				_thread.Join(1000);
			}

			ReplayObjectPool.Trim();
		}

		public void RecordTick(int tick, List<ReplayUnitSnapshot> units, List<ReplayProjectileSnapshot> projectiles, float gold, float wood, float stone, bool isKeyframe)
		{
			var frame = ReplayObjectPool.RentFrame();
			frame.Tick = tick;
			frame.IsKeyframe = isKeyframe;
			frame.Resources = new ReplayPlayerResourceSnapshot
			{
				Gold = gold,
				Wood = wood,
				Stone = stone
			};
			frame.Units.AddRange(units);
			if (projectiles != null) frame.Projectiles.AddRange(projectiles);

			// A replay is a diagnostic artifact, so a writer that cannot keep up costs the oldest frames rather than
			// making the match wait or letting the queue grow for as long as the disk is slow.
			_queuedFrames = Interlocked.Increment(ref _queuedFrames);
			if (_queuedFrames > MaximumQueuedFrames)
			{
				if (_queue.TryDequeue(out ReplayFrame? dropped))
				{
					Interlocked.Decrement(ref _queuedFrames);
					Interlocked.Increment(ref _droppedFrameCount);
					ReplayObjectPool.ReturnFrame(dropped);
				}
				else
				{
					Interlocked.Decrement(ref _queuedFrames);
				}
			}

			_queue.Enqueue(frame);
			_signal.Set();

			// Read by the writer thread when it patches the frame count into the header, so it is published rather than
			// left for the two threads to settle between themselves.
			Volatile.Write(ref _tickCount, tick + 1);
		}

		private void WriteLoop()
		{
			try
			{
				var dir = Path.GetDirectoryName(_replayPath);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
				{
					Directory.CreateDirectory(dir);
				}

				using var fs = new FileStream(_replayPath, FileMode.Create, FileAccess.Write, FileShare.None);

				byte[] magicBytes = Encoding.ASCII.GetBytes("REALMREP");
				fs.Write(magicBytes, 0, 8);

				var header = new ReplayHeader
				{
					Magic = "REALMREP",
					Version = 1,
					MapName = _mapName,
					GameVersion = RealmVersion.GameBinaryVersion,
					Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
					Players = _players
				};
				byte[] headerBytes = MemoryPackSerializer.Serialize(header);
				fs.Write(BitConverter.GetBytes(headerBytes.Length), 0, 4);
				fs.Write(headerBytes, 0, headerBytes.Length);

				long ticksOffset = fs.Position;
				fs.Write(BitConverter.GetBytes(0), 0, 4);

				using (var deflate = new DeflateStream(fs, CompressionMode.Compress, true))
				{
					var bufferWriter = new ArrayBufferWriter<byte>(65536);
					while (!_cts.Token.IsCancellationRequested || !_queue.IsEmpty)
					{
						if (_queue.IsEmpty && !_cts.Token.IsCancellationRequested)
						{
							_signal.WaitOne(500);
						}

						while (_queue.TryDequeue(out var frame))
						{
							Interlocked.Decrement(ref _queuedFrames);
							bufferWriter.Clear();
							MemoryPackSerializer.Serialize(in bufferWriter, frame);

							ReadOnlySpan<byte> writtenSpan = bufferWriter.WrittenSpan;
							byte[] lenBytes = BitConverter.GetBytes(writtenSpan.Length);
							deflate.Write(lenBytes, 0, 4);
							deflate.Write(writtenSpan);

							ReplayObjectPool.ReturnFrame(frame);
						}
					}

					deflate.Flush();
				}

				fs.Seek(ticksOffset, SeekOrigin.Begin);
				fs.Write(BitConverter.GetBytes(Volatile.Read(ref _tickCount)), 0, 4);
			}
			catch (Exception ex)
			{
				ReportWriteError(ex.Message);
			}
		}

		private void ReportWriteError(string message)
		{
			try
			{
				File.AppendAllText(_replayPath + ".error.log", $"{DateTime.UtcNow:O} {message}{System.Environment.NewLine}");
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
			}
		}
	}
}