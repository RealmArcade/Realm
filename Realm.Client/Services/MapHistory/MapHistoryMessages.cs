using Godot;

namespace Realm.Client.Services.MapHistory;

public class MapHistoryMessages
{
	private MapHistoryMessages(StringName locale)
	{
		Locale = locale;
		NoMapFolderOpen = TranslationServer.Translate("No map folder is open.");
		NoMetadataYet = TranslationServer.Translate("This folder has no metadata.json yet.");
		HistoryUnreadableReset = TranslationServer.Translate("The stored version history of this map could not be read, so a new one was started. The previous files were kept in the history folder.");
		NoVersionsYet = TranslationServer.Translate("This map has no versions yet.");
		VersionUnreadable = TranslationServer.Translate("That version can no longer be read.");
		HistoryCouldNotBeOpened = TranslationServer.Translate("This map's version history could not be opened.");
		FileBeingWritten = TranslationServer.Translate("'{0}' was being written by another program while the version was created. Try again in a moment.");
		VersionIsMissingFile = TranslationServer.Translate("That version is missing '{0}', so the map was left untouched.");
		RestoreFailed = TranslationServer.Translate("Could not restore the map: {0}. The map was put back the way it was.");
		HistoryUnavailableInBuild = TranslationServer.Translate("Version history is not available in this build. Saving still works, but earlier versions cannot be opened.");
		NoHistoryFolderChosen = TranslationServer.Translate("No history folder was chosen.");
		NotPartOfHistoryStorage = TranslationServer.Translate("That folder is not part of the map history storage.");
		OpenMapUsesThisHistory = TranslationServer.Translate("The map you have open is using this history right now.");
		AnotherWindowUsesThisHistory = TranslationServer.Translate("Another editor window is using this history right now.");
		HistoryFolderNotRemoved = TranslationServer.Translate("The history folder could not be removed completely.");
		HistoryStillBusy = TranslationServer.Translate("The version history is still saving another version. Try again in a moment.");
	}

	public StringName Locale { get; }
	public string NoMapFolderOpen { get; }
	public string NoMetadataYet { get; }
	public string HistoryUnreadableReset { get; }
	public string NoVersionsYet { get; }
	public string VersionUnreadable { get; }
	public string HistoryCouldNotBeOpened { get; }
	public string FileBeingWritten { get; }
	public string VersionIsMissingFile { get; }
	public string RestoreFailed { get; }
	public string HistoryUnavailableInBuild { get; }
	public string NoHistoryFolderChosen { get; }
	public string NotPartOfHistoryStorage { get; }
	public string OpenMapUsesThisHistory { get; }
	public string AnotherWindowUsesThisHistory { get; }
	public string HistoryFolderNotRemoved { get; }
	public string HistoryStillBusy { get; }

	public static MapHistoryMessages Capture()
	{
		return new MapHistoryMessages(TranslationServer.GetLocale());
	}

	public static MapHistoryMessages KeepCurrent(MapHistoryMessages current)
	{
		if (current != null && current.Locale == TranslationServer.GetLocale())
		{
			return current;
		}

		return Capture();
	}
}
