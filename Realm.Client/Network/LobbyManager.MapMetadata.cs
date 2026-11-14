using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.Utils;
using Realm.Shared;
using Realm.Shared.Accounts;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Metadata;
using Realm.Shared.Network;
using Realm.Shared.Services;
using Environment = System.Environment;
using FileAccess = System.IO.FileAccess;
using HttpClient = System.Net.Http.HttpClient;

namespace Realm.Client.Network;
public partial class LobbyManager
{
	private (string Version, string Signature, string PublicKey, string MapHash, long SizeBytes) GetMapMetadataInfo(string mapPathName, string mapDisplayName, string? explicitVersion)
	{
		string mapVersion = "1.0.0";
		if (!string.IsNullOrWhiteSpace(explicitVersion))
		{
			mapVersion = explicitVersion.Trim();
		}

		string signature = "";
		string publicKey = "";
		string mapHash = "";

		try
		{
			string? manifestPath = MapAssetManager.FindManifestPath(mapPathName, mapVersion);
			if (manifestPath == null)
			{
				manifestPath = MapAssetManager.FindManifestPath(mapDisplayName, mapVersion);
			}

			if (manifestPath != null)
			{
				TryReadManifestMetadata(manifestPath, ref mapVersion, out signature, out publicKey, out mapHash);
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[LobbyManager] Failed to read map signature metadata: {ex.Message}");
		}

		long mapSizeBytes = MapAssetManager.GetMapTotalSizeBytes(mapPathName, mapVersion);
		if (mapSizeBytes <= 0)
		{
			mapSizeBytes = MapAssetManager.GetMapTotalSizeBytes(mapDisplayName, mapVersion);
		}

		return (mapVersion, signature, publicKey, mapHash, mapSizeBytes);
	}

	private void TryReadManifestMetadata(string manifestPath, ref string mapVersion, out string signature, out string publicKey, out string mapHash)
	{
		signature = "";
		publicKey = "";
		mapHash = "";

		if (!File.Exists(manifestPath))
		{
			return;
		}

		string json = File.ReadAllText(manifestPath);
		using var mapDoc = JsonDocument.Parse(json);
		var root = mapDoc.RootElement;

		mapVersion = UpdateMapVersion(root, mapVersion);

		signature = GetStringProperty(root, "signature");
		publicKey = GetStringProperty(root, "author_key");

		if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(publicKey))
		{
			return;
		}

		byte[] mapBytes = File.ReadAllBytes(manifestPath);
		string mapBlake3 = RealmMetadataHelper.ComputeBlake3(mapBytes, ".json");
		mapHash = $"{mapBlake3}.json";
	}

	private string UpdateMapVersion(JsonElement root, string defaultVersion)
	{
		if (root.TryGetProperty("Version", out var vProp) && vProp.ValueKind == JsonValueKind.String)
		{
			string? ver = vProp.GetString();
			if (ver != null) return ver;
		}

		if (root.TryGetProperty("MapProperties", out var props) && props.TryGetProperty("MapVersion", out var mv))
		{
			string? mver = mv.GetString();
			if (mver != null) return mver;
		}

		return defaultVersion;
	}

	private string GetStringProperty(JsonElement root, string propName)
	{
		if (root.TryGetProperty(propName, out var prop))
		{
			string? val = prop.GetString();
			if (val != null)
			{
				return val;
			}
		}

		return "";
	}

	private async Task<bool> HandleRegisterFailureAsync(HttpResponseMessage? response)
	{
		string errMsg = "Registry server registration failed.";

		if (response != null)
		{
			errMsg = await ExtractErrorMessageAsync(response, errMsg);
		}

		LastHostError = errMsg;
		GameLog.LogError($"[LobbyManager] {errMsg}");
		return false;
	}

	private async Task<string> ExtractErrorMessageAsync(HttpResponseMessage response, string defaultError)
	{
		try
		{
			string body = await response.Content.ReadAsStringAsync();
			using var errDoc = JsonDocument.Parse(body);

			string parsedError = TryGetErrorMessage(errDoc.RootElement);
			if (!string.IsNullOrEmpty(parsedError))
			{
				return parsedError;
			}

			if (!string.IsNullOrWhiteSpace(body))
			{
				return body;
			}
		}
		catch
		{
		}

		return defaultError;
	}

	private string TryGetErrorMessage(JsonElement root)
	{
		string[] errorProperties = { "message", "Message", "title", "detail" };

		foreach (string propName in errorProperties)
		{
			if (root.TryGetProperty(propName, out var msgProp))
			{
				string? msg = msgProp.GetString();
				if (msg != null) return msg;
			}
		}

		return string.Empty;
	}

	private async Task ProcessSuccessfulRegistrationAsync(HttpResponseMessage response)
	{
		var respText = await response.Content.ReadAsStringAsync();
		using var doc = JsonDocument.Parse(respText);
		ActiveLobbyId = doc.RootElement.GetProperty("lobbyId").GetString();

		if (doc.RootElement.TryGetProperty("hostToken", out var hostTokenProp))
		{
			_hostToken = hostTokenProp.GetString();
		}

		GameLog.LogToConsole($"[LobbyManager] Lobby registered on server. LobbyId: {ActiveLobbyId}");

		if (!string.IsNullOrEmpty(ActiveLobbyId))
		{
			StartHostWebSocketSignaling(ActiveLobbyId);
			StartHeartbeatLoop(ActiveLobbyId);
		}
	}

}
