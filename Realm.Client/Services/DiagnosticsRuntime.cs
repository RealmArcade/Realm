using Godot;

namespace Realm.Client.Services;

/// <summary>
///     Installs the client's crash capture and keeps the log folder bounded before anything else runs. It comes up with the
///     tree so a failure during startup is recorded, which is the window that produces the reports worth having.
/// </summary>
[GlobalClass]
public partial class DiagnosticsRuntime : Node
{
	public override void _Ready()
	{
		DiagnosticsService.InstallCrashHandlers("client");
		DiagnosticsService.TrimLogFolder();
	}
}
