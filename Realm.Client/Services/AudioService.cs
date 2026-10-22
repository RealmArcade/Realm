using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Core;
using Realm.Client.UI;
using Realm.Shared.Audio;

namespace Realm.Client.Services;

public class AudioService
{
	public const string MasterBusName = "Master";
	public const string MusicBusName = "Music";
	public const string SfxBusName = "SFX";
	public const string VoiceBusName = "Voice";

	private const int Max2DPlayers = 16;
	private const int Max3DPlayers = 32;
	private const int MaxConcurrentPerSound = 4;

	private readonly Dictionary<string, AudioStream> _streamCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<AudioStreamPlayer> _player2DPool = new();
	private readonly List<AudioStreamPlayer3D> _player3DPool = new();
	private readonly Dictionary<AudioStreamPlayer3D, (string SoundKey, Vector3 Position)> _active3DPlayers = new();
	private readonly Random _random = new();

	private Node? _audioRoot;

	public AudioService()
	{
	}

	public static void ApplyVolumeSettings()
	{
		EnsureAudioBuses();
		ApplyBusVolume(MasterBusName, GameSettings.MasterVolume);
		ApplyBusVolume(MusicBusName, GameSettings.MusicVolume);
		ApplyBusVolume(SfxBusName, GameSettings.SfxVolume);
		ApplyBusVolume(VoiceBusName, GameSettings.VoiceVolume);
	}

	public static void EnsureAudioBuses()
	{
		EnsureBus(MusicBusName);
		EnsureBus(SfxBusName);
		EnsureBus(VoiceBusName);
	}

	private static void EnsureBus(string busName)
	{
		if (AudioServer.GetBusIndex(busName) >= 0) return;

		int busIndex = AudioServer.BusCount;
		AudioServer.AddBus(busIndex);
		AudioServer.SetBusName(busIndex, busName);
		AudioServer.SetBusSend(busIndex, MasterBusName);
	}

	private static void ApplyBusVolume(string busName, float volumePercent)
	{
		int busIndex = AudioServer.GetBusIndex(busName);
		if (busIndex < 0) return;

		float linearVolume = Math.Clamp(volumePercent / 100f, 0f, 1f);
		AudioServer.SetBusVolumeDb(busIndex, linearVolume <= 0f ? -80f : Mathf.LinearToDb(linearVolume));
	}

	public void InitializeAudioNodes(Node parentNode)
	{
		if (_audioRoot != null && GodotObject.IsInstanceValid(_audioRoot)) return;

		EnsureAudioBuses();

		_audioRoot = new Node { Name = "AudioRoot" };
		parentNode.AddChild(_audioRoot);

		_player2DPool.Clear();
		for (int i = 0; i < Max2DPlayers; i++)
		{
			var p = new AudioStreamPlayer { Name = $"Audio2D_{i}", Bus = SfxBusName };
			_audioRoot.AddChild(p);
			_player2DPool.Add(p);
		}

		_player3DPool.Clear();
		_active3DPlayers.Clear();
		for (int i = 0; i < Max3DPlayers; i++)
		{
			var p = new AudioStreamPlayer3D
			{
				Name = $"Audio3D_{i}",
				Bus = SfxBusName,
				MaxDistance = 60f,
				UnitSize = 10f,
				AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseSquareDistance
			};
			_audioRoot.AddChild(p);
			_player3DPool.Add(p);
		}
	}

	public AudioStream? LoadAudioStream(string soundPath)
	{
		if (string.IsNullOrWhiteSpace(soundPath)) return null;

		if (_streamCache.TryGetValue(soundPath, out var cachedStream))
		{
			return cachedStream;
		}

		AudioStream? stream = null;
		try
		{
			if (soundPath.StartsWith("res://") || soundPath.StartsWith("user://"))
			{
				stream = TryLoadFromResourcePath(soundPath);
			}
			else
			{
				string fullPath = ResolveExternalAudioPath(soundPath);
				if (File.Exists(fullPath))
				{
					stream = LoadStreamFromFile(fullPath);
				}
				else
				{
					stream = TryLoadFallbackResource(soundPath);
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AudioService] Error loading audio '{soundPath}': {ex.Message}");
		}

		if (stream != null)
		{
			_streamCache[soundPath] = stream;
		}

		return stream;
	}

	private AudioStream? TryLoadFromResourcePath(string soundPath)
	{
		if (ResourceLoader.Exists(soundPath))
		{
			return GD.Load<AudioStream>(soundPath);
		}

		return null;
	}

	private string ResolveExternalAudioPath(string soundPath)
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string fullPath = Path.Combine(wsPath, "Assets", "audio", soundPath);

		if (File.Exists(fullPath)) return fullPath;

		string candRaud = Path.ChangeExtension(fullPath, ".raud");
		if (File.Exists(candRaud)) return candRaud;

		fullPath = Path.Combine(wsPath, soundPath);
		if (File.Exists(fullPath)) return fullPath;

		candRaud = Path.ChangeExtension(fullPath, ".raud");
		if (File.Exists(candRaud)) return candRaud;

		fullPath = ProjectSettings.GlobalizePath($"res://Assets/Audio/UI/{soundPath}");
		if (File.Exists(fullPath)) return fullPath;

		return ProjectSettings.GlobalizePath($"res://Assets/Audio/{soundPath}");
	}

	private AudioStream? LoadStreamFromFile(string fullPath)
	{
		if (fullPath.EndsWith(".raud", StringComparison.OrdinalIgnoreCase))
		{
			return LoadRaudStream(fullPath);
		}

		if (fullPath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
		{
			return AudioStreamOggVorbis.LoadFromFile(fullPath);
		}

		return GD.Load<AudioStream>(fullPath);
	}

	private AudioStream? LoadRaudStream(string fullPath)
	{
		byte[] raudBytes = File.ReadAllBytes(fullPath);
		byte[]? oggBytes = RaudFile.GetTrack(raudBytes, 0);
		if (oggBytes != null && oggBytes.Length > 0)
		{
			return AudioStreamOggVorbis.LoadFromBuffer(oggBytes);
		}

		return null;
	}

	private AudioStream? TryLoadFallbackResource(string soundPath)
	{
		if (ResourceLoader.Exists($"res://Assets/Audio/UI/{soundPath}"))
		{
			return GD.Load<AudioStream>($"res://Assets/Audio/UI/{soundPath}");
		}

		if (ResourceLoader.Exists($"res://Assets/Audio/{soundPath}"))
		{
			return GD.Load<AudioStream>($"res://Assets/Audio/{soundPath}");
		}

		return null;
	}

	public void PlaySound2D(string soundPath, float volumeDb = 0f, float pitchVariation = 0.05f)
	{
		var stream = LoadAudioStream(soundPath);
		if (stream == null) return;

		EnsureAudioRoot();

		AudioStreamPlayer? player = null;
		for (int i = 0; i < _player2DPool.Count; i++)
		{
			if (!_player2DPool[i].Playing)
			{
				player = _player2DPool[i];
				break;
			}
		}

		if (player == null && _player2DPool.Count > 0)
		{
			player = _player2DPool[0];
		}

		if (player != null)
		{
			player.Stream = stream;
			player.VolumeDb = volumeDb;
			player.PitchScale = 1.0f + (pitchVariation > 0 ? (float)(_random.NextDouble() * 2 - 1) * pitchVariation : 0f);
			player.Play();
		}
	}

	public void PlaySound3D(string soundPath, Vector3 position, float volumeDb = 0f, float pitchVariation = 0.05f, float maxDistance = 60f)
	{
		var stream = LoadAudioStream(soundPath);
		if (stream == null) return;

		EnsureAudioRoot();

		var cameraPos = GameHost.Instance?.MainCamera?.GlobalPosition ?? position;
		CleanupAndScore3DPlayers(soundPath, cameraPos, out int activeCount, out var furthestPlayer, out float maxDistSq);

		AudioStreamPlayer3D? player = Get3DPlayer(activeCount, position, cameraPos, furthestPlayer, maxDistSq);
		if (player == null) return;

		player.Stream = stream;
		player.GlobalPosition = position;
		player.VolumeDb = volumeDb;
		player.MaxDistance = maxDistance;
		player.PitchScale = 1.0f + (pitchVariation > 0 ? (float)(_random.NextDouble() * 2 - 1) * pitchVariation : 0f);
		player.Play();
		_active3DPlayers[player] = (soundPath, position);
	}

	private void CleanupAndScore3DPlayers(string soundPath, Vector3 cameraPos, out int activeCount, out AudioStreamPlayer3D? furthestPlayer, out float maxDistSq)
	{
		List<AudioStreamPlayer3D>? stopped = null;
		activeCount = 0;
		furthestPlayer = null;
		maxDistSq = -1f;

		foreach (var (p, info) in _active3DPlayers)
		{
			if (!p.Playing)
			{
				stopped ??= new List<AudioStreamPlayer3D>();
				stopped.Add(p);
				continue;
			}

			if (string.Equals(info.SoundKey, soundPath, StringComparison.OrdinalIgnoreCase))
			{
				activeCount++;
				float distSq = info.Position.DistanceSquaredTo(cameraPos);
				if (distSq > maxDistSq)
				{
					maxDistSq = distSq;
					furthestPlayer = p;
				}
			}
		}

		if (stopped != null)
		{
			foreach (var s in stopped) _active3DPlayers.Remove(s);
		}
	}

	private AudioStreamPlayer3D? Get3DPlayer(int activeCount, Vector3 position, Vector3 cameraPos, AudioStreamPlayer3D? furthestPlayer, float maxDistSq)
	{
		if (activeCount >= MaxConcurrentPerSound)
		{
			float newDistSq = position.DistanceSquaredTo(cameraPos);
			if (furthestPlayer != null && newDistSq < maxDistSq)
			{
				return furthestPlayer;
			}

			return null;
		}

		for (int i = 0; i < _player3DPool.Count; i++)
		{
			if (!_player3DPool[i].Playing)
			{
				return _player3DPool[i];
			}
		}

		if (_player3DPool.Count > 0)
		{
			return _player3DPool[0];
		}

		return null;
	}

	public void PlayUnitSound(string unitId, UnitSoundEvent eventType, Vector3 position, float volumeDb = 0f)
	{
		if (string.IsNullOrEmpty(unitId)) return;
		if (GameHost.UnitRegistry != null && GameHost.UnitRegistry.TryGetValue(unitId, out var meta))
		{
			PlayUnitSound(meta, eventType, position, volumeDb);
		}
	}

	public void PlayUnitSound(UnitMetadata meta, UnitSoundEvent eventType, Vector3 position, float volumeDb = 0f)
	{
		if (meta.Sounds == null) return;

		string[]? pool = GetSoundPool(meta.Sounds, eventType);
		if (pool == null || pool.Length == 0) return;

		string clip = pool.Length == 1 ? pool[0] : pool[_random.Next(pool.Length)];
		if (!string.IsNullOrEmpty(clip))
		{
			PlaySound3D(clip, position, volumeDb, 0.08f);
		}
	}

	private string[]? GetSoundPool(UnitSoundsMetadata sounds, UnitSoundEvent eventType)
	{
		return eventType switch
		{
			UnitSoundEvent.Select => sounds.OnSelect,
			UnitSoundEvent.MoveOrder => sounds.OnMoveOrder,
			UnitSoundEvent.AttackOrder => sounds.OnAttackOrder,
			UnitSoundEvent.Wounded => sounds.OnWounded,
			UnitSoundEvent.Death => sounds.OnDeath,
			UnitSoundEvent.Ready => sounds.OnReady,
			UnitSoundEvent.SpellCast => sounds.OnSpellCast,
			_ => null
		};
	}

	public (bool Success, string Message, string FileName, string Blake3, string AudioCategory) ImportAudioAsset(string sourceFilePath, string audioType, string tempWorkspacePath)
	{
		try
		{
			string baseName = Path.GetFileNameWithoutExtension(sourceFilePath);
			string fileName = baseName + ".ogg";
			string targetPath = Path.Combine(tempWorkspacePath, fileName);

			if (sourceFilePath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
			{
				File.Copy(sourceFilePath, targetPath, true);
			}
			else
			{
				AudioConverter.ConvertToOgg(sourceFilePath, targetPath);
				if (!File.Exists(targetPath) || new FileInfo(targetPath).Length == 0)
				{
					File.Copy(sourceFilePath, targetPath, true);
				}
			}

			byte[] bytes = File.ReadAllBytes(targetPath);
			string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, ".ogg");
			string audioCategory = audioType.ToLowerInvariant() == "music" ? "music" : "sfx";

			return (true, string.Empty, fileName, blake3, audioCategory);
		}
		catch (Exception ex)
		{
			return (false, ex.Message, string.Empty, string.Empty, string.Empty);
		}
	}

	public void PlayWarningSound()
	{
		PlaySound2D("res://Assets/Audio/UI/alert_warning_buzz.ogg", 0f, 0f);
	}

	public void PlayClickSound()
	{
		PlaySound2D("res://Assets/Audio/UI/click_confirm_heavy.ogg", 0f, 0f);
	}

	public void PlayHoverSound()
	{
		PlaySound2D("res://Assets/Audio/UI/hover_highlight_sparkle.ogg", -4f, 0.05f);
	}

	public void PlayVictorySound()
	{
		PlaySound2D("res://Assets/Audio/UI/victory_theme_sting.ogg", 0f, 0f);
	}

	public void PlayDefeatSound()
	{
		PlaySound2D("res://Assets/Audio/UI/defeat_drone_low.ogg", 0f, 0f);
	}

	private void EnsureAudioRoot()
	{
		if (_audioRoot == null || !GodotObject.IsInstanceValid(_audioRoot))
		{
			if (GameHost.Instance != null && GodotObject.IsInstanceValid(GameHost.Instance))
			{
				InitializeAudioNodes(GameHost.Instance);
			}
			else if (Engine.GetMainLoop() is SceneTree tree && tree.Root != null)
			{
				InitializeAudioNodes(tree.Root);
			}
		}
	}
}