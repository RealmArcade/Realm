using Godot;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// The two readouts the editor prints while the pointer is on the map: the tape measure's distance/slope line and the
/// paste pivot line. Pure formatting - the callers own the panels and the labels, so this only turns numbers into text.
/// </summary>
public static class MapEditorTelemetryText
{
	public static string Measure(float eucTiles, float eucWorld, float manhattanTiles, float dx, float dz, float dy,
		float angleDeg, float slopePct)
	{
		string text = $"{UnicodeIcons.RULER} " + string.Format(
			TranslationServer.Translate("MEASURE: Manhattan: {0:F1}T | Euclidean: {1:F1}T ({2:F1}m) | Angle: {3:F1}° | Slope: {4}"),
			manhattanTiles, eucTiles, eucWorld, angleDeg, Slope(dx, dz));

		if (Mathf.Abs(dy) > 0.01f)
		{
			text += string.Format(TranslationServer.Translate(" | Elev: {0:+0.0;-0.0;0.0}m ({1:F1}%)"), dy, slopePct);
		}

		return text;
	}

	public static string Paste(int deltaX, int deltaZ, float distTiles, float distWorld, float angleDeg) =>
		string.Format("ΔX: {0:+0;-0;0}, ΔZ: {1:+0;-0;0} | Pivot: {2:F1}T ({3:F1}m) @ {4:F1}°",
			deltaX, deltaZ, distTiles, distWorld, angleDeg);

	/// <summary>
	/// The slope as a ratio the way players read it. A purely vertical drag has no horizontal component and a purely
	/// horizontal one has no rise, so both get their own wording; anything else snaps to a whole ratio when it is within
	/// five percent of one, because a hand-dragged selection never lands on exactly 1:1.
	/// </summary>
	private static string Slope(float dx, float dz)
	{
		float absDx = Mathf.Abs(dx);
		float absDz = Mathf.Abs(dz);

		if (absDx < 0.01f && absDz < 0.01f) return "0:0";
		if (absDx < 0.01f) return TranslationServer.Translate("Vertical");
		if (absDz < 0.01f) return TranslationServer.Translate("Horizontal (1:0)");

		float ratio = absDz / absDx;
		if (Mathf.Abs(ratio - Mathf.Round(ratio)) < 0.05f)
		{
			return $"{Mathf.Round(ratio):0}:1";
		}

		if (Mathf.Abs(1.0f / ratio - Mathf.Round(1.0f / ratio)) < 0.05f)
		{
			return $"1:{Mathf.Round(1.0f / ratio):0}";
		}

		return $"{ratio:F1}:1";
	}
}
