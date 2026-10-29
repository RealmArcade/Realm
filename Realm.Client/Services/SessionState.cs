using Realm.Client.Core;

namespace Realm.Client.Services;

public class SessionState
{
	public bool IsMapEditorMode { get; set; }
	public bool IsTestMode { get; set; } = false;
	public bool ReturningFromTest { get; set; } = false;

	public MapStateSnapshot EditorWorldState { get; set; }
	public bool SavedEditorHasUnsavedChanges { get; set; } = false;
	public Godot.Vector3 SavedCameraPosition { get; set; }
	public float SavedTargetHeight { get; set; }
	public float SavedCurrentHeight { get; set; }
	public float SavedTargetYaw { get; set; }
	public float SavedCurrentYaw { get; set; }
	public float SavedTargetPitch { get; set; }
	public float SavedCurrentPitch { get; set; }
	public bool SavedIsTopDown { get; set; }
	public float SavedYawSwing { get; set; }
	public float SavedPitchSwing { get; set; }
	public GridOverlayMode SavedGridMode { get; set; } = GridOverlayMode.Off;
	public bool SavedDisableShadows { get; set; } = false;
	public string SavedEntityCategory { get; set; } = "";
	public float SavedBrushRadius { get; set; } = 2f;
	public float SavedBrushStrength { get; set; } = 0.5f;
	public EditorTool SavedActiveTool { get; set; } = EditorTool.Raise;
	public string SavedActivePlaceId { get; set; } = "";
	public bool SavedCameraBoundsVisible { get; set; } = false;
	public float SavedTextureIntensity { get; set; } = 10f;
}