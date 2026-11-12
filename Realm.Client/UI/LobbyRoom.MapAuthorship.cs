using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using NSec.Cryptography;
using Realm.Client.Network;
using Realm.Shared;
using Realm.Shared.Network;
using HttpClient = System.Net.Http.HttpClient;
using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Utils;
using Realm.Client.UI.InGame;
using Realm.Client.Services;
using Realm.Shared.Accounts;

namespace Realm.Client.UI;

public partial class LobbyRoom
{
	private async void VerifyMapAuthorship(string mapName)
	{
		_authorshipWarningLabel.Visible = false;
		_primaryAuthorLabel.Visible = false;
		_primaryAuthorLabel.Text = Tr("Author: Unknown");
		_otherAuthorsButton.Visible = false;
		_otherAuthorsList.Clear();

		string? mapJsonPath = TryGetMapJsonPath(mapName);
		_authorshipMapJsonPath = mapJsonPath;
		if (mapJsonPath == null) return;

		try
		{
			ExtractAuthorsFromJson(mapJsonPath);
			await VerifyAuthorSignatureAsync(mapJsonPath);
		}
		catch
		{
			if (IsCurrentAuthorshipMap(mapJsonPath)) _authorshipWarningLabel.Visible = true;
		}
	}

	private bool IsCurrentAuthorshipMap(string mapJsonPath)
	{
		return string.Equals(_authorshipMapJsonPath, mapJsonPath, StringComparison.OrdinalIgnoreCase);
	}

	private string? TryGetMapJsonPath(string mapName)
	{
		string? manifestPath = MapAssetManager.FindManifestPath(mapName);
		string mapDir = manifestPath != null ? Path.GetDirectoryName(manifestPath)! : ProjectSettings.GlobalizePath("user://maps/" + mapName);
		string mapJsonPath = Path.Combine(mapDir, "metadata.json");
		if (File.Exists(mapJsonPath)) return mapJsonPath;

		mapJsonPath = Path.Combine(mapDir, "manifest.json");
		return File.Exists(mapJsonPath) ? mapJsonPath : null;
	}

	private void ExtractAuthorsFromJson(string mapJsonPath)
	{
		string jsonContent = File.ReadAllText(mapJsonPath);
		var mapDoc = JsonNode.Parse(jsonContent);
		if (mapDoc == null) return;

		if (mapDoc["Contributors"] is JsonArray contArr)
		{
			foreach (var node in contArr)
			{
				if (node != null)
				{
					_otherAuthorsList.Add(node.GetValue<string>());
				}
			}
		}

		ShowStatedAuthor(ReadStatedAuthor(mapDoc, mapJsonPath));
		_otherAuthorsButton.Visible = _otherAuthorsList.Count > 0;
	}

	private void ShowStatedAuthor(string? author)
	{
		if (string.IsNullOrWhiteSpace(author)) return;

		_primaryAuthorLabel.Text = Tr("Author:") + " " + author.Trim();
		_primaryAuthorLabel.Visible = true;
	}

	/// <summary>
	///     The author a map folder states about itself. A metadata file keeps its display properties under MapProperties,
	///     while the manifest beside it is the file the lobby browser and the archive headers read the author from, so the
	///     folder is asked in the order its own files describe it.
	/// </summary>
	private static string? ReadStatedAuthor(JsonNode mapDoc, string mapJsonPath)
	{
		string? statedAuthor = mapDoc["MapProperties"]?["Author"]?.ToString();
		if (string.IsNullOrWhiteSpace(statedAuthor)) statedAuthor = mapDoc["Author"]?.ToString();
		if (!string.IsNullOrWhiteSpace(statedAuthor)) return statedAuthor;

		string manifestJsonPath = Path.Combine(Path.GetDirectoryName(mapJsonPath) ?? string.Empty, "manifest.json");
		if (!File.Exists(manifestJsonPath)) return null;

		try
		{
			return JsonNode.Parse(File.ReadAllText(manifestJsonPath))?["Author"]?.ToString();
		}
		catch
		{
			return null;
		}
	}

	private async Task VerifyAuthorSignatureAsync(string mapJsonPath)
	{
		byte[] fileBytes = File.ReadAllBytes(mapJsonPath);
		string mapBlake3 = RealmMetadataHelper.ComputeBlake3(fileBytes, ".json");
		string hash = $"{mapBlake3}.json";

		var mapDoc = JsonNode.Parse(fileBytes);
		bool claimsAuthorship = mapDoc?["signature"] != null || mapDoc?["author_key"] != null;

		string adminServerUrl = Lobby.RegistryServerUrl;
		if (string.IsNullOrWhiteSpace(adminServerUrl)) return;

		string assetAuthorJson;
		try
		{
			using var assetAuthorRes = await _sharedHttpClient.GetAsync(adminServerUrl + "/api/publish_map/asset_author/" + hash);
			if (!assetAuthorRes.IsSuccessStatusCode)
			{
				if (claimsAuthorship && IsCurrentAuthorshipMap(mapJsonPath)) _authorshipWarningLabel.Visible = true;
				return;
			}

			assetAuthorJson = await assetAuthorRes.Content.ReadAsStringAsync();
		}
		catch (Exception ex)
		{
			GameLog.LogError($"could not fetch the asset author for this lobby, so attribution will be missing: {ex.GetType().Name}: {ex.Message}");
			return;
		}

		var assetMeta = JsonNode.Parse(assetAuthorJson);
		if (assetMeta == null)
		{
			if (IsCurrentAuthorshipMap(mapJsonPath)) _authorshipWarningLabel.Visible = true;
			return;
		}

		string? author = assetMeta["AuthorUsername"]?.GetValue<string>();
		string? signatureB64 = assetMeta["Signature"]?.GetValue<string>();
		string? pubKeyB64 = assetMeta["PublicKey"]?.GetValue<string>();

		if (string.IsNullOrEmpty(signatureB64) || string.IsNullOrEmpty(pubKeyB64))
		{
			if (IsCurrentAuthorshipMap(mapJsonPath)) _authorshipWarningLabel.Visible = true;
			return;
		}

		byte[] signatureBytes = Convert.FromBase64String(signatureB64);
		byte[] pubKeyBytes = Convert.FromBase64String(pubKeyB64);
		byte[] hashBytes = Encoding.UTF8.GetBytes(hash);

		var publicKey = PublicKey.Import(SignatureAlgorithm.Ed25519, pubKeyBytes, KeyBlobFormat.RawPublicKey);
		bool isValid = SignatureAlgorithm.Ed25519.Verify(publicKey, hashBytes, signatureBytes);

		if (!IsCurrentAuthorshipMap(mapJsonPath)) return;

		if (isValid)
		{
			_authorshipWarningLabel.Visible = false;
			ShowStatedAuthor(string.IsNullOrWhiteSpace(author) ? null : author);
		}
		else
		{
			_authorshipWarningLabel.Visible = true;
		}
	}

	private void SetupAttributionButton()
	{
		_attributionButton = new Button();
		_attributionButton.Name = "AttributionButton_Button";
		_attributionButton.Text = LocalizationManager.Translate("ATTRIBUTION");
		_attributionButton.Set("icon_max_width", 0);
		_attributionButton.CustomMinimumSize = new Vector2(160, 28);
		_attributionButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		_attributionButton.FocusMode = FocusModeEnum.None;
		_attributionButton.TooltipText = LocalizationManager.Translate("Show maps whose content was imported into this map");
		_attributionButton.AddThemeFontSizeOverride("font_size", 11);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(_attributionButton);
		_attributionButton.Pressed += OnAttributionPressed;
		GetNode<VBoxContainer>("BriefingPanel/VBoxContainer").AddChild(_attributionButton);
	}

	private void OnAttributionPressed()
	{
		GameContext.UserInterface?.PlayClickSound();

		if (_attributionPopover == null || !GodotObject.IsInstanceValid(_attributionPopover))
		{
			_attributionPopover = new AttributionPopover();
			AddChild(_attributionPopover);
			_attributionPopover.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		}

		int selectedIndex = GetSelectedMapIndex();
		IReadOnlyList<string> sourceMapNames = selectedIndex >= 0
			? MapAttributionLookup.Resolve(_lobbyRoomMaps[selectedIndex].Attribution_SourceMap, _lobbyRoomMaps[selectedIndex].PathName, _lobbyRoomMaps[selectedIndex].Version)
			: Array.Empty<string>();

		_attributionPopover.ShowFor(_attributionButton, sourceMapNames);
	}

	private void OnOtherAuthorsPressed()
	{
		GameContext.UserInterface?.PlayClickSound();

		if (_otherAuthorsPopover == null || !GodotObject.IsInstanceValid(_otherAuthorsPopover))
		{
			_otherAuthorsPopover = new AttributionPopover();
			AddChild(_otherAuthorsPopover);
			_otherAuthorsPopover.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		}

		_otherAuthorsPopover.ShowFor(
			_otherAuthorsButton,
			LocalizationManager.Translate("Other Authors"),
			LocalizationManager.Translate("This map lists these additional contributors:"),
			LocalizationManager.Translate("No other contributors listed."),
			_otherAuthorsList);
	}

	private int GetSelectedMapIndex()
	{
		string currentMap = Lobby.ActiveMapName ?? "melee";
		int selectedIndex = _lobbyRoomMaps.FindIndex(m => m.PathName == currentMap);
		if (selectedIndex >= 0) return selectedIndex;

		selectedIndex = _lobbyRoomMaps.FindIndex(m => m.PathName.ToLower().Contains(currentMap.ToLower()));
		if (selectedIndex >= 0) return selectedIndex;

		return _lobbyRoomMaps.Count > 0 ? 0 : -1;
	}

	private void UpdateSelectedMapUI()
	{
		int selectedIndex = GetSelectedMapIndex();
		if (selectedIndex >= 0)
		{
			MapBriefingDetails selectedMap = _lobbyRoomMaps[selectedIndex];
			_mapNameLabel.Text = selectedMap.DisplayName.ToUpper();
			_briefingLabel.Text = BuildBriefingText(selectedMap);
			_briefingLabel.ScrollToLine(0);
			VerifyMapAuthorship(selectedMap.PathName);
		}

		_attributionPopover?.Close();
		_otherAuthorsPopover?.Close();
	}

	/// <summary>
	///     Composes the briefing this screen shows from the selected map's own metadata, so a map describes itself and the
	///     lobby never states something the map did not say.
	/// </summary>
	private static string BuildBriefingText(MapBriefingDetails selectedMap)
	{
		var builder = new StringBuilder();
		AppendBriefingParagraph(builder, selectedMap.Description);
		AppendBriefingSection(builder, TranslationServer.Translate("OBJECTIVE").ToString(), selectedMap.HowToPlayObjective);
		AppendBriefingSection(builder, TranslationServer.Translate("PLAYERS").ToString(), selectedMap.SuggestedPlayers);
		AppendBriefingSection(builder, TranslationServer.Translate("TAGS").ToString(), selectedMap.Tags is { Count: > 0 } ? string.Join(" · ", selectedMap.Tags) : string.Empty);

		return builder.Length > 0 ? builder.ToString() : TranslationServer.Translate("This map has not described itself yet.").ToString();
	}

	private static void AppendBriefingParagraph(StringBuilder builder, string? text)
	{
		if (string.IsNullOrWhiteSpace(text)) return;

		if (builder.Length > 0) builder.Append("\n\n");
		builder.Append(text.Trim());
	}

	private static void AppendBriefingSection(StringBuilder builder, string label, string? text)
	{
		if (string.IsNullOrWhiteSpace(text)) return;

		if (builder.Length > 0) builder.Append("\n\n");
		builder.Append($"[b][color={UIStyle.ColorGold.ToHtml(false)}]{label}[/color][/b]\n{text.Trim()}");
	}
}
