namespace Realm.Client.Services;

public class SessionState
{
	public bool IsMapEditorMode { get; set; }
	public bool IsTestMode { get; set; } = false;
	public bool ReturningFromTest { get; set; } = false;
}