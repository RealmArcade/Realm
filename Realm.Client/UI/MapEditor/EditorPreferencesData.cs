using Realm.Client.Services.MapHistory;

namespace Realm.Client.UI.MapEditor;

public class EditorPreferencesData
{
	public bool HideChromeBorderOverlay { get; set; } = false;
	public bool HideHudDuringToolUsage { get; set; } = true;
	public float PanelOpacity { get; set; } = 0.95f;
	public int AutoBackupIntervalMinutes { get; set; } = 5;
	public bool CleanUpHistoryAutomatically { get; set; } = true;
	public int HistoryKeepDays { get; set; } = 60;
	public int HistoryKeepVersions { get; set; } = 200;

	public MapHistoryRetention ToHistoryRetention()
	{
		return new MapHistoryRetention
		{
			KeepDays = HistoryKeepDays,
			KeepVersions = HistoryKeepVersions
		};
	}

	public EditorPreferencesData Clone()
	{
		return new EditorPreferencesData
		{
			HideChromeBorderOverlay = this.HideChromeBorderOverlay,
			HideHudDuringToolUsage = this.HideHudDuringToolUsage,
			PanelOpacity = this.PanelOpacity,
			AutoBackupIntervalMinutes = this.AutoBackupIntervalMinutes,
			CleanUpHistoryAutomatically = this.CleanUpHistoryAutomatically,
			HistoryKeepDays = this.HistoryKeepDays,
			HistoryKeepVersions = this.HistoryKeepVersions
		};
	}
}
