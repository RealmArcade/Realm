using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Ecs.Services;
using Realm.EditorAPI;
using Realm.Shared.Audio;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Environment = System.Environment;
using FileAccess = Godot.FileAccess;
using HttpClient = System.Net.Http.HttpClient;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client;

[SupportedOSPlatform("windows")]
public partial class VSCodeManager
{
	private async void HandleIpcHttpRequest(HttpListenerContext ctx)
	{
		try
		{
			SetCorsHeaders(ctx);

			if (ctx.Request.HttpMethod == "OPTIONS")
			{
				HandleIpcOptionsRequest(ctx);
				return;
			}

			if (ctx.Request.HttpMethod == "GET")
			{
				await HandleIpcGetRequest(ctx);
				return;
			}

			await ProcessIpcPostRequest(ctx);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[VSCodeManager] HandleIpcHttpRequest error: {ex.Message}");
			try
			{
				ctx.Response.StatusCode = 500;
				ctx.Response.Close();
			}
			catch
			{
			}
		}
	}

	private void SetCorsHeaders(HttpListenerContext ctx)
	{
		ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
		ctx.Response.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
		ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
	}

	private async Task ProcessIpcPostRequest(HttpListenerContext ctx)
	{
		using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
		string body = await reader.ReadToEndAsync();
		JsonNode? node = JsonNode.Parse(body);
		string? action = GetActionFromNode(node);

		CheckFocusGodotWindow(action, node);

		var responseObj = new JsonObject();
		await RouteIpcAction(action, node, responseObj);

		await SendIpcResponse(ctx, responseObj);
	}

	private string? GetActionFromNode(JsonNode? node)
	{
		return node?["action"]?.ToString() ?? node?["type"]?.ToString();
	}

	private void CheckFocusGodotWindow(string? action, JsonNode? node)
	{
		if (ShouldFocusGodot(action, node))
		{
			EditorWindowIntegration.RestoreAndFocusGodotWindow();
		}
	}

	private bool ShouldFocusGodot(string? action, JsonNode? node)
	{
		return action == "openVfxDialog" ||
		       action == "openModelPicker" ||
		       action == "openAbilityVfxDialog" ||
		       action == "openAnimationStudio" ||
		       action == "openAnimationPreview" ||
		       action == "openEditAnimations" ||
		       (node?["focusGodot"]?.GetValue<bool>() ?? false);
	}

	private async Task RouteIpcAction(string? action, JsonNode? node, JsonObject responseObj)
	{
		if (string.IsNullOrEmpty(action)) return;

		if (action == "invokeEditorApi" || action.StartsWith("editorApi."))
		{
			await HandleEditorApiRequest(node, action, responseObj);
			return;
		}

		if (TryRouteDialogAction(action, node, responseObj)) return;
		if (TryRouteFileAction(action, node, responseObj)) return;
		if (TryRouteConversionAction(action, node, responseObj)) return;
	}

	private bool TryRouteDialogAction(string? action, JsonNode? node, JsonObject responseObj)
	{
		switch (action)
		{
			case "openVfxDialog":
				HandleOpenVfxDialog(node, responseObj);
				return true;
			case "openModelPicker":
				HandleOpenModelPicker(node, responseObj);
				return true;
			case "openAbilityVfxDialog":
				HandleOpenAbilityVfxDialog(node, responseObj);
				return true;
			case "openAnimationStudio":
			case "openAnimationPreview":
			case "openEditAnimations":
				HandleOpenAnimationStudio(node, responseObj);
				return true;
		}

		return false;
	}

	private bool TryRouteFileAction(string? action, JsonNode? node, JsonObject responseObj)
	{
		switch (action)
		{
			case "formatAndSaveJson":
			case "saveJsonFile":
			case "saveMetadata":
			case "saveTerrain":
				HandleFormatAndSaveJson(node, action, responseObj);
				return true;
			case "reloadMetadata":
			case "updateMetadata":
				HandleReloadMetadata(responseObj);
				return true;
		}

		return false;
	}

	private bool TryRouteConversionAction(string? action, JsonNode? node, JsonObject responseObj)
	{
		switch (action)
		{
			case "convertRtex":
				HandleConvertRtex(node, responseObj);
				return true;
			case "renderRanim":
				HandleRenderRanim(node, responseObj);
				return true;
			case "convertRmesh":
				HandleConvertRmesh(node, responseObj);
				return true;
			case "convertRaud":
				HandleConvertRaud(node, responseObj);
				return true;
		}

		return false;
	}

	private async Task SendIpcResponse(HttpListenerContext ctx, JsonObject responseObj)
	{
		string resJson = responseObj.ToJsonString();
		byte[] resBytes = Encoding.UTF8.GetBytes(resJson);
		ctx.Response.ContentType = "application/json";
		ctx.Response.ContentLength64 = resBytes.Length;
		await ctx.Response.OutputStream.WriteAsync(resBytes, 0, resBytes.Length);
		ctx.Response.Close();
	}


}
