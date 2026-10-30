namespace Realm.MapAPI;

using System;

/// <summary>
/// Marks a map script type as a curated export that map authors can discover and import as a
/// whole unit through the map editor's "Import From Map" code list instead of hunting through
/// namespaces and individual classes.
/// </summary>
/// <remarks>
/// The attribute is metadata only: it does not change how the type is compiled or executed. The
/// map editor reads it from the source text of the originating map and lists the marked type in the
/// "Exported" scope of the code importer, where the supplied display name and category replace the
/// raw fully qualified type name.
/// </remarks>
/// <example>
/// <code>
/// [Export("Melee Rules", "Rules")]
/// public class MeleeRules
/// {
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum, AllowMultiple = false, Inherited = false)]
public class ExportAttribute : Attribute
{
	/// <summary>
	/// Creates an export marker.
	/// </summary>
	/// <param name="displayName">
	/// Friendly name shown in the import dialog. When omitted or blank, the importer falls back to the
	/// type's short name.
	/// </param>
	/// <param name="category">
	/// Optional grouping label (for example <c>Rules</c>) shown in front of the friendly name in the
	/// import dialog.
	/// </param>
	public ExportAttribute(string displayName = "", string category = "")
	{
		DisplayName = displayName ?? string.Empty;
		Category = category ?? string.Empty;
	}

	/// <summary>
	/// Friendly name shown in the import dialog, or an empty string to use the type's short name.
	/// </summary>
	public string DisplayName { get; }

	/// <summary>
	/// Optional grouping label shown in front of <see cref="DisplayName"/> in the import dialog.
	/// </summary>
	public string Category { get; }
}
