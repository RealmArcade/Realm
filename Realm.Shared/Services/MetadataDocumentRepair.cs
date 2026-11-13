using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Realm.Shared.Metadata;

namespace Realm.Shared.Services;

/// <summary>
///     What a repair pass did to a metadata document: the members it removed because this build does not state them, the
///     ones it added because the document did not state them, and whether the document carried a signature that the
///     rewrite invalidated.
/// </summary>
public class MetadataDocumentRepairResult
{
	public bool Success { get; init; } = true;
	public bool Changed { get; init; }
	public bool SignatureDropped { get; init; }
	public string? ErrorMessage { get; init; }
	public List<string> RemovedMembers { get; init; } = new();
	public List<string> AddedMembers { get; init; } = new();
}

/// <summary>
///     Brings a metadata document to the exact shape this build states: members the model does not declare are removed
///     and members the document leaves out are added with their defaults, so a document is either canonical or refused
///     with a member named, never silently half-understood. The document is walked against the schema generated from the
///     same model the reader uses, so a change to the model changes what is repaired without a second list to maintain.
/// </summary>
public static class MetadataDocumentRepair
{
	private const string SchemaReferencePrefix = "#/";

	private const int MaximumReferenceDepth = 8;

	public static MetadataDocumentRepairResult Canonicalize(string metadataPath, Action<MapMetadata> fillDefaults)
	{
		string originalText;
		try
		{
			originalText = File.Exists(metadataPath) ? File.ReadAllText(metadataPath) : string.Empty;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return Failed($"metadata.json cannot be read: {exception.Message}");
		}

		if (string.IsNullOrWhiteSpace(originalText)) return new MetadataDocumentRepairResult();

		JsonObject? document;
		try
		{
			document = JsonNode.Parse(originalText) as JsonObject;
		}
		catch (JsonException exception)
		{
			return Failed($"metadata.json is not valid JSON{DescribeLocation(exception)}: {exception.Message}");
		}

		if (document == null) return Failed("metadata.json does not hold an object.");

		JsonObject? schema = LoadSchema();
		var removedMembers = new List<string>();
		if (schema != null)
		{
			RemoveUnknownMembers(document, schema, schema, string.Empty, removedMembers);
		}

		MapMetadata metadata;
		try
		{
			metadata = MapFileService.LoadMetadataFromJson(document.ToJsonString());
		}
		catch (JsonException exception)
		{
			return Failed($"metadata.json cannot be read by this build{DescribeLocation(exception)}: {exception.Message}");
		}

		bool carriedSignature = !string.IsNullOrWhiteSpace(metadata.Signature);
		fillDefaults(metadata);
		metadata.Signature = null;

		string canonicalJson = MapFileService.SaveMetadataToJson(metadata);
		var addedMembers = new List<string>();
		JsonNode? canonicalDocument = JsonNode.Parse(canonicalJson);
		if (schema != null && canonicalDocument != null)
		{
			CollectMissingMembers(canonicalDocument, document, schema, schema, string.Empty, addedMembers);
		}

		bool changed = removedMembers.Count > 0 || addedMembers.Count > 0;
		if (changed)
		{
			string lineEnding = DetectLineEnding(originalText);
			string textToWrite = lineEnding == "\r\n"
				? canonicalJson.Replace("\r\n", "\n").Replace("\n", "\r\n")
				: canonicalJson.Replace("\r\n", "\n");
			try
			{
				MapFileService.WriteAllTextAtomically(metadataPath, textToWrite);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				return Failed($"metadata.json cannot be written: {exception.Message}");
			}
		}

		return new MetadataDocumentRepairResult
		{
			Changed = changed,
			SignatureDropped = changed && carriedSignature,
			RemovedMembers = removedMembers,
			AddedMembers = addedMembers
		};
	}

	private static MetadataDocumentRepairResult Failed(string message) => new() { Success = false, ErrorMessage = message };

	private static string DescribeLocation(JsonException exception)
	{
		string location = string.IsNullOrEmpty(exception.Path) ? string.Empty : $" at {exception.Path}";
		string line = exception.LineNumber is long lineNumber ? $" (line {lineNumber + 1})" : string.Empty;
		return location + line;
	}

	private static string DetectLineEnding(string text)
	{
		for (int index = 0; index < text.Length; index++)
		{
			if (text[index] == '\n') return index > 0 && text[index - 1] == '\r' ? "\r\n" : "\n";
		}

		return "\n";
	}

	private static JsonObject? LoadSchema()
	{
		try
		{
			return JsonNode.Parse(MapMetadata.GenerateJsonSchema()) as JsonObject;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static void RemoveUnknownMembers(JsonObject document, JsonObject schema, JsonObject rootSchema, string path, List<string> removed)
	{
		schema = ResolveSchema(schema, rootSchema);
		JsonObject? properties = schema["properties"] as JsonObject;
		JsonNode? additionalProperties = schema["additionalProperties"];
		bool closed = additionalProperties is JsonValue additionalValue
		              && additionalValue.TryGetValue(out bool allowed)
		              && !allowed;

		var unknownMembers = new List<string>();
		foreach (KeyValuePair<string, JsonNode?> member in document)
		{
			string memberPath = JoinPath(path, member.Key);
			if (properties?[member.Key] is JsonObject declaredSchema)
			{
				VisitUnknownMembers(member.Value, declaredSchema, rootSchema, memberPath, removed);
				continue;
			}

			if (additionalProperties is JsonObject dictionaryValueSchema)
			{
				VisitUnknownMembers(member.Value, dictionaryValueSchema, rootSchema, memberPath, removed);
				continue;
			}

			if (closed)
			{
				unknownMembers.Add(member.Key);
				removed.Add(memberPath);
			}
		}

		foreach (string memberKey in unknownMembers)
		{
			document.Remove(memberKey);
		}
	}

	private static void VisitUnknownMembers(JsonNode? node, JsonObject schema, JsonObject rootSchema, string path, List<string> removed)
	{
		if (node is JsonObject memberObject)
		{
			RemoveUnknownMembers(memberObject, schema, rootSchema, path, removed);
			return;
		}

		if (node is JsonArray memberArray)
		{
			schema = ResolveSchema(schema, rootSchema);
			if (schema["items"] is not JsonObject itemSchema) return;
			for (int index = 0; index < memberArray.Count; index++)
			{
				VisitUnknownMembers(memberArray[index], itemSchema, rootSchema, $"{path}[{index}]", removed);
			}
		}
	}

	private static void CollectMissingMembers(JsonNode? canonical, JsonNode? original, JsonObject schema, JsonObject rootSchema, string path, List<string> added)
	{
		schema = ResolveSchema(schema, rootSchema);
		JsonObject? properties = schema["properties"] as JsonObject;

		if (canonical is JsonObject canonicalObject)
		{
			JsonObject? originalObject = original as JsonObject;
			foreach (KeyValuePair<string, JsonNode?> member in canonicalObject)
			{
				string memberPath = JoinPath(path, member.Key);
				if (properties?[member.Key] is JsonObject declaredSchema)
				{
					if (originalObject == null || !originalObject.ContainsKey(member.Key))
					{
						added.Add(memberPath);
						continue;
					}

					CollectMissingMembers(member.Value, originalObject[member.Key], declaredSchema, rootSchema, memberPath, added);
					continue;
				}

				if (schema["additionalProperties"] is JsonObject dictionaryValueSchema && originalObject != null)
				{
					CollectMissingMembers(member.Value, originalObject[member.Key], dictionaryValueSchema, rootSchema, memberPath, added);
				}
			}

			return;
		}

		if (canonical is JsonArray canonicalArray
		    && original is JsonArray originalArray
		    && canonicalArray.Count == originalArray.Count
		    && schema["items"] is JsonObject itemSchema)
		{
			for (int index = 0; index < canonicalArray.Count; index++)
			{
				CollectMissingMembers(canonicalArray[index], originalArray[index], itemSchema, rootSchema, $"{path}[{index}]", added);
			}
		}
	}

	private static JsonObject ResolveSchema(JsonObject schema, JsonObject rootSchema)
	{
		JsonObject current = schema;
		for (int depth = 0; depth < MaximumReferenceDepth; depth++)
		{
			if (current["$ref"] is not JsonValue referenceValue || !referenceValue.TryGetValue(out string? reference)) return current;
			if (string.IsNullOrEmpty(reference) || !reference.StartsWith(SchemaReferencePrefix, StringComparison.Ordinal)) return current;

			JsonNode? target = rootSchema;
			foreach (string rawToken in reference[SchemaReferencePrefix.Length..].Split('/'))
			{
				string token = rawToken.Replace("~1", "/").Replace("~0", "~");
				target = target?[token];
			}

			if (target is not JsonObject targetSchema) return current;
			current = targetSchema;
		}

		return current;
	}

	private static string JoinPath(string path, string member) => path.Length == 0 ? "$." + member : path + "." + member;
}
