using Godot;
using MemoryPack;
using Realm.Shared;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

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

		public ReplayRecorder(string replayPath, string mapName, List<Network.LobbyManager.PlayerInfo> players)
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
					Name = "Horaid_Topa",
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

			_queue.Enqueue(frame);
			_signal.Set();
			_tickCount = tick + 1;
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

				using var fs = new FileStream(_replayPath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None);
				
				byte[] magicBytes = System.Text.Encoding.ASCII.GetBytes("REALMREP");
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
					var bufferWriter = new System.Buffers.ArrayBufferWriter<byte>(65536);
					while (!_cts.Token.IsCancellationRequested || !_queue.IsEmpty)
					{
						if (_queue.IsEmpty && !_cts.Token.IsCancellationRequested)
						{
							_signal.WaitOne(500);
						}

						while (_queue.TryDequeue(out var frame))
						{
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
				fs.Write(BitConverter.GetBytes(_tickCount), 0, 4);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ReplayRecorder] Error writing replay file: {ex.Message}");
			}
		}
	}
}
