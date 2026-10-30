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
	public const char RaudEntrySeparator = '#';

	private const int Max2DPlayers = 16;
	public const int MaxPositionalVoices = 32;
	public const int CriticalPositionalVoices = 8;
	private const int MaxConcurrentPerSound = 4;
	private const float EvictionMargin = 0.5f;
	private const ulong VoiceGracePeriodMs = 300;
	private const float VolumeWriteThresholdDb = 0.05f;

	private readonly Dictionary<string, AudioStream> _streamCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<AudioStreamPlayer> _player2DPool = new();
	private readonly AudioStreamPlayer3D?[] _positionalPlayers = new AudioStreamPlayer3D?[MaxPositionalVoices];
	private readonly PositionalVoiceState[] _positionalVoices = new PositionalVoiceState[MaxPositionalVoices];
	private readonly Random _random = new();

	private AudioListener _listener = AudioListener.Default;
	private AudioListener3D? _listenerNode;
	private Transform3D _appliedListenerTransform = Transform3D.Identity;
	private bool _hasAppliedListenerTransform;
	private bool _followCameraListener = true;
	private bool _listenerCurrentWarned;

	private Node? _audioRoot;

	private struct PositionalVoiceState
	{
		public bool Active;
		public string SoundKey;
		public Vector3 Position;
		public float BaseVolumeDb;
		public float MaxDistance;
		public float Priority;
		public float AppliedVolumeDb;
		public ulong StartedTicksMs;
	}

	public static void ApplyVolumeSettings()
	{
		EnsureAudioBuses();
		ApplyBusVolume(MasterBusName, GameSettings.MasterVolume);
		ApplyBusVolume(MusicBusName, GameSettings.MusicVolume);
		ApplyBusVolume(SfxBusName, GameSettings.SfxVolume);
		ApplyBusVolume(VoiceBusName, GameSettings.VoiceVolume);
		GameContext.AudioService.ApplyPositionalProfile();
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

	private void InitializeAudioNodes()
	{
		if (_audioRoot != null && GodotObject.IsInstanceValid(_audioRoot)) return;

		EnsureAudioBuses();

		_audioRoot = new Node { Name = "AudioRoot" };

		_player2DPool.Clear();
		for (int i = 0; i < Max2DPlayers; i++)
		{
			var p = new AudioStreamPlayer { Name = $"Audio2D_{i}", Bus = SfxBusName };
			_audioRoot.AddChild(p);
			_player2DPool.Add(p);
		}

		InitializePositionalVoices();
		InitializeListenerNode();
	}

	private void InitializePositionalVoices()
	{
		for (int index = 0; index < MaxPositionalVoices; index++)
		{
			var player = new AudioStreamPlayer3D
			{
				Name = $"AudioPositional_{index}",
				Bus = SfxBusName,
				MaxDistance = PositionalAudioProfile.DefaultMaxDistance
			};
			ApplyVoiceProfile(player);
			_audioRoot!.AddChild(player);
			_positionalPlayers[index] = player;
			_positionalVoices[index] = default;
		}
	}

	private void InitializeListenerNode()
	{
		if (_listenerNode != null && GodotObject.IsInstanceValid(_listenerNode)) return;

		_listenerNode = new AudioListener3D { Name = "PositionalAudioListener", TopLevel = true };
		_audioRoot!.AddChild(_listenerNode);
		_listenerNode.GlobalTransform = new Transform3D(Basis.Identity, _listener.Position);
		ApplyListenerDopplerTracking();
		MakeListenerCurrent();
	}

	private void ApplyListenerDopplerTracking()
	{
		if (_listenerNode == null || !GodotObject.IsInstanceValid(_listenerNode)) return;

		_listenerNode.DopplerTracking = GameSettings.PositionalDoppler
			? AudioListener3D.DopplerTrackingEnum.IdleStep
			: AudioListener3D.DopplerTrackingEnum.Disabled;
	}

	private static void ApplyVoiceProfile(AudioStreamPlayer3D player)
	{
		player.AttenuationModel = PositionalAudioProfile.AttenuationModel;
		player.UnitSize = PositionalAudioProfile.UnitSize;
		player.MaxDb = PositionalAudioProfile.MaxVolumeDb;
		player.AttenuationFilterCutoffHz = GameSettings.PositionalAttenuationFilter
			? PositionalAudioProfile.AttenuationFilterCutoffHz
			: PositionalAudioProfile.DisabledAttenuationFilterCutoffHz;
		player.AttenuationFilterDb = PositionalAudioProfile.AttenuationFilterDb;
		player.DopplerTracking = GameSettings.PositionalDoppler
			? AudioStreamPlayer3D.DopplerTrackingEnum.IdleStep
			: AudioStreamPlayer3D.DopplerTrackingEnum.Disabled;
	}

	public void ApplyPositionalProfile()
	{
		if (_audioRoot == null || !GodotObject.IsInstanceValid(_audioRoot)) return;

		for (int index = 0; index < _positionalPlayers.Length; index++)
		{
			AudioStreamPlayer3D? player = _positionalPlayers[index];
			if (player == null || !GodotObject.IsInstanceValid(player)) continue;

			ApplyVoiceProfile(player);
		}

		ApplyListenerDopplerTracking();

		_listenerCurrentWarned = false;
		MakeListenerCurrent();
	}

	private void MakeListenerCurrent()
	{
		if (_listenerNode == null || !GodotObject.IsInstanceValid(_listenerNode) || !_listenerNode.IsInsideTree()) return;

		_listenerNode.MakeCurrent();
		if (_listenerNode.IsCurrent()) return;

		if (_listenerCurrentWarned) return;

		GD.PushError("[AudioService] AudioListener3D could not be made current; the engine is falling back to the camera listener.");
		_listenerCurrentWarned = true;
	}

	public static string BuildRaudEntryKey(string assetPath, string entryName)
	{
		return string.IsNullOrWhiteSpace(entryName) ? assetPath : $"{assetPath}{RaudEntrySeparator}{entryName}";
	}

	public AudioStream? LoadAudioStream(string soundPath)
	{
		if (string.IsNullOrWhiteSpace(soundPath)) return null;

		if (_streamCache.TryGetValue(soundPath, out var cachedStream))
		{
			return cachedStream;
		}

		SplitRaudEntryKey(soundPath, out string assetPath, out string? entryName);

		AudioStream? stream = null;
		try
		{
			if (assetPath.StartsWith("res://") || assetPath.StartsWith("user://"))
			{
				stream = TryLoadFromResourcePath(assetPath);
			}
			else
			{
				string fullPath = ResolveExternalAudioPath(assetPath);
				if (File.Exists(fullPath))
				{
					stream = LoadStreamFromFile(fullPath, entryName);
				}
				else
				{
					stream = TryLoadFallbackResource(assetPath);
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

	private static void SplitRaudEntryKey(string soundPath, out string assetPath, out string? entryName)
	{
		assetPath = soundPath;
		entryName = null;

		int separatorIndex = soundPath.IndexOf(RaudEntrySeparator, StringComparison.Ordinal);
		if (separatorIndex < 0) return;

		assetPath = soundPath[..separatorIndex];
		entryName = soundPath[(separatorIndex + 1)..];
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

	private AudioStream? LoadStreamFromFile(string fullPath, string? entryName = null)
	{
		if (fullPath.EndsWith(".raud", StringComparison.OrdinalIgnoreCase))
		{
			return LoadRaudStream(fullPath, entryName);
		}

		if (fullPath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
		{
			return AudioStreamOggVorbis.LoadFromFile(fullPath);
		}

		return GD.Load<AudioStream>(fullPath);
	}

	private AudioStream? LoadRaudStream(string fullPath, string? entryName = null)
	{
		byte[] raudBytes = File.ReadAllBytes(fullPath);
		byte[]? oggBytes = RaudFile.GetEntry(raudBytes, entryName);
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

	public void SetAudioListener(in AudioListener listener)
	{
		Vector3 normalizedForward = listener.Forward.LengthSquared() > 0.0001f ? listener.Forward.Normalized() : Vector3.Forward;
		Vector3 normalizedUp = listener.Up.LengthSquared() > 0.0001f ? listener.Up.Normalized() : Vector3.Up;

		_followCameraListener = false;
		ApplyListenerTransform(new Transform3D(CreateListenerBasis(normalizedForward, normalizedUp), listener.Position));
		UpdatePositionalVoices();
	}

	private static Basis CreateListenerBasis(Vector3 forward, Vector3 up)
	{
		Vector3 right = forward.Cross(up);
		if (right.LengthSquared() <= 0.0001f) return Basis.Identity;

		right = right.Normalized();
		Vector3 correctedUp = right.Cross(forward).Normalized();

		return new Basis(right, correctedUp, -forward);
	}

	public AudioListener GetAudioListener()
	{
		return _listener;
	}

	public void UpdateListenerFromCamera(Transform3D cameraTransform)
	{
		if (!_followCameraListener) return;

		ApplyListenerTransform(cameraTransform);
		UpdatePositionalVoices();
	}

	private void ApplyListenerTransform(Transform3D transform)
	{
		if (_hasAppliedListenerTransform && transform == _appliedListenerTransform) return;

		_appliedListenerTransform = transform;
		_hasAppliedListenerTransform = true;

		Basis basis = transform.Basis;
		Vector3 forward = -basis.Z;
		Vector3 up = basis.Y;

		_listener = new AudioListener(
			transform.Origin,
			forward.LengthSquared() > 0.0001f ? forward.Normalized() : Vector3.Forward,
			up.LengthSquared() > 0.0001f ? up.Normalized() : Vector3.Up);

		if (_listenerNode != null && GodotObject.IsInstanceValid(_listenerNode))
		{
			_listenerNode.GlobalTransform = new Transform3D(CreateListenerBasis(_listener.Forward, _listener.Up), transform.Origin);
			if (!_listenerNode.IsCurrent()) MakeListenerCurrent();
		}
	}

	public bool IsListenerCurrent
	{
		get => _listenerNode != null && GodotObject.IsInstanceValid(_listenerNode) && _listenerNode.IsCurrent();
	}

	public void UpdatePositionalVoices()
	{
		AudioListener listener = _listener;

		for (int index = 0; index < _positionalVoices.Length; index++)
		{
			ref PositionalVoiceState voice = ref _positionalVoices[index];
			AudioStreamPlayer3D? player = _positionalPlayers[index];
			if (!voice.Active || player == null || !GodotObject.IsInstanceValid(player)) continue;

			if (!player.Playing)
			{
				voice.Active = false;
				continue;
			}

			PositionalAudioMix mix = PositionalAudioMixer.Calculate(listener, voice.Position, voice.MaxDistance, voice.BaseVolumeDb);
			voice.Priority = mix.PerceivedGain;

			if (Mathf.Abs(mix.VolumeDb - voice.AppliedVolumeDb) < VolumeWriteThresholdDb) continue;

			player.VolumeDb = mix.VolumeDb;
			voice.AppliedVolumeDb = mix.VolumeDb;
		}
	}

	public void PlaySound3D(string soundPath, Vector3 position, float volumeDb = 0f, float pitchVariation = 0.05f, AudioPlaybackChannel channel = AudioPlaybackChannel.Sfx, AudioPriorityTier tier = AudioPriorityTier.General, float maxDistance = PositionalAudioProfile.DefaultMaxDistance)
	{
		var stream = LoadAudioStream(soundPath);
		if (stream == null) return;

		EnsureAudioRoot();

		AudioListener listener = GetAudioListener();
		PositionalAudioMix mix = PositionalAudioMixer.Calculate(listener, position, maxDistance, volumeDb);
		if (mix.PerceivedGain <= 0f) return;

		int slotIndex = ReservePositionalVoice(soundPath, mix.PerceivedGain, tier);
		if (slotIndex < 0) return;

		AudioStreamPlayer3D? player = _positionalPlayers[slotIndex];
		if (player == null || !GodotObject.IsInstanceValid(player)) return;

		_positionalVoices[slotIndex] = new PositionalVoiceState
		{
			Active = true,
			SoundKey = soundPath,
			Position = position,
			BaseVolumeDb = volumeDb,
			MaxDistance = maxDistance,
			Priority = mix.PerceivedGain,
			AppliedVolumeDb = mix.VolumeDb,
			StartedTicksMs = Time.GetTicksMsec()
		};

		player.Bus = channel == AudioPlaybackChannel.Voice ? VoiceBusName : SfxBusName;
		player.MaxDistance = maxDistance;
		player.Stream = stream;
		player.VolumeDb = mix.VolumeDb;
		player.PitchScale = 1.0f + (pitchVariation > 0 ? (float)(_random.NextDouble() * 2 - 1) * pitchVariation : 0f);
		player.GlobalPosition = position;
		player.Play();
	}

	private int ReservePositionalVoice(string soundPath, float newPriority, AudioPriorityTier tier)
	{
		int tierStart = tier == AudioPriorityTier.Critical ? 0 : CriticalPositionalVoices;
		int tierEnd = tier == AudioPriorityTier.Critical ? CriticalPositionalVoices : MaxPositionalVoices;

		int freeSlot = -1;
		int quietestSlot = -1;
		float quietestPriority = float.MaxValue;
		int sameSoundCount = 0;
		int quietestSameSoundSlot = -1;
		float quietestSameSoundPriority = float.MaxValue;
		ulong nowTicksMs = Time.GetTicksMsec();

		for (int index = tierStart; index < tierEnd; index++)
		{
			ref PositionalVoiceState voice = ref _positionalVoices[index];
			AudioStreamPlayer3D? player = _positionalPlayers[index];

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
			bool canReplaceSameSound = newPriority > quietestSameSoundPriority + EvictionMargin
				&& HasSurvivedGracePeriod(quietestSameSoundSlot, nowTicksMs);
			return canReplaceSameSound ? quietestSameSoundSlot : -1;
		}

		if (freeSlot >= 0) return freeSlot;
		if (quietestSlot < 0) return -1;

		bool canEvict = newPriority > quietestPriority + EvictionMargin
			&& HasSurvivedGracePeriod(quietestSlot, nowTicksMs);
		return canEvict ? quietestSlot : -1;
	}

	private bool HasSurvivedGracePeriod(int slotIndex, ulong nowTicksMs)
	{
		if (slotIndex < 0) return false;

		return nowTicksMs - _positionalVoices[slotIndex].StartedTicksMs >= VoiceGracePeriodMs;
	}

	public void PlayUnitSound(string unitId, UnitSoundEvent eventType, Vector3 position, float volumeDb = 0f, AudioPriorityTier tier = AudioPriorityTier.General)
	{
		if (string.IsNullOrEmpty(unitId)) return;
		if (GameHost.UnitRegistry != null && GameHost.UnitRegistry.TryGetValue(unitId, out var meta))
		{
			PlayUnitSound(meta, eventType, position, volumeDb, tier);
		}
	}

	public void PlayUnitSound(UnitMetadata meta, UnitSoundEvent eventType, Vector3 position, float volumeDb = 0f, AudioPriorityTier tier = AudioPriorityTier.General)
	{
		if (meta.Sounds == null) return;

		string[]? pool = GetSoundPool(meta.Sounds, eventType);
		if (pool == null || pool.Length == 0) return;

		string clip = pool.Length == 1 ? pool[0] : pool[_random.Next(pool.Length)];
		if (string.IsNullOrEmpty(clip)) return;

		string soundKey = string.IsNullOrWhiteSpace(meta.Sounds.VoiceAsset)
			? clip
			: BuildRaudEntryKey(meta.Sounds.VoiceAsset, clip);

		PlaySound3D(soundKey, position, volumeDb, 0.08f, AudioPlaybackChannel.Voice, tier, PositionalAudioProfile.VoiceMaxDistance);
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
		if (_audioRoot != null && GodotObject.IsInstanceValid(_audioRoot)) return;

		Node? parentNode = null;
		if (GameContext.Host != null && GodotObject.IsInstanceValid(GameContext.Host))
		{
			parentNode = GameContext.Host;
		}
		else if (Engine.GetMainLoop() is SceneTree sceneTree)
		{
			parentNode = sceneTree.Root;
		}

		if (parentNode == null) return;

		InitializeAudioNodes();
		parentNode.AddChild(_audioRoot!);
		MakeListenerCurrent();
	}
}