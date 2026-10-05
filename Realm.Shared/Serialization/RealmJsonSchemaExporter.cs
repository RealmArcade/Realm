using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace Realm.Shared.Serialization;

public static class RealmJsonSchemaExporter
{
	private static readonly JsonSerializerOptions SchemaSerializerOptions = new(JsonSerializerOptions.Default)
	{
		TypeInfoResolver = new DefaultJsonTypeInfoResolver
		{
			Modifiers =
			{
				typeInfo =>
				{
					for (int i = typeInfo.Properties.Count - 1; i >= 0; i--)
					{
						var property = typeInfo.Properties[i];
						if (property.AttributeProvider?.GetCustomAttributes(typeof(ObsoleteAttribute), false).Length > 0)
						{
							typeInfo.Properties.RemoveAt(i);
						}
					}
				}
			}
		}
	};

	private static readonly JsonSerializerOptions IndentedPrintOptions = new()
	{
		WriteIndented = true
	};

	public static string GenerateJsonSchema(Type type)
	{
		var schemaNode = JsonSchemaExporter.GetJsonSchemaAsNode(SchemaSerializerOptions, type);
		return schemaNode.ToJsonString(IndentedPrintOptions) + Environment.NewLine;
	}
}
