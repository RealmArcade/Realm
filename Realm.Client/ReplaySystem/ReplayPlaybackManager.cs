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
		public ReplayHeader Header { get; private set; }
		public bool IsPlayingReplay { get; set; } = false;
		public bool IsPlaying { get; set; } = false;
		public int CurrentTick { get; set; } = 0;
		public int TotalTicks { get; set; } = 0;
		public float PlaybackSpeed { get; set; } = 1.0f;
		public int SpectatorPerspective { get; set; } = -1;

		private ReplayFrame[] _frames;
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

				_frames = new ReplayFrame[TotalTicks];
				int framesRead = ReadReplayFrames(fs);
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
				GD.PrintErr($"[ReplayPlaybackManager] Error loading replay: {ex.Message}");
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

			fs.ReadExactly(lenBytes, 0, 4);
			TotalTicks = BitConverter.ToInt32(lenBytes, 0);

			return true;
		}

		private int ReadReplayFrames(FileStream fs)
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

				_frames[i] = MemoryPackSerializer.Deserialize<ReplayFrame>(buffer.AsSpan(0, frameLen));
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
			Header = default;
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
				if (_frames[i] != null && _frames[i].IsKeyframe)
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

		public void ApplyFrame(ReplayFrame frame)
		{
			if (GameContext.Host == null || frame == null) return;

			ApplyResources(frame);
			ApplyProjectiles(frame);
			ApplyUnits(frame);
		}

		private void ApplyResources(ReplayFrame frame)
		{
			if (GameContext.InGameHud != null)
			{
				GameContext.InGameHud.Gold = frame.Resources.Gold;
				GameContext.InGameHud.Wood = frame.Resources.Wood;
				GameContext.InGameHud.Stone = frame.Resources.Stone;
			}
			else
			{
				GameContext.Host.SetBackupResources(frame.Resources.Gold, frame.Resources.Wood, frame.Resources.Stone);
			}
		}

		private void ApplyProjectiles(ReplayFrame frame)
		{
			if (frame.Projectiles == null) return;

			foreach (var proj in frame.Projectiles)
			{
				GameContext.Host.SpawnWeaponProjectileForReplay(proj.Start.ToNumerics(), proj.Target.ToNumerics(), proj.ProjectileTypeId);
			}
		}

		private void ApplyUnits(ReplayFrame frame)
		{
			if (frame.Units == null) return;

			foreach (var snap in frame.Units)
			{
				ApplyUnitSnapshot(snap);
			}
		}

		private void ApplyUnitSnapshot(ReplayUnitSnapshot snap)
		{
			if (GameContext.Host.TryGetLocalEntity(snap.EntityId, out var localEntity))
			{
				if (GameContext.World.IsAlive(localEntity))
				{
					if (snap.IsDead)
					{
						HandleDeadUnit(localEntity);
					}
					else
					{
						UpdateUnitState(localEntity, snap);
					}
				}
			}
			else if (!snap.IsDead)
			{
				GameContext.Host.SpawnUnitFromReplaySnapshot(snap);
			}
		}

		private void HandleDeadUnit(Entity localEntity)
		{
			if (!GameContext.World.Has<Dead>(localEntity))
			{
				GameContext.World.Add<Dead>(localEntity);
				GameHost.TryGetUnit3D(localEntity, out var unit3D);
				GameContext.Host.KillUnitDeferredExternal(unit3D);
			}
		}

		private void UpdateUnitState(Entity localEntity, ReplayUnitSnapshot snap)
		{
			if (GameContext.World.Has<Health>(localEntity))
			{
				var hp = GameContext.World.Get<Health>(localEntity);
				hp.Current = snap.CurrentHp;
				hp.Max = snap.MaxHp;
				GameContext.World.Set(localEntity, hp);
			}

			if (GameContext.World.Has<Position>(localEntity))
			{
				GameContext.World.Set(localEntity, new Position(snap.Position.ToNumerics()));
				GameContext.World.SetOrAdd(localEntity, new PreviousPosition(snap.Position.ToNumerics()));
				GameContext.World.SetOrAdd(localEntity, new PreviousRotationY(snap.RotationY));
			}

			if (GameContext.World.Has<Velocity>(localEntity))
			{
				GameContext.World.Set(localEntity, new Velocity(snap.Velocity.ToNumerics()));
			}

			if (GameContext.World.Has<LatestDescription>(localEntity))
			{
				GameContext.World.Set(localEntity, new LatestDescription
				{
					Position = snap.Position.ToNumerics(),
					RotationY = snap.RotationY
				});
			}

			GameContext.World.SetOrAdd(localEntity, new ReplayAnimationState(snap.Animation ?? "Idle"));

			GameHost.TryGetUnit3D(localEntity, out var unit3DNode);
			if (GodotObject.IsInstanceValid(unit3DNode))
			{
				unit3DNode.GlobalPosition = new Vector3(snap.Position.X, snap.Position.Y, snap.Position.Z);
				unit3DNode.Velocity = new Vector3(snap.Velocity.X, snap.Velocity.Y, snap.Velocity.Z);
				unit3DNode.GlobalRotation = new Vector3(0, snap.RotationY, 0);
			}
		}

		public static ReplayHeader ReadReplayHeader(string path, out int totalTicks)
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
				GD.PrintErr($"[ReplayPlaybackManager] Error reading header from {path}: {ex}");
				return null;
			}
		}
	}
}