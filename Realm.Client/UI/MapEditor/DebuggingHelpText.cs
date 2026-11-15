using Godot;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// The shared explanation of what the map-script debugger can inspect, used by the editor manual and the test-map console.
/// </summary>
public static class DebuggingHelpText
{
	/// <summary>Answers the heading shown before the debugging explanation.</summary>
	public static string GetTitle()
	{
		return TranslationServer.Translate("DEBUGGING MAP SCRIPTS").ToString();
	}

	/// <summary>Answers the translated explanation lines shown in the editor manual and the test-map console.</summary>
	public static string[] GetLines()
	{
		return new[]
		{
			TranslationServer.Translate("The debugger attaches to the authority process that runs your map script.").ToString(),
			TranslationServer.Translate("It can inspect only map script code and the data that code owns inside the WASM module.").ToString(),
			TranslationServer.Translate("Engine APIs and the authority's C# objects live across the WASM boundary; stepping into them or evaluating them requires running host threads, which a paused debugger cannot do.").ToString(),
			TranslationServer.Translate("Hovering such a property shows \"Inspection requires threads to execute. Copy value to local variable to inspect.\" Copy the value into a local variable first if you need to inspect it.").ToString(),
			TranslationServer.Translate("Standard F9/F10 breakpoints and stepping work on map script lines. Engine-side calls appear as a single step and are not inspectable until their result is stored in a local variable.").ToString()
		};
	}
}
