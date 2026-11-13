using System;

namespace Realm.Shared.Serialization;

/// <summary>
///     The editor affordance a schema member is best rendered with, emitted as the <c>x-realm-control</c> keyword.
/// </summary>
public enum RealmSchemaControl
{
	/// <summary>A colour value, rendered with a colour picker.</summary>
	Color,

	/// <summary>A list of free-form labels, rendered as removable tag chips.</summary>
	Tags,

	/// <summary>An integer whose bits carry meaning, rendered as a set of named checkboxes.</summary>
	Flags,

	/// <summary>A free-form string long enough to want several lines.</summary>
	Textarea,

	/// <summary>A string that holds a JSON document, rendered with syntax checking.</summary>
	Json,

	/// <summary>A free-form string that reads best as a single line of prose.</summary>
	Text,

	/// <summary>A path to a file, rendered with a file picker restricted to the declared kinds.</summary>
	Asset,

	/// <summary>A number the author picks by dragging between two bounds.</summary>
	Slider
}

/// <summary>
///     Declares the sentence a human reads beside a member, an object type or an enum value. The generator emits it as
///     the <c>description</c> keyword, which is the only place the schema says what a value means rather than what type
///     it has.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum)]
public class SchemaDescriptionAttribute : Attribute
{
	public SchemaDescriptionAttribute(string description)
	{
		Description = description;
	}

	/// <summary>The sentence shown on hover and in completion detail.</summary>
	public string Description { get; }
}

/// <summary>
///     Overrides the label a consumer shows for a member, emitted as the <c>title</c> keyword. A member without this
///     declaration is titled with its JSON name.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum)]
public class SchemaTitleAttribute : Attribute
{
	public SchemaTitleAttribute(string title)
	{
		Title = title;
	}

	/// <summary>The label shown in completion lists and as the hover heading.</summary>
	public string Title { get; }
}

/// <summary>
///     Declares that a member names records held by the given template sheets of the same document, emitted as part of
///     the <c>x-realm-reference</c> keyword. Several sheets in one declaration mean the member may name a record from
///     any of them; several declarations on one member mean the member names one of several kinds of thing.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public class SchemaReferenceAttribute : Attribute
{
	public SchemaReferenceAttribute(params string[] sheets)
	{
		Sheets = sheets;
	}

	/// <summary>The template sheet names this member may point into.</summary>
	public string[] Sheets { get; }
}

/// <summary>
///     Declares that a member names a key of a dictionary in the same document rather than a template record, emitted as
///     part of the <c>x-realm-reference</c> keyword so a consumer can tell the two apart.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public class SchemaDictionaryReferenceAttribute : Attribute
{
	public SchemaDictionaryReferenceAttribute(string dictionary)
	{
		Dictionary = dictionary;
	}

	/// <summary>The JSON name of the dictionary whose keys this member names.</summary>
	public string Dictionary { get; }
}

/// <summary>
///     Declares that a member holds a path to a file, emitted as the <c>x-realm-asset</c> keyword together with the file
///     kinds it accepts and the folders those files live in.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public class SchemaAssetAttribute : Attribute
{
	public SchemaAssetAttribute(string[] kinds, string[] folders)
	{
		Kinds = kinds;
		Folders = folders;
	}

	/// <summary>The canonical file extensions, without a leading dot, the member accepts.</summary>
	public string[] Kinds { get; }

	/// <summary>The map folders the accepted kinds live in.</summary>
	public string[] Folders { get; }
}

/// <summary>
///     Declares the affordance a consumer should render a member with when its type, references and assets do not
///     already answer the question. Emitted as the <c>x-realm-control</c> keyword.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class SchemaControlAttribute : Attribute
{
	public SchemaControlAttribute(RealmSchemaControl control)
	{
		Control = control;
	}

	/// <summary>The affordance to render.</summary>
	public RealmSchemaControl Control { get; }
}

/// <summary>
///     Declares the unit a numeric member is measured in, emitted as the <c>x-realm-unit</c> keyword so a consumer never
///     shows a bare number where a percentage, a duration, a distance or an angle was meant.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class SchemaUnitAttribute : Attribute
{
	public SchemaUnitAttribute(string unit)
	{
		Unit = unit;
	}

	/// <summary>The unit symbol, such as <c>%</c>, <c>s</c>, <c>m</c> or <c>°</c>.</summary>
	public string Unit { get; }
}

/// <summary>
///     Declares one named bit of an integer member, emitted as part of the <c>x-realm-flags</c> keyword. Every bit the
///     member carries meaning for is declared once; a consumer renders the declared bits and shows any other set bit as
///     a number rather than dropping it.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public class SchemaFlagAttribute : Attribute
{
	public SchemaFlagAttribute(int value, string label)
	{
		Value = value;
		Label = label;
	}

	/// <summary>The value of the bit, a single power of two.</summary>
	public int Value { get; }

	/// <summary>The label shown for the bit.</summary>
	public string Label { get; }
}

/// <summary>
///     Declares one named value of an integer member that is a choice between whole numbers rather than a bitmask,
///     emitted as part of the <c>x-realm-enum-labels</c> keyword.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public class SchemaEnumLabelAttribute : Attribute
{
	public SchemaEnumLabelAttribute(int value, string label)
	{
		Value = value;
		Label = label;
	}

	/// <summary>The number the member stores for this choice.</summary>
	public int Value { get; }

	/// <summary>The label shown for the choice.</summary>
	public string Label { get; }
}

/// <summary>
///     Declares the bounds a numeric member is clamped to, emitted as the <c>minimum</c> and <c>maximum</c> keywords so
///     validation is the schema's job rather than a consumer's.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class SchemaRangeAttribute : Attribute
{
	public SchemaRangeAttribute(double minimum, double maximum)
	{
		Minimum = minimum;
		Maximum = maximum;
	}

	/// <summary>The smallest accepted value.</summary>
	public double Minimum { get; }

	/// <summary>The largest accepted value.</summary>
	public double Maximum { get; }
}

/// <summary>
///     Declares that a string member's exact content matters, emitted as the <c>x-realm-sensitive</c> keyword so a
///     consumer renders it in a monospace face and never trims it on save.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class SchemaSensitiveAttribute : Attribute
{
}

/// <summary>
///     Declares that a member mirrors a value the document does not own, emitted as the <c>x-realm-derived</c> keyword
///     together with the name of the value it mirrors so a consumer renders it read-only.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class SchemaDerivedAttribute : Attribute
{
	public SchemaDerivedAttribute(string source)
	{
		Source = source;
	}

	/// <summary>The name of the value this member is derived from.</summary>
	public string Source { get; }
}
