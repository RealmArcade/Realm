using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Realm.Ecs;
using Godot;
using MemoryPack;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using Realm.Shared.Network;
using FileAccess = System.IO.FileAccess;

namespace Realm.Client.ReplaySystem
{
	public class ReplayPlaybackManager
	{
		public ReplayHeader? Header { get; private set; }
		public bool IsPlayingReplay { get; set; } = false;
		public bool IsPlaying { get; set; } = false;
		public int CurrentTick { get; set; } = 0;
		public int TotalTicks { get; set; } = 0;
		public float PlaybackSpeed { get; set; } = 1.0f;
		public int SpectatorPerspective { get; set; } = -1;

		private ReplayFrame?[]? _frames;
		private float _timer = 0.0f;
		private const float TickInterval = 1.0f / 30.0f;

		public bool LoadReplay(string path)
		{
			try
			{
				if (!File.Exists(path)) return false;

				using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
				if (!ReadReplayHeaderInternal(fs)) return false;
				if (TotalTicks <= 0) return false;

				var frames = new ReplayFrame?[TotalTicks];
				int framesRead = ReadReplayFrames(fs, frames);
				_frames = frames;
				if (framesRead == 0) return false;

				TotalTicks = framesRead;
				CurrentTick = 0;
				_timer = 0.0f;
				PlaybackSpeed = 1.0f;
				IsPlaying = false;
				SpectatorPerspective = -1;
				IsPlayingReplay = true;

				return true;
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[ReplayPlaybackManager] Error loading replay: {ex.Message}");
				return false;
			}
		}

		private bool ReadReplayHeaderInternal(FileStream fs)
		{
			byte[] magicBytes = new byte[8];
			fs.ReadExactly(magicBytes, 0, 8);
			if (Encoding.ASCII.GetString(magicBytes) != "REALMREP") return false;

			byte[] lenBytes = new byte[4];
			fs.ReadExactly(lenBytes, 0, 4);
			int headerLen = BitConverter.ToInt32(lenBytes, 0);

			byte[] headerBytes = new byte[headerLen];
			fs.ReadExactly(headerBytes, 0, headerLen);
			Header = MemoryPackSerializer.Deserialize<ReplayHeader>(headerBytes);
			if (Header == null) return false;

			fs.ReadExactly(lenBytes, 0, 4);
			TotalTicks = BitConverter.ToInt32(lenBytes, 0);

			return true;
		}

		private int ReadReplayFrames(FileStream fs, ReplayFrame?[] frames)
		{
			using var deflate = new DeflateStream(fs, CompressionMode.Decompress, true);
			byte[] frameLenBytes = new byte[4];
			byte[] buffer = new byte[512 * 1024];

			int framesRead = 0;
			for (int i = 0; i < TotalTicks; i++)
			{
				int read = deflate.Read(frameLenBytes, 0, 4);
				if (read < 4) break;

				int frameLen = BitConverter.ToInt32(frameLenBytes, 0);
				if (frameLen <= 0) break;
				if (frameLen > buffer.Length)
				{
					buffer = new byte[frameLen * 2];
				}

				if (!ReadFrameData(deflate, buffer, frameLen)) break;

				var frame = MemoryPackSerializer.Deserialize<ReplayFrame>(buffer.AsSpan(0, frameLen));
				if (frame == null) break;

				frames[i] = frame;
				framesRead++;
			}

			return framesRead;
		}

		private bool ReadFrameData(DeflateStream deflate, byte[] buffer, int frameLen)
		{
			int offset = 0;
			while (offset < frameLen)
			{
				int chunk = deflate.Read(buffer, offset, frameLen - offset);
				if (chunk <= 0) return false;
				offset += chunk;
			}

			return true;
		}

		public void StopReplay()
		{
			IsPlayingReplay = false;
			IsPlaying = false;
			_frames = null;
			Header = null;
			TotalTicks = 0;
			CurrentTick = 0;
			_timer = 0.0f;
			SpectatorPerspective = -1;
		}

		public void ApplyInitialFrame()
		{
			if (_frames == null || TotalTicks == 0) return;
			GameContext.Host?.ResetStateForReplayPlayback();
			ApplyFrame(_frames[0]);
			CurrentTick = 0;
			_timer = 0.0f;
			IsPlaying = true;
		}

		public void Update(float fDelta)
		{
			if (!IsPlayingReplay || !IsPlaying || _frames == null) return;

			_timer += fDelta * PlaybackSpeed;
			while (_timer >= TickInterval)
			{
				_timer -= TickInterval;
				int nextTick = CurrentTick + 1;
				if (nextTick < TotalTicks)
				{
					CurrentTick = nextTick;
					ApplyFrame(_frames[CurrentTick]);
				}
				else
				{
					IsPlaying = false;
					break;
				}
			}
		}

		public void ScrubTo(int targetTick)
		{
			if (_frames == null || targetTick < 0 || targetTick >= TotalTicks) return;

			int keyframeTick = 0;
			for (int i = targetTick; i >= 0; i--)
			{
				if (_frames[i] is { IsKeyframe: true })
				{
					keyframeTick = i;
					break;
				}
			}

			GameContext.Host?.ResetStateForReplayPlayback();

			ApplyFrame(_frames[keyframeTick]);

			for (int t = keyframeTick + 1; t <= targetTick; t++)
			{
				if (_frames[t] != null)
				{
					ApplyFrame(_frames[t]);
				}
			}

			CurrentTick = targetTick;
			_timer = 0.0f;
		}

		public void ApplyFrame(ReplayFrame? frame)
		{
			var host = GameContext.Host;
			var world = GameContext.World;
			if (host == null || world == null || frame == null) return;

			ApplyResources(frame, host);
			ApplyProjectiles(frame, host);
			ApplyUnits(frame, world, host);
		}

		private static void ApplyResources(ReplayFrame frame, GameHost host)
		{
			if (GameContext.InGameHud != null)
			{
				GameContext.InGameHud.Gold = frame.Resources.Gold;
				GameContext.InGameHud.Wood = frame.Resources.Wood;
				GameContext.InGameHud.Stone = frame.Resources.Stone;
			}
			else
			{
				host.SetBackupResources(frame.Resources.Gold, frame.Resources.Wood, frame.Resources.Stone);
			}
		}

		private static void ApplyProjectiles(ReplayFrame frame, GameHost host)
		{
			if (frame.Projectiles == null) return;

			foreach (var proj in frame.Projectiles)
			{
				host.SpawnWeaponProjectileForReplay(proj.Start.ToNumerics(), proj.Target.ToNumerics(), proj.ProjectileTypeId);
			}
		}

		private static void ApplyUnits(ReplayFrame frame, EcsWorld world, GameHost host)
		{
			if (frame.Units == null) return;

			foreach (var snap in frame.Units)
			{
				ApplyUnitSnapshot(snap, world, host);
			}
		}

		private static void ApplyUnitSnapshot(ReplayUnitSnapshot snap, EcsWorld world, GameHost host)
		{
			if (host.TryGetLocalEntity(snap.EntityId, out var localEntity))
			{
				if (world.IsAlive(localEntity))
				{
					if (snap.IsDead)
					{
						HandleDeadUnit(localEntity, world, host);
					}
					else
					{
						UpdateUnitState(localEntity, snap, world);
					}
				}
			}
			else if (!snap.IsDead)
			{
				host.SpawnUnitFromReplaySnapshot(snap);
			}
		}

		private static void HandleDeadUnit(Entity localEntity, EcsWorld world, GameHost host)
		{
			if (!world.Has<Dead>(localEntity))
			{
				world.Add<Dead>(localEntity);
				GameHost.TryGetUnit3D(localEntity, out var unit3D);
				host.KillUnitDeferredExternal(unit3D);
			}
		}

		private static void UpdateUnitState(Entity localEntity, ReplayUnitSnapshot snap, EcsWorld world)
		{
			if (world.Has<Health>(localEntity))
			{
				var hp = world.Get<Health>(localEntity);
				hp.Current = snap.CurrentHp;
				hp.Max = snap.MaxHp;
				world.Set(localEntity, hp);
			}

			if (world.Has<Position>(localEntity))
			{
				world.Set(localEntity, new Position(snap.Position.ToNumerics()));
				world.SetOrAdd(localEntity, new PreviousPosition(snap.Position.ToNumerics()));
				world.SetOrAdd(localEntity, new PreviousRotationY(snap.RotationY));
			}

			if (world.Has<Velocity>(localEntity))
			{
				world.Set(localEntity, new Velocity(snap.Velocity.ToNumerics()));
			}

			if (world.Has<LatestDescription>(localEntity))
			{
				world.Set(localEntity, new LatestDescription
				{
					Position = snap.Position.ToNumerics(),
					RotationY = snap.RotationY
				});
			}

			world.SetOrAdd(localEntity, new ReplayAnimationState(snap.Animation ?? "Idle"));

			GameHost.TryGetUnit3D(localEntity, out var unit3DNode);
			if (GodotObject.IsInstanceValid(unit3DNode))
			{
				unit3DNode.GlobalPosition = new Vector3(snap.Position.X, snap.Position.Y, snap.Position.Z);
				unit3DNode.Velocity = new Vector3(snap.Velocity.X, snap.Velocity.Y, snap.Velocity.Z);
				unit3DNode.GlobalRotation = new Vector3(0, snap.RotationY, 0);
			}
		}

		public static ReplayHeader? ReadReplayHeader(string path, out int totalTicks)
		{
			totalTicks = 0;
			try
			{
				if (!File.Exists(path)) return null;
				using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
				byte[] magicBytes = new byte[8];
				fs.ReadExactly(magicBytes, 0, 8);
				if (Encoding.ASCII.GetString(magicBytes) != "REALMREP") return null;

				byte[] lenBytes = new byte[4];
				fs.ReadExactly(lenBytes, 0, 4);
				int headerLen = BitConverter.ToInt32(lenBytes, 0);

				byte[] headerBytes = new byte[headerLen];
				fs.ReadExactly(headerBytes, 0, headerLen);
				var header = MemoryPackSerializer.Deserialize<ReplayHeader>(headerBytes);

				fs.ReadExactly(lenBytes, 0, 4);
				totalTicks = BitConverter.ToInt32(lenBytes, 0);

				return header;
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[ReplayPlaybackManager] Error reading header from {path}: {ex}");
				return null;
			}
		}
	}
}