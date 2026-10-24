using System.Collections.Generic;
using Realm.Client.Core;
using Realm.Client.Services;
using PasteReflection = Realm.Ecs.Components.Core.PasteReflection;

namespace Realm.Client.UI.MapEditor;

public class MapEditorHUDViewModel
{
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

	public float ClumpDensity
	{
		get => ClumpCount;
		set => ClumpCount = value;
	}

	public float ClumpScale { get; set; } = 0.3f;

	public float ClumpScaleVar
	{
		get => ClumpScale;
		set => ClumpScale = value;
	}

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
		if (GameContext.Host != null)
		{
			BrushSize = GameContext.Host.EditorBrushRadius;
			BrushStrength = GameContext.Host.EditorBrushStrength;
			BlockStep = GameContext.Host.EditorBlockLevelHeight;
			ExactHeight = GameContext.Host.EditorExactHeight;

			PlacementRotate = GameContext.EditorService.EditorPlacementRotation;
			PlacementScale = GameContext.Host.EditorPlacementScale;
			PasteRotation = GameContext.EditorService.EditorPasteRotation;
			PasteReflection = GameContext.EditorService.EditorPasteReflection;
			SpawnAsEnemy = GameContext.EditorService.PlaceUnitIsEnemy;
			RandomRotation = GameContext.EditorService.EditorRandomRotation;
			RandomScale = GameContext.EditorService.EditorRandomScale;
			ClumpMode = GameContext.Host.EditorClumpMode;
			ClumpCount = GameContext.Host.EditorClumpCount;
			ClumpScale = GameContext.Host.EditorClumpScale;

			SnapToGrid = GameContext.Host.EditorSnapToGrid;
			GridOverlayVisible = GameContext.Host.EditorGridVisible;
			CameraBoundsOverlayVisible = GameContext.EditorService.EditorCameraBoundsVisible;
			DisableShadows = GameContext.EditorService.EditorDisableShadows;
			PathingOverlayVisible = GameContext.Host.PathingOverlayVisible;
			BrushShapeSquare = GameContext.Host.EditorBrushIsSquare;
		}
	}
}