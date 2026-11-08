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
	private void HandleIpcOptionsRequest(HttpListenerContext ctx)
	{
		ctx.Response.StatusCode = 200;
		ctx.Response.Close();
	}

	private async Task HandleIpcGetRequest(HttpListenerContext ctx)
	{
		if (ctx.Request.Url != null && ctx.Request.Url.AbsolutePath.EndsWith("/locale"))
		{
			string locCode = GameSettings.Language.ToLocaleCode();
			var dict = LocalizationManager.GetDictionary(locCode);
			var locObj = new JsonObject();
			locObj["locale"] = locCode;
			locObj["dictionary"] = JsonSerializer.SerializeToNode(dict);
			string locJson = locObj.ToJsonString();
			byte[] locBytes = Encoding.UTF8.GetBytes(locJson);
			ctx.Response.ContentType = "application/json";
			ctx.Response.ContentLength64 = locBytes.Length;
			await ctx.Response.OutputStream.WriteAsync(locBytes, 0, locBytes.Length);
			ctx.Response.Close();
			return;
		}

		var pollResponseObj = new JsonObject();
		var commandsArray = new JsonArray();
		while (_pendingExtensionCommands.TryDequeue(out var cmd))
		{
			commandsArray.Add(cmd);
		}

		if (GameContext.SessionState.IsMapEditorMode && !string.IsNullOrEmpty(GameContext.EditorService._editingDirectory))
		{
			pollResponseObj["workspaceMetadataPath"] = Path.Combine(GameContext.EditorService._editingDirectory, "metadata.json");
		}

		pollResponseObj["commands"] = commandsArray;
		string pollResJson = pollResponseObj.ToJsonString();
		byte[] pollResBytes = Encoding.UTF8.GetBytes(pollResJson);
		ctx.Response.ContentType = "application/json";
		ctx.Response.ContentLength64 = pollResBytes.Length;
		await ctx.Response.OutputStream.WriteAsync(pollResBytes, 0, pollResBytes.Length);
		ctx.Response.Close();
	}

	private async Task HandleEditorApiRequest(JsonNode? node, string action, JsonObject responseObj)
	{
		string apiMethod = GetApiMethodName(node, action);
		string reqId = node?["requestId"]?.ToString() ?? "";

		var (apiSuccess, invocationResult, apiError) = await ExecuteEditorApiMethod(apiMethod, node);

		PopulateEditorApiResponse(responseObj, apiMethod, reqId, apiSuccess, invocationResult, apiError);
	}

	private string GetApiMethodName(JsonNode? node, string action)
	{
		return action == "invokeEditorApi" ? (node?["method"]?.ToString() ?? "") : action.Substring(10);
	}

	private async Task<(bool success, object? result, string error)> ExecuteEditorApiMethod(string apiMethod, JsonNode? node)
	{
		try
		{
			var editorApi = GameContext.IEditorAPI;
			if (editorApi == null)
				return (false, null, "IEditorAPI service not registered or editor inactive.");

			var methodInfo = typeof(IEditorAPI).GetMethods()
				.FirstOrDefault(m => string.Equals(m.Name, apiMethod, StringComparison.OrdinalIgnoreCase));

			if (methodInfo == null)
				return (false, null, $"Method '{apiMethod}' not found on IEditorAPI.");

			var args = ParseApiArguments(methodInfo, node);
			return await InvokeMethodAsync(editorApi, methodInfo, args);
		}
		catch (Exception ex)
		{
			return (false, null, ex.Message);
		}
	}

	private async Task<(bool success, object? result, string error)> InvokeMethodAsync(IEditorAPI editorApi, MethodInfo methodInfo, object?[] args)
	{
		var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		Callable.From(() =>
		{
			try
			{
				var result = methodInfo.Invoke(editorApi, args);
				tcs.TrySetResult(result);
			}
			catch (TargetInvocationException tie)
			{
				tcs.TrySetException(tie.InnerException ?? tie);
			}
			catch (Exception exMethod)
			{
				tcs.TrySetException(exMethod);
			}
		}).CallDeferred();

		try
		{
			var invocationResult = await tcs.Task;
			return (true, invocationResult, "");
		}
		catch (Exception exAsync)
		{
			GameLog.LogError($"[VSCodeManager] EditorAPI call {methodInfo.Name} failed: {exAsync.Message}");
			return (false, null, exAsync.Message);
		}
	}

	private void PopulateEditorApiResponse(JsonObject responseObj, string apiMethod, string reqId, bool apiSuccess, object? invocationResult, string apiError)
	{
		responseObj["action"] = "invokeEditorApiResult";
		responseObj["method"] = apiMethod;
		responseObj["requestId"] = reqId;
		responseObj["success"] = apiSuccess;

		if (invocationResult != null)
		{
			responseObj["result"] = JsonSerializer.SerializeToNode(invocationResult);
			if (invocationResult is string strVal)
				responseObj["instanceId"] = strVal;
			else if (invocationResult is bool bVal)
				responseObj["deleted"] = bVal;
		}

		if (!string.IsNullOrEmpty(apiError))
			responseObj["error"] = apiError;
	}


	private object?[] ParseApiArguments(MethodInfo methodInfo, JsonNode? node)
	{
		var parameters = methodInfo.GetParameters();
		var args = new object?[parameters.Length];
		var argsNode = node?["args"] as JsonObject ?? node as JsonObject;

		for (int i = 0; i < parameters.Length; i++)
		{
			args[i] = ParseSingleArgument(parameters[i], argsNode);
		}

		return args;
	}

	private object? ParseSingleArgument(ParameterInfo param, JsonObject? argsNode)
	{
		Type paramType = param.ParameterType;
		JsonNode? paramNode = GetParamNode(param.Name, argsNode);

		if (paramNode == null)
		{
			return GetDefaultArgument(paramType, param, argsNode);
		}

		if (paramType == typeof(Vector3))
		{
			return ParseVector3Node(paramNode);
		}

		return JsonSerializer.Deserialize(paramNode.ToJsonString(), paramType, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
	}

	private JsonNode? GetParamNode(string? paramName, JsonObject? argsNode)
	{
		if (argsNode == null) return null;

		if (paramName != null && argsNode.TryGetPropertyValue(paramName, out var matchedNode))
		{
			return matchedNode;
		}

		foreach (var kvp in argsNode)
		{
			if (string.Equals(kvp.Key, paramName, StringComparison.OrdinalIgnoreCase))
			{
				return kvp.Value;
			}
		}

		return null;
	}

	private object? GetDefaultArgument(Type paramType, ParameterInfo param, JsonObject? argsNode)
	{
		if (paramType == typeof(Vector3))
		{
			return ExtractVector3FromArgsNode(argsNode);
		}

		if (param.HasDefaultValue)
			return param.DefaultValue;
		if (paramType.IsValueType)
			return Activator.CreateInstance(paramType);

		return null;
	}

	private Vector3 ExtractVector3FromArgsNode(JsonObject? argsNode)
	{
		float vx = 0f, vy = 0f, vz = 0f;
		if (argsNode != null)
		{
			foreach (var kvp in argsNode)
			{
				if (kvp.Value == null) continue;
				if (string.Equals(kvp.Key, "x", StringComparison.OrdinalIgnoreCase))
					vx = kvp.Value.GetValue<float>();
				else if (string.Equals(kvp.Key, "y", StringComparison.OrdinalIgnoreCase))
					vy = kvp.Value.GetValue<float>();
				else if (string.Equals(kvp.Key, "z", StringComparison.OrdinalIgnoreCase))
					vz = kvp.Value.GetValue<float>();
			}
		}

		return new Vector3(vx, vy, vz);
	}

	private Vector3 ParseVector3Node(JsonNode paramNode)
	{
		if (paramNode is JsonObject vObj)
		{
			return ExtractVector3FromArgsNode(vObj);
		}

		if (paramNode is JsonArray vArr && vArr.Count >= 3)
		{
			return new Vector3(
				vArr[0]?.GetValue<float>() ?? 0f,
				vArr[1]?.GetValue<float>() ?? 0f,
				vArr[2]?.GetValue<float>() ?? 0f
			);
		}

		return new Vector3(0f, 0f, 0f);
	}


}
