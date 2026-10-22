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
	public const int MaxPositionalVoices = 16;
	private const int MaxConcurrentPerSound = 4;
	private const float DefaultPositionalMaxDistance = 60f;

	private static readonly string[] PositionalVoiceBusNames = CreatePositionalVoiceBusNames();

	private readonly Dictionary<string, AudioStream> _streamCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<AudioStreamPlayer> _player2DPool = new();
	private readonly AudioStreamPlayer[] _positionalPlayers = new AudioStreamPlayer[MaxPositionalVoices];
	private readonly AudioEffectPanner?[] _positionalPanners = new AudioEffectPanner?[MaxPositionalVoices];
	private readonly PositionalVoiceState[] _positionalVoices = new PositionalVoiceState[MaxPositionalVoices];
	private readonly Random _random = new();

	private AudioListener _listener = AudioListener.Default;
	private bool _hasConfiguredListener;
	private bool _followCameraListener = true;

	private Node? _audioRoot;

	private struct PositionalVoiceState
	{
		public bool Active;
		public string SoundKey;
		public Vector3 Position;
		public float BaseVolumeDb;
		public float MaxDistance;
		public float Priority;
	}

	private static string[] CreatePositionalVoiceBusNames()
	{
		var names = new string[MaxPositionalVoices];
		for (int index = 0; index < MaxPositionalVoices; index++)
		{
			names[index] = $"PositionalVoice_{index}";
		}

		return names;
	}

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

		InitializePositionalVoices();
	}

	private void InitializePositionalVoices()
	{
		for (int index = 0; index < MaxPositionalVoices; index++)
		{
			AudioServer.SetBusSend(GetOrCreatePositionalVoiceBus(index), SfxBusName);

			var player = new AudioStreamPlayer
			{
				Name = $"AudioPositional_{index}",
				Bus = PositionalVoiceBusNames[index]
			};
			_audioRoot!.AddChild(player);
			_positionalPlayers[index] = player;
			_positionalVoices[index] = default;
		}
	}

	private int GetOrCreatePositionalVoiceBus(int voiceIndex)
	{
		string busName = PositionalVoiceBusNames[voiceIndex];
		int busIndex = AudioServer.GetBusIndex(busName);
		if (busIndex < 0)
		{
			busIndex = AudioServer.BusCount;
			AudioServer.AddBus(busIndex);
			AudioServer.SetBusName(busIndex, busName);
		}

		AudioServer.SetBusSend(busIndex, SfxBusName);

		if (_positionalPanners[voiceIndex] == null || !GodotObject.IsInstanceValid(_positionalPanners[voiceIndex]))
		{
			AudioEffectPanner? existingPanner = AudioServer.GetBusEffectCount(busIndex) > 0
				? AudioServer.GetBusEffect(busIndex, 0) as AudioEffectPanner
				: null;

			if (existingPanner == null)
			{
				existingPanner = new AudioEffectPanner { Pan = 0f };
				AudioServer.AddBusEffect(busIndex, existingPanner, 0);
			}

			_positionalPanners[voiceIndex] = existingPanner;
		}

		return busIndex;
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

	public void SetAudioListener(Vector3 position, Vector3 forward, Vector3 up)
	{
		Vector3 normalizedForward = forward.LengthSquared() > 0.0001f ? forward.Normalized() : Vector3.Forward;
		Vector3 normalizedUp = up.LengthSquared() > 0.0001f ? up.Normalized() : Vector3.Up;

		_listener = new AudioListener(position, normalizedForward, normalizedUp);
		_hasConfiguredListener = true;
		_followCameraListener = false;
		UpdatePositionalVoices();
	}

	public void SetAudioListener(in AudioListener listener)
	{
		_listener = listener;
		_hasConfiguredListener = true;
		_followCameraListener = false;
		UpdatePositionalVoices();
	}

	public void EnableCameraListener()
	{
		_followCameraListener = true;
		_hasConfiguredListener = false;
	}

	public AudioListener GetAudioListener()
	{
		if (_hasConfiguredListener) return _listener;

		Node3D? camera = GameHost.Instance?.MainCamera;
		if (camera != null && GodotObject.IsInstanceValid(camera))
		{
			Basis cameraBasis = camera.GlobalTransform.Basis;
			return new AudioListener(camera.GlobalPosition, -cameraBasis.Z, cameraBasis.Y);
		}

		return AudioListener.Default;
	}

	public void UpdateListenerFromCamera(Node3D? camera)
	{
		if (!_followCameraListener) return;
		if (camera == null || !GodotObject.IsInstanceValid(camera)) return;

		Basis cameraBasis = camera.GlobalTransform.Basis;
		_listener = new AudioListener(camera.GlobalPosition, -cameraBasis.Z, cameraBasis.Y);
		_hasConfiguredListener = true;
		UpdatePositionalVoices();
	}

	public void UpdatePositionalVoices()
	{
		AudioListener listener = GetAudioListener();

		for (int index = 0; index < _positionalVoices.Length; index++)
		{
			ref PositionalVoiceState voice = ref _positionalVoices[index];
			AudioStreamPlayer? player = _positionalPlayers[index];
			if (!voice.Active || player == null || !GodotObject.IsInstanceValid(player)) continue;

			if (!player.Playing)
			{
				voice.Active = false;
				continue;
			}

			PositionalAudioMix mix = PositionalAudioMixer.Calculate(listener, voice.Position, voice.MaxDistance, voice.BaseVolumeDb);
			voice.Priority = mix.GainLinear;
			player.VolumeDb = mix.VolumeDb;

			AudioEffectPanner? panner = _positionalPanners[index];
			if (panner != null && GodotObject.IsInstanceValid(panner))
			{
				panner.Pan = mix.Pan;
			}
		}
	}

	public void PlaySound3D(string soundPath, Vector3 position, float volumeDb = 0f, float pitchVariation = 0.05f, float maxDistance = DefaultPositionalMaxDistance)
	{
		var stream = LoadAudioStream(soundPath);
		if (stream == null) return;

		EnsureAudioRoot();

		AudioListener listener = GetAudioListener();
		PositionalAudioMix mix = PositionalAudioMixer.Calculate(listener, position, maxDistance, volumeDb);
		if (mix.GainLinear <= 0f) return;

		int slotIndex = ReservePositionalVoice(soundPath, mix.GainLinear);
		if (slotIndex < 0) return;

		AudioStreamPlayer? player = _positionalPlayers[slotIndex];
		if (player == null || !GodotObject.IsInstanceValid(player)) return;

		_positionalVoices[slotIndex] = new PositionalVoiceState
		{
			Active = true,
			SoundKey = soundPath,
			Position = position,
			BaseVolumeDb = volumeDb,
			MaxDistance = maxDistance,
			Priority = mix.GainLinear
		};

		player.Stream = stream;
		player.VolumeDb = mix.VolumeDb;
		player.PitchScale = 1.0f + (pitchVariation > 0 ? (float)(_random.NextDouble() * 2 - 1) * pitchVariation : 0f);

		AudioEffectPanner? panner = _positionalPanners[slotIndex];
		if (panner != null && GodotObject.IsInstanceValid(panner))
		{
			panner.Pan = mix.Pan;
		}

		player.Play();
	}

	private int ReservePositionalVoice(string soundPath, float newPriority)
	{
		int freeSlot = -1;
		int quietestSlot = -1;
		float quietestPriority = float.MaxValue;
		int sameSoundCount = 0;
		int quietestSameSoundSlot = -1;
		float quietestSameSoundPriority = float.MaxValue;

		for (int index = 0; index < _positionalVoices.Length; index++)
		{
			ref PositionalVoiceState voice = ref _positionalVoices[index];
			AudioStreamPlayer? player = _positionalPlayers[index];

			if (!voice.Active || player == null || !GodotObject.IsInstanceValid(player) || !player.Playing)
			{
				if (freeSlot < 0) freeSlot = index;
				continue;
			}

			if (voice.Priority < quietestPriority)
			{
				quietestPriority = voice.Priority;
				quietestSlot = index;
			}

			if (string.Equals(voice.SoundKey, soundPath, StringComparison.OrdinalIgnoreCase))
			{
				sameSoundCount++;
				if (voice.Priority < quietestSameSoundPriority)
				{
					quietestSameSoundPriority = voice.Priority;
					quietestSameSoundSlot = index;
				}
			}
		}

		if (sameSoundCount >= MaxConcurrentPerSound)
		{
			return newPriority > quietestSameSoundPriority ? quietestSameSoundSlot : -1;
		}

		if (freeSlot >= 0) return freeSlot;

		return newPriority > quietestPriority ? quietestSlot : -1;
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