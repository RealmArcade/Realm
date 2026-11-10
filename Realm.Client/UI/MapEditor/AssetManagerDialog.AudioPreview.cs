using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.EditorAPI;
using Realm.Shared.Audio;
using Realm.Shared.Textures;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

public partial class AssetManagerDialog
{
	private void UpdateSelectedAssetAudioPreview()
	{
		string ext = _selectedAsset!.Extension?.ToLowerInvariant() ?? "";
		bool isAudio = ext is ".raud" or ".ogg" or ".wav" or ".mp3" or ".flac" or ".aac";

		if (!isAudio || !File.Exists(_selectedAsset.FilePath))
		{
			_btnAudioPlay.Visible = false;
			_optRaudEntries.Visible = false;
			_optRaudEntries.Clear();
			_selectedRaudEntryName = null;
			return;
		}

		if (ext == ".raud")
		{
			PopulateRaudEntrySelector(_selectedAsset.FilePath);
		}
		else
		{
			_optRaudEntries.Visible = false;
			_optRaudEntries.Clear();
			_selectedRaudEntryName = null;
		}

		LoadAudioStream(ext, _selectedAsset.FilePath, _selectedRaudEntryName);

		_btnAudioPlay.Visible = _audioPlayer.Stream != null;
		if (_btnAudioPlay.Visible)
		{
			_btnAudioPlay.Text = "▶ " + TranslationServer.Translate("Play");
		}
	}

	private void PopulateRaudEntrySelector(string filePath)
	{
		_optRaudEntries.Clear();
		_selectedRaudEntryName = null;

		List<string> entryNames = RaudFile.GetEntryNamesFromFile(filePath);
		bool showSelector = entryNames.Count > 1;
		_optRaudEntries.Visible = showSelector;
		if (!showSelector) return;

		foreach (string entryName in entryNames)
		{
			_optRaudEntries.AddItem(entryName);
		}

		_optRaudEntries.Select(0);
		_selectedRaudEntryName = entryNames[0];
	}

	private void OnRaudEntrySelected(long index)
	{
		if (_selectedAsset == null) return;

		List<string> entryNames = RaudFile.GetEntryNamesFromFile(_selectedAsset.FilePath);
		if (index < 0 || index >= entryNames.Count) return;

		StopAudio();
		_selectedRaudEntryName = entryNames[(int)index];
		LoadAudioStream(".raud", _selectedAsset.FilePath, _selectedRaudEntryName);

		_btnAudioPlay.Visible = _audioPlayer.Stream != null;
		if (_btnAudioPlay.Visible)
		{
			_btnAudioPlay.Text = "▶ " + TranslationServer.Translate("Play");
		}
	}

	private void LoadAudioStream(string ext, string filePath, string? entryName = null)
	{
		try
		{
			if (ext == ".raud")
			{
				LoadRaudStream(filePath, entryName);
			}
			else if (ext == ".ogg")
			{
				LoadOggStream(filePath);
			}
			else
			{
				_audioPlayer.Stream = GD.Load<AudioStream>(filePath);
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[AssetManagerDialog] Failed to load audio stream: {ex.Message}");
		}
	}

	private void LoadRaudStream(string filePath, string? entryName = null)
	{
		byte[] raudBytes = File.ReadAllBytes(filePath);
		byte[]? oggBytes = RaudFile.GetEntry(raudBytes, entryName);
		if (oggBytes == null || oggBytes.Length == 0) return;

		var oggStream = AudioStreamOggVorbis.LoadFromBuffer(oggBytes);
		if (oggStream != null)
		{
			oggStream.Loop = false;
			_audioPlayer.Stream = oggStream;
		}
	}

	private void LoadOggStream(string filePath)
	{
		var oggStream = AudioStreamOggVorbis.LoadFromFile(filePath);
		if (oggStream != null)
		{
			oggStream.Loop = false;
			_audioPlayer.Stream = oggStream;
		}
	}

	private void OnAudioPlayPausePressed()
	{
		if (_audioPlayer == null || _audioPlayer.Stream == null) return;

		if (_audioPlayer.Playing)
		{
			_audioPlayer.Stop();
			_btnAudioPlay.Text = "▶ " + TranslationServer.Translate("Play");
		}
		else
		{
			_audioPlayer.Play();
			_btnAudioPlay.Text = "⏹ " + TranslationServer.Translate("Stop");

			if (!AudioService.IsMasterVolumeAudible())
			{
				Hud?.ShowFeedback(AudioService.PreviewInaudibleWarning);
			}
		}
	}

	private void OnAudioFinished()
	{
		if (_btnAudioPlay != null)
		{
			_btnAudioPlay.Text = "▶ " + TranslationServer.Translate("Play");
		}
	}

	private void StopAudio()
	{
		if (_audioPlayer != null)
		{
			if (_audioPlayer.Playing)
			{
				_audioPlayer.Stop();
			}

			_audioPlayer.Stream = null;
		}

		if (_btnAudioPlay != null)
		{
			_btnAudioPlay.Text = "▶ " + TranslationServer.Translate("Play");
		}
	}
}
