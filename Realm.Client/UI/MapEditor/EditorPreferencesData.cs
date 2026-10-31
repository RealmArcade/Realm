namespace Realm.Client.UI.MapEditor;

public class EditorPreferencesData
{
	public bool HideChromeBorderOverlay { get; set; } = false;
	public bool HideHudDuringToolUsage { get; set; } = true;
	public float PanelOpacity { get; set; } = 0.95f;
	public int AutoBackupIntervalMinutes { get; set; } = 5;

	public EditorPreferencesData Clone()
	{
		return new EditorPreferencesData
		{
			HideChromeBorderOverlay = this.HideChromeBorderOverlay,
			HideHudDuringToolUsage = this.HideHudDuringToolUsage,
			PanelOpacity = this.PanelOpacity,
			AutoBackupIntervalMinutes = this.AutoBackupIntervalMinutes
		};
	}
}