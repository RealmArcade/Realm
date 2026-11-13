using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Realm.Shared.Serialization;

/// <summary>
///     The canonical keyword vocabulary a Realm JSON schema is allowed to carry, so the transform cannot invent one and
///     a consumer can enumerate what it may meet. The <c>x-realm-*</c> keywords are documented in
///     <c>RealmJsonSchemaExporter</c>; the remaining entries are the standard JSON Schema keywords the exporter emits or
///     that the vocabulary adds.
/// </summary>
public static class RealmSchemaKeywords
{
	public const string Description = "description";
	public const string Title = "title";
	public const string Enum = "enum";
	public const string EnumDescriptions = "enumDescriptions";
	public const string AdditionalProperties = "additionalProperties";
	public const string Minimum = "minimum";
	public const string Maximum = "maximum";
	public const string EnumLabels = "x-realm-enum-labels";
	public const string Reference = "x-realm-reference";
	public const string Asset = "x-realm-asset";
	public const string Control = "x-realm-control";
	public const string Unit = "x-realm-unit";
	public const string Flags = "x-realm-flags";
	public const string Sensitive = "x-realm-sensitive";
	public const string Derived = "x-realm-derived";
	public const string SchemaVersion = "x-realm-schema-version";

	/// <summary>Every <c>x-realm-*</c> keyword the generator is allowed to emit.</summary>
	public static readonly string[] RealmKeywords =
	{
		EnumLabels,
		Reference,
		Asset,
		Control,
		Unit,
		Flags,
		Sensitive,
		Derived,
		SchemaVersion
	};

	/// <summary>Every keyword the generated schema may carry at all.</summary>
	public static readonly string[] CanonicalKeywords =
	{
		"type",
		"properties",
		"items",
		"required",
		"anyOf",
		"$schema",
		"$ref",
		"$defs",
		Description,
		Title,
		Enum,
		EnumDescriptions,
		AdditionalProperties,
		Minimum,
		Maximum,
		EnumLabels,
		Reference,
		Asset,
		Control,
		Unit,
		Flags,
		Sensitive,
		Derived,
		SchemaVersion
	};
}

/// <summary>
///     One generated schema and everything the generator wants to report about it.
/// </summary>
/// <param name="Json">The exact text written to the schema file.</param>
/// <param name="MissingDescriptions">One entry per model member, object type and enum value that carries no description.</param>
public class RealmSchemaDocument
{
	public RealmSchemaDocument(string json, IReadOnlyList<string> missingDescriptions)
	{
		Json = json;
		MissingDescriptions = missingDescriptions;
	}

	/// <summary>The exact text written to the schema file.</summary>
	public string Json { get; }

	/// <summary>One entry per member, object type and enum value that carries no description.</summary>
	public IReadOnlyList<string> MissingDescriptions { get; }
}

/// <summary>
///     Turns a strongly typed Realm model into its JSON schema.
/// </summary>
/// <remarks>
///     <para>
///         Structural shape comes from <see cref="JsonSchemaExporter" /> reflecting over the model, so a new property
///         arrives with its type, its <c>[JsonPropertyName]</c> and its object and array shapes without the generator
///         knowing about it. Meaning comes from the attributes in <see cref="RealmSchemaVocabulary" /> declared beside
///         that property, which the transform below turns into keywords.
///     </para>
///     <para>
///         The transform is the only code that knows how a declaration is spelled. It emits
///         <c>description</c> (member, object type and enum value), <c>title</c> (declared, else the JSON name),
///         <c>enum</c> and <c>enumDescriptions</c> for a string-serialized enum, <c>x-realm-enum-labels</c> for a numeric
///         enum or an integer member with declared names, <c>x-realm-reference</c>, <c>x-realm-asset</c>,
///         <c>x-realm-control</c>, <c>x-realm-unit</c>, <c>x-realm-flags</c>, <c>x-realm-sensitive</c>,
///         <c>x-realm-derived</c>, <c>minimum</c>/<c>maximum</c> from a declared range, and
///         <c>additionalProperties: false</c> for every strongly typed object. <c>x-realm-schema-version</c> is written
///         once, at the root. Nothing outside <see cref="RealmSchemaKeywords.CanonicalKeywords" /> is ever emitted, and
///         a generated document is checked against that list before it is returned.
///     </para>
///     <para>
///         Numeric enum labels are derived by one rule: the label of a value is the name of the enum member that declares
///         it, in declaration order, and the value is the number the member stores. A label is declared explicitly only
///         where the meaning is not obvious from the member name, which for the metadata model is the pathing bitmasks.
///     </para>
///     <para>
///         Determinism is a contract, not a coincidence. Property order follows model declaration order, keywords are
///         inserted in a fixed order, indentation and line endings are pinned to two spaces and LF, and no unordered
///         collection reaches the writer. Regenerating an unchanged model therefore produces a byte-identical file on
///         every machine.
///     </para>
/// </remarks>
public static class RealmJsonSchemaExporter
{
	/// <summary>
	///     The version of the keyword vocabulary carried by every generated document as
	///     <c>x-realm-schema-version</c>. It is informational: a consumer uses it to report that a schema is too old for a
	///     feature it has. It never selects between two behaviours, because there are no fallbacks.
	/// </summary>
	public const int VocabularyVersion = 1;

	private static readonly JsonSerializerOptions SchemaSerializerOptions = new(JsonSerializerOptions.Default)
	{
		TypeInfoResolver = new DefaultJsonTypeInfoResolver
		{
			Modifiers = { PruneObsoleteProperties }
		}
	};

	private static readonly JsonSerializerOptions IndentedPrintOptions = new()
	{
		WriteIndented = true,
		NewLine = "\n",
		IndentCharacter = ' ',
		IndentSize = 2,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	/// <summary>
	///     Generates the schema of a model type as the exact text that belongs in the committed schema file.
	/// </summary>
	/// <param name="type">The root model type.</param>
	/// <returns>The schema document, including its vocabulary version and its trailing newline.</returns>
	public static string GenerateJsonSchema(Type type)
	{
		return GenerateSchema(type).Json;
	}

	/// <summary>
	///     Generates the schema of a model type together with the report of what the model failed to declare.
	/// </summary>
	/// <param name="type">The root model type.</param>
	/// <returns>The generated document and its missing-description report.</returns>
	public static RealmSchemaDocument GenerateSchema(Type type)
	{
		var missingDescriptions = new List<string>();
		var exporterOptions = new JsonSchemaExporterOptions
		{
			TransformSchemaNode = (context, schema) => ApplyVocabulary(context, schema, type, missingDescriptions)
		};

		JsonNode? generated = JsonSchemaExporter.GetJsonSchemaAsNode(SchemaSerializerOptions, type, exporterOptions);
		if (generated is null)
		{
			throw new InvalidOperationException($"The schema exporter produced nothing for '{type.FullName}'.");
		}

		EnsureCanonicalKeywords(generated, type);

		string text = generated.ToJsonString(IndentedPrintOptions) + "\n";
		string[] report = missingDescriptions.Distinct(StringComparer.Ordinal).OrderBy(entry => entry, StringComparer.Ordinal).ToArray();
		return new RealmSchemaDocument(text, report);
	}

	/// <summary>
	///     Reports whether regenerating the model still produces the committed text, which is what a build checks so a
	///     hand-edited or stale schema cannot be committed.
	/// </summary>
	/// <param name="type">The root model type.</param>
	/// <param name="committedSchemaPath">The committed schema file path.</param>
	/// <returns>Null when the committed file matches, otherwise the reason it does not.</returns>
	public static string? FindSchemaDrift(Type type, string committedSchemaPath)
	{
		if (!File.Exists(committedSchemaPath))
		{
			return $"{committedSchemaPath} does not exist.";
		}

		RealmSchemaDocument first = GenerateSchema(type);
		RealmSchemaDocument second = GenerateSchema(type);
		if (!string.Equals(first.Json, second.Json, StringComparison.Ordinal))
		{
			return $"{Path.GetFileName(committedSchemaPath)} is not deterministic: two runs over the unchanged model differ.";
		}

		string committed = File.ReadAllText(committedSchemaPath);
		if (string.Equals(committed, first.Json, StringComparison.Ordinal))
		{
			return null;
		}

		return $"{Path.GetFileName(committedSchemaPath)} is stale: regenerating the model would change it.";
	}

	private static void PruneObsoleteProperties(JsonTypeInfo typeInfo)
	{
		for (int index = typeInfo.Properties.Count - 1; index >= 0; index--)
		{
			JsonPropertyInfo property = typeInfo.Properties[index];
			if (property.AttributeProvider?.GetCustomAttributes(typeof(ObsoleteAttribute), false).Length > 0)
			{
				typeInfo.Properties.RemoveAt(index);
			}
		}
	}

	private static JsonNode ApplyVocabulary(JsonSchemaExporterContext context, JsonNode schema, Type rootType, List<string> missingDescriptions)
	{
		JsonPropertyInfo? property = context.PropertyInfo;
		Type exportedType = context.TypeInfo.Type;
		Type valueType = Nullable.GetUnderlyingType(exportedType) ?? exportedType;

		string? description = ReadDescription(property?.AttributeProvider) ?? (property is null ? ReadDescription(exportedType) : null);
		string? title = ReadTitle(property?.AttributeProvider) ?? property?.Name;

		if (description is null && DeservesDescription(property, context.TypeInfo, valueType))
		{
			missingDescriptions.Add(DescribeMissing(property, exportedType));
		}

		if (schema.GetValueKind() == JsonValueKind.True)
		{
			return BuildUntypedNode(description, title);
		}

		if (schema is not JsonObject node)
		{
			return schema;
		}

		if (description is not null && !node.ContainsKey(RealmSchemaKeywords.Description))
		{
			node[RealmSchemaKeywords.Description] = description;
		}

		if (title is not null && !node.ContainsKey(RealmSchemaKeywords.Title))
		{
			node[RealmSchemaKeywords.Title] = title;
		}

		if (context.TypeInfo.Type == rootType && property is null)
		{
			node[RealmSchemaKeywords.SchemaVersion] = VocabularyVersion;
		}

		CloseStronglyTypedObject(context, node);

		if (valueType.IsEnum)
		{
			ApplyEnumVocabulary(node, valueType);
		}

		if (property is not null)
		{
			ApplyMemberVocabulary(node, property, valueType);
		}

		return InCanonicalKeywordOrder(node);
	}

	private static JsonNode BuildUntypedNode(string? description, string? title)
	{
		if (description is null && title is null)
		{
			return JsonValue.Create(true)!;
		}

		var node = new JsonObject();
		if (title is not null)
		{
			node[RealmSchemaKeywords.Title] = title;
		}

		if (description is not null)
		{
			node[RealmSchemaKeywords.Description] = description;
		}

		return node;
	}

	private static void CloseStronglyTypedObject(JsonSchemaExporterContext context, JsonObject node)
	{
		if (context.TypeInfo.Kind != JsonTypeInfoKind.Object)
		{
			return;
		}

		if (!DeclaresObject(node))
		{
			return;
		}

		node[RealmSchemaKeywords.AdditionalProperties] = false;
	}

	private static bool DeclaresObject(JsonObject node)
	{
		if (node.ContainsKey("properties"))
		{
			return true;
		}

		if (node.ContainsKey(RealmSchemaKeywords.AdditionalProperties))
		{
			return true;
		}

		if (!node.TryGetPropertyValue("type", out JsonNode? typeNode))
		{
			return false;
		}

		if (typeNode is JsonArray types)
		{
			foreach (JsonNode? candidate in types)
			{
				if (candidate is not null && string.Equals(candidate.GetValue<string>(), "object", StringComparison.Ordinal))
				{
					return true;
				}
			}

			return false;
		}

		return typeNode is not null && string.Equals(typeNode.GetValue<string>(), "object", StringComparison.Ordinal);
	}

	private static readonly string[] KeywordOrder =
	{
		"type",
		RealmSchemaKeywords.Title,
		RealmSchemaKeywords.Description,
		RealmSchemaKeywords.Enum,
		RealmSchemaKeywords.EnumDescriptions,
		RealmSchemaKeywords.Minimum,
		RealmSchemaKeywords.Maximum,
		RealmSchemaKeywords.SchemaVersion,
		RealmSchemaKeywords.EnumLabels,
		RealmSchemaKeywords.Flags,
		RealmSchemaKeywords.Unit,
		RealmSchemaKeywords.Control,
		RealmSchemaKeywords.Reference,
		RealmSchemaKeywords.Asset,
		RealmSchemaKeywords.Sensitive,
		RealmSchemaKeywords.Derived,
		"properties",
		"required",
		"items",
		RealmSchemaKeywords.AdditionalProperties,
		"$ref",
		"$defs",
		"anyOf"
	};

	private static JsonObject InCanonicalKeywordOrder(JsonObject node)
	{
		var ordered = new JsonObject();
		foreach (string keyword in KeywordOrder)
		{
			if (node.TryGetPropertyValue(keyword, out JsonNode? value))
			{
				node.Remove(keyword);
				ordered[keyword] = value;
			}
		}

		foreach (KeyValuePair<string, JsonNode?> remainder in node)
		{
			ordered[remainder.Key] = remainder.Value;
		}

		return ordered;
	}

	private static void ApplyEnumVocabulary(JsonObject node, Type enumType)
	{
		FieldInfo[] fields = enumType.GetFields(BindingFlags.Public | BindingFlags.Static);

		if (node.TryGetPropertyValue(RealmSchemaKeywords.Enum, out JsonNode? enumNode) && enumNode is JsonArray enumValues)
		{
			var descriptions = new JsonArray();
			bool described = false;
			foreach (JsonNode? enumValue in enumValues)
			{
				string name = enumValue is null ? string.Empty : enumValue.GetValue<string>();
				string? valueDescription = ReadEnumValueDescription(fields, name);
				described |= valueDescription is not null;
				descriptions.Add(valueDescription ?? string.Empty);
			}

			if (described)
			{
				node[RealmSchemaKeywords.EnumDescriptions] = descriptions;
			}

			return;
		}

		node[RealmSchemaKeywords.EnumLabels] = BuildLabels(fields);
	}

	private static JsonArray BuildLabels(FieldInfo[] fields)
	{
		var labels = new JsonArray();
		foreach (FieldInfo field in fields)
		{
			object? raw = field.GetRawConstantValue();
			if (raw is null)
			{
				continue;
			}

			var entry = new JsonObject
			{
				["value"] = Convert.ToInt64(raw),
				["label"] = field.Name
			};
			labels.Add(entry);
		}

		return labels;
	}

	private static string? ReadEnumValueDescription(FieldInfo[] fields, string name)
	{
		foreach (FieldInfo field in fields)
		{
			if (string.Equals(field.Name, name, StringComparison.Ordinal))
			{
				return ReadDescription(field);
			}
		}

		return null;
	}

	private static void ApplyMemberVocabulary(JsonObject node, JsonPropertyInfo property, Type valueType)
	{
		ICustomAttributeProvider? attributeProvider = property.AttributeProvider;
		if (attributeProvider is null)
		{
			return;
		}

		ApplyReference(node, attributeProvider);
		ApplyAsset(node, attributeProvider);
		ApplyControl(node, attributeProvider);
		ApplyUnit(node, attributeProvider);
		ApplyFlags(node, attributeProvider);
		ApplyEnumLabels(node, attributeProvider);
		ApplyRange(node, attributeProvider);
		ApplySensitive(node, attributeProvider);
		ApplyDerived(node, attributeProvider);
	}

	private static void ApplyReference(JsonObject node, ICustomAttributeProvider attributeProvider)
	{
		var alternatives = new JsonArray();

		foreach (SchemaReferenceAttribute reference in ReadAttributes<SchemaReferenceAttribute>(attributeProvider))
		{
			if (reference.Sheets.Length == 0)
			{
				continue;
			}

			var sheets = new JsonArray();
			foreach (string sheet in reference.Sheets)
			{
				sheets.Add(sheet);
			}

			alternatives.Add(new JsonObject { ["sheets"] = sheets });
		}

		foreach (SchemaDictionaryReferenceAttribute reference in ReadAttributes<SchemaDictionaryReferenceAttribute>(attributeProvider))
		{
			alternatives.Add(new JsonObject { ["dictionary"] = reference.Dictionary });
		}

		if (alternatives.Count == 0)
		{
			return;
		}

		node[RealmSchemaKeywords.Reference] = alternatives.Count == 1 ? alternatives[0]!.DeepClone() : new JsonObject { ["anyOf"] = alternatives };
	}

	private static void ApplyAsset(JsonObject node, ICustomAttributeProvider attributeProvider)
	{
		SchemaAssetAttribute? asset = ReadAttribute<SchemaAssetAttribute>(attributeProvider);
		if (asset is null)
		{
			return;
		}

		var kinds = new JsonArray();
		foreach (string kind in asset.Kinds)
		{
			kinds.Add(kind);
		}

		var folders = new JsonArray();
		foreach (string folder in asset.Folders)
		{
			folders.Add(folder);
		}

		node[RealmSchemaKeywords.Asset] = new JsonObject
		{
			["kinds"] = kinds,
			["folders"] = folders
		};
	}

	private static void ApplyControl(JsonObject node, ICustomAttributeProvider attributeProvider)
	{
		SchemaControlAttribute? control = ReadAttribute<SchemaControlAttribute>(attributeProvider);
		if (control is null)
		{
			return;
		}

		node[RealmSchemaKeywords.Control] = ToControlKeyword(control.Control);
	}

	private static string ToControlKeyword(RealmSchemaControl control)
	{
		return control switch
		{
			RealmSchemaControl.Color => "color",
			RealmSchemaControl.Tags => "tags",
			RealmSchemaControl.Flags => "flags",
			RealmSchemaControl.Textarea => "textarea",
			RealmSchemaControl.Json => "json",
			RealmSchemaControl.Text => "text",
			RealmSchemaControl.Asset => "asset",
			RealmSchemaControl.Slider => "slider",
			_ => throw new ArgumentOutOfRangeException(nameof(control), control, "The control has no schema keyword.")
		};
	}

	private static void ApplyUnit(JsonObject node, ICustomAttributeProvider attributeProvider)
	{
		SchemaUnitAttribute? unit = ReadAttribute<SchemaUnitAttribute>(attributeProvider);
		if (unit is null)
		{
			return;
		}

		node[RealmSchemaKeywords.Unit] = unit.Unit;
	}

	private static void ApplyFlags(JsonObject node, ICustomAttributeProvider attributeProvider)
	{
		SchemaFlagAttribute[] flags = ReadAttributes<SchemaFlagAttribute>(attributeProvider);
		if (flags.Length == 0)
		{
			return;
		}

		var entries = new JsonArray();
		foreach (SchemaFlagAttribute flag in flags)
		{
			entries.Add(new JsonObject
			{
				["value"] = flag.Value,
				["label"] = flag.Label
			});
		}

		node[RealmSchemaKeywords.Flags] = entries;
	}

	private static void ApplyEnumLabels(JsonObject node, ICustomAttributeProvider attributeProvider)
	{
		SchemaEnumLabelAttribute[] labels = ReadAttributes<SchemaEnumLabelAttribute>(attributeProvider);
		if (labels.Length == 0)
		{
			return;
		}

		var entries = new JsonArray();
		foreach (SchemaEnumLabelAttribute label in labels)
		{
			entries.Add(new JsonObject
			{
				["value"] = label.Value,
				["label"] = label.Label
			});
		}

		node[RealmSchemaKeywords.EnumLabels] = entries;
	}

	private static void ApplyRange(JsonObject node, ICustomAttributeProvider attributeProvider)
	{
		SchemaRangeAttribute? range = ReadAttribute<SchemaRangeAttribute>(attributeProvider);
		if (range is null)
		{
			return;
		}

		node[RealmSchemaKeywords.Minimum] = range.Minimum;
		node[RealmSchemaKeywords.Maximum] = range.Maximum;
	}

	private static void ApplySensitive(JsonObject node, ICustomAttributeProvider attributeProvider)
	{
		if (ReadAttribute<SchemaSensitiveAttribute>(attributeProvider) is null)
		{
			return;
		}

		node[RealmSchemaKeywords.Sensitive] = true;
	}

	private static void ApplyDerived(JsonObject node, ICustomAttributeProvider attributeProvider)
	{
		SchemaDerivedAttribute? derived = ReadAttribute<SchemaDerivedAttribute>(attributeProvider);
		if (derived is null)
		{
			return;
		}

		node[RealmSchemaKeywords.Derived] = derived.Source;
	}

	private static bool DeservesDescription(JsonPropertyInfo? property, JsonTypeInfo typeInfo, Type valueType)
	{
		if (property is not null)
		{
			return true;
		}

		return valueType.IsEnum || typeInfo.Kind == JsonTypeInfoKind.Object;
	}

	private static string DescribeMissing(JsonPropertyInfo? property, Type exportedType)
	{
		if (property is null)
		{
			return $"type {exportedType.FullName} has no description";
		}

		string declaringType = property.DeclaringType.FullName ?? property.DeclaringType.Name;
		return $"member {declaringType}.{property.Name} (\"{property.Name}\") has no description";
	}

	private static string? ReadDescription(MemberInfo? member)
	{
		return member is null ? null : ReadAttribute<SchemaDescriptionAttribute>(member)?.Description;
	}

	private static string? ReadDescription(ICustomAttributeProvider? attributeProvider)
	{
		return ReadAttribute<SchemaDescriptionAttribute>(attributeProvider)?.Description;
	}

	private static string? ReadDescription(Type type)
	{
		return ReadAttribute<SchemaDescriptionAttribute>(type)?.Description;
	}

	private static string? ReadTitle(ICustomAttributeProvider? attributeProvider)
	{
		return attributeProvider is null ? null : ReadAttribute<SchemaTitleAttribute>(attributeProvider)?.Title;
	}

	private static TAttribute? ReadAttribute<TAttribute>(ICustomAttributeProvider? attributeProvider) where TAttribute : Attribute
	{
		if (attributeProvider is null)
		{
			return null;
		}

		var attributes = attributeProvider.GetCustomAttributes(typeof(TAttribute), false);
		return attributes.Length == 0 ? null : (TAttribute)attributes[0];
	}

	private static TAttribute[] ReadAttributes<TAttribute>(ICustomAttributeProvider? attributeProvider) where TAttribute : Attribute
	{
		if (attributeProvider is null)
		{
			return Array.Empty<TAttribute>();
		}

		var attributes = attributeProvider.GetCustomAttributes(typeof(TAttribute), false);
		if (attributes.Length == 0)
		{
			return Array.Empty<TAttribute>();
		}

		var typed = new TAttribute[attributes.Length];
		for (int index = 0; index < attributes.Length; index++)
		{
			typed[index] = (TAttribute)attributes[index];
		}

		return typed;
	}

	private static void EnsureCanonicalKeywords(JsonNode node, Type rootType)
	{
		if (node is not JsonObject schema)
		{
			return;
		}

		foreach (KeyValuePair<string, JsonNode?> pair in schema)
		{
			if (!IsCanonicalKeyword(pair.Key))
			{
				throw new InvalidOperationException($"The schema of '{rootType.FullName}' carries the unknown keyword '{pair.Key}'.");
			}

			switch (pair.Key)
			{
				case "properties":
				case "$defs":
					if (pair.Value is JsonObject nestedSchemas)
					{
						foreach (KeyValuePair<string, JsonNode?> nestedSchema in nestedSchemas)
						{
							if (nestedSchema.Value is not null)
							{
								EnsureCanonicalKeywords(nestedSchema.Value, rootType);
							}
						}
					}

					break;
				case "items":
				case "additionalProperties":
					if (pair.Value is not null)
					{
						EnsureCanonicalKeywords(pair.Value, rootType);
					}

					break;
				default:
					break;
			}
		}
	}

	private static bool IsCanonicalKeyword(string keyword)
	{
		foreach (string canonical in RealmSchemaKeywords.CanonicalKeywords)
		{
			if (string.Equals(canonical, keyword, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}
}
