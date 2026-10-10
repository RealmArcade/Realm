using System.Collections.Generic;
using PasteReflection = Realm.Ecs.Components.Core.PasteReflection;

namespace Realm.Client.UI.MapEditor;

public class MapEditorHUDViewModel
{
	public enum EditorModule
	{
		Terrain,
		TextureDeco,
		Pathing,
		Objects,
		Coordinates,
		Clipboard
	}

	public bool LeftPanelExpanded { get; set; } = false;
	public bool RightPanelExpanded { get; set; } = true;
	public EditorModule ActiveModule { get; set; } = EditorModule.Terrain;

	public float BrushSize { get; set; } = 2f;
	public float BrushStrength { get; set; } = 20f;
	public float BlockStep { get; set; } = 3.0f;
	public float ExactHeight { get; set; } = 0.0f;

	public float PlacementRotate { get; set; } = 0f;
	public float PlacementScale { get; set; } = 1.0f;
	public float PasteRotation { get; set; } = 0f;
	public PasteReflection PasteReflection { get; set; } = PasteReflection.None;
	public bool SpawnAsEnemy { get; set; } = false;
	public bool RandomRotation { get; set; } = false;
	public bool RandomScale { get; set; } = false;
	public bool ClumpMode { get; set; } = false;
	public float ClumpCount { get; set; } = 5f;
	public float ClumpDensity { get => ClumpCount; set => ClumpCount = value; }
	public float ClumpScale { get; set; } = 0.3f;
	public float ClumpScaleVar { get => ClumpScale; set => ClumpScale = value; }

	public string CurrentCategory { get; set; } = "Units";
	public List<string> CategoryFiles { get; } = new();
	public int SelectedCategoryItemIndex { get; set; } = -1;

	public bool SnapToGrid { get; set; } = false;
	public bool GridOverlayVisible { get; set; } = false;
	public bool CameraBoundsOverlayVisible { get; set; } = false;
	public bool DisableShadows { get; set; } = false;
	public string SkyboxSelected { get; set; } = "";
	public bool PathingOverlayVisible { get; set; } = false;
	public bool BrushShapeSquare { get; set; } = true;

	public string StatusText { get; set; } = "";
	public string FeedbackText { get; set; } = "";

	public bool HasInspectorSelection { get; set; } = false;
	public string InspectorTitle { get; set; } = "No Selection";
	public string InspectorPos { get; set; } = "Position: (0, 0)";

	public bool ShallowWater { get; set; } = false;
	public bool DeepWater { get; set; } = false;
	public bool Flying { get; set; } = false;
	public bool Ground { get; set; } = true;
	public bool Buildable { get; set; } = false;
	public int PathingModeIndex { get; set; } = 0;

	public void UpdateFromHost()
	{
		if (Realm.Client.Core.GameHost.Instance != null)
		{
			BrushSize = Realm.Client.Core.GameHost.Instance.EditorBrushRadius;
			BrushStrength = Realm.Client.Core.GameHost.Instance.EditorBrushStrength;
			BlockStep = Realm.Client.Core.GameHost.Instance.EditorBlockLevelHeight;
			ExactHeight = Realm.Client.Core.GameHost.Instance.EditorExactHeight;

			PlacementRotate = Realm.Client.Core.GameHost.Instance.EditorPlacementRotation;
			PlacementScale = Realm.Client.Core.GameHost.Instance.EditorPlacementScale;
			PasteRotation = Realm.Client.Core.GameHost.Instance.EditorPasteRotation;
			PasteReflection = Realm.Client.Core.GameHost.Instance.EditorPasteReflection;
			SpawnAsEnemy = Realm.Client.Core.GameHost.Instance.PlaceUnitIsEnemy;
			RandomRotation = Realm.Client.Core.GameHost.Instance.EditorRandomRotation;
			RandomScale = Realm.Client.Core.GameHost.Instance.EditorRandomScale;
			ClumpMode = Realm.Client.Core.GameHost.Instance.EditorClumpMode;
			ClumpCount = Realm.Client.Core.GameHost.Instance.EditorClumpCount;
			ClumpScale = Realm.Client.Core.GameHost.Instance.EditorClumpScale;

			SnapToGrid = Realm.Client.Core.GameHost.Instance.EditorSnapToGrid;
			GridOverlayVisible = Realm.Client.Core.GameHost.Instance.EditorGridVisible;
			CameraBoundsOverlayVisible = Realm.Client.Core.GameHost.Instance.EditorCameraBoundsVisible;
			DisableShadows = Realm.Client.Core.GameHost.Instance.EditorDisableShadows;
			PathingOverlayVisible = Realm.Client.Core.GameHost.Instance.PathingOverlayVisible;
			BrushShapeSquare = Realm.Client.Core.GameHost.Instance.EditorBrushIsSquare;
		}
	}
}