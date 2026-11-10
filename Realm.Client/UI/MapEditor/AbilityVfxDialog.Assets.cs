// Asset resolution for the preview: sounds and textures are looked up in the workspace first, then in
// res:// candidates, and each route reports the first candidate that actually loads.

using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using Realm.Client.Services;
using Realm.Client.VFX;
using Realm.Shared.Audio;
using Realm.Shared.Textures;
using Realm.Client.Utils;


namespace Realm.Client.UI.MapEditor;

public partial class AbilityVfxDialog
{
	private void PlaySoundFile(string soundPath)
	{
		if (string.IsNullOrWhiteSpace(soundPath) || _sfxPlayer == null) return;

		try
		{
			if (TryPlayResPath(soundPath)) return;

			AudioStream? stream = TryLoadSoundFromWorkspace(soundPath) ?? TryLoadSoundFromResCandidates(soundPath);

			if (stream != null)
			{
				_sfxPlayer.Stream = stream;
				_sfxPlayer.Play();
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[AbilityVfxDialog] PlaySoundFile error: {ex.Message}");
		}
	}

	private bool TryPlayResPath(string soundPath)
	{
		if (_sfxPlayer == null) return false;
		if (!soundPath.StartsWith("res://") || !ResourceLoader.Exists(soundPath)) return false;

		_sfxPlayer.Stream = GD.Load<AudioStream>(soundPath);
		_sfxPlayer.Play();
		return true;
	}

	private AudioStream? TryLoadSoundFromWorkspace(string soundPath)
	{
		string wsPath = GameContext.EditorService._editingDirectory;
		string cleanPath = soundPath.Trim().TrimStart('/', '\\').Replace('\\', '/');
		string fileName = Path.GetFileName(cleanPath);

		var candidatePaths = new[]
		{
			soundPath,
			Path.Combine(wsPath, cleanPath),
			Path.Combine(wsPath, "Assets", cleanPath),
			Path.Combine(wsPath, "Assets", "audio", "sfx", fileName),
			Path.Combine(wsPath, "Assets", "audio", "music", fileName),
			Path.Combine(wsPath, "Assets", "audio", fileName),
			Path.Combine(wsPath, "Assets", "sounds", fileName),
		};

		foreach (var candidate in candidatePaths)
		{
			if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate)) continue;

			AudioStream? stream = LoadAudioStreamFromFile(candidate);
			if (stream != null) return stream;
		}

		return null;
	}

	private AudioStream? LoadAudioStreamFromFile(string candidate)
	{
		if (candidate.EndsWith(".raud", StringComparison.OrdinalIgnoreCase))
		{
			byte[] raudBytes = File.ReadAllBytes(candidate);
			byte[]? oggBytes = RaudFile.GetTrack(raudBytes, 0);
			if (oggBytes != null && oggBytes.Length > 0)
			{
				return AudioStreamOggVorbis.LoadFromBuffer(oggBytes);
			}

			return null;
		}

		if (candidate.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
		{
			return AudioStreamOggVorbis.LoadFromFile(candidate);
		}

		return GD.Load<AudioStream>(candidate);
	}

	private AudioStream? TryLoadSoundFromResCandidates(string soundPath)
	{
		string cleanPath = soundPath.Trim().TrimStart('/', '\\').Replace('\\', '/');
		string fileName = Path.GetFileName(cleanPath);

		var resCandidates = new[]
		{
			$"res://Assets/audio/sfx/{fileName}",
			$"res://Assets/audio/music/{fileName}",
			$"res://Assets/audio/{fileName}",
			$"res://Assets/sounds/{fileName}",
			$"res://{cleanPath}"
		};

		foreach (var resPath in resCandidates)
		{
			if (ResourceLoader.Exists(resPath))
			{
				var stream = GD.Load<AudioStream>(resPath);
				if (stream != null) return stream;
			}
		}

		return null;
	}

	private Texture2D? ResolveTexture(string path)
	{
		if (string.IsNullOrWhiteSpace(path)) return null;

		try
		{
			if (path.StartsWith("res://") && ResourceLoader.Exists(path))
			{
				return TrimmedTextureLoader.LoadTrimmedTexture(path);
			}

			Texture2D? tex = TryLoadTextureFromWorkspace(path) ?? TryLoadTextureFromResCandidates(path);
			return tex;
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[AbilityVfxDialog] ResolveTexture error: {ex.Message}");
		}

		return null;
	}

	private Texture2D? TryLoadTextureFromWorkspace(string path)
	{
		string wsPath = GameContext.EditorService._editingDirectory;
		string cleanPath = path.Trim().TrimStart('/', '\\').Replace('\\', '/');
		string fileName = Path.GetFileName(cleanPath);

		var candidatePaths = new[]
		{
			path,
			Path.Combine(wsPath, cleanPath),
			Path.Combine(wsPath, "Assets", cleanPath),
			Path.Combine(wsPath, "Assets", "vfx", fileName),
			Path.Combine(wsPath, "Assets", "icons", fileName),
			Path.Combine(wsPath, "Assets", "decals", fileName),
			Path.Combine(wsPath, "Assets", "textures", fileName),
			Path.Combine(wsPath, "Assets", "ribbons", fileName),
			Path.Combine(wsPath, "Assets", "noise", fileName),
			Path.Combine(wsPath, "Assets", "skyboxes", fileName),
			Path.Combine(wsPath, "Assets", "UI", fileName),
		};

		foreach (var candidate in candidatePaths)
		{
			if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate)) continue;

			Texture2D? tex = LoadTextureFromFile(candidate);
			if (tex != null) return tex;
		}

		return null;
	}

	private Texture2D? LoadTextureFromFile(string candidate)
	{
		Image? img = null;
		if (candidate.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			byte[] rtexBytes = File.ReadAllBytes(candidate);
			byte[]? webpBytes = RtexFile.GetLayer(rtexBytes, 0);
			if (webpBytes != null && webpBytes.Length > 0)
			{
				img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
				if (img.LoadWebpFromBuffer(webpBytes) != Error.Ok)
				{
					img.LoadPngFromBuffer(webpBytes);
				}
			}
		}
		else
		{
			img = Image.LoadFromFile(candidate);
		}

		if (img != null)
		{
			return ImageTexture.CreateFromImage(img);
		}

		return null;
	}

	private Texture2D? TryLoadTextureFromResCandidates(string path)
	{
		string cleanPath = path.Trim().TrimStart('/', '\\').Replace('\\', '/');
		string fileName = Path.GetFileName(cleanPath);

		var resCandidates = new[]
		{
			$"res://Assets/vfx/{fileName}",
			$"res://Assets/icons/{fileName}",
			$"res://Assets/decals/{fileName}",
			$"res://Assets/textures/{fileName}",
			$"res://Assets/UI/{fileName}",
			$"res://{cleanPath}"
		};

		foreach (var resPath in resCandidates)
		{
			if (ResourceLoader.Exists(resPath))
			{
				return TrimmedTextureLoader.LoadTrimmedTexture(resPath);
			}
		}

		return null;
	}

}